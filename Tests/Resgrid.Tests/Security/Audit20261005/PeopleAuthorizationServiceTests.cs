using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Security.Audit20261005
{
	/// <summary>
	/// Permission audit 2026-10-05, people and authorization core: Remove Personnel (2.3), View Group Users "all people"
	/// (3.4), the visibility matrices' fallback when the cache has no answer (3.6) and the live matrix build.
	/// </summary>
	[TestFixture]
	public class PeopleAuthorizationServiceTests
	{
		private const int DepartmentId = 4;
		private const string Owner = "owner";
		private const string DeptAdmin = "dept-admin";
		private const string GroupAdmin = "group-admin";
		private const string GroupMember = "group-member";
		private const string OtherMember = "other-member";
		private const int StationOne = 11;
		private const int StationTwo = 12;

		private Mock<IDepartmentsService> _departments;
		private Mock<IDepartmentGroupsService> _groups;
		private Mock<IPersonnelRolesService> _roles;
		private Mock<IUnitsService> _units;
		private Mock<IPermissionsService> _permissions;
		private Mock<ICacheProvider> _cache;
		private Mock<IEventAggregator> _events;
		private AuthorizationService _service;
		private DepartmentGroup _stationOne;
		private DepartmentGroup _stationTwo;

		[SetUp]
		public void SetUp()
		{
			_departments = new Mock<IDepartmentsService>();
			_groups = new Mock<IDepartmentGroupsService>();
			_roles = new Mock<IPersonnelRolesService>();
			_units = new Mock<IUnitsService>();
			_permissions = new Mock<IPermissionsService>();
			_cache = new Mock<ICacheProvider>();
			_events = new Mock<IEventAggregator>();

			var department = new Department
			{
				DepartmentId = DepartmentId,
				ManagingUserId = Owner,
				Members = new List<DepartmentMember>
				{
					new DepartmentMember { DepartmentId = DepartmentId, UserId = Owner, IsAdmin = true },
					new DepartmentMember { DepartmentId = DepartmentId, UserId = DeptAdmin, IsAdmin = true },
					new DepartmentMember { DepartmentId = DepartmentId, UserId = GroupAdmin },
					new DepartmentMember { DepartmentId = DepartmentId, UserId = GroupMember },
					new DepartmentMember { DepartmentId = DepartmentId, UserId = OtherMember }
				}
			};
			_departments.Setup(x => x.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(department);
			_departments.Setup(x => x.GetAllMembersForDepartmentIncludingDeletedAsync(DepartmentId)).ReturnsAsync(department.Members);

			_stationOne = Group(StationOne, (GroupAdmin, true), (GroupMember, false), (DeptAdmin, false));
			_stationTwo = Group(StationTwo, (OtherMember, false));
			foreach (var member in _stationOne.Members)
				_groups.Setup(x => x.GetGroupForUserAsync(member.UserId, DepartmentId)).ReturnsAsync(_stationOne);
			_groups.Setup(x => x.GetGroupForUserAsync(OtherMember, DepartmentId)).ReturnsAsync(_stationTwo);
			_groups.Setup(x => x.GetGroupIdsForAllUsersInDepartmentAsync(DepartmentId)).ReturnsAsync(new Dictionary<string, int>
			{
				[GroupAdmin] = StationOne, [GroupMember] = StationOne, [DeptAdmin] = StationOne, [OtherMember] = StationTwo
			});
			_groups.Setup(x => x.GetAllGroupsForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<DepartmentGroup> { _stationOne, _stationTwo });
			_groups.Setup(x => x.GetAllAdminsForGroupAndAncestorsAsync(StationOne)).ReturnsAsync(new List<DepartmentGroupMember> { _stationOne.Members.First() });
			_groups.Setup(x => x.GetAllAdminsForGroupAndAncestorsAsync(StationTwo)).ReturnsAsync(new List<DepartmentGroupMember>());

			_service = new AuthorizationService(_departments.Object, Mock.Of<IInvitesService>(), Mock.Of<ICallsService>(),
				Mock.Of<IMessageService>(), Mock.Of<IWorkLogsService>(), Mock.Of<ISubscriptionsService>(), _groups.Object,
				_roles.Object, _units.Object, _permissions.Object, Mock.Of<ICalendarService>(), Mock.Of<IProtocolsService>(),
				Mock.Of<IShiftsService>(), Mock.Of<ICustomStateService>(), Mock.Of<ICertificationService>(), Mock.Of<IDocumentsService>(),
				Mock.Of<INotesService>(), _cache.Object, Mock.Of<IContactsService>(), _events.Object, Mock.Of<IDispatchScopeService>());
		}

		private static DepartmentGroup Group(int id, params (string UserId, bool IsAdmin)[] members)
		{
			var group = new DepartmentGroup { DepartmentGroupId = id, DepartmentId = DepartmentId, Members = new List<DepartmentGroupMember>() };
			foreach (var member in members)
				group.Members.Add(new DepartmentGroupMember { DepartmentGroupId = id, UserId = member.UserId, IsAdmin = member.IsAdmin });
			return group;
		}

		private void Rule(PermissionTypes type, PermissionActions action, bool lockToGroup = false, string data = null) =>
			_permissions.Setup(x => x.GetPermissionByDepartmentTypeAsync(DepartmentId, type))
				.ReturnsAsync(new Permission { DepartmentId = DepartmentId, PermissionType = (int)type, Action = (int)action, LockToGroup = lockToGroup, Data = data });

		#region 2.3 Remove Personnel

		[Test]
		public async Task A_group_admin_cannot_remove_a_department_admin_in_their_own_group()
		{
			Rule(PermissionTypes.RemovePersonnel, PermissionActions.DepartmentAndGroupAdmins);

			(await _service.CanUserDeleteUserAsync(DepartmentId, GroupAdmin, DeptAdmin)).Should().BeFalse();
		}

		[Test]
		public async Task A_group_admin_cannot_remove_the_managing_user()
		{
			Rule(PermissionTypes.RemovePersonnel, PermissionActions.DepartmentAndGroupAdmins);
			_groups.Setup(x => x.GetGroupForUserAsync(Owner, DepartmentId)).ReturnsAsync(_stationOne);

			(await _service.CanUserDeleteUserAsync(DepartmentId, GroupAdmin, Owner)).Should().BeFalse();
		}

		[Test]
		public async Task A_group_admin_still_removes_a_regular_member_of_their_group_and_a_department_admin_still_removes_an_admin()
		{
			Rule(PermissionTypes.RemovePersonnel, PermissionActions.DepartmentAndGroupAdmins);

			(await _service.CanUserDeleteUserAsync(DepartmentId, GroupAdmin, GroupMember)).Should().BeTrue();
			(await _service.CanUserDeleteUserAsync(DepartmentId, Owner, DeptAdmin)).Should().BeTrue();
		}

		#endregion 2.3 Remove Personnel

		#region 3.4 View Group Users: all people

		[Test]
		public async Task Locked_department_and_group_admins_gives_all_people_to_department_admins_only()
		{
			Rule(PermissionTypes.ViewGroupUsers, PermissionActions.DepartmentAndGroupAdmins, lockToGroup: true);

			(await _service.CanUserViewAllPeopleAsync(GroupAdmin, DepartmentId)).Should().BeFalse("a locked group admin sees their own group");
			(await _service.CanUserViewAllPeopleAsync(DeptAdmin, DepartmentId)).Should().BeTrue();
		}

		[Test]
		public async Task Unlocked_department_and_group_admins_still_gives_group_admins_all_people()
		{
			Rule(PermissionTypes.ViewGroupUsers, PermissionActions.DepartmentAndGroupAdmins);

			(await _service.CanUserViewAllPeopleAsync(GroupAdmin, DepartmentId)).Should().BeTrue();
			(await _service.CanUserViewAllPeopleAsync(GroupMember, DepartmentId)).Should().BeFalse();
		}

		[Test]
		public async Task Locked_everyone_gives_all_people_to_department_admins_but_not_members()
		{
			Rule(PermissionTypes.ViewGroupUsers, PermissionActions.Everyone, lockToGroup: true);

			(await _service.CanUserViewAllPeopleAsync(DeptAdmin, DepartmentId)).Should().BeTrue();
			(await _service.CanUserViewAllPeopleAsync(Owner, DepartmentId)).Should().BeTrue();
			(await _service.CanUserViewAllPeopleAsync(GroupMember, DepartmentId)).Should().BeFalse();
		}

		[Test]
		public async Task No_rule_or_unlocked_everyone_gives_all_people_to_every_member()
		{
			(await _service.CanUserViewAllPeopleAsync(GroupMember, DepartmentId)).Should().BeTrue();

			Rule(PermissionTypes.ViewGroupUsers, PermissionActions.Everyone);
			(await _service.CanUserViewAllPeopleAsync(GroupMember, DepartmentId)).Should().BeTrue();
		}

		#endregion 3.4 View Group Users: all people

		#region 3.6 Matrix fallback

		[Test]
		public async Task Without_a_cached_matrix_person_visibility_follows_the_permission_rows()
		{
			Rule(PermissionTypes.ViewGroupUsers, PermissionActions.Everyone, lockToGroup: true);

			(await _service.CanUserViewPersonViaMatrixAsync(OtherMember, GroupMember, DepartmentId)).Should().BeFalse("other group, locked");
			(await _service.CanUserViewPersonViaMatrixAsync(GroupAdmin, GroupMember, DepartmentId)).Should().BeTrue("same group");
			(await _service.CanUserViewPersonViaMatrixAsync(OtherMember, DeptAdmin, DepartmentId)).Should().BeTrue("department admin");
			(await _service.CanUserViewPersonViaMatrixAsync(GroupMember, GroupMember, DepartmentId)).Should().BeTrue("self");
		}

		[Test]
		public async Task Without_a_cached_matrix_or_a_rule_everyone_still_sees_everyone()
		{
			(await _service.CanUserViewPersonViaMatrixAsync(OtherMember, GroupMember, DepartmentId)).Should().BeTrue();
			(await _service.CanUserViewPersonLocationViaMatrixAsync(OtherMember, GroupMember, DepartmentId)).Should().BeTrue();
			(await _service.CanUserViewUnitViaMatrixAsync(50, GroupMember, DepartmentId)).Should().BeTrue();
			(await _service.CanUserViewUnitLocationViaMatrixAsync(50, GroupMember, DepartmentId)).Should().BeTrue();
		}

		[Test]
		public async Task A_person_missing_from_the_matrix_is_answered_live_and_a_rebuild_is_requested()
		{
			Rule(PermissionTypes.CanSeePersonnelLocations, PermissionActions.DepartmentAdminsOnly);
			_cache.Setup(x => x.GetAsync<VisibilityPayloadUsers>($"ViewUserLocationsSecurityMaxtix_{DepartmentId}"))
				.ReturnsAsync(new VisibilityPayloadUsers { Users = new Dictionary<string, List<string>> { [GroupAdmin] = new List<string> { Owner } } });

			(await _service.CanUserViewPersonLocationViaMatrixAsync(OtherMember, GroupMember, DepartmentId)).Should().BeFalse();
			(await _service.CanUserViewPersonLocationViaMatrixAsync(OtherMember, DeptAdmin, DepartmentId)).Should().BeTrue();
			// A listed person keeps the matrix answer.
			(await _service.CanUserViewPersonLocationViaMatrixAsync(GroupAdmin, DeptAdmin, DepartmentId)).Should().BeFalse();
			// (The rebuild request is debounced process-wide per department, so it is not asserted here.)
		}

		[Test]
		public async Task Without_a_cached_matrix_unit_visibility_follows_the_unit_station()
		{
			Rule(PermissionTypes.ViewGroupUnits, PermissionActions.Everyone, lockToGroup: true);
			Rule(PermissionTypes.CanSeeUnitLocations, PermissionActions.DepartmentAdminsOnly);
			_units.Setup(x => x.GetUnitByIdAsync(50)).ReturnsAsync(new Unit { UnitId = 50, DepartmentId = DepartmentId, StationGroupId = StationTwo });
			_units.Setup(x => x.GetUnitByIdAsync(60)).ReturnsAsync(new Unit { UnitId = 60, DepartmentId = 99, StationGroupId = StationOne });

			(await _service.CanUserViewUnitViaMatrixAsync(50, GroupMember, DepartmentId)).Should().BeFalse();
			(await _service.CanUserViewUnitViaMatrixAsync(50, OtherMember, DepartmentId)).Should().BeTrue();
			(await _service.CanUserViewUnitViaMatrixAsync(60, DeptAdmin, DepartmentId)).Should().BeFalse("another department's unit");
			(await _service.CanUserViewUnitLocationViaMatrixAsync(50, OtherMember, DepartmentId)).Should().BeFalse();
			(await _service.CanUserViewUnitLocationViaMatrixAsync(50, DeptAdmin, DepartmentId)).Should().BeTrue();
		}

		[Test]
		public async Task An_unrestricted_cached_matrix_still_answers_without_reading_the_rows()
		{
			_cache.Setup(x => x.GetAsync<VisibilityPayloadUsers>($"ViewUsersSecurityMaxtix_{DepartmentId}"))
				.ReturnsAsync(new VisibilityPayloadUsers { EveryoneNoGroupLock = true });

			(await _service.CanUserViewPersonViaMatrixAsync(OtherMember, GroupMember, DepartmentId)).Should().BeTrue();
			_permissions.Verify(x => x.GetPermissionByDepartmentTypeAsync(It.IsAny<int>(), It.IsAny<PermissionTypes>()), Times.Never);
		}

		[Test]
		public async Task Live_personnel_matrix_lists_the_same_viewers_as_the_live_checks()
		{
			Rule(PermissionTypes.CanSeePersonnelLocations, PermissionActions.DepartmentAndGroupAdmins, lockToGroup: true);

			var live = await _service.GetLivePersonnelVisibilityAsync(DepartmentId, PermissionTypes.CanSeePersonnelLocations);

			live.EveryoneNoGroupLock.Should().BeFalse();
			live.Users[GroupMember].Should().BeEquivalentTo(new[] { Owner, DeptAdmin, GroupAdmin });
			live.Users[OtherMember].Should().BeEquivalentTo(new[] { Owner, DeptAdmin });
		}

		[Test]
		public async Task Live_unit_matrix_is_unrestricted_without_a_rule()
		{
			var live = await _service.GetLiveUnitVisibilityAsync(DepartmentId, PermissionTypes.CanSeeUnitLocations);

			live.EveryoneNoGroupLock.Should().BeTrue();
		}

		#endregion 3.6 Matrix fallback
	}
}
