using System;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Calendar
{
	public class CalendarItemView
	{
		public CalendarItem CalendarItem { get; set; }
		public string Note { get; set; }
		public string UserId { get; set; }
		public Department Department { get; set; }
		public bool IsRecurrenceParent { get; set; }
		public bool CanEdit { get; set; }
	}
}
