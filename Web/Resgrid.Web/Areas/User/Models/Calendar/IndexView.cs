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
		/// <summary>The user's CalendarSyncToken (null/empty means sync not yet activated).</summary>
		public string CalendarSyncToken { get; set; }
		/// <summary>The full HTTPS subscription URL to display to the user once sync is activated.</summary>
		public string CalendarSubscriptionUrl { get; set; }
	}
}
