using System;
using System.Collections.Generic;

namespace Resgrid.Model.Checklists
{
	// Values 0-2 retain the original on-demand occurrence/run state mapping.
	public enum ChecklistOccurrenceState { InProgress = 0, AwaitingWitness = 1, Submitted = 2, Scheduled = 3, Missed = 4, Skipped = 5, Cancelled = 6 }
	public sealed class ChecklistSchedule : ChecklistRow
	{
		public int AssignmentType { get; set; }
		public string AssignmentId { get; set; }
		public string VersionId { get; set; }
		public int TargetType { get; set; }
		public string TargetId { get; set; }
		public int? TargetGroupId { get; set; }
		public int Frequency { get; set; }
		public string TimeZoneId { get; set; }
		public DateTime StartDate { get; set; }
		public DateTime? EndDate { get; set; }
		public string ClockMinutes { get; set; }
		public int Weekdays { get; set; }
		public int DayOfMonth { get; set; }
		public int MonthOfYear { get; set; }
		public int WindowMinutes { get; set; }
		public string WorkshiftId { get; set; }
		public bool IsActive { get; set; }
		public bool IsSuspended { get; set; }
		public DateTime ActiveFromUtc { get; set; }
		public DateTime GeneratedThroughUtc { get; set; }
		public DateTime LastSweepUtc { get; set; }
	}
	public sealed class ChecklistScheduleContent { public string Name { get; set; } public string Notes { get; set; } }
	public sealed class ChecklistScheduleInput
	{
		public int AssignmentType { get; set; }
		public string AssignmentId { get; set; }
		public string Id { get; set; }
		public string DefinitionId { get; set; }
		public int Revision { get; set; }
		public string Name { get; set; }
		public string Notes { get; set; }
		public string TargetId { get; set; }
		public ChecklistScheduleFrequency Frequency { get; set; } = ChecklistScheduleFrequency.Daily;
		public string TimeZoneId { get; set; } = "UTC";
		public DateTime StartDate { get; set; } = DateTime.UtcNow.Date;
		public DateTime? EndDate { get; set; }
		public string TimesOfDay { get; set; } = "08:00";
		public int Weekdays { get; set; } = 127;
		public int DayOfMonth { get; set; } = 1;
		public int MonthOfYear { get; set; } = 1;
		public int WindowMinutes { get; set; } = 60;
		public string WorkshiftId { get; set; }
		public bool IsActive { get; set; } = true;
	}
	public sealed class ChecklistScheduleView { public ChecklistSchedule Schedule { get; set; } public ChecklistScheduleContent Content { get; set; } }
	public sealed class ChecklistOccurrenceView
	{
		public ChecklistOccurrence Occurrence { get; set; }
		public string Name { get; set; }
		public ChecklistTarget Target { get; set; }
		public bool CanStart { get; set; }
		public bool CanSkip { get; set; }
	}
	public sealed class ChecklistScheduleSweepResult { public int Generated { get; set; } public int Missed { get; set; } public int Errors { get; set; } }
}
