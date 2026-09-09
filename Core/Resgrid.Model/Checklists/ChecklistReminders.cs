using System;

namespace Resgrid.Model.Checklists
{
	public sealed class ChecklistReminderSettingsInput
	{
		public bool NotifyAtShiftStart { get; set; } = true;
		public string FixedDigestTime { get; set; }
		public int Revision { get; set; }
		public bool Enabled { get; set; }
		public int NotifyBeforeMinutes { get; set; } = 60;
		public bool NotifyMissed { get; set; } = true;
		public bool DigestMode { get; set; } = true;
		public int? EscalateAfterMinutes { get; set; }
	}
	public enum ChecklistReminderKind { Due = 0, Missed = 1, Escalation = 2, ShiftStart = 3, FixedTime = 4 }
	public enum ChecklistReminderStatus { Pending = 0, HandedOff = 1, Suppressed = 2 }
	// Delivery bookkeeping contains routing identifiers only. Never store message bodies,
	// checklist content, contact details, grants, exception text or rendered outcomes here.
	public sealed class ChecklistReminder
	{
		public string PeriodKey { get; set; } = "";
		public string Id { get; set; } = Guid.NewGuid().ToString("D");
		public int DepartmentId { get; set; }
		public string OccurrenceId { get; set; }
		public string RecipientUserId { get; set; }
		public int Kind { get; set; }
		public int Status { get; set; }
		public DateTime CreatedOnUtc { get; set; }
		public DateTime NextAttemptUtc { get; set; }
		public int Attempts { get; set; }
		public string ClaimToken { get; set; }
		public DateTime? ClaimUntilUtc { get; set; }
		public DateTime? CompletedOnUtc { get; set; }
	}
	public sealed class ChecklistReminderSweepResult
	{
		public int HandedOff { get; set; }
		public int Suppressed { get; set; }
		public int Errors { get; set; }
	}
}
