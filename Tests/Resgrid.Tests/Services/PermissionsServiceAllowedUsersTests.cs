using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// GetAllowedUsersAsync (the user map's personnel-location filter) under every permission action that can
	/// be locked to group. Locked means the caller's own group, except that a group admin also reaches the groups
	/// beneath theirs (the same reach the visibility matrices give); a caller in no group shares one with nobody.
	/// </summary>
	[TestFixture]
	public class PermissionsServiceAllowedUsersTests
	{
		private const int DepartmentId = 1;
		private const int GroupA = 10;
		private const int GroupA1 = 11;  // beneath group A
		private const int GroupB = 20;
		private const int SelectedRoleId = 9;

		private static readonly List<PersonnelRole> SelectedRole = new List<PersonnelRole> { new PersonnelRole { PersonnelRoleId = SelectedRoleId } };
		private static readonly List<PersonnelRole> NoRoles = new List<PersonnelRole>();

		private PermissionsService _service;

		[SetUp]
		public void SetUp()
		{
			var users = new Mock<IUsersService>();
			users.Setup(x => x.GetUserGroupAndRolesByDepartmentIdAsync(DepartmentId, true, false, false)).ReturnsAsync(new List<UserGroupRole>
			{
				new UserGroupRole { UserId = "a1", DepartmentGroupId = GroupA },
				new UserGroupRole { UserId = "a2", DepartmentGroupId = GroupA },
				new UserGroupRole { UserId = "a3", DepartmentGroupId = GroupA1 },
				new UserGroupRole { UserId = "b1", DepartmentGroupId = GroupB },
				new UserGroupRole { UserId = "n1", DepartmentGroupId = null }
			});

			var groups = new Mock<IDepartmentGroupsService>();
			groups.Setup(x => x.GetAllGroupsForDepartmentUnlimitedAsync(DepartmentId)).ReturnsAsync(new List<DepartmentGroup>
			{
				new DepartmentGroup { DepartmentGroupId = GroupA, DepartmentId = DepartmentId },
				new DepartmentGroup { DepartmentGroupId = GroupA1, DepartmentId = DepartmentId, ParentDepartmentGroupId = GroupA },
				new DepartmentGroup { DepartmentGroupId = GroupB, DepartmentId = DepartmentId }
			});

			_service = new PermissionsService(new Mock<IPermissionsRepository>().Object, users.Object, groups.Object);
		}

		private static Permission Permission(PermissionActions action, bool lockToGroup, string roleIds = null)
		{
			return new Permission
			{
				DepartmentId = DepartmentId,
				PermissionType = (int)PermissionTypes.CanSeePersonnelLocations,
				Action = (int)action,
				LockToGroup = lockToGroup,
				Data = roleIds
			};
		}

		private Task<List<string>> Allowed(Permission permission, int? callerGroupId, bool isDepartmentAdmin = false, bool isGroupAdmin = false, List<PersonnelRole> roles = null)
		{
			return _service.GetAllowedUsersAsync(permission, DepartmentId, callerGroupId, isDepartmentAdmin, isGroupAdmin, roles ?? NoRoles);
		}

		#region Department and group admins

		[Test]
		public async Task group_admins_locked_get_their_own_group_and_the_groups_beneath_it()
		{
			// This branch was unreachable (its condition duplicated the department-admin one): group admins got nobody.
			(await Allowed(Permission(PermissionActions.DepartmentAndGroupAdmins, true), GroupA, isGroupAdmin: true))
				.Should().BeEquivalentTo("a1", "a2", "a3");
		}

		[Test]
		public async Task group_admins_locked_do_not_reach_up_into_the_group_above()
		{
			(await Allowed(Permission(PermissionActions.DepartmentAndGroupAdmins, true), GroupA1, isGroupAdmin: true))
				.Should().BeEquivalentTo("a3");
		}

		[Test]
		public async Task group_admins_unlocked_get_everyone()
		{
			(await Allowed(Permission(PermissionActions.DepartmentAndGroupAdmins, false), GroupA, isGroupAdmin: true))
				.Should().BeEquivalentTo("a1", "a2", "a3", "b1", "n1");
		}

		[Test]
		public async Task group_admins_locked_still_give_department_admins_everyone()
		{
			(await Allowed(Permission(PermissionActions.DepartmentAndGroupAdmins, true), GroupB, isDepartmentAdmin: true))
				.Should().BeEquivalentTo("a1", "a2", "a3", "b1", "n1");
		}

		[Test]
		public async Task group_admins_permission_gives_plain_members_nobody()
		{
			(await Allowed(Permission(PermissionActions.DepartmentAndGroupAdmins, true), GroupA)).Should().BeEmpty();
		}

		#endregion

		#region Everyone

		[Test]
		public async Task everyone_locked_gets_their_own_group()
		{
			// The lock used to be ignored here: everyone saw every group.
			(await Allowed(Permission(PermissionActions.Everyone, true), GroupB)).Should().BeEquivalentTo("b1");
		}

		[Test]
		public async Task everyone_locked_does_not_reach_the_groups_beneath()
		{
			// Only the group-admin rule reaches down, as in the visibility matrices.
			(await Allowed(Permission(PermissionActions.Everyone, true), GroupA)).Should().BeEquivalentTo("a1", "a2");
		}

		[Test]
		public async Task everyone_locked_in_no_group_gets_nobody()
		{
			(await Allowed(Permission(PermissionActions.Everyone, true), null)).Should().BeEmpty();
		}

		[Test]
		public async Task everyone_locked_still_gives_department_admins_everyone()
		{
			(await Allowed(Permission(PermissionActions.Everyone, true), GroupA, isDepartmentAdmin: true))
				.Should().BeEquivalentTo("a1", "a2", "a3", "b1", "n1");
		}

		[Test]
		public async Task everyone_unlocked_gets_everyone()
		{
			(await Allowed(Permission(PermissionActions.Everyone, false), GroupA)).Should().BeEquivalentTo("a1", "a2", "a3", "b1", "n1");
		}

		#endregion

		#region Department admins and select roles

		[Test]
		public async Task select_roles_locked_get_their_own_group()
		{
			(await Allowed(Permission(PermissionActions.DepartmentAdminsAndSelectRoles, true, SelectedRoleId.ToString()), GroupB, roles: SelectedRole))
				.Should().BeEquivalentTo("b1");
		}

		[Test]
		public async Task select_roles_locked_in_no_group_gets_nobody()
		{
			// Used to match every other ungrouped member.
			(await Allowed(Permission(PermissionActions.DepartmentAdminsAndSelectRoles, true, SelectedRoleId.ToString()), null, roles: SelectedRole))
				.Should().BeEmpty();
		}

		[Test]
		public async Task select_roles_without_the_role_gets_nobody()
		{
			(await Allowed(Permission(PermissionActions.DepartmentAdminsAndSelectRoles, true, SelectedRoleId.ToString()), GroupA)).Should().BeEmpty();
		}

		[Test]
		public async Task select_roles_with_no_roles_configured_gets_nobody_instead_of_failing()
		{
			(await Allowed(Permission(PermissionActions.DepartmentAdminsAndSelectRoles, false, null), GroupA, roles: SelectedRole)).Should().BeEmpty();
		}

		#endregion

		#region Department, group admins and select roles

		[Test]
		public async Task group_admins_and_select_roles_locked_in_no_group_gets_nobody()
		{
			(await Allowed(Permission(PermissionActions.DepartmentAndGroupAdminsAndSelectRoles, true, SelectedRoleId.ToString()), null, roles: SelectedRole))
				.Should().BeEmpty();
		}

		[Test]
		public async Task group_admins_and_select_roles_locked_get_their_own_group()
		{
			(await Allowed(Permission(PermissionActions.DepartmentAndGroupAdminsAndSelectRoles, true, SelectedRoleId.ToString()), GroupA, isGroupAdmin: true))
				.Should().BeEquivalentTo("a1", "a2");
		}

		#endregion
	}
}
