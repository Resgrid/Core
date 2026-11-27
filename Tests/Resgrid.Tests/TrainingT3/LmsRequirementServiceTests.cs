using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Lms;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.TrainingT3
{
	/// <summary>
	/// The learning requirements engine (Training T3) against mocks: audience entry and onboarding, the compliance matrix (not licensed,
	/// inactive members out), the enforcement preview and the blast-radius refusal (R-L11), enforcement under the share, undo on completion,
	/// the lapse revert (rule 10), one billing read per daily pass and none for a department without due work, and workflow payloads
	/// without contact data.
	/// </summary>
	[TestFixture]
	public class LmsRequirementServiceTests
	{
		private const int Dept = 7;
		private const int RoleId = 5;
		private const int TypeId = 1;
		private static readonly DateTime Now = DateTime.UtcNow;
		private static readonly Department Department = new Department { DepartmentId = Dept, TimeZone = "UTC", Name = "Station 7" };

		private static string U(int i) => $"00000000-0000-4000-8000-{i:000000000000}";

		private sealed class Harness
		{
			public readonly Mock<ILmsRequirementsRepository> Requirements = new();
			public readonly Mock<ILmsRequirementAudiencesRepository> Audiences = new();
			public readonly Mock<ILmsRequirementMembersRepository> Members = new();
			public readonly Mock<ILmsRequirementEnforcementsRepository> Enforcements = new();
			public readonly Mock<ILmsRequirementFactsRepository> Facts = new();
			public readonly Mock<ILmsRequirementDepartmentStatesRepository> States = new();
			public readonly Mock<ILmsMemberSeatsRepository> Seats = new();
			public readonly Mock<ILmsPathsRepository> PathsRepo = new();
			public readonly Mock<ILmsCoursesRepository> Courses = new();
			public readonly Mock<ILmsAccessService> Access = new();
			public readonly Mock<ILmsReactivationService> Reactivation = new();
			public readonly Mock<ILmsLearnerService> Learner = new();
			public readonly Mock<ILmsPathService> Paths = new();
			public readonly Mock<ILmsAcademyService> Academy = new();
			public readonly Mock<IContentAudienceService> AudienceService = new();
			public readonly Mock<IMemberLeaveService> Leave = new();
			public readonly Mock<IDepartmentsService> Departments = new();
			public readonly Mock<IDepartmentSettingsService> Settings = new();
			public readonly Mock<ICertificationService> Certifications = new();
			public readonly Mock<IPersonnelRolesService> Roles = new();
			public readonly Mock<ITrainingService> Trainings = new();
			public readonly Mock<ILearningRecordService> LearningRecords = new();
			public readonly Mock<ILmsRequirementNotifier> Notifier = new();
			public readonly Mock<IEventAggregator> Events = new();

			public readonly List<LmsRequirement> RequirementRows = new();
			public readonly List<LmsRequirementAudience> AudienceRows = new();
			public readonly List<LmsRequirementMember> MemberRows = new();
			public readonly List<LmsRequirementEnforcement> EnforcementRows = new();
			public readonly List<LmsRequirementFact> CourseFacts = new();
			public readonly List<PersonnelCertification> Records = new();
			public readonly HashSet<string> Active = new(StringComparer.OrdinalIgnoreCase);
			public readonly List<LmsMemberSeat> SeatRows = new();
			public readonly Dictionary<string, List<PersonnelRole>> RolesByUser = new(StringComparer.OrdinalIgnoreCase);
			public readonly List<TrainingWorkflowEvent> Raised = new();
			public LmsSubscriptionSnapshot Subscription = new() { State = LmsSubscriptionStates.Active, Packs = 1 };
			public LearningRequirementSettings RequirementSettings = new() { EnforcementEnabled = true, MaxRoleRemovalSharePercent = 20 };
			public DepartmentCertificationSettings CertificationSettings = new() { DepartmentId = Dept, EnforcementMode = (int)CertificationEnforcementModes.Enforce, RoleRemovalGraceDays = 30 };

			public Harness()
			{
				Requirements.Setup(r => r.GetForDepartmentAsync(Dept, It.IsAny<bool>())).ReturnsAsync((int _, bool deleted) => RequirementRows.Where(r => deleted || !r.DeletedOn.HasValue).ToList());
				Requirements.Setup(r => r.GetByIdForDepartmentAsync(Dept, It.IsAny<int>())).ReturnsAsync((int _, int id) => RequirementRows.FirstOrDefault(r => r.LmsRequirementId == id));
				Requirements.Setup(r => r.HasLiveAsync(Dept)).ReturnsAsync(() => RequirementRows.Any(r => r.IsLive));
				Requirements.Setup(r => r.SaveOrUpdateAsync(It.IsAny<LmsRequirement>(), It.IsAny<CancellationToken>(), It.IsAny<bool>())).ReturnsAsync((LmsRequirement r, CancellationToken _, bool __) =>
				{
					if (r.LmsRequirementId == 0)
					{
						r.LmsRequirementId = RequirementRows.Count + 1;
						RequirementRows.Add(r);
					}
					return r;
				});
				Audiences.Setup(a => a.GetForDepartmentAsync(Dept)).ReturnsAsync(() => AudienceRows.ToList());
				Audiences.Setup(a => a.ReplaceAsync(Dept, It.IsAny<int>(), It.IsAny<IEnumerable<IContentAudienceRule>>(), It.IsAny<CancellationToken>()))
					.Callback((int _, int id, IEnumerable<IContentAudienceRule> rules, CancellationToken __) =>
					{
						AudienceRows.RemoveAll(a => a.LmsRequirementId == id);
						AudienceRows.AddRange(rules.Select(r => new LmsRequirementAudience { LmsRequirementId = id, DepartmentId = Dept, Kind = r.Kind, RefId = r.RefId }));
					}).Returns(Task.CompletedTask);
				Members.Setup(m => m.GetOpenForDepartmentAsync(Dept)).ReturnsAsync(() => MemberRows.Where(m => !m.LeftOn.HasValue).ToList());
				Members.Setup(m => m.GetOpenForUserAsync(Dept, It.IsAny<string>())).ReturnsAsync((int _, string u) =>
					MemberRows.Where(m => !m.LeftOn.HasValue && string.Equals(m.UserId, u, StringComparison.OrdinalIgnoreCase)).ToList());
				Members.Setup(m => m.InsertOpenAsync(It.IsAny<LmsRequirementMember>(), It.IsAny<CancellationToken>())).ReturnsAsync((LmsRequirementMember m, CancellationToken _) =>
				{
					m.LmsRequirementMemberId = MemberRows.Count + 100;
					MemberRows.Add(m);
					return (m, true);
				});
				Members.Setup(m => m.TryClaimNoticeAsync(Dept, It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
				Members.Setup(m => m.TryClaimAssignmentAsync(Dept, It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
				Enforcements.Setup(e => e.GetOpenForDepartmentAsync(Dept)).ReturnsAsync(() => EnforcementRows.Where(e => !e.UndoneOn.HasValue).ToList());
				Enforcements.Setup(e => e.TryInsertOpenAsync(It.IsAny<LmsRequirementEnforcement>(), It.IsAny<CancellationToken>())).ReturnsAsync((LmsRequirementEnforcement e, CancellationToken _) =>
				{
					e.LmsRequirementEnforcementId = EnforcementRows.Count + 1;
					EnforcementRows.Add(e);
					return true;
				});
				Enforcements.Setup(e => e.TryMarkUndoneAsync(Dept, It.IsAny<int>(), It.IsAny<LmsEnforcementUndoReasons>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
					.ReturnsAsync((int _, int id, LmsEnforcementUndoReasons reason, string roles, DateTime on, CancellationToken __) =>
					{
						var row = EnforcementRows.First(e => e.LmsRequirementEnforcementId == id);
						row.UndoneOn = on;
						row.UndoReason = (int)reason;
						row.RestoredRolesJson = roles;
						return true;
					});
				Facts.Setup(f => f.GetCourseCompletionsAsync(Dept, It.IsAny<IEnumerable<int>>())).ReturnsAsync(() => CourseFacts.ToList());
				Seats.Setup(s => s.GetForDepartmentAsync(Dept)).ReturnsAsync(() => SeatRows.ToList());
				Access.Setup(a => a.IsFlagEnabledAsync(Dept)).ReturnsAsync(true);
				Access.Setup(a => a.GetSubscriptionAsync(Dept)).ReturnsAsync(() => Subscription);
				Reactivation.Setup(r => r.GetLapsesAsync(Dept)).ReturnsAsync(new List<LmsAddonLapse>());
				Leave.Setup(l => l.GetByMemberAsync(Dept)).ReturnsAsync(new Dictionary<string, List<MemberLeavePeriod>>(StringComparer.OrdinalIgnoreCase));
				Departments.Setup(d => d.GetDepartmentByIdAsync(Dept, It.IsAny<bool>())).ReturnsAsync(Department);
				Departments.Setup(d => d.GetActiveMemberUserIdsAsync(Dept)).ReturnsAsync(() => new HashSet<string>(Active, StringComparer.OrdinalIgnoreCase));
				Departments.Setup(d => d.GetAllPersonnelNamesForDepartmentAsync(Dept)).ReturnsAsync(() =>
					Active.Select(u => new PersonName { UserId = u, FirstName = "Member", LastName = u.Substring(u.Length - 2) }).ToList());
				Settings.Setup(s => s.GetSettingByTypeAsync(Dept, DepartmentSettingTypes.LearningRequirementSettings))
					.ReturnsAsync(() => new DepartmentSetting { DepartmentId = Dept, Setting = ObjectSerialization.Serialize(RequirementSettings) });
				Certifications.Setup(c => c.GetCertificationsForDepartmentAsync(Dept, It.IsAny<IEnumerable<string>>())).ReturnsAsync(() => Records.ToList());
				Certifications.Setup(c => c.GetAllCertificationTypesByDepartmentAsync(Dept)).ReturnsAsync(new List<DepartmentCertificationType>
				{
					new DepartmentCertificationType { DepartmentCertificationTypeId = TypeId, DepartmentId = Dept, Type = "SCBA", DefaultValidityMonths = 12, IsActive = true }
				});
				Certifications.Setup(c => c.GetAllRoleRequirementsAsync(Dept)).ReturnsAsync(new List<PersonnelRoleCertificationRequirement>
				{
					new PersonnelRoleCertificationRequirement { PersonnelRoleId = RoleId, DepartmentId = Dept, DepartmentCertificationTypeId = TypeId, IsMandatory = true }
				});
				Certifications.Setup(c => c.GetCertificationSettingsAsync(Dept)).ReturnsAsync(() => CertificationSettings);
				Certifications.Setup(c => c.ExpireCertificationByRuleAsync(It.IsAny<int>(), Dept, It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
					.ReturnsAsync((int id, int _, DateTime __, string reason, string ___, CancellationToken ____) =>
					{
						var record = Records.First(r => r.PersonnelCertificationId == id);
						record.Status = (int)PersonnelCertificationStatuses.Expired;
						record.StatusReason = reason;
						return record;
					});
				Certifications.Setup(c => c.GetCertificationByIdAsync(It.IsAny<int>())).ReturnsAsync((int id) => Records.FirstOrDefault(r => r.PersonnelCertificationId == id));
				Roles.Setup(r => r.GetAllRolesForUsersInDepartmentAsync(Dept)).ReturnsAsync(() => RolesByUser.ToDictionary(p => p.Key, p => p.Value.ToList()));
				Roles.Setup(r => r.GetRolesForUserAsync(It.IsAny<string>(), Dept)).ReturnsAsync((string u, int _) => RolesByUser.TryGetValue(u, out var roles) ? roles.ToList() : new List<PersonnelRole>());
				Roles.Setup(r => r.GetRolesForDepartmentAsync(Dept)).ReturnsAsync(new List<PersonnelRole> { new PersonnelRole { PersonnelRoleId = RoleId, DepartmentId = Dept, Name = "Engineer" } });
				Courses.Setup(c => c.GetVisibleByIdsAsync(Dept, It.IsAny<IEnumerable<int>>())).ReturnsAsync(new List<LmsCourse> { new LmsCourse { LmsCourseId = 31, DepartmentId = Dept, Title = "SCBA refresher" } });
				Courses.Setup(c => c.GetVisibleByIdAsync(Dept, 31)).ReturnsAsync(new LmsCourse { LmsCourseId = 31, DepartmentId = Dept, Title = "SCBA refresher" });
				Departments.Setup(d => d.GetSelectablePersonnelNamesAsync(Dept)).ReturnsAsync(new List<PersonName>());
				Events.Setup(e => e.SendMessage(It.IsAny<TrainingWorkflowEvent>())).Callback((TrainingWorkflowEvent e) => Raised.Add(e));
				Notifier.Setup(n => n.NotifyRefusedAsync(Dept, It.IsAny<IReadOnlyCollection<LmsRoleBreach>>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(1);
			}

			public LmsRequirementService Build() => new LmsRequirementService(Requirements.Object, Audiences.Object, Members.Object, Enforcements.Object, Facts.Object, States.Object,
				Seats.Object, PathsRepo.Object, Courses.Object, Access.Object, Reactivation.Object, Learner.Object, Paths.Object, Academy.Object, AudienceService.Object, Leave.Object,
				Departments.Object, Settings.Object, Certifications.Object, Roles.Object, Trainings.Object, LearningRecords.Object, Notifier.Object, Events.Object);

			/// <summary>A requirement (course 31, 30 days, backs SCBA, enforcing) on role 5 and <paramref name="members"/> active, seated role holders who entered 60 days ago.</summary>
			public LmsRequirement Seed(int members, bool enforcing = true)
			{
				var requirement = new LmsRequirement
				{
					LmsRequirementId = 1, DepartmentId = Dept, Name = "SCBA refresher", Kind = (int)LmsRequirementKinds.Course, TargetRefId = 31, DueDays = 30, AtRiskDays = 14,
					BacksCertificationTypeId = TypeId, EnforcementEnabled = enforcing, IsActive = true, CreatedOn = Now.AddDays(-90), CreatedByUserId = U(99)
				};
				RequirementRows.Add(requirement);
				AudienceRows.Add(new LmsRequirementAudience { LmsRequirementId = 1, DepartmentId = Dept, Kind = (int)ContentAudienceKinds.Role, RefId = RoleId.ToString() });
				for (var i = 0; i < members; i++)
				{
					Active.Add(U(i));
					SeatRows.Add(new LmsMemberSeat { LmsMemberSeatId = i + 1, DepartmentId = Dept, UserId = U(i), EnabledOn = Now.AddDays(-100) });
					RolesByUser[U(i)] = new List<PersonnelRole> { new PersonnelRole { PersonnelRoleId = RoleId, DepartmentId = Dept, Name = "Engineer" } };
					Records.Add(new PersonnelCertification
					{
						PersonnelCertificationId = 1000 + i, DepartmentId = Dept, UserId = U(i), DepartmentCertificationTypeId = TypeId, Status = (int)PersonnelCertificationStatuses.Active,
						ExpiresOn = Now.AddMonths(8).Date
					});
					MemberRows.Add(new LmsRequirementMember
					{
						LmsRequirementMemberId = 10 + i, DepartmentId = Dept, LmsRequirementId = 1, UserId = U(i), EnteredOn = Now.AddDays(-60), State = (int)LmsRequirementStates.Overdue
					});
				}
				AudienceService.Setup(a => a.ResolveAsync(Dept, It.IsAny<IEnumerable<IContentAudienceRule>>()))
					.ReturnsAsync(() => new ContentAudienceResolution { MemberUserIds = new HashSet<string>(RolesByUser.Keys.Where(Active.Contains), StringComparer.OrdinalIgnoreCase) });
				return requirement;
			}

			public void Completed(int member, DateTime on) => CourseFacts.Add(new LmsRequirementFact { UserId = U(member), RefId = 31, CompletedOn = on, LearningRecordId = 5000 + member });
		}

		private static LmsActor Manager() => new LmsActor
		{
			DepartmentId = Dept, UserId = U(99), IsCurrentMember = true, IsDepartmentAdmin = true,
			Gate = new LmsDepartmentGate { DepartmentId = Dept, ModuleOn = true, FlagOn = true, AddonActive = true, LastEntitledOn = Now }
		};

		#region Enforcement and the blast-radius guard (R-L11)

		[Test]
		public async Task The_preview_shows_who_would_lose_which_role_and_that_the_run_would_be_refused()
		{
			var h = new Harness();
			h.Seed(10);

			var preview = await h.Build().PreviewEnforcementAsync(Manager(), Now);

			preview.Rows.Should().HaveCount(10);
			preview.Rows.Should().OnlyContain(r => r.RolesLost.Count == 1 && r.RolesLost[0].PersonnelRoleId == RoleId && r.CertificationTypeName == "SCBA");
			preview.WouldRefuse.Should().BeTrue();
			preview.Breaches.Single().Losing.Should().Be(10);
			preview.CertificationEnforcementMode.Should().Be((int)CertificationEnforcementModes.Enforce);
			h.Certifications.Verify(c => c.ExpireCertificationByRuleAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
				Times.Never(), "a preview changes nothing");
		}

		[Test]
		public async Task A_run_that_would_thin_a_role_beyond_the_share_is_refused_and_the_admins_are_told()
		{
			var h = new Harness();
			h.Seed(10);
			for (var i = 0; i < 7; i++)
				h.Completed(i, Now.AddDays(-5));
			// Three of ten holders overdue: 30% > 20%.

			var result = await h.Build().RunDailyAsync(Dept, Now);

			result.Refused.Should().BeTrue();
			result.Enforced.Should().Be(0);
			h.Certifications.Verify(c => c.ExpireCertificationByRuleAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
				Times.Never());
			h.EnforcementRows.Should().BeEmpty();
			h.Notifier.Verify(n => n.NotifyRefusedAsync(Dept, It.Is<IReadOnlyCollection<LmsRoleBreach>>(b => b.Single().Losing == 3 && b.Single().Holders == 10), 20,
				It.IsAny<CancellationToken>()), Times.Once());
			h.States.Verify(s => s.StampRefusedAsync(Dept, Now, It.IsAny<CancellationToken>()), Times.Once());
			h.Raised.Should().NotContain(e => e.Change == TrainingWorkflowChanges.Enforced);
		}

		[Test]
		public async Task A_run_within_the_share_expires_the_backed_certifications_and_records_each_action()
		{
			var h = new Harness();
			h.Seed(10);
			for (var i = 2; i < 10; i++)
				h.Completed(i, Now.AddDays(-5));

			var result = await h.Build().RunDailyAsync(Dept, Now);

			result.Refused.Should().BeFalse();
			result.Enforced.Should().Be(2);
			h.EnforcementRows.Select(e => e.UserId).Should().BeEquivalentTo(new[] { U(0), U(1) });
			h.EnforcementRows.Should().OnlyContain(e => e.PreviousStatus == (int)PersonnelCertificationStatuses.Active && e.PreviousExpiresOn.HasValue &&
				LmsRequirementService.ParseRoleIds(e.RolesJson).SequenceEqual(new[] { RoleId }));
			h.Certifications.Verify(c => c.ExpireCertificationByRuleAsync(1000, Dept, It.IsAny<DateTime>(), "learning-requirement:1", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once());
			h.Raised.Count(e => e.Change == TrainingWorkflowChanges.Enforced).Should().Be(2);
			h.Notifier.Verify(n => n.NotifyEnforcementAsync(Dept, U(0), It.IsAny<LmsRequirement>(), "SCBA", false, It.IsAny<CancellationToken>()), Times.Once());
		}

		[Test]
		public async Task Enforcement_waits_for_the_department_switch()
		{
			var h = new Harness();
			h.Seed(10);
			for (var i = 1; i < 10; i++)
				h.Completed(i, Now.AddDays(-5));
			h.RequirementSettings = new LearningRequirementSettings { EnforcementEnabled = false };

			(await h.Build().RunDailyAsync(Dept, Now)).Enforced.Should().Be(0);
			h.EnforcementRows.Should().BeEmpty();
		}

		[Test]
		public async Task Completing_the_requirement_undoes_the_action_restores_the_record_and_gives_the_role_back()
		{
			var h = new Harness();
			h.Seed(10);
			var previousExpiry = Now.AddMonths(8).Date;
			var record = h.Records.First(r => r.UserId == U(0));
			record.Status = (int)PersonnelCertificationStatuses.Expired;
			record.StatusReason = "learning-requirement:1";
			h.RolesByUser[U(0)] = new List<PersonnelRole>();
			h.EnforcementRows.Add(new LmsRequirementEnforcement
			{
				LmsRequirementEnforcementId = 1, DepartmentId = Dept, LmsRequirementId = 1, LmsRequirementMemberId = 10, UserId = U(0), PersonnelCertificationId = record.PersonnelCertificationId,
				DepartmentCertificationTypeId = TypeId, PreviousStatus = (int)PersonnelCertificationStatuses.Active, PreviousExpiresOn = previousExpiry, DueOn = Now.AddDays(-30),
				ActedOn = Now.AddDays(-20), RolesJson = "[{\"PersonnelRoleId\":5,\"RoleName\":\"Engineer\"}]"
			});
			h.Completed(0, Now.AddHours(-1));

			var undone = await h.Build().UndoCompletedAsync(Dept, Now);

			undone.Should().Be(1);
			h.Certifications.Verify(c => c.RestoreCertificationStateAsync(record.PersonnelCertificationId, Dept, PersonnelCertificationStatuses.Active, previousExpiry, It.IsAny<string>(),
				It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once());
			h.Roles.Verify(r => r.SetRolesForUserAsync(Dept, U(0), It.Is<string[]>(ids => ids.SequenceEqual(new[] { "5" })), It.IsAny<CancellationToken>(), It.IsAny<string>()), Times.Once());
			h.EnforcementRows.Single().UndoReason.Should().Be((int)LmsEnforcementUndoReasons.Completed);
			h.EnforcementRows.Single().RestoredRolesJson.Should().Be("[5]", "the role the certification sweep took is recorded as given back");
			h.Notifier.Verify(n => n.NotifyEnforcementAsync(Dept, U(0), It.IsAny<LmsRequirement>(), null, true, It.IsAny<CancellationToken>()), Times.Once());
		}

		[Test]
		public async Task Undoing_an_enforcement_raises_undone_once_and_a_lost_race_raises_nothing()
		{
			// Round 7C, workflow trigger 264.
			var h = new Harness();
			var raised = new List<RequirementEnforcementWorkflowEvent>();
			h.Events.Setup(e => e.SendMessage(It.IsAny<RequirementEnforcementWorkflowEvent>())).Callback((RequirementEnforcementWorkflowEvent e) => raised.Add(e));
			h.Seed(10);
			h.EnforcementRows.Add(new LmsRequirementEnforcement
			{
				LmsRequirementEnforcementId = 1, DepartmentId = Dept, LmsRequirementId = 1, LmsRequirementMemberId = 10, UserId = U(0), PersonnelCertificationId = 1000,
				DepartmentCertificationTypeId = TypeId, PreviousStatus = (int)PersonnelCertificationStatuses.Active, DueOn = Now.AddDays(-30), ActedOn = Now.AddDays(-20)
			});
			h.Completed(0, Now.AddHours(-1));

			(await h.Build().UndoCompletedAsync(Dept, Now)).Should().Be(1);
			(await h.Build().UndoCompletedAsync(Dept, Now)).Should().Be(0, "nothing open any more");

			raised.Should().ContainSingle();
			var undone = raised.Single();
			undone.Trigger.Should().Be(WorkflowTriggerEventType.RequirementEnforcementUndone);
			(undone.EnforcementId, undone.RequirementId, undone.UserId, undone.UndoReason).Should().Be((1, 1, U(0), "Completed"));
			undone.ActorUserId.Should().BeNull("the system undid it");
			undone.EnforcedOn.Should().Be(Now.AddDays(-20));

			raised.Clear();
			h.EnforcementRows.Add(new LmsRequirementEnforcement
			{
				LmsRequirementEnforcementId = 2, DepartmentId = Dept, LmsRequirementId = 1, LmsRequirementMemberId = 11, UserId = U(1), PersonnelCertificationId = 1001,
				DepartmentCertificationTypeId = TypeId, PreviousStatus = (int)PersonnelCertificationStatuses.Active, DueOn = Now.AddDays(-30), ActedOn = Now.AddDays(-20)
			});
			h.Completed(1, Now.AddHours(-1));
			h.Enforcements.Setup(e => e.TryMarkUndoneAsync(Dept, It.IsAny<int>(), It.IsAny<LmsEnforcementUndoReasons>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(false);
			(await h.Build().UndoCompletedAsync(Dept, Now)).Should().Be(0);
			raised.Should().BeEmpty("another run undid it first");
		}

		[Test]
		public async Task An_action_whose_member_has_not_completed_stays_and_a_record_changed_since_is_not_touched()
		{
			var h = new Harness();
			h.Seed(3);
			h.EnforcementRows.Add(new LmsRequirementEnforcement
			{
				LmsRequirementEnforcementId = 1, DepartmentId = Dept, LmsRequirementId = 1, UserId = U(0), PersonnelCertificationId = 1000, DepartmentCertificationTypeId = TypeId,
				PreviousStatus = (int)PersonnelCertificationStatuses.Active, DueOn = Now.AddDays(-30), ActedOn = Now.AddDays(-20)
			});
			(await h.Build().UndoCompletedAsync(Dept, Now)).Should().Be(0);

			// An officer renewed the record meanwhile (Active again): undoing on completion leaves it as it is.
			h.Completed(0, Now.AddHours(-1));
			(await h.Build().UndoCompletedAsync(Dept, Now)).Should().Be(1);
			h.Certifications.Verify(c => c.RestoreCertificationStateAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<PersonnelCertificationStatuses>(), It.IsAny<DateTime?>(), It.IsAny<string>(),
				It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never());
		}

		[Test]
		public async Task A_lapse_pauses_the_engine_and_reverts_every_open_action()
		{
			var h = new Harness();
			h.Seed(3);
			h.Subscription = new LmsSubscriptionSnapshot { State = LmsSubscriptionStates.Lapsed, LapsedSinceUtc = Now.AddDays(-2) };
			var record = h.Records.First(r => r.UserId == U(1));
			record.Status = (int)PersonnelCertificationStatuses.Expired;
			record.StatusReason = "learning-requirement:1";
			h.EnforcementRows.Add(new LmsRequirementEnforcement
			{
				LmsRequirementEnforcementId = 1, DepartmentId = Dept, LmsRequirementId = 1, UserId = U(1), PersonnelCertificationId = record.PersonnelCertificationId, DepartmentCertificationTypeId = TypeId,
				PreviousStatus = (int)PersonnelCertificationStatuses.Active, PreviousExpiresOn = Now.AddMonths(3).Date, DueOn = Now.AddDays(-30), ActedOn = Now.AddDays(-20)
			});

			var result = await h.Build().RunDailyAsync(Dept, Now);

			result.Paused.Should().BeTrue();
			result.Undone.Should().Be(1);
			h.EnforcementRows.Single().UndoReason.Should().Be((int)LmsEnforcementUndoReasons.Lapse);
			h.AudienceService.Verify(a => a.ResolveAsync(It.IsAny<int>(), It.IsAny<IEnumerable<IContentAudienceRule>>()), Times.Never(), "nothing is reconciled while paused");
			h.Raised.Should().BeEmpty("no new overdue states during a lapse");
		}

		#endregion Enforcement and the blast-radius guard (R-L11)

		#region Gate discipline (R-L9)

		[Test]
		public async Task A_daily_pass_reads_billing_once()
		{
			var h = new Harness();
			h.Seed(10);
			await h.Build().RunDailyAsync(Dept, Now);
			h.Access.Verify(a => a.GetSubscriptionAsync(Dept), Times.Once());
			h.Access.Verify(a => a.CanBeTrainedAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never(), "never a billing read per member");
		}

		[Test]
		public async Task An_audience_change_in_a_department_without_a_live_requirement_asks_no_gate()
		{
			var h = new Harness();
			var result = await h.Build().OnAudienceChangesAsync(Dept, new[] { U(1) }, Now);
			result.Skipped.Should().BeTrue();
			h.Access.Verify(a => a.IsFlagEnabledAsync(It.IsAny<int>()), Times.Never());
			h.Access.Verify(a => a.GetSubscriptionAsync(It.IsAny<int>()), Times.Never());
		}

		#endregion Gate discipline (R-L9)

		#region Audience entry, onboarding and the matrix

		[Test]
		public async Task A_member_who_enters_the_audience_gets_a_row_due_from_entry_is_told_and_gets_the_onboarding_path()
		{
			var h = new Harness();
			var requirement = h.Seed(2, enforcing: false);
			requirement.Kind = (int)LmsRequirementKinds.Path;
			requirement.TargetRefId = 40;
			requirement.AutoAssign = true;
			requirement.IsOnboarding = true;
			h.Paths.Setup(p => p.GetCompletionFactsAsync(Dept, It.IsAny<IEnumerable<int>>())).ReturnsAsync(new List<LmsRequirementFact>());
			h.Paths.Setup(p => p.AssignAsync(It.IsAny<LmsActor>(), 40, It.IsAny<IEnumerable<IContentAudienceRule>>(), It.IsAny<DateTime?>(), 1, It.IsAny<CancellationToken>()))
				.ReturnsAsync(new LmsAssignOutcome { Created = 1 });
			h.PathsRepo.Setup(p => p.GetVisibleByIdAsync(Dept, 40)).ReturnsAsync(new LmsPath { LmsPathId = 40, DepartmentId = Dept, Name = "Engineer onboarding" });
			h.Paths.Setup(p => p.TitleOf(It.IsAny<LmsPath>(), It.IsAny<string>())).Returns("Engineer onboarding");
			var joiner = U(7);
			h.Active.Add(joiner);
			h.SeatRows.Add(new LmsMemberSeat { LmsMemberSeatId = 77, DepartmentId = Dept, UserId = joiner, EnabledOn = Now.AddDays(-1) });
			h.AudienceService.Setup(a => a.GetMemberFactsAsync(Dept, joiner)).ReturnsAsync(new ContentAudienceMemberFacts { UserId = joiner, IsActive = true, RoleIds = new HashSet<int> { RoleId } });

			var result = await h.Build().OnAudienceChangesAsync(Dept, new[] { joiner }, Now);

			result.MembersAdded.Should().Be(1);
			var row = h.MemberRows.Single(m => m.UserId == joiner);
			row.EnteredOn.Should().Be(Now);
			row.NotRequired.Should().BeFalse();
			h.Notifier.Verify(n => n.NotifyAssignedAsync(Dept, joiner, requirement, "Engineer onboarding", It.Is<DateTime?>(d => d.HasValue && d.Value > Now.AddDays(29)), It.IsAny<CancellationToken>()),
				Times.Once());
			h.Paths.Verify(p => p.AssignAsync(It.Is<LmsActor>(a => a.UserId == U(99)), 40, It.Is<IEnumerable<IContentAudienceRule>>(w => w.Single().RefId == joiner),
				It.Is<DateTime?>(d => d.HasValue), 1, It.IsAny<CancellationToken>()), Times.Once());
			h.Raised.Should().ContainSingle(e => e.Change == TrainingWorkflowChanges.Assigned && e.UserId == joiner && e.PathId == 40);
		}

		[Test]
		public async Task A_course_renewal_is_assigned_once_when_its_at_risk_window_opens_and_announced_against_the_next_due_date()
		{
			var h = new Harness();
			var requirement = h.Seed(1, enforcing: false);
			requirement.Recurrence = (int)LmsRequirementRecurrences.RollingMonths;
			requirement.IntervalMonths = 12;
			requirement.AutoAssign = true;
			h.MemberRows[0].State = (int)LmsRequirementStates.Compliant;
			h.Completed(0, Now.AddMonths(-12).AddDays(10));
			h.Learner.Setup(l => l.AssignAsync(It.IsAny<LmsActor>(), 31, It.IsAny<IEnumerable<IContentAudienceRule>>(), It.IsAny<DateTime?>(), LmsEnrollmentSources.Assigned, null,
				It.IsAny<CancellationToken>())).ReturnsAsync(new LmsAssignOutcome { Created = 1 });

			var result = await h.Build().RunDailyAsync(Dept, Now);

			result.Assigned.Should().Be(1);
			h.Learner.Verify(l => l.AssignAsync(It.IsAny<LmsActor>(), 31, It.Is<IEnumerable<IContentAudienceRule>>(w => w.Single().RefId == U(0)), It.Is<DateTime?>(d => d.HasValue && d.Value > Now),
				LmsEnrollmentSources.Assigned, null, It.IsAny<CancellationToken>()), Times.Once());
			h.Notifier.Verify(n => n.NotifyDueAsync(Dept, U(0), requirement, It.IsAny<string>(), LmsRequirementStates.AtRisk, It.Is<DateTime?>(d => d.HasValue && d.Value > Now),
				It.IsAny<CancellationToken>()), Times.Once());
			h.Raised.Should().ContainSingle(e => e.Change == TrainingWorkflowChanges.AtRisk && e.UserId == U(0));
		}

		[Test]
		public async Task A_new_members_only_requirement_marks_its_starting_audience_not_required()
		{
			var h = new Harness();
			h.Seed(0);
			h.RequirementRows.Clear();
			h.AudienceRows.Clear();
			h.Active.Add(U(1));
			h.RolesByUser[U(1)] = new List<PersonnelRole> { new PersonnelRole { PersonnelRoleId = RoleId, DepartmentId = Dept } };

			var saved = await h.Build().SaveRequirementAsync(Manager(), new LmsRequirementInput
			{
				Name = "Engineer onboarding", Kind = LmsRequirementKinds.Course, TargetRefId = 31, DueDays = 30, NewMembersOnly = true, IsOnboarding = true,
				Audience = new List<ContentAudienceRule> { new ContentAudienceRule(ContentAudienceKinds.Role, RoleId.ToString()) }
			});

			saved.Succeeded.Should().BeTrue();
			h.MemberRows.Single().NotRequired.Should().BeTrue("the member was already in the role when onboarding started");
			h.Notifier.Verify(n => n.NotifyAssignedAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<LmsRequirement>(), It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()),
				Times.Never());
		}

		[Test]
		public async Task The_matrix_shows_members_without_a_seat_as_not_licensed_and_leaves_inactive_members_out()
		{
			var h = new Harness();
			h.Seed(3, enforcing: false);
			h.SeatRows.RemoveAll(s => s.UserId == U(1));
			h.Active.Remove(U(2));
			h.Completed(0, Now.AddDays(-1));
			var scope = TrainingManagementScope.None(U(99), Dept);

			var matrix = await h.Build().GetMatrixAsync(Manager(), scope, new LmsMatrixFilter(), Now);

			matrix.Error.Should().Be(LmsErrors.None);
			matrix.Rows.Select(r => r.UserId).Should().BeEquivalentTo(new[] { U(0), U(1) }, "inactive members never count and never show");
			matrix.Rows.Single(r => r.UserId == U(0)).Cells[1].State.Should().Be(LmsRequirementStates.Compliant);
			matrix.Rows.Single(r => r.UserId == U(1)).Cells[1].State.Should().Be(LmsRequirementStates.NotLicensed, "a member without an LMS seat is not licensed, never overdue");
			matrix.Totals.Should().ContainKey(LmsRequirementStates.NotLicensed).And.NotContainKey(LmsRequirementStates.Overdue);
		}

		[Test]
		public async Task A_group_admin_sees_only_the_group_they_administer()
		{
			var h = new Harness();
			h.Seed(3, enforcing: false);
			var actor = Manager();
			actor.IsDepartmentAdmin = false;
			var scope = new TrainingManagementScope { UserId = U(99), DepartmentId = Dept, Assign = new TrainingRightScope { AdministeredMemberUserIds = new HashSet<string> { U(1) } } };

			var matrix = await h.Build().GetMatrixAsync(actor, scope, new LmsMatrixFilter(), Now);
			matrix.Rows.Select(r => r.UserId).Should().Equal(new[] { U(1) });

			var none = await h.Build().GetMatrixAsync(actor, TrainingManagementScope.None(U(99), Dept), new LmsMatrixFilter(), Now);
			none.Error.Should().Be(LmsErrors.NotAuthorized);
		}

		#endregion Audience entry, onboarding and the matrix

		#region Workflow payloads (224-229)

		[Test]
		public void Workflow_payloads_carry_ids_names_of_the_training_and_dates_never_contact_data()
		{
			var requirement = new LmsRequirement { LmsRequirementId = 3, DepartmentId = Dept, Name = "SCBA refresher", Kind = (int)LmsRequirementKinds.Course, TargetRefId = 31 };
			var status = new LmsRequirementStatus { State = LmsRequirementStates.Overdue, EffectiveDueUtc = Now.AddDays(-8), LastCompletedUtc = Now.AddYears(-1) };
			var payload = LmsRequirementService.BuildEvent(requirement, U(4), TrainingWorkflowChanges.Overdue, status, "SCBA refresher 2026", Now);

			payload.Trigger.Should().Be(WorkflowTriggerEventType.TrainingOverdue);
			payload.Days.Should().Be(8);
			var names = JObject.Parse(JsonConvert.SerializeObject(payload)).Properties().Select(p => p.Name).ToList();
			names.Should().NotContain(n => n.Contains("Email", StringComparison.OrdinalIgnoreCase) || n.Contains("Phone", StringComparison.OrdinalIgnoreCase) ||
				n.Contains("Mobile", StringComparison.OrdinalIgnoreCase) || n.Contains("FirstName", StringComparison.OrdinalIgnoreCase) || n.Contains("LastName", StringComparison.OrdinalIgnoreCase) ||
				n.Contains("Address", StringComparison.OrdinalIgnoreCase));
			typeof(TrainingWorkflowEvent).GetProperties().Select(p => p.Name).Should().NotContain(new[] { "Email", "PhoneNumber", "MobileNumber", "MemberName", "FullName" });

			foreach (var change in Enum.GetValues<TrainingWorkflowChanges>())
				((int)new TrainingWorkflowEvent { Change = change }.Trigger).Should().BeInRange(224, 229);
		}

		#endregion Workflow payloads (224-229)
	
		#region Round 10 A7 (LMS seats)

		[Test]
		public async Task A_requirement_never_assigns_or_announces_to_a_member_without_an_lms_seat_and_hands_its_licensed_members_on()
		{
			var h = new Harness();
			var requirement = h.Seed(2, enforcing: false);
			requirement.Kind = (int)LmsRequirementKinds.Path;
			requirement.TargetRefId = 40;
			requirement.AutoAssign = true;
			requirement.IsOnboarding = true;
			h.Paths.Setup(p => p.GetCompletionFactsAsync(Dept, It.IsAny<IEnumerable<int>>())).ReturnsAsync(new List<LmsRequirementFact>());
			h.Paths.Setup(p => p.AssignAsync(It.IsAny<LmsActor>(), 40, It.IsAny<IEnumerable<IContentAudienceRule>>(), It.IsAny<DateTime?>(), 1, It.IsAny<CancellationToken>()))
				.ReturnsAsync(new LmsAssignOutcome { Created = 1 });
			h.PathsRepo.Setup(p => p.GetVisibleByIdAsync(Dept, 40)).ReturnsAsync(new LmsPath { LmsPathId = 40, DepartmentId = Dept, Name = "Engineer onboarding" });
			h.Paths.Setup(p => p.TitleOf(It.IsAny<LmsPath>(), It.IsAny<string>())).Returns("Engineer onboarding");
			var seated = U(7);
			var unseated = U(8);
			foreach (var joiner in new[] { seated, unseated })
			{
				h.Active.Add(joiner);
				h.AudienceService.Setup(a => a.GetMemberFactsAsync(Dept, joiner)).ReturnsAsync(new ContentAudienceMemberFacts { UserId = joiner, IsActive = true, RoleIds = new HashSet<int> { RoleId } });
			}
			h.SeatRows.Add(new LmsMemberSeat { LmsMemberSeatId = 77, DepartmentId = Dept, UserId = seated, EnabledOn = Now.AddDays(-1) });

			await h.Build().OnAudienceChangesAsync(Dept, new[] { seated, unseated }, Now);

			h.Paths.Verify(p => p.AssignAsync(It.IsAny<LmsActor>(), 40, It.Is<IEnumerable<IContentAudienceRule>>(w => w.Any(r => r.RefId == unseated)), It.IsAny<DateTime?>(), 1,
				It.IsAny<CancellationToken>()), Times.Never(), "§17.4: a member without a seat cannot be enrolled in a path");
			h.Notifier.Verify(n => n.NotifyAssignedAsync(Dept, unseated, It.IsAny<LmsRequirement>(), It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()), Times.Never());
			h.Paths.Verify(p => p.AssignAsync(It.Is<LmsActor>(a => a.SeatHolders != null && a.SeatHolders.Contains(seated) && !a.SeatHolders.Contains(unseated)), 40,
				It.Is<IEnumerable<IContentAudienceRule>>(w => w.Single().RefId == seated), It.IsAny<DateTime?>(), 1, It.IsAny<CancellationToken>()), Times.Once(),
				"the licensed members the run read stand in for the seat holders, so the assignment reads no seats again (R-L9)");
		}

		#endregion Round 10 A7 (LMS seats)
}
}
