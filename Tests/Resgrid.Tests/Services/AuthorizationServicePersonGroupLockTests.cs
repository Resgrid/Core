using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;

namespace Resgrid.Tests.Services
{
	namespace AuthorizationServiceTests
	{
		/// <summary>
		/// The direct visibility checks for a person (ViewGroupUsers), a person's location (CanSeePersonnelLocations),
		/// a unit (ViewGroupUnits) and a unit's location (CanSeeUnitLocations), under every permission action that can
		/// be locked to group.
		/// They give the same answers as the visibility matrices: locked means the target's own group, except that
		/// admins of a group above it (an area supervisor) count for the locked "department and group admins" rule.
		/// Department admins always pass; someone in no group shares a group with nobody.
		/// </summary>
		[TestFixture]
		public class when_authorizing_a_view_locked_to_group : with_the_authorization_service
		{
			private const int DepartmentId = 1;
			private const int ServiceArea = 5;
			private const int GroupA = 10;   // beneath the service area
			private const int GroupB = 20;   // a separate top-level group
			private const int SelectedRoleId = 9;

			private const int UnitA = 100;
			private const int UnitB = 200;
			private const int UnitNoStation = 300;
			private const int UnitOtherDepartment = 400;

			private const string DepartmentAdmin = "dept-admin";
			private const string AreaSupervisor = "area-supervisor";
			private const string AreaMember = "area-member";
			private const string GroupAdminA = "group-admin-a";
			private const string MemberA = "member-a";
			private const string RoleHolderA = "role-holder-a";
			private const string RoleHolderNoGroup = "role-holder-none";
			private const string NoGroupMember = "no-group";
			private const string TargetA = "target-a";
			private const string TargetB = "target-b";
			private const string TargetNoGroup = "target-none";

			private Permission _permission;

			[SetUp]
			public void Setup()
			{
				var department = CreateDepartmentWithAdmins(DepartmentId, "owner", DepartmentAdmin);
				_departmentsServiceMock.Setup(m => m.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(department);
				_departmentsServiceMock.Setup(m => m.GetDepartmentByUserIdAsync(It.IsAny<string>(), It.IsAny<bool>())).ReturnsAsync(department);

				var serviceArea = Group(ServiceArea, null,
					new DepartmentGroupMember { UserId = AreaSupervisor, IsAdmin = true },
					new DepartmentGroupMember { UserId = AreaMember, IsAdmin = false });
				var groupA = Group(GroupA, ServiceArea,
					new DepartmentGroupMember { UserId = GroupAdminA, IsAdmin = true },
					new DepartmentGroupMember { UserId = MemberA, IsAdmin = false },
					new DepartmentGroupMember { UserId = RoleHolderA, IsAdmin = false },
					new DepartmentGroupMember { UserId = TargetA, IsAdmin = false });
				var groupB = Group(GroupB, null, new DepartmentGroupMember { UserId = TargetB, IsAdmin = false });

				_departmentGroupsServiceMock.Setup(m => m.GetGroupForUserAsync(It.IsAny<string>(), DepartmentId)).ReturnsAsync((DepartmentGroup)null);
				foreach (var userId in new[] { AreaSupervisor, AreaMember })
					_departmentGroupsServiceMock.Setup(m => m.GetGroupForUserAsync(userId, DepartmentId)).ReturnsAsync(serviceArea);
				foreach (var userId in new[] { GroupAdminA, MemberA, RoleHolderA, TargetA })
					_departmentGroupsServiceMock.Setup(m => m.GetGroupForUserAsync(userId, DepartmentId)).ReturnsAsync(groupA);
				_departmentGroupsServiceMock.Setup(m => m.GetGroupForUserAsync(TargetB, DepartmentId)).ReturnsAsync(groupB);

				// What the matrices use too: a group's admins plus the admins of every group above it.
				_departmentGroupsServiceMock.Setup(m => m.GetAllAdminsForGroupAndAncestorsAsync(ServiceArea))
					.ReturnsAsync(new List<DepartmentGroupMember> { new DepartmentGroupMember { UserId = AreaSupervisor, IsAdmin = true } });
				_departmentGroupsServiceMock.Setup(m => m.GetAllAdminsForGroupAndAncestorsAsync(GroupA))
					.ReturnsAsync(new List<DepartmentGroupMember>
					{
						new DepartmentGroupMember { UserId = GroupAdminA, IsAdmin = true },
						new DepartmentGroupMember { UserId = AreaSupervisor, IsAdmin = true }
					});
				_departmentGroupsServiceMock.Setup(m => m.GetAllAdminsForGroupAndAncestorsAsync(GroupB))
					.ReturnsAsync(new List<DepartmentGroupMember>());

				var selectedRole = new List<PersonnelRole> { new PersonnelRole { PersonnelRoleId = SelectedRoleId, Name = "Supervisor" } };
				_personnelRolesServiceMock.Setup(m => m.GetRolesForUserAsync(It.IsAny<string>(), DepartmentId)).ReturnsAsync(new List<PersonnelRole>());
				_personnelRolesServiceMock.Setup(m => m.GetRolesForUserAsync(RoleHolderA, DepartmentId)).ReturnsAsync(selectedRole);
				_personnelRolesServiceMock.Setup(m => m.GetRolesForUserAsync(RoleHolderNoGroup, DepartmentId)).ReturnsAsync(selectedRole);

				_unitsServiceMock.Setup(m => m.GetUnitByIdAsync(UnitA)).ReturnsAsync(new Unit { UnitId = UnitA, DepartmentId = DepartmentId, StationGroupId = GroupA });
				_unitsServiceMock.Setup(m => m.GetUnitByIdAsync(UnitB)).ReturnsAsync(new Unit { UnitId = UnitB, DepartmentId = DepartmentId, StationGroupId = GroupB });
				_unitsServiceMock.Setup(m => m.GetUnitByIdAsync(UnitNoStation)).ReturnsAsync(new Unit { UnitId = UnitNoStation, DepartmentId = DepartmentId, StationGroupId = null });
				_unitsServiceMock.Setup(m => m.GetUnitByIdAsync(UnitOtherDepartment)).ReturnsAsync(new Unit { UnitId = UnitOtherDepartment, DepartmentId = DepartmentId + 1, StationGroupId = GroupA });

				_permissionsServiceMock.Setup(m => m.GetPermissionByDepartmentTypeAsync(DepartmentId, It.IsAny<PermissionTypes>()))
					.ReturnsAsync(() => _permission);
			}

			private static DepartmentGroup Group(int id, int? parentId, params DepartmentGroupMember[] members)
			{
				return new DepartmentGroup { DepartmentGroupId = id, DepartmentId = DepartmentId, ParentDepartmentGroupId = parentId, Members = new List<DepartmentGroupMember>(members) };
			}

			private void Permit(PermissionTypes type, PermissionActions action, bool lockToGroup, string roleIds = null)
			{
				_permission = new Permission { DepartmentId = DepartmentId, PermissionType = (int)type, Action = (int)action, LockToGroup = lockToGroup, Data = roleIds };
			}

			private Task<bool> CanView(PermissionTypes type, string viewer, string target)
			{
				return type == PermissionTypes.CanSeePersonnelLocations
					? _authorizationService.CanUserViewPersonLocationAsync(viewer, target, DepartmentId)
					: _authorizationService.CanUserViewPersonAsync(viewer, target, DepartmentId);
			}

			private Task<bool> CanSeeUnit(PermissionTypes type, string viewer, int unitId)
			{
				return type == PermissionTypes.CanSeeUnitLocations
					? _authorizationService.CanUserViewUnitLocationAsync(viewer, unitId, DepartmentId)
					: _authorizationService.CanUserViewUnitAsync(viewer, unitId);
			}

			#region People: everyone, locked to group

			[TestCase(PermissionTypes.ViewGroupUsers)]
			[TestCase(PermissionTypes.CanSeePersonnelLocations)]
			public async Task everyone_locked_sees_their_own_group(PermissionTypes type)
			{
				Permit(type, PermissionActions.Everyone, true);

				(await CanView(type, MemberA, TargetA)).Should().BeTrue();
			}

			[TestCase(PermissionTypes.ViewGroupUsers)]
			[TestCase(PermissionTypes.CanSeePersonnelLocations)]
			public async Task everyone_locked_does_not_see_another_group(PermissionTypes type)
			{
				Permit(type, PermissionActions.Everyone, true);

				(await CanView(type, MemberA, TargetB)).Should().BeFalse();
			}

			[TestCase(PermissionTypes.ViewGroupUsers)]
			[TestCase(PermissionTypes.CanSeePersonnelLocations)]
			public async Task everyone_locked_does_not_reach_down_into_groups_beneath(PermissionTypes type)
			{
				// Only the admin rule reaches beneath an area; plain members stay in their own group, as in the matrices.
				Permit(type, PermissionActions.Everyone, true);

				(await CanView(type, AreaMember, TargetA)).Should().BeFalse();
			}

			[TestCase(PermissionTypes.ViewGroupUsers)]
			[TestCase(PermissionTypes.CanSeePersonnelLocations)]
			public async Task everyone_locked_shares_no_group_with_someone_in_none(PermissionTypes type)
			{
				Permit(type, PermissionActions.Everyone, true);

				(await CanView(type, MemberA, TargetNoGroup)).Should().BeFalse();
				(await CanView(type, NoGroupMember, TargetA)).Should().BeFalse();
			}

			[TestCase(PermissionTypes.ViewGroupUsers)]
			[TestCase(PermissionTypes.CanSeePersonnelLocations)]
			public async Task everyone_locked_still_lets_department_admins_see_every_group(PermissionTypes type)
			{
				Permit(type, PermissionActions.Everyone, true);

				(await CanView(type, DepartmentAdmin, TargetB)).Should().BeTrue();
			}

			[TestCase(PermissionTypes.ViewGroupUsers)]
			[TestCase(PermissionTypes.CanSeePersonnelLocations)]
			public async Task everyone_unlocked_sees_every_group(PermissionTypes type)
			{
				Permit(type, PermissionActions.Everyone, false);

				(await CanView(type, MemberA, TargetB)).Should().BeTrue();
			}

			#endregion

			#region People: department and group admins, locked to group

			[TestCase(PermissionTypes.ViewGroupUsers)]
			[TestCase(PermissionTypes.CanSeePersonnelLocations)]
			public async Task group_admins_locked_see_their_own_group_but_not_another(PermissionTypes type)
			{
				Permit(type, PermissionActions.DepartmentAndGroupAdmins, true);

				(await CanView(type, GroupAdminA, TargetA)).Should().BeTrue();
				(await CanView(type, GroupAdminA, TargetB)).Should().BeFalse();
			}

			[TestCase(PermissionTypes.ViewGroupUsers)]
			[TestCase(PermissionTypes.CanSeePersonnelLocations)]
			public async Task area_supervisors_locked_see_the_groups_beneath_their_area(PermissionTypes type)
			{
				Permit(type, PermissionActions.DepartmentAndGroupAdmins, true);

				(await CanView(type, AreaSupervisor, TargetA)).Should().BeTrue();
				(await CanView(type, AreaSupervisor, TargetB)).Should().BeFalse();
			}

			[TestCase(PermissionTypes.ViewGroupUsers)]
			[TestCase(PermissionTypes.CanSeePersonnelLocations)]
			public async Task group_admins_locked_do_not_see_up_into_the_area_above(PermissionTypes type)
			{
				Permit(type, PermissionActions.DepartmentAndGroupAdmins, true);

				(await CanView(type, GroupAdminA, AreaMember)).Should().BeFalse();
			}

			[TestCase(PermissionTypes.ViewGroupUsers)]
			[TestCase(PermissionTypes.CanSeePersonnelLocations)]
			public async Task group_admins_locked_excludes_plain_members_of_the_same_group(PermissionTypes type)
			{
				Permit(type, PermissionActions.DepartmentAndGroupAdmins, true);

				(await CanView(type, MemberA, TargetA)).Should().BeFalse();
			}

			[TestCase(PermissionTypes.ViewGroupUsers)]
			[TestCase(PermissionTypes.CanSeePersonnelLocations)]
			public async Task group_admins_locked_still_lets_department_admins_see_every_group(PermissionTypes type)
			{
				Permit(type, PermissionActions.DepartmentAndGroupAdmins, true);

				(await CanView(type, DepartmentAdmin, TargetB)).Should().BeTrue();
			}

			#endregion

			#region People: department admins and select roles, locked to group

			[TestCase(PermissionTypes.ViewGroupUsers)]
			[TestCase(PermissionTypes.CanSeePersonnelLocations)]
			public async Task select_roles_locked_see_their_own_group_only(PermissionTypes type)
			{
				Permit(type, PermissionActions.DepartmentAdminsAndSelectRoles, true, SelectedRoleId.ToString());

				(await CanView(type, RoleHolderA, TargetA)).Should().BeTrue();
				(await CanView(type, RoleHolderA, TargetB)).Should().BeFalse();
			}

			[TestCase(PermissionTypes.ViewGroupUsers)]
			[TestCase(PermissionTypes.CanSeePersonnelLocations)]
			public async Task select_roles_locked_shares_no_group_with_someone_in_none(PermissionTypes type)
			{
				Permit(type, PermissionActions.DepartmentAdminsAndSelectRoles, true, SelectedRoleId.ToString());

				(await CanView(type, RoleHolderNoGroup, TargetA)).Should().BeFalse();
				(await CanView(type, RoleHolderA, TargetNoGroup)).Should().BeFalse();
			}

			[TestCase(PermissionTypes.ViewGroupUsers)]
			[TestCase(PermissionTypes.CanSeePersonnelLocations)]
			public async Task select_roles_locked_requires_the_role(PermissionTypes type)
			{
				Permit(type, PermissionActions.DepartmentAdminsAndSelectRoles, true, SelectedRoleId.ToString());

				(await CanView(type, MemberA, TargetA)).Should().BeFalse();
			}

			[TestCase(PermissionTypes.ViewGroupUsers)]
			[TestCase(PermissionTypes.CanSeePersonnelLocations)]
			public async Task select_roles_locked_still_lets_department_admins_see_every_group(PermissionTypes type)
			{
				Permit(type, PermissionActions.DepartmentAdminsAndSelectRoles, true, SelectedRoleId.ToString());

				(await CanView(type, DepartmentAdmin, TargetB)).Should().BeTrue();
			}

			#endregion

			#region Units and unit locations

			[TestCase(PermissionTypes.ViewGroupUnits)]
			[TestCase(PermissionTypes.CanSeeUnitLocations)]
			public async Task units_everyone_locked_is_the_units_own_station(PermissionTypes type)
			{
				Permit(type, PermissionActions.Everyone, true);

				(await CanSeeUnit(type, MemberA, UnitA)).Should().BeTrue();
				(await CanSeeUnit(type, MemberA, UnitB)).Should().BeFalse();
				(await CanSeeUnit(type, MemberA, UnitNoStation)).Should().BeFalse();
				(await CanSeeUnit(type, DepartmentAdmin, UnitB)).Should().BeTrue("department admins always pass, as in the matrices");
			}

			[TestCase(PermissionTypes.ViewGroupUnits)]
			[TestCase(PermissionTypes.CanSeeUnitLocations)]
			public async Task units_group_admins_locked_include_the_area_supervisor_above_the_station(PermissionTypes type)
			{
				Permit(type, PermissionActions.DepartmentAndGroupAdmins, true);

				(await CanSeeUnit(type, GroupAdminA, UnitA)).Should().BeTrue();
				(await CanSeeUnit(type, GroupAdminA, UnitB)).Should().BeFalse();
				(await CanSeeUnit(type, AreaSupervisor, UnitA)).Should().BeTrue();
				(await CanSeeUnit(type, AreaSupervisor, UnitB)).Should().BeFalse();
				(await CanSeeUnit(type, AreaSupervisor, UnitNoStation)).Should().BeFalse();
				(await CanSeeUnit(type, MemberA, UnitA)).Should().BeFalse("plain members aren't admins");
				(await CanSeeUnit(type, DepartmentAdmin, UnitB)).Should().BeTrue();
			}

			[TestCase(PermissionTypes.ViewGroupUnits)]
			[TestCase(PermissionTypes.CanSeeUnitLocations)]
			public async Task units_select_roles_locked_is_the_units_own_station(PermissionTypes type)
			{
				Permit(type, PermissionActions.DepartmentAdminsAndSelectRoles, true, SelectedRoleId.ToString());

				(await CanSeeUnit(type, RoleHolderA, UnitA)).Should().BeTrue();
				(await CanSeeUnit(type, RoleHolderA, UnitB)).Should().BeFalse();
				(await CanSeeUnit(type, RoleHolderA, UnitNoStation)).Should().BeFalse();
				(await CanSeeUnit(type, RoleHolderNoGroup, UnitA)).Should().BeFalse();
				(await CanSeeUnit(type, MemberA, UnitA)).Should().BeFalse("the role is still required");
			}

			[TestCase(PermissionTypes.ViewGroupUnits)]
			[TestCase(PermissionTypes.CanSeeUnitLocations)]
			public async Task units_are_open_to_the_department_when_no_restriction_is_configured(PermissionTypes type)
			{
				_permission = null;

				(await CanSeeUnit(type, MemberA, UnitB)).Should().BeTrue();
				(await CanSeeUnit(type, NoGroupMember, UnitNoStation)).Should().BeTrue();
			}

			[Test]
			public async Task unit_view_never_crosses_departments()
			{
				_permission = null;

				(await CanSeeUnit(PermissionTypes.ViewGroupUnits, DepartmentAdmin, UnitOtherDepartment)).Should().BeFalse();
			}

			#endregion
		}
	}
}
