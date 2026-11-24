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
	/// What the training notifier sends. The notifier sent nothing for years, so every stored training looks unannounced;
	/// these pin who is told and that an old training is never announced as new.
	/// </summary>
	[TestFixture]
	public class TrainingNoticePlannerTests
	{
		private static readonly DateTime Now = new DateTime(2026, 11, 24, 18, 0, 0, DateTimeKind.Utc);

		private static Training NewTraining(DateTime createdOn, DateTime? notified, params TrainingUser[] users) => new Training
		{
			TrainingId = 1,
			DepartmentId = 10,
			Name = "Hose Testing",
			CreatedOn = createdOn,
			Notified = notified,
			Users = users.ToList()
		};

		private static TrainingUser Assignee(string userId, bool complete = false) => new TrainingUser { UserId = userId, Complete = complete };

		[Test]
		public void A_new_training_is_announced_to_its_assignees()
		{
			var plan = TrainingNoticePlanner.Plan(NewTraining(Now.AddHours(-1), null, Assignee("a"), Assignee("b")), new[] { "a", "b" }, Now);

			plan.Kind.Should().Be(TrainingNoticeKinds.NewTraining);
			plan.Title.Should().Be(TrainingNoticePlanner.NewTrainingTitle);
			plan.Message.Should().Be("New Training (Hose Testing) assigned to you");
			plan.RecipientUserIds.Should().BeEquivalentTo(new[] { "a", "b" });
		}

		[Test]
		public void An_old_training_that_was_never_announced_is_not_announced_now()
		{
			var training = NewTraining(Now.AddDays(-(TrainingNoticePlanner.NewNoticeMaxAgeDays + 1)), null, Assignee("a"));

			TrainingNoticePlanner.IsStaleUnannounced(training, Now).Should().BeTrue();
			TrainingNoticePlanner.Plan(training, new[] { "a" }, Now).Kind.Should().Be(TrainingNoticeKinds.None);
		}

		[Test]
		public void An_announced_training_gets_the_due_tomorrow_reminder()
		{
			var training = NewTraining(Now.AddDays(-30), Now.AddDays(-30), Assignee("a"));
			training.ToBeCompletedBy = Now.AddDays(1);

			var plan = TrainingNoticePlanner.Plan(training, new[] { "a" }, Now);

			plan.Kind.Should().Be(TrainingNoticeKinds.DueTomorrow);
			plan.Title.Should().Be(TrainingNoticePlanner.DueTomorrowTitle);
			plan.Message.Should().Be("Training (Hose Testing) is due tomorrow");
		}

		[Test]
		public void Members_who_completed_it_are_not_told()
		{
			var plan = TrainingNoticePlanner.Plan(NewTraining(Now, null, Assignee("done", complete: true), Assignee("open")), new[] { "done", "open" }, Now);

			plan.RecipientUserIds.Should().Equal("open");
		}

		[Test]
		public void Members_who_are_no_longer_active_are_not_told()
		{
			var plan = TrainingNoticePlanner.Plan(NewTraining(Now, null, Assignee("gone"), Assignee("HERE")), new[] { "here" }, Now);

			plan.RecipientUserIds.Should().Equal("HERE");
		}

		[Test]
		public void Nothing_is_sent_when_nobody_is_left_to_tell()
		{
			TrainingNoticePlanner.Plan(NewTraining(Now, null), new[] { "a" }, Now).Kind.Should().Be(TrainingNoticeKinds.None);
			TrainingNoticePlanner.Plan(NewTraining(Now, null, Assignee("a", complete: true)), new[] { "a" }, Now).Kind.Should().Be(TrainingNoticeKinds.None);
			TrainingNoticePlanner.Plan(null, new[] { "a" }, Now).Kind.Should().Be(TrainingNoticeKinds.None);
		}
	}

	/// <summary>The notifier's service side: assignees are loaded for the trainings it will announce, and marking a training touches the row alone.</summary>
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
				Mock.Of<IDepartmentSettingsService>());
		}

		[Test]
		public async Task Unannounced_trainings_come_back_with_their_assignees()
		{
			var now = DateTime.UtcNow;
			// GetAllAsync reads the rows alone: no assignees, which is why the notifier used to skip every training.
			_trainings.Setup(x => x.GetAllAsync()).ReturnsAsync(new List<Training>
			{
				new Training { TrainingId = 5, DepartmentId = 10, Name = "Fresh", CreatedOn = now.AddHours(-2) }
			});
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
		public async Task A_stale_unannounced_training_is_returned_to_be_marked_without_loading_assignees()
		{
			var now = DateTime.UtcNow;
			_trainings.Setup(x => x.GetAllAsync()).ReturnsAsync(new List<Training>
			{
				new Training { TrainingId = 6, DepartmentId = 10, Name = "Old", CreatedOn = now.AddDays(-400) }
			});

			var result = await _service.GetTrainingsToNotifyAsync(now);

			result.Should().ContainSingle(x => x.TrainingId == 6);
			_trainings.Verify(x => x.GetTrainingByTrainingIdAsync(It.IsAny<int>()), Times.Never);
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
	}

	/// <summary>"Everyone" on the Create Training permission lets every member create a training; it no longer hands every member the department-wide edit and delete claims.</summary>
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
		public void Under_everyone_a_member_may_view_and_create_but_holds_no_department_wide_edit_or_delete()
		{
			var identity = ClaimsFor(PermissionActions.Everyone, isAdmin: false);

			Has(identity, ResgridClaimTypes.Actions.View).Should().BeTrue();
			Has(identity, ResgridClaimTypes.Actions.Create).Should().BeTrue();
			Has(identity, ResgridClaimTypes.Actions.Update).Should().BeFalse();
			Has(identity, ResgridClaimTypes.Actions.Delete).Should().BeFalse();
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
