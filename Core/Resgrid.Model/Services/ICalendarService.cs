using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Interface ICalendarService
	/// </summary>
	public interface ICalendarService
	{
		Task<List<CalendarItem>> GetAllCalendarItemsForDepartmentAsync(int departmentId);

		Task<List<CalendarItem>> GetAllCalendarItemsForDepartmentAsync(int departmentId, DateTime startDate);

		Task<List<CalendarItem>> GetAllCalendarItemsForDepartmentInRangeAsync(int departmentId, DateTime startDate,
			DateTime endDate);

		Task<List<CalendarItemType>> GetAllCalendarItemTypesForDepartmentAsync(int departmentId);

		Task<List<CalendarItem>> GetUpcomingCalendarItemsAsync(int departmentId, DateTime start);

		Task<CalendarItem> SaveCalendarItemAsync(CalendarItem calendarItem,
			CancellationToken cancellationToken = default(CancellationToken));

		Task<CalendarItem> GetCalendarItemByIdAsync(int calendarItemId);

		Task<CalendarItemAttendee> GetCalendarAttendeeByIdAsync(int calendarAttendeeId);

		Task<bool> DeleteCalendarItemByIdAsync(int calendarItemId,
			CancellationToken cancellationToken = default(CancellationToken));

		Task<bool> DeleteCalendarAttendeeByIdAsync(int calendarAttendeeId,
			CancellationToken cancellationToken = default(CancellationToken));

		Task<CalendarItemType> SaveCalendarItemTypeAsync(CalendarItemType type,
			CancellationToken cancellationToken = default(CancellationToken));

		Task<CalendarItemType> GetCalendarItemTypeByIdAsync(int calendarItemTypeId);

		Task<bool> DeleteCalendarItemTypeAsync(CalendarItemType type,
			CancellationToken cancellationToken = default(CancellationToken));

		Task<CalendarItem> AddNewCalendarItemAsync(CalendarItem item, string timeZone,
			CancellationToken cancellationToken = default(CancellationToken));

		Task<CalendarItem> UpdateCalendarItemAsync(CalendarItem item, string timeZone,
			CancellationToken cancellationToken = default(CancellationToken));

		Task<List<CalendarItem>> GetAllCalendarItemRecurrencesAsync(int calendarItemId);

		Task<bool> DeleteCalendarItemAndRecurrences(int calendarItemId,
			CancellationToken cancellationToken = default(CancellationToken));

		Task<List<CalendarItem>> CreateRecurrenceCalendarItemsAsync(CalendarItem item, DateTime start);

		DateTime?[] GetNextWeekValues(CalendarItem item, DateTime currentDate);

		Task<List<CalendarItem>> GetCalendarItemsForDepartmentInRangeAsync(int departmentId, DateTime start);

		Task<CalendarItemAttendee> SignupForEvent(int calendarEventItemId, string userId, string note, int attendeeType,
			CancellationToken cancellationToken = default(CancellationToken));

		Task<CalendarItemAttendee> GetCalendarItemAttendeeByUserAsync(int calendarEventItemId, string userId);

		Task<List<CalendarItem>> GetCalendarItemsToNotifyAsync(DateTime timestamp);

		Task<bool> MarkAsNotifiedAsync(int calendarItemId,
			CancellationToken cancellationToken = default(CancellationToken));

		int NotificationTypeToMinutes(int notificationType);

		Task<bool> NotifyNewCalendarItemAsync(CalendarItem calendarItem);
	}
}
