using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.WebCore.Areas.User.Models.Calendar
{
	public class NewCalendarEntry
	{
		public string Message { get; set; }
		public CalendarItem Item { get; set; }
		public RecurrenceTypes RecurrenceTypes { get; set; }
		public List<CalendarItemType> Types { get; set; }
	}
}
