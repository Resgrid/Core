using System;

namespace Resgrid.Model.Dashboard.Widgets.Administration
{
	/// <summary>
	/// admin.setup (plan §7.11): Admin Assist setup progress from the same overview v4 AdminAssist/Overview returns — the setup report's
	/// verified / required checks, the setup plan's tasks and the next one to do. The AdminAssistSetupPrompt banner stays above the grid.
	/// </summary>
	public sealed class AdminSetupWidgetData
	{
		/// <summary>The overview was read in setup mode (Admin.Setup); false = the Admin Assist workspace.</summary>
		public bool SetupMode { get; set; }

		/// <summary>Configuration checks in the selected areas.</summary>
		public int ChecksRequired { get; set; }

		public int ChecksVerified { get; set; }

		public int ChecksFailed { get; set; }

		public int ChecksUnknown { get; set; }

		/// <summary>ChecksVerified / ChecksRequired × 100; 100 when nothing is required.</summary>
		public int PercentComplete { get; set; }

		public int TasksTotal { get; set; }

		/// <summary>Tasks learned, available, configured or verified.</summary>
		public int TasksDone { get; set; }

		/// <summary>Key capabilities in the plan whose configuration needs attention.</summary>
		public int NeedsAttention { get; set; }

		/// <summary>The first pending task's capability, localized (Admin Assist strings); null when the plan is done.</summary>
		public string NextLabel { get; set; }

		/// <summary>Where the next task is done; falls back to the setup wizard.</summary>
		public string NextUrl { get; set; }

		public bool RevisitDue { get; set; }

		public DateTime? RevisitOnUtc { get; set; }
	}
}
