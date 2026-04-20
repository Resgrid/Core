using System;

namespace Resgrid.Model
{
	/// <summary>
	/// Represents the check-in timer status for a single user on a single active call
	/// that has personnel check-in timers enabled.
	/// </summary>
	public class UserCallCheckInSummary
	{
		/// <summary>The call identifier.</summary>
		public int CallId { get; set; }

		/// <summary>The call name / nature of call.</summary>
		public string CallName { get; set; }

		/// <summary>The human-readable call number (e.g. "2024-0042").</summary>
		public string CallNumber { get; set; }

		/// <summary>UTC timestamp when the call was logged.</summary>
		public DateTime CallStartedOn { get; set; }

		/// <summary>True when a personnel-type check-in timer is active for this call.</summary>
		public bool HasPersonnelTimer { get; set; }

		/// <summary>
		/// How long (in minutes) the user has before they must check in.
		/// Only meaningful when <see cref="HasPersonnelTimer"/> is true.
		/// </summary>
		public int DurationMinutes { get; set; }

		/// <summary>
		/// Number of minutes before the deadline at which a "Warning" status is issued.
		/// Only meaningful when <see cref="HasPersonnelTimer"/> is true.
		/// </summary>
		public int WarningThresholdMinutes { get; set; }

		/// <summary>UTC timestamp of the user's most-recent check-in on this call, or null if never checked in.</summary>
		public DateTime? LastCheckIn { get; set; }

		/// <summary>True when the user must check in immediately (timer has expired).</summary>
		public bool NeedsCheckIn { get; set; }

		/// <summary>
		/// Minutes remaining until the next check-in is required.
		/// Positive = time still available, negative = how many minutes overdue.
		/// Zero or negative implies <see cref="NeedsCheckIn"/> is true.
		/// </summary>
		public double MinutesRemaining { get; set; }

		/// <summary>
		/// Colour-coded status string: "Green", "Warning", "Critical", or "NoTimer"
		/// when no personnel timer is configured for the call.
		/// </summary>
		public string Status { get; set; }
	}
}
