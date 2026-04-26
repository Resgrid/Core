using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
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
		private CallDispatchStatusService _service;

		[SetUp]
		public void SetUp()
		{
			_departmentSettingsService = new Mock<IDepartmentSettingsService>();
			_departmentsService = new Mock<IDepartmentsService>();
			_shiftsService = new Mock<IShiftsService>();
			_actionLogsService = new Mock<IActionLogsService>();
			_unitsService = new Mock<IUnitsService>();

			_departmentsService
				.Setup(x => x.GetDepartmentByIdAsync(It.IsAny<int>(), It.IsAny<bool>()))
				.ReturnsAsync(new Department { DepartmentId = 7, TimeZone = "UTC" });
			_actionLogsService
				.Setup(x => x.SetUserActionAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new ActionLog());
			_unitsService
				.Setup(x => x.SetUnitStateAsync(It.IsAny<UnitState>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((UnitState state, int _, CancellationToken __) => state);

			_service = new CallDispatchStatusService(
				_departmentSettingsService.Object,
				_departmentsService.Object,
				_shiftsService.Object,
				_actionLogsService.Object,
				_unitsService.Object);
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
			_departmentSettingsService.Setup(x => x.GetUnitCallDispatchStatusToSetAsync(7)).ReturnsAsync(-1);
			_shiftsService
				.Setup(x => x.GetShiftSignupsByDepartmentGroupIdAndDayAsync(5, It.Is<DateTime>(d => d == new DateTime(2026, 1, 12))))
				.ReturnsAsync(new List<ShiftSignup>
				{
					new ShiftSignup { UserId = "user1" },
					new ShiftSignup { UserId = "user2" }
				});

			await _service.ApplyDispatchStatusesAsync(call);

			_actionLogsService.Verify(x => x.SetUserActionAsync("user1", 7, (int)ActionTypes.RespondingToScene, null, 12, It.IsAny<CancellationToken>()), Times.Once);
			_actionLogsService.Verify(x => x.SetUserActionAsync("user2", 7, (int)ActionTypes.RespondingToScene, null, 12, It.IsAny<CancellationToken>()), Times.Once);
			_unitsService.Verify(x => x.SetUnitStateAsync(
				It.Is<UnitState>(s =>
					s.UnitId == 11 &&
					s.State == (int)UnitStateTypes.Responding &&
					s.DestinationId == 12 &&
					s.DestinationType == (int)DestinationEntityTypes.Call),
				7,
				It.IsAny<CancellationToken>()), Times.Once);
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
			_departmentSettingsService.Setup(x => x.GetUnitCallReleaseStatusToSetAsync(7)).ReturnsAsync((int)UnitStateTypes.Returning);
			_shiftsService
				.Setup(x => x.GetShiftSignupsByDepartmentGroupIdAndDayAsync(5, It.Is<DateTime>(d => d == new DateTime(2026, 2, 4))))
				.ReturnsAsync(new List<ShiftSignup> { new ShiftSignup { UserId = "user1" } });

			await _service.ApplyReleaseStatusesAsync(call, new[] { 5 }, new[] { 11 });

			_actionLogsService.Verify(x => x.SetUserActionAsync("user1", 7, (int)ActionTypes.AvailableStation, null, 22, It.IsAny<CancellationToken>()), Times.Once);
			_unitsService.Verify(x => x.SetUnitStateAsync(
				It.Is<UnitState>(s =>
					s.UnitId == 11 &&
					s.State == (int)UnitStateTypes.Returning &&
					s.DestinationId == 22 &&
					s.DestinationType == (int)DestinationEntityTypes.Call),
				7,
				It.IsAny<CancellationToken>()), Times.Once);
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
			_departmentSettingsService.Setup(x => x.GetUnitCallDispatchStatusToSetAsync(7)).ReturnsAsync((int)UnitStateTypes.Committed);

			await _service.ApplyDispatchStatusesAsync(call, new[] { 5 }, new[] { 11 });

			_shiftsService.Verify(x => x.GetShiftSignupsByDepartmentGroupIdAndDayAsync(It.IsAny<int>(), It.IsAny<DateTime>()), Times.Never);
			_actionLogsService.Verify(x => x.SetUserActionAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
			_unitsService.Verify(x => x.SetUnitStateAsync(
				It.Is<UnitState>(s =>
					s.UnitId == 11 &&
					s.State == (int)UnitStateTypes.Committed &&
					s.DestinationId == 32 &&
					s.DestinationType == (int)DestinationEntityTypes.Call),
				7,
				It.IsAny<CancellationToken>()), Times.Once);
		}
	}
}
