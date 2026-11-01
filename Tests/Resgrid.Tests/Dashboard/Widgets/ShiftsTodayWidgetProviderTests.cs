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
	public class ShiftsTodayWidgetProviderTests
	{
		internal static Department PacificDepartment() => new Department { DepartmentId = DepartmentId, TimeZone = "Pacific Standard Time" };

		internal static ShiftDaySchedule Schedule(int shiftDayId, DateTime localDay, string start, string end, bool active, int departmentId = DepartmentId,
			List<ShiftDayRosterEntry> roster = null, Dictionary<int, Dictionary<int, int>> needs = null, params int[] groupIds)
		{
			var shift = new Shift
			{
				ShiftId = shiftDayId * 10, DepartmentId = departmentId, Name = "Shift " + shiftDayId, Color = "#123456", StartTime = start, EndTime = end,
				Groups = groupIds.Select(g => new ShiftGroup { DepartmentGroupId = g }).ToList()
			};
			return new ShiftDaySchedule
			{
				Shift = shift,
				Day = new ShiftDay { ShiftDayId = shiftDayId, ShiftId = shift.ShiftId, Day = localDay, Shift = shift },
				Roster = roster ?? new List<ShiftDayRosterEntry>(),
				Needs = needs ?? new Dictionary<int, Dictionary<int, int>>(),
				IsActive = active
			};
		}

		[Test]
		public async Task Lists_todays_and_still_running_shift_days_with_on_duty_counts_by_group()
		{
			var today = new DateTime(2026, 10, 31);
			var roster = new List<ShiftDayRosterEntry>
			{
				new ShiftDayRosterEntry { UserId = "u1", DepartmentGroupId = 10 },
				new ShiftDayRosterEntry { UserId = "u2", DepartmentGroupId = 11 },
				new ShiftDayRosterEntry { UserId = "u3" },
				new ShiftDayRosterEntry { UserId = "u4", DepartmentGroupId = 10, ApprovalPending = true }
			};
			var shifts = new Mock<IShiftsService>(MockBehavior.Strict);
			shifts.Setup(x => x.GetShiftDaySchedulesForDateRangeAsync(DepartmentId, today.AddDays(-1), today, null)).ReturnsAsync(new List<ShiftDaySchedule>
			{
				Schedule(1, today, "07:00", "19:00", true, roster: roster, needs: new Dictionary<int, Dictionary<int, int>> { { 10, new Dictionary<int, int> { { 1, 1 } } } }, groupIds: new[] { 10, 12 }),
				Schedule(2, today.AddDays(-1), "07:00", "19:00", false),
				Schedule(3, today.AddDays(-1), "19:00", "07:00", true),
				Schedule(4, today, "07:00", "19:00", true, departmentId: OtherDepartmentId)
			});
			var departments = new Mock<IDepartmentsService>(MockBehavior.Strict);
			departments.Setup(x => x.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(PacificDepartment());
			var groups = new Mock<IDepartmentGroupsService>(MockBehavior.Strict);
			groups.Setup(x => x.GetAllGroupsForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<DepartmentGroup>
			{
				new DepartmentGroup { DepartmentGroupId = 10, DepartmentId = DepartmentId, Name = "Station 10" },
				new DepartmentGroup { DepartmentGroupId = 11, DepartmentId = DepartmentId, Name = "Station 11" },
				new DepartmentGroup { DepartmentGroupId = 12, DepartmentId = DepartmentId, Name = "Station 12" }
			});
			var provider = new ShiftsTodayWidgetProvider(shifts.Object, departments.Object, groups.Object);

			var data = (ShiftsTodayWidgetData)await provider.GetDataAsync(For("shifts.today"), CancellationToken.None);

			data.Day.Should().Be("2026-10-31");
			data.Shifts.Select(s => s.ShiftDayId).Should().Equal(3, 1);
			var day = data.Shifts.Single(s => s.ShiftDayId == 1);
			day.OnDutyCount.Should().Be(3, "a pending signup is not on duty");
			day.OpenSlots.Should().Be(1);
			day.IsFilled.Should().BeFalse();
			day.StartUtc.Should().Be(new DateTime(2026, 10, 31, 14, 0, 0), "07:00 PDT is 14:00 UTC");
			day.Groups.Select(g => (g.GroupName, g.OnDutyCount)).Should().Equal(("Station 10", 1), ("Station 12", 0), ("Station 11", 1), (string.Empty, 1));
			day.Url.Should().Be("/User/Shifts/ViewShift?shiftDayId=1");
		}

		[Test]
		public async Task No_shift_days_reads_no_groups()
		{
			var shifts = new Mock<IShiftsService>(MockBehavior.Strict);
			shifts.Setup(x => x.GetShiftDaySchedulesForDateRangeAsync(DepartmentId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), null)).ReturnsAsync(new List<ShiftDaySchedule>());
			var departments = new Mock<IDepartmentsService>(MockBehavior.Strict);
			departments.Setup(x => x.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(PacificDepartment());
			var provider = new ShiftsTodayWidgetProvider(shifts.Object, departments.Object, new Mock<IDepartmentGroupsService>(MockBehavior.Strict).Object);

			var data = (ShiftsTodayWidgetData)await provider.GetDataAsync(For("shifts.today"), CancellationToken.None);

			data.Shifts.Should().BeEmpty();
		}
	}
}
