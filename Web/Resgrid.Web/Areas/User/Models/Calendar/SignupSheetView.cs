using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Calendar
{
	public class SignupSheetView
	{
		public CalendarItem CalendarItem { get; set; }
		public Department Department { get; set; }
		public List<PersonName> PersonnelNames { get; set; } = new List<PersonName>();
		public int TotalRows { get; set; }
	}
}
