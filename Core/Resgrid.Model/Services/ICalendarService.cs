using System;
using System.Collections.Generic;

namespace Resgrid.Model.Services
{
	public interface ICalendarService
	{
		List<CalendarItem> GetAllCalendarItemsForDepartment(int departmentId);
		List<CalendarItemType> GetAllCalendarItemTypesForDepartment(int departmentId);
		CalendarItem SaveCalendarItem(CalendarItem calendarItem);
		CalendarItemType SaveCalendarItemType(CalendarItemType type);
		void DeleteCalendarItemById(int calendarItemId);
		CalendarItem GetCalendarItemById(int calendarItemId);
		CalendarItemType GetCalendarItemTypeById(int calendarItemTypeId);
		void DeleteCalendarItemType(CalendarItemType type);
		void SignupForEvent(int calendarEventItemId, string userId, string note, int attendeeType);
		List<CalendarItem> GetCalendarItemsToNotify(DateTime timestamp);
		void MarkAsNotified(int calendarItemId);
		CalendarItemAttendee GetCalendarAttendeeById(int calendarAttendeeId);
		void DeleteCalendarAttendeeById(int calendarAttendeeId);
		CalendarItemAttendee GetCalendarItemAttendeeByUser(int calendarEventItemId, string userId);
		List<CalendarItem> GetAllCalendarItemsForDepartment(int departmentId, DateTime startDate);
		List<CalendarItem> GetAllCalendarItemsForDepartmentInRange(int departmentId, DateTime startDate, DateTime endDate);
		List<CalendarItem> CreateRecurrenceCalendarItems(CalendarItem item, DateTime currentDate);
		DateTime?[] GetNextWeekValues(CalendarItem item, DateTime currentDate);
		CalendarItem AddNewV2CalendarItem(CalendarItem item, string timeZone);
		List<CalendarItem> GetAllV2CalendarItemsForDepartment(int departmentId, DateTime startDate);
		List<CalendarItem> GetV2CalendarItemsToNotify(DateTime timestamp);
		List<CalendarItem> GetAllV2CalendarItemRecurrences(int calendarItemId);
		CalendarItem UpdateV2CalendarItem(CalendarItem item, string timeZone);
		void DeleteCalendarItemAndRecurrences(int calendarItemId);
		List<CalendarItem> GetUpcomingCalendarItems(int departmentId, DateTime start);
	}
}
