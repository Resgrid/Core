using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Calendar
{
	public class IndexView
	{
		public Department Department { get; set; }

		public string TimeZone { get; set; }
		public List<CalendarItemType> Types { get; set; }
		public List<CalendarItem> UpcomingItems { get; set; }
	}
}
