using System.Collections.Generic;
using System.Linq;
using Resgrid.Model.Onboarding;

namespace Resgrid.Services.Onboarding
{
	/// <summary>
	/// The workspace, dataset and run state rules (wizard plan section 10.2). The workspace status is derived from its
	/// datasets and runs, never set by hand, except Cancelled (and Abandoned, set by the maintenance worker).
	/// </summary>
	public static class DepartmentImportStateMachine
	{
		public static readonly ImportRunStatus[] OpenRunStatuses =
		{
			ImportRunStatus.Queued, ImportRunStatus.Running, ImportRunStatus.Paused, ImportRunStatus.Reconciling, ImportRunStatus.AwaitingChecker
		};

		public static bool IsTerminal(ImportWorkspaceStatus status) =>
			status == ImportWorkspaceStatus.Closed || status == ImportWorkspaceStatus.Abandoned || status == ImportWorkspaceStatus.Cancelled;

		/// <summary>Datasets that still take part in the import.</summary>
		public static bool IsLive(ImportDataset dataset) =>
			dataset.Status != (int)ImportDatasetStatus.Excluded && dataset.Status != (int)ImportDatasetStatus.Superseded;

		public static ImportWorkspaceStatus Derive(ImportWorkspace workspace, IReadOnlyList<ImportDataset> datasets, IReadOnlyList<ImportRun> runs)
		{
			var current = (ImportWorkspaceStatus)workspace.Status;
			if (IsTerminal(current))
				return current;

			if (workspace.ActivatedOn.HasValue)
				return ImportWorkspaceStatus.Active;

			var open = runs.Where(r => OpenRunStatuses.Contains((ImportRunStatus)r.Status)).ToList();
			if (open.Any(r => r.Kind == (int)ImportRunKind.Commit || r.Kind == (int)ImportRunKind.Rollback))
				return open.Any(r => r.Status == (int)ImportRunStatus.Reconciling) ? ImportWorkspaceStatus.Verifying : ImportWorkspaceStatus.Importing;
			if (open.Any(r => r.Kind == (int)ImportRunKind.Reconcile))
				return ImportWorkspaceStatus.Verifying;

			var live = datasets.Where(IsLive).ToList();
			if (live.Count == 0)
				return datasets.Count > 0 || workspace.DomainPlan != null ? ImportWorkspaceStatus.Collecting : ImportWorkspaceStatus.Draft;

			if (live.All(d => d.Status == (int)ImportDatasetStatus.Verified))
				return ImportWorkspaceStatus.ReadyToActivate;

			if (live.All(d => d.Status == (int)ImportDatasetStatus.Imported || d.Status == (int)ImportDatasetStatus.Verified))
				return ImportWorkspaceStatus.Verifying;

			if (live.Any(d => d.Status == (int)ImportDatasetStatus.Importing))
				return ImportWorkspaceStatus.Importing;

			if (live.Any(d => d.Status >= (int)ImportDatasetStatus.NeedsMapping && d.Status <= (int)ImportDatasetStatus.Approved))
				return ImportWorkspaceStatus.Reviewing;

			return ImportWorkspaceStatus.Collecting;
		}

		/// <summary>A material change after approval (new file, mapping, value map or issue decision) returns the dataset to Ready.</summary>
		public static void InvalidateApproval(ImportDataset dataset)
		{
			if (dataset.Status == (int)ImportDatasetStatus.Approved)
				dataset.Status = (int)ImportDatasetStatus.Ready;
			dataset.ApprovedRunId = null;
		}

		/// <summary>Run-level retry backoff: 1, 4, 16 minutes … capped at 6 hours (the outbox schedule, section 11.4).</summary>
		public static System.TimeSpan Backoff(int attempts)
		{
			var minutes = System.Math.Pow(4, System.Math.Max(0, attempts - 1));
			return System.TimeSpan.FromMinutes(System.Math.Min(minutes, 360));
		}
	}
}
