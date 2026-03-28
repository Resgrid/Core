using System;

namespace Resgrid.Web.Services.Models.v4.Calendar
{
	/// <summary>
	/// Input for updating check-in times
	/// </summary>
	public class CalendarCheckInUpdateInput
	{
		/// <summary>
		/// The check-in record id to update
		/// </summary>
		public string CheckInId { get; set; }

		/// <summary>
		/// The updated check-in time in UTC
		/// </summary>
		public DateTime CheckInTime { get; set; }

		/// <summary>
		/// The updated check-out time in UTC (nullable)
		/// </summary>
		public DateTime? CheckOutTime { get; set; }

		/// <summary>
		/// Check-in note
		/// </summary>
		public string CheckInNote { get; set; }

		/// <summary>
		/// Check-out note
		/// </summary>
		public string CheckOutNote { get; set; }
	}
}
