using System.Collections.Generic;
using System.Linq;
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
		/// GetViewablePersonIdsAsync is the bulk form of CanUserViewPersonAsync: for every permission action, locked or not,
		/// every viewer and every target, it returns exactly the targets the single check allows, while reading the
		/// permission, the viewer and each ancestor-admin group once instead of once per target.
		/// </summary>
		[TestFixture]
		public class when_authorizing_many_people_at_once : with_the_authorization_service
		{
			private const int DepartmentId = 1;
			private const int ServiceArea = 5;
			private const int GroupA = 10;   // beneath the service area
			private const int GroupB = 20;   // a separate top-level group
			private const int SelectedRoleId = 9;

			private const string DepartmentAdmin = "dept-admin";
			private const string AreaSupervisor = "area-supervisor";
			private const string AreaMember = "area-member";
			private const string GroupAdminA = "group-admin-a";
			private const string MemberA = "member-a";
			private const string RoleHolderA = "role-holder-a";
			private const string RoleHolderNoGroup = "role-holder-none";
			private const string NoGroupMember = "no-group";
			private const string TargetA = "target-a";
			private const string TargetA2 = "target-a2";
			private const string TargetB = "target-b";
			private const string TargetNoGroup = "target-none";

			private static readonly string[] Viewers =
				{ DepartmentAdmin, AreaSupervisor, AreaMember, GroupAdminA, MemberA, RoleHolderA, RoleHolderNoGroup, NoGroupMember };

			private static readonly string[] Targets =
				{ TargetA, TargetA2, TargetB, TargetNoGroup, AreaMember, AreaSupervisor, GroupAdminA, MemberA, DepartmentAdmin, null };

			private Permission _permission;
			private Department _department;

			[SetUp]
			public void Setup()
			{
				// The fixture's mocks live for the whole fixture; the read counts below are per test.
				_permissionsServiceMock.Invocations.Clear();
				_departmentGroupsServiceMock.Invocations.Clear();

				_department =CreateDepartmentWithAdmins(DepartmentId, "owner", DepartmentAdmin);
				_departmentsServiceMock.Setup(m => m.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(() => _department);

				var serviceArea = Group(ServiceArea, null,
					new DepartmentGroupMember { UserId = AreaSupervisor, IsAdmin = true },
					new DepartmentGroupMember { UserId = AreaMember, IsAdmin = false });
				var groupA = Group(GroupA, ServiceArea,
					new DepartmentGroupMember { UserId = GroupAdminA, IsAdmin = true },
					new DepartmentGroupMember { UserId = MemberA, IsAdmin = false },
					new DepartmentGroupMember { UserId = RoleHolderA, IsAdmin = false },
					new DepartmentGroupMember { UserId = TargetA, IsAdmin = false },
					new DepartmentGroupMember { UserId = TargetA2, IsAdmin = false });
				var groupB = Group(GroupB, null, new DepartmentGroupMember { UserId = TargetB, IsAdmin = false });

				// The single check reads one group per user; the bulk check reads the same assignment for the whole department.
				var groupsByUser = new Dictionary<string, DepartmentGroup>();
				foreach (var group in new[] { serviceArea, groupA, groupB })
					foreach (var member in group.Members)
						groupsByUser[member.UserId] = group;

				_departmentGroupsServiceMock.Setup(m => m.GetGroupForUserAsync(It.IsAny<string>(), DepartmentId))
					.ReturnsAsync((string userId, int _) => userId != null && groupsByUser.TryGetValue(userId, out var group) ? group : null);
				_departmentGroupsServiceMock.Setup(m => m.GetGroupIdsForAllUsersInDepartmentAsync(DepartmentId))
					.ReturnsAsync(() => groupsByUser.ToDictionary(p => p.Key, p => p.Value.DepartmentGroupId));

				// A group's admins plus the admins of every group above it.
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

				_permissionsServiceMock.Setup(m => m.GetPermissionByDepartmentTypeAsync(DepartmentId, PermissionTypes.ViewGroupUsers))
					.ReturnsAsync(() => _permission);
			}

			private static DepartmentGroup Group(int id, int? parentId, params DepartmentGroupMember[] members)
			{
				return new DepartmentGroup { DepartmentGroupId = id, DepartmentId = DepartmentId, ParentDepartmentGroupId = parentId, Members = new List<DepartmentGroupMember>(members) };
			}

			private static IEnumerable<TestCaseData> Permissions()
			{
				yield return new TestCaseData(null, false, null).SetName("no restriction configured");
				foreach (var action in new[] { PermissionActions.DepartmentAdminsOnly, PermissionActions.DepartmentAndGroupAdmins,
					PermissionActions.DepartmentAdminsAndSelectRoles, PermissionActions.Everyone })
					foreach (var locked in new[] { false, true })
						yield return new TestCaseData(action, locked, action == PermissionActions.DepartmentAdminsAndSelectRoles ? SelectedRoleId.ToString() : null)
							.SetName($"{action} {(locked ? "locked" : "unlocked")} to group");
				yield return new TestCaseData(PermissionActions.DepartmentAdminsAndSelectRoles, true, "x, 9").SetName("select roles with a malformed entry");
				yield return new TestCaseData((PermissionActions)4, false, null).SetName("an action the person gate does not implement");
			}

			private void Permit(PermissionActions? action, bool lockToGroup, string roleIds)
			{
				_permission = action == null ? null : new Permission
				{
					DepartmentId = DepartmentId, PermissionType = (int)PermissionTypes.ViewGroupUsers, Action = (int)action.Value, LockToGroup = lockToGroup, Data = roleIds
				};
			}

			private async Task ShouldAgreeForEveryViewerAndTarget()
			{
				foreach (var viewer in Viewers)
				{
					var expected = new List<string>();
					foreach (var target in Targets)
						if (await _authorizationService.CanUserViewPersonAsync(viewer, target, DepartmentId))
							expected.Add(target);

					var viewable = await _authorizationService.GetViewablePersonIdsAsync(viewer, Targets, DepartmentId);

					viewable.Should().BeEquivalentTo(expected, $"{viewer} must see the same people either way");
				}
			}

			[TestCaseSource(nameof(Permissions))]
			public async Task the_bulk_check_allows_exactly_the_people_the_single_check_allows(PermissionActions? action, bool lockToGroup, string roleIds)
			{
				Permit(action, lockToGroup, roleIds);

				await ShouldAgreeForEveryViewerAndTarget();
			}

			[TestCaseSource(nameof(Permissions))]
			public async Task a_missing_department_denies_everyone_either_way_unless_nothing_is_restricted(PermissionActions? action, bool lockToGroup, string roleIds)
			{
				Permit(action, lockToGroup, roleIds);
				_department = null;

				await ShouldAgreeForEveryViewerAndTarget();
			}

			[Test]
			public async Task area_supervisors_see_the_groups_beneath_them_but_not_beside_them()
			{
				Permit(PermissionActions.DepartmentAndGroupAdmins, true, null);

				var viewable = await _authorizationService.GetViewablePersonIdsAsync(AreaSupervisor, new[] { TargetA, TargetA2, TargetB, TargetNoGroup }, DepartmentId);

				viewable.Should().BeEquivalentTo(new[] { TargetA, TargetA2 });
			}

			[Test]
			public async Task shared_reads_happen_once_for_the_whole_set()
			{
				Permit(PermissionActions.DepartmentAndGroupAdmins, true, null);

				await _authorizationService.GetViewablePersonIdsAsync(AreaSupervisor, new[] { TargetA, TargetA2, MemberA, TargetB, TargetB, TargetNoGroup }, DepartmentId);

				_permissionsServiceMock.Verify(m => m.GetPermissionByDepartmentTypeAsync(DepartmentId, PermissionTypes.ViewGroupUsers), Times.Once);
				_departmentGroupsServiceMock.Verify(m => m.GetGroupForUserAsync(It.IsAny<string>(), DepartmentId), Times.Once, "only the viewer's own group is read one user at a time");
				_departmentGroupsServiceMock.Verify(m => m.GetGroupIdsForAllUsersInDepartmentAsync(DepartmentId), Times.Once);
				_departmentGroupsServiceMock.Verify(m => m.GetAllAdminsForGroupAndAncestorsAsync(GroupA), Times.Once, "three targets share group A");
				_departmentGroupsServiceMock.Verify(m => m.GetAllAdminsForGroupAndAncestorsAsync(GroupB), Times.Once);
			}

			[Test]
			public async Task an_empty_set_reads_nothing()
			{
				Permit(PermissionActions.Everyone, true, null);

				(await _authorizationService.GetViewablePersonIdsAsync(MemberA, new string[0], DepartmentId)).Should().BeEmpty();
				(await _authorizationService.GetViewablePersonIdsAsync(MemberA, null, DepartmentId)).Should().BeEmpty();

				_permissionsServiceMock.Verify(m => m.GetPermissionByDepartmentTypeAsync(It.IsAny<int>(), It.IsAny<PermissionTypes>()), Times.Never);
			}
		}
	}
}
