using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Dashboard.Widgets.Planning;
using Resgrid.Model.Services;

namespace Resgrid.Services.Dashboard.Widgets.Planning
{
	/// <summary>
	/// shifts.today (plan §7.6): today's shift days (department-local), plus a night shift from yesterday that is still
	/// running, with open slots and on-duty counts by group — the On Duty page's grouping (shift groups first, then other
	/// groups people were placed in, then no group). Counts only, so no names leave the server.
	/// </summary>
	[DashboardWidget("shifts.today")]
	public class ShiftsTodayWidgetProvider : IDashboardWidgetDataProvider
	{
		private readonly IShiftsService _shiftsService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IDepartmentGroupsService _departmentGroupsService;

		public ShiftsTodayWidgetProvider(IShiftsService shiftsService, IDepartmentsService departmentsService, IDepartmentGroupsService departmentGroupsService)
		{
			_shiftsService = shiftsService;
			_departmentsService = departmentsService;
			_departmentGroupsService = departmentGroupsService;
		}

		public async Task<object> GetDataAsync(DashboardWidgetContext context, CancellationToken cancellationToken)
		{
			var departmentId = context.DepartmentId;
			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId);
			var localNow = ShiftWidgetHelpers.LocalNow(context.NowUtc, department);
			var today = localNow.Date;

			var data = new ShiftsTodayWidgetData { Day = ShiftWidgetHelpers.DayKey(today) };

			var schedules = (await _shiftsService.GetShiftDaySchedulesForDateRangeAsync(departmentId, today.AddDays(-1), today) ?? new List<ShiftDaySchedule>())
				.Where(s => s?.Shift != null && s.Day != null && s.Shift.DepartmentId == departmentId)
				.Where(s => s.Day.Day.Date == today || s.IsActive)
				.GroupBy(s => s.Day.ShiftDayId)
				.Select(g => g.First())
				.OrderBy(s => s.Day.Start)
				.ThenBy(s => s.Shift.Name, StringComparer.OrdinalIgnoreCase)
				.ToList();

			if (schedules.Count == 0)
				return data;

			var groupNames = ShiftWidgetHelpers.GroupNames(await _departmentGroupsService.GetAllGroupsForDepartmentAsync(departmentId), departmentId);

			foreach (var schedule in schedules)
			{
				var onDuty = (schedule.Roster ?? new List<ShiftDayRosterEntry>()).Where(r => r != null && r.UserId != null && r.IsOnDuty()).ToList();
				var item = new ShiftsTodayItem
				{
					ShiftId = schedule.Shift.ShiftId,
					ShiftDayId = schedule.Day.ShiftDayId,
					Name = schedule.Shift.Name,
					Color = schedule.Shift.Color,
					StartUtc = ShiftWidgetHelpers.ToUtc(schedule.Day.Start, department),
					EndUtc = ShiftWidgetHelpers.ToUtc(schedule.Day.End, department),
					IsActive = schedule.IsActive,
					IsOver = localNow >= schedule.Day.End,
					IsFilled = schedule.IsFilled(),
					OpenSlots = schedule.OpenSlots(),
					OnDutyCount = onDuty.Select(r => r.UserId).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
					Url = ShiftWidgetHelpers.ViewShiftUrl(schedule.Day.ShiftDayId, "/User/Shifts")
				};

				var groupIds = (schedule.Shift.Groups ?? new List<ShiftGroup>()).Where(g => g != null).Select(g => (int?)g.DepartmentGroupId)
					.Concat(onDuty.Where(r => r.DepartmentGroupId.HasValue).Select(r => r.DepartmentGroupId))
					.Distinct()
					.ToList();

				if (onDuty.Any(r => !r.DepartmentGroupId.HasValue))
					groupIds.Add(null);

				foreach (var groupId in groupIds)
				{
					item.Groups.Add(new ShiftsTodayGroup
					{
						GroupId = groupId,
						GroupName = ShiftWidgetHelpers.GroupNameFor(groupNames, groupId),
						OnDutyCount = onDuty.Where(r => r.DepartmentGroupId == groupId).Select(r => r.UserId).Distinct(StringComparer.OrdinalIgnoreCase).Count()
					});
				}

				data.Shifts.Add(item);
			}

			return data;
		}
	}
}
