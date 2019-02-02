using System;
using System.Collections.Generic;

namespace Resgrid.Web.Services.Controllers.Version3.Models.Calendar
{
	public class CalendarItem
	{
		public int CalendarItemId { get; set; }

		public string Title { get; set; }

		public DateTime Start { get; set; }

		public DateTime End { get; set; }

		public string StartTimezone { get; set; }

		public string EndTimezone { get; set; }

		public string Description { get; set; }

		public string RecurrenceId { get; set; }

		public string RecurrenceRule { get; set; }

		public string RecurrenceException { get; set; }

		public int? ItemType { get; set; }

		public bool IsAllDay { get; set; }

		public string Location { get; set; }

		public int SignupType { get; set; }

		public int Reminder { get; set; }

		public bool LockEditing { get; set; }

		public string Entities { get; set; }

		public string RequiredAttendes { get; set; }

		public string OptionalAttendes { get; set; }

		public bool IsAdminOrCreator { get; set; }

		public string CreatorUserId { get; set; }

		public bool Attending { get; set; }

		public List<CalendarItemAttendee> Attendees { get; set; }
	}

	public class CalendarItemAttendee
	{
		public int CalendarItemId { get; set; }
		public string UserId { get; set; }
		public int AttendeeType { get; set; }
		public DateTime Timestamp { get; set; }
		public string Note { get; set; }
	}
}
