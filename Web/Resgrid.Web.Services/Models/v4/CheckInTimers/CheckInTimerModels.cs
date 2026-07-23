using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Resgrid.Web.Services.Models.v4.CheckInTimers
{
	// ── Config ──────────────────────────────────────────────────

	public class CheckInTimerConfigResult : StandardApiResponseV4Base
	{
		public List<CheckInTimerConfigResultData> Data { get; set; }
	}

	public class CheckInTimerConfigResultData
	{
		public string CheckInTimerConfigId { get; set; }
		public int DepartmentId { get; set; }
		public int TimerTargetType { get; set; }
		public string TimerTargetTypeName { get; set; }
		public int? UnitTypeId { get; set; }
		public int DurationMinutes { get; set; }
		public int WarningThresholdMinutes { get; set; }
		public bool IsEnabled { get; set; }
		public string ActiveForStates { get; set; }
		public DateTime CreatedOn { get; set; }
		public DateTime? UpdatedOn { get; set; }
	}

	public class CheckInTimerConfigInput
	{
		public string CheckInTimerConfigId { get; set; }

		[Required]
		[Range(0, int.MaxValue)]
		public int TimerTargetType { get; set; }

		public int? UnitTypeId { get; set; }

		[Required]
		[Range(1, int.MaxValue)]
		public int DurationMinutes { get; set; }

		[Required]
		[Range(1, int.MaxValue)]
		public int WarningThresholdMinutes { get; set; }

		public bool IsEnabled { get; set; } = true;

		public string ActiveForStates { get; set; }
	}

	public class SaveCheckInTimerConfigResult : StandardApiResponseV4Base
	{
		public string Id { get; set; }
	}

	// ── Override ────────────────────────────────────────────────

	public class CheckInTimerOverrideResult : StandardApiResponseV4Base
	{
		public List<CheckInTimerOverrideResultData> Data { get; set; }
	}

	public class CheckInTimerOverrideResultData
	{
		public string CheckInTimerOverrideId { get; set; }
		public int DepartmentId { get; set; }
		public int? CallTypeId { get; set; }
		public int? CallPriority { get; set; }
		public int TimerTargetType { get; set; }
		public string TimerTargetTypeName { get; set; }
		public int? UnitTypeId { get; set; }
		public int DurationMinutes { get; set; }
		public int WarningThresholdMinutes { get; set; }
		public bool IsEnabled { get; set; }
		public string ActiveForStates { get; set; }
		public DateTime CreatedOn { get; set; }
		public DateTime? UpdatedOn { get; set; }
	}

	public class CheckInTimerOverrideInput
	{
		public string CheckInTimerOverrideId { get; set; }
		public int? CallTypeId { get; set; }
		public int? CallPriority { get; set; }

		[Required]
		[Range(0, int.MaxValue)]
		public int TimerTargetType { get; set; }

		public int? UnitTypeId { get; set; }

		[Required]
		[Range(1, int.MaxValue)]
		public int DurationMinutes { get; set; }

		[Required]
		[Range(1, int.MaxValue)]
		public int WarningThresholdMinutes { get; set; }

		public bool IsEnabled { get; set; } = true;

		public string ActiveForStates { get; set; }
	}

	public class SaveCheckInTimerOverrideResult : StandardApiResponseV4Base
	{
		public string Id { get; set; }
	}

	// ── Timer Status ────────────────────────────────────────────

	public class CheckInTimerStatusResult : StandardApiResponseV4Base
	{
		public List<CheckInTimerStatusResultData> Data { get; set; }
	}

	public class CheckInTimerStatusResultData
	{
		public int TargetType { get; set; }
		public string TargetTypeName { get; set; }
		public string TargetEntityId { get; set; }
		public string TargetName { get; set; }
		public int? UnitId { get; set; }
		public DateTime? LastCheckIn { get; set; }
		public int DurationMinutes { get; set; }
		public int WarningThresholdMinutes { get; set; }
		public double ElapsedMinutes { get; set; }
		public string Status { get; set; }
	}

	// ── Check-in ────────────────────────────────────────────────

	public class PerformCheckInInput
	{
		[Required]
		[Range(1, int.MaxValue)]
		public int CallId { get; set; }

		[Required]
		[Range(0, int.MaxValue)]
		public int CheckInType { get; set; }

		/// <summary>Optional personnel user id when an incident commander checks in on that person's behalf.</summary>
		public string UserId { get; set; }

		public string Latitude { get; set; }
		public string Longitude { get; set; }
		public int? UnitId { get; set; }
		public string Note { get; set; }

		/// <summary>Optional offline idempotency key (the client's outbox event id); a replayed check-in dedups on it.</summary>
		public string IdempotencyKey { get; set; }
	}

	public class PerformCheckInResult : StandardApiResponseV4Base
	{
		public string Id { get; set; }
	}

	public class CheckInRecordResult : StandardApiResponseV4Base
	{
		public List<CheckInRecordResultData> Data { get; set; }
	}

	public class CheckInRecordResultData
	{
		public string CheckInRecordId { get; set; }
		public int CallId { get; set; }
		public int CheckInType { get; set; }
		public string CheckInTypeName { get; set; }
		public string UserId { get; set; }
		public int? UnitId { get; set; }
		public string Latitude { get; set; }
		public string Longitude { get; set; }
		public DateTime Timestamp { get; set; }
		public string Note { get; set; }
	}

	// ── Resolved Timers ─────────────────────────────────────────

	public class ResolvedCheckInTimerResult : StandardApiResponseV4Base
	{
		public List<ResolvedCheckInTimerResultData> Data { get; set; }
	}

	public class ResolvedCheckInTimerResultData
	{
		public int TargetType { get; set; }
		public string TargetTypeName { get; set; }
		public int? UnitTypeId { get; set; }
		public string TargetEntityId { get; set; }
		public string TargetName { get; set; }
		public int DurationMinutes { get; set; }
		public int WarningThresholdMinutes { get; set; }
		public bool IsFromOverride { get; set; }
		public string ActiveForStates { get; set; }
	}

	// ── Toggle ──────────────────────────────────────────────────

	public class ToggleCallTimersResult : StandardApiResponseV4Base
	{
		public string Id { get; set; }
	}

	// ── Endpoint 1: User active-call check-in summaries ─────────

	/// <summary>
	/// Response wrapper for <see cref="GetUserCallCheckInStatuses"/>.
	/// </summary>
	public class UserCallCheckInStatusResult : StandardApiResponseV4Base
	{
		public List<UserCallCheckInStatusResultData> Data { get; set; }
	}

	/// <summary>
	/// Per-call check-in status for the requested user.
	/// </summary>
	public class UserCallCheckInStatusResultData
	{
		/// <summary>The call identifier.</summary>
		public int CallId { get; set; }

		/// <summary>Call name / nature of call.</summary>
		public string CallName { get; set; }

		/// <summary>Human-readable call number.</summary>
		public string CallNumber { get; set; }

		/// <summary>UTC timestamp when the call was logged.</summary>
		public DateTime CallStartedOn { get; set; }

		/// <summary>True when a personnel check-in timer is active on this call.</summary>
		public bool HasPersonnelTimer { get; set; }

		/// <summary>Timer interval in minutes (0 when HasPersonnelTimer is false).</summary>
		public int DurationMinutes { get; set; }

		/// <summary>Warning window in minutes before the deadline (0 when HasPersonnelTimer is false).</summary>
		public int WarningThresholdMinutes { get; set; }

		/// <summary>UTC timestamp of the user's last check-in on this call, or null.</summary>
		public DateTime? LastCheckIn { get; set; }

		/// <summary>True when the timer has expired and the user must check in immediately.</summary>
		public bool NeedsCheckIn { get; set; }

		/// <summary>
		/// Minutes remaining until the next check-in is due.
		/// Positive = time still available; negative = number of minutes overdue.
		/// </summary>
		public double MinutesRemaining { get; set; }

		/// <summary>Colour-coded status: "Green", "Warning", "Critical", or "NoTimer".</summary>
		public string Status { get; set; }
	}

	// ── Endpoint 2: Call personnel check-in statuses ────────────

	/// <summary>
	/// Response wrapper for <see cref="GetCallPersonnelCheckInStatuses"/>.
	/// </summary>
	public class CallPersonnelCheckInStatusResult : StandardApiResponseV4Base
	{
		/// <summary>The call identifier that was queried.</summary>
		public int CallId { get; set; }

		/// <summary>True when a personnel check-in timer is currently active for this call.</summary>
		public bool HasActivePersonnelTimer { get; set; }

		/// <summary>Resolved timer duration in minutes (0 when HasActivePersonnelTimer is false).</summary>
		public int DurationMinutes { get; set; }

		/// <summary>Warning window in minutes (0 when HasActivePersonnelTimer is false).</summary>
		public int WarningThresholdMinutes { get; set; }

		public List<CallPersonnelCheckInStatusResultData> Data { get; set; }
	}

	/// <summary>
	/// Per-person check-in status on a specific call.
	/// </summary>
	public class CallPersonnelCheckInStatusResultData
	{
		/// <summary>The ASP.NET Identity user identifier.</summary>
		public string UserId { get; set; }

		/// <summary>The user's full display name (first + last from their profile).</summary>
		public string FullName { get; set; }

		/// <summary>UTC timestamp of the user's last personnel check-in on this call, or null.</summary>
		public DateTime? LastCheckIn { get; set; }

		/// <summary>True when the timer has expired and this person must check in immediately.</summary>
		public bool NeedsCheckIn { get; set; }

		/// <summary>
		/// Minutes remaining until the next check-in is due.
		/// Positive = time still available; negative = number of minutes overdue.
		/// </summary>
		public double MinutesRemaining { get; set; }

		/// <summary>Colour-coded status: "Green", "Warning", or "Critical".</summary>
		public string Status { get; set; }
	}
}
