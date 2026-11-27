using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Identity;
using Resgrid.Model.Lms;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Workers.Framework.Logic;

namespace Resgrid.Tests.TrainingT3
{
	/// <summary>An in-memory table behind a mocked repository (identity ids, save, insert, delete).</summary>
	internal sealed class MemoryTable<T> where T : class, IEntity
	{
		public readonly List<T> Rows = new List<T>();
		private int _next = 1;

		public T Save(T entity)
		{
			var id = Convert.ToInt32(entity.IdValue, CultureInfo.InvariantCulture);
			if (id <= 0)
			{
				entity.IdValue = _next++;
				Rows.Add(entity);
				return entity;
			}
			var index = Rows.FindIndex(r => Convert.ToInt32(r.IdValue, CultureInfo.InvariantCulture) == id);
			if (index >= 0)
				Rows[index] = entity;
			else
				Rows.Add(entity);
			return entity;
		}

		public TRepository Bind<TRepository>(Mock<TRepository> mock) where TRepository : class, IRepository<T>
		{
			mock.Setup(r => r.SaveOrUpdateAsync(It.IsAny<T>(), It.IsAny<CancellationToken>(), It.IsAny<bool>())).ReturnsAsync((T e, CancellationToken _, bool __) => Save(e));
			mock.Setup(r => r.InsertAsync(It.IsAny<T>(), It.IsAny<CancellationToken>(), It.IsAny<bool>())).ReturnsAsync((T e, CancellationToken _, bool __) => Save(e));
			mock.Setup(r => r.DeleteAsync(It.IsAny<T>(), It.IsAny<CancellationToken>())).ReturnsAsync((T e, CancellationToken _) => Rows.Remove(e));
			return mock.Object;
		}
	}

	/// <summary>
	/// Training T3 part 2 (round 4C): the services with their stores in memory. The training request approval chain and cost totals, the
	/// scheduled report run on worker 94 (due-work query only, the gate once per department, inactive recipients skipped), the
	/// certification cycle auto-renew and alerts (once, inactive holders skipped), the credential-status audience, the CASE import and
	/// gap report, familiarization credit (idempotent) and the worker's step isolation.
	/// </summary>
	[TestFixture]
	public class LmsProgramServiceTests
	{
		private const int Dept = 7;
		private const string Member = "member-1";
		private const string Captain = "captain-1";
		private const string Officer = "officer-1";
		private const string Admin = "admin-1";
		private const string Gone = "gone-1";

		private static Mock<IDepartmentsService> Departments(params string[] active)
		{
			var departments = new Mock<IDepartmentsService>();
			var set = new HashSet<string>(active, StringComparer.OrdinalIgnoreCase);
			departments.Setup(d => d.GetActiveMemberUserIdsAsync(Dept)).ReturnsAsync(() => new HashSet<string>(set, StringComparer.OrdinalIgnoreCase));
			departments.Setup(d => d.GetDepartmentMemberAsync(It.IsAny<string>(), Dept, It.IsAny<bool>())).ReturnsAsync((string id, int dept, bool _) =>
				new DepartmentMember { UserId = id, DepartmentId = dept, IsDeleted = !set.Contains(id) });
			departments.Setup(d => d.GetActiveAdminsForDepartmentAsync(Dept)).ReturnsAsync(new List<IdentityUser> { new IdentityUser { Id = Admin } });
			departments.Setup(d => d.GetDepartmentByIdAsync(Dept, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = Dept, Name = "Station 7", TimeZone = "UTC" });
			departments.Setup(d => d.GetAllPersonnelNamesForDepartmentAsync(Dept)).ReturnsAsync(active.Select(id => new PersonName { UserId = id, FirstName = id, LastName = "X" }).ToList());
			return departments;
		}

		private static Mock<IAuthorizationService> Officers(params string[] officers)
		{
			var authorization = new Mock<IAuthorizationService>();
			authorization.Setup(a => a.GetTrainingManagementScopeAsync(It.IsAny<string>(), Dept)).ReturnsAsync((string id, int dept) =>
				officers.Contains(id, StringComparer.OrdinalIgnoreCase)
					? new TrainingManagementScope { UserId = id, DepartmentId = dept, Assign = new TrainingRightScope { ManagesAll = true } }
					: TrainingManagementScope.None(id, dept));
			return authorization;
		}

		private static Mock<IDepartmentGroupsService> Groups()
		{
			var groups = new Mock<IDepartmentGroupsService>();
			groups.Setup(g => g.GetGroupForUserAsync(Member, Dept)).ReturnsAsync(new DepartmentGroup { DepartmentGroupId = 5, DepartmentId = Dept });
			groups.Setup(g => g.GetAllAdminsForGroupAndAncestorsAsync(5)).ReturnsAsync(new List<DepartmentGroupMember> { new DepartmentGroupMember { UserId = Captain, IsAdmin = true } });
			return groups;
		}

		private static Mock<ILmsProgramNotifier> Notifier(List<(string Key, List<string> To)> sent)
		{
			var notifier = new Mock<ILmsProgramNotifier>();
			notifier.Setup(n => n.SendAsync(Dept, It.IsAny<IEnumerable<string>>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Func<CultureInfo, object[]>>(), It.IsAny<string>(),
					It.IsAny<CancellationToken>()))
				.ReturnsAsync((int d, IEnumerable<string> to, string title, string key, Func<CultureInfo, object[]> args, string code, CancellationToken _) =>
				{
					var list = to.ToList();
					args?.Invoke(CultureInfo.InvariantCulture);
					sent.Add((key, list));
					return list.Count;
				});
			return notifier;
		}

		private static Mock<ILmsProgramSettingsService> Settings(LmsProgramSettings settings = null)
		{
			var mock = new Mock<ILmsProgramSettingsService>();
			mock.Setup(s => s.GetAsync(It.IsAny<int>())).ReturnsAsync((settings ?? new LmsProgramSettings()).Normalized());
			return mock;
		}

		#region Training requests

		private sealed class RequestWorld
		{
			public readonly MemoryTable<LmsTrainingRequest> Requests = new MemoryTable<LmsTrainingRequest>();
			public readonly MemoryTable<LmsTrainingRequestDecision> Decisions = new MemoryTable<LmsTrainingRequestDecision>();
			public readonly List<(string Key, List<string> To)> Sent = new List<(string, List<string>)>();
			public readonly Mock<ILearningRecordWriter> Writer = new Mock<ILearningRecordWriter>();
			public bool LoseNextRace;
			public LmsTrainingRequestService Service;
			public readonly Mock<IEventAggregator> Events = new Mock<IEventAggregator>();
			public readonly List<Resgrid.Model.Events.TrainingRequestWorkflowEvent> Raised = new List<Resgrid.Model.Events.TrainingRequestWorkflowEvent>();

			public RequestWorld(LmsProgramSettings settings = null)
			{
				Events.Setup(e => e.SendMessage(It.IsAny<Resgrid.Model.Events.TrainingRequestWorkflowEvent>())).Callback((Resgrid.Model.Events.TrainingRequestWorkflowEvent e) => Raised.Add(e));
				var requests = new Mock<ILmsTrainingRequestsRepository>();
				Requests.Bind(requests);
				requests.Setup(r => r.GetByIdForDepartmentAsync(Dept, It.IsAny<int>())).ReturnsAsync((int d, int id) => Copy(Requests.Rows.FirstOrDefault(x => x.LmsTrainingRequestId == id)));
				requests.Setup(r => r.GetOpenAsync(Dept)).ReturnsAsync(() => Requests.Rows.Where(x => x.IsOpen).Select(Copy).ToList());
				requests.Setup(r => r.GetForDepartmentAsync(Dept, It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<int>())).ReturnsAsync(() => Requests.Rows.Select(Copy).ToList());
				requests.Setup(r => r.TryTransitionAsync(It.IsAny<LmsTrainingRequest>(), It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
					.ReturnsAsync((LmsTrainingRequest changed, int status, int? step, CancellationToken _) =>
					{
						var stored = Requests.Rows.Single(x => x.LmsTrainingRequestId == changed.LmsTrainingRequestId);
						if (LoseNextRace || stored.Status != status || stored.CurrentStep != step)
						{
							LoseNextRace = false;
							return false;
						}
						Requests.Save(Copy(changed));
						return true;
					});
				var decisions = new Mock<ILmsTrainingRequestDecisionsRepository>();
				Decisions.Bind(decisions);
				decisions.Setup(r => r.GetForRequestAsync(Dept, It.IsAny<int>())).ReturnsAsync((int d, int id) => Decisions.Rows.Where(x => x.LmsTrainingRequestId == id).ToList());

				var learning = new Mock<ILearningRecordService>();
				learning.Setup(l => l.GetCategoriesAsync(Dept, It.IsAny<bool>())).ReturnsAsync(new List<TrainingCategory> { new TrainingCategory { TrainingCategoryId = 4, DepartmentId = Dept, Name = "EMS" } });
				Writer.Setup(w => w.WriteSourceAsync(Dept, LearningRecordSourceKinds.ExternalSubmission, It.IsAny<string>(), It.IsAny<IReadOnlyCollection<LearningRecordDraft>>(), It.IsAny<string>(),
						It.IsAny<CancellationToken>()))
					.ReturnsAsync(new LearningRecordWriteResult { Written = 1, Records = { new LearningRecord { LearningRecordId = 77, Status = (int)LearningRecordStatuses.Pending } } });
				var files = new Mock<ILearningRecordFilesRepository>();
				files.Setup(f => f.InsertAsync(It.IsAny<LearningRecordFile>(), It.IsAny<CancellationToken>(), It.IsAny<bool>())).ReturnsAsync((LearningRecordFile f, CancellationToken _, bool __) =>
				{
					f.LearningRecordFileId = 31;
					return f;
				});

				Service = new LmsTrainingRequestService(requests.Object, decisions.Object, Settings(settings).Object, learning.Object, Writer.Object, files.Object,
					Officers(Officer).Object, Departments(Member, Captain, Officer, Admin).Object, Groups().Object, Notifier(Sent).Object, Events.Object);
			}

			private static LmsTrainingRequest Copy(LmsTrainingRequest r) => r == null ? null : Newtonsoft.Json.JsonConvert.DeserializeObject<LmsTrainingRequest>(Newtonsoft.Json.JsonConvert.SerializeObject(r));
		}

		private static LmsTrainingRequest Class() => new LmsTrainingRequest
		{
			Title = "Rope rescue technician", Provider = "State academy", Hours = 16m, CategoryId = 4, CostTuition = 400m, CostTravel = 120.456m, StartsOn = new DateTime(2027, 4, 1),
			EndsOn = new DateTime(2027, 4, 2)
		};

		[Test]
		public async Task A_request_goes_supervisor_then_training_officer_and_nobody_decides_their_own()
		{
			var world = new RequestWorld();
			var request = await world.Service.SubmitAsync(Dept, Member, Class());

			request.Status.Should().Be((int)LmsTrainingRequestStatuses.Submitted);
			request.CurrentStep.Should().Be((int)LmsTrainingRequestSteps.Supervisor);
			request.CostTravel.Should().Be(120.46m);
			request.Currency.Should().Be("USD");
			world.Sent.Should().ContainSingle(s => s.Key == "Prg.Notify.RequestWaiting").Which.To.Should().Equal(Captain);

			await FluentActions.Awaiting(() => world.Service.DecideAsync(Dept, request.LmsTrainingRequestId, Member, true, null)).Should()
				.ThrowAsync<InvalidOperationException>().WithMessage("lms_program_not_allowed");
			await FluentActions.Awaiting(() => world.Service.DecideAsync(Dept, request.LmsTrainingRequestId, Officer, true, null)).Should()
				.ThrowAsync<InvalidOperationException>().WithMessage("lms_program_not_allowed", "the officer is not the member's supervisor");

			var afterSupervisor = await world.Service.DecideAsync(Dept, request.LmsTrainingRequestId, Captain, true, "Good course");
			afterSupervisor.Status.Should().Be((int)LmsTrainingRequestStatuses.Submitted);
			afterSupervisor.CurrentStep.Should().Be((int)LmsTrainingRequestSteps.TrainingOfficer);
			world.Sent.Last().To.Should().BeEquivalentTo(new[] { Officer }, "only records managers are told the officer step waits");
			(await world.Service.GetAwaitingDecisionAsync(Dept, Officer)).Should().ContainSingle();
			(await world.Service.GetAwaitingDecisionAsync(Dept, Captain)).Should().BeEmpty();

			var approved = await world.Service.DecideAsync(Dept, request.LmsTrainingRequestId, Officer, true, null);
			approved.Status.Should().Be((int)LmsTrainingRequestStatuses.Approved);
			approved.CurrentStep.Should().BeNull();
			approved.DecidedByUserId.Should().Be(Officer);
			world.Decisions.Rows.Select(d => (LmsTrainingRequestSteps)d.Step).Should().Equal(LmsTrainingRequestSteps.Supervisor, LmsTrainingRequestSteps.TrainingOfficer);
			world.Sent.Last().Key.Should().Be("Prg.Notify.RequestApproved");
			world.Sent.Last().To.Should().Equal(Member);
		}

		[Test]
		public async Task Submitting_and_the_final_decision_each_raise_one_workflow_event_without_the_justification_or_notes()
		{
			// Round 7C, workflow triggers 265-266: a step passed on is not a decision; a lost race raises nothing.
			var world = new RequestWorld();
			var input = Class();
			input.Justification = "Needed for the swift-water team rotation";
			var request = await world.Service.SubmitAsync(Dept, Member, input);

			world.Raised.Should().ContainSingle(e => e.Trigger == WorkflowTriggerEventType.TrainingRequestSubmitted && e.UserId == Member && e.Status == "Submitted" &&
				e.AwaitingStep == "Supervisor" && e.TotalCost == 520.46m && e.RequestTitle == "Rope rescue technician");

			await world.Service.DecideAsync(Dept, request.LmsTrainingRequestId, Captain, true, "Good course, confidential budget note");
			world.Raised.Should().HaveCount(1, "the supervisor passed it on; nothing is decided yet");

			world.LoseNextRace = true;
			await FluentActions.Awaiting(() => world.Service.DecideAsync(Dept, request.LmsTrainingRequestId, Officer, true, null)).Should().ThrowAsync<InvalidOperationException>();
			world.Raised.Should().HaveCount(1);

			await world.Service.DecideAsync(Dept, request.LmsTrainingRequestId, Officer, true, null);
			var decided = world.Raised.Last();
			decided.Trigger.Should().Be(WorkflowTriggerEventType.TrainingRequestDecided);
			(decided.Status, decided.DecidedByUserId, decided.AwaitingStep).Should().Be(("Approved", Officer, (string)null));
			world.Raised.Should().HaveCount(2);
			var json = Newtonsoft.Json.JsonConvert.SerializeObject(world.Raised);
			json.Should().NotContain("swift-water").And.NotContain("confidential");
		}

		[Test]
		public async Task A_rejection_needs_a_reason_and_ends_the_request()
		{
			var world = new RequestWorld();
			var request = await world.Service.SubmitAsync(Dept, Member, Class());

			await FluentActions.Awaiting(() => world.Service.DecideAsync(Dept, request.LmsTrainingRequestId, Admin, false, " ")).Should()
				.ThrowAsync<InvalidOperationException>().WithMessage("lms_program_reason_required");
			var rejected = await world.Service.DecideAsync(Dept, request.LmsTrainingRequestId, Admin, false, "No budget left");
			rejected.Status.Should().Be((int)LmsTrainingRequestStatuses.Rejected);
			await FluentActions.Awaiting(() => world.Service.DecideAsync(Dept, request.LmsTrainingRequestId, Officer, true, null)).Should()
				.ThrowAsync<InvalidOperationException>().WithMessage("lms_program_request_not_open");
		}

		[Test]
		public async Task Two_approvers_deciding_at_once_cannot_both_win()
		{
			var world = new RequestWorld();
			var request = await world.Service.SubmitAsync(Dept, Member, Class());
			world.LoseNextRace = true;

			await FluentActions.Awaiting(() => world.Service.DecideAsync(Dept, request.LmsTrainingRequestId, Captain, true, null)).Should()
				.ThrowAsync<InvalidOperationException>().WithMessage("lms_program_request_changed");
			world.Decisions.Rows.Should().BeEmpty("the decision is recorded only by the approver whose transition won");
		}

		[Test]
		public async Task A_taken_class_becomes_the_expected_external_submission_on_the_transcript()
		{
			var world = new RequestWorld(new LmsProgramSettings { RequestSupervisorApproval = false, RequestTrainingOfficerApproval = false });
			var request = await world.Service.SubmitAsync(Dept, Member, Class());
			request.Status.Should().Be((int)LmsTrainingRequestStatuses.Approved, "no approval step is configured");
			world.Sent.Should().BeEmpty();

			await FluentActions.Awaiting(() => world.Service.CompleteAsync(Dept, request.LmsTrainingRequestId, Captain, new DateTime(2027, 4, 2), 16m, null, null, null, null))
				.Should().ThrowAsync<InvalidOperationException>().WithMessage("lms_program_not_allowed");

			var done = await world.Service.CompleteAsync(Dept, request.LmsTrainingRequestId, Member, DateTime.UtcNow.Date.AddDays(-1), 15.5m, 505m, "cert.pdf", "application/pdf", new byte[] { 1, 2, 3 });

			done.Status.Should().Be((int)LmsTrainingRequestStatuses.Completed);
			done.LearningRecordId.Should().Be(77);
			done.ActualCost.Should().Be(505m);
			world.Writer.Verify(w => w.WriteSourceAsync(Dept, LearningRecordSourceKinds.ExternalSubmission, "treq:" + request.LmsTrainingRequestId,
				It.Is<IReadOnlyCollection<LearningRecordDraft>>(d => d.Single().Status == LearningRecordStatuses.Pending && d.Single().Hours == 15.5m && d.Single().CategoryId == 4 &&
					d.Single().EvidenceFileId == 31 && d.Single().UserId == Member), Member, It.IsAny<CancellationToken>()), Times.Once);

			var costs = await world.Service.GetCostSummaryAsync(Dept, null, null, LmsTrainingRequestCostGroupings.Member);
			costs.Committed.Should().Be(505m);
			costs.Currency.Should().Be("USD");
		}

		#endregion Training requests

		#region Scheduled reports (worker 94)

		private sealed class FakeSource : ILmsReportSource
		{
			public int Builds;
			public LmsReportDescriptor Descriptor { get; } = new LmsReportDescriptor { Key = "fake", NameKey = "Prg.Report.Hours", DescriptionKey = "Prg.Report.HoursHelp", Tier = LmsReportTiers.Lms };

			public Task<LmsReportTable> BuildAsync(LmsReportRequest request, Func<string, string> text)
			{
				Builds++;
				return Task.FromResult(new LmsReportTable { Title = text(Descriptor.NameKey), Columns = { "A" }, Rows = { new List<string> { request.DepartmentId.ToString(CultureInfo.InvariantCulture) } } });
			}
		}

		[Test]
		public async Task Due_reports_are_claimed_and_sent_to_active_recipients_asking_the_gate_once_per_department()
		{
			var now = new DateTime(2027, 5, 17, 8, 0, 0, DateTimeKind.Utc);
			LmsReportDefinition Due(int id, int dept, string recipients) => new LmsReportDefinition
			{
				LmsReportDefinitionId = id, DepartmentId = dept, ReportKey = "fake", Name = "Monthly ISO", Frequency = (int)LmsReportFrequencies.Daily, HourLocal = 8,
				NextRunUtc = now.AddMinutes(-5), CreatedByUserId = Admin, RecipientUserIds = recipients, Format = (int)LmsReportFormats.Csv
			};
			var definitions = new Mock<ILmsReportDefinitionsRepository>();
			definitions.Setup(d => d.GetDueAsync(now, It.IsAny<int>())).ReturnsAsync(new List<LmsReportDefinition>
			{
				Due(1, Dept, Admin + "," + Officer + "," + Gone), Due(2, Dept, Officer), Due(3, 99, Admin)
			});
			var claimed = new List<int>();
			definitions.Setup(d => d.TryClaimRunAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<DateTime?>(), now, It.IsAny<CancellationToken>()))
				.ReturnsAsync((int id, DateTime expected, DateTime? next, DateTime ran, CancellationToken _) =>
				{
					next.Should().Be(new DateTime(2027, 5, 18, 8, 0, 0), "the next run is tomorrow at the same local hour");
					if (claimed.Contains(id) || id == 2) return false; // 2: another worker claimed it first
					claimed.Add(id);
					return true;
				});
			var runs = new MemoryTable<LmsReportRun>();
			var runRepository = new Mock<ILmsReportRunsRepository>();
			runs.Bind(runRepository);

			var departments = Departments(Admin, Officer);
			departments.Setup(d => d.GetDepartmentByIdAsync(99, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = 99, TimeZone = "UTC" });
			var access = new Mock<ILmsAccessService>();
			access.Setup(a => a.CanAuthorAndManageAsync(Dept)).ReturnsAsync(true);
			access.Setup(a => a.CanAuthorAndManageAsync(99)).ReturnsAsync(false);
			var users = new Mock<IUsersService>();
			users.Setup(u => u.GetUserById(It.IsAny<string>(), It.IsAny<bool>())).Returns((string id, bool _) => new IdentityUser { Id = id, Email = id + "@example.org" });
			var sentTo = new List<string>();
			var email = new Mock<IEmailService>();
			email.Setup(e => e.SendReportDeliveryAsync(It.IsAny<EmailNotification>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()))
				.ReturnsAsync((EmailNotification n, int d, string url, string name) =>
				{
					sentTo.Add(n.To);
					n.AttachmentName.Should().EndWith(".csv");
					n.AttachmentData.Take(3).Should().Equal(new byte[] { 0xEF, 0xBB, 0xBF });
					return true;
				});
			var source = new FakeSource();

			var service = new LmsReportCatalogService(new[] { source }, Mock.Of<ILmsReportFoldersRepository>(), definitions.Object, runRepository.Object, departments.Object,
				Officers(Officer).Object, users.Object, Mock.Of<IUserProfileService>(), email.Object, Mock.Of<IPdfProvider>(), access.Object, Mock.Of<IEventAggregator>());

			var handled = await service.RunDueAsync(now);

			handled.Should().Be(2, "report 2 was claimed elsewhere");
			definitions.Verify(d => d.GetDueAsync(now, It.IsAny<int>()), Times.Once, "one due-work query");
			access.Verify(a => a.CanAuthorAndManageAsync(Dept), Times.Once, "the gate once per department, not per report");
			access.Verify(a => a.CanAuthorAndManageAsync(99), Times.Once);
			access.Verify(a => a.CanAuthorAndManageAsync(It.Is<int>(d => d != Dept && d != 99)), Times.Never);
			sentTo.Should().BeEquivalentTo(new[] { Admin + "@example.org", Officer + "@example.org" }, "the member who left is skipped");
			source.Builds.Should().Be(1, "one build per language");
			runs.Rows.Should().HaveCount(2);
			runs.Rows.Single(r => r.LmsReportDefinitionId == 1).Status.Should().Be((int)LmsReportRunStatuses.Sent);
			runs.Rows.Single(r => r.LmsReportDefinitionId == 1).Recipients.Should().Be(2);
			runs.Rows.Single(r => r.LmsReportDefinitionId == 3).Status.Should().Be((int)LmsReportRunStatuses.Skipped, "no add-on: nothing is sent, the schedule moves on");
		}

		#endregion Scheduled reports (worker 94)

		#region Certification cycles

		private sealed class CycleWorld
		{
			public readonly HashSet<string> Claims = new HashSet<string>();
			public readonly List<(string Key, List<string> To)> Sent = new List<(string, List<string>)>();
			public readonly Mock<ICertificationService> Certifications = new Mock<ICertificationService>();
			public readonly Mock<ILmsAccessService> Access = new Mock<ILmsAccessService>();
			public readonly Mock<ILmsProgramStatesRepository> States = new Mock<ILmsProgramStatesRepository>();
			public readonly Mock<ILmsCertificationIssuanceService> Ledger = new Mock<ILmsCertificationIssuanceService>();
			public readonly List<LmsCertificationIssuance> Ledgered = new List<LmsCertificationIssuance>();
			public readonly List<PersonnelCertification> Records;
			public LmsCertificationCycleService Service;

			public CycleWorld()
			{
				var type = new DepartmentCertificationType { DepartmentCertificationTypeId = 3, DepartmentId = Dept, Type = "EMT", RenewalCreditHoursRequired = 40m, DefaultValidityMonths = 24 };
				Records = new List<PersonnelCertification>
				{
					new PersonnelCertification { PersonnelCertificationId = 1, DepartmentId = Dept, UserId = Member, DepartmentCertificationTypeId = 3, RecievedOn = new DateTime(2025, 6, 1), ExpiresOn = new DateTime(2027, 5, 31) },
					new PersonnelCertification { PersonnelCertificationId = 2, DepartmentId = Dept, UserId = Gone, DepartmentCertificationTypeId = 3, RecievedOn = new DateTime(2025, 6, 1), ExpiresOn = new DateTime(2027, 5, 31) }
				};
				var rules = new Mock<ILmsCertificationCycleRulesRepository>();
				rules.Setup(r => r.GetForDepartmentAsync(Dept)).ReturnsAsync(new List<LmsCertificationCycleRule>
				{
					new LmsCertificationCycleRule
					{
						LmsCertificationCycleRuleId = 1, DepartmentId = Dept, DepartmentCertificationTypeId = 3, AutoRenewMode = (int)LmsAutoRenewModes.OnCompletion, AlertOnCompletion = true,
						AlertOnRenewal = true, AlertLeadDaysCsv = "30", NotifySupervisors = true, NamedRecipientUserIds = Officer + "," + Member, AlertsFrom = new DateTime(2027, 1, 1)
					}
				});
				rules.Setup(r => r.GetDepartmentIdsAsync()).ReturnsAsync(new List<int> { Dept });
				Certifications.Setup(c => c.GetAllCertificationTypesByDepartmentAsync(Dept)).ReturnsAsync(new List<DepartmentCertificationType> { type });
				Certifications.Setup(c => c.GetCertificationsForDepartmentAsync(Dept, null)).ReturnsAsync(() => Records.ToList());
				Certifications.Setup(c => c.GetCertificationCreditTotalsAsync(It.IsAny<IEnumerable<int>>())).ReturnsAsync(new Dictionary<int, decimal> { { 1, 41m }, { 2, 50m } });
				Certifications.Setup(c => c.SaveCertificationAsync(It.IsAny<PersonnelCertification>(), It.IsAny<CancellationToken>())).ReturnsAsync((PersonnelCertification p, CancellationToken _) =>
				{
					p.PersonnelCertificationId = 100 + Records.Count;
					Records.Add(p);
					return p;
				});
				var alerts = new Mock<ILmsCertificationAlertsRepository>();
				alerts.Setup(a => a.TryClaimAsync(It.IsAny<LmsCertificationAlert>(), It.IsAny<CancellationToken>()))
					.ReturnsAsync((LmsCertificationAlert a, CancellationToken _) => Claims.Add(a.PersonnelCertificationId + ":" + a.Kind + ":" + a.AlertKey));
				var day = default(DateTime?);
				States.Setup(s => s.TryClaimCycleSweepAsync(Dept, It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync((int d, DateTime local, CancellationToken _) =>
				{
					if (day == local.Date) return false;
					day = local.Date;
					return true;
				});
				Access.Setup(a => a.CanAuthorAndManageAsync(Dept)).ReturnsAsync(true);
				var groups = Groups();
				// Round 9A: every next cycle is ledgered (the issuance engine records it).
				Ledger.Setup(l => l.RecordAsync(It.IsAny<LmsCertificationIssuance>(), It.IsAny<CancellationToken>()))
					.ReturnsAsync((LmsCertificationIssuance i, CancellationToken _) => { Ledgered.Add(i); return i; });
				Service = new LmsCertificationCycleService(rules.Object, alerts.Object, States.Object, Certifications.Object, Departments(Member, Captain, Officer, Admin).Object,
					groups.Object, Access.Object, Notifier(Sent).Object, Mock.Of<IEventAggregator>(), Ledger.Object);
			}
		}

		[Test]
		public async Task A_completed_cycle_renews_once_and_alerts_supervisors_and_named_members_never_the_holder()
		{
			var world = new CycleWorld();

			var first = await world.Service.RunDepartmentAsync(Dept, new DateTime(2027, 3, 1));

			first.Renewed.Should().Be(1, "the member who left is never renewed");
			world.Certifications.Verify(c => c.SaveCertificationAsync(It.Is<PersonnelCertification>(p => p.UserId == Member && p.DepartmentCertificationTypeId == 3 &&
				p.RecievedOn == new DateTime(2027, 6, 1) && p.ExpiresOn == new DateTime(2029, 5, 31) && p.Number == null), It.IsAny<CancellationToken>()), Times.Once);
			world.Sent.Should().ContainSingle(s => s.Key == "Prg.Notify.CertCompleted").Which.To.Should().BeEquivalentTo(new[] { Captain, Officer }, "the holder is never their own alert's recipient");
			world.Sent.Should().NotContain(s => s.Key == "Prg.Notify.CertRenewed", "the next cycle has not started yet");

			var again = await world.Service.RunDepartmentAsync(Dept, new DateTime(2027, 3, 1));
			again.Renewed.Should().Be(0);
			again.Alerts.Should().Be(0);
			world.Certifications.Verify(c => c.SaveCertificationAsync(It.IsAny<PersonnelCertification>(), It.IsAny<CancellationToken>()), Times.Once);

			// The new cycle starts: the renewal alert goes out once, and the old cycle's expiry notices stay quiet (it is superseded).
			var started = await world.Service.RunDepartmentAsync(Dept, new DateTime(2027, 6, 2));
			started.Alerts.Should().Be(1);
			world.Sent.Count(s => s.Key == "Prg.Notify.CertRenewed").Should().Be(1);
			world.Sent.Should().NotContain(s => s.Key == "Prg.Notify.CertExpired" || s.Key == "Prg.Notify.CertExpiring");
		}

		[Test]
		public async Task The_cycle_sweep_claims_the_local_day_and_asks_the_gate_once()
		{
			var world = new CycleWorld();
			var now = new DateTime(2027, 3, 1, 10, 0, 0, DateTimeKind.Utc);

			(await world.Service.RunDueAsync(now)).Departments.Should().Be(1);
			(await world.Service.RunDueAsync(now.AddHours(1))).Departments.Should().Be(0, "the day was claimed");
			world.Access.Verify(a => a.CanAuthorAndManageAsync(Dept), Times.Once);

			world.Access.Setup(a => a.CanAuthorAndManageAsync(Dept)).ReturnsAsync(false);
			var lapsed = await world.Service.RunDueAsync(now.AddDays(1));
			lapsed.Skipped.Should().Be(1);
			lapsed.Renewed.Should().Be(0, "nothing renews while the add-on is inactive");
		}

		#endregion Certification cycles

		#region Credential-status audience

		[Test]
		public async Task The_credential_audience_returns_active_members_only()
		{
			var certifications = new Mock<ICertificationService>();
			certifications.Setup(c => c.GetCertificationTypeByIdAsync(3)).ReturnsAsync(new DepartmentCertificationType { DepartmentCertificationTypeId = 3, DepartmentId = Dept, Type = "CPR" });
			certifications.Setup(c => c.GetCertificationsForDepartmentAsync(Dept, null)).ReturnsAsync(new List<PersonnelCertification>
			{
				new PersonnelCertification { PersonnelCertificationId = 1, UserId = Member, DepartmentCertificationTypeId = 3, ExpiresOn = new DateTime(2027, 1, 1), Status = (int)PersonnelCertificationStatuses.Expired },
				new PersonnelCertification { PersonnelCertificationId = 2, UserId = Gone, DepartmentCertificationTypeId = 3, ExpiresOn = new DateTime(2027, 1, 1), Status = (int)PersonnelCertificationStatuses.Expired }
			});
			var service = new CredentialStatusAudienceService(certifications.Object, Departments(Member, Captain).Object, Mock.Of<ILmsAccessService>());

			(await service.ResolveAsync(Dept, 3, CredentialAudienceStatuses.Expired, 30, new DateTime(2027, 3, 1))).Should().Equal(Member);
			(await service.ResolveAsync(Dept, 3, CredentialAudienceStatuses.Missing, 30, new DateTime(2027, 3, 1))).Should().Equal(Captain);
			(await service.ResolveAsync(Dept, 4, CredentialAudienceStatuses.Missing, 30, new DateTime(2027, 3, 1))).Should().BeEmpty("a type of another department or unknown answers nobody");
		}

		#endregion Credential-status audience

		#region Competency frameworks

		private sealed class CompetencyWorld
		{
			public readonly MemoryTable<LmsCompetencyFramework> Frameworks = new MemoryTable<LmsCompetencyFramework>();
			public readonly MemoryTable<LmsCompetencyItem> Items = new MemoryTable<LmsCompetencyItem>();
			public readonly List<LmsCompetencyAlignment> Alignments = new List<LmsCompetencyAlignment>();
			public readonly Mock<ILearningRecordService> Learning = new Mock<ILearningRecordService>();
			public LmsCompetencyService Service;

			public CompetencyWorld()
			{
				var frameworks = new Mock<ILmsCompetencyFrameworksRepository>();
				Frameworks.Bind(frameworks);
				frameworks.Setup(f => f.GetForDepartmentAsync(Dept, It.IsAny<bool>())).ReturnsAsync(() => Frameworks.Rows.Where(f => !f.IsDeleted).ToList());
				frameworks.Setup(f => f.GetByIdForDepartmentAsync(Dept, It.IsAny<int>())).ReturnsAsync((int d, int id) => Frameworks.Rows.FirstOrDefault(f => f.LmsCompetencyFrameworkId == id));
				frameworks.Setup(f => f.GetByCaseDocumentIdAsync(Dept, It.IsAny<string>())).ReturnsAsync((int d, string id) => Frameworks.Rows.FirstOrDefault(f => f.CaseDocumentId == id && !f.IsDeleted));
				var items = new Mock<ILmsCompetencyItemsRepository>();
				Items.Bind(items);
				items.Setup(i => i.GetForFrameworkAsync(Dept, It.IsAny<int>(), It.IsAny<bool>())).ReturnsAsync((int d, int id, bool deleted) =>
					Items.Rows.Where(i => i.LmsCompetencyFrameworkId == id && (deleted || !i.IsDeleted)).ToList());
				items.Setup(i => i.GetByIdForDepartmentAsync(Dept, It.IsAny<int>())).ReturnsAsync((int d, int id) => Items.Rows.FirstOrDefault(i => i.LmsCompetencyItemId == id));
				items.Setup(i => i.SetParentAsync(Dept, It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
					.Callback((int d, int id, int? parent, CancellationToken _) => Items.Rows.Single(i => i.LmsCompetencyItemId == id).ParentItemId = parent).Returns(Task.CompletedTask);
				var alignments = new Mock<ILmsCompetencyAlignmentsRepository>();
				alignments.Setup(a => a.AddAsync(It.IsAny<LmsCompetencyAlignment>(), It.IsAny<CancellationToken>())).ReturnsAsync((LmsCompetencyAlignment a, CancellationToken _) =>
				{
					if (Alignments.Any(x => x.LmsCompetencyItemId == a.LmsCompetencyItemId && x.SubjectKind == a.SubjectKind && x.SubjectId == a.SubjectId)) return false;
					a.LmsCompetencyAlignmentId = Alignments.Count + 1;
					Alignments.Add(a);
					return true;
				});
				alignments.Setup(a => a.GetForItemsAsync(Dept, It.IsAny<IEnumerable<int>>())).ReturnsAsync((int d, IEnumerable<int> ids) => Alignments.Where(a => ids.Contains(a.LmsCompetencyItemId)).ToList());
				alignments.Setup(a => a.GetForSubjectAsync(Dept, It.IsAny<int>(), It.IsAny<string>())).ReturnsAsync((int d, int kind, string id) =>
					Alignments.Where(a => a.SubjectKind == kind && a.SubjectId == id).ToList());

				Learning.Setup(l => l.GetCategoriesAsync(Dept, It.IsAny<bool>())).ReturnsAsync(new List<TrainingCategory> { new TrainingCategory { TrainingCategoryId = 9, DepartmentId = Dept, Name = "Ladders" } });
				var trainings = new Mock<ITrainingService>();
				trainings.Setup(t => t.GetTrainingByIdAsync(3)).ReturnsAsync(new Training { TrainingId = 3, DepartmentId = Dept, Name = "Ladder drill" });

				Service = new LmsCompetencyService(frameworks.Object, items.Object, alignments.Object, Learning.Object, Mock.Of<ICertificationService>(), Mock.Of<ILmsCoursesRepository>(),
					Mock.Of<ILmsEnrollmentsRepository>(), Departments(Member).Object, trainings.Object, Mock.Of<IEventAggregator>(), Mock.Of<ILmsSkillCompetencySubjects>());
			}
		}

		[Test]
		public async Task A_CASE_import_builds_the_tree_and_a_reimport_updates_it_in_place_keeping_tags()
		{
			var world = new CompetencyWorld();

			var first = await world.Service.ImportCaseAsync(Dept, LmsProgramRulesTests.CaseBytes(), Member);

			first.Error.Should().BeNull();
			first.ItemsAdded.Should().Be(3);
			first.Updated.Should().BeFalse();
			var level = world.Items.Rows.Single(i => i.Identifier == "i-1");
			world.Items.Rows.Where(i => i.Identifier != "i-1").Should().OnlyContain(i => i.ParentItemId == level.LmsCompetencyItemId);
			first.Framework.Source.Should().Be((int)LmsCompetencyFrameworkSources.CaseImport);

			var ladder = world.Items.Rows.Single(i => i.Identifier == "i-3");
			await world.Service.TagAsync(Dept, ladder.LmsCompetencyItemId, LmsCompetencySubjectKinds.Training, "3", Officer);
			await FluentActions.Awaiting(() => world.Service.TagAsync(Dept, ladder.LmsCompetencyItemId, LmsCompetencySubjectKinds.SkillSheet, "1", Officer)).Should()
				.ThrowAsync<InvalidOperationException>().WithMessage("lms_program_subject_invalid", "skill sheets arrive with T4");

			var changed = System.Text.Encoding.UTF8.GetString(LmsProgramRulesTests.CaseBytes()).Replace("Raise a ground ladder", "Raise and climb a ground ladder")
				.Replace(@"{ ""identifier"": ""i-2"", ""humanCodingScheme"": ""FF1.1"", ""fullStatement"": ""Don and doff protective equipment"", ""CFItemType"": ""Competency"", ""listEnumeration"": ""1"" },", string.Empty);
			var second = await world.Service.ImportCaseAsync(Dept, System.Text.Encoding.UTF8.GetBytes(changed), Member);

			second.Updated.Should().BeTrue();
			second.Framework.LmsCompetencyFrameworkId.Should().Be(first.Framework.LmsCompetencyFrameworkId);
			second.ItemsAdded.Should().Be(0);
			second.ItemsUpdated.Should().Be(1);
			second.ItemsRemoved.Should().Be(1);
			world.Items.Rows.Single(i => i.Identifier == "i-3").LmsCompetencyItemId.Should().Be(ladder.LmsCompetencyItemId, "the item keeps its id, so its tags stay");
			world.Alignments.Should().ContainSingle(a => a.LmsCompetencyItemId == ladder.LmsCompetencyItemId);
			world.Frameworks.Rows.Should().HaveCount(1);
		}

		[Test]
		public async Task The_gap_report_reads_the_members_transcript_for_tagged_trainings_and_categories()
		{
			var world = new CompetencyWorld();
			var framework = await world.Service.ApplyCatalogAsync(Dept, LmsCompetencyFrameworkCatalog.NfpaFirefighterKey, Officer);
			(await world.Service.ApplyCatalogAsync(Dept, LmsCompetencyFrameworkCatalog.NfpaFirefighterKey, Officer)).LmsCompetencyFrameworkId.Should().Be(framework.LmsCompetencyFrameworkId, "applied once");
			var items = await world.Service.GetItemsAsync(Dept, framework.LmsCompetencyFrameworkId);
			var ff1 = items.Single(i => i.Identifier == "ff1");
			var jpr = await world.Service.SaveItemAsync(new LmsCompetencyItem { DepartmentId = Dept, LmsCompetencyFrameworkId = framework.LmsCompetencyFrameworkId, ParentItemId = ff1.LmsCompetencyItemId, HumanCodingScheme = "4.3.6", Label = "Ladders" }, Officer);
			await world.Service.TagAsync(Dept, jpr.LmsCompetencyItemId, LmsCompetencySubjectKinds.Training, "3", Officer);
			await world.Service.TagAsync(Dept, ff1.LmsCompetencyItemId, LmsCompetencySubjectKinds.Category, "9", Officer);

			world.Learning.Setup(l => l.GetTranscriptAsync(Dept, Member, null, null, null, null, false)).ReturnsAsync(new List<LearningRecord>
			{
				new LearningRecord { UserId = Member, Status = (int)LearningRecordStatuses.Verified, SourceKind = (int)LearningRecordSourceKinds.CourseCompletion, SourceRefId = "training:3:2", CompletedOn = new DateTime(2027, 1, 10), Title = "Ladder drill" },
				new LearningRecord { UserId = Member, Status = (int)LearningRecordStatuses.Pending, CategoryId = 9, CompletedOn = new DateTime(2027, 2, 1), Title = "Pending, not evidence" }
			});

			var report = await world.Service.GetMemberGapsAsync(Dept, framework.LmsCompetencyFrameworkId, Member, new DateTime(2027, 3, 1));

			report.Rows.Single(r => r.LmsCompetencyItemId == jpr.LmsCompetencyItemId).State.Should().Be(LmsCompetencyEvidenceStates.Current);
			report.Rows.Single(r => r.LmsCompetencyItemId == jpr.LmsCompetencyItemId).AgeDays.Should().Be(50);
			report.Rows.Single(r => r.LmsCompetencyItemId == ff1.LmsCompetencyItemId).State.Should().Be(LmsCompetencyEvidenceStates.None, "a pending record is not evidence");
			report.Rows.Count(r => r.State == LmsCompetencyEvidenceStates.NotTagged).Should().Be(items.Count - 1);
		}

		#endregion Competency frameworks

		#region Familiarization credit

		private sealed class IdempotentWriter : ILearningRecordWriter
		{
			public readonly Dictionary<string, string> Live = new Dictionary<string, string>();
			public readonly List<string> Voided = new List<string>();

			public Task<LearningRecordWriteResult> WriteSourceAsync(int departmentId, LearningRecordSourceKinds kind, string sourceRefId, IReadOnlyCollection<LearningRecordDraft> drafts, string actingUserId,
				CancellationToken cancellationToken = default)
			{
				kind.Should().Be(LearningRecordSourceKinds.BuildingFamiliarization);
				var fingerprint = Newtonsoft.Json.JsonConvert.SerializeObject(drafts);
				var result = new LearningRecordWriteResult();
				if (Live.TryGetValue(sourceRefId, out var stored) && stored == fingerprint)
					result.Unchanged = drafts.Count;
				else
				{
					Live[sourceRefId] = fingerprint;
					result.Written = drafts.Count;
				}
				return Task.FromResult(result);
			}

			public Task<LearningRecordWriteResult> VoidSourceAsync(int departmentId, LearningRecordSourceKinds kind, string sourceRefId, string reason, string actingUserId,
				CancellationToken cancellationToken = default)
			{
				var voided = Live.Remove(sourceRefId) ? 1 : 0;
				Voided.Add(sourceRefId);
				return Task.FromResult(new LearningRecordWriteResult { Voided = voided });
			}

			public Task<LearningRecordWriteResult> WriteCourseCompletionAsync(int departmentId, int trainingId, string userId, bool isBackfill = false, CancellationToken cancellationToken = default) => throw new NotSupportedException();
			public Task<LearningRecordWriteResult> WriteTrainingLogAsync(Log log, string actingUserId, bool isBackfill = false, CancellationToken cancellationToken = default) => throw new NotSupportedException();
			public Task<LearningRecordWriteResult> RemoveTrainingLogAsync(int departmentId, int logId, string actingUserId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
			public Task<LearningRecordWriteResult> WriteRecordsTrainingAsync(int departmentId, string rmsRecordId, string actingUserId, bool isBackfill = false, CancellationToken cancellationToken = default) => throw new NotSupportedException();
			public Task<LearningRecordWriteResult> WriteCalendarCheckInAsync(CalendarItemCheckIn checkIn, string actingUserId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
			public Task<LearningRecordWriteResult> RemoveCalendarCheckInAsync(CalendarItemCheckIn checkIn, string actingUserId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
			public Task<LearningRecordWriteResult> WriteManualCreditAsync(PersonnelCertificationCredit credit, string userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
			public Task<LearningRecordWriteResult> RemoveManualCreditAsync(PersonnelCertificationCredit credit, string actingUserId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
			public Task<int> ApplyCreditsAsync(LearningRecord record, bool includeBackfill, string actingUserId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
			public Task<int> RemoveCreditsAsync(LearningRecord record, string actingUserId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
			public Task NotifyChangedAsync(int departmentId, IReadOnlyCollection<LearningRecord> written, IReadOnlyCollection<LearningRecord> voided, string actingUserId,
				CancellationToken cancellationToken = default) => Task.CompletedTask;
		}

		[Test]
		public async Task Familiarization_credit_is_written_once_per_visit_voided_when_withdrawn_and_never_for_a_member_who_left()
		{
			var since = new DateTime(2027, 3, 1, 0, 0, 0, DateTimeKind.Utc);
			var until = since.AddDays(1);
			var completed = new DateTime(2027, 3, 1, 15, 30, 0);
			var sources = new Mock<ILmsFamiliarizationSourceRepository>();
			sources.Setup(s => s.GetInspectionsChangedAsync(Dept, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int>())).ReturnsAsync(new List<LmsFamiliarizationVisit>
			{
				new LmsFamiliarizationVisit { Kind = "Inspection", SourceRefId = "insp:A", UserId = Member, StartedOn = completed.AddMinutes(-90), CompletedOn = completed, Reference = "INSP-2027-0001", ModifiedOn = completed },
				new LmsFamiliarizationVisit { Kind = "Inspection", SourceRefId = "insp:B", UserId = Member, CompletedOn = completed, Withdrawn = true, ModifiedOn = completed }
			});
			sources.Setup(s => s.GetOccupancyReviewsAsync(Dept, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int>())).ReturnsAsync(new List<LmsFamiliarizationVisit>
			{
				new LmsFamiliarizationVisit { Kind = "OccupancyReview", SourceRefId = "occ-review:O:20270301", UserId = Gone, CompletedOn = completed, ModifiedOn = completed }
			});
			sources.Setup(s => s.GetPreplanReviewsAsync(Dept, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int>())).ReturnsAsync(new List<LmsFamiliarizationVisit>
			{
				new LmsFamiliarizationVisit { Kind = "PreplanReview", SourceRefId = "preplan-review:P:20270301", UserId = Captain, CompletedOn = completed, ModifiedOn = completed }
			});
			var states = new Mock<ILmsProgramStatesRepository>();
			var writer = new IdempotentWriter();
			var settings = new LmsProgramSettings { FamiliarizationEnabled = true, FamiliarizationCategoryId = 12, FamiliarizationMinutes = 45 };
			var service = new LmsFamiliarizationCreditService(sources.Object, states.Object, Settings(settings).Object, writer, Departments(Member, Captain).Object, Mock.Of<ILmsAccessService>());

			var first = await service.CreditAsync(Dept, settings, since, until);

			first.Written.Should().Be(2, "the inspector and the pre-plan reviewer; the member who left earns nothing new");
			writer.Voided.Should().Equal("insp:B");
			var inspection = Newtonsoft.Json.JsonConvert.DeserializeObject<List<LearningRecordDraft>>(writer.Live["insp:A"]).Single();
			inspection.Hours.Should().Be(1.5m, "the visit's own 90 minutes");
			inspection.CategoryId.Should().Be(12);
			inspection.Status.Should().Be(LearningRecordStatuses.Verified);
			Newtonsoft.Json.JsonConvert.DeserializeObject<List<LearningRecordDraft>>(writer.Live["preplan-review:P:20270301"]).Single().Hours.Should().Be(0.75m, "the configured minutes");
			states.Verify(s => s.SetFamiliarizationScannedThroughAsync(Dept, until, It.IsAny<CancellationToken>()), Times.Once);

			var second = await service.CreditAsync(Dept, settings, since, until);
			second.Written.Should().Be(0, "re-reading the same visits is idempotent");
			second.Unchanged.Should().Be(2);
		}

		[Test]
		public async Task Familiarization_credit_reads_only_departments_with_settings_that_turned_it_on_and_asks_the_gate_for_them()
		{
			var states = new Mock<ILmsProgramStatesRepository>();
			states.Setup(s => s.GetDepartmentsWithProgramSettingsAsync()).ReturnsAsync(new List<int> { Dept, 8 });
			var settings = new Mock<ILmsProgramSettingsService>();
			settings.Setup(s => s.GetAsync(Dept)).ReturnsAsync(new LmsProgramSettings { FamiliarizationEnabled = true, FamiliarizationCategoryId = 3 }.Normalized());
			settings.Setup(s => s.GetAsync(8)).ReturnsAsync(new LmsProgramSettings().Normalized());
			var access = new Mock<ILmsAccessService>();
			access.Setup(a => a.CanAuthorAndManageAsync(Dept)).ReturnsAsync(false);
			var service = new LmsFamiliarizationCreditService(Mock.Of<ILmsFamiliarizationSourceRepository>(), states.Object, settings.Object, new IdempotentWriter(),
				Departments(Member).Object, access.Object);

			var result = await service.RunDueAsync(DateTime.UtcNow);

			result.Departments.Should().Be(0);
			access.Verify(a => a.CanAuthorAndManageAsync(Dept), Times.Once);
			access.Verify(a => a.CanAuthorAndManageAsync(8), Times.Never, "a department that did not turn it on is never asked about");
		}

		#endregion Familiarization credit

		#region Worker 94

		[Test]
		public async Task One_failing_worker_step_does_not_stop_the_others()
		{
			var reports = new Mock<ILmsReportCatalogService>();
			reports.Setup(r => r.RunDueAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("boom"));
			var cycles = new Mock<ILmsCertificationCycleService>();
			cycles.Setup(c => c.RunDueAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(new LmsCertificationCycleRunResult { Renewed = 2, Alerts = 3 });
			var familiarization = new Mock<ILmsFamiliarizationCreditService>();
			familiarization.Setup(f => f.RunDueAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(new LmsFamiliarizationRunResult { Written = 4 });

			var result = await LmsProgramsLogic.Process(reports.Object, cycles.Object, familiarization.Object, DateTime.UtcNow);

			result.Failures.Should().Be(1);
			result.Renewed.Should().Be(2);
			result.Alerts.Should().Be(3);
			result.FamiliarizationWritten.Should().Be(4);
		}

		#endregion Worker 94
	}
}
