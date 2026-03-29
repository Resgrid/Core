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
		// ...existing methods...

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

		Task<bool> NotifyUsersAboutCalendarItemAsync(CalendarItem calendarItem, List<string> userIds);

		// ── External calendar sync ─────────────────────────────────────────────────

		/// <summary>
		/// Activates external calendar sync for the specified user, generating a new sync GUID,
		/// persisting it on the user's profile, and returning the encrypted URL-safe token
		/// that should be embedded in the subscription URL.
		/// </summary>
		Task<string> ActivateCalendarSyncAsync(int departmentId, string userId,
			CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Regenerates the external calendar sync token for the user, invalidating any
		/// previously issued subscription URLs.
		/// </summary>
		Task<string> RegenerateCalendarSyncAsync(int departmentId, string userId,
			CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Returns the encrypted feed token for a user who has already activated sync,
		/// without regenerating the underlying GUID. Returns null if sync is not activated.
		/// </summary>
		Task<string> GetCalendarFeedTokenAsync(int departmentId, string userId);

		/// <summary>
		/// Validates an encrypted calendar feed token extracted from a subscription URL.
		/// Returns (DepartmentId, UserId) if the token is valid and the stored sync GUID matches;
		/// returns null if the token is invalid, expired, or has been regenerated.
		/// </summary>
		Task<(int DepartmentId, string UserId)?> ValidateCalendarFeedTokenAsync(string encryptedToken);

		// ── Calendar Check-In Attendance ───────────────────────────────────────────

		Task<CalendarItemCheckIn> CheckInToEventAsync(int calendarItemId, string userId, string note,
			string adminUserId = null, string latitude = null, string longitude = null,
			CancellationToken cancellationToken = default(CancellationToken));

		Task<CalendarItemCheckIn> CheckOutFromEventAsync(int calendarItemId, string userId,
			string note = null, string adminUserId = null, string latitude = null, string longitude = null,
			CancellationToken cancellationToken = default(CancellationToken));

		Task<CalendarItemCheckIn> UpdateCheckInTimesAsync(string checkInId, DateTime checkInTime,
			DateTime? checkOutTime, string checkInNote, string checkOutNote,
			CancellationToken cancellationToken = default(CancellationToken));

		Task<CalendarItemCheckIn> GetCheckInByCalendarItemAndUserAsync(int calendarItemId, string userId);

		Task<CalendarItemCheckIn> GetCheckInByIdAsync(string checkInId);

		Task<List<CalendarItemCheckIn>> GetCheckInsByCalendarItemAsync(int calendarItemId);

		Task<bool> DeleteCheckInAsync(string checkInId, CancellationToken cancellationToken = default(CancellationToken));

		Task<List<CalendarItemCheckIn>> GetCheckInsByDepartmentDateRangeAsync(int departmentId, DateTime start, DateTime end);

		Task<List<CalendarItemCheckIn>> GetCheckInsByUserDateRangeAsync(string userId, int departmentId, DateTime start, DateTime end);
	}
}
