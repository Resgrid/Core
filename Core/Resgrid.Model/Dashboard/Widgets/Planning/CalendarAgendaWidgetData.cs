using System;
using System.Collections.Generic;

namespace Resgrid.Model.Dashboard.Widgets.Planning
{
	/// <summary>
	/// calendar.agenda (plan §7.6): the next days of the department calendar with the viewer's sign-up and check-in state.
	/// Only titles, times, types and locations: <c>calendaritems.description</c> (ADP) is never selected.
	/// </summary>
	public sealed class CalendarAgendaWidgetData
	{
		/// <summary>IANA time zone of the department, for grouping the agenda by local day.</summary>
		public string TimeZoneId { get; set; }

		public int Days { get; set; }

		/// <summary>Items in the window before the type filter and the cap.</summary>
		public int TotalCount { get; set; }

		public List<CalendarAgendaItem> Items { get; set; } = new List<CalendarAgendaItem>();
	}

	public sealed class CalendarAgendaItem
	{
		public int CalendarItemId { get; set; }

		public string Title { get; set; }

		public string Location { get; set; }

		public DateTime StartUtc { get; set; }

		public DateTime EndUtc { get; set; }

		public bool IsAllDay { get; set; }

		/// <summary>CalendarItemType id (0 = no type).</summary>
		public int TypeId { get; set; }

		public string TypeName { get; set; }

		public string TypeColor { get; set; }

		/// <summary>CalendarItemSignupTypes value (0 none, 1 assigned, 2 RSVP).</summary>
		public int SignupType { get; set; }

		/// <summary>CalendarItemCheckInTypes value (0 = check-in disabled).</summary>
		public int CheckInType { get; set; }

		/// <summary>The viewer's CalendarItemAttendeeTypes value (1 RSVP, 2 required, 3 optional, 4 not attending), null when not listed.</summary>
		public int? MyAttendeeType { get; set; }

		public bool CheckedIn { get; set; }

		public bool CheckedOut { get; set; }

		public string Url { get; set; }
	}
}
