using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Onboarding;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.AdminAssist
{
	/// <summary>
	/// Import &amp; Migration evidence (wizard plan section 20): counts, booleans and ages only — never a name, file or value.
	/// A department that never used imports reads as zero everywhere, so the import rules simply pass.
	/// </summary>
	public sealed class DepartmentImportEvidenceSource(IImportWorkspacesRepository workspaces, IImportDatasetsRepository datasets, IImportIssuesRepository issues,
		IImportRunsRepository runs, IImportReconciliationsRepository reconciliations, IImportArtifactsRepository artifacts, IImportExternalIdsRepository externalIds,
		IDepartmentsService departments) : IAdminAssistEvidenceSource
	{
		/// <summary>Blocking issues count once they are this old: a fresh issue is work in progress, an old one means the import is stuck.</summary>
		public const int StaleIssueDays = 7;

		public string SourceId => "DepartmentImport";

		public IReadOnlyList<string> EvidenceIds { get; } = new[]
		{
			"importWorkspaceOpen", "importWorkspaceVerified", "importBlockingIssueCount", "importUnreconciledRunCount", "importDormantMemberCount",
			"importOldestDormantDays", "importPausedIntakeCount", "importDisabledScheduleCount", "importRelaxedEnforcementCount", "importArtifactsRetainedCount",
			"importStalledRunCount"
		};

		public async Task<IReadOnlyList<ConfigurationEvidence>> ReadAsync(AdminAssistActor actor, DateTime now, CancellationToken ct)
		{
			const string version = "department-import-v1";
			var all = await workspaces.GetAllForDepartmentAsync(actor.DepartmentId, ct) ?? throw new InvalidOperationException();
			if (all.Any(w => w.DepartmentId != actor.DepartmentId))
				throw new InvalidOperationException();

			bool Terminal(ImportWorkspace w) => w.Status == (int)ImportWorkspaceStatus.Closed || w.Status == (int)ImportWorkspaceStatus.Cancelled ||
				w.Status == (int)ImportWorkspaceStatus.Abandoned;
			var open = all.Where(w => !Terminal(w) && w.Status != (int)ImportWorkspaceStatus.Active).ToList();
			var verified = 0;
			var staleBlocking = 0;
			var unreconciled = 0;
			var retained = 0;
			var stalled = 0;
			DateTime? oldestCommit = null;
			var createdPeople = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			foreach (var workspace in all)
			{
				var sets = await datasets.GetAllForWorkspaceAsync(actor.DepartmentId, workspace.Id, ct);
				var live = sets.Where(d => d.Status != (int)ImportDatasetStatus.Excluded && d.Status != (int)ImportDatasetStatus.Superseded).ToList();
				if (workspace.Status == (int)ImportWorkspaceStatus.Active || live.Count > 0 && live.All(d => d.Status == (int)ImportDatasetStatus.Verified))
					verified++;

				if (open.Contains(workspace))
					staleBlocking += (await issues.GetAllForWorkspaceAsync(actor.DepartmentId, workspace.Id, ct))
						.Count(i => i.Status == (int)ImportIssueStatus.Open && i.Severity == (int)ImportIssueSeverity.Blocking && i.CreatedOn <= now.AddDays(-StaleIssueDays));

				foreach (var run in await runs.GetAllForWorkspaceAsync(actor.DepartmentId, workspace.Id, ct))
				{
					if ((run.Status == (int)ImportRunStatus.Running || run.Status == (int)ImportRunStatus.Reconciling) &&
						(run.CursorMovedOn ?? run.UpdatedOn) < now.AddMinutes(-Math.Max(1, Config.ImportConfig.StalledRunMinutes)))
						stalled++;

					if (run.Kind != (int)ImportRunKind.Commit || run.Status != (int)ImportRunStatus.Completed)
						continue;

					if ((await reconciliations.GetForRunAsync(actor.DepartmentId, run.Id, ct)).Any(r => r.State == (int)ImportReconciliationState.Unexplained))
						unreconciled++;

					var completed = run.CompletedOn ?? run.UpdatedOn;
					foreach (var claim in (await externalIds.GetForRunAsync(actor.DepartmentId, run.Id, ct))
						.Where(e => e.EntityType == "personnel" && e.State == (int)ImportExternalIdState.Written && e.TargetKey != null))
					{
						if (createdPeople.Add(claim.TargetKey) && (oldestCommit == null || completed < oldestCommit))
							oldestCommit = completed;
					}
				}

				// Files kept longer than the default window after a successful import (an administrator extended retention).
				retained += (await artifacts.GetAllForWorkspaceAsync(actor.DepartmentId, workspace.Id, ct)).Count(a => !a.PurgedOn.HasValue &&
					a.CompletedOn.HasValue && a.RetentionDeadline.HasValue &&
					a.RetentionDeadline.Value > a.CompletedOn.Value.AddDays(DepartmentImportConfig.DefaultArtifactRetentionDaysAfterFailure));
			}

			var dormant = createdPeople.Count == 0 ? 0 : (await departments.GetAllMembersForDepartmentIncludingDeletedAsync(actor.DepartmentId) ?? new List<DepartmentMember>())
				.Count(m => !m.IsDeleted && createdPeople.Contains(m.UserId) && m.ImportActivationState == (int)ImportActivationState.Dormant);
			var oldestDormantDays = dormant == 0 || oldestCommit == null ? 0 : Math.Max(0, (int)(now - oldestCommit.Value).TotalDays);

			ConfigurationEvidence Count(string id, int value) => new(id, EvidenceState.Known, SourceId, version, now, Number: value);
			return new[]
			{
				Count("importWorkspaceOpen", open.Count),
				Count("importWorkspaceVerified", verified),
				Count("importBlockingIssueCount", staleBlocking),
				Count("importUnreconciledRunCount", unreconciled),
				Count("importDormantMemberCount", dormant),
				Count("importOldestDormantDays", oldestDormantDays),
				// Wave 1 creates no intake, schedules or relaxed enforcement; later waves count what they created paused or disabled.
				Count("importPausedIntakeCount", 0),
				Count("importDisabledScheduleCount", 0),
				Count("importRelaxedEnforcementCount", 0),
				Count("importArtifactsRetainedCount", retained),
				Count("importStalledRunCount", stalled)
			};
		}
	}
}
