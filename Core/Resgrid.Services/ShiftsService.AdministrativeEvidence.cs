using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;

namespace Resgrid.Services
{
	public partial class ShiftsService
	{
		public async Task<List<ShiftDaySchedule>> ReadSchedulesForAdministrationAsync(int departmentId, DateTime localStart, DateTime localEnd,
			DateTime asOfUtc, int maximumRows, CancellationToken cancellationToken)
		{
			if (departmentId <= 0 || asOfUtc.Kind != DateTimeKind.Utc || localEnd < localStart || localEnd - localStart > TimeSpan.FromDays(14) || maximumRows is < 1 or > 10000)
				throw new ArgumentException("Invalid administrative schedule window.");
			cancellationToken.ThrowIfCancellationRequested();
			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId, true) ?? throw new InvalidOperationException("Department unavailable.");
			var zone = TimeZoneInfo.FindSystemTimeZoneById(department.TimeZone);
			var shifts = (await _shiftsRepository.GetShiftAndDaysByDepartmentIdAsync(departmentId))?.ToList() ?? throw new InvalidOperationException("Schedules unavailable.");
			var signups = (await _shiftSignupRepository.GetShiftSignupsByDepartmentIdAndDateRangeAsync(departmentId, localStart.Date, localEnd.Date.AddDays(1)))?.ToList() ?? throw new InvalidOperationException("Signups unavailable.");
			var trades = (await _shiftSignupTradeRepository.GetShiftSignupTradesByDepartmentIdAsync(departmentId, localStart.Date))?.ToList() ?? throw new InvalidOperationException("Trades unavailable.");
			// This owning getter reads membership, role and role-user repositories; it does not use Redis.
			var roles = await _personnelRolesService.GetAllRolesForUsersInDepartmentAsync(departmentId) ?? throw new InvalidOperationException("Roles unavailable.");
			cancellationToken.ThrowIfCancellationRequested();
			var rows = (long)shifts.Count + signups.Count + trades.Count + roles.Sum(r => (long)r.Value.Count) + shifts.Sum(s =>
				(long)(s.Days?.Count ?? 0) + (s.Personnel?.Count ?? 0) + (s.Groups?.Count ?? 0) + (s.Groups?.Sum(g => g.Roles?.Count ?? 0) ?? 0));
			if (rows > maximumRows || shifts.Any(s => s.DepartmentId != departmentId) || roles.Any(r => r.Value.Any(role => role == null)))
				throw new InvalidOperationException("Incomplete or out-of-scope schedule evidence.");
			var shiftIds = shifts.Select(s => s.ShiftId).ToHashSet();
			if (signups.Any(s => !shiftIds.Contains(s.ShiftId)) || trades.Any(t => t.SourceShiftSignup == null || !shiftIds.Contains(t.SourceShiftSignup.ShiftId) ||
				(t.TargetShiftSignupId.HasValue && (t.TargetShiftSignup == null || !shiftIds.Contains(t.TargetShiftSignup.ShiftId)))))
				throw new InvalidOperationException("Incomplete or out-of-scope trade evidence.");
			var data = new ScheduleData { Department = department, Shifts = shifts, Signups = signups, Trades = trades, Roles = roles };
			var localNow = TimeZoneInfo.ConvertTimeFromUtc(asOfUtc, zone);
			return shifts.SelectMany(shift => (shift.Days ?? Array.Empty<ShiftDay>()).Where(day => day.Day.Date >= localStart.Date && day.Day.Date <= localEnd.Date)
				.Select(day => BuildSchedule(shift, day, data, localNow))).ToList();
		}
	}
}
