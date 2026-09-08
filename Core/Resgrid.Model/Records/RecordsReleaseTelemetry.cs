using System;
using System.Collections.Generic;

namespace Resgrid.Model
{
	/// <summary>
	/// The RMS-4 baseline telemetry (RMS plan section 6, RMS-4: "legacy-write attempts, outbox lag, Workflow
	/// run/skip/retry counts, scope-leak alerts, protected-content scans — so a problem after release is visible
	/// rather than reported by a customer"). Counts and ages only; nothing here is record content. One snapshot per
	/// department per read; the daily worker logs a structured line so the numbers exist in the log store too.
	/// </summary>
	public class RecordsReleaseTelemetry
	{
		public int DepartmentId { get; set; }
		public DateTime GeneratedOn { get; set; } = DateTime.UtcNow;
		/// <summary>Window the rolling counters cover, in hours.</summary>
		public int WindowHours { get; set; }

		public bool RecordsActive { get; set; }
		public DateTime? ActivatedOn { get; set; }

		/// <summary>Denied legacy Log/UnitLog mutation attempts inside the window (RmsAccessAudits action LegacyWriteDenied).</summary>
		public int LegacyWriteAttempts { get; set; }
		/// <summary>Denied Records reads/actions inside the window (RmsAccessAudits action Denied) — the runtime face of the scope-leak suite.</summary>
		public int AuthorizationDenials { get; set; }

		public int OutboxPending { get; set; }
		public int OutboxFailed { get; set; }
		/// <summary>Age of the oldest undispatched outbox row; the section 5.12 alert threshold is 60 sustained seconds.</summary>
		public double? OutboxOldestPendingSeconds { get; set; }
		public bool OutboxLagAlert { get; set; }

		public int WorkflowRunsCompleted { get; set; }
		public int WorkflowRunsSkipped { get; set; }
		public int WorkflowRunsFailed { get; set; }
		public int WorkflowRunsRunning { get; set; }

		public int AttachmentsScanPending { get; set; }
		public int AttachmentsScanRejected { get; set; }

		public int RecordsCreated { get; set; }
		public int RecordsFinalized { get; set; }
		public int RecordsOverdue { get; set; }
		public int SubmissionsFailed { get; set; }
		public int SubmissionsAwaiting { get; set; }

		/// <summary>Prevention sweep counters from the last daily run, when the modules are enabled.</summary>
		public int InspectionsDue { get; set; }
		public int ViolationsOverdue { get; set; }
		public int PermitsExpiringSoon { get; set; }
		public int HydrantsOutOfService { get; set; }

		/// <summary>Whether the Records search host is configured (SearchConfig.Enabled); the index sweep reports its own health on the Settings page.</summary>
		public bool SearchEnabled { get; set; }
		public bool ProtectionEnforced { get; set; }
		public int ProtectedCatalogVersion { get; set; }

		public List<string> Alerts { get; set; } = new List<string>();
		public List<string> Warnings { get; set; } = new List<string>();
	}

	/// <summary>Outcome of one prevention sweep (worker 42, daily).</summary>
	public class RecordsPreventionSweepResult
	{
		public int DepartmentsEvaluated { get; set; }
		public int InspectionsGenerated { get; set; }
		public int ViolationsBecameOverdue { get; set; }
		public int PermitsExpiringNotified { get; set; }
		public int PermitsExpired { get; set; }
		public int HydrantTestsDue { get; set; }
		public int Errors { get; set; }
	}
}
