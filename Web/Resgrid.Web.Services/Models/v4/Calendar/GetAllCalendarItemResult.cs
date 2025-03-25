using System;
using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Calendar;

/// <summary>
/// Result containing all calendar items
/// </summary>
public class GetAllCalendarItemResult : StandardApiResponseV4Base
{
	/// <summary>
	/// Response Data
	/// </summary>
	public List<GetAllCalendarItemResultData> Data { get; set; }
}

/// <summary>
///
/// </summary>
public class GetAllCalendarItemResultData
{
	/// <summary>
	/// Identifier for the calendar item
	/// </summary>
	public string CalendarItemId { get; set; }

	/// <summary>
	/// Title
	/// </summary>
	public string Title { get; set; }

	/// <summary>
	/// Start Time (local for department)
	/// </summary>
	public DateTime Start { get; set; }

	/// <summary>
	/// Start Time in UTC
	/// </summary>
	public DateTime StartUtc { get; set; }

	/// <summary>
	/// End time (local for the department)
	/// </summary>
	public DateTime End { get; set; }

	/// <summary>
	/// End Time in UTC
	/// </summary>
	public DateTime EndUtc { get; set; }

	/// <summary>
	/// Start Timezone
	/// </summary>
	public string StartTimezone { get; set; }

	/// <summary>
	/// End Timezone
	/// </summary>
	public string EndTimezone { get; set; }

	/// <summary>
	/// Description for Calendar Item
	/// </summary>
	public string Description { get; set; }

	/// <summary>
	/// Id for reoccurring
	/// </summary>
	public string RecurrenceId { get; set; }

	/// <summary>
	/// Rule used to calculate reoccurring
	/// </summary>
	public string RecurrenceRule { get; set; }

	/// <summary>
	/// Reoccurring exceptions
	/// </summary>
	public string RecurrenceException { get; set; }

	/// <summary>
	/// Calendar Item Type
	/// </summary>
	public int? ItemType { get; set; }

	/// <summary>
	/// Is an all day event
	/// </summary>
	public bool IsAllDay { get; set; }

	/// <summary>
	/// Location for the calendar item
	/// </summary>
	public string Location { get; set; }

	/// <summary>
	/// Signup type
	/// </summary>
	public int SignupType { get; set; }

	/// <summary>
	/// Reminder type
	/// </summary>
	public int Reminder { get; set; }

	/// <summary>
	/// Prevent editing
	/// </summary>
	public bool LockEditing { get; set; }

	/// <summary>
	/// Entities (groups) list
	/// </summary>
	public string Entities { get; set; }

	/// <summary>
	/// Who, persons, that are required
	/// </summary>
	public string RequiredAttendes { get; set; }

	/// <summary>
	/// Who, persons, that are optional
	/// </summary>
	public string OptionalAttendes { get; set; }

	/// <summary>
	/// Is locked to admin or calendar creator
	/// </summary>
	public bool IsAdminOrCreator { get; set; }

	/// <summary>
	/// UserId of who created the event
	/// </summary>
	public string CreatorUserId { get; set; }

	/// <summary>
	/// Are you attending
	/// </summary>
	public bool Attending { get; set; }

	/// <summary>
	/// Color of the Type (if any)
	/// </summary>
	public string TypeColor { get; set; }

	/// <summary>
	/// Name of the type (if any)
	/// </summary>
	public string TypeName { get; set; }

	/// <summary>
	/// All attendees
	/// </summary>
	public List<CalendarItemResultAttendeeData> Attendees { get; set; }
}

/// <summary>
/// Data describing a calendar item attendee
/// </summary>
public class CalendarItemResultAttendeeData
{
	/// <summary>
	/// Calendar item identifier
	/// </summary>
	public string CalendarItemId { get; set; }

	/// <summary>
	/// User Id of who's attending
	/// </summary>
	public string UserId { get; set; }

	/// <summary>
	/// Name of the user who is attending
	/// </summary>
	public string Name { get; set; }

	/// <summary>
	/// Group Name of the user who is attending
	/// </summary>
	public string GroupName { get; set; }

	/// <summary>
	/// Attendance type
	/// </summary>
	public int AttendeeType { get; set; }

	/// <summary>
	/// Timestamp of when the user said they were attending
	/// </summary>
	public DateTime Timestamp { get; set; }

	/// <summary>
	/// User supplied note
	/// </summary>
	public string Note { get; set; }
}
