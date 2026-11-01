using System;

namespace Resgrid.Model.Dashboard.Widgets.Administration
{
	/// <summary>admin.import (plan §7.11): Department Import workspaces and the progress of the one being worked. Counts only.</summary>
	public sealed class AdminImportWidgetData
	{
		public int OpenWorkspaces { get; set; }

		public int ActivatedWorkspaces { get; set; }

		public int FinishedWorkspaces { get; set; }

		/// <summary>The open workspace with the newest activity; null when none is open.</summary>
		public AdminImportWorkspace Current { get; set; }

		public DateTime? LastActivityOn { get; set; }
	}

	public sealed class AdminImportWorkspace
	{
		public string WorkspaceId { get; set; }

		public string Name { get; set; }

		/// <summary>ImportWorkspaceStatus name (Draft, Collecting, Reviewing, Importing, Verifying, ReadyToActivate) — W.admin.import.Status.{name}.</summary>
		public string Status { get; set; }

		public DateTime LastActivityOn { get; set; }

		public DateTime? TargetActivationDate { get; set; }

		public int Datasets { get; set; }

		/// <summary>Data sets waiting for a person (mapping or attention).</summary>
		public int NeedingReview { get; set; }

		public int Ready { get; set; }

		public int Imported { get; set; }

		public int Failed { get; set; }

		public int RowsTotal { get; set; }

		public int RowsImported { get; set; }

		public int RowsWithWarnings { get; set; }

		public int RowsBlocked { get; set; }

		public int RunsActive { get; set; }

		public int RunsFailed { get; set; }
	}
}
