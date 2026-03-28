using System;
using System.Collections.Generic;
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
		/// <summary>URL to download a single-event .ics file for this calendar item via the v4 API.</summary>
		public string ExportIcsUrl { get; set; }
		public CalendarItemCheckIn UserCheckIn { get; set; }
		public List<CalendarItemCheckIn> CheckIns { get; set; }
		public bool IsAdmin { get; set; }
		public List<PersonName> PersonnelNames { get; set; }
	}
}
