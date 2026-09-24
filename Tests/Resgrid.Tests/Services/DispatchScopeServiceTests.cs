using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// Two dispatch models on one department: area supervisors dispatch their own service area by day,
	/// a central dispatch center (a department-wide role) dispatches every area after hours.
	/// </summary>
	[TestFixture]
	public class DispatchScopeServiceTests
	{
		private const int DepartmentId = 1;
		private const int ServiceArea1 = 10;
		private const int StationA = 11;
		private const int ServiceArea2 = 20;
		private const int StationC = 21;
		private const int AccessCenterRoleId = 500;
		private const int ClinicianRoleId = 501;

		private const string Owner = "owner";
		private const string Admin = "admin-1";
		private const string Supervisor1 = "supervisor-1";
		private const string StationAMember = "member-a";
		private const string StationCMember = "member-c";
		private const string Dispatcher = "access-1";
		private const string Ungrouped = "ungrouped";

		// Service Area 1 covers lat 34.00..34.10, Service Area 2 covers lat 34.20..34.30 (same longitudes).
		private const string InArea1 = "34.05,-118.25";
		private const string InArea2 = "34.25,-118.25";

		private Mock<IDepartmentSettingsService> _departmentSettingsService;
		private Mock<IDepartmentsService> _departmentsService;
		private Mock<IDepartmentGroupsService> _departmentGroupsService;
		private Mock<IPersonnelRolesService> _personnelRolesService;
		private Mock<IUnitsService> _unitsService;
		private Mock<ICallsService> _callsService;

		private GroupDispatchScopeConfig _config;
		private Dictionary<string, List<PersonnelRole>> _roles;

		[SetUp]
		public void SetUp()
		{
			_departmentSettingsService = new Mock<IDepartmentSettingsService>();
			_departmentsService = new Mock<IDepartmentsService>();
			_departmentGroupsService = new Mock<IDepartmentGroupsService>();
			_personnelRolesService = new Mock<IPersonnelRolesService>();
			_unitsService = new Mock<IUnitsService>();
			_callsService = new Mock<ICallsService>();

			_config = new GroupDispatchScopeConfig { Enabled = true, DepartmentWideRoleIds = new List<int> { AccessCenterRoleId } };
			_departmentSettingsService.Setup(x => x.GetGroupDispatchScopeConfigAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(() => _config);

			_departmentsService.Setup(x => x.GetDepartmentMemberAsync(It.IsAny<string>(), DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync((string userId, int departmentId, bool bypass) =>
					userId == "stranger" ? null : new DepartmentMember { UserId = userId, DepartmentId = DepartmentId, IsAdmin = userId == Admin });
			_departmentsService.Setup(x => x.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync(new Department { DepartmentId = DepartmentId, ManagingUserId = Owner });

			_roles = new Dictionary<string, List<PersonnelRole>>
			{
				{ Dispatcher, new List<PersonnelRole> { new PersonnelRole { PersonnelRoleId = AccessCenterRoleId, Name = "ACCESS Center" } } },
				{ StationAMember, new List<PersonnelRole> { new PersonnelRole { PersonnelRoleId = ClinicianRoleId, Name = "Clinician" } } }
			};
			_personnelRolesService.Setup(x => x.GetRolesForUserAsync(It.IsAny<string>(), DepartmentId))
				.ReturnsAsync((string userId, int departmentId) => _roles.TryGetValue(userId, out var r) ? r : new List<PersonnelRole>());

			_departmentGroupsService.Setup(x => x.GetAllGroupsForDepartmentUnlimitedAsync(DepartmentId)).ReturnsAsync(() => new List<DepartmentGroup>
			{
				Group(ServiceArea1, null, DepartmentGroupTypes.Orginizational, Square(34.00, 34.10), new DepartmentGroupMember { UserId = Supervisor1, IsAdmin = true }),
				Group(StationA, ServiceArea1, DepartmentGroupTypes.Station, null, new DepartmentGroupMember { UserId = StationAMember, IsAdmin = false }),
				Group(ServiceArea2, null, DepartmentGroupTypes.Orginizational, Square(34.20, 34.30)),
				Group(StationC, ServiceArea2, DepartmentGroupTypes.Orginizational, null, new DepartmentGroupMember { UserId = StationCMember, IsAdmin = false })
			});

			_unitsService.Setup(x => x.GetUnitsForDepartmentUnlimitedAsync(DepartmentId)).ReturnsAsync(new List<Unit>
			{
				new Unit { UnitId = 1, DepartmentId = DepartmentId, Name = "PMRT A1", StationGroupId = StationA },
				new Unit { UnitId = 2, DepartmentId = DepartmentId, Name = "PMRT C1", StationGroupId = StationC }
			});
		}

		private DispatchScopeService BuildService()
		{
			return new DispatchScopeService(_departmentSettingsService.Object, _departmentsService.Object, _departmentGroupsService.Object,
				_personnelRolesService.Object, _unitsService.Object, _callsService.Object);
		}

		private static DepartmentGroup Group(int id, int? parentId, DepartmentGroupTypes type, string geofence, params DepartmentGroupMember[] members)
		{
			return new DepartmentGroup
			{
				DepartmentGroupId = id,
				DepartmentId = DepartmentId,
				Name = "Group " + id,
				Type = (int)type,
				ParentDepartmentGroupId = parentId,
				Geofence = geofence,
				Members = members.Select(m => { m.DepartmentGroupId = id; m.DepartmentId = DepartmentId; return m; }).ToList()
			};
		}

		private static string Square(double south, double north)
		{
			return System.FormattableString.Invariant(
				$"[{{\"lat\":{south},\"lng\":-118.30}},{{\"lat\":{north},\"lng\":-118.30}},{{\"lat\":{north},\"lng\":-118.20}},{{\"lat\":{south},\"lng\":-118.20}}]");
		}

		/// <summary>A call with its dispatch collections already loaded (so no lookups happen).</summary>
		private static Call NewCall(int callId, string location, string reportedBy = "someone-else", int? groupDispatch = null, int? unitDispatch = null, string personDispatch = null)
		{
			return new Call
			{
				CallId = callId,
				DepartmentId = DepartmentId,
				GeoLocationData = location,
				ReportingUserId = reportedBy,
				GroupDispatches = groupDispatch.HasValue ? new List<CallDispatchGroup> { new CallDispatchGroup { CallId = callId, DepartmentGroupId = groupDispatch.Value } } : new List<CallDispatchGroup>(),
				UnitDispatches = unitDispatch.HasValue ? new List<CallDispatchUnit> { new CallDispatchUnit { CallId = callId, UnitId = unitDispatch.Value } } : new List<CallDispatchUnit>(),
				Dispatches = personDispatch != null ? new List<CallDispatch> { new CallDispatch { CallId = callId, UserId = personDispatch } } : new List<CallDispatch>()
			};
		}

		#region Resolving scope

		[Test]
		public async Task scoping_off_leaves_everyone_department_wide()
		{
			_config = new GroupDispatchScopeConfig();

			var scope = await BuildService().GetScopeForUserAsync(DepartmentId, StationAMember);

			scope.IsDepartmentWide.Should().BeTrue();
			scope.Reason.Should().Be(DispatchScopeReasons.ScopingDisabled);
			(await BuildService().IsCallInScopeAsync(scope, NewCall(1, InArea2))).Should().BeTrue();
		}

		[Test]
		public async Task department_admins_and_the_managing_user_are_department_wide()
		{
			var service = BuildService();

			(await service.GetScopeForUserAsync(DepartmentId, Admin)).Reason.Should().Be(DispatchScopeReasons.DepartmentAdmin);
			(await service.GetScopeForUserAsync(DepartmentId, Owner)).Reason.Should().Be(DispatchScopeReasons.DepartmentAdmin);
		}

		[Test]
		public async Task a_dispatch_center_role_is_department_wide()
		{
			var scope = await BuildService().GetScopeForUserAsync(DepartmentId, Dispatcher);

			scope.IsDepartmentWide.Should().BeTrue();
			scope.Reason.Should().Be(DispatchScopeReasons.DepartmentWideRole);
		}

		[Test]
		public async Task hand_off_is_a_role_change_that_applies_on_the_next_request()
		{
			// After hours: the supervisor is given the dispatch center role...
			_roles[Supervisor1] = new List<PersonnelRole> { new PersonnelRole { PersonnelRoleId = AccessCenterRoleId } };
			(await BuildService().GetScopeForUserAsync(DepartmentId, Supervisor1)).IsDepartmentWide.Should().BeTrue();

			// ...and in the morning it is taken away again; nothing else changed.
			_roles.Remove(Supervisor1);
			var scope = await BuildService().GetScopeForUserAsync(DepartmentId, Supervisor1);

			scope.IsDepartmentWide.Should().BeFalse();
			scope.GroupIds.Should().BeEquivalentTo(new[] { ServiceArea1, StationA });
		}

		[Test]
		public async Task a_supervisor_scope_is_their_area_and_every_group_beneath_it()
		{
			var scope = await BuildService().GetScopeForUserAsync(DepartmentId, Supervisor1);

			scope.IsDepartmentWide.Should().BeFalse();
			scope.Reason.Should().Be(DispatchScopeReasons.GroupAdmin);
			scope.AnchorGroupId.Should().Be(ServiceArea1);
			scope.GroupIds.Should().BeEquivalentTo(new[] { ServiceArea1, StationA });
		}

		[Test]
		public async Task a_station_member_scope_is_their_station()
		{
			var scope = await BuildService().GetScopeForUserAsync(DepartmentId, StationAMember);

			scope.Reason.Should().Be(DispatchScopeReasons.GroupMember);
			scope.GroupIds.Should().BeEquivalentTo(new[] { StationA });
		}

		[Test]
		public async Task an_ungrouped_member_and_a_non_member_get_an_empty_scope()
		{
			var service = BuildService();

			var ungrouped = await service.GetScopeForUserAsync(DepartmentId, Ungrouped);
			ungrouped.IsDepartmentWide.Should().BeFalse();
			ungrouped.Reason.Should().Be(DispatchScopeReasons.NoGroup);
			ungrouped.GroupIds.Should().BeEmpty();

			(await service.GetScopeForUserAsync(DepartmentId, "stranger")).IsDepartmentWide.Should().BeFalse();
		}

		#endregion

		#region Which calls are in scope

		[Test]
		public async Task a_call_inside_the_supervisors_area_is_in_scope()
		{
			(await BuildService().CanUserAccessCallAsync(DepartmentId, Supervisor1, NewCall(1, InArea1))).Should().BeTrue();
		}

		[Test]
		public async Task a_call_in_another_area_is_out_of_scope()
		{
			(await BuildService().CanUserAccessCallAsync(DepartmentId, Supervisor1, NewCall(1, InArea2))).Should().BeFalse();
		}

		[Test]
		public async Task a_call_elsewhere_that_one_of_the_areas_stations_or_units_was_dispatched_to_is_in_scope()
		{
			var service = BuildService();

			(await service.CanUserAccessCallAsync(DepartmentId, Supervisor1, NewCall(1, InArea2, groupDispatch: StationA))).Should().BeTrue("the station was dispatched");
			(await service.CanUserAccessCallAsync(DepartmentId, Supervisor1, NewCall(2, InArea2, unitDispatch: 1))).Should().BeTrue("a unit at the station was dispatched");
			(await service.CanUserAccessCallAsync(DepartmentId, Supervisor1, NewCall(3, InArea2, personDispatch: StationAMember))).Should().BeTrue("a member of the station was dispatched");
			(await service.CanUserAccessCallAsync(DepartmentId, Supervisor1, NewCall(4, InArea2, unitDispatch: 2))).Should().BeFalse("only another area's unit was dispatched");
		}

		[Test]
		public async Task a_member_keeps_calls_they_reported_or_were_dispatched_to()
		{
			var service = BuildService();

			(await service.CanUserAccessCallAsync(DepartmentId, Ungrouped, NewCall(1, InArea2, reportedBy: Ungrouped))).Should().BeTrue();
			(await service.CanUserAccessCallAsync(DepartmentId, Ungrouped, NewCall(2, InArea2, personDispatch: Ungrouped))).Should().BeTrue();
			(await service.CanUserAccessCallAsync(DepartmentId, Ungrouped, NewCall(3, InArea1))).Should().BeFalse();
		}

		[Test]
		public async Task a_call_without_a_location_is_only_in_scope_through_its_dispatches()
		{
			var service = BuildService();

			(await service.CanUserAccessCallAsync(DepartmentId, Supervisor1, NewCall(1, null))).Should().BeFalse();
			(await service.CanUserAccessCallAsync(DepartmentId, Supervisor1, NewCall(2, null, groupDispatch: ServiceArea1))).Should().BeTrue();
		}

		[Test]
		public async Task the_dispatch_center_sees_every_area()
		{
			var service = BuildService();

			(await service.CanUserAccessCallAsync(DepartmentId, Dispatcher, NewCall(1, InArea1))).Should().BeTrue();
			(await service.CanUserAccessCallAsync(DepartmentId, Dispatcher, NewCall(2, InArea2))).Should().BeTrue();
		}

		[Test]
		public async Task a_call_from_another_department_is_never_in_scope()
		{
			var call = NewCall(1, InArea1);
			call.DepartmentId = 99;

			(await BuildService().CanUserAccessCallAsync(DepartmentId, Admin, call)).Should().BeFalse();
		}

		[Test]
		public async Task filtering_drops_out_of_scope_calls_and_keeps_order()
		{
			var calls = new List<Call>
			{
				NewCall(3, InArea1),
				NewCall(1, InArea2),
				NewCall(2, InArea2, groupDispatch: StationA)
			};

			var filtered = await BuildService().FilterCallsForUserAsync(DepartmentId, Supervisor1, calls);

			filtered.Select(c => c.CallId).Should().Equal(3, 2);
		}

		[Test]
		public async Task filtering_is_a_no_op_when_scoping_is_off()
		{
			_config = new GroupDispatchScopeConfig();
			var calls = new List<Call> { NewCall(1, InArea2), NewCall(2, null) };

			var filtered = await BuildService().FilterCallsForUserAsync(DepartmentId, StationCMember, calls);

			filtered.Should().BeSameAs(calls);
			_callsService.VerifyNoOtherCalls();
		}

		#endregion
	}
}
