using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	public class DispatchRecommendationServiceTests
	{
		private const int DepartmentId = 1;
		private const int EngineTypeId = 100;
		private const int LadderTypeId = 101;
		private const int FirefighterRoleId = 200;
		private const int StationAId = 10;
		private const int StationBId = 20;

		private Mock<IRunCardsService> _runCardsService;
		private Mock<IUnitsService> _unitsService;
		private Mock<IActionLogsService> _actionLogsService;
		private Mock<IUserStateService> _userStateService;
		private Mock<IPersonnelRolesService> _personnelRolesService;
		private Mock<ICustomStateService> _customStateService;
		private Mock<IDepartmentGroupsService> _departmentGroupsService;
		private Mock<IDepartmentSettingsService> _departmentSettingsService;
		private Mock<IGeoService> _geoService;
		private Mock<IPersonnelLocationResolver> _personnelLocationResolver;
		private Mock<IShiftsService> _shiftsService;
		private Mock<Resgrid.Model.Repositories.IRunCardActivationsRepository> _runCardActivationsRepository;
		private Mock<Resgrid.Model.Providers.IEventAggregator> _eventAggregator;
		private DispatchRecommendationService _service;

		private DepartmentGroup _stationA;
		private DepartmentGroup _stationB;
		private DispatchRecommendationConfig _config;

		[SetUp]
		public void SetUp()
		{
			_runCardsService = new Mock<IRunCardsService>();
			_unitsService = new Mock<IUnitsService>();
			_actionLogsService = new Mock<IActionLogsService>();
			_userStateService = new Mock<IUserStateService>();
			_personnelRolesService = new Mock<IPersonnelRolesService>();
			_customStateService = new Mock<ICustomStateService>();
			_departmentGroupsService = new Mock<IDepartmentGroupsService>();
			_departmentSettingsService = new Mock<IDepartmentSettingsService>();
			_geoService = new Mock<IGeoService>();
			_personnelLocationResolver = new Mock<IPersonnelLocationResolver>();
			_shiftsService = new Mock<IShiftsService>();
			_runCardActivationsRepository = new Mock<Resgrid.Model.Repositories.IRunCardActivationsRepository>();
			_eventAggregator = new Mock<Resgrid.Model.Providers.IEventAggregator>();

			_stationA = new DepartmentGroup { DepartmentGroupId = StationAId, DepartmentId = DepartmentId, Name = "Station 1", Type = (int)DepartmentGroupTypes.Station };
			_stationB = new DepartmentGroup { DepartmentGroupId = StationBId, DepartmentId = DepartmentId, Name = "Station 2", Type = (int)DepartmentGroupTypes.Station };
			_config = new DispatchRecommendationConfig();

			_departmentSettingsService.Setup(x => x.GetDispatchRecommendationModeAsync(DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync(DispatchRecommendationModes.StationBased);
			_departmentSettingsService.Setup(x => x.GetDispatchRecommendationAutoDispatchAsync(DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync(false);
			_departmentSettingsService.Setup(x => x.GetDispatchRecommendationConfigAsync(DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync(() => _config);
			_departmentSettingsService.Setup(x => x.GetDispatchShiftInsteadOfGroupAsync(DepartmentId))
				.ReturnsAsync(false);

			_unitsService.Setup(x => x.GetUnitsForDepartmentUnlimitedAsync(DepartmentId))
				.ReturnsAsync(new List<Unit>
				{
					new Unit { UnitId = 1, DepartmentId = DepartmentId, Name = "Engine 1", Type = "Engine", StationGroupId = StationAId, StationGroup = _stationA },
					new Unit { UnitId = 2, DepartmentId = DepartmentId, Name = "Engine 2", Type = "Engine", StationGroupId = StationBId, StationGroup = _stationB },
					new Unit { UnitId = 3, DepartmentId = DepartmentId, Name = "Ladder 1", Type = "Ladder", StationGroupId = StationAId, StationGroup = _stationA }
				});
			_unitsService.Setup(x => x.GetAllLatestStatusForUnitsByDepartmentIdAsync(DepartmentId))
				.ReturnsAsync(new List<UnitState>());
			_unitsService.Setup(x => x.GetUnitTypesForDepartmentAsync(DepartmentId))
				.ReturnsAsync(new List<UnitType>
				{
					new UnitType { UnitTypeId = EngineTypeId, DepartmentId = DepartmentId, Type = "Engine" },
					new UnitType { UnitTypeId = LadderTypeId, DepartmentId = DepartmentId, Type = "Ladder" }
				});
			_unitsService.Setup(x => x.GetLatestUnitLocationsAsync(DepartmentId))
				.ReturnsAsync(new List<UnitsLocation>());
			_unitsService.Setup(x => x.GetUnitStaffingForDepartmentAsync(DepartmentId))
				.ReturnsAsync(new Dictionary<int, UnitRoleStaffingResult>());

			_customStateService.Setup(x => x.GetAllActiveUnitStatesForDepartmentAsync(DepartmentId))
				.ReturnsAsync(new List<CustomState>());
			_customStateService.Setup(x => x.GetActivePersonnelStateForDepartmentAsync(DepartmentId))
				.ReturnsAsync((CustomState)null);
			_customStateService.Setup(x => x.GetActiveStaffingLevelsForDepartmentAsync(DepartmentId))
				.ReturnsAsync((CustomState)null);

			_personnelRolesService.Setup(x => x.GetAllRolesForUsersInDepartmentAsync(DepartmentId))
				.ReturnsAsync(new Dictionary<string, List<PersonnelRole>>
				{
					{ "user-1", new List<PersonnelRole> { new PersonnelRole { PersonnelRoleId = FirefighterRoleId, Name = "Firefighter" } } },
					{ "user-2", new List<PersonnelRole> { new PersonnelRole { PersonnelRoleId = FirefighterRoleId, Name = "Firefighter" } } }
				});
			_actionLogsService.Setup(x => x.GetLastActionLogsForDepartmentAsync(DepartmentId, It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()))
				.ReturnsAsync(new List<ActionLog>());
			_userStateService.Setup(x => x.GetLatestStatesForDepartmentAsync(DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync(new List<UserState>());
			_departmentGroupsService.Setup(x => x.GetAllDepartmentGroupsForDepartmentAsync(DepartmentId))
				.ReturnsAsync(new Dictionary<string, DepartmentGroup>
				{
					{ "user-1", _stationA },
					{ "user-2", _stationB }
				});
			_departmentGroupsService.Setup(x => x.GetGroupByIdAsync(StationAId, It.IsAny<bool>())).ReturnsAsync(_stationA);
			_departmentGroupsService.Setup(x => x.GetGroupByIdAsync(StationBId, It.IsAny<bool>())).ReturnsAsync(_stationB);

			_geoService.Setup(x => x.OrderStationsByDistanceAsync(DepartmentId, It.IsAny<double>(), It.IsAny<double>()))
				.ReturnsAsync(new List<StationDistanceResult>
				{
					new StationDistanceResult { Station = _stationA, Latitude = 39.75, Longitude = -104.95, DistanceMeters = 100, ContainsPoint = true, HasGeofence = true },
					new StationDistanceResult { Station = _stationB, Latitude = 39.70, Longitude = -104.90, DistanceMeters = 5000, ContainsPoint = false, HasGeofence = true }
				});
			_geoService.Setup(x => x.GetStationCoordinatesAsync(It.IsAny<DepartmentGroup>()))
				.ReturnsAsync(new GeoMath.GeoPoint(39.75, -104.95));

			_personnelLocationResolver.Setup(x => x.GetLatestLocationsAsync(DepartmentId, It.IsAny<int>(), It.IsAny<DateTime?>()))
				.ReturnsAsync(new Dictionary<string, ResolvedPersonnelLocation>());

			_runCardsService.Setup(x => x.GetLastUnitDispatchTimesAsync(DepartmentId))
				.ReturnsAsync(new Dictionary<int, DateTime>());
			_runCardsService.Setup(x => x.GetLastUserDispatchTimesAsync(DepartmentId))
				.ReturnsAsync(new Dictionary<string, DateTime>());
			_runCardsService.Setup(x => x.GetStationCoverageRequirementsForDepartmentAsync(DepartmentId))
				.ReturnsAsync(new List<StationCoverageRequirement>());

			_service = new DispatchRecommendationService(
				_runCardsService.Object,
				_unitsService.Object,
				_actionLogsService.Object,
				_userStateService.Object,
				_personnelRolesService.Object,
				_customStateService.Object,
				_departmentGroupsService.Object,
				_departmentSettingsService.Object,
				_geoService.Object,
				_personnelLocationResolver.Object,
				_shiftsService.Object,
				_runCardActivationsRepository.Object,
				_eventAggregator.Object);
		}

		private RunCard BuildCard(int engineCount = 0, int roleCount = 0, int alarmLevel = 1)
		{
			var level = new RunCardAlarmLevel
			{
				RunCardAlarmLevelId = 50,
				RunCardId = 5,
				AlarmLevel = alarmLevel,
				UnitRequirements = new List<RunCardUnitRequirement>(),
				RoleRequirements = new List<RunCardRoleRequirement>()
			};

			if (engineCount > 0)
				level.UnitRequirements.Add(new RunCardUnitRequirement { RunCardUnitRequirementId = 1000, RunCardAlarmLevelId = 50, UnitTypeId = EngineTypeId, RequiredCount = engineCount });

			if (roleCount > 0)
				level.RoleRequirements.Add(new RunCardRoleRequirement { RunCardRoleRequirementId = 2000, RunCardAlarmLevelId = 50, PersonnelRoleId = FirefighterRoleId, RequiredCount = roleCount });

			var card = new RunCard
			{
				RunCardId = 5,
				DepartmentId = DepartmentId,
				Name = "Structure Fire",
				AlarmLevels = new List<RunCardAlarmLevel> { level },
				Triggers = new List<RunCardTrigger>(),
				AvailabilitySelections = new List<RunCardAvailabilitySelection>()
			};

			_runCardsService.Setup(x => x.GetMatchingRunCardAsync(DepartmentId, It.IsAny<int>(), It.IsAny<string>()))
				.ReturnsAsync(card);

			return card;
		}

		private static DispatchRecommendationRequest BuildRequest(double? lat = 39.75, double? lon = -104.95)
		{
			return new DispatchRecommendationRequest
			{
				DepartmentId = DepartmentId,
				Priority = 3,
				CallTypeName = "Structure Fire",
				Latitude = lat,
				Longitude = lon,
				TargetAlarmLevel = 1
			};
		}

		[Test]
		public async Task returns_noop_result_when_no_card_matches()
		{
			_runCardsService.Setup(x => x.GetMatchingRunCardAsync(DepartmentId, It.IsAny<int>(), It.IsAny<string>()))
				.ReturnsAsync((RunCard)null);

			var result = await _service.GetRecommendationAsync(BuildRequest());

			result.MatchedRunCardId.Should().BeNull();
			result.HasRecommendations.Should().BeFalse();
		}

		[Test]
		public async Task station_based_fills_from_containing_station_then_cascades()
		{
			BuildCard(engineCount: 2);

			var result = await _service.GetRecommendationAsync(BuildRequest());

			result.Units.Should().HaveCount(2);
			result.Units[0].UnitId.Should().Be(1);
			result.Units[0].SelectionReason.Should().Be(RecommendationSelectionReasons.InGeofence);
			result.Units[0].CascadeDepth.Should().Be(0);
			result.Units[1].UnitId.Should().Be(2);
			result.Units[1].SelectionReason.Should().Be(RecommendationSelectionReasons.CascadeStation);
			result.Units[1].CascadeDepth.Should().Be(1);
			result.Shortfalls.Should().BeEmpty();
		}

		[Test]
		public async Task station_based_reports_shortfall_when_stations_exhausted()
		{
			BuildCard(engineCount: 3);

			var result = await _service.GetRecommendationAsync(BuildRequest());

			result.Units.Should().HaveCount(2);
			result.Shortfalls.Should().ContainSingle(s =>
				s.IsUnitRequirement && s.RequiredCount == 3 && s.FilledCount == 2 && s.Reason == RequirementShortfallReasons.StationsExhausted);
		}

		[Test]
		public async Task committed_units_are_not_candidates()
		{
			BuildCard(engineCount: 2);

			_unitsService.Setup(x => x.GetAllLatestStatusForUnitsByDepartmentIdAsync(DepartmentId))
				.ReturnsAsync(new List<UnitState>
				{
					new UnitState { UnitId = 1, State = (int)UnitStateTypes.Committed, Timestamp = DateTime.UtcNow }
				});

			var result = await _service.GetRecommendationAsync(BuildRequest());

			result.Units.Should().ContainSingle(u => u.UnitId == 2);
			result.Shortfalls.Should().ContainSingle(s => s.FilledCount == 1);
		}

		[Test]
		public async Task custom_status_selections_override_matrix_availability()
		{
			var card = BuildCard(engineCount: 2);

			// The card only counts built-in Available as dispatchable; Engine 2 sits
			// Delayed which the matrix would allow but the selection set excludes.
			card.AvailabilitySelections.Add(new RunCardAvailabilitySelection
			{
				RunCardId = 5,
				SelectionType = (int)RunCardSelectionTypes.UnitStatus,
				IsCustomState = false,
				StateId = (int)UnitStateTypes.Available
			});

			_unitsService.Setup(x => x.GetAllLatestStatusForUnitsByDepartmentIdAsync(DepartmentId))
				.ReturnsAsync(new List<UnitState>
				{
					new UnitState { UnitId = 2, State = (int)UnitStateTypes.Delayed, Timestamp = DateTime.UtcNow }
				});

			var result = await _service.GetRecommendationAsync(BuildRequest());

			result.Units.Should().ContainSingle(u => u.UnitId == 1);
		}

		[Test]
		public async Task no_location_and_no_home_station_shortfalls_everything()
		{
			BuildCard(engineCount: 1, roleCount: 1);

			var result = await _service.GetRecommendationAsync(BuildRequest(lat: null, lon: null));

			result.Units.Should().BeEmpty();
			result.Personnel.Should().BeEmpty();
			result.Shortfalls.Should().HaveCount(2);
			result.Shortfalls.Should().OnlyContain(s => s.Reason == RequirementShortfallReasons.NoLocationData);
		}

		[Test]
		public async Task staffing_gate_excludes_understaffed_units_but_unknown_passes()
		{
			BuildCard(engineCount: 2);
			_config.UnitMinimumStaffingLevel = (int)UnitStaffingLevel.FullyStaffed;

			_unitsService.Setup(x => x.GetUnitStaffingForDepartmentAsync(DepartmentId))
				.ReturnsAsync(new Dictionary<int, UnitRoleStaffingResult>
				{
					{ 1, new UnitRoleStaffingResult { UnitId = 1, Level = UnitStaffingLevel.NotStaffed, DefinedRoleCount = 4 } }
					// Unit 2 has no entry -> Unknown -> passes.
				});

			var result = await _service.GetRecommendationAsync(BuildRequest());

			result.Units.Should().ContainSingle(u => u.UnitId == 2);
			result.Shortfalls.Should().ContainSingle(s => s.Reason == RequirementShortfallReasons.UnitsNotStaffed);
		}

		[Test]
		public async Task rest_period_prefers_rested_unit_from_farther_station()
		{
			BuildCard(engineCount: 1);
			_config.RestPeriodMinutes = 60;

			_runCardsService.Setup(x => x.GetLastUnitDispatchTimesAsync(DepartmentId))
				.ReturnsAsync(new Dictionary<int, DateTime>
				{
					{ 1, DateTime.UtcNow.AddMinutes(-10) } // Engine 1 dispatched 10 minutes ago.
				});

			var result = await _service.GetRecommendationAsync(BuildRequest());

			result.Units.Should().ContainSingle(u => u.UnitId == 2 && u.SelectionReason == RecommendationSelectionReasons.CascadeStation);
		}

		[Test]
		public async Task rest_period_unit_still_picked_when_nothing_else_can_fill()
		{
			BuildCard(engineCount: 2);
			_config.RestPeriodMinutes = 60;

			_runCardsService.Setup(x => x.GetLastUnitDispatchTimesAsync(DepartmentId))
				.ReturnsAsync(new Dictionary<int, DateTime>
				{
					{ 1, DateTime.UtcNow.AddMinutes(-10) }
				});

			var result = await _service.GetRecommendationAsync(BuildRequest());

			result.Units.Should().HaveCount(2);
			result.Units.Should().ContainSingle(u => u.UnitId == 1 && u.SelectionReason == RecommendationSelectionReasons.RestPeriodOverridden);
		}

		[Test]
		public async Task already_dispatched_units_are_excluded_for_escalation()
		{
			BuildCard(engineCount: 1);

			var request = BuildRequest();
			request.AlreadyDispatchedUnitIds.Add(1);

			var result = await _service.GetRecommendationAsync(request);

			result.Units.Should().ContainSingle(u => u.UnitId == 2);
		}

		[Test]
		public async Task station_based_fills_role_requirements_from_group_membership()
		{
			BuildCard(roleCount: 2);

			var result = await _service.GetRecommendationAsync(BuildRequest());

			result.Personnel.Should().HaveCount(2);
			result.Personnel.Should().ContainSingle(p => p.UserId == "user-1" && p.SelectionReason == RecommendationSelectionReasons.InGeofence);
			result.Personnel.Should().ContainSingle(p => p.UserId == "user-2" && p.SelectionReason == RecommendationSelectionReasons.CascadeStation);
		}

		[Test]
		public async Task closest_unit_orders_by_distance_and_flags_radius_exclusions()
		{
			BuildCard(engineCount: 1);
			_departmentSettingsService.Setup(x => x.GetDispatchRecommendationModeAsync(DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync(DispatchRecommendationModes.ClosestUnit);

			var now = DateTime.UtcNow;
			_unitsService.Setup(x => x.GetLatestUnitLocationsAsync(DepartmentId))
				.ReturnsAsync(new List<UnitsLocation>
				{
					// Engine 2 is much closer to the call than Engine 1.
					new UnitsLocation { UnitId = 1, Latitude = 40.5m, Longitude = -105.5m, Timestamp = now },
					new UnitsLocation { UnitId = 2, Latitude = 39.7501m, Longitude = -104.9501m, Timestamp = now }
				});

			var result = await _service.GetRecommendationAsync(BuildRequest());

			result.Units.Should().ContainSingle(u => u.UnitId == 2 && u.SelectionReason == RecommendationSelectionReasons.ClosestByDistance);

			// Now cap the radius so tight that nothing qualifies.
			_config.MaxRadiusMeters = 1;

			var capped = await _service.GetRecommendationAsync(BuildRequest());

			capped.Units.Should().BeEmpty();
			capped.Shortfalls.Should().ContainSingle(s => s.Reason == RequirementShortfallReasons.OutsideRadius);
		}

		[Test]
		public async Task closest_unit_excludes_stale_fixes_unless_configured_in()
		{
			BuildCard(engineCount: 2);
			_departmentSettingsService.Setup(x => x.GetDispatchRecommendationModeAsync(DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync(DispatchRecommendationModes.ClosestUnit);
			_config.MaxLocationAgeSeconds = 600;

			var now = DateTime.UtcNow;
			_unitsService.Setup(x => x.GetLatestUnitLocationsAsync(DepartmentId))
				.ReturnsAsync(new List<UnitsLocation>
				{
					new UnitsLocation { UnitId = 1, Latitude = 39.7501m, Longitude = -104.9501m, Timestamp = now.AddHours(-2) },
					new UnitsLocation { UnitId = 2, Latitude = 39.76m, Longitude = -104.96m, Timestamp = now }
				});

			var result = await _service.GetRecommendationAsync(BuildRequest());

			result.Units.Should().ContainSingle(u => u.UnitId == 2);
			result.Shortfalls.Should().ContainSingle(s => s.Reason == RequirementShortfallReasons.LocationsTooStale);

			_config.IncludeStaleLocations = true;

			var withStale = await _service.GetRecommendationAsync(BuildRequest());

			withStale.Units.Should().HaveCount(2);
			withStale.Units.Should().ContainSingle(u => u.UnitId == 1 && u.LocationIsStale);
		}

		[Test]
		public async Task move_up_pass_flags_station_coverage_gap_with_donor()
		{
			BuildCard(engineCount: 1);
			_config.MoveUpRecommendationsEnabled = true;

			_runCardsService.Setup(x => x.GetStationCoverageRequirementsForDepartmentAsync(DepartmentId))
				.ReturnsAsync(new List<StationCoverageRequirement>
				{
					new StationCoverageRequirement
					{
						StationCoverageRequirementId = 1,
						DepartmentId = DepartmentId,
						DepartmentGroupId = StationAId,
						UnitTypeId = EngineTypeId,
						MinimumAvailableCount = 1,
						IsEnabled = true
					}
				});

			// Engine 1 (Station A's only engine) gets recommended for the call, leaving
			// Station A at zero engines -> move-up from Station B.
			var result = await _service.GetRecommendationAsync(BuildRequest());

			result.Units.Should().ContainSingle(u => u.UnitId == 1);
			result.MoveUps.Should().ContainSingle(m =>
				m.StationGroupId == StationAId && m.SuggestedUnitId == 2 && m.FromStationGroupId == StationBId);
		}

		[Test]
		public void cancelling_the_request_stops_routed_eta_lookups()
		{
			BuildCard(engineCount: 1);
			_departmentSettingsService.Setup(x => x.GetDispatchRecommendationModeAsync(DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync(DispatchRecommendationModes.ClosestUnit);
			_config.UseRoutedEta = true;

			var now = DateTime.UtcNow;
			_unitsService.Setup(x => x.GetLatestUnitLocationsAsync(DepartmentId))
				.ReturnsAsync(new List<UnitsLocation>
				{
					new UnitsLocation { UnitId = 1, Latitude = 39.7501m, Longitude = -104.9501m, Timestamp = now },
					new UnitsLocation { UnitId = 2, Latitude = 39.76m, Longitude = -104.96m, Timestamp = now }
				});

			// The caller gives up while the first routed ETA lookup is in flight; the loop
			// must not keep calling the mapping provider for the rest of the shortlist.
			var cancellation = new CancellationTokenSource();
			_geoService.Setup(x => x.GetEtaInSecondsAsync(It.IsAny<string>(), It.IsAny<string>()))
				.ReturnsAsync(() =>
				{
					cancellation.Cancel();
					return 120d;
				});

			Assert.ThrowsAsync<OperationCanceledException>(async () =>
				await _service.GetRecommendationAsync(BuildRequest(), cancellation.Token));

			_geoService.Verify(x => x.GetEtaInSecondsAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Once);
		}

		[Test]
		public async Task move_up_pass_measures_role_coverage_by_radius_in_closest_unit_mode()
		{
			BuildCard(roleCount: 1);
			_config.MoveUpRecommendationsEnabled = true;
			_departmentSettingsService.Setup(x => x.GetDispatchRecommendationModeAsync(DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync(DispatchRecommendationModes.ClosestUnit);

			// user-1 sits at Station A, user-2 is 100km away. Station A requires one
			// firefighter within 5km; user-1 gets dispatched to the call, so the radius
			// leaves the station uncovered even though user-2 is still available.
			var now = DateTime.UtcNow;
			_personnelLocationResolver.Setup(x => x.GetLatestLocationsAsync(DepartmentId, It.IsAny<int>(), It.IsAny<DateTime?>()))
				.ReturnsAsync(new Dictionary<string, ResolvedPersonnelLocation>
				{
					{ "user-1", new ResolvedPersonnelLocation { UserId = "user-1", Latitude = 39.7501, Longitude = -104.9501, Timestamp = now } },
					{ "user-2", new ResolvedPersonnelLocation { UserId = "user-2", Latitude = 40.75, Longitude = -104.95, Timestamp = now } }
				});

			_runCardsService.Setup(x => x.GetStationCoverageRequirementsForDepartmentAsync(DepartmentId))
				.ReturnsAsync(new List<StationCoverageRequirement>
				{
					new StationCoverageRequirement
					{
						StationCoverageRequirementId = 1,
						DepartmentId = DepartmentId,
						DepartmentGroupId = StationAId,
						PersonnelRoleId = FirefighterRoleId,
						MinimumAvailableCount = 1,
						RadiusMeters = 5000,
						IsEnabled = true
					}
				});

			var result = await _service.GetRecommendationAsync(BuildRequest());

			result.Personnel.Should().ContainSingle(p => p.UserId == "user-1");
			result.MoveUps.Should().ContainSingle(m => m.StationGroupId == StationAId
				&& m.PersonnelRoleId == FirefighterRoleId && m.AvailableAfterDispatch == 0);
		}

		[Test]
		public async Task move_up_pass_ignores_radius_for_role_coverage_in_station_based_mode()
		{
			BuildCard(roleCount: 1);
			_config.MoveUpRecommendationsEnabled = true;

			// Same requirement, but the department dispatches station-based, where "at this
			// station" means group assignment. user-2 is assigned to Station B, so Station A
			// is genuinely uncovered once user-1 goes on the call; the radius is not applied.
			_runCardsService.Setup(x => x.GetStationCoverageRequirementsForDepartmentAsync(DepartmentId))
				.ReturnsAsync(new List<StationCoverageRequirement>
				{
					new StationCoverageRequirement
					{
						StationCoverageRequirementId = 1,
						DepartmentId = DepartmentId,
						DepartmentGroupId = StationAId,
						PersonnelRoleId = FirefighterRoleId,
						MinimumAvailableCount = 1,
						RadiusMeters = 5000,
						IsEnabled = true
					}
				});

			var result = await _service.GetRecommendationAsync(BuildRequest());

			result.MoveUps.Should().ContainSingle(m => m.StationGroupId == StationAId
				&& m.SuggestedUserId == "user-2" && m.FromStationGroupId == StationBId);
		}

		[Test]
		public async Task enrich_call_adds_dispatch_rows_without_duplicates_and_stamps_run_card()
		{
			BuildCard(engineCount: 2, roleCount: 1);

			var call = new Call
			{
				CallId = 77,
				DepartmentId = DepartmentId,
				Priority = 3,
				Type = "Structure Fire",
				GeoLocationData = "39.75,-104.95",
				AlarmLevel = 0,
				UnitDispatches = new System.Collections.ObjectModel.Collection<CallDispatchUnit>
				{
					new CallDispatchUnit { CallId = 77, UnitId = 1 } // Engine 1 already on the call.
				}
			};

			var result = await _service.EnrichCallForDispatchAsync(call, 1);

			result.MatchedRunCardId.Should().Be(5);
			call.ActiveRunCardId.Should().Be(5);
			call.AlarmLevel.Should().Be(1);

			// Engine 1 was excluded from recommendations (already dispatched) and must not
			// be duplicated; Engine 2 gets added.
			call.UnitDispatches.Should().HaveCount(2);
			call.UnitDispatches.Count(d => d.UnitId == 1).Should().Be(1);
			call.UnitDispatches.Should().Contain(d => d.UnitId == 2);
			call.Dispatches.Should().NotBeNull();
			call.Dispatches.Should().ContainSingle(d => d.UserId == "user-1");
		}
	}
}
