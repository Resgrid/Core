using System;

namespace Resgrid.Model.Onboarding
{
	/// <summary>
	/// Department Import progress for the home dashboard Import &amp; Migration widget (home-dashboard-redesign-plan.md §7.11): workspace
	/// counts plus the data sets and runs of the most recently active open workspace. Counts only; no row content.
	/// </summary>
	public sealed class DepartmentImportSummary
	{
		/// <summary>Workspaces still being worked (Draft through ReadyToActivate).</summary>
		public int OpenWorkspaces { get; set; }

		public int ActivatedWorkspaces { get; set; }

		/// <summary>Closed, abandoned or cancelled.</summary>
		public int FinishedWorkspaces { get; set; }

		/// <summary>The open workspace with the newest activity, or null.</summary>
		public ImportWorkspace Current { get; set; }

		public int Datasets { get; set; }

		/// <summary>Data sets waiting for a person: NeedsMapping or NeedsAttention.</summary>
		public int DatasetsNeedingReview { get; set; }

		public int DatasetsReady { get; set; }

		public int DatasetsImported { get; set; }

		public int DatasetsFailed { get; set; }

		public int RowsTotal { get; set; }

		public int RowsWithWarnings { get; set; }

		public int RowsBlocked { get; set; }

		/// <summary>Rows written: created, updated, linked or unchanged.</summary>
		public int RowsImported { get; set; }

		/// <summary>Runs queued, running, paused, reconciling or awaiting a checker.</summary>
		public int RunsActive { get; set; }

		public int RunsFailed { get; set; }

		public DateTime? LastActivityOn { get; set; }
	}
}
