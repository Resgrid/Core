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

		public string Latitude { get; set; }
		public string Longitude { get; set; }
		public int? UnitId { get; set; }
		public string Note { get; set; }
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
}
