using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// What the training notifier sends each assignee (T-D9, T-D19, T0 item 4). The notifier sent nothing for years, so old
	/// rows still look unannounced: these pin who is told, that an old assignment is never announced as new, and that
	/// reminders are "due within the next N hours and not yet reminded" rather than a 10:00 window.
	/// </summary>
	[TestFixture]
	public class TrainingNoticePlannerTests
	{
		private static readonly DateTime Now = new DateTime(2026, 11, 24, 18, 0, 0, DateTimeKind.Utc);

		private static Training NewTraining(DateTime createdOn, DateTime? notified = null) => new Training
		{
			TrainingId = 1,
			DepartmentId = 10,
			Name = "Hose Testing",
			CreatedOn = createdOn,
			Notified = notified,
			Users = new List<TrainingUser>()
		};

		private static TrainingUser Assignee(string userId, DateTime? assignedOn = null, DateTime? notifiedOn = null, bool complete = false) =>
			new TrainingUser { UserId = userId, AssignedOn = assignedOn ?? Now.AddHours(-1), NotifiedOn = notifiedOn, Complete = complete };

		private static TrainingNoticePlan Plan(Training training, TrainingUser assignment, DateTime? due, bool active = true, bool onLeave = false, string dueText = null) =>
			TrainingNoticePlanner.PlanAssignment(training, assignment, due, active, onLeave, dueText, Now);

		[Test]
		public void A_new_assignment_is_announced()
		{
			var plan = Plan(NewTraining(Now.AddHours(-1)), Assignee("a"), null);

			plan.Kind.Should().Be(TrainingNoticeKinds.NewTraining);
			plan.Title.Should().Be(TrainingNoticePlanner.NewTrainingTitle);
			plan.Message.Should().Be("New Training (Hose Testing) assigned to you");
		}

		[Test]
		public void A_new_assignment_with_a_due_date_names_it_and_the_custom_message_follows()
		{
			var training = NewTraining(Now.AddHours(-1));
			training.ReminderMessage = "Bring your SCBA.";

			var plan = Plan(training, Assignee("a"), Now.AddDays(5), dueText: "11/29/2026");

			plan.Message.Should().Be("New Training (Hose Testing) due on 11/29/2026 Bring your SCBA.");
		}

		[Test]
		public void An_old_assignment_that_was_never_announced_is_only_marked()
		{
			var training = NewTraining(Now.AddDays(-30));
			var plan = Plan(training, Assignee("a", assignedOn: Now.AddDays(-(TrainingNoticePlanner.NewNoticeMaxAgeDays + 1))), null);

			plan.Kind.Should().Be(TrainingNoticeKinds.None);
			plan.MarkNotifiedOnly.Should().BeTrue();
		}

		[Test]
		public void A_scheduled_training_is_not_announced_before_it_starts_and_is_announced_once_it_does()
		{
			var training = NewTraining(Now.AddDays(-30));
			training.StartsOn = Now.AddDays(2);

			Plan(training, Assignee("a", assignedOn: Now.AddDays(-30)), null).Kind.Should().Be(TrainingNoticeKinds.None);

			training.StartsOn = Now.AddHours(-2);
			Plan(training, Assignee("a", assignedOn: Now.AddDays(-30)), null).Kind.Should().Be(TrainingNoticeKinds.NewTraining);
		}

		[Test]
		public void Members_who_completed_are_exempt_or_removed_are_not_told()
		{
			var training = NewTraining(Now);

			Plan(training, Assignee("done", complete: true), null).Kind.Should().Be(TrainingNoticeKinds.None);
			var exempt = Assignee("exempt");
			exempt.ExemptOn = Now.AddDays(-1);
			Plan(training, exempt, null).Kind.Should().Be(TrainingNoticeKinds.None);
			var removed = Assignee("removed");
			removed.RemovedOn = Now.AddDays(-1);
			Plan(training, removed, null).Kind.Should().Be(TrainingNoticeKinds.None);
		}

		[Test]
		public void Members_who_are_no_longer_active_are_not_told()
		{
			var plan = Plan(NewTraining(Now), Assignee("gone"), null, active: false);

			plan.Kind.Should().Be(TrainingNoticeKinds.None);
			plan.MarkNotifiedOnly.Should().BeTrue();
			Plan(NewTraining(Now), Assignee("gone", notifiedOn: Now.AddDays(-2)), Now.AddHours(3), active: false).Kind.Should().Be(TrainingNoticeKinds.None);
		}

		[Test]
		public void A_reminder_goes_when_the_due_moment_is_within_the_lead_and_not_yet_reminded()
		{
			var training = NewTraining(Now.AddDays(-10), Now.AddDays(-10));
			var assignment = Assignee("a", notifiedOn: Now.AddDays(-10));

			// Due in 30 hours: outside the default one-day lead.
			Plan(training, assignment, Now.AddHours(30)).Kind.Should().Be(TrainingNoticeKinds.None);

			// Due in 20 hours: inside it, at any minute of the day (no 10:00 window).
			var plan = Plan(training, assignment, Now.AddHours(20));
			plan.Kind.Should().Be(TrainingNoticeKinds.DueReminder);
			plan.LeadDays.Should().Be(1);
			plan.Title.Should().Be(TrainingNoticePlanner.DueReminderTitle);

			// Reminded for that lead: not again.
			assignment.LastReminderLeadDays = 1;
			Plan(training, assignment, Now.AddHours(10)).Kind.Should().Be(TrainingNoticeKinds.None);
		}

		[Test]
		public void Each_configured_lead_is_reminded_once_and_a_late_start_skips_the_wider_ones()
		{
			var training = NewTraining(Now.AddDays(-20), Now.AddDays(-20));
			training.ReminderLeadDaysCsv = "7,1";
			var assignment = Assignee("a", notifiedOn: Now.AddDays(-20));

			var week = Plan(training, assignment, Now.AddDays(5));
			week.Kind.Should().Be(TrainingNoticeKinds.DueReminder);
			week.LeadDays.Should().Be(7);
			assignment.LastReminderLeadDays = 7;
			Plan(training, assignment, Now.AddDays(4)).Kind.Should().Be(TrainingNoticeKinds.None);
			Plan(training, assignment, Now.AddHours(12)).LeadDays.Should().Be(1);

			// Nothing reminded yet and already inside one day: only the one-day reminder goes.
			Plan(training, Assignee("b", notifiedOn: Now.AddDays(-20)), Now.AddHours(12)).LeadDays.Should().Be(1);
		}

		[Test]
		public void An_overdue_notice_goes_once_or_on_the_repeating_cadence_and_only_when_switched_on()
		{
			var training = NewTraining(Now.AddDays(-20), Now.AddDays(-20));
			var assignment = Assignee("a", notifiedOn: Now.AddDays(-20));

			Plan(training, assignment, Now.AddDays(-1)).Kind.Should().Be(TrainingNoticeKinds.None);

			training.SendOverdueNotices = true;
			Plan(training, assignment, Now.AddDays(-1)).Kind.Should().Be(TrainingNoticeKinds.Overdue);

			assignment.OverdueNotifiedOn = Now.AddDays(-3);
			Plan(training, assignment, Now.AddDays(-5)).Kind.Should().Be(TrainingNoticeKinds.None);

			training.OverdueRepeatDays = 7;
			Plan(training, assignment, Now.AddDays(-5)).Kind.Should().Be(TrainingNoticeKinds.None);
			assignment.OverdueNotifiedOn = Now.AddDays(-8);
			Plan(training, assignment, Now.AddDays(-9)).Kind.Should().Be(TrainingNoticeKinds.Overdue);
		}

		[Test]
		public void A_member_on_leave_gets_no_reminder()
		{
			var training = NewTraining(Now.AddDays(-20), Now.AddDays(-20));
			training.SendOverdueNotices = true;

			Plan(training, Assignee("a", notifiedOn: Now.AddDays(-20)), Now.AddHours(5), onLeave: true).Kind.Should().Be(TrainingNoticeKinds.None);
			Plan(training, Assignee("a", notifiedOn: Now.AddDays(-20)), Now.AddDays(-2), onLeave: true).Kind.Should().Be(TrainingNoticeKinds.None);
		}

		[Test]
		public void Nothing_is_planned_without_a_training_or_an_assignee()
		{
			Plan(null, Assignee("a"), null).Kind.Should().Be(TrainingNoticeKinds.None);
			Plan(NewTraining(Now), null, null).Kind.Should().Be(TrainingNoticeKinds.None);
			var archived = NewTraining(Now);
			archived.ArchivedOn = Now.AddHours(-1);
			Plan(archived, Assignee("a"), null).Kind.Should().Be(TrainingNoticeKinds.None);
		}

		[Test]
		public void A_training_never_announced_and_older_than_the_window_is_stale()
		{
			TrainingNoticePlanner.IsStaleUnannounced(NewTraining(Now.AddDays(-(TrainingNoticePlanner.NewNoticeMaxAgeDays + 1))), Now).Should().BeTrue();
			TrainingNoticePlanner.IsStaleUnannounced(NewTraining(Now.AddDays(-1)), Now).Should().BeFalse();
		}
	}

	/// <summary>
	/// The notifier's service side: one query over every department finds the trainings with notice work (no load of every
	/// training and no 10:00 window, T-D19), each comes back with its assignees, and marking a training touches the row alone.
	/// </summary>
	[TestFixture]
	public class TrainingNotifierServiceTests
	{
		private Mock<ITrainingRepository> _trainings;
		private TrainingService _service;

		[SetUp]
		public void SetUp()
		{
			_trainings = new Mock<ITrainingRepository>();
			_service = new TrainingService(_trainings.Object,
				Mock.Of<ITrainingAttachmentRepository>(),
				Mock.Of<ITrainingUserRepository>(),
				Mock.Of<ITrainingQuestionRepository>(),
				Mock.Of<ICommunicationService>(),
				Mock.Of<IDepartmentsService>(),
				Mock.Of<IDepartmentSettingsService>(),
				Mock.Of<IKnowledgeIndexQueue>(), Mock.Of<IUserProfileService>());
		}

		[Test]
		public async Task Trainings_with_notice_work_come_back_with_their_assignees()
		{
			var now = DateTime.UtcNow;
			_trainings.Setup(x => x.GetTrainingIdsWithNoticeWorkAsync(now, TrainingService.MaxReminderLeadDays)).ReturnsAsync(new List<int> { 5 });
			_trainings.Setup(x => x.GetTrainingByTrainingIdAsync(5)).ReturnsAsync(new Training
			{
				TrainingId = 5,
				DepartmentId = 10,
				Users = new List<TrainingUser> { new TrainingUser { UserId = "a" }, new TrainingUser { UserId = "b" } }
			});

			var result = await _service.GetTrainingsToNotifyAsync(now);

			result.Should().ContainSingle();
			result[0].Users.Select(x => x.UserId).Should().BeEquivalentTo(new[] { "a", "b" });
		}

		[Test]
		public async Task The_old_load_everything_sweep_is_gone()
		{
			var now = DateTime.UtcNow;
			_trainings.Setup(x => x.GetTrainingIdsWithNoticeWorkAsync(It.IsAny<DateTime>(), It.IsAny<int>())).ReturnsAsync(new List<int>());

			(await _service.GetTrainingsToNotifyAsync(now)).Should().BeEmpty();

			_trainings.Verify(x => x.GetAllAsync(), Times.Never);
			_trainings.Verify(x => x.GetTrainingByTrainingIdAsync(It.IsAny<int>()), Times.Never);
		}

		[Test]
		public async Task A_training_that_could_not_be_read_is_left_for_a_later_run()
		{
			var now = DateTime.UtcNow;
			_trainings.Setup(x => x.GetTrainingIdsWithNoticeWorkAsync(It.IsAny<DateTime>(), It.IsAny<int>())).ReturnsAsync(new List<int> { 5, 9, 9 });
			// The repository answers null when the read throws: returned empty it would be marked notified with nobody told.
			_trainings.Setup(x => x.GetTrainingByTrainingIdAsync(5)).ReturnsAsync((Training)null);
			_trainings.Setup(x => x.GetTrainingByTrainingIdAsync(9)).ReturnsAsync(new Training
			{
				TrainingId = 9,
				DepartmentId = 10,
				Users = new List<TrainingUser> { new TrainingUser { UserId = "a" } }
			});

			var result = await _service.GetTrainingsToNotifyAsync(now);

			result.Select(x => x.TrainingId).Should().Equal(9);
		}

		[Test]
		public async Task A_training_read_with_no_assignees_comes_back_with_an_empty_list()
		{
			var now = DateTime.UtcNow;
			_trainings.Setup(x => x.GetTrainingIdsWithNoticeWorkAsync(It.IsAny<DateTime>(), It.IsAny<int>())).ReturnsAsync(new List<int> { 5 });
			_trainings.Setup(x => x.GetTrainingByTrainingIdAsync(5)).ReturnsAsync(new Training { TrainingId = 5, DepartmentId = 10 });

			var result = await _service.GetTrainingsToNotifyAsync(now);

			result.Should().ContainSingle(x => x.TrainingId == 5).Which.Users.Should().BeEmpty();
		}

		[Test]
		public async Task Marking_a_training_notified_saves_the_training_row_alone()
		{
			var training = new Training { TrainingId = 7, DepartmentId = 10, Users = new List<TrainingUser> { new TrainingUser { UserId = "a" } } };
			_trainings.Setup(x => x.GetTrainingByTrainingIdAsync(7)).ReturnsAsync(training);
			_trainings.Setup(x => x.SaveOrUpdateAsync(training, It.IsAny<CancellationToken>(), true)).ReturnsAsync(training);

			var saved = await _service.MarkAsNotifiedAsync(7);

			saved.Notified.Should().NotBeNull();
			_trainings.Verify(x => x.SaveOrUpdateAsync(training, It.IsAny<CancellationToken>(), true), Times.Once);
			_trainings.Verify(x => x.SaveOrUpdateAsync(It.IsAny<Training>(), It.IsAny<CancellationToken>(), false), Times.Never);
		}

		[Test]
		public async Task Marking_a_training_that_is_gone_does_nothing()
		{
			_trainings.Setup(x => x.GetTrainingByTrainingIdAsync(8)).ReturnsAsync((Training)null);

			(await _service.MarkAsNotifiedAsync(8)).Should().BeNull();
		}
	}

	/// <summary>Which trainings a member manages and opens, given what the scope says about them.</summary>
	[TestFixture]
	public class TrainingManagementScopeTests
	{
		private const int DepartmentId = 10;

		private static Training TrainingFor(string createdBy, params string[] assignees) => new Training
		{
			TrainingId = 1,
			DepartmentId = DepartmentId,
			CreatedByUserId = createdBy,
			Users = assignees.Select(x => new TrainingUser { UserId = x }).ToList()
		};

		[Test]
		public void A_member_with_no_training_rights_opens_only_what_is_assigned_to_them()
		{
			var scope = TrainingManagementScope.None("me", DepartmentId);

			scope.CanOpen(TrainingFor("officer", "me", "other")).Should().BeTrue();
			scope.CanOpen(TrainingFor("officer", "other")).Should().BeFalse();
			scope.CanManage(TrainingFor("officer", "me")).Should().BeFalse();
		}

		[Test]
		public void A_member_who_may_create_trainings_manages_their_own_and_not_everyone_elses()
		{
			var scope = new TrainingManagementScope { UserId = "me", DepartmentId = DepartmentId, ManagesOwn = true };

			scope.CanManage(TrainingFor("ME", "other")).Should().BeTrue();
			scope.CanManage(TrainingFor("officer", "other")).Should().BeFalse();
			scope.CanOpen(TrainingFor("officer", "other")).Should().BeFalse();
		}

		[Test]
		public void A_group_admin_manages_a_training_assigned_only_to_their_own_group()
		{
			var scope = new TrainingManagementScope
			{
				UserId = "captain",
				DepartmentId = DepartmentId,
				ManagesOwn = true,
				AdministeredMemberUserIds = new HashSet<string>(new[] { "captain", "ff1", "ff2" }, StringComparer.OrdinalIgnoreCase)
			};

			scope.CanManage(TrainingFor("officer", "ff1", "FF2")).Should().BeTrue();
			scope.CanManage(TrainingFor("officer", "ff1", "other-station")).Should().BeFalse();
			scope.CanManage(TrainingFor("officer")).Should().BeFalse("a training with no assignees is nobody's group's");
		}

		[Test]
		public void A_training_open_to_all_members_is_read_by_any_member_but_its_quiz_and_management_are_not_opened()
		{
			var scope = TrainingManagementScope.None("me", DepartmentId);
			var open = TrainingFor("officer", "other");
			open.OpenToAllMembers = true;

			scope.CanOpen(open).Should().BeTrue();
			scope.CanOpenQuiz(open).Should().BeFalse("the quiz is answered by assignees; nobody else's answers are recorded");
			scope.CanManage(open).Should().BeFalse();
		}

		[Test]
		public void A_training_open_to_all_members_is_not_open_to_another_department()
		{
			var scope = TrainingManagementScope.None("me", DepartmentId);
			var foreign = TrainingFor("officer", "other");
			foreign.OpenToAllMembers = true;
			foreign.DepartmentId = 99;

			scope.CanOpen(foreign).Should().BeFalse();
		}

		[Test]
		public void An_assignee_and_a_manager_may_open_the_quiz()
		{
			TrainingManagementScope.None("me", DepartmentId).CanOpenQuiz(TrainingFor("officer", "me")).Should().BeTrue();
			new TrainingManagementScope { UserId = "chief", DepartmentId = DepartmentId, ManagesAll = true }
				.CanOpenQuiz(TrainingFor("officer", "me")).Should().BeTrue();
		}

		[Test]
		public void Whoever_manages_everything_manages_every_training_of_the_department_only()
		{
			var scope = new TrainingManagementScope { UserId = "chief", DepartmentId = DepartmentId, ManagesAll = true };
			var foreign = TrainingFor("x", "chief");
			foreign.DepartmentId = 99;

			scope.CanManage(TrainingFor("officer", "other")).Should().BeTrue();
			scope.CanManage(foreign).Should().BeFalse();
			scope.CanOpen(foreign).Should().BeFalse();
		}

		[Test]
		public void An_assignee_is_matched_without_regard_to_the_casing_of_their_id()
		{
			TrainingManagementScope.None("ME", DepartmentId).IsAssigned(TrainingFor("officer", "me")).Should().BeTrue();
		}

		/// <summary>
		/// The trainings list decides on its own which button an assignee gets. It must find the assignee the way the scope
		/// does: an id stored in another casing matched nothing, and the member was left with no way to open the training.
		/// </summary>
		[Test]
		public void The_trainings_list_matches_assignee_ids_the_way_the_scope_does()
		{
			var directory = new System.IO.DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (directory != null && !System.IO.File.Exists(System.IO.Path.Combine(directory.FullName, "Resgrid.sln")))
				directory = directory.Parent;

			var view = System.IO.File.ReadAllText(System.IO.Path.Combine(directory.FullName, "Web", "Resgrid.Web", "Areas", "User", "Views", "Trainings", "Index.cshtml"));

			// Any comparison of an assignee id in the list is the scope's (or a case-insensitive one), never a plain ==.
			view.Should().NotContain("x.UserId == currentUserId");
			System.Text.RegularExpressions.Regex.IsMatch(view, @"\.UserId\s*==").Should().BeFalse();
		}
	}

	/// <summary>
	/// "Everyone" on the Create Training permission lets every member create a training, and (Assign and Delete following the
	/// Create row) hands them the assign and delete claims too; what those claims reach is only the member's own trainings,
	/// decided by the scope, never everyone else's.
	/// </summary>
	[TestFixture]
	public class TrainingClaimsTests
	{
		private static ClaimsIdentity ClaimsFor(PermissionActions action, bool isAdmin, bool isGroupAdmin = false)
		{
			var identity = new ClaimsIdentity();
			var permissions = new List<Permission> { new Permission { PermissionType = (int)PermissionTypes.CreateTraining, Action = (int)action } };

			ClaimsLogic.AddTrainingClaims(identity, isAdmin, permissions, isGroupAdmin, new List<PersonnelRole>());

			return identity;
		}

		private static bool Has(ClaimsIdentity identity, string action) => identity.HasClaim(ResgridClaimTypes.Resources.Training, action);

		[Test]
		public void Under_everyone_a_member_holds_the_claims_but_the_scope_limits_them_to_their_own_trainings()
		{
			var identity = ClaimsFor(PermissionActions.Everyone, isAdmin: false);

			Has(identity, ResgridClaimTypes.Actions.View).Should().BeTrue();
			Has(identity, ResgridClaimTypes.Actions.Create).Should().BeTrue();
			Has(identity, ResgridClaimTypes.Actions.Update).Should().BeTrue();
			Has(identity, ResgridClaimTypes.Actions.Delete).Should().BeTrue();

			// The scope an Everyone member gets (GetTrainingManagementScopeAsync): own only, for every right.
			var own = new TrainingRightScope { ManagesOwn = true };
			var scope = new TrainingManagementScope { UserId = "member", DepartmentId = 10, Author = own, Assign = own, Delete = own };
			var mine = new Training { TrainingId = 1, DepartmentId = 10, CreatedByUserId = "MEMBER", Users = new List<TrainingUser>() };
			var theirs = new Training { TrainingId = 2, DepartmentId = 10, CreatedByUserId = "officer", Users = new List<TrainingUser> { new TrainingUser { UserId = "ff1" } } };

			scope.CanAuthor(mine).Should().BeTrue();
			scope.CanAssign(mine).Should().BeTrue();
			scope.CanDelete(mine).Should().BeTrue();
			scope.CanAuthor(theirs).Should().BeFalse();
			scope.CanAssign(theirs).Should().BeFalse();
			scope.CanDelete(theirs).Should().BeFalse();
		}

		[Test]
		public void Under_everyone_an_admin_keeps_every_training_claim()
		{
			var identity = ClaimsFor(PermissionActions.Everyone, isAdmin: true);

			Has(identity, ResgridClaimTypes.Actions.Create).Should().BeTrue();
			Has(identity, ResgridClaimTypes.Actions.Update).Should().BeTrue();
			Has(identity, ResgridClaimTypes.Actions.Delete).Should().BeTrue();
		}
	}

	namespace AuthorizationServiceTests
	{
		/// <summary>How the scope is built from the department's Create Training permission.</summary>
		[TestFixture]
		public class when_building_the_training_management_scope : with_the_authorization_service
		{
			private const int DepartmentId = 1;
			private const string Admin = "admin-user";
			private const string Member = "member-user";

			[SetUp]
			public void Setup()
			{
				_departmentsServiceMock.Setup(m => m.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>()))
					.ReturnsAsync(CreateDepartmentWithAdmins(DepartmentId, "managing-user", Admin));
			}

			private void PermissionIs(PermissionActions action, string data = null)
			{
				_permissionsServiceMock.Setup(m => m.GetPermissionByDepartmentTypeAsync(DepartmentId, PermissionTypes.CreateTraining))
					.ReturnsAsync(new Permission { PermissionType = (int)PermissionTypes.CreateTraining, Action = (int)action, Data = data });
			}

			[Test]
			public async Task a_department_admin_manages_everything()
			{
				var scope = await _authorizationService.GetTrainingManagementScopeAsync(Admin, DepartmentId);

				scope.ManagesAll.Should().BeTrue();
			}

			[Test]
			public async Task without_a_permission_row_a_member_manages_nothing()
			{
				_permissionsServiceMock.Setup(m => m.GetPermissionByDepartmentTypeAsync(DepartmentId, PermissionTypes.CreateTraining)).ReturnsAsync((Permission)null);

				var scope = await _authorizationService.GetTrainingManagementScopeAsync(Member, DepartmentId);

				scope.ManagesAll.Should().BeFalse();
				scope.ManagesOwn.Should().BeFalse();
				scope.AdministeredMemberUserIds.Should().BeNull();
			}

			[Test]
			public async Task under_admins_only_a_member_manages_nothing()
			{
				PermissionIs(PermissionActions.DepartmentAdminsOnly);

				var scope = await _authorizationService.GetTrainingManagementScopeAsync(Member, DepartmentId);

				scope.ManagesAll.Should().BeFalse();
				scope.ManagesOwn.Should().BeFalse();
			}

			[Test]
			public async Task under_everyone_a_member_manages_only_their_own()
			{
				PermissionIs(PermissionActions.Everyone);

				var scope = await _authorizationService.GetTrainingManagementScopeAsync(Member, DepartmentId);

				scope.ManagesAll.Should().BeFalse();
				scope.ManagesOwn.Should().BeTrue();
				scope.AdministeredMemberUserIds.Should().BeNull();
			}

			[Test]
			public async Task a_member_of_a_named_role_manages_everything()
			{
				PermissionIs(PermissionActions.DepartmentAdminsAndSelectRoles, "4");
				var roles = new List<PersonnelRole> { new PersonnelRole { PersonnelRoleId = 4 } };
				_personnelRolesServiceMock.Setup(m => m.GetRolesForUserAsync(Member, DepartmentId)).ReturnsAsync(roles);
				_permissionsServiceMock.Setup(m => m.IsUserAllowed(It.IsAny<Permission>(), false, false, roles)).Returns(true);

				var scope = await _authorizationService.GetTrainingManagementScopeAsync(Member, DepartmentId);

				scope.ManagesAll.Should().BeTrue();
			}

			[Test]
			public async Task a_member_outside_the_named_roles_manages_nothing()
			{
				PermissionIs(PermissionActions.DepartmentAdminsAndSelectRoles, "4");
				_personnelRolesServiceMock.Setup(m => m.GetRolesForUserAsync(Member, DepartmentId)).ReturnsAsync(new List<PersonnelRole>());
				_permissionsServiceMock.Setup(m => m.IsUserAllowed(It.IsAny<Permission>(), false, false, It.IsAny<List<PersonnelRole>>())).Returns(false);

				var scope = await _authorizationService.GetTrainingManagementScopeAsync(Member, DepartmentId);

				scope.ManagesAll.Should().BeFalse();
				scope.ManagesOwn.Should().BeFalse();
			}

			[Test]
			public async Task a_group_admin_is_scoped_to_the_members_of_their_group()
			{
				PermissionIs(PermissionActions.DepartmentAndGroupAdmins);
				_departmentGroupsServiceMock.Setup(m => m.GetGroupForUserAsync(Member, DepartmentId)).ReturnsAsync(new DepartmentGroup
				{
					DepartmentGroupId = 3,
					DepartmentId = DepartmentId,
					Members = new List<DepartmentGroupMember>
					{
						new DepartmentGroupMember { UserId = Member, IsAdmin = true },
						new DepartmentGroupMember { UserId = "ff1" }
					}
				});

				var scope = await _authorizationService.GetTrainingManagementScopeAsync(Member, DepartmentId);

				scope.ManagesAll.Should().BeFalse();
				scope.ManagesOwn.Should().BeTrue();
				scope.AdministeredMemberUserIds.Should().BeEquivalentTo(new[] { Member, "ff1" });
			}

			[Test]
			public async Task a_plain_group_member_is_not_scoped_to_anything()
			{
				PermissionIs(PermissionActions.DepartmentAndGroupAdmins);
				_departmentGroupsServiceMock.Setup(m => m.GetGroupForUserAsync(Member, DepartmentId)).ReturnsAsync(new DepartmentGroup
				{
					DepartmentGroupId = 3,
					DepartmentId = DepartmentId,
					Members = new List<DepartmentGroupMember> { new DepartmentGroupMember { UserId = Member } }
				});

				var scope = await _authorizationService.GetTrainingManagementScopeAsync(Member, DepartmentId);

				scope.ManagesOwn.Should().BeFalse();
				scope.AdministeredMemberUserIds.Should().BeNull();
			}
		}
	}
}
