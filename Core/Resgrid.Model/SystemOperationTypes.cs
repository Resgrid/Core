namespace Resgrid.Model
{
	/// <summary>
	/// An operation staff can ask the worker to run on demand (BackOffice -> System Operations, worker command 76),
	/// stored on SystemOperationRequests.OperationType. Append-only: the value is persisted, so never renumber.
	/// 1-9 rebuild cached state; 10 and up run one of the worker's daily jobs now. Every value needs a
	/// <see cref="SystemOperationCatalog"/> entry and a case in the worker's SystemOperationRunner.
	/// </summary>
	public enum SystemOperationTypes
	{
		/// <summary>The four Redis visibility matrices per department (Security Refresh, worker 15).</summary>
		RebuildSecurityMatrices = 1,

		/// <summary>Invalidates the department-scoped cache entries so they reload from the database.</summary>
		ClearDepartmentCaches = 2,

		/// <summary>Regenerates the TTS service's static voice prompts (worker 18).</summary>
		RefreshTtsStaticPrompts = 3,

		/// <summary>System SQL Queue (worker 14): department deletions whose waiting period has passed.</summary>
		PendingDepartmentDeletions = 10,

		/// <summary>Reporting Rollup (worker 21).</summary>
		ReportingRollup = 11,

		/// <summary>Unit Tracking Location Retention (worker 24).</summary>
		UnitTrackingLocationRetention = 12,

		/// <summary>Chat Retention (worker 25).</summary>
		ChatRetention = 13,

		/// <summary>Bid Expiration (worker 31).</summary>
		BidExpiration = 14,

		/// <summary>Deployment Finance Reminder (worker 32).</summary>
		DeploymentFinanceReminder = 15,

		/// <summary>Compliance Expiry (worker 33).</summary>
		ComplianceExpiry = 16,

		/// <summary>RMS Due State Evaluation (worker 42).</summary>
		RmsDueStateEvaluation = 17,

		/// <summary>RMS Retention And Purge (worker 43).</summary>
		RmsRetentionAndPurge = 18,

		/// <summary>Pay Data Reporting Readiness (worker 49).</summary>
		PayDataReportingReadiness = 19,

		/// <summary>Protected Workflow Sweep (worker 71).</summary>
		ProtectedWorkflowSweep = 20,

		/// <summary>UTF-8 Data Cleanup (worker 22).</summary>
		Utf8Cleanup = 21
	}
}
