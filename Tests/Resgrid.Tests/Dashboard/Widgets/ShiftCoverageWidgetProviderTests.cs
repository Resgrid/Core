using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Dashboard.Widgets.Planning;
using Resgrid.Model.Services;
using Resgrid.Services.Dashboard.Widgets.Planning;
using static Resgrid.Tests.Dashboard.Widgets.CommsPlanningWidgetContexts;

namespace Resgrid.Tests.Dashboard.Widgets
{
	[TestFixture]
	public class ShiftCoverageWidgetProviderTests
	{
		private static readonly DateTime Today = new DateTime(2026, 10, 31);

		[Test]
		public async Task A_member_who_supervises_nothing_reads_no_staffing()
		{
			var authorization = new Mock<IAuthorizationService>(MockBehavior.Strict);
			authorization.Setup(x => x.GetShiftManagementScopeAsync(UserId, DepartmentId)).ReturnsAsync(ShiftManagementScope.None());
			var provider = new ShiftCoverageWidgetProvider(new Mock<IShiftStaffingService>(MockBehavior.Strict).Object, new Mock<IShiftsService>(MockBehavior.Strict).Object,
				authorization.Object, new Mock<IDepartmentsService>(MockBehavior.Strict).Object);

			var data = (ShiftCoverageWidgetData)await provider.GetDataAsync(For("shifts.coverage"), CancellationToken.None);

			data.CanView.Should().BeFalse();
			data.Items.Should().BeEmpty();
		}

		[Test]
		public async Task Lists_supervised_occurrences_in_the_window_and_flags_gaps()
		{
			var authorization = new Mock<IAuthorizationService>(MockBehavior.Strict);
			authorization.Setup(x => x.GetShiftManagementScopeAsync(UserId, DepartmentId)).ReturnsAsync(new ShiftManagementScope { GroupIds = new HashSet<int> { 10 } });
			var shifts = new Mock<IShiftsService>(MockBehavior.Strict);
			shifts.Setup(x => x.GetAllShiftsByDepartmentAsync(DepartmentId)).ReturnsAsync(new List<Shift>
			{
				new Shift { ShiftId = 1, DepartmentId = DepartmentId, Groups = new List<ShiftGroup> { new ShiftGroup { DepartmentGroupId = 10 } } },
				new Shift { ShiftId = 2, DepartmentId = DepartmentId, Groups = new List<ShiftGroup> { new ShiftGroup { DepartmentGroupId = 20 } } }
			});
			var staffing = new Mock<IShiftStaffingService>(MockBehavior.Strict);
			staffing.Setup(x => x.GetStaffingForRangeAsync(DepartmentId, Today, Today.AddDays(7), null, null, false)).ReturnsAsync(new List<ShiftDayStaffing>
			{
				new ShiftDayStaffing { ShiftType = StaffedShiftTypes.Assigned, ShiftId = 1, ShiftDayId = 11, Name = "Days", Day = Today, Start = Today.AddHours(7), End = Today.AddHours(19),
					Source = ShiftStaffingSources.Manual,
					Personnel = new List<ShiftDayStaffingPerson> { new ShiftDayStaffingPerson { UserId = "a" }, new ShiftDayStaffingPerson { UserId = "c" } },
					AutomaticPersonnel = new List<ShiftDayStaffingPerson> { new ShiftDayStaffingPerson { UserId = "a" }, new ShiftDayStaffingPerson { UserId = "b" } } },
				new ShiftDayStaffing { ShiftType = StaffedShiftTypes.Assigned, ShiftId = 1, ShiftDayId = 12, Name = "Days", Day = Today.AddDays(1), Start = Today.AddDays(1).AddHours(7), End = Today.AddDays(1).AddHours(19) },
				new ShiftDayStaffing { ShiftType = StaffedShiftTypes.Assigned, ShiftId = 1, ShiftDayId = 13, Name = "Night before", Day = Today.AddDays(-1), Start = Today.AddHours(-5), End = Today.AddHours(7) },
				new ShiftDayStaffing { ShiftType = StaffedShiftTypes.Assigned, ShiftId = 2, ShiftDayId = 21, Name = "Not mine", Day = Today, Start = Today.AddHours(8), End = Today.AddHours(20) },
				new ShiftDayStaffing { ShiftType = StaffedShiftTypes.Workshift, WorkshiftDayId = "w1", Name = "Static", Day = Today, Start = Today, End = Today.AddDays(1) }
			});
			var departments = new Mock<IDepartmentsService>(MockBehavior.Strict);
			departments.Setup(x => x.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(ShiftsTodayWidgetProviderTests.PacificDepartment());
			var provider = new ShiftCoverageWidgetProvider(staffing.Object, shifts.Object, authorization.Object, departments.Object);

			var data = (ShiftCoverageWidgetData)await provider.GetDataAsync(For("shifts.coverage"), CancellationToken.None);

			data.CanView.Should().BeTrue();
			data.StartDay.Should().Be("2026-10-31");
			data.Items.Select(i => i.Key).Should().Equal(new[] { "s:11", "s:12" }, "static shifts are for department-wide supervisors, others' shifts and the night before are left out");
			data.Items[0].Should().BeEquivalentTo(new { Source = "manual", People = 2, AutomaticPeople = 2, AddedCount = 1, RemovedCount = 1, IsGap = false });
			data.Items[1].IsGap.Should().BeTrue();
			data.GapCount.Should().Be(1);
		}
	}
}
