using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	public interface ICalendarItemCheckInRepository : IRepository<CalendarItemCheckIn>
	{
		Task<CalendarItemCheckIn> GetCheckInByCalendarItemAndUserAsync(int calendarItemId, string userId);
		Task<IEnumerable<CalendarItemCheckIn>> GetCheckInsByCalendarItemIdAsync(int calendarItemId);
		Task<IEnumerable<CalendarItemCheckIn>> GetCheckInsByDepartmentAndDateRangeAsync(int departmentId, DateTime start, DateTime end);
		Task<IEnumerable<CalendarItemCheckIn>> GetCheckInsByUserAndDateRangeAsync(string userId, int departmentId, DateTime start, DateTime end);
	}
}
