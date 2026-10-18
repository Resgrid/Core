using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model.Onboarding;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.Onboarding
{
	/// <summary>
	/// Department Import maintenance (wizard plan sections 13.2 and 14.6). The watchdog runs every five minutes inside
	/// worker 47's tick; the hourly pass runs as worker 48. Each step is independent and idempotent: a failure is logged
	/// and the next step still runs, so one broken department never stops the sweep for the rest.
	/// </summary>
	public class DepartmentImportMaintenanceService : IDepartmentImportMaintenanceService
	{
		private const int Batch = 100;

		private readonly IImportRunService _runs;
		private readonly IImportRunsRepository _runRows;
		private readonly IImportArtifactsRepository _artifacts;
		private readonly IImportWorkspacesRepository _workspaces;
		private readonly IImportSourcesRepository _sources;
		private readonly IImportSupportGrantsRepository _grants;
		private readonly IImportStagedRowsRepository _stagedRows;
		private readonly IImportIssuesRepository _issues;
		private readonly IImportValueMapsRepository _valueMaps;
		private readonly IImportColumnMapsRepository _columnMaps;
		private readonly IImportArtifactStore _store;
		private readonly IImportUploadService _uploads;
		private readonly IImportRunQueue _runQueue;
		private readonly IImportTimelineService _timeline;
		private readonly IDepartmentImportNotificationService _notifications;
		private readonly IDepartmentImportSupportOutbox _supportOutbox;
		private readonly IDepartmentsService _departments;
		private readonly IImportEventsRepository _events;

		public DepartmentImportMaintenanceService(IImportRunService runs, IImportRunsRepository runRows, IImportArtifactsRepository artifacts,
			IImportWorkspacesRepository workspaces, IImportSourcesRepository sources, IImportSupportGrantsRepository grants, IImportStagedRowsRepository stagedRows,
			IImportIssuesRepository issues, IImportValueMapsRepository valueMaps, IImportColumnMapsRepository columnMaps, IImportArtifactStore store,
			IImportUploadService uploads, IImportRunQueue runQueue, IImportTimelineService timeline, IDepartmentImportNotificationService notifications,
			IDepartmentImportSupportOutbox supportOutbox, IDepartmentsService departments, IImportEventsRepository events)
		{
			_events = events;
			_departments = departments;
			_runs = runs;
			_runRows = runRows;
			_artifacts = artifacts;
			_workspaces = workspaces;
			_sources = sources;
			_grants = grants;
			_stagedRows = stagedRows;
			_issues = issues;
			_valueMaps = valueMaps;
			_columnMaps = columnMaps;
			_store = store;
			_uploads = uploads;
			_runQueue = runQueue;
			_timeline = timeline;
			_notifications = notifications;
			_supportOutbox = supportOutbox;
		}

		public async Task<string> RunWatchdogAsync(CancellationToken cancellationToken = default)
		{
			var stalled = await Step("stalled runs", () => _runs.FailStalledRunsAsync(cancellationToken));
			var scans = await Step("pending scans", () => RetryPendingScansAsync(cancellationToken));
			return $"Department import watchdog: {stalled} stalled run(s) failed for retry, {scans} scan(s) re-queued.";
		}

		public async Task<string> RunHourlyAsync(CancellationToken cancellationToken = default)
		{
			var summary = new List<string>
			{
				$"{await Step("abandoned uploads", () => _uploads.AbortStaleUploadsAsync(cancellationToken))} upload(s) aborted",
				$"{await Step("source credentials", () => ExpireCredentialsAsync(cancellationToken))} credential(s) deleted",
				$"{await Step("support grants", () => ExpireGrantsAsync(cancellationToken))} grant(s) expired",
				$"{await Step("artifact retention", () => PurgeArtifactsAsync(cancellationToken))} artifact(s) purged",
				$"{await Step("staging retention", () => PurgeStagingAsync(cancellationToken))} workspace(s) staging purged",
				$"{await Step("idle workspaces", () => RemindAndAbandonAsync(cancellationToken))} workspace(s) reminded or abandoned",
				$"{await Step("helpdesk outbox", () => _supportOutbox.ProcessDueAsync(cancellationToken))} helpdesk ticket(s) and note(s) sent",
				$"{await Step("staff view digest", () => SendStaffViewDigestsAsync(cancellationToken))} staff view digest(s) sent",
				$"{await Step("orphaned objects", () => PurgeOrphanedDepartmentsAsync(cancellationToken))} deleted department(s) cleared from the store"
			};

			return "Department import maintenance: " + string.Join(", ", summary) + ".";
		}

		private static async Task<int> Step(string name, Func<Task<int>> step)
		{
			try
			{
				return await step();
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Department import maintenance step '{name}' failed; the next pass retries it");
				return 0;
			}
		}

		/// <summary>An artifact whose scan engine was unavailable is re-queued every pass for ImportConfig.ScanRetryHours.</summary>
		/// <summary>
		/// The daily digest of staff value views (section 17.2): when Resgrid staff opened rows to fix issues, every administrator
		/// hears about it at most once a day per workspace, rather than once per view. Each view and its reason are already on the
		/// workspace timeline; the digest says how many and points there.
		/// </summary>
		public async Task<int> SendStaffViewDigestsAsync(CancellationToken cancellationToken)
		{
			var now = DateTime.UtcNow;
			var views = await _events.GetByActionSinceAsync(StaffValuesViewed, now.AddHours(-72), 5000, cancellationToken);
			var sent = 0;
			foreach (var group in views.GroupBy(e => (e.DepartmentId, e.WorkspaceId)))
			{
				var workspace = await _workspaces.GetByIdAsync(group.Key.DepartmentId, group.Key.WorkspaceId, cancellationToken);
				if (workspace == null || workspace.StaffViewDigestSentOn is { } last && last > now.AddHours(-24))
					continue;

				var since = workspace.StaffViewDigestSentOn;
				var pending = group.Where(e => !since.HasValue || e.CreatedOn > since.Value).ToList();
				if (pending.Count == 0)
					continue;

				workspace.StaffViewDigestSentOn = now;
				workspace.UpdatedOn = now;
				if (!await _workspaces.UpdateWithRevisionAsync(workspace, workspace.Revision, cancellationToken))
					continue;

				var staff = pending.Select(e => e.ActorIdentity).Where(a => !string.IsNullOrWhiteSpace(a)).Distinct(StringComparer.OrdinalIgnoreCase).Count();
				await _notifications.NotifyAdministratorsAsync(workspace.DepartmentId, workspace.Id, $"staff-views;{pending.Count};{staff}", cancellationToken);
				sent++;
			}

			return sent;
		}

		/// <summary>The timeline action a staff row reveal records (DepartmentImportStaffService.RevealRowAsync).</summary>
		public const string StaffValuesViewed = "StaffValuesViewed";

		private async Task<int> RetryPendingScansAsync(CancellationToken cancellationToken)
		{
			var pending = await _artifacts.GetPendingScansAsync(Batch, cancellationToken);
			var requeued = 0;
			foreach (var artifact in pending)
			{
				if (artifact.CompletedOn.HasValue && artifact.CompletedOn.Value < DateTime.UtcNow.AddHours(-ImportConfig.ScanRetryHours))
				{
					artifact.ScanState = (int)ImportArtifactScanState.Failed;
					artifact.RejectionCode = "IMP-FILE-SCAN-UNAVAILABLE";
					artifact.UpdatedOn = DateTime.UtcNow;
					await _artifacts.UpdateAsync(artifact, cancellationToken, true);
					continue;
				}

				var open = await _runRows.GetOpenForDepartmentAsync(artifact.DepartmentId, cancellationToken);
				if (open.Any(r => r.ArtifactId == artifact.Id))
					continue;

				var failed = (await _runRows.GetAllForWorkspaceAsync(artifact.DepartmentId, artifact.WorkspaceId, cancellationToken))
					.FirstOrDefault(r => r.ArtifactId == artifact.Id && r.Status == (int)ImportRunStatus.Failed);
				if (failed != null)
					continue; // its own backoff retries it

				await _runQueue.QueueAsync(new ImportRun
				{
					DepartmentId = artifact.DepartmentId,
					WorkspaceId = artifact.WorkspaceId,
					Kind = (int)ImportRunKind.Analysis,
					ArtifactId = artifact.Id,
					RequestedBy = "worker48"
				}, cancellationToken);
				requeued++;
			}

			return requeued;
		}

		/// <summary>Source API credentials are write-only and deleted after extraction or 24 hours (section 8.8).</summary>
		private async Task<int> ExpireCredentialsAsync(CancellationToken cancellationToken)
		{
			var expired = await _sources.GetWithExpiredCredentialsAsync(DateTime.UtcNow, Batch, cancellationToken);
			foreach (var source in expired)
				await _sources.ClearCredentialsAsync(source.DepartmentId, source.Id, cancellationToken);
			return expired.Count;
		}

		private async Task<int> ExpireGrantsAsync(CancellationToken cancellationToken)
		{
			var expired = await _grants.GetExpiredActiveAsync(DateTime.UtcNow, Batch, cancellationToken);
			foreach (var grant in expired)
			{
				grant.Status = (int)ImportGrantStatus.Expired;
				grant.Version++;
				await _grants.UpdateAsync(grant, cancellationToken, true);

				var workspace = await _workspaces.GetByIdAsync(grant.DepartmentId, grant.WorkspaceId, cancellationToken);
				if (workspace != null && workspace.SupportAccessExpiresOn <= DateTime.UtcNow)
				{
					workspace.SupportAccessLevel = (int)ImportSupportAccessLevel.None;
					workspace.SupportAccessExpiresOn = null;
					workspace.UpdatedOn = DateTime.UtcNow;
					await _workspaces.UpdateWithRevisionAsync(workspace, workspace.Revision, cancellationToken);
				}

				await _timeline.RecordAsync(grant.DepartmentId, grant.WorkspaceId, ImportActorKind.System, "worker48", "SupportAccessExpired",
					new { grantId = grant.Id, level = grant.Level }, cancellationToken: cancellationToken);
			}

			return expired.Count;
		}

		/// <summary>Deletes the stored objects and marks artifacts purged once their retention deadline passes.</summary>
		private async Task<int> PurgeArtifactsAsync(CancellationToken cancellationToken)
		{
			if (!_store.IsConfigured)
				return 0;

			var due = await _artifacts.GetPastRetentionAsync(DateTime.UtcNow, Batch, cancellationToken);
			foreach (var artifact in due)
			{
				if (artifact.UploadState == (int)ImportArtifactUploadState.InProgress && !string.IsNullOrWhiteSpace(artifact.MultipartUploadId))
					await _store.AbortUploadAsync(artifact.ObjectKey, artifact.MultipartUploadId, cancellationToken);
				else if (!string.IsNullOrWhiteSpace(artifact.ObjectKey))
					await _store.DeletePrefixAsync(artifact.ObjectKey, cancellationToken);

				artifact.PurgedOn = DateTime.UtcNow;
				artifact.UploadState = (int)ImportArtifactUploadState.Purged;
				artifact.WrappedDataKey = null;
				artifact.UpdatedOn = DateTime.UtcNow;
				await _artifacts.UpdateAsync(artifact, cancellationToken, true);
			}

			return due.Count;
		}

		/// <summary>Removes staged rows, issues and value maps past the staging retention; committed records stay (section 14.6).</summary>
		private async Task<int> PurgeStagingAsync(CancellationToken cancellationToken)
		{
			var due = await _workspaces.GetPastStagingRetentionAsync(DateTime.UtcNow, 20, cancellationToken);
			foreach (var workspace in due)
			{
				await _stagedRows.DeleteAllForWorkspaceAsync(workspace.DepartmentId, workspace.Id, cancellationToken);
				await _issues.DeleteAllForWorkspaceAsync(workspace.DepartmentId, workspace.Id, cancellationToken);
				await _valueMaps.DeleteAllForWorkspaceAsync(workspace.DepartmentId, workspace.Id, cancellationToken);
				await _columnMaps.DeleteUnsavedForWorkspaceAsync(workspace.DepartmentId, workspace.Id, cancellationToken);

				workspace.StagingRetentionUntil = null;
				if (workspace.Status == (int)ImportWorkspaceStatus.Active)
				{
					workspace.Status = (int)ImportWorkspaceStatus.Closed;
					workspace.ClosedOn = DateTime.UtcNow;
				}
				workspace.UpdatedOn = DateTime.UtcNow;
				await _workspaces.UpdateWithRevisionAsync(workspace, workspace.Revision, cancellationToken);
				await _timeline.RecordAsync(workspace.DepartmentId, workspace.Id, ImportActorKind.System, "worker48", "StagingPurged", null,
					cancellationToken: cancellationToken);
			}

			return due.Count;
		}

		/// <summary>Reminds the owner after ImportConfig.ReminderIdleDays and abandons after AbandonWorkspaceDays (section 13.1).</summary>
		private async Task<int> RemindAndAbandonAsync(CancellationToken cancellationToken)
		{
			var handled = 0;
			var idle = await _workspaces.GetIdleOpenWorkspacesAsync(DateTime.UtcNow.AddDays(-ImportConfig.ReminderIdleDays), Batch, cancellationToken);
			foreach (var workspace in idle)
			{
				if (workspace.LastActivityOn < DateTime.UtcNow.AddDays(-ImportConfig.AbandonWorkspaceDays))
				{
					workspace.Status = (int)ImportWorkspaceStatus.Abandoned;
					workspace.ClosedOn = DateTime.UtcNow;
					workspace.StagingRetentionUntil = DateTime.UtcNow;
					foreach (var artifact in await _artifacts.GetAllForWorkspaceAsync(workspace.DepartmentId, workspace.Id, cancellationToken))
					{
						if (artifact.PurgedOn.HasValue)
							continue;
						artifact.RetentionDeadline = DateTime.UtcNow;
						artifact.UpdatedOn = DateTime.UtcNow;
						await _artifacts.UpdateAsync(artifact, cancellationToken, true);
					}
					workspace.UpdatedOn = DateTime.UtcNow;
					await _workspaces.UpdateWithRevisionAsync(workspace, workspace.Revision, cancellationToken);
					await _timeline.RecordAsync(workspace.DepartmentId, workspace.Id, ImportActorKind.System, "worker48", "WorkspaceAbandoned", null,
						cancellationToken: cancellationToken);
					handled++;
				}
				else if (!workspace.AbandonReminderSentOn.HasValue)
				{
					workspace.AbandonReminderSentOn = DateTime.UtcNow;
					await _workspaces.UpdateWithRevisionAsync(workspace, workspace.Revision, cancellationToken);
					await _notifications.NotifyAbandonmentReminderAsync(workspace, cancellationToken);
					handled++;
				}
			}

			return handled;
		}

		/// <summary>
		/// The department purge deletes import rows inside the deletion transaction; the stored objects of a department that no
		/// longer has any workspace rows are removed here (conformance I5 for RustFS).
		/// </summary>
		private async Task<int> PurgeOrphanedDepartmentsAsync(CancellationToken cancellationToken)
		{
			if (!_store.IsConfigured)
				return 0;

			var cleared = 0;
			foreach (var departmentId in await _store.ListDepartmentIdsAsync(cancellationToken))
			{
				if ((await _workspaces.GetAllForDepartmentAsync(departmentId, cancellationToken)).Count > 0)
					continue;

				// Belt and braces: only a department that is gone (deletion removes the row) loses its objects, never one whose
				// workspace rows merely failed to load.
				if (await _departments.GetDepartmentByIdAsync(departmentId, true) != null)
					continue;

				await _store.DeletePrefixAsync(ImportArtifactKeys.ForDepartment(departmentId), cancellationToken);
				cleared++;
			}

			return cleared;
		}
	}
}
