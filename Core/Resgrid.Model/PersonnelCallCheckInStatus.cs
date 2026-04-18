using System;

namespace Resgrid.Model
{
	/// <summary>
	/// Represents the check-in timer status for a single dispatched person on a call
	/// that has personnel check-in timers enabled.
	/// </summary>
	public class PersonnelCallCheckInStatus
	{
		/// <summary>The ASP.NET Identity user identifier.</summary>
		public string UserId { get; set; }

		/// <summary>
		/// The user's display name (first + last name from their profile).
		/// May be null if the profile could not be resolved.
		/// </summary>
		public string FullName { get; set; }

		/// <summary>UTC timestamp of the user's most-recent personnel check-in on this call, or null if never checked in.</summary>
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
		/// Colour-coded status string: "Green" (within timer), "Warning" (within warning
		/// threshold), or "Critical" (timer expired).
		/// </summary>
		public string Status { get; set; }

		/// <summary>
		/// The resolved duration (in minutes) of the personnel check-in timer for this call.
		/// </summary>
		public int DurationMinutes { get; set; }

		/// <summary>
		/// The warning threshold (in minutes) of the personnel check-in timer for this call.
		/// </summary>
		public int WarningThresholdMinutes { get; set; }
	}
}
