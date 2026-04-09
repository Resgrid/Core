using System;
using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.WebCore.Areas.User.Models.Calendar
{
	public class EditCalendarEntry
	{
		public string Message { get; set; }
		public CalendarItem Item { get; set; }
		public RecurrenceTypes RecurrenceTypes { get; set; }
		public List<CalendarItemType> Types { get; set; }

		public DateTime StartTime { get; set; }
		public DateTime EndTime { get; set; }
		public DateTime? RecurrenceEndLocal { get; set; }
		public bool IsRecurrenceParent { get; set; }
		public string entities { get; set; }
		public int WeekdayOccurrence { get; set; }
		public int WeekdayDayOfWeek { get; set; }
	}
}
