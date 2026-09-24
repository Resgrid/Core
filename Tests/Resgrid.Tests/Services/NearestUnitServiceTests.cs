using System;
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
	/// The nearest available unit board. A unit is whatever the department dispatches: here a crisis team,
	/// an apparatus and an individual clinician set up as a unit. Every unit in scope is ranked with its
	/// status, position, ETA, crew shift coverage and crew role mix side by side.
	/// </summary>
	[TestFixture]
	public class NearestUnitServiceTests
	{
		private const int DepartmentId = 1;
		private const string Viewer = "dispatcher";

		// Service Area 1 contains the incident; stations A and B sit under it. Service Area 2 is ~20 km north
		// with station C, and station D in between.
		private const int ServiceArea1 = 10;
		private const int StationA = 11;
		private const int StationB = 12;
		private const int ServiceArea2 = 20;
		private const int StationC = 21;
		private const int StationD = 22;

		private const int TeamUnit = 1;        // crisis team with an assigned crew, ~1 km away
		private const int FarTeamUnit = 2;     // crisis team with no seats filled, ~20 km away
		private const int ApparatusUnit = 3;   // on scene elsewhere
		private const int IndividualUnit = 4;  // a clinician set up as a unit, no GPS

		private const int ClinicianRoleId = 300;
		private const int PeerRoleId = 301;

		private const double IncidentLat = 34.05;
		private const double IncidentLon = -118.25;

		private Mock<IDispatchScopeService> _dispatchScopeService;
		private Mock<IDepartmentGroupsService> _departmentGroupsService;
		private Mock<IUnitsService> _unitsService;
		private Mock<IUsersService> _usersService;
		private Mock<IPersonnelRolesService> _personnelRolesService;
		private Mock<IActionLogsService> _actionLogsService;
		private Mock<IUserStateService> _userStateService;
		private Mock<ICustomStateService> _customStateService;
		private Mock<IPersonnelLocationResolver> _personnelLocationResolver;
		private Mock<IShiftsService> _shiftsService;
		private Mock<IDepartmentSettingsService> _departmentSettingsService;
		private Mock<IGeoService> _geoService;
		private Mock<IAuthorizationService> _authorizationService;

		private DispatchScope _scope;
		private DispatchRecommendationConfig _config;

		[SetUp]
		public void SetUp()
		{
			_dispatchScopeService = new Mock<IDispatchScopeService>();
			_departmentGroupsService = new Mock<IDepartmentGroupsService>();
			_unitsService = new Mock<IUnitsService>();
			_usersService = new Mock<IUsersService>();
			_personnelRolesService = new Mock<IPersonnelRolesService>();
			_actionLogsService = new Mock<IActionLogsService>();
			_userStateService = new Mock<IUserStateService>();
			_customStateService = new Mock<ICustomStateService>();
			_personnelLocationResolver = new Mock<IPersonnelLocationResolver>();
			_shiftsService = new Mock<IShiftsService>();
			_departmentSettingsService = new Mock<IDepartmentSettingsService>();
			_geoService = new Mock<IGeoService>();
			_authorizationService = new Mock<IAuthorizationService>();

			var now = DateTime.UtcNow;

			_scope = DispatchScope.DepartmentWide(DepartmentId, Viewer, DispatchScopeReasons.DepartmentWideRole);
			_dispatchScopeService.Setup(x => x.GetScopeForUserAsync(DepartmentId, Viewer)).ReturnsAsync(() => _scope);

			_config = new DispatchRecommendationConfig();
			_departmentSettingsService.Setup(x => x.GetDispatchRecommendationConfigAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(() => _config);

			_departmentGroupsService.Setup(x => x.GetAllGroupsForDepartmentUnlimitedAsync(DepartmentId)).ReturnsAsync(new List<DepartmentGroup>
			{
				new DepartmentGroup { DepartmentGroupId = ServiceArea1, Name = "Service Area 1", Type = (int)DepartmentGroupTypes.Orginizational, Geofence = Square(34.00, 34.10) },
				new DepartmentGroup { DepartmentGroupId = StationA, Name = "Station A", Type = (int)DepartmentGroupTypes.Station, ParentDepartmentGroupId = ServiceArea1, Latitude = "34.04", Longitude = "-118.24" },
				new DepartmentGroup { DepartmentGroupId = StationB, Name = "Station B", Type = (int)DepartmentGroupTypes.Station, ParentDepartmentGroupId = ServiceArea1 },
				new DepartmentGroup { DepartmentGroupId = ServiceArea2, Name = "Service Area 2", Type = (int)DepartmentGroupTypes.Orginizational, Geofence = Square(34.20, 34.30) },
				new DepartmentGroup { DepartmentGroupId = StationC, Name = "Station C", Type = (int)DepartmentGroupTypes.Station, ParentDepartmentGroupId = ServiceArea2 },
				new DepartmentGroup { DepartmentGroupId = StationD, Name = "Station D", Type = (int)DepartmentGroupTypes.Station, ParentDepartmentGroupId = ServiceArea2, Latitude = "34.15", Longitude = "-118.25" }
			});

			_unitsService.Setup(x => x.GetUnitsForDepartmentUnlimitedAsync(DepartmentId)).ReturnsAsync(new List<Unit>
			{
				new Unit { UnitId = TeamUnit, DepartmentId = DepartmentId, Name = "PMRT A1", Type = "Crisis Team", StationGroupId = StationA },
				new Unit { UnitId = FarTeamUnit, DepartmentId = DepartmentId, Name = "PMRT C1", Type = "Crisis Team", StationGroupId = StationC },
				new Unit { UnitId = ApparatusUnit, DepartmentId = DepartmentId, Name = "Medic B1", Type = "Ambulance", StationGroupId = StationB },
				new Unit { UnitId = IndividualUnit, DepartmentId = DepartmentId, Name = "Dr. Diaz", Type = "Individual", StationGroupId = StationD }
			});
			_unitsService.Setup(x => x.GetAllLatestStatusForUnitsByDepartmentIdAsync(DepartmentId)).ReturnsAsync(new List<UnitState>
			{
				new UnitState { UnitId = TeamUnit, State = (int)UnitStateTypes.Available, Timestamp = now },
				new UnitState { UnitId = FarTeamUnit, State = (int)UnitStateTypes.Available, Timestamp = now },
				new UnitState { UnitId = ApparatusUnit, State = (int)UnitStateTypes.OnScene, Timestamp = now },
				new UnitState { UnitId = IndividualUnit, State = (int)UnitStateTypes.Available, Timestamp = now }
			});
			_unitsService.Setup(x => x.GetLatestUnitLocationsAsync(DepartmentId)).ReturnsAsync(new List<UnitsLocation>
			{
				new UnitsLocation { UnitId = TeamUnit, Latitude = 34.059m, Longitude = -118.25m, Timestamp = now },
				new UnitsLocation { UnitId = FarTeamUnit, Latitude = 34.23m, Longitude = -118.25m, Timestamp = now },
				new UnitsLocation { UnitId = ApparatusUnit, Latitude = 34.051m, Longitude = -118.25m, Timestamp = now }
			});
			_unitsService.Setup(x => x.GetAllActiveRolesForUnitsByDepartmentIdAsync(DepartmentId)).ReturnsAsync(new List<UnitActiveRole>
			{
				new UnitActiveRole { UnitId = TeamUnit, UserId = "clin-a", Role = "Clinician" },
				new UnitActiveRole { UnitId = TeamUnit, UserId = "peer-a", Role = "Peer" },
				new UnitActiveRole { UnitId = ApparatusUnit, UserId = "medic-b", Role = "Medic" }
			});
			_customStateService.Setup(x => x.GetAllActiveUnitStatesForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<CustomState>());
			_customStateService.Setup(x => x.GetActivePersonnelStateForDepartmentAsync(DepartmentId)).ReturnsAsync((CustomState)null);
			_customStateService.Setup(x => x.GetActiveStaffingLevelsForDepartmentAsync(DepartmentId)).ReturnsAsync((CustomState)null);

			_usersService.Setup(x => x.GetUserGroupAndRolesByDepartmentIdAsync(DepartmentId, false, false, false)).ReturnsAsync(new List<UserGroupRole>
			{
				new UserGroupRole { UserId = "clin-a", DepartmentGroupId = StationA, FirstName = "Ana", LastName = "Clinician" },
				new UserGroupRole { UserId = "peer-a", DepartmentGroupId = StationA, FirstName = "Pat", LastName = "Peer" },
				new UserGroupRole { UserId = "medic-b", DepartmentGroupId = StationB, FirstName = "Bo", LastName = "Medic" },
				new UserGroupRole { UserId = "night-c", DepartmentGroupId = StationC, FirstName = "Nia", LastName = "Night" }
			});
			_personnelRolesService.Setup(x => x.GetAllRolesForUsersInDepartmentAsync(DepartmentId)).ReturnsAsync(new Dictionary<string, List<PersonnelRole>>
			{
				{ "clin-a", new List<PersonnelRole> { new PersonnelRole { PersonnelRoleId = ClinicianRoleId, Name = "Clinician" } } },
				{ "peer-a", new List<PersonnelRole> { new PersonnelRole { PersonnelRoleId = PeerRoleId, Name = "Peer Support" } } },
				{ "night-c", new List<PersonnelRole> { new PersonnelRole { PersonnelRoleId = ClinicianRoleId, Name = "Clinician" } } }
			});
			_actionLogsService.Setup(x => x.GetLastActionLogsForDepartmentAsync(DepartmentId, It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>())).ReturnsAsync(new List<ActionLog>
			{
				new ActionLog { UserId = "medic-b", ActionTypeId = (int)ActionTypes.OnScene, Timestamp = now }
			});
			_userStateService.Setup(x => x.GetLatestStatesForDepartmentAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new List<UserState>());
			_personnelLocationResolver.Setup(x => x.GetLatestLocationsAsync(DepartmentId, It.IsAny<int>(), It.IsAny<DateTime?>()))
				.ReturnsAsync(new Dictionary<string, ResolvedPersonnelLocation>
				{
					{ "clin-a", new ResolvedPersonnelLocation { UserId = "clin-a", Latitude = 34.068, Longitude = -118.25, Timestamp = now } }
				});

			// Ana is on the day shift at station A; Nia is on the night shift at station C, where no unit seats are filled.
			_shiftsService.Setup(x => x.GetOnShiftPersonnelAsync(DepartmentId, It.IsAny<DateTime>())).ReturnsAsync(new List<OnShiftAssignment>
			{
				new OnShiftAssignment { UserId = "clin-a", ShiftId = 7, ShiftName = "Day Shift", DepartmentGroupId = StationA },
				new OnShiftAssignment { UserId = "night-c", ShiftId = 8, ShiftName = "Night Shift", DepartmentGroupId = StationC }
			});

			_geoService.Setup(x => x.GetStationCoordinatesAsync(It.IsAny<DepartmentGroup>()))
				.ReturnsAsync((DepartmentGroup g) => GeoMath.ParseCoordinatePair(g.Latitude, g.Longitude));

			_authorizationService.Setup(x => x.CanUserViewUnitViaMatrixAsync(It.IsAny<int>(), Viewer, DepartmentId)).ReturnsAsync(true);
			_authorizationService.Setup(x => x.CanUserViewUnitLocationViaMatrixAsync(It.IsAny<int>(), Viewer, DepartmentId)).ReturnsAsync(true);
			_authorizationService.Setup(x => x.CanUserViewPersonViaMatrixAsync(It.IsAny<string>(), Viewer, DepartmentId)).ReturnsAsync(true);
			_authorizationService.Setup(x => x.CanUserViewPersonLocationViaMatrixAsync(It.IsAny<string>(), Viewer, DepartmentId)).ReturnsAsync(true);
		}

		private NearestUnitService BuildService()
		{
			return new NearestUnitService(_dispatchScopeService.Object, _departmentGroupsService.Object, _unitsService.Object, _usersService.Object,
				_personnelRolesService.Object, _actionLogsService.Object, _userStateService.Object, _customStateService.Object,
				_personnelLocationResolver.Object, _shiftsService.Object, _departmentSettingsService.Object, _geoService.Object,
				_authorizationService.Object);
		}

		private Task<NearestUnitBoard> GetBoardAsync(bool? useRoadEta = null)
		{
			return BuildService().GetBoardAsync(new NearestUnitRequest
			{
				DepartmentId = DepartmentId,
				UserId = Viewer,
				Latitude = IncidentLat,
				Longitude = IncidentLon,
				UseRoadEta = useRoadEta
			});
		}

		private static string Square(double south, double north)
		{
			return FormattableString.Invariant(
				$"[{{\"lat\":{south},\"lng\":-118.30}},{{\"lat\":{north},\"lng\":-118.30}},{{\"lat\":{north},\"lng\":-118.20}},{{\"lat\":{south},\"lng\":-118.20}}]");
		}

		private static NearestUnitResult UnitRow(NearestUnitBoard board, int unitId) => board.Units.Single(u => u.UnitId == unitId);

		[Test]
		public async Task every_unit_is_ranked_available_first_then_by_eta_whatever_kind_of_unit_it_is()
		{
			var board = await GetBoardAsync();

			// A crisis team, an individual and another team are available; the ambulance is on scene.
			board.Units.Select(u => u.UnitId).Should().Equal(TeamUnit, IndividualUnit, FarTeamUnit, ApparatusUnit);
			UnitRow(board, ApparatusUnit).IsAvailable.Should().BeFalse();
			UnitRow(board, ApparatusUnit).StatusText.Should().Be(UnitStateTypes.OnScene.ToString());
		}

		[Test]
		public async Task a_unit_row_carries_its_type_station_area_and_boundary()
		{
			var team = UnitRow(await GetBoardAsync(), TeamUnit);

			team.Name.Should().Be("PMRT A1");
			team.UnitType.Should().Be("Crisis Team");
			team.GroupName.Should().Be("Station A");
			team.ParentGroupName.Should().Be("Service Area 1");
			team.IncidentInGroupBoundary.Should().BeFalse("station A has no boundary of its own");
			team.IncidentInParentBoundary.Should().BeTrue();
		}

		[Test]
		public async Task a_unit_with_assigned_seats_takes_its_crew_shift_coverage_and_role_mix_from_them()
		{
			var team = UnitRow(await GetBoardAsync(), TeamUnit);

			team.CrewSource.Should().Be(UnitCrewSources.Assigned);
			team.Crew.Should().Equal("Ana Clinician", "Pat Peer");
			team.CrewCount.Should().Be(2);
			team.CrewAvailableCount.Should().Be(2);
			team.OnShiftCount.Should().Be(1);
			team.ShiftNames.Should().Equal("Day Shift");
			team.RoleMix.Select(r => (r.Name, r.Count)).Should().Equal(("Clinician", 1), ("Peer Support", 1));
		}

		[Test]
		public async Task a_unit_with_no_seats_filled_takes_its_crew_from_the_shift_running_at_its_station()
		{
			var board = await GetBoardAsync();
			var farTeam = UnitRow(board, FarTeamUnit);

			farTeam.CrewSource.Should().Be(UnitCrewSources.StationShift);
			farTeam.Crew.Should().Equal("Nia Night");
			farTeam.OnShiftCount.Should().Be(1);
			farTeam.ShiftNames.Should().Equal("Night Shift");
			farTeam.RoleMix.Should().ContainSingle(r => r.Name == "Clinician");

			UnitRow(board, IndividualUnit).CrewSource.Should().Be(UnitCrewSources.None);
		}

		[Test]
		public async Task the_eta_comes_from_the_units_live_position()
		{
			var team = UnitRow(await GetBoardAsync(), TeamUnit);

			team.PositionSource.Should().Be(UnitPositionSources.Live);
			team.Latitude.Should().Be(34.059);
			team.DistanceMeters.Should().BeApproximately(1000, 50);
			team.EtaSource.Should().Be(EtaSources.Estimated);
			team.EtaSeconds.Should().Be(NearestUnitService.EstimateEtaSeconds(team.DistanceMeters.Value));
		}

		[Test]
		public async Task a_unit_without_gps_is_placed_at_its_station()
		{
			var individual = UnitRow(await GetBoardAsync(), IndividualUnit);

			individual.PositionSource.Should().Be(UnitPositionSources.Station);
			individual.Latitude.Should().Be(34.15);
			individual.EtaSource.Should().Be(EtaSources.Estimated);
			individual.DistanceMeters.Should().BeApproximately(11100, 200);
		}

		[Test]
		public async Task individual_responders_are_listed_with_the_unit_they_are_on()
		{
			var board = await GetBoardAsync();

			board.Personnel.Should().HaveCount(4);
			var ana = board.Personnel.Single(p => p.UserId == "clin-a");
			ana.UnitId.Should().Be(TeamUnit);
			ana.UnitName.Should().Be("PMRT A1");
			ana.IsOnShift.Should().BeTrue();
			board.Personnel.Single(p => p.UserId == "night-c").UnitId.Should().BeNull();
			board.Personnel.Single(p => p.UserId == "medic-b").IsAvailable.Should().BeFalse();
		}

		[Test]
		public async Task boundaries_containing_the_incident_are_listed()
		{
			var board = await GetBoardAsync();

			board.ContainingBoundaries.Select(b => b.DepartmentGroupId).Should().Equal(ServiceArea1);
		}

		[Test]
		public async Task a_scoped_supervisor_sees_only_their_areas_units()
		{
			_scope = new DispatchScope
			{
				DepartmentId = DepartmentId,
				UserId = Viewer,
				Reason = DispatchScopeReasons.GroupAdmin,
				AnchorGroupId = ServiceArea1,
				GroupIds = new HashSet<int> { ServiceArea1, StationA, StationB }
			};

			var board = await GetBoardAsync();

			board.IsDepartmentWide.Should().BeFalse();
			board.Units.Select(u => u.UnitId).Should().BeEquivalentTo(new[] { TeamUnit, ApparatusUnit });
			board.Personnel.Should().NotContain(p => p.UserId == "night-c");
			board.ContainingBoundaries.Should().ContainSingle(b => b.DepartmentGroupId == ServiceArea1);
		}

		[Test]
		public async Task road_etas_go_to_the_closest_available_units_first_and_failures_keep_the_estimate()
		{
			_config.EtaShortlistSize = 2;
			_geoService.Setup(x => x.GetEtaInSecondsAsync("34.059,-118.25", "34.05,-118.25")).ReturnsAsync(180);
			_geoService.Setup(x => x.GetEtaInSecondsAsync(It.Is<string>(s => s != "34.059,-118.25"), "34.05,-118.25")).ReturnsAsync(-1);

			var board = await GetBoardAsync(useRoadEta: true);

			// The two closest available units (the team, then the individual at station D), not the responders.
			_geoService.Verify(x => x.GetEtaInSecondsAsync("34.059,-118.25", "34.05,-118.25"), Times.Once);
			_geoService.Verify(x => x.GetEtaInSecondsAsync("34.15,-118.25", "34.05,-118.25"), Times.Once);
			_geoService.Verify(x => x.GetEtaInSecondsAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Exactly(2));

			UnitRow(board, TeamUnit).EtaSeconds.Should().Be(180);
			UnitRow(board, TeamUnit).EtaSource.Should().Be(EtaSources.Road);
			UnitRow(board, IndividualUnit).EtaSource.Should().Be(EtaSources.Estimated);
		}

		[Test]
		public async Task without_routing_no_lookups_are_made()
		{
			var board = await GetBoardAsync();

			_geoService.Verify(x => x.GetEtaInSecondsAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
			board.Notes.Should().NotBeEmpty();
		}

		[Test]
		public async Task a_hidden_unit_location_is_withheld_but_still_ranked()
		{
			_authorizationService.Setup(x => x.CanUserViewUnitLocationViaMatrixAsync(TeamUnit, Viewer, DepartmentId)).ReturnsAsync(false);

			var team = UnitRow(await GetBoardAsync(), TeamUnit);

			team.LocationHidden.Should().BeTrue();
			team.Latitude.Should().BeNull();
			team.DistanceMeters.Should().NotBeNull();
			team.EtaSeconds.Should().NotBeNull();
		}

		[Test]
		public async Task crew_the_viewer_cannot_see_count_toward_the_unit_but_are_not_named()
		{
			_authorizationService.Setup(x => x.CanUserViewPersonViaMatrixAsync("peer-a", Viewer, DepartmentId)).ReturnsAsync(false);

			var board = await GetBoardAsync();
			var team = UnitRow(board, TeamUnit);

			team.CrewCount.Should().Be(2);
			team.Crew.Should().Equal("Ana Clinician");
			board.Personnel.Should().NotContain(p => p.UserId == "peer-a");
		}

		[Test]
		public async Task units_the_viewer_cannot_see_are_left_off()
		{
			_authorizationService.Setup(x => x.CanUserViewUnitViaMatrixAsync(TeamUnit, Viewer, DepartmentId)).ReturnsAsync(false);

			var board = await GetBoardAsync();

			board.Units.Should().NotContain(u => u.UnitId == TeamUnit);
		}

		[Test]
		public async Task an_incident_without_a_location_returns_an_empty_board()
		{
			var board = await BuildService().GetBoardAsync(new NearestUnitRequest { DepartmentId = DepartmentId, UserId = Viewer, Latitude = 0, Longitude = 0 });

			board.Units.Should().BeEmpty();
			board.Notes.Should().ContainSingle();
			_dispatchScopeService.Verify(x => x.GetScopeForUserAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
		}
	}
}
