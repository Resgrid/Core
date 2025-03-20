namespace Resgrid.Web.Services.Controllers.Version3.Models.Calendar
{
	/// <summary>
	/// Input object to set the attendace to a calendar event
	/// </summary>
	public class CalendarItemAttendInput
	{
		/// <summary>
		/// The identifier of the calendar event
		/// </summary>
		public string CalendarEventId { get; set; }

		/// <summary>
		/// An optional note for the status
		/// </summary>
		public string Note { get; set; }

		/// <summary>
		/// 1 = Attending, 4 = Not Attending
		/// </summary>
		public int Type { get; set; }
	}
}
