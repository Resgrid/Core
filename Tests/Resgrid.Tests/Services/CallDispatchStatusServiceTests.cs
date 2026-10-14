using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	public class CallDispatchStatusServiceTests
	{
		private Mock<IDepartmentSettingsService> _departmentSettingsService;
		private Mock<IDepartmentsService> _departmentsService;
		private Mock<IShiftsService> _shiftsService;
		private Mock<IActionLogsService> _actionLogsService;
		private Mock<IUnitsService> _unitsService;
		private Mock<ICustomStateService> _customStateService;
		private Mock<ICallsRepository> _callsRepository;
		private CallDispatchStatusService _service;

		[SetUp]
		public void SetUp()
		{
			_departmentSettingsService = new Mock<IDepartmentSettingsService>();
			_departmentsService = new Mock<IDepartmentsService>();
			_shiftsService = new Mock<IShiftsService>();
			_actionLogsService = new Mock<IActionLogsService>();
			_unitsService = new Mock<IUnitsService>();
			_customStateService = new Mock<ICustomStateService>();
			_callsRepository = new Mock<ICallsRepository>();

			_departmentsService
				.Setup(x => x.GetDepartmentByIdAsync(It.IsAny<int>(), It.IsAny<bool>()))
				.ReturnsAsync(new Department { DepartmentId = 7, TimeZone = "UTC" });
			_actionLogsService
				.Setup(x => x.SetUserActionAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new ActionLog());
			_unitsService
				.Setup(x => x.SetUnitStateAsync(It.IsAny<UnitState>(), It.IsAny<int>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((UnitState state, int _, CancellationToken __, bool ___) => state);

			_service = new CallDispatchStatusService(
				_departmentSettingsService.Object,
				_departmentsService.Object,
				_shiftsService.Object,
				_actionLogsService.Object,
				_unitsService.Object,
				_customStateService.Object,
				_callsRepository.Object);
		}

		[Test]
		public async Task ApplyDispatchStatusesAsync_uses_default_shift_and_unit_dispatch_statuses()
		{
			var call = new Call
			{
				CallId = 12,
				DepartmentId = 7,
				LoggedOn = new DateTime(2026, 1, 12, 15, 0, 0, DateTimeKind.Utc),
				GroupDispatches = new List<CallDispatchGroup> { new CallDispatchGroup { DepartmentGroupId = 5 } },
				UnitDispatches = new List<CallDispatchUnit> { new CallDispatchUnit { UnitId = 11 } }
			};

			_departmentSettingsService.Setup(x => x.GetDispatchShiftInsteadOfGroupAsync(7)).ReturnsAsync(true);
			_departmentSettingsService.Setup(x => x.GetAutoSetStatusForShiftDispatchPersonnelAsync(7)).ReturnsAsync(true);
			_departmentSettingsService.Setup(x => x.GetShiftCallDispatchPersonnelStatusToSetAsync(7)).ReturnsAsync(-1);
			_departmentSettingsService.Setup(x => x.GetUnitCallDispatchStatusTargetAsync(7)).ReturnsAsync(StatusTarget.FromStoredValue(-1));
			// On duty at dispatch time (the call's logged time), from the resolved shift roster.
			_shiftsService
				.Setup(x => x.GetOnDutyUserIdsForGroupsAsync(7, It.Is<IEnumerable<int>>(g => g.Contains(5)), new DateTime(2026, 1, 12, 15, 0, 0, DateTimeKind.Utc)))
				.ReturnsAsync(new Dictionary<int, List<string>> { { 5, new List<string> { "user1", "user2" } } });

			await _service.ApplyDispatchStatusesAsync(call);

			_actionLogsService.Verify(x => x.SetUserActionAsync("user1", 7, (int)ActionTypes.RespondingToScene, null, 12, (int)DestinationEntityTypes.Call, It.IsAny<CancellationToken>()), Times.Once);
			_actionLogsService.Verify(x => x.SetUserActionAsync("user2", 7, (int)ActionTypes.RespondingToScene, null, 12, (int)DestinationEntityTypes.Call, It.IsAny<CancellationToken>()), Times.Once);
			_unitsService.Verify(x => x.SetUnitStateAsync(
				It.Is<UnitState>(s =>
					s.UnitId == 11 &&
					s.State == (int)UnitStateTypes.Responding &&
					s.DestinationId == 12 &&
					s.DestinationType == (int)DestinationEntityTypes.Call),
				7,
				It.IsAny<CancellationToken>(),
				true), Times.Once);
		}

		[Test]
		public async Task ApplyReleaseStatusesAsync_uses_configured_release_statuses()
		{
			var call = new Call
			{
				CallId = 22,
				DepartmentId = 7,
				LoggedOn = new DateTime(2026, 2, 4, 9, 30, 0, DateTimeKind.Utc)
			};

			_departmentSettingsService.Setup(x => x.GetDispatchShiftInsteadOfGroupAsync(7)).ReturnsAsync(true);
			_departmentSettingsService.Setup(x => x.GetAutoSetStatusForShiftDispatchPersonnelAsync(7)).ReturnsAsync(true);
			_departmentSettingsService.Setup(x => x.GetShiftCallReleasePersonnelStatusToSetAsync(7)).ReturnsAsync((int)ActionTypes.AvailableStation);
			_departmentSettingsService.Setup(x => x.GetUnitCallReleaseStatusTargetAsync(7)).ReturnsAsync(StatusTarget.FromStoredValue((int)UnitStateTypes.Returning));
			_shiftsService
				.Setup(x => x.GetOnDutyUserIdsForGroupsAsync(7, It.Is<IEnumerable<int>>(g => g.Contains(5)), new DateTime(2026, 2, 4, 9, 30, 0, DateTimeKind.Utc)))
				.ReturnsAsync(new Dictionary<int, List<string>> { { 5, new List<string> { "user1" } } });

			await _service.ApplyReleaseStatusesAsync(call, new[] { 5 }, new[] { 11 });

			_actionLogsService.Verify(x => x.SetUserActionAsync("user1", 7, (int)ActionTypes.AvailableStation, null, 22, (int)DestinationEntityTypes.Call, It.IsAny<CancellationToken>()), Times.Once);
			_unitsService.Verify(x => x.SetUnitStateAsync(
				It.Is<UnitState>(s =>
					s.UnitId == 11 &&
					s.State == (int)UnitStateTypes.Returning &&
					s.DestinationId == 22 &&
					s.DestinationType == (int)DestinationEntityTypes.Call),
				7,
				It.IsAny<CancellationToken>(),
				true), Times.Once);
		}

		[Test]
		public async Task ApplyDispatchStatusesAsync_skips_shift_personnel_when_auto_status_is_disabled()
		{
			var call = new Call
			{
				CallId = 32,
				DepartmentId = 7,
				LoggedOn = new DateTime(2026, 3, 7, 11, 0, 0, DateTimeKind.Utc)
			};

			_departmentSettingsService.Setup(x => x.GetDispatchShiftInsteadOfGroupAsync(7)).ReturnsAsync(true);
			_departmentSettingsService.Setup(x => x.GetAutoSetStatusForShiftDispatchPersonnelAsync(7)).ReturnsAsync(false);
			_departmentSettingsService.Setup(x => x.GetUnitCallDispatchStatusTargetAsync(7)).ReturnsAsync(StatusTarget.FromStoredValue((int)UnitStateTypes.Committed));

			await _service.ApplyDispatchStatusesAsync(call, new[] { 5 }, new[] { 11 });

			_shiftsService.Verify(x => x.GetOnDutyUserIdsForGroupsAsync(It.IsAny<int>(), It.IsAny<IEnumerable<int>>(), It.IsAny<DateTime>()), Times.Never);
			_actionLogsService.Verify(x => x.SetUserActionAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
			_unitsService.Verify(x => x.SetUnitStateAsync(
				It.Is<UnitState>(s =>
					s.UnitId == 11 &&
					s.State == (int)UnitStateTypes.Committed &&
					s.DestinationId == 32 &&
					s.DestinationType == (int)DestinationEntityTypes.Call),
				7,
				It.IsAny<CancellationToken>(),
				true), Times.Once);
		}

		[Test]
		public async Task ApplyDispatchStatusesAsync_uses_unit_type_override_only_for_matching_unit_type()
		{
			var call = new Call
			{
				CallId = 42,
				DepartmentId = 7,
				LoggedOn = new DateTime(2026, 4, 6, 14, 0, 0, DateTimeKind.Utc)
			};

			_departmentSettingsService.Setup(x => x.GetDispatchShiftInsteadOfGroupAsync(7)).ReturnsAsync(false);
			_departmentSettingsService.Setup(x => x.GetUnitCallDispatchStatusTargetAsync(7)).ReturnsAsync(StatusTarget.FromStoredValue((int)UnitStateTypes.Responding));
			_departmentSettingsService.Setup(x => x.GetUnitCallStatusOverridesByUnitTypeAsync(7)).ReturnsAsync(new List<UnitTypeCallStatusOverride>
			{
				new UnitTypeCallStatusOverride { UnitTypeId = 2, DispatchStatus = 44, ReleaseStatus = -1 }
			});
			_unitsService.Setup(x => x.GetUnitByIdAsync(11)).ReturnsAsync(new Unit { UnitId = 11, Type = "Engine" });
			_unitsService.Setup(x => x.GetUnitByIdAsync(12)).ReturnsAsync(new Unit { UnitId = 12, Type = "Truck" });
			_unitsService.Setup(x => x.GetUnitTypeByNameAsync(7, "Engine")).ReturnsAsync(new UnitType { UnitTypeId = 2, Type = "Engine", CustomStatesId = 100 });
			_unitsService.Setup(x => x.GetUnitTypeByNameAsync(7, "Truck")).ReturnsAsync(new UnitType { UnitTypeId = 3, Type = "Truck", CustomStatesId = 101 });
			_customStateService.Setup(x => x.GetCustomSateByIdAsync(100)).ReturnsAsync(new CustomState
			{
				CustomStateId = 100,
				Details = new List<CustomStateDetail>
				{
					new CustomStateDetail { CustomStateDetailId = 44, ButtonText = "Enroute Custom" }
				}
			});

			await _service.ApplyDispatchStatusesAsync(call, unitIds: new[] { 11, 12 });

			_unitsService.Verify(x => x.SetUnitStateAsync(
				It.Is<UnitState>(s => s.UnitId == 11 && s.State == 44 && s.DestinationId == 42),
				7,
				It.IsAny<CancellationToken>(),
				true), Times.Once);
			_unitsService.Verify(x => x.SetUnitStateAsync(
				It.Is<UnitState>(s => s.UnitId == 12 && s.State == (int)UnitStateTypes.Responding && s.DestinationId == 42),
				7,
				It.IsAny<CancellationToken>(),
				true), Times.Once);
		}

		[Test]
		public async Task ApplyReleaseStatusesAsync_uses_unit_type_release_override_when_valid()
		{
			var call = new Call
			{
				CallId = 52,
				DepartmentId = 7,
				LoggedOn = new DateTime(2026, 4, 6, 18, 0, 0, DateTimeKind.Utc)
			};

			_departmentSettingsService.Setup(x => x.GetDispatchShiftInsteadOfGroupAsync(7)).ReturnsAsync(false);
			_departmentSettingsService.Setup(x => x.GetUnitCallReleaseStatusTargetAsync(7)).ReturnsAsync(StatusTarget.FromStoredValue((int)UnitStateTypes.Released));
			_departmentSettingsService.Setup(x => x.GetUnitCallStatusOverridesByUnitTypeAsync(7)).ReturnsAsync(new List<UnitTypeCallStatusOverride>
			{
				new UnitTypeCallStatusOverride { UnitTypeId = 2, DispatchStatus = -1, ReleaseStatus = 77 }
			});
			_unitsService.Setup(x => x.GetUnitByIdAsync(11)).ReturnsAsync(new Unit { UnitId = 11, Type = "Engine" });
			_unitsService.Setup(x => x.GetUnitTypeByNameAsync(7, "Engine")).ReturnsAsync(new UnitType { UnitTypeId = 2, Type = "Engine", CustomStatesId = 100 });
			_customStateService.Setup(x => x.GetCustomSateByIdAsync(100)).ReturnsAsync(new CustomState
			{
				CustomStateId = 100,
				Details = new List<CustomStateDetail>
				{
					new CustomStateDetail { CustomStateDetailId = 77, ButtonText = "Back In Service" }
				}
			});

			await _service.ApplyReleaseStatusesAsync(call, unitIds: new[] { 11 });

			_unitsService.Verify(x => x.SetUnitStateAsync(
				It.Is<UnitState>(s => s.UnitId == 11 && s.State == 77 && s.DestinationId == 52),
				7,
				It.IsAny<CancellationToken>(),
				true), Times.Once);
		}

		// Belgian EMS, 2026-10-07: closing a call put units that had been taken out of service back to Radio Available.
		[TestCase((int)UnitStateTypes.OutOfService)]
		[TestCase((int)UnitStateTypes.Unavailable)]
		[TestCase((int)UnitStateTypes.Delayed)]
		[TestCase((int)UnitStateTypes.Available)]
		public async Task ApplyReleaseStatusesAsync_leaves_units_back_in_service_or_out_of_service_alone(int currentState)
		{
			_departmentSettingsService.Setup(x => x.GetUnitCallReleaseStatusTargetAsync(7)).ReturnsAsync(StatusTarget.FromStoredValue((int)UnitStateTypes.Available));
			_unitsService.Setup(x => x.GetLastUnitStateByUnitIdAsync(11))
				.ReturnsAsync(new UnitState { UnitStateId = 900, UnitId = 11, State = currentState, DestinationId = 40, DestinationType = (int)DestinationEntityTypes.Call });

			await _service.ApplyReleaseStatusesAsync(new Call { CallId = 40, DepartmentId = 7 }, null, new[] { 11 });

			_unitsService.Verify(x => x.SetUnitStateAsync(It.IsAny<UnitState>(), It.IsAny<int>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
		}

		[Test]
		public async Task ApplyReleaseStatusesAsync_reads_custom_statuses_by_base_type()
		{
			// Custom "Out of Service" (base type Maintenance) stays; custom "On Scene" (base type On Scene) is released.
			_departmentSettingsService.Setup(x => x.GetUnitCallReleaseStatusTargetAsync(7)).ReturnsAsync(StatusTarget.FromStoredValue((int)UnitStateTypes.Available));
			_customStateService.Setup(x => x.GetAllCustomStatesForDepartmentAsync(7)).ReturnsAsync(new List<CustomState>
			{
				new CustomState
				{
					CustomStateId = 3, DepartmentId = 7, Type = (int)CustomStateTypes.Unit,
					Details = new List<CustomStateDetail>
					{
						new CustomStateDetail { CustomStateDetailId = 501, CustomStateId = 3, ButtonText = "Out of Service", BaseType = (int)ActionBaseTypes.Maintenance },
						new CustomStateDetail { CustomStateDetailId = 502, CustomStateId = 3, ButtonText = "On Scene", BaseType = (int)ActionBaseTypes.OnScene }
					}
				}
			});
			_unitsService.Setup(x => x.GetLastUnitStateByUnitIdAsync(11)).ReturnsAsync(new UnitState { UnitStateId = 1, UnitId = 11, State = 501 });
			_unitsService.Setup(x => x.GetLastUnitStateByUnitIdAsync(12)).ReturnsAsync(new UnitState { UnitStateId = 2, UnitId = 12, State = 502 });

			await _service.ApplyReleaseStatusesAsync(new Call { CallId = 40, DepartmentId = 7 }, null, new[] { 11, 12 });

			_unitsService.Verify(x => x.SetUnitStateAsync(It.Is<UnitState>(u => u.UnitId == 11), It.IsAny<int>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
			_unitsService.Verify(x => x.SetUnitStateAsync(It.Is<UnitState>(u => u.UnitId == 12 && u.State == (int)UnitStateTypes.Available), 7, It.IsAny<CancellationToken>(), true), Times.Once);
		}

		[Test]
		public async Task ApplyReleaseStatusesAsync_leaves_a_unit_working_another_open_call_alone()
		{
			_departmentSettingsService.Setup(x => x.GetUnitCallReleaseStatusTargetAsync(7)).ReturnsAsync(StatusTarget.FromStoredValue((int)UnitStateTypes.Available));
			_unitsService.Setup(x => x.GetLastUnitStateByUnitIdAsync(11))
				.ReturnsAsync(new UnitState { UnitStateId = 1, UnitId = 11, State = (int)UnitStateTypes.Responding, DestinationId = 41, DestinationType = (int)DestinationEntityTypes.Call });
			_unitsService.Setup(x => x.GetLastUnitStateByUnitIdAsync(12))
				.ReturnsAsync(new UnitState { UnitStateId = 2, UnitId = 12, State = (int)UnitStateTypes.Responding, DestinationId = 42, DestinationType = (int)DestinationEntityTypes.Call });
			_callsRepository.Setup(x => x.GetByIdAsync(41)).ReturnsAsync(new Call { CallId = 41, DepartmentId = 7, State = (int)CallStates.Active });
			_callsRepository.Setup(x => x.GetByIdAsync(42)).ReturnsAsync(new Call { CallId = 42, DepartmentId = 7, State = (int)CallStates.Closed });

			await _service.ApplyReleaseStatusesAsync(new Call { CallId = 40, DepartmentId = 7 }, null, new[] { 11, 12 });

			// Unit 11 is on open call 41 and keeps its status; unit 12's other call is closed, so it is released from this one.
			_unitsService.Verify(x => x.SetUnitStateAsync(It.Is<UnitState>(u => u.UnitId == 11), It.IsAny<int>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
			_unitsService.Verify(x => x.SetUnitStateAsync(It.Is<UnitState>(u => u.UnitId == 12 && u.DestinationId == 40), 7, It.IsAny<CancellationToken>(), true), Times.Once);
		}

		[Test]
		public async Task ApplyDispatchStatusesAsync_sets_dispatch_statuses_whatever_the_current_status()
		{
			_departmentSettingsService.Setup(x => x.GetUnitCallDispatchStatusTargetAsync(7)).ReturnsAsync(StatusTarget.FromStoredValue(-1));
			_unitsService.Setup(x => x.GetLastUnitStateByUnitIdAsync(11)).ReturnsAsync(new UnitState { UnitStateId = 1, UnitId = 11, State = (int)UnitStateTypes.Available });

			await _service.ApplyDispatchStatusesAsync(new Call { CallId = 40, DepartmentId = 7 }, null, new[] { 11 });

			_unitsService.Verify(x => x.SetUnitStateAsync(It.Is<UnitState>(u => u.UnitId == 11 && u.State == (int)UnitStateTypes.Responding), 7, It.IsAny<CancellationToken>(), true), Times.Once);
		}

		// Brockville, 2026-10-13: every unit type uses its own status set, and the department-wide setting wrote built-in
		// statuses (Available on dispatch, Returning on close) outside it.
		private void CustomSetUnit(int unitId, params CustomStateDetail[] options)
		{
			_unitsService.Setup(x => x.GetUnitByIdAsync(unitId)).ReturnsAsync(new Unit { UnitId = unitId, Type = "Pumper" });
			_unitsService.Setup(x => x.GetUnitTypeByNameAsync(7, "Pumper")).ReturnsAsync(new UnitType { UnitTypeId = 4334, Type = "Pumper", CustomStatesId = 2041 });
			_customStateService.Setup(x => x.GetCustomSateByIdAsync(2041)).ReturnsAsync(new CustomState
			{
				CustomStateId = 2041, Type = (int)CustomStateTypes.Unit, Details = options.ToList()
			});
		}

		private static CustomStateDetail Option(int id, string text, int order, ActionBaseTypes baseType, bool deleted = false) =>
			new CustomStateDetail { CustomStateDetailId = id, CustomStateId = 2041, ButtonText = text, Order = order, BaseType = (int)baseType, IsDeleted = deleted };

		[Test]
		public async Task Default_dispatch_status_on_a_custom_set_is_its_dispatched_option()
		{
			CustomSetUnit(11, Option(12707, "IN QUARTERS", 7, ActionBaseTypes.InQuarters), Option(12702, "EN ROUTE", 2, ActionBaseTypes.Enroute),
				Option(12701, "DISPATCHED", 1, ActionBaseTypes.Dispatched));

			await _service.ApplyDispatchStatusesAsync(new Call { CallId = 40, DepartmentId = 7 }, null, new[] { 11 });

			_unitsService.Verify(x => x.SetUnitStateAsync(It.Is<UnitState>(u => u.UnitId == 11 && u.State == 12701), 7, It.IsAny<CancellationToken>(), true), Times.Once);
		}

		[Test]
		public async Task A_base_type_target_uses_the_first_matching_option_by_order()
		{
			_departmentSettingsService.Setup(x => x.GetUnitCallDispatchStatusTargetAsync(7)).ReturnsAsync(StatusTarget.ForBaseType(ActionBaseTypes.Responding));
			CustomSetUnit(11, Option(3, "Code 3", 2, ActionBaseTypes.Responding), Option(2, "Rolling", 1, ActionBaseTypes.Responding),
				Option(1, "Old Rolling", 0, ActionBaseTypes.Responding, deleted: true));

			await _service.ApplyDispatchStatusesAsync(new Call { CallId = 40, DepartmentId = 7 }, null, new[] { 11 });

			_unitsService.Verify(x => x.SetUnitStateAsync(It.Is<UnitState>(u => u.UnitId == 11 && u.State == 2), 7, It.IsAny<CancellationToken>(), true), Times.Once);
		}

		[Test]
		public async Task A_custom_set_without_a_matching_option_leaves_the_unit_alone()
		{
			_departmentSettingsService.Setup(x => x.GetUnitCallReleaseStatusTargetAsync(7)).ReturnsAsync(StatusTarget.ForBaseType(ActionBaseTypes.Cleared));
			CustomSetUnit(11, Option(12701, "DISPATCHED", 1, ActionBaseTypes.Dispatched));
			_unitsService.Setup(x => x.GetLastUnitStateByUnitIdAsync(11)).ReturnsAsync(new UnitState { UnitStateId = 1, UnitId = 11, State = 12701 });

			await _service.ApplyReleaseStatusesAsync(new Call { CallId = 40, DepartmentId = 7 }, null, new[] { 11 });

			_unitsService.Verify(x => x.SetUnitStateAsync(It.IsAny<UnitState>(), It.IsAny<int>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
		}

		[Test]
		public async Task Leave_unchanged_sets_nothing()
		{
			_departmentSettingsService.Setup(x => x.GetUnitCallDispatchStatusTargetAsync(7)).ReturnsAsync(StatusTarget.LeaveUnchanged);

			await _service.ApplyDispatchStatusesAsync(new Call { CallId = 40, DepartmentId = 7 }, null, new[] { 11 });

			_unitsService.Verify(x => x.SetUnitStateAsync(It.IsAny<UnitState>(), It.IsAny<int>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
		}

		[Test]
		public async Task A_saved_built_in_status_on_a_custom_set_becomes_its_option_with_the_same_meaning()
		{
			// Setting 51 = 8 (built-in Returning): the custom set's RETURNING, not the built-in value.
			_departmentSettingsService.Setup(x => x.GetUnitCallReleaseStatusTargetAsync(7)).ReturnsAsync(StatusTarget.FromStoredValue((int)UnitStateTypes.Returning));
			CustomSetUnit(11, Option(12706, "RETURNING", 6, ActionBaseTypes.Returning), Option(12703, "ON LOCATION", 3, ActionBaseTypes.OnScene));
			_unitsService.Setup(x => x.GetLastUnitStateByUnitIdAsync(11)).ReturnsAsync(new UnitState { UnitStateId = 1, UnitId = 11, State = 12703 });
			_customStateService.Setup(x => x.GetAllCustomStatesForDepartmentAsync(7)).ReturnsAsync(new List<CustomState>
			{
				new CustomState { CustomStateId = 2041, Type = (int)CustomStateTypes.Unit, Details = new List<CustomStateDetail> { Option(12703, "ON LOCATION", 3, ActionBaseTypes.OnScene) } }
			});

			await _service.ApplyReleaseStatusesAsync(new Call { CallId = 40, DepartmentId = 7 }, null, new[] { 11 });

			_unitsService.Verify(x => x.SetUnitStateAsync(It.Is<UnitState>(u => u.UnitId == 11 && u.State == 12706), 7, It.IsAny<CancellationToken>(), true), Times.Once);
		}

		[Test]
		public async Task A_unit_in_quarters_under_its_own_set_is_not_released()
		{
			// The Brockville complaint: IN QUARTERS with base type In Quarters keeps its status when the call closes.
			_departmentSettingsService.Setup(x => x.GetUnitCallReleaseStatusTargetAsync(7)).ReturnsAsync(StatusTarget.FromStoredValue((int)UnitStateTypes.Returning));
			CustomSetUnit(11, Option(12706, "RETURNING", 6, ActionBaseTypes.Returning), Option(12707, "IN QUARTERS", 7, ActionBaseTypes.InQuarters));
			_unitsService.Setup(x => x.GetLastUnitStateByUnitIdAsync(11)).ReturnsAsync(new UnitState { UnitStateId = 1, UnitId = 11, State = 12707 });
			_customStateService.Setup(x => x.GetAllCustomStatesForDepartmentAsync(7)).ReturnsAsync(new List<CustomState>
			{
				new CustomState { CustomStateId = 2041, Type = (int)CustomStateTypes.Unit, Details = new List<CustomStateDetail> { Option(12707, "IN QUARTERS", 7, ActionBaseTypes.InQuarters) } }
			});

			await _service.ApplyReleaseStatusesAsync(new Call { CallId = 40, DepartmentId = 7 }, null, new[] { 11 });

			_unitsService.Verify(x => x.SetUnitStateAsync(It.IsAny<UnitState>(), It.IsAny<int>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
		}
	}
}
