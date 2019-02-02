using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	public interface ICalendarItemsRepository : IRepository<CalendarItem>
	{
		List<CalendarItem> GetAllCalendarItemsToNotify();
		List<CalendarItem> GetAllV2CalendarItemsForDepartment(int departmentId, DateTime startDate);
		List<CalendarItem> GetAllV2CalendarItemsToNotify(DateTime startDate);
		List<CalendarItem> GetAllV2CalendarItemRecurrences(string calendarItemId);
		bool DeleteCalendarItemAndRecurrences(int calendarItemId);

		Task<List<CalendarItem>> GetAllCalendarItemsToNotifyAsync();
		Task<List<CalendarItem>> GetAllV2CalendarItemsForDepartmentAsync(int departmentId, DateTime startDate);
		Task<List<CalendarItem>> GetAllV2CalendarItemRecurrencesAsync(string calendarItemId);
		Task<List<CalendarItem>> GetAllV2CalendarItemsToNotifyAsync(DateTime startDate);
		Task<bool> DeleteCalendarItemAndRecurrencesAsync(int calendarItemId);
	}
}
