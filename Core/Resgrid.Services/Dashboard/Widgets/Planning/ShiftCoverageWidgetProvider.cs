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
	/// shifts.coverage (plan §7.6): the Shift Staffing page's overview (ShiftsController.GetStaffingOverview) for the next
	/// days — supervisors only; shifts the viewer supervises, static shifts for department-wide supervisors; a night shift
	/// that began the evening before the range belongs to the day before and is left out. Counts only, no names.
	/// </summary>
	[DashboardWidget("shifts.coverage")]
	public class ShiftCoverageWidgetProvider : IDashboardWidgetDataProvider
	{
		public const string StaffingUrl = "/User/Shifts/ShiftStaffing";

		private readonly IShiftStaffingService _shiftStaffingService;
		private readonly IShiftsService _shiftsService;
		private readonly IAuthorizationService _authorizationService;
		private readonly IDepartmentsService _departmentsService;

		public ShiftCoverageWidgetProvider(IShiftStaffingService shiftStaffingService, IShiftsService shiftsService, IAuthorizationService authorizationService,
			IDepartmentsService departmentsService)
		{
			_shiftStaffingService = shiftStaffingService;
			_shiftsService = shiftsService;
			_authorizationService = authorizationService;
			_departmentsService = departmentsService;
		}

		public async Task<object> GetDataAsync(DashboardWidgetContext context, CancellationToken cancellationToken)
		{
			var departmentId = context.DepartmentId;
			var days = context.GetInt("days", 7);
			var data = new ShiftCoverageWidgetData { Days = days, StaffingUrl = StaffingUrl };

			var scope = await _authorizationService.GetShiftManagementScopeAsync(context.UserId, departmentId) ?? ShiftManagementScope.None();
			if (!scope.IsSupervisor)
				return data;

			data.CanView = true;

			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId);
			var startDate = ShiftWidgetHelpers.LocalNow(context.NowUtc, department).Date;
			var endDate = startDate.AddDays(days - 1);
			data.StartDay = ShiftWidgetHelpers.DayKey(startDate);

			var shifts = (await _shiftsService.GetAllShiftsByDepartmentAsync(departmentId) ?? new List<Shift>())
				.Where(s => s != null && s.DepartmentId == departmentId)
				.GroupBy(s => s.ShiftId)
				.ToDictionary(g => g.Key, g => g.First());

			var occurrences = await _shiftStaffingService.GetStaffingForRangeAsync(departmentId, startDate, endDate.AddDays(1)) ?? new List<ShiftDayStaffing>();

			var rows = occurrences
				.Where(x => x != null)
				.Where(x => x.ShiftType == StaffedShiftTypes.Workshift
					? scope.AllGroups
					: x.ShiftId.HasValue && shifts.TryGetValue(x.ShiftId.Value, out var shift) && scope.CanSuperviseShift(shift))
				.Where(x => x.Day.Date >= startDate && x.Day.Date <= endDate)
				.OrderBy(x => x.Start)
				.ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
				.Select(x =>
				{
					var people = x.UserIds().Count;
					return new ShiftCoverageItem
					{
						Key = x.Key,
						Kind = x.ShiftType == StaffedShiftTypes.Workshift ? "workshift" : "shift",
						ShiftId = x.ShiftId,
						ShiftDayId = x.ShiftDayId,
						WorkshiftDayId = x.WorkshiftDayId,
						Name = x.Name,
						Color = x.Color,
						Day = ShiftWidgetHelpers.DayKey(x.Day.Date),
						StartUtc = ShiftWidgetHelpers.ToUtc(x.Start, department),
						EndUtc = ShiftWidgetHelpers.ToUtc(x.End, department),
						Source = x.Source == ShiftStaffingSources.Manual ? "manual" : "automatic",
						People = people,
						AutomaticPeople = (x.AutomaticPersonnel ?? new List<ShiftDayStaffingPerson>())
							.Where(p => p != null && !string.IsNullOrWhiteSpace(p.UserId)).Select(p => p.UserId).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
						AddedCount = x.AddedUserIds().Count,
						RemovedCount = x.RemovedUserIds().Count,
						IsGap = people == 0,
						IsActive = x.IsActive,
						IsOver = x.IsOver
					};
				})
				.ToList();

			data.GapCount = rows.Count(r => r.IsGap && !r.IsOver);
			data.Items = context.GetBool("gapsOnly", false) ? rows.Where(r => r.IsGap).ToList() : rows;
			return data;
		}
	}
}
