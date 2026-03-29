namespace Resgrid.Web.Services.Models.v4.Calendar
{
	/// <summary>
	/// Input for checking out from a calendar event
	/// </summary>
	public class CalendarCheckOutInput
	{
		/// <summary>
		/// Calendar event item id to check out from
		/// </summary>
		public int CalendarEventId { get; set; }

		/// <summary>
		/// Optional user id for admin/group admin check-out on behalf of another user
		/// </summary>
		public string UserId { get; set; }

		/// <summary>
		/// Optional note for the check-out
		/// </summary>
		public string Note { get; set; }

		/// <summary>
		/// GPS latitude at check-out (from Responder app)
		/// </summary>
		public string Latitude { get; set; }

		/// <summary>
		/// GPS longitude at check-out (from Responder app)
		/// </summary>
		public string Longitude { get; set; }
	}
}
