namespace Resgrid.WebCore.Areas.User.Models.Calendar
{
	public class CalendarItemV2Json
	{
		public int id { get; set; }
		public string title { get; set; }
		public string start { get; set; }
		public string end { get; set; }
		public string url { get; set; }
		public string backgroundColor { get; set; }
		/// <summary>When true FullCalendar v6 renders the event as an all-day banner spanning the full day(s).</summary>
		public bool allDay { get; set; }
	}
}
