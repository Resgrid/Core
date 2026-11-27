using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Providers.Claims;
using Resgrid.Tests.Services.AuthorizationServiceTests;

namespace Resgrid.Tests.TrainingT0
{
	/// <summary>
	/// T0 item 1b: Create Training authors (Training:Create), Assign Training (126) assigns and reports (Training:Update),
	/// Delete Training (127) archives and deletes (Training:Delete). Assign and Delete with no row of their own follow the
	/// Create Training row.
	/// </summary>
	[TestFixture]
	public class TrainingClaimsSplitTests
	{
		private static Permission Row(PermissionTypes type, PermissionActions action, string data = null) =>
			new Permission { PermissionType = (int)type, Action = (int)action, Data = data };

		private static ClaimsIdentity Claims(bool isAdmin, bool isGroupAdmin, List<PersonnelRole> roles, params Permission[] permissions)
		{
			var identity = new ClaimsIdentity();
			ClaimsLogic.AddTrainingClaims(identity, isAdmin, permissions.ToList(), isGroupAdmin, roles ?? new List<PersonnelRole>());
			return identity;
		}

		private static bool Has(ClaimsIdentity identity, string action) => identity.HasClaim(ResgridClaimTypes.Resources.Training, action);

		[Test]
		public void Without_rows_only_admins_hold_the_three_rights_and_everyone_views()
		{
			var member = Claims(false, true, null);
			Has(member, ResgridClaimTypes.Actions.View).Should().BeTrue();
			Has(member, ResgridClaimTypes.Actions.Create).Should().BeFalse();
			Has(member, ResgridClaimTypes.Actions.Update).Should().BeFalse();
			Has(member, ResgridClaimTypes.Actions.Delete).Should().BeFalse();

			var admin = Claims(true, false, null);
			Has(admin, ResgridClaimTypes.Actions.Create).Should().BeTrue();
			Has(admin, ResgridClaimTypes.Actions.Update).Should().BeTrue();
			Has(admin, ResgridClaimTypes.Actions.Delete).Should().BeTrue();
		}

		[Test]
		public void Assign_and_delete_follow_the_create_row_when_they_have_none()
		{
			var groupAdmin = Claims(false, true, null, Row(PermissionTypes.CreateTraining, PermissionActions.DepartmentAndGroupAdmins));

			Has(groupAdmin, ResgridClaimTypes.Actions.Create).Should().BeTrue();
			Has(groupAdmin, ResgridClaimTypes.Actions.Update).Should().BeTrue();
			Has(groupAdmin, ResgridClaimTypes.Actions.Delete).Should().BeTrue();
		}

		[Test]
		public void Each_right_is_decided_by_its_own_row()
		{
			var roles = new List<PersonnelRole> { new PersonnelRole { PersonnelRoleId = 4 } };
			var permissions = new[]
			{
				Row(PermissionTypes.CreateTraining, PermissionActions.DepartmentAdminsOnly),
				Row(PermissionTypes.AssignTraining, PermissionActions.DepartmentAdminsAndSelectRoles, "4, 9"),
				Row(PermissionTypes.DeleteTraining, PermissionActions.DepartmentAndGroupAdmins)
			};

			var trainingOfficer = Claims(false, false, roles, permissions);
			Has(trainingOfficer, ResgridClaimTypes.Actions.Create).Should().BeFalse();
			Has(trainingOfficer, ResgridClaimTypes.Actions.Update).Should().BeTrue();
			Has(trainingOfficer, ResgridClaimTypes.Actions.Delete).Should().BeFalse();

			var groupAdmin = Claims(false, true, new List<PersonnelRole>(), permissions);
			Has(groupAdmin, ResgridClaimTypes.Actions.Create).Should().BeFalse();
			Has(groupAdmin, ResgridClaimTypes.Actions.Update).Should().BeFalse();
			Has(groupAdmin, ResgridClaimTypes.Actions.Delete).Should().BeTrue();
		}

		[Test]
		public void A_role_list_that_does_not_parse_grants_nothing_and_never_throws()
		{
			var member = Claims(false, false, new List<PersonnelRole> { new PersonnelRole { PersonnelRoleId = 4 } },
				Row(PermissionTypes.CreateTraining, PermissionActions.DepartmentAdminsAndSelectRoles, "x,,y"));

			Has(member, ResgridClaimTypes.Actions.Create).Should().BeFalse();
			ClaimsLogic.HoldsTrainingRight(Row(PermissionTypes.CreateTraining, PermissionActions.DepartmentAdminsAndSelectRoles, null), false, false, null).Should().BeFalse();
			ClaimsLogic.HoldsTrainingRight(null, true, false, null).Should().BeTrue();
			ClaimsLogic.HoldsTrainingRight(Row(PermissionTypes.CreateTraining, PermissionActions.Everyone), false, false, null).Should().BeTrue();
		}
	}

	/// <summary>The three rights on TrainingManagementScope, and what an assignee may open.</summary>
	[TestFixture]
	public class TrainingScopeSplitTests
	{
		private const int DepartmentId = 10;
		private static readonly DateTime Now = DateTime.UtcNow;

		private static Training TrainingFor(string createdBy, params string[] assignees) => new Training
		{
			TrainingId = 1,
			DepartmentId = DepartmentId,
			CreatedByUserId = createdBy,
			Users = assignees.Select(x => new TrainingUser { UserId = x }).ToList()
		};

		[Test]
		public void Author_assign_and_delete_are_answered_separately()
		{
			var scope = new TrainingManagementScope
			{
				UserId = "officer",
				DepartmentId = DepartmentId,
				Author = new TrainingRightScope(),
				Assign = new TrainingRightScope { ManagesAll = true },
				Delete = new TrainingRightScope()
			};
			var training = TrainingFor("chief", "ff1");

			scope.CanAuthor(training).Should().BeFalse("the edit page shows the answer key");
			scope.CanAssign(training).Should().BeTrue();
			scope.CanDelete(training).Should().BeFalse();
			scope.CanManage(training).Should().BeTrue();
			scope.CanOpenQuiz(training).Should().BeTrue("the report reviews attempts");
			scope.CanCreate.Should().BeFalse();
			scope.IsManager.Should().BeTrue();
		}

		[Test]
		public void A_group_admin_assigns_only_their_group_and_only_trainings_assigned_only_to_it()
		{
			var group = new TrainingRightScope { ManagesOwn = true, AdministeredMemberUserIds = new HashSet<string>(new[] { "captain", "ff1" }, StringComparer.OrdinalIgnoreCase) };
			var scope = new TrainingManagementScope { UserId = "captain", DepartmentId = DepartmentId, Author = new TrainingRightScope(), Assign = group, Delete = new TrainingRightScope() };

			scope.CanAssign(TrainingFor("chief", "FF1")).Should().BeTrue();
			scope.CanAssign(TrainingFor("chief", "ff1", "other-station")).Should().BeFalse();
			scope.CanAssign(TrainingFor("CAPTAIN", "other-station")).Should().BeTrue("they created it");
			group.CoversMember("ff1").Should().BeTrue();
			group.CoversMember("other-station").Should().BeFalse();

			// A removed assignee from another group does not take the training out of the group admin's hands.
			var training = TrainingFor("chief", "ff1", "other-station");
			training.Users.Last().RemovedOn = Now;
			scope.CanAssign(training).Should().BeTrue();
		}

		[Test]
		public void Without_a_separate_assign_or_delete_right_the_author_right_is_used()
		{
			var scope = new TrainingManagementScope { UserId = "me", DepartmentId = DepartmentId, ManagesOwn = true };

			scope.CanAssign(TrainingFor("me")).Should().BeTrue();
			scope.CanDelete(TrainingFor("me")).Should().BeTrue();
			scope.CanDelete(TrainingFor("someone")).Should().BeFalse();
		}

		[Test]
		public void An_archived_or_not_yet_started_training_is_hidden_from_assignees_but_not_from_managers()
		{
			var member = TrainingManagementScope.None("me", DepartmentId);
			var manager = new TrainingManagementScope { UserId = "chief", DepartmentId = DepartmentId, ManagesAll = true };

			var archived = TrainingFor("chief", "me");
			archived.ArchivedOn = Now.AddDays(-1);
			member.CanOpen(archived, Now).Should().BeFalse();
			member.CanOpenQuiz(archived, Now).Should().BeFalse();
			manager.CanOpen(archived, Now).Should().BeTrue();

			var scheduled = TrainingFor("chief", "me");
			scheduled.StartsOn = Now.AddDays(2);
			member.CanOpen(scheduled, Now).Should().BeFalse();
			member.CanOpen(scheduled, Now.AddDays(3)).Should().BeTrue();
			manager.CanOpen(scheduled, Now).Should().BeTrue();

			var openToAll = TrainingFor("chief");
			openToAll.OpenToAllMembers = true;
			openToAll.StartsOn = Now.AddDays(2);
			member.CanOpen(openToAll, Now).Should().BeFalse("the catalog does not show it before it starts either");
		}

		[Test]
		public void A_removed_assignee_is_no_longer_assigned_but_keeps_their_row()
		{
			var training = TrainingFor("chief", "me");
			training.Users.Single().RemovedOn = Now;
			var scope = TrainingManagementScope.None("me", DepartmentId);

			scope.IsAssigned(training).Should().BeFalse();
			scope.AssignmentOf(training).Should().NotBeNull();
		}
	}

	/// <summary>GetTrainingManagementScopeAsync builds three rights from the three rows.</summary>
	[TestFixture]
	public class when_building_the_split_training_scope : with_the_authorization_service
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

		private void RowIs(PermissionTypes type, PermissionActions action, string data = null)
		{
			_permissionsServiceMock.Setup(m => m.GetPermissionByDepartmentTypeAsync(DepartmentId, type))
				.ReturnsAsync(new Permission { PermissionType = (int)type, Action = (int)action, Data = data });
		}

		[Test]
		public async Task An_admin_holds_all_three_rights_department_wide()
		{
			var scope = await _authorizationService.GetTrainingManagementScopeAsync(Admin, DepartmentId);

			scope.Author.ManagesAll.Should().BeTrue();
			scope.AssignRight.ManagesAll.Should().BeTrue();
			scope.DeleteRight.ManagesAll.Should().BeTrue();
		}

		[Test]
		public async Task Assign_and_delete_without_rows_follow_the_create_row()
		{
			RowIs(PermissionTypes.CreateTraining, PermissionActions.Everyone);

			var scope = await _authorizationService.GetTrainingManagementScopeAsync(Member, DepartmentId);

			scope.Author.ManagesOwn.Should().BeTrue();
			scope.AssignRight.ManagesOwn.Should().BeTrue();
			scope.DeleteRight.ManagesOwn.Should().BeTrue();
			scope.AssignRight.ManagesAll.Should().BeFalse();
		}

		[Test]
		public async Task Each_row_builds_its_own_right()
		{
			RowIs(PermissionTypes.CreateTraining, PermissionActions.DepartmentAdminsOnly);
			RowIs(PermissionTypes.AssignTraining, PermissionActions.DepartmentAdminsAndSelectRoles, "4");
			RowIs(PermissionTypes.DeleteTraining, PermissionActions.DepartmentAndGroupAdmins);
			var roles = new List<PersonnelRole> { new PersonnelRole { PersonnelRoleId = 4 } };
			_personnelRolesServiceMock.Setup(m => m.GetRolesForUserAsync(Member, DepartmentId)).ReturnsAsync(roles);
			_permissionsServiceMock.Setup(m => m.IsUserAllowed(It.Is<Permission>(p => p.PermissionType == (int)PermissionTypes.AssignTraining), false, false, roles)).Returns(true);
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

			scope.Author.HoldsAny.Should().BeFalse();
			scope.AssignRight.ManagesAll.Should().BeTrue();
			scope.DeleteRight.ManagesAll.Should().BeFalse();
			scope.DeleteRight.ManagesOwn.Should().BeTrue();
			scope.DeleteRight.AdministeredMemberUserIds.Should().BeEquivalentTo(new[] { Member, "ff1" });

			var groupTraining = new Training { TrainingId = 1, DepartmentId = DepartmentId, CreatedByUserId = "chief", Users = new List<TrainingUser> { new TrainingUser { UserId = "ff1" } } };
			scope.CanAuthor(groupTraining).Should().BeFalse();
			scope.CanAssign(groupTraining).Should().BeTrue();
			scope.CanDelete(groupTraining).Should().BeTrue();
		}

		[Test]
		public async Task A_member_with_no_rows_holds_no_right_but_still_has_empty_rights()
		{
			var scope = await _authorizationService.GetTrainingManagementScopeAsync(Member, DepartmentId);

			scope.IsManager.Should().BeFalse();
			scope.Assign.Should().NotBeNull();
			scope.Delete.Should().NotBeNull();
		}
	}
}
