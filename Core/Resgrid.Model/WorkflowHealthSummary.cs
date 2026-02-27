﻿using System;

namespace Resgrid.Model
{
	/// <summary>
	/// Health summary for a workflow over recent time windows.
	/// </summary>
	public sealed class WorkflowHealthSummary
	{
		public string WorkflowId { get; set; }
		public string WorkflowName { get; set; }

		// Last 24 hours
		public int TotalRuns24h { get; set; }
		public int SuccessfulRuns24h { get; set; }
		public int FailedRuns24h { get; set; }
		public int RetryingRuns24h { get; set; }

		// Last 7 days
		public int TotalRuns7d { get; set; }
		public int SuccessfulRuns7d { get; set; }
		public int FailedRuns7d { get; set; }

		// Last 30 days
		public int TotalRuns30d { get; set; }
		public int SuccessfulRuns30d { get; set; }
		public int FailedRuns30d { get; set; }

		/// <summary>Success rate as a percentage (0–100) over the last 30 days.</summary>
		public double SuccessRatePercent30d =>
			TotalRuns30d == 0 ? 0 : Math.Round((double)SuccessfulRuns30d / TotalRuns30d * 100, 1);

		/// <summary>Average duration of completed runs in milliseconds (last 30 days).</summary>
		public double? AverageDurationMs30d { get; set; }

		public DateTime? LastRunOn { get; set; }
		public WorkflowRunStatus? LastRunStatus { get; set; }
		public string LastErrorMessage { get; set; }
	}
}


