using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Approvals;
using Resgrid.Model.Events;
using Resgrid.Model.Identity;
using Resgrid.Model.IncidentLogistics;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Services.Approvals;
using Resgrid.Services.Signatures;
using Resgrid.Workers.Framework.Logic;

namespace Resgrid.Tests.Approvals
{
	/// <summary>
	/// e-Signature S4 approval chains end to end on the real S3 signing layer (in-memory stores): threshold routing, ordered and
	/// parallel steps, separation of duties, delegation while on leave (decided at turn start), escalation timing through worker 97's
	/// due-work passes, rejection with a reason and re-submission after a change (hash binding voids prior approvals), the module gate,
	/// sealing that fails closed, and workflow payloads without contact details.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class ApprovalChainTests
	{
		private const int Dept = 31;
		private const string Requester = "11111111-0000-4000-8000-0000000000a1";
		private const string Officer = "22222222-0000-4000-8000-0000000000a2";
		private const string Chief = "33333333-0000-4000-8000-0000000000a3";
		private const string Medic = "44444444-0000-4000-8000-0000000000a4";
		private const int LogisticsRole = 1;
		private const int FinanceRole = 2;

		private string _baseUrl;
		private List<SignatureRequestRecord> _sigRequests;
		private List<SignatureRequestSigner> _signers;
		private List<SignatureRecord> _records;
		private List<ApprovalChain> _chains;
		private List<ApprovalChainStep> _chainSteps;
		private List<ApprovalRequest> _approvals;
		private List<ApprovalRequestStep> _steps;
		private List<ApprovalDelegate> _delegates;
		private List<MemberLeavePeriod> _leave;
		private List<ApprovalWorkflowEvent> _workflow;
		private List<(List<string> Users, bool Escalated, int ApprovalId)> _awaiting;
		private List<ApprovalRequest> _decided;
		private HashSet<string> _active;
		private FakeSubject _subject;
		private SignatureRequestService _signatures;
		private ApprovalChainService _service;

		/// <summary>A module subject whose content, amount and availability the test controls.</summary>
		public sealed class FakeSubject : IApprovalSubjectSource
		{
			public SignatureSubjectKinds Kind { get; set; } = SignatureSubjectKinds.PurchaseRequisitionApproval;
			public bool Available { get; set; } = true;
			public decimal? Amount { get; set; } = 1200m;
			public string Content { get; set; } = "Generator rental, 3 days";
			public bool Submittable { get; set; } = true;
			public string Owner { get; set; } = Requester;
			public int Approved { get; private set; }
			public int Rejected { get; private set; }

			public Task<bool> IsAvailableAsync(int departmentId) => Task.FromResult(Available);

			public Task<ApprovalSubjectInfo> DescribeAsync(int departmentId, string subjectId) => Task.FromResult(new ApprovalSubjectInfo
			{
				Kind = Kind, SubjectId = subjectId, Title = "REQ-7", Amount = Amount, Currency = "USD", OwnerUserId = Owner, Submittable = Submittable,
				ContentSha256 = ApprovalDocuments.Hash(new { Content, Amount }), DocumentHtml = ApprovalDocuments.Build("REQ-7", new[] { ("Description", Content) })
			});

			public Task<List<ApprovalSubjectInfo>> GetCandidatesAsync(int departmentId, int take) => Task.FromResult(new List<ApprovalSubjectInfo>());

			public Task OnApprovedAsync(ApprovalRequest approval, string approverUserId, CancellationToken cancellationToken = default) { Approved++; return Task.CompletedTask; }

			public Task OnRejectedAsync(ApprovalRequest approval, string userId, string reason, CancellationToken cancellationToken = default) { Rejected++; return Task.CompletedTask; }
		}

		[SetUp]
		public void SetUp()
		{
			_baseUrl = SystemBehaviorConfig.ResgridBaseUrl;
			SystemBehaviorConfig.ResgridBaseUrl = "https://resgrid.test";
			_sigRequests = new List<SignatureRequestRecord>();
			_signers = new List<SignatureRequestSigner>();
			_records = new List<SignatureRecord>();
			_chains = new List<ApprovalChain>();
			_chainSteps = new List<ApprovalChainStep>();
			_approvals = new List<ApprovalRequest>();
			_steps = new List<ApprovalRequestStep>();
			_delegates = new List<ApprovalDelegate>();
			_leave = new List<MemberLeavePeriod>();
			_workflow = new List<ApprovalWorkflowEvent>();
			_awaiting = new List<(List<string>, bool, int)>();
			_decided = new List<ApprovalRequest>();
			_active = new HashSet<string>(new[] { Requester, Officer, Chief, Medic }, StringComparer.OrdinalIgnoreCase);
			_subject = new FakeSubject();
			var roles = new Dictionary<int, HashSet<string>>
			{
				[LogisticsRole] = new HashSet<string>(new[] { Requester, Medic }, StringComparer.OrdinalIgnoreCase),
				[FinanceRole] = new HashSet<string>(new[] { Officer }, StringComparer.OrdinalIgnoreCase)
			};
			var admins = new HashSet<string>(new[] { Chief }, StringComparer.OrdinalIgnoreCase);

			// --- S3 stores ---
			var sigRequests = new Mock<ISignatureRequestsRepository>();
			sigRequests.Setup(x => x.InsertAsync(It.IsAny<SignatureRequestRecord>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((SignatureRequestRecord r, CancellationToken _, bool __) => { r.SignatureRequestId = _sigRequests.Count + 1; _sigRequests.Add(r); return r; });
			sigRequests.Setup(x => x.UpdateAsync(It.IsAny<SignatureRequestRecord>(), It.IsAny<CancellationToken>(), It.IsAny<bool>())).ReturnsAsync((SignatureRequestRecord r, CancellationToken _, bool __) => r);
			sigRequests.Setup(x => x.GetByIdForDepartmentAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync((int d, int id) => _sigRequests.FirstOrDefault(r => r.DepartmentId == d && r.SignatureRequestId == id));
			var signers = new Mock<ISignatureRequestSignersRepository>();
			signers.Setup(x => x.InsertAsync(It.IsAny<SignatureRequestSigner>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((SignatureRequestSigner s, CancellationToken _, bool __) => { s.SignatureRequestSignerId = _signers.Count + 100; _signers.Add(s); return s; });
			signers.Setup(x => x.UpdateAsync(It.IsAny<SignatureRequestSigner>(), It.IsAny<CancellationToken>(), It.IsAny<bool>())).ReturnsAsync((SignatureRequestSigner s, CancellationToken _, bool __) => s);
			signers.Setup(x => x.GetForRequestAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync((int d, int id) => _signers.Where(s => s.DepartmentId == d && s.SignatureRequestId == id).ToList());
			var records = new Mock<ISignatureRecordsRepository>();
			records.Setup(x => x.InsertAsync(It.IsAny<SignatureRecord>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((SignatureRecord r, CancellationToken _, bool __) => { r.SignatureRecordId = _records.Count + 500; _records.Add(r); return r; });
			records.Setup(x => x.GetForSubjectAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>()))
				.ReturnsAsync((int d, int k, string s) => _records.Where(r => r.DepartmentId == d && r.SubjectKind == k && r.SubjectId == s).ToList());
			var captures = new Mock<ISignatureCapturesRepository>();
			captures.Setup(x => x.GetForRequestAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(new List<SignatureCapture>());

			// Sealing is not configured: the seal fails closed ("sealing pending"), never sealed.
			var sealing = new Mock<IDocumentSealingService>();
			sealing.Setup(x => x.SealHtmlAsync(It.IsAny<SealHtmlRequest>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new SealOutcome { Document = new SealedDocument { SealedDocumentId = 88, DepartmentId = Dept, Status = (int)SealedDocumentStatuses.Pending }, Reason = "sealing_sidecar_not_configured" });
			sealing.Setup(x => x.GetByIdAsync(Dept, 88)).ReturnsAsync(new SealedDocument { SealedDocumentId = 88, DepartmentId = Dept, Status = (int)SealedDocumentStatuses.Pending });

			var pdf = new Mock<IPdfProvider>();
			pdf.Setup(x => x.ConvertHtmlToPdf(It.IsAny<string>(), It.IsAny<string>())).Returns(Encoding.ASCII.GetBytes("%PDF-1.4 /Type /Page"));
			var departments = new Mock<IDepartmentsService>();
			departments.Setup(x => x.GetDepartmentByIdAsync(It.IsAny<int>(), It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = Dept, Name = "Mesa Flats ERT" });
			departments.Setup(x => x.GetActiveMemberUserIdsAsync(Dept)).ReturnsAsync(() => new HashSet<string>(_active, StringComparer.OrdinalIgnoreCase));
			departments.Setup(x => x.GetActiveAdminsForDepartmentAsync(Dept)).ReturnsAsync(admins.Select(a => new IdentityUser { Id = a }).ToList());

			var events = new Mock<IEventAggregator>();
			events.Setup(x => x.SendMessage(It.IsAny<ApprovalWorkflowEvent>())).Callback((ApprovalWorkflowEvent e) => _workflow.Add(e));

			// The eligibility rule as SignerEligibilityService applies it: excluded first, named members, department admins, roles.
			bool Eligible(string u, SignerEligibility e) => e != null && !e.Excludes(u) && _active.Contains(u)
				&& (e.Names(u) || (e.DepartmentAdmins && admins.Contains(u)) || e.PersonnelRoleIds.Any(r => roles.TryGetValue(r, out var m) && m.Contains(u)));
			var eligibility = new Mock<ISignerEligibilityService>();
			eligibility.Setup(x => x.IsEligibleAsync(Dept, It.IsAny<string>(), It.IsAny<SignerEligibility>())).ReturnsAsync((int d, string u, SignerEligibility e) => Eligible(u, e));
			eligibility.Setup(x => x.GetEligibleUserIdsAsync(Dept, It.IsAny<SignerEligibility>())).ReturnsAsync((int d, SignerEligibility e) => _active.Where(u => Eligible(u, e)).ToList());

			var profiles = new Mock<IUserProfileService>();
			profiles.Setup(x => x.GetProfileByUserIdAsync(It.IsAny<string>(), It.IsAny<bool>())).ReturnsAsync((string id, bool _) => new UserProfile { UserId = id, FirstName = "M", LastName = id.Substring(0, 4) });
			var memberSignatures = new SignatureService(records.Object, Mock.Of<IProtocolVersionsRepository>(), Mock.Of<IPersonnelCertificationRepository>(), profiles.Object,
				Mock.Of<IPersonnelRolesService>(), Mock.Of<IEventAggregator>(), Mock.Of<IProtocolVersionFilesRepository>(), Resgrid.Tests.Security.StepUpTestPolicies.Strict());

			var sources = new IApprovalSubjectSource[] { _subject };
			ApprovalChainService service = null;
			var adapter = new ApprovalSignatureSubjectSource(SignatureSubjectKinds.PurchaseRequisitionApproval, sources, new Lazy<IApprovalChainService>(() => service));
			_signatures = new SignatureRequestService(sigRequests.Object, signers.Object, captures.Object, records.Object, Mock.Of<IESignWebhookEventsRepository>(),
				Mock.Of<IRmsSignaturesRepository>(), new ISignatureSubjectSource[] { adapter }, Mock.Of<IESignProvider>(), Mock.Of<IESignAccountService>(), sealing.Object, pdf.Object,
				Mock.Of<IEncryptionService>(), Mock.Of<IEmailSender>(), Mock.Of<ITextMessageProvider>(), departments.Object, Mock.Of<IDepartmentSettingsService>(),
				Mock.Of<IBusinessOperationsAccessService>(), Mock.Of<IReadinessAccessService>(), Mock.Of<IIncidentBackOfficeAccessService>(), Mock.Of<ILmsAccessService>(),
				Mock.Of<ICacheProvider>(), events.Object, memberSignatures, eligibility.Object, Mock.Of<ISignatureNotificationService>(), Mock.Of<IDepartmentDataProtectionService>(), new Lazy<IProtectedWriteService>(() => Mock.Of<IProtectedWriteService>()));

			// --- Approval stores ---
			var chains = new Mock<IApprovalChainsRepository>();
			chains.Setup(x => x.InsertAsync(It.IsAny<ApprovalChain>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((ApprovalChain c, CancellationToken _, bool __) => { c.ApprovalChainId = _chains.Count + 1; _chains.Add(c); return c; });
			chains.Setup(x => x.UpdateAsync(It.IsAny<ApprovalChain>(), It.IsAny<CancellationToken>(), It.IsAny<bool>())).ReturnsAsync((ApprovalChain c, CancellationToken _, bool __) => c);
			chains.Setup(x => x.GetForDepartmentAsync(Dept)).ReturnsAsync(() => _chains.ToList());
			chains.Setup(x => x.GetActiveForKindAsync(Dept, It.IsAny<int>())).ReturnsAsync((int d, int k) => _chains.Where(c => c.IsActive && c.SubjectKind == k).OrderByDescending(c => c.ApprovalChainId).FirstOrDefault());
			var chainSteps = new Mock<IApprovalChainStepsRepository>();
			chainSteps.Setup(x => x.InsertAsync(It.IsAny<ApprovalChainStep>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((ApprovalChainStep s, CancellationToken _, bool __) => { s.ApprovalChainStepId = _chainSteps.Count + 1; _chainSteps.Add(s); return s; });
			chainSteps.Setup(x => x.GetForChainAsync(Dept, It.IsAny<int>())).ReturnsAsync((int d, int id) => _chainSteps.Where(s => s.ApprovalChainId == id).OrderBy(s => s.StepOrder).ToList());
			var requests = new Mock<IApprovalRequestsRepository>();
			requests.Setup(x => x.InsertAsync(It.IsAny<ApprovalRequest>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((ApprovalRequest r, CancellationToken _, bool __) => { r.ApprovalRequestId = _approvals.Count + 1; _approvals.Add(r); return r; });
			requests.Setup(x => x.UpdateAsync(It.IsAny<ApprovalRequest>(), It.IsAny<CancellationToken>(), It.IsAny<bool>())).ReturnsAsync((ApprovalRequest r, CancellationToken _, bool __) => r);
			requests.Setup(x => x.GetForDepartmentAsync(Dept, It.IsAny<int>())).ReturnsAsync((int d, int id) => _approvals.FirstOrDefault(r => r.ApprovalRequestId == id));
			requests.Setup(x => x.GetForSubjectAsync(Dept, It.IsAny<int>(), It.IsAny<string>()))
				.ReturnsAsync((int d, int k, string s) => _approvals.Where(r => r.SubjectKind == k && r.SubjectId == s).OrderByDescending(r => r.ApprovalRequestId).ToList());
			requests.Setup(x => x.GetBySignatureRequestAsync(Dept, It.IsAny<int>())).ReturnsAsync((int d, int id) => _approvals.FirstOrDefault(r => r.SignatureRequestId == id));
			requests.Setup(x => x.GetByStatusAsync(Dept, It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync((int d, int s, int t) => _approvals.Where(r => r.Status == s).ToList());
			requests.Setup(x => x.ClaimStatusAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int id, int from, int to, DateTime now, CancellationToken _) =>
				{
					var row = _approvals.FirstOrDefault(r => r.ApprovalRequestId == id);
					if (row == null || row.Status != from) return false;
					row.Status = to;
					return true;
				});
			requests.Setup(x => x.GetPendingWithClosedSignatureAsync(It.IsAny<int>()))
				.ReturnsAsync((int t) => _approvals.Where(a => a.IsPending && _sigRequests.Any(r => r.SignatureRequestId == a.SignatureRequestId && !r.IsOpen)).ToList());
			var steps = new Mock<IApprovalRequestStepsRepository>();
			steps.Setup(x => x.InsertAsync(It.IsAny<ApprovalRequestStep>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((ApprovalRequestStep s, CancellationToken _, bool __) => { s.ApprovalRequestStepId = _steps.Count + 1; _steps.Add(s); return s; });
			steps.Setup(x => x.UpdateAsync(It.IsAny<ApprovalRequestStep>(), It.IsAny<CancellationToken>(), It.IsAny<bool>())).ReturnsAsync((ApprovalRequestStep s, CancellationToken _, bool __) => s);
			steps.Setup(x => x.GetForRequestAsync(Dept, It.IsAny<int>())).ReturnsAsync((int d, int id) => _steps.Where(s => s.ApprovalRequestId == id).ToList());
			steps.Setup(x => x.GetUnannouncedTurnsAsync(It.IsAny<int>())).ReturnsAsync((int t) => _steps.Where(s => !s.TurnNotifiedOn.HasValue
				&& _approvals.Any(a => a.ApprovalRequestId == s.ApprovalRequestId && a.IsPending)
				&& _signers.Any(g => g.SignatureRequestSignerId == s.SignatureRequestSignerId && g.TurnStartedOn.HasValue && !g.IsDone)).ToList());
			steps.Setup(x => x.GetDueEscalationsAsync(It.IsAny<DateTime>(), It.IsAny<int>())).ReturnsAsync((DateTime now, int t) => _steps.Where(s => s.EscalateDueOn.HasValue
				&& s.EscalateDueOn <= now && !s.EscalatedOn.HasValue && _approvals.Any(a => a.ApprovalRequestId == s.ApprovalRequestId && a.IsPending)).ToList());
			steps.Setup(x => x.ClaimAnnouncementAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int id, DateTime now, DateTime? due, CancellationToken _) =>
				{
					var row = _steps.First(s => s.ApprovalRequestStepId == id);
					if (row.TurnNotifiedOn.HasValue) return false;
					row.TurnNotifiedOn = now;
					row.EscalateDueOn = due;
					return true;
				});
			steps.Setup(x => x.ClaimEscalationAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int id, DateTime now, CancellationToken _) =>
				{
					var row = _steps.First(s => s.ApprovalRequestStepId == id);
					if (row.EscalatedOn.HasValue) return false;
					row.EscalatedOn = now;
					return true;
				});
			var delegates = new Mock<IApprovalDelegatesRepository>();
			delegates.Setup(x => x.GetForDepartmentAsync(Dept)).ReturnsAsync(() => _delegates.ToList());
			var leave = new Mock<IMemberLeaveService>();
			leave.Setup(x => x.GetForDepartmentAsync(Dept)).ReturnsAsync(() => _leave.ToList());
			var notifications = new Mock<IApprovalNotificationService>();
			notifications.Setup(x => x.NotifyAwaitingAsync(It.IsAny<ApprovalRequest>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
				.Callback((ApprovalRequest r, IReadOnlyCollection<string> users, bool escalated, CancellationToken _) => _awaiting.Add((users.ToList(), escalated, r.ApprovalRequestId))).ReturnsAsync(1);
			notifications.Setup(x => x.NotifyDecidedAsync(It.IsAny<ApprovalRequest>(), It.IsAny<CancellationToken>()))
				.Callback((ApprovalRequest r, CancellationToken _) => _decided.Add(r)).ReturnsAsync(1);

			service = new ApprovalChainService(chains.Object, chainSteps.Object, requests.Object, steps.Object, delegates.Object, sources, _signatures, signers.Object,
				eligibility.Object, leave.Object, departments.Object, Mock.Of<IDepartmentGroupsService>(), notifications.Object, sealing.Object, events.Object);
			_service = service;
		}

		[TearDown]
		public void TearDown() => SystemBehaviorConfig.ResgridBaseUrl = _baseUrl;

		private static SignatureProof SteppedUp(string userId)
		{
			var proof = SignatureProof.SessionOnly(userId, "10.0.0.1", "test");
			proof.StepUpVerifiedOnUtc = DateTime.UtcNow.AddMinutes(-1);
			proof.StepUpMethod = 1;
			return proof;
		}

		private static ApprovalChainStep Step(int order, ApprovalApproverKinds kind, int? role = null, string user = null, decimal? above = null, int? escalateHours = null,
			ApprovalEscalationTargets escalateTo = ApprovalEscalationTargets.DepartmentAdmins) =>
			new ApprovalChainStep { StepOrder = order, ApproverKind = (int)kind, PersonnelRoleId = role, UserId = user, MinimumAmount = above, EscalateAfterHours = escalateHours, EscalateTo = (int)escalateTo };

		private async Task DefineAsync(params ApprovalChainStep[] steps)
		{
			var (refusal, _) = await _service.SaveChainAsync(new ApprovalChainDefinition
			{
				Chain = new ApprovalChain { DepartmentId = Dept, SubjectKind = (int)SignatureSubjectKinds.PurchaseRequisitionApproval, Name = "Purchases", IsActive = true, ExpiresInDays = 14 },
				Steps = steps.Select(s => { s.DepartmentId = Dept; return s; }).ToList()
			}, Chief);
			refusal.Should().Be(ApprovalRefusals.None);
		}

		private Task<ApprovalResult> SubmitAsync(string subjectId = "req-1", string userId = Requester) => _service.SubmitAsync(new ApprovalSubmitInput
		{
			DepartmentId = Dept, Kind = SignatureSubjectKinds.PurchaseRequisitionApproval, SubjectId = subjectId, UserId = userId, Culture = "en"
		});

		private Task<ApprovalResult> ApproveAsync(int id, string userId) => _service.ApproveAsync(Dept, id, SteppedUp(userId), false);

		private List<SignatureRequestSigner> SignersOf(ApprovalRequest approval) => _signers.Where(s => s.SignatureRequestId == approval.SignatureRequestId).ToList();

		#region Routing (pure)

		[Test]
		public void Thresholds_filter_steps_and_orders_compact_with_parallel_groups_kept()
		{
			var steps = new List<ApprovalChainStep>
			{
				new ApprovalChainStep { ApprovalChainStepId = 1, StepOrder = 1 },
				new ApprovalChainStep { ApprovalChainStepId = 2, StepOrder = 1 },
				new ApprovalChainStep { ApprovalChainStepId = 3, StepOrder = 2, MinimumAmount = 5000m },
				new ApprovalChainStep { ApprovalChainStepId = 4, StepOrder = 3 }
			};

			ApprovalRouting.Resolve(steps, 1200m).Select(r => (r.Step.ApprovalChainStepId, r.Order)).Should().Equal((1, 1), (2, 1), (4, 2));
			ApprovalRouting.Resolve(steps, 5000m).Should().HaveCount(3, "a threshold applies only above the amount");
			ApprovalRouting.Resolve(steps, 5000.01m).Select(r => (r.Step.ApprovalChainStepId, r.Order)).Should().Equal((1, 1), (2, 1), (3, 2), (4, 3));
			ApprovalRouting.Resolve(steps, null).Should().HaveCount(3, "a subject without an amount never meets a threshold");
		}

		[Test]
		public void Escalation_time_is_turn_start_plus_hours_and_never_without_hours()
		{
			var start = new DateTime(2026, 11, 25, 8, 0, 0, DateTimeKind.Utc);
			ApprovalRouting.EscalationDue(start, 24).Should().Be(start.AddHours(24));
			ApprovalRouting.EscalationDue(start, null).Should().BeNull();
			ApprovalRouting.EscalationDue(start, 0).Should().BeNull();
			ApprovalRouting.EscalationDue(null, 24).Should().BeNull("a step whose turn has not started does not escalate");
		}

		[Test]
		public void A_member_on_leave_or_inside_a_delegation_window_is_away_and_has_their_delegates()
		{
			var now = DateTime.UtcNow;
			var leave = new[] { new MemberLeavePeriod { UserId = Chief, StartsOn = now.AddDays(-1), EndsOn = now.AddDays(2) } };
			var delegates = new[]
			{
				new ApprovalDelegate { UserId = Chief.ToLowerInvariant(), DelegateUserId = Medic },
				new ApprovalDelegate { UserId = Officer, DelegateUserId = Medic, StartsOn = now.AddDays(5), EndsOn = now.AddDays(6) }
			};

			ApprovalRouting.IsAway(Chief, now, leave, delegates).Should().BeTrue();
			ApprovalRouting.DelegatesFor(Chief, now, leave, delegates).Should().Equal(Medic);
			ApprovalRouting.IsAway(Officer, now, leave, delegates).Should().BeFalse("the window has not opened");
			ApprovalRouting.IsAway(Officer, now.AddDays(5.5), leave, delegates).Should().BeTrue();
			ApprovalRouting.DelegatesFor(Medic, now, leave, delegates).Should().BeEmpty();
		}

		[Test]
		public void Chain_validation_refuses_steps_without_approvers_and_thresholds_on_kinds_without_amounts()
		{
			ApprovalChainDefinition Def(SignatureSubjectKinds kind, params ApprovalChainStep[] steps) => new ApprovalChainDefinition
			{
				Chain = new ApprovalChain { DepartmentId = Dept, SubjectKind = (int)kind, Name = "c", ExpiresInDays = 14 }, Steps = steps.ToList()
			};

			ApprovalRouting.Validate(Def(SignatureSubjectKinds.InvoiceApproval, Step(1, ApprovalApproverKinds.DepartmentAdmins))).Should().Be(ApprovalRefusals.None);
			ApprovalRouting.Validate(Def(SignatureSubjectKinds.InvoiceApproval, Step(1, ApprovalApproverKinds.Role))).Should().Be(ApprovalRefusals.NoApprover);
			ApprovalRouting.Validate(Def(SignatureSubjectKinds.InvoiceApproval, Step(1, ApprovalApproverKinds.NamedMember))).Should().Be(ApprovalRefusals.NoApprover);
			ApprovalRouting.Validate(Def(SignatureSubjectKinds.MutualAidAgreementApproval, Step(1, ApprovalApproverKinds.DepartmentAdmins, above: 10m))).Should().Be(ApprovalRefusals.InvalidRequest);
			ApprovalRouting.Validate(Def(SignatureSubjectKinds.InvoiceApproval, Step(1, ApprovalApproverKinds.DepartmentAdmins, escalateHours: 4, escalateTo: ApprovalEscalationTargets.Role)))
				.Should().Be(ApprovalRefusals.NoApprover);
			ApprovalRouting.Validate(Def(SignatureSubjectKinds.Bid, Step(1, ApprovalApproverKinds.DepartmentAdmins))).Should().Be(ApprovalRefusals.InvalidRequest, "only approval kinds have chains");
		}

		[Test]
		public void An_S3_eligibility_rule_stores_as_before_and_the_S4_members_round_trip()
		{
			new SignerEligibility { CertificationTypeIds = new List<int> { 3 } }.ToJson().Should().Be("{\"c\":[3],\"r\":[]}", "an S3 rule's stored form is unchanged");

			var rule = new SignerEligibility { PersonnelRoleIds = new List<int> { 2 }, UserIds = new List<string> { Officer, Officer.ToLowerInvariant() }, DepartmentAdmins = true, ExcludedUserIds = new List<string> { Requester } };
			var parsed = SignerEligibility.Parse(rule.ToJson());
			parsed.UserIds.Should().ContainSingle().Which.Should().BeEquivalentTo(Officer);
			parsed.DepartmentAdmins.Should().BeTrue();
			parsed.Excludes(Requester.ToLowerInvariant()).Should().BeTrue();
			parsed.Names(Officer.ToLowerInvariant()).Should().BeTrue();
			new SignerEligibility { ExcludedUserIds = new List<string> { Requester } }.IsEmpty.Should().BeTrue("an exclusion alone names nobody");
		}

		#endregion

		#region Submission and routing on the signing layer

		[Test]
		public async Task A_second_approver_is_added_only_above_the_threshold()
		{
			await DefineAsync(Step(1, ApprovalApproverKinds.Role, role: FinanceRole), Step(2, ApprovalApproverKinds.NamedMember, user: Chief, above: 5000m));

			var small = await SubmitAsync("req-small");
			small.Succeeded.Should().BeTrue();
			SignersOf(small.Request).Should().ContainSingle();

			_subject.Amount = 7500m;
			var large = await SubmitAsync("req-large");
			large.Succeeded.Should().BeTrue();
			SignersOf(large.Request).Select(s => s.SignerOrder).Should().Equal(1, 2);
			_sigRequests.Should().OnlyContain(r => r.Ordered && r.RequiredLevel == (int)SignatureLevels.StepUp);
			SignersOf(large.Request).Should().OnlyContain(s => s.RequiredLevel == (int)SignatureLevels.StepUp && s.UserId == null, "approval steps are open L2 steps");
		}

		[Test]
		public async Task Parallel_steps_all_approve_before_the_next_and_completion_applies_and_seals_failing_closed()
		{
			await DefineAsync(Step(1, ApprovalApproverKinds.Role, role: FinanceRole), Step(1, ApprovalApproverKinds.Role, role: LogisticsRole), Step(3, ApprovalApproverKinds.DepartmentAdmins));
			var submitted = await SubmitAsync();
			submitted.Succeeded.Should().BeTrue();
			var id = submitted.Request.ApprovalRequestId;
			_awaiting.SelectMany(a => a.Users).Should().BeEquivalentTo(new[] { Officer, Medic }, "the parallel group's approvers are told (one notice per step); the submitter never");

			(await ApproveAsync(id, Chief)).Refusal.Should().Be(ApprovalRefusals.NotYourTurn, "the admins' step waits for the parallel group");
			(await ApproveAsync(id, Requester)).Refusal.Should().Be(ApprovalRefusals.NotAnApprover, "the submitter cannot approve their own submission");
			(await _service.ApproveAsync(Dept, id, SignatureProof.SessionOnly(Officer), false)).Refusal.Should().Be(ApprovalRefusals.StepUpRequired, "approvals are L2");

			(await ApproveAsync(id, Officer)).Succeeded.Should().BeTrue();
			_awaiting.Should().HaveCount(2, "the group is not complete");
			(await ApproveAsync(id, Medic)).Succeeded.Should().BeTrue();
			_awaiting.Last().Users.Should().BeEquivalentTo(new[] { Chief });

			(await ApproveAsync(id, Chief)).Succeeded.Should().BeTrue();
			var approval = _approvals.Single(a => a.ApprovalRequestId == id);
			approval.Status.Should().Be((int)ApprovalRequestStatuses.Approved);
			approval.DecidedByUserId.Should().Be(Chief);
			approval.AppliedOn.Should().NotBeNull();
			_subject.Approved.Should().Be(1, "the module applies the decision once, although the completion hook runs per signer");
			approval.SealedDocumentId.Should().Be(88);
			var view = await _service.GetViewAsync(Dept, id, Requester, false);
			view.Sealed.IsSealed.Should().BeFalse("sealing is not configured: sealing pending, never sealed");
			_records.Where(r => r.SignatureRequestId == approval.SignatureRequestId).Should().OnlyContain(r => r.ArtifactSha256 == approval.ContentSha256 && r.Level == (int)SignatureLevels.StepUp);
			_workflow.Select(w => w.Change).Should().Equal(ApprovalWorkflowChanges.Requested, ApprovalWorkflowChanges.Granted);
			_decided.Should().ContainSingle();
		}

		[Test]
		public async Task A_named_approver_on_leave_is_delegated_when_the_step_turns_and_the_delegate_approves()
		{
			_delegates.Add(new ApprovalDelegate { DepartmentId = Dept, UserId = Chief, DelegateUserId = Medic });
			await DefineAsync(Step(1, ApprovalApproverKinds.Role, role: FinanceRole), Step(2, ApprovalApproverKinds.NamedMember, user: Chief));
			var submitted = await SubmitAsync();
			var id = submitted.Request.ApprovalRequestId;

			// The chief goes on leave after submission: delegation is decided when the chief's step turns.
			_leave.Add(new MemberLeavePeriod { DepartmentId = Dept, UserId = Chief, StartsOn = DateTime.UtcNow.AddHours(-1), EndsOn = DateTime.UtcNow.AddDays(3) });
			(await ApproveAsync(id, Officer)).Succeeded.Should().BeTrue();

			var second = _steps.Single(s => s.ApprovalRequestId == id && s.StepOrder == 2);
			second.DelegatedToUserIds.Should().Be(Medic);
			_awaiting.Last().Users.Should().BeEquivalentTo(new[] { Chief, Medic });
			(await ApproveAsync(id, Medic)).Succeeded.Should().BeTrue();
			_approvals.Single(a => a.ApprovalRequestId == id).Status.Should().Be((int)ApprovalRequestStatuses.Approved);
		}

		[Test]
		public async Task A_named_approver_away_without_a_delegate_escalates_at_once()
		{
			_leave.Add(new MemberLeavePeriod { DepartmentId = Dept, UserId = Officer, StartsOn = DateTime.UtcNow.AddDays(-1), EndsOn = DateTime.UtcNow.AddDays(1) });
			await DefineAsync(Step(1, ApprovalApproverKinds.NamedMember, user: Officer));
			var id = (await SubmitAsync()).Request.ApprovalRequestId;

			_steps.Single(s => s.ApprovalRequestId == id).EscalateDueOn.Should().NotBeNull();
			(await _service.EscalateDueAsync(DateTime.UtcNow.AddSeconds(1), 10)).Should().Be(1);
			(await ApproveAsync(id, Chief)).Succeeded.Should().BeTrue("the department admins can approve an escalated step");
		}

		[Test]
		public async Task A_stalled_step_escalates_after_its_hours_once_through_the_worker()
		{
			await DefineAsync(Step(1, ApprovalApproverKinds.Role, role: FinanceRole, escalateHours: 24));
			var id = (await SubmitAsync()).Request.ApprovalRequestId;
			var step = _steps.Single(s => s.ApprovalRequestId == id);
			var turn = _signers.Single(s => s.SignatureRequestSignerId == step.SignatureRequestSignerId).TurnStartedOn.Value;
			step.EscalateDueOn.Should().BeCloseTo(turn.AddHours(24), TimeSpan.FromSeconds(1));

			(await ApprovalsLogic.Process(_service, turn.AddHours(23))).Escalated.Should().Be(0);
			(await ApproveAsync(id, Chief)).Refusal.Should().Be(ApprovalRefusals.NotAnApprover);

			var run = await ApprovalsLogic.Process(_service, turn.AddHours(25));
			run.Escalated.Should().Be(1);
			_awaiting.Last().Escalated.Should().BeTrue();
			_awaiting.Last().Users.Should().BeEquivalentTo(new[] { Chief });
			_workflow.Last().Change.Should().Be(ApprovalWorkflowChanges.Escalated);
			(await ApprovalsLogic.Process(_service, turn.AddHours(26))).Escalated.Should().Be(0, "a step escalates once");

			(await ApproveAsync(id, Chief)).Succeeded.Should().BeTrue();
		}

		#endregion

		#region Rejection, change and re-submission

		[Test]
		public async Task A_rejection_needs_a_reason_and_resubmission_needs_a_change_that_voids_the_earlier_approvals()
		{
			await DefineAsync(Step(1, ApprovalApproverKinds.Role, role: FinanceRole), Step(2, ApprovalApproverKinds.DepartmentAdmins));
			var first = (await SubmitAsync()).Request;
			(await ApproveAsync(first.ApprovalRequestId, Officer)).Succeeded.Should().BeTrue();

			(await _service.RejectAsync(Dept, first.ApprovalRequestId, Chief, "  ")).Refusal.Should().Be(ApprovalRefusals.ReasonRequired);
			(await _service.RejectAsync(Dept, first.ApprovalRequestId, Medic, "no")).Refusal.Should().Be(ApprovalRefusals.NotAnApprover);
			(await _service.RejectAsync(Dept, first.ApprovalRequestId, Chief, "Quote missing")).Succeeded.Should().BeTrue();
			first.Status.Should().Be((int)ApprovalRequestStatuses.Rejected);
			first.RejectReason.Should().Be("Quote missing");
			_subject.Rejected.Should().Be(1, "the subject goes back to its owner");
			_signers.Single(s => s.SignatureRequestId == first.SignatureRequestId && s.SignerOrder == 2).UserId.Should().Be(Chief, "the declined open step records who rejected it");

			(await SubmitAsync()).Refusal.Should().Be(ApprovalRefusals.Unchanged);

			_subject.Content = "Generator rental, 3 days, quote attached";
			var second = await SubmitAsync();
			second.Succeeded.Should().BeTrue();
			second.Request.SupersedesApprovalRequestId.Should().Be(first.ApprovalRequestId);
			second.Request.ContentSha256.Should().NotBe(first.ContentSha256);
			SignersOf(second.Request).Should().OnlyContain(s => s.Status == (int)SignatureSignerStatuses.Pending, "the officer's earlier approval does not carry over");
			(await ApproveAsync(second.Request.ApprovalRequestId, Chief)).Succeeded.Should().BeFalse("the finance step must approve the new content first");
		}

		[Test]
		public async Task A_change_while_pending_voids_the_chain_and_a_change_after_approval_voids_the_approval()
		{
			await DefineAsync(Step(1, ApprovalApproverKinds.Role, role: FinanceRole));
			var gate = new ApprovalGateService(Mock.Of<IApprovalChainsRepository>(c => c.GetActiveForKindAsync(Dept, It.IsAny<int>()) == Task.FromResult(_chains.Last())),
				Mock.Of<IApprovalChainStepsRepository>(s => s.GetForChainAsync(Dept, It.IsAny<int>()) == Task.FromResult(_chainSteps.ToList())),
				Mock.Of<IApprovalRequestsRepository>(r => r.GetForSubjectAsync(Dept, It.IsAny<int>(), "req-1") == Task.FromResult(_approvals.OrderByDescending(a => a.ApprovalRequestId).ToList())),
				new IApprovalSubjectSource[] { _subject });

			var first = (await SubmitAsync()).Request;
			_subject.Content = "Changed after submission";
			(await ApproveAsync(first.ApprovalRequestId, Officer)).Refusal.Should().Be(ApprovalRefusals.SubjectChanged, "an approval binds to the submitted content");

			var second = (await SubmitAsync()).Request;
			first.Status.Should().Be((int)ApprovalRequestStatuses.Superseded);
			_sigRequests.Single(r => r.SignatureRequestId == first.SignatureRequestId).Status.Should().Be((int)SignatureRequestStatuses.Voided);
			(await ApproveAsync(second.ApprovalRequestId, Officer)).Succeeded.Should().BeTrue();

			var approved = await new ApprovalGateService(Mock.Of<IApprovalChainsRepository>(c => c.GetActiveForKindAsync(Dept, It.IsAny<int>()) == Task.FromResult(_chains.Last())),
				Mock.Of<IApprovalChainStepsRepository>(s => s.GetForChainAsync(Dept, It.IsAny<int>()) == Task.FromResult(_chainSteps.ToList())),
				Mock.Of<IApprovalRequestsRepository>(r => r.GetForSubjectAsync(Dept, It.IsAny<int>(), "req-1") == Task.FromResult(_approvals.OrderByDescending(a => a.ApprovalRequestId).ToList())),
				new IApprovalSubjectSource[] { _subject }).CheckAsync(Dept, SignatureSubjectKinds.PurchaseRequisitionApproval, "req-1");
			approved.Required.Should().BeTrue();
			approved.Approved.Should().BeTrue();

			_subject.Content = "Changed after approval";
			var changed = await new ApprovalGateService(Mock.Of<IApprovalChainsRepository>(c => c.GetActiveForKindAsync(Dept, It.IsAny<int>()) == Task.FromResult(_chains.Last())),
				Mock.Of<IApprovalChainStepsRepository>(s => s.GetForChainAsync(Dept, It.IsAny<int>()) == Task.FromResult(_chainSteps.ToList())),
				Mock.Of<IApprovalRequestsRepository>(r => r.GetForSubjectAsync(Dept, It.IsAny<int>(), "req-1") == Task.FromResult(_approvals.OrderByDescending(a => a.ApprovalRequestId).ToList())),
				new IApprovalSubjectSource[] { _subject }).CheckAsync(Dept, SignatureSubjectKinds.PurchaseRequisitionApproval, "req-1");
			changed.Approved.Should().BeFalse();
			changed.ChangedSinceApproval.Should().BeTrue();
			changed.Allows.Should().BeFalse();
			gate.Should().NotBeNull();
		}

		[Test]
		public async Task A_decline_on_the_shared_signing_page_is_synced_by_the_worker_as_a_rejection()
		{
			await DefineAsync(Step(1, ApprovalApproverKinds.Role, role: FinanceRole));
			var approval = (await SubmitAsync()).Request;
			(await _signatures.DeclineStepAsync(Dept, approval.SignatureRequestId.Value, null, Officer, "Over budget")).Succeeded.Should().BeTrue();

			(await ApprovalsLogic.Process(_service, DateTime.UtcNow)).Synced.Should().Be(1);
			approval.Status.Should().Be((int)ApprovalRequestStatuses.Rejected);
			approval.RejectReason.Should().Be("Over budget");
			approval.DecidedByUserId.Should().Be(Officer);
		}

		[Test]
		public async Task Withdrawal_is_the_submitters_or_an_admins_and_voids_the_signature_request()
		{
			await DefineAsync(Step(1, ApprovalApproverKinds.Role, role: FinanceRole));
			var approval = (await SubmitAsync()).Request;
			(await _service.WithdrawAsync(Dept, approval.ApprovalRequestId, Medic, false)).Refusal.Should().Be(ApprovalRefusals.NotAllowed);
			(await _service.WithdrawAsync(Dept, approval.ApprovalRequestId, Requester, false)).Succeeded.Should().BeTrue();
			approval.Status.Should().Be((int)ApprovalRequestStatuses.Withdrawn);
			_sigRequests.Single().Status.Should().Be((int)SignatureRequestStatuses.Voided);
		}

		#endregion

		#region Gates, availability and payloads

		[Test]
		public async Task A_submission_needs_the_module_a_chain_an_applicable_step_and_the_owner_or_an_admin()
		{
			(await SubmitAsync()).Refusal.Should().Be(ApprovalRefusals.NoChain);
			await DefineAsync(Step(1, ApprovalApproverKinds.Role, role: FinanceRole, above: 10000m));
			(await SubmitAsync()).Refusal.Should().Be(ApprovalRefusals.NotRequired, "no step applies to 1,200");
			_subject.Amount = 20000m;
			(await SubmitAsync(userId: Medic)).Refusal.Should().Be(ApprovalRefusals.NotAllowed);
			_subject.Submittable = false;
			(await SubmitAsync()).Refusal.Should().Be(ApprovalRefusals.NotSubmittable);
			_subject.Submittable = true;
			_subject.Available = false;
			(await SubmitAsync()).Refusal.Should().Be(ApprovalRefusals.ModuleUnavailable);
			_subject.Available = true;
			(await SubmitAsync()).Succeeded.Should().BeTrue();
			(await SubmitAsync()).Refusal.Should().Be(ApprovalRefusals.AlreadyPending);
		}

		[Test]
		public async Task Each_use_is_available_only_while_its_module_and_add_on_are_active()
		{
			var ibo = new Mock<IIncidentBackOfficeAccessService>();
			var business = new Mock<IBusinessOperationsAccessService>();
			var requisitions = new PurchaseRequisitionApprovalSource(Mock.Of<IIncidentLedgerRepository<PurchaseRequisition>>(), ibo.Object, new Lazy<IIncidentLedgerService<PurchaseRequisition>>(() => null));
			var plans = new IncidentPlanApprovalSource(Mock.Of<IIncidentLedgerRepository<IncidentPlanVersion>>(), ibo.Object, new Lazy<IIncidentPlanningService>(() => null),
				new Lazy<IIncidentLedgerService<IncidentPlanVersion>>(() => null));
			var invoices = new InvoiceApprovalSource(Mock.Of<IInvoiceRepository>(), Mock.Of<IInvoiceLineItemRepository>(), business.Object);
			var mars = new CalOesMarsPackageApprovalSource(Mock.Of<ICalOesMarsWorkItemRepository>(), business.Object);
			var agreements = new MutualAidAgreementApprovalSource(Mock.Of<IMutualAidAgreementsRepository>(), new System.Lazy<IMutualAidCountersignService>(() => null));

			(await requisitions.IsAvailableAsync(Dept)).Should().BeFalse();
			(await plans.IsAvailableAsync(Dept)).Should().BeFalse();
			(await invoices.IsAvailableAsync(Dept)).Should().BeFalse();
			(await mars.IsAvailableAsync(Dept)).Should().BeFalse();
			(await agreements.IsAvailableAsync(Dept)).Should().BeTrue("mutual-aid agreements belong to no add-on");

			ibo.Setup(x => x.HasActiveAddonAsync(Dept)).ReturnsAsync(true);
			business.Setup(x => x.CanUseInvoicingAsync(Dept)).ReturnsAsync(true);
			(await requisitions.IsAvailableAsync(Dept)).Should().BeTrue();
			(await plans.IsAvailableAsync(Dept)).Should().BeTrue();
			(await invoices.IsAvailableAsync(Dept)).Should().BeTrue();
			(await mars.IsAvailableAsync(Dept)).Should().BeFalse("MARS needs cost recovery, not invoicing");
			business.Setup(x => x.CanUseCostRecoveryAsync(Dept)).ThrowsAsync(new InvalidOperationException("billing down"));
			(await mars.IsAvailableAsync(Dept)).Should().BeFalse("an unknown add-on state fails closed");

			// The gate asks nothing of a department whose module is off (the module is gated already).
			var gate = new ApprovalGateService(Mock.Of<IApprovalChainsRepository>(c => c.GetActiveForKindAsync(Dept, It.IsAny<int>()) == Task.FromResult(new ApprovalChain { DepartmentId = Dept, IsActive = true })),
				Mock.Of<IApprovalChainStepsRepository>(), Mock.Of<IApprovalRequestsRepository>(), new IApprovalSubjectSource[] { mars });
			(await gate.CheckAsync(Dept, SignatureSubjectKinds.CalOesMarsPackageApproval, "w-1")).Required.Should().BeFalse();
		}

		[Test]
		public async Task The_ledger_gate_refuses_an_approval_transition_until_the_chain_approved_the_current_content()
		{
			var result = new ApprovalGateResult { Required = true, Approved = false };
			var gate = new Mock<IApprovalGateService>();
			gate.Setup(x => x.CheckAsync(Dept, SignatureSubjectKinds.PurchaseRequisitionApproval, "req-9")).ReturnsAsync(() => result);
			var rule = new ApprovalChainLedgerRule<PurchaseRequisition>(gate.Object, SignatureSubjectKinds.PurchaseRequisitionApproval, (int)IncidentRequisitionStatuses.Approved, e => e.PurchaseRequisitionId);
			IncidentLedgerChange<PurchaseRequisition> Move(int from, int to) => new IncidentLedgerChange<PurchaseRequisition>
			{
				Actor = new IncidentActor { DepartmentId = Dept, UserId = Officer }, Entity = new PurchaseRequisition { PurchaseRequisitionId = "req-9" },
				Existing = new PurchaseRequisition(), IsTransition = true, OldStatus = from, NewStatus = to
			};

			Func<Task> approve = () => rule.BeforeSaveAsync(Move((int)IncidentRequisitionStatuses.Submitted, (int)IncidentRequisitionStatuses.Approved), CancellationToken.None);
			(await approve.Should().ThrowAsync<IncidentRuleException>()).Which.Message.Should().Contain("ApprovalChainRequired");
			await rule.Invoking(r => r.BeforeSaveAsync(Move((int)IncidentRequisitionStatuses.Submitted, (int)IncidentRequisitionStatuses.Rejected), CancellationToken.None)).Should().NotThrowAsync();

			result = new ApprovalGateResult { Required = true, Approved = true };
			await approve.Should().NotThrowAsync();
			result = new ApprovalGateResult { Required = false };
			await approve.Should().NotThrowAsync("without a chain the module's own approval stands");
		}

		[Test]
		public async Task Workflow_payloads_and_notification_text_carry_no_contact_details()
		{
			await DefineAsync(Step(1, ApprovalApproverKinds.Role, role: FinanceRole));
			var approval = (await SubmitAsync()).Request;
			await _service.RejectAsync(Dept, approval.ApprovalRequestId, Officer, "Quote missing");

			_workflow.Should().HaveCount(2);
			foreach (var json in _workflow.Select(e => JsonConvert.SerializeObject(e)))
			{
				json.Should().NotContain("@").And.NotContainEquivalentOf("email").And.NotContainEquivalentOf("phone");
			}
			typeof(ApprovalWorkflowEvent).GetProperties().Select(p => p.Name).Should().NotContain(n => n.Contains("Email") || n.Contains("Phone") || n.Contains("Name"));
			ApprovalNotificationService.AwaitingMessage(approval, true).Should().Contain(SignaturePolicy.ShortCode(approval.ContentSha256)).And.Contain("/User/Approvals/Detail?id=");
			ApprovalNotificationService.DecidedMessage(approval).Should().Contain("Quote missing").And.NotContain("@");
		}

		[Test]
		public async Task Saving_a_chain_keeps_running_requests_on_their_version_and_turns_off_the_old_one()
		{
			await DefineAsync(Step(1, ApprovalApproverKinds.Role, role: FinanceRole));
			var approval = (await SubmitAsync()).Request;
			var first = _chains.Single();

			var (refusal, saved) = await _service.SaveChainAsync(new ApprovalChainDefinition
			{
				Chain = new ApprovalChain { ApprovalChainId = first.ApprovalChainId, DepartmentId = Dept, SubjectKind = first.SubjectKind, Name = "Purchases v2", IsActive = true, ExpiresInDays = 7 },
				Steps = new List<ApprovalChainStep> { Step(1, ApprovalApproverKinds.DepartmentAdmins) }
			}, Chief);
			refusal.Should().Be(ApprovalRefusals.None);
			first.IsActive.Should().BeFalse();
			saved.Chain.ApprovalChainId.Should().NotBe(first.ApprovalChainId);
			(await _service.GetActiveChainAsync(Dept, SignatureSubjectKinds.PurchaseRequisitionApproval)).Chain.Name.Should().Be("Purchases v2");

			(await ApproveAsync(approval.ApprovalRequestId, Officer)).Succeeded.Should().BeTrue("the running request keeps the steps it started with");

			(await _service.SaveChainAsync(new ApprovalChainDefinition
			{
				Chain = new ApprovalChain { DepartmentId = Dept, SubjectKind = first.SubjectKind, Name = "x", IsActive = true, ExpiresInDays = 7 },
				Steps = new List<ApprovalChainStep> { Step(1, ApprovalApproverKinds.NamedMember, user: "99999999-0000-4000-8000-000000000099") }
			}, Chief)).Refusal.Should().Be(ApprovalRefusals.NoApprover, "a named approver must be an active member");
		}

		[Test]
		public async Task An_approved_mutual_aid_agreement_is_not_edited_and_an_edit_puts_a_draft_back()
		{
			var store = new List<MutualAidAgreement>();
			var repo = new Mock<IMutualAidAgreementsRepository>();
			repo.Setup(x => x.InsertAsync(It.IsAny<MutualAidAgreement>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((MutualAidAgreement a, CancellationToken _, bool __) => { a.MutualAidAgreementId = store.Count + 1; store.Add(a); return a; });
			repo.Setup(x => x.UpdateAsync(It.IsAny<MutualAidAgreement>(), It.IsAny<CancellationToken>(), It.IsAny<bool>())).ReturnsAsync((MutualAidAgreement a, CancellationToken _, bool __) => a);
			repo.Setup(x => x.GetForDepartmentAsync(Dept, It.IsAny<int>())).ReturnsAsync((int d, int id) => store.FirstOrDefault(a => a.MutualAidAgreementId == id && !a.IsDeleted));
			var service = new MutualAidAgreementService(repo.Object, Mock.Of<IEventAggregator>(), Mock.Of<IDepartmentLinksService>());

			var draft = await service.SaveAsync(new MutualAidAgreement { DepartmentId = Dept, Title = "County mutual aid", Terms = "Each party responds within 30 minutes." }, Chief);
			draft.Status.Should().Be((int)MutualAidAgreementStatuses.Draft);
			var hash = MutualAidAgreementApprovalSource.Describe(draft).ContentSha256;

			draft.Status = (int)MutualAidAgreementStatuses.InApproval;
			var edited = await service.SaveAsync(new MutualAidAgreement { MutualAidAgreementId = draft.MutualAidAgreementId, DepartmentId = Dept, Title = "County mutual aid", Terms = "Within 20 minutes." }, Chief);
			edited.Status.Should().Be((int)MutualAidAgreementStatuses.Draft);
			MutualAidAgreementApprovalSource.Describe(edited).ContentSha256.Should().NotBe(hash, "an edit changes the content the approvals bind to");

			edited.Status = (int)MutualAidAgreementStatuses.Approved;
			(await service.SaveAsync(new MutualAidAgreement { MutualAidAgreementId = draft.MutualAidAgreementId, DepartmentId = Dept, Title = "Changed" }, Chief)).Should().BeNull();
			(await service.DeleteAsync(Dept, draft.MutualAidAgreementId, Chief)).Should().BeFalse();
		}

		#endregion
	}
}
