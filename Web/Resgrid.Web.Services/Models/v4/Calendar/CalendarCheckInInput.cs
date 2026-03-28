namespace Resgrid.Web.Services.Models.v4.Calendar
{
	/// <summary>
	/// Input for checking in to a calendar event
	/// </summary>
	public class CalendarCheckInInput
	{
		/// <summary>
		/// Calendar event item id to check in to
		/// </summary>
		public int CalendarEventId { get; set; }

		/// <summary>
		/// Optional note for the check-in
		/// </summary>
		public string Note { get; set; }

		/// <summary>
		/// Optional user id for admin/group admin check-in on behalf of another user
		/// </summary>
		public string UserId { get; set; }

		/// <summary>
		/// GPS latitude at check-in (from Responder app)
		/// </summary>
		public string Latitude { get; set; }

		/// <summary>
		/// GPS longitude at check-in (from Responder app)
		/// </summary>
		public string Longitude { get; set; }
	}
}
