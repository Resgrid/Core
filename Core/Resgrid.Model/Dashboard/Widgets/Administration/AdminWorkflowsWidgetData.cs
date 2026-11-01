using System;
using System.Collections.Generic;

namespace Resgrid.Model.Dashboard.Widgets.Administration
{
	/// <summary>
	/// admin.workflows (plan §7.11, §9A.2): run health over the last 24 hours and the workflows failing most. Run error text
	/// (workflowruns.errormessage) is ADP-catalogued and the Workflows module has no reveal path, so it is never read here: the client
	/// shows a placeholder that links to the module.
	/// </summary>
	public sealed class AdminWorkflowsWidgetData
	{
		public int Workflows { get; set; }

		public int ActiveWorkflows { get; set; }

		public int Pending { get; set; }

		public int Running { get; set; }

		public int Retrying { get; set; }

		public int Completed24h { get; set; }

		public int Failed24h { get; set; }

		/// <summary>Runs finished in 24 hours (completed, failed, cancelled, skipped).</summary>
		public int Total24h { get; set; }

		/// <summary>Completed / (completed + failed) over 24 hours, 0–100; null without finished runs.</summary>
		public int? SuccessPercent24h { get; set; }

		/// <summary>Department enforces ADP: the client's placeholder reads "Error details protected" rather than "See details".</summary>
		public bool ErrorDetailsProtected { get; set; }

		/// <summary>Workflows with failures in 24 hours, most failures first; at most the maxRows setting.</summary>
		public List<AdminWorkflowHealthRow> Unhealthy { get; set; } = new List<AdminWorkflowHealthRow>();
	}

	public sealed class AdminWorkflowHealthRow
	{
		public string WorkflowId { get; set; }

		public string Name { get; set; }

		public bool IsEnabled { get; set; }

		public int Failed24h { get; set; }

		public int Total24h { get; set; }

		public DateTime? LastFailureOn { get; set; }
	}
}
