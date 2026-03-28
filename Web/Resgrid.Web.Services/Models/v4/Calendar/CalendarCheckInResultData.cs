using System;

namespace Resgrid.Web.Services.Models.v4.Calendar
{
	/// <summary>
	/// Data representing a single calendar check-in record
	/// </summary>
	public class CalendarCheckInResultData
	{
		/// <summary>
		/// The check-in record id
		/// </summary>
		public string CheckInId { get; set; }

		/// <summary>
		/// The calendar item id
		/// </summary>
		public int CalendarItemId { get; set; }

		/// <summary>
		/// The user id who checked in
		/// </summary>
		public string UserId { get; set; }

		/// <summary>
		/// The display name of the user
		/// </summary>
		public string Name { get; set; }

		/// <summary>
		/// Check-in time in UTC
		/// </summary>
		public DateTime CheckInTime { get; set; }

		/// <summary>
		/// Check-out time in UTC (nullable)
		/// </summary>
		public DateTime? CheckOutTime { get; set; }

		/// <summary>
		/// Duration in seconds (null if not checked out)
		/// </summary>
		public double? DurationSeconds { get; set; }

		/// <summary>
		/// Whether the times were manually overridden
		/// </summary>
		public bool IsManualOverride { get; set; }

		/// <summary>
		/// Name of the person who performed the check-in (if on behalf)
		/// </summary>
		public string CheckInByName { get; set; }

		/// <summary>
		/// Name of the person who performed the check-out (if on behalf)
		/// </summary>
		public string CheckOutByName { get; set; }

		/// <summary>
		/// Check-in note
		/// </summary>
		public string CheckInNote { get; set; }

		/// <summary>
		/// Check-out note
		/// </summary>
		public string CheckOutNote { get; set; }

		/// <summary>
		/// GPS latitude at check-in
		/// </summary>
		public string CheckInLatitude { get; set; }

		/// <summary>
		/// GPS longitude at check-in
		/// </summary>
		public string CheckInLongitude { get; set; }

		/// <summary>
		/// GPS latitude at check-out
		/// </summary>
		public string CheckOutLatitude { get; set; }

		/// <summary>
		/// GPS longitude at check-out
		/// </summary>
		public string CheckOutLongitude { get; set; }
	}
}
