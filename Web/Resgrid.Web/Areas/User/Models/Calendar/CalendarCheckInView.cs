using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Calendar
{
	public class CalendarCheckInView
	{
		public CalendarItem CalendarItem { get; set; }
		public CalendarItemCheckIn UserCheckIn { get; set; }
		public List<CalendarItemCheckIn> CheckIns { get; set; }
		public bool IsAdmin { get; set; }
		public List<PersonName> PersonnelNames { get; set; }
	}
}
