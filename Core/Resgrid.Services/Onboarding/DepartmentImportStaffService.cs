using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Model;
using Resgrid.Model.Onboarding;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.Onboarding
{
	/// <summary>
	/// Resgrid staff operations over Import &amp; Migration (wizard plan section 17), for BackOffice's in-process calls.
	/// Rules held here, not in BackOffice: the staff role for each action; the active grant, its level, allowed actions and
	/// expiry for anything that sees department data or writes it; that file names and column headers appear only from
	/// Observe up; that ADP-protected values never appear; and two-person control for managed migration (the operator who
	/// requests a commit or rollback, or the SuperAdmin who requests a contract authorization, can never approve it).
	/// Every staff action is recorded on the department-visible workspace timeline as Resgrid staff.
	/// </summary>
	public class DepartmentImportStaffService : IDepartmentImportStaffService
	{
		private static readonly TimeSpan QueueWindow = TimeSpan.FromDays(30);

		/// <summary>The most rows one sample shows (section 17.2).</summary>
		public const int SampleRowsPerGroup = 20;

		private readonly IImportWorkspacesRepository _workspaces;
		private readonly IImportDatasetsRepository _datasets;
		private readonly IImportColumnMapsRepository _columnMaps;
		private readonly IImportIssuesRepository _issues;
		private readonly IImportRunsRepository _runs;
		private readonly IImportReconciliationsRepository _reconciliations;
		private readonly IImportSupportRequestsRepository _requests;
		private readonly IImportMessagesRepository _messages;
		private readonly IImportSuggestionsRepository _suggestions;
		private readonly IImportSupportGrantsRepository _grants;
		private readonly IImportArtifactsRepository _artifacts;
		private readonly IImportEventsRepository _events;
		private readonly IImportArtifactStore _store;
		private readonly IImportTimelineService _timeline;
		private readonly IDepartmentImportNotificationService _notifications;
		private readonly IImportRunQueue _queue;
		private readonly IImportApprovalService _approval;
		private readonly IImportPlanService _plan;
		private readonly IImportMappingService _mapping;
		private readonly IDepartmentImportAccessService _access;
		private readonly IDepartmentsService _departments;
		private readonly IImportUploadService _uploads;
		private readonly IDepartmentImportService _imports;
		private readonly IImportIssueService _issueService;
		private readonly IImportActivationService _activation;
		private readonly IImportArtifactCipher _cipher;
		private readonly IImportKeyService _keys;
		private readonly IDepartmentDataProtectionService _protection;

		private readonly IImportConfigurationChangesRepository _configurationChanges;
		private readonly IImportConfigurationService _configuration;

		public DepartmentImportStaffService(IImportWorkspacesRepository workspaces, IImportDatasetsRepository datasets, IImportColumnMapsRepository columnMaps,
			IImportIssuesRepository issues, IImportRunsRepository runs, IImportReconciliationsRepository reconciliations, IImportSupportRequestsRepository requests,
			IImportMessagesRepository messages, IImportSuggestionsRepository suggestions, IImportSupportGrantsRepository grants, IImportArtifactsRepository artifacts,
			IImportEventsRepository events, IImportArtifactStore store, IImportTimelineService timeline, IDepartmentImportNotificationService notifications,
			IImportRunQueue queue, IImportApprovalService approval, IImportPlanService plan, IImportMappingService mapping, IDepartmentImportAccessService access,
			IDepartmentsService departments, IImportUploadService uploads, IDepartmentImportService imports, IImportIssueService issueService,
			IImportActivationService activation, IImportArtifactCipher cipher, IImportKeyService keys, IDepartmentDataProtectionService protection,
			IImportConfigurationChangesRepository configurationChanges, IImportConfigurationService configuration)
		{
			_configurationChanges = configurationChanges;
			_configuration = configuration;
			_activation = activation;
			_cipher = cipher;
			_keys = keys;
			_protection = protection;
			_workspaces = workspaces;
			_datasets = datasets;
			_columnMaps = columnMaps;
			_issues = issues;
			_runs = runs;
			_reconciliations = reconciliations;
			_requests = requests;
			_messages = messages;
			_suggestions = suggestions;
			_grants = grants;
			_artifacts = artifacts;
			_events = events;
			_store = store;
			_timeline = timeline;
			_notifications = notifications;
			_queue = queue;
			_approval = approval;
			_plan = plan;
			_mapping = mapping;
			_access = access;
			_departments = departments;
			_uploads = uploads;
			_imports = imports;
			_issueService = issueService;
		}

		// ---- queue and workspace views ------------------------------------------------------------------------------------

		public async Task<List<ImportStaffQueueRow>> GetQueueAsync(StaffActor staff, int? departmentId, int skip, int take, CancellationToken cancellationToken = default)
		{
			RequireStaff(staff);
			var now = DateTime.UtcNow;
			var rows = new List<ImportStaffQueueRow>();
			foreach (var workspace in await _workspaces.GetQueueAsync(now.Subtract(QueueWindow), departmentId, Math.Max(0, skip), Math.Clamp(take, 1, 200), cancellationToken))
			{
				var runs = await _runs.GetAllForWorkspaceAsync(workspace.DepartmentId, workspace.Id, cancellationToken);
				var current = runs.Where(r => DepartmentImportStateMachine.OpenRunStatuses.Contains((ImportRunStatus)r.Status) || r.Status == (int)ImportRunStatus.Failed)
					.OrderByDescending(r => r.RequestedOn).FirstOrDefault();
				var datasets = (await _datasets.GetAllForWorkspaceAsync(workspace.DepartmentId, workspace.Id, cancellationToken)).Where(DepartmentImportStateMachine.IsLive).ToList();
				var grant = DepartmentImportSupportService.ActiveGrant(await _grants.GetForWorkspaceAsync(workspace.DepartmentId, workspace.Id, cancellationToken));
				var openRequests = (await _requests.GetForWorkspaceAsync(workspace.DepartmentId, workspace.Id, cancellationToken))
					.Count(r => r.Status != (int)ImportSupportRequestStatus.Resolved && r.Status != (int)ImportSupportRequestStatus.Closed);

				var row = new ImportStaffQueueRow
				{
					DepartmentId = workspace.DepartmentId,
					WorkspaceId = workspace.Id,
					Mode = workspace.Mode,
					Status = workspace.Status,
					OperatingModel = workspace.OperatingModel,
					CreatedOn = workspace.CreatedOn,
					LastActivityOn = workspace.LastActivityOn,
					GrantLevel = grant?.Level ?? 0,
					GrantExpiresOn = grant?.ExpiresOn,
					OpenRequests = openRequests,
					OpenBlockingIssues = await _issues.CountOpenBlockingAsync(workspace.DepartmentId, workspace.Id, null, cancellationToken),
					ReadyDatasets = datasets.Count(d => d.Status == (int)ImportDatasetStatus.Ready || d.Status == (int)ImportDatasetStatus.Approved),
					TotalDatasets = datasets.Count,
					CurrentRunId = current?.Id,
					CurrentRunStatus = current?.Status,
					LastHeartbeatOn = current?.HeartbeatOn,
					Attempts = current?.Attempts ?? 0,
					LastErrorCode = current?.LastErrorCode,
					NeedsEngineering = workspace.NeedsEngineering
				};
				row.Bucket = Bucket(row, current, now);
				rows.Add(row);
			}

			return rows.OrderBy(r => r.Bucket).ThenByDescending(r => r.LastActivityOn).ToList();
		}

		private static ImportStaffQueueBucket Bucket(ImportStaffQueueRow row, ImportRun current, DateTime now)
		{
			if (row.OpenRequests > 0) return ImportStaffQueueBucket.SupportRequestsOpen;
			if (row.NeedsEngineering || current?.Status == (int)ImportRunStatus.Paused && current.Attempts >= Config.ImportConfig.MaxRunAttempts)
				return ImportStaffQueueBucket.NeedsEngineering;
			if (current != null && (current.Status == (int)ImportRunStatus.Running || current.Status == (int)ImportRunStatus.Reconciling) &&
				(current.CursorMovedOn ?? current.UpdatedOn) < now.AddMinutes(-Math.Max(1, Config.ImportConfig.StalledRunMinutes)))
				return ImportStaffQueueBucket.Stalled;
			if (current?.Status == (int)ImportRunStatus.Failed) return ImportStaffQueueBucket.FailedRetrying;
			if (current?.Status == (int)ImportRunStatus.AwaitingChecker) return ImportStaffQueueBucket.AwaitingChecker;
			if (current != null && current.Status != (int)ImportRunStatus.Paused) return ImportStaffQueueBucket.Running;
			if (row.Status == (int)ImportWorkspaceStatus.Active || row.Status == (int)ImportWorkspaceStatus.Closed) return ImportStaffQueueBucket.RecentlyCompleted;
			return ImportStaffQueueBucket.AwaitingAdministrator;
		}

		public async Task<ImportStaffWorkspaceView> GetWorkspaceAsync(StaffActor staff, int departmentId, string workspaceId, CancellationToken cancellationToken = default)
		{
			RequireStaff(staff);
			var workspace = await RequireWorkspaceAsync(departmentId, workspaceId, cancellationToken);
			var grants = await _grants.GetForWorkspaceAsync(departmentId, workspaceId, cancellationToken);
			var grant = DepartmentImportSupportService.ActiveGrant(grants);
			var level = grant?.Level ?? (int)ImportSupportAccessLevel.None;
			var observe = level >= (int)ImportSupportAccessLevel.Observe;
			var view = new ImportStaffWorkspaceView
			{
				Workspace = Scrub(workspace, observe),
				ActiveGrant = grant,
				Level = level,
				Grants = grants.OrderByDescending(g => g.GrantedOn).ToList(),
				Runs = (await _runs.GetAllForWorkspaceAsync(departmentId, workspaceId, cancellationToken)).OrderByDescending(r => r.RequestedOn).ToList(),
				Requests = await _requests.GetForWorkspaceAsync(departmentId, workspaceId, cancellationToken),
				Messages = await _messages.GetPageAsync(departmentId, workspaceId, 0, 200, cancellationToken),
				Suggestions = Scrub(await _suggestions.GetAllForWorkspaceAsync(departmentId, workspaceId, cancellationToken), observe),
				Timeline = await _events.GetPageAsync(departmentId, workspaceId, 0, 200, cancellationToken)
			};

			foreach (var run in view.Runs.Where(r => r.Kind == (int)ImportRunKind.Commit || r.Kind == (int)ImportRunKind.Rollback))
				view.Reconciliations.AddRange(await _reconciliations.GetForRunAsync(departmentId, run.Id, cancellationToken));
			if (observe)
			{
				view.Continuation = await _imports.GetContinuationAsync(departmentId, workspaceId, cancellationToken);
				view.Products = (await _imports.GetSourcesAsync(departmentId, workspaceId, cancellationToken) ?? new List<ImportSource>())
					.Where(s => !string.Equals(s.ProductKey, ImportSourceProducts.Continuation, StringComparison.OrdinalIgnoreCase))
					.OrderBy(s => s.CreatedOn).Select(Product).ToList();
			}

			var maps = observe ? (await _columnMaps.GetForWorkspaceAsync(departmentId, workspaceId, cancellationToken)).ToDictionary(m => m.Id, StringComparer.Ordinal)
				: new Dictionary<string, ImportColumnMap>();
			foreach (var dataset in await _datasets.GetAllForWorkspaceAsync(departmentId, workspaceId, cancellationToken))
			{
				var row = new ImportStaffDatasetRow
				{
					Id = dataset.Id,
					Name = observe ? dataset.Name : null,
					EntityKey = dataset.EntityKey,
					ContractVersion = dataset.ContractVersion,
					Status = dataset.Status,
					RowCount = dataset.RowCount,
					ReadyCount = dataset.ReadyCount,
					WarningCount = dataset.WarningCount,
					BlockedCount = dataset.BlockedCount,
					CreatedCount = dataset.CreatedCount,
					FailedCount = dataset.FailedCount
				};
				if (observe && dataset.ColumnMapId != null && maps.TryGetValue(dataset.ColumnMapId, out var map))
				{
					var body = JsonConvert.DeserializeObject<ImportColumnMapBody>(map.Body) ?? new ImportColumnMapBody();
					row.Columns = body.Columns.Select(c => (c.SourceColumn, c.FieldKey)).ToList();
				}
				view.Datasets.Add(row);

				foreach (var group in (await _issues.GetForDatasetAsync(departmentId, dataset.Id, cancellationToken)).GroupBy(i => i.Code))
					view.Issues.Add(new ImportStaffIssueRow
					{
						DatasetId = dataset.Id,
						Code = group.Key,
						Severity = group.Max(i => i.Severity),
						Count = group.Count(),
						OpenCount = group.Count(i => i.Status == (int)ImportIssueStatus.Open),
						Rows = observe ? group.Where(i => i.RowNumber.HasValue).Select(i => i.RowNumber.Value).Distinct().OrderBy(n => n).Take(50).ToList() : new List<int>()
					});
			}

			foreach (var artifact in await _artifacts.GetAllForWorkspaceAsync(departmentId, workspaceId, cancellationToken))
				view.Artifacts.Add(new ImportStaffArtifactRow
				{
					Id = artifact.Id,
					FileName = observe ? artifact.OriginalName : null,
					SizeBytes = artifact.SizeBytes,
					ScanState = artifact.ScanState,
					UploadState = artifact.UploadState,
					RejectionCode = artifact.RejectionCode,
					CreatedOn = artifact.CreatedOn,
					RetentionDeadline = artifact.RetentionDeadline,
					PurgedOn = artifact.PurgedOn
				});

			return view;
		}

		public async Task<ImportStaffDashboard> GetDashboardAsync(StaffActor staff, CancellationToken cancellationToken = default)
		{
			RequireStaff(staff);
			var now = DateTime.UtcNow;
			var requests = await _requests.GetQueueAsync(true, 0, 500, cancellationToken);
			var recent = await _runs.GetRecentAsync(now.Subtract(QueueWindow), 1000, cancellationToken);
			var stalled = await _runs.GetStalledAsync(now.AddMinutes(-Math.Max(1, Config.ImportConfig.StalledRunMinutes)), 500, cancellationToken);
			var workspaces = await _workspaces.GetQueueAsync(now.Subtract(QueueWindow), null, 0, 500, cancellationToken);

			return new ImportStaffDashboard
			{
				OpenSupportRequests = requests.Count,
				OldestOpenRequestOn = requests.Count == 0 ? null : requests.Min(r => r.OpenedOn),
				StalledRuns = stalled.Count,
				FailedRunsLast24Hours = recent.Count(r => r.Status == (int)ImportRunStatus.Failed && r.UpdatedOn >= now.AddHours(-24)),
				AwaitingChecker = recent.Count(r => r.Status == (int)ImportRunStatus.AwaitingChecker && Authority(r) is { IsStaff: true } a &&
					string.IsNullOrWhiteSpace(a.StaffChecker)),
				NeedsEngineering = workspaces.Count(w => w.NeedsEngineering)
			};
		}

		public async Task<int?> FindWorkspaceDepartmentAsync(StaffActor staff, string workspaceId, CancellationToken cancellationToken = default)
		{
			RequireStaff(staff);
			return (await _workspaces.FindByIdAnyDepartmentAsync(workspaceId, cancellationToken))?.DepartmentId;
		}

		/// <summary>Staff below Observe see workspace metadata, never its names or the administrator's own words.</summary>
		private static ImportWorkspace Scrub(ImportWorkspace workspace, bool observe)
		{
			if (observe)
				return workspace;
			var copy = JsonConvert.DeserializeObject<ImportWorkspace>(JsonConvert.SerializeObject(workspace));
			copy.Name = null;
			copy.InternalReference = null;
			copy.DomainPlan = null;
			return copy;
		}

		/// <summary>
		/// A suggestion's payload is a column map or value decisions, so it carries column headers: below Observe (a grant that
		/// expired or was revoked after the suggestion was made) staff keep its kind, status and note, not the payload.
		/// </summary>
		private static List<ImportSuggestion> Scrub(List<ImportSuggestion> suggestions, bool observe)
		{
			if (observe || suggestions == null)
				return suggestions;
			var copies = JsonConvert.DeserializeObject<List<ImportSuggestion>>(JsonConvert.SerializeObject(suggestions));
			foreach (var suggestion in copies)
				suggestion.Payload = null;
			return copies;
		}

		// ---- support requests and messages ----------------------------------------------------------------------------------

		public Task<List<ImportSupportRequest>> GetRequestQueueAsync(StaffActor staff, bool openOnly, int skip, int take, CancellationToken cancellationToken = default)
		{
			RequireStaff(staff);
			return _requests.GetQueueAsync(openOnly, Math.Max(0, skip), Math.Clamp(take, 1, 200), cancellationToken);
		}

		public async Task<ImportSupportRequest> AssignRequestAsync(StaffActor staff, int departmentId, string requestId, string assignee, CancellationToken cancellationToken = default)
		{
			RequireSupport(staff);
			var request = await _requests.GetByIdAsync(departmentId, requestId, cancellationToken) ?? throw new DepartmentImportException("import_request_not_found", 404);
			request.AssigneeIdentity = Clip(string.IsNullOrWhiteSpace(assignee) ? staff.Identity : assignee, 256);
			request.UpdatedOn = DateTime.UtcNow;
			await _requests.UpdateAsync(request, cancellationToken, true);
			return request;
		}

		public async Task<ImportSupportRequest> SetRequestStatusAsync(StaffActor staff, int departmentId, string requestId, ImportSupportRequestStatus status,
			CancellationToken cancellationToken = default)
		{
			RequireSupport(staff);
			if (status == ImportSupportRequestStatus.Open || status == ImportSupportRequestStatus.Closed)
				// Opening is the department's, and closing follows the department confirming a resolution.
				throw new DepartmentImportException("import_request_category_invalid");

			var request = await _requests.GetByIdAsync(departmentId, requestId, cancellationToken) ?? throw new DepartmentImportException("import_request_not_found", 404);
			var now = DateTime.UtcNow;
			request.Status = (int)status;
			request.FirstResponseOn ??= now;
			if (status == ImportSupportRequestStatus.Resolved)
				request.ResolvedOn = now;
			request.UpdatedOn = now;
			await _requests.UpdateAsync(request, cancellationToken, true);

			await StaffEventAsync(staff, departmentId, request.WorkspaceId, "SupportRequestStatusChanged", new { requestId, status = status.ToString() }, cancellationToken);
			await _notifications.NotifySupportActivityAsync(departmentId, request.WorkspaceId, "request:" + status, cancellationToken);
			return request;
		}

		public async Task<ImportMessage> PostMessageAsync(StaffActor staff, int departmentId, string workspaceId, string body, string requestId,
			CancellationToken cancellationToken = default)
		{
			RequireSupport(staff);
			await RequireWorkspaceAsync(departmentId, workspaceId, cancellationToken);
			if (string.IsNullOrWhiteSpace(body))
				throw new DepartmentImportException("import_message_empty");

			var now = DateTime.UtcNow;
			var request = string.IsNullOrWhiteSpace(requestId) ? null : await _requests.GetByIdAsync(departmentId, requestId, cancellationToken);
			if (!string.IsNullOrWhiteSpace(requestId) && (request == null || !string.Equals(request.WorkspaceId, workspaceId, StringComparison.OrdinalIgnoreCase)))
				throw new DepartmentImportException("import_request_not_found", 404);

			var message = new ImportMessage
			{
				DepartmentId = departmentId,
				WorkspaceId = workspaceId,
				SupportRequestId = request?.Id,
				AuthorKind = (int)ImportActorKind.ResgridStaff,
				AuthorIdentity = Clip(staff.Identity, 256),
				// Plain text: Core encodes it when it renders (no Markdown, no HTML).
				Body = Clip(body.Trim(), 4000),
				CreatedOn = now,
				ReadByStaffOn = now,
				// A reply on a request with a helpdesk ticket (or one being created) is mirrored there as a note (section 17.5).
				HelpdeskState = request != null && (request.HelpdeskState == (int)ImportHelpdeskState.Pending || request.HelpdeskState == (int)ImportHelpdeskState.Created)
					? (int)ImportHelpdeskState.Pending
					: (int)ImportHelpdeskState.NotConfigured
			};
			await _messages.InsertAsync(message, cancellationToken, true);

			if (request != null && request.FirstResponseOn == null)
			{
				request.FirstResponseOn = now;
				request.UpdatedOn = now;
				await _requests.UpdateAsync(request, cancellationToken, true);
			}

			await _notifications.NotifySupportActivityAsync(departmentId, workspaceId, "message", cancellationToken);
			return message;
		}

		// ---- recovery actions ---------------------------------------------------------------------------------------------

		public async Task<ImportRun> RetryRunNowAsync(StaffActor staff, int departmentId, string runId, CancellationToken cancellationToken = default)
		{
			RequireSupport(staff);
			var run = await RequireRunAsync(departmentId, runId, cancellationToken);
			if (run.Status != (int)ImportRunStatus.Failed || !run.NextAttemptOn.HasValue)
				throw new DepartmentImportException("import_run_not_paused", 409);

			run.NextAttemptOn = DateTime.UtcNow;
			run.UpdatedOn = DateTime.UtcNow;
			await _runs.UpdateAsync(run, cancellationToken, true);
			await _queue.NudgeAsync(run);
			await StaffEventAsync(staff, departmentId, run.WorkspaceId, "RunRetried", new { runId }, cancellationToken);
			return run;
		}

		public async Task<ImportRun> ReleaseExpiredLeaseAsync(StaffActor staff, int departmentId, string runId, CancellationToken cancellationToken = default)
		{
			RequireSupport(staff);
			var run = await RequireRunAsync(departmentId, runId, cancellationToken);
			// The ADP force-release rule: a live lease is never taken away from a working worker.
			if (string.IsNullOrWhiteSpace(run.LeaseOwner) || !run.LeaseExpiresOn.HasValue || run.LeaseExpiresOn.Value > DateTime.UtcNow)
				throw new DepartmentImportException("import_run_in_progress", 409);

			var owner = run.LeaseOwner;
			run.LeaseOwner = null;
			run.LeaseExpiresOn = null;
			run.Status = run.Status == (int)ImportRunStatus.Reconciling ? run.Status : (int)ImportRunStatus.Queued;
			run.UpdatedOn = DateTime.UtcNow;
			await _runs.SaveLeasedAsync(run, owner, cancellationToken);
			await _queue.NudgeAsync(run);
			await StaffEventAsync(staff, departmentId, run.WorkspaceId, "LeaseReleased", new { runId }, cancellationToken);
			return run;
		}

		public async Task<ImportRun> PauseRunAsync(StaffActor staff, int departmentId, string runId, CancellationToken cancellationToken = default)
		{
			RequireSupport(staff);
			var run = await RequireRunAsync(departmentId, runId, cancellationToken);
			if (run.Status != (int)ImportRunStatus.Queued && run.Status != (int)ImportRunStatus.Running && run.Status != (int)ImportRunStatus.Reconciling)
				throw new DepartmentImportException("import_run_finished", 409);

			await _runs.RequestCancelAsync(departmentId, runId, ImportRunCancelRequest.Pause, DateTime.UtcNow, cancellationToken);
			await StaffEventAsync(staff, departmentId, run.WorkspaceId, "RunPauseRequested", new { runId }, cancellationToken);
			return await _runs.GetByIdAsync(departmentId, runId, cancellationToken);
		}

		public async Task<ImportRun> RequeueAnalysisAsync(StaffActor staff, int departmentId, string workspaceId, IReadOnlyCollection<string> datasetIds,
			CancellationToken cancellationToken = default)
		{
			RequireSupport(staff);
			await RequireWorkspaceAsync(departmentId, workspaceId, cancellationToken);
			var datasets = await _datasets.GetAllForWorkspaceAsync(departmentId, workspaceId, cancellationToken);
			var ids = (datasetIds?.Count > 0 ? datasets.Where(d => datasetIds.Contains(d.Id)) : datasets)
				.Where(d => d.Status != (int)ImportDatasetStatus.Importing).Select(d => d.Id).ToList();
			if (ids.Count == 0)
				throw new DepartmentImportException("import_dataset_locked", 409);

			// On the current engine and catalog: an approval given before is no longer valid (the plan re-runs with it).
			foreach (var dataset in datasets.Where(d => ids.Contains(d.Id) && d.Status == (int)ImportDatasetStatus.Approved))
			{
				DepartmentImportStateMachine.InvalidateApproval(dataset);
				dataset.UpdatedOn = DateTime.UtcNow;
				await _datasets.UpdateWithRevisionAsync(dataset, dataset.Revision, cancellationToken);
			}

			var run = await _mapping.RevalidateAsync(departmentId, workspaceId, ids, cancellationToken);
			await StaffEventAsync(staff, departmentId, workspaceId, "AnalysisRequeued", new { datasets = ids.Count }, cancellationToken);
			return run;
		}

		public async Task<ImportWorkspace> EscalateAsync(StaffActor staff, int departmentId, string workspaceId, string note, CancellationToken cancellationToken = default)
		{
			RequireSupport(staff);
			var workspace = await RequireWorkspaceAsync(departmentId, workspaceId, cancellationToken);
			workspace.NeedsEngineering = true;
			workspace.EngineeringNote = Clip(note, 2000);
			workspace.UpdatedOn = DateTime.UtcNow;
			await _workspaces.UpdateWithRevisionAsync(workspace, workspace.Revision, cancellationToken);
			await StaffEventAsync(staff, departmentId, workspaceId, "Escalated", null, cancellationToken);
			return workspace;
		}

		public async Task<int> ExtendRetentionAsync(StaffActor staff, int departmentId, string workspaceId, int days, CancellationToken cancellationToken = default)
		{
			RequireSupport(staff);
			var workspace = await RequireWorkspaceAsync(departmentId, workspaceId, cancellationToken);
			days = Math.Clamp(days, 1, 30);
			var extended = 0;
			foreach (var artifact in (await _artifacts.GetAllForWorkspaceAsync(departmentId, workspaceId, cancellationToken)).Where(a => !a.PurgedOn.HasValue))
			{
				artifact.RetentionDeadline = (artifact.RetentionDeadline ?? DateTime.UtcNow).AddDays(days);
				artifact.UpdatedOn = DateTime.UtcNow;
				await _artifacts.UpdateAsync(artifact, cancellationToken, true);
				extended++;
			}
			if (workspace.StagingRetentionUntil.HasValue)
			{
				workspace.StagingRetentionUntil = workspace.StagingRetentionUntil.Value.AddDays(days);
				await _workspaces.UpdateWithRevisionAsync(workspace, workspace.Revision, cancellationToken);
			}

			await StaffEventAsync(staff, departmentId, workspaceId, "RetentionExtended", new { days, artifacts = extended }, cancellationToken);
			return extended;
		}

		public async Task<int> PurgeArtifactsAsync(StaffActor staff, int departmentId, string workspaceId, CancellationToken cancellationToken = default)
		{
			RequireSupport(staff);
			await RequireWorkspaceAsync(departmentId, workspaceId, cancellationToken);
			var purged = 0;
			foreach (var artifact in (await _artifacts.GetAllForWorkspaceAsync(departmentId, workspaceId, cancellationToken)).Where(a => !a.PurgedOn.HasValue))
			{
				if (_store.IsConfigured)
				{
					if (artifact.UploadState == (int)ImportArtifactUploadState.InProgress && !string.IsNullOrWhiteSpace(artifact.MultipartUploadId))
						await _store.AbortUploadAsync(artifact.ObjectKey, artifact.MultipartUploadId, cancellationToken);
					else if (!string.IsNullOrWhiteSpace(artifact.ObjectKey))
						await _store.DeletePrefixAsync(artifact.ObjectKey, cancellationToken);
				}

				artifact.PurgedOn = DateTime.UtcNow;
				artifact.UploadState = (int)ImportArtifactUploadState.Purged;
				artifact.WrappedDataKey = null;
				artifact.UpdatedOn = DateTime.UtcNow;
				await _artifacts.UpdateAsync(artifact, cancellationToken, true);
				purged++;
			}

			await StaffEventAsync(staff, departmentId, workspaceId, "ArtifactsPurged", new { artifacts = purged }, cancellationToken);
			return purged;
		}

		// ---- suggestions ----------------------------------------------------------------------------------------------------

		public async Task<ImportSuggestion> SuggestAsync(StaffActor staff, int departmentId, string workspaceId, ImportSuggestionKind kind, string datasetId,
			string payloadJson, string targetRef, string note, CancellationToken cancellationToken = default)
		{
			RequireSupport(staff);
			await RequireWorkspaceAsync(departmentId, workspaceId, cancellationToken);
			var grant = await RequireGrantAsync(departmentId, workspaceId, kind == ImportSuggestionKind.IssueResolution ? ImportSupportAccessLevel.Inspect : ImportSupportAccessLevel.Observe,
				cancellationToken);

			var suggestion = new ImportSuggestion
			{
				DepartmentId = departmentId,
				WorkspaceId = workspaceId,
				DatasetId = datasetId,
				Kind = (int)kind,
				Payload = payloadJson,
				TargetRef = Clip(targetRef, 256),
				Note = Clip(note, 2000),
				Status = (int)ImportSuggestionStatus.Proposed,
				SuggestedBy = Clip(staff.Identity, 256),
				SuggestedOn = DateTime.UtcNow
			};
			await _suggestions.InsertAsync(suggestion, cancellationToken, true);
			await StaffEventAsync(staff, departmentId, workspaceId, "FixSuggested", new { kind = kind.ToString(), grantId = grant.Id }, cancellationToken);
			await _notifications.NotifySupportActivityAsync(departmentId, workspaceId, "suggestion", cancellationToken);
			return suggestion;
		}

		// ---- sampled values (Inspect) ----------------------------------------------------------------------------------------------

		public async Task<ImportStaffSampleView> GetIssueSamplesAsync(StaffActor staff, int departmentId, string workspaceId, string datasetId, string code,
			string reason, CancellationToken cancellationToken = default)
		{
			RequireSupport(staff);
			await RequireWorkspaceAsync(departmentId, workspaceId, cancellationToken);
			await RequireGrantAsync(departmentId, workspaceId, ImportSupportAccessLevel.Inspect, cancellationToken);
			if (string.IsNullOrWhiteSpace(reason))
				throw new DepartmentImportException("import_request_subject_required");
			if (string.IsNullOrWhiteSpace(code))
				throw new DepartmentImportException("import_value_action_invalid");
			var dataset = await RequireDatasetAsync(departmentId, workspaceId, datasetId, cancellationToken);

			var issues = (await _issueService.GetIssuesAsync(departmentId, workspaceId, dataset.Id, code, null, 0, 200, cancellationToken))
				.Where(i => !string.IsNullOrWhiteSpace(i.StagedRowId)).ToList();
			var mapping = await _mapping.GetMappingAsync(departmentId, dataset.Id, cancellationToken);
			var looksSensitive = (mapping.Profile?.Columns ?? new List<ImportColumnProfile>()).Where(c => c.LooksSensitive)
				.Select(c => c.Header).ToHashSet(StringComparer.OrdinalIgnoreCase);

			var view = new ImportStaffSampleView { DatasetId = dataset.Id, Code = code, IssueCount = issues.Count };
			foreach (var group in issues.GroupBy(i => i.StagedRowId, StringComparer.OrdinalIgnoreCase).Take(SampleRowsPerGroup))
			{
				var row = await _issueService.GetRowAsync(departmentId, group.Key, cancellationToken);
				if (row == null || !string.Equals(row.DatasetId, dataset.Id, StringComparison.OrdinalIgnoreCase))
					continue;

				var sample = new ImportStaffSampleRow { RowNumber = row.RowNumber, IssueColumn = group.Select(i => i.ColumnName).FirstOrDefault(c => !string.IsNullOrWhiteSpace(c)) };
				for (var i = 0; i < row.Headers.Count; i++)
				{
					var header = row.Headers[i];
					var field = FieldFor(mapping.Body, mapping.Contract, header);
					// Restricted and protected (ADP) values never; personal and sensitive-looking ones partially (section 17.2).
					var withheld = field?.Protected != null || field?.SensitivityLevel == ImportFieldSensitivity.Restricted;
					var masked = !withheld && (field?.SensitivityLevel == ImportFieldSensitivity.Personal || field == null && looksSensitive.Contains(header));
					var value = withheld || i >= row.Cells.Count ? null : row.Cells[i];
					sample.Cells.Add(new ImportStaffCell
					{
						Column = header,
						FieldKey = field?.Key,
						Value = masked ? Mask(value) : value,
						Withheld = withheld,
						Masked = masked && !string.IsNullOrEmpty(value)
					});
				}
				view.Rows.Add(sample);
			}

			// The department sees every staff value view, and why, and is told about it (sections 17.2 and 17.4).
			await StaffEventAsync(staff, departmentId, workspaceId, "StaffSampleViewed",
				new { datasetId = dataset.Id, code, rows = view.Rows.Count, reason = Clip(reason.Trim(), 200) }, cancellationToken);
			await _notifications.NotifySupportActivityAsync(departmentId, workspaceId, "sample-viewed", cancellationToken);
			return view;
		}

		// ---- raw file download (Prepare) ----------------------------------------------------------------------------------------

		public async Task<ImportStaffArtifactDownload> OpenArtifactAsync(StaffActor staff, int departmentId, string workspaceId, string artifactId, string reason,
			CancellationToken cancellationToken = default)
		{
			RequireSupport(staff);
			await RequireWorkspaceAsync(departmentId, workspaceId, cancellationToken);
			await RequireGrantAsync(departmentId, workspaceId, ImportSupportAccessLevel.Prepare, cancellationToken);
			if (string.IsNullOrWhiteSpace(reason))
				throw new DepartmentImportException("import_request_subject_required");

			// The raw file holds every value as written, protected ones included: never under Advanced Data Protection (section 17.4).
			if (await _protection.IsProtectionEnforcedAsync(departmentId) || await _protection.ShouldEncryptNewWritesAsync(departmentId))
				throw new DepartmentImportException("import_download_adp_protected", 403);

			var artifact = await _artifacts.GetByIdAsync(departmentId, artifactId, cancellationToken);
			if (artifact == null || !string.Equals(artifact.WorkspaceId, workspaceId, StringComparison.OrdinalIgnoreCase) ||
				artifact.Kind != (int)ImportArtifactKind.Upload || artifact.PurgedOn.HasValue || string.IsNullOrWhiteSpace(artifact.WrappedDataKey))
				throw new DepartmentImportException("import_artifact_not_found", 404);
			// Only a completed upload that passed (or did not need) its scan: staff never carry an infected file out.
			if (artifact.UploadState != (int)ImportArtifactUploadState.Completed ||
				artifact.ScanState != (int)ImportArtifactScanState.Clean && artifact.ScanState != (int)ImportArtifactScanState.NotRequired)
				throw new DepartmentImportException("import_download_not_clean", 409);
			if (!_store.IsConfigured)
				throw new DepartmentImportException("IMP-STORE-UNAVAILABLE", 503);

			var key = await _keys.UnwrapAsync(departmentId, artifact.WrappedDataKey);
			var content = await _cipher.OpenReadAsync(_store, artifact.ObjectKey, key, artifact.Id, cancellationToken);

			await StaffEventAsync(staff, departmentId, workspaceId, "StaffArtifactDownloaded",
				new { artifactId = artifact.Id, bytes = artifact.SizeBytes, reason = Clip(reason.Trim(), 200) }, cancellationToken);
			await _notifications.NotifySupportActivityAsync(departmentId, workspaceId, "artifact-downloaded", cancellationToken);

			return new ImportStaffArtifactDownload
			{
				Content = content,
				FileName = WatermarkedName(artifact.OriginalName, staff.Identity, DateTime.UtcNow),
				Length = artifact.SizeBytes
			};
		}

		/// <summary>"roster.csv" → "roster.resgrid-staff-jdoe-20261013T1530Z.csv": a copy on disk names who took it and when.</summary>
		internal static string WatermarkedName(string originalName, string identity, DateTime utcNow)
		{
			var name = string.IsNullOrWhiteSpace(originalName) ? "import" : System.IO.Path.GetFileName(originalName);
			var extension = System.IO.Path.GetExtension(name);
			var stem = System.IO.Path.GetFileNameWithoutExtension(name);
			string Safe(string value, int max) =>
				new string((value ?? string.Empty).Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '-').ToArray()).Trim('-') is { Length: > 0 } safe
					? safe.Length <= max ? safe : safe.Substring(0, max)
					: "file";
			var who = Safe((identity ?? string.Empty).Split('@')[0], 32);
			var ext = Safe(extension.TrimStart('.'), 10);
			return $"{Safe(stem, 80)}.resgrid-staff-{who}-{utcNow:yyyyMMdd'T'HHmm}Z{(string.IsNullOrEmpty(extension) ? string.Empty : "." + ext)}";
		}

		// ---- staff uploads ----------------------------------------------------------------------------------------------------

		public async Task<ImportUploadStatus> StartUploadAsync(StaffActor staff, int departmentId, string workspaceId, string fileName, long sizeBytes, string sha256,
			string contentType, CancellationToken cancellationToken = default)
		{
			RequireSupport(staff);
			await RequireGrantAsync(departmentId, workspaceId, ImportSupportAccessLevel.Prepare, cancellationToken);
			return await _uploads.StartAsync(departmentId, StaffIdentity(staff), workspaceId, fileName, sizeBytes, sha256, contentType, null, true, cancellationToken);
		}

		public async Task<ImportUploadStatus> AppendUploadAsync(StaffActor staff, int departmentId, string workspaceId, string artifactId, int partNumber, byte[] chunk,
			string chunkSha256, CancellationToken cancellationToken = default)
		{
			RequireSupport(staff);
			await RequireGrantAsync(departmentId, workspaceId, ImportSupportAccessLevel.Prepare, cancellationToken);
			await RequireStaffArtifactAsync(departmentId, workspaceId, artifactId, cancellationToken);
			return await _uploads.AppendAsync(departmentId, artifactId, partNumber, chunk, chunkSha256, cancellationToken);
		}

		public async Task<ImportUploadStatus> CompleteUploadAsync(StaffActor staff, int departmentId, string workspaceId, string artifactId,
			CancellationToken cancellationToken = default)
		{
			RequireSupport(staff);
			await RequireGrantAsync(departmentId, workspaceId, ImportSupportAccessLevel.Prepare, cancellationToken);
			await RequireStaffArtifactAsync(departmentId, workspaceId, artifactId, cancellationToken);
			var status = await _uploads.CompleteAsync(departmentId, StaffIdentity(staff), artifactId, cancellationToken);
			await StaffEventAsync(staff, departmentId, workspaceId, "DraftPrepared", new { artifactId }, cancellationToken);
			return status;
		}

		/// <summary>A staff upload continues only on an artifact staff started in the same workspace.</summary>
		private async Task RequireStaffArtifactAsync(int departmentId, string workspaceId, string artifactId, CancellationToken cancellationToken)
		{
			var artifact = await _artifacts.GetByIdAsync(departmentId, artifactId, cancellationToken);
			if (artifact == null || artifact.WorkspaceId != workspaceId || !artifact.StaffAuthored)
				throw new DepartmentImportException("import_artifact_not_found", 404);
		}

		private static string StaffIdentity(StaffActor staff) => "staff:" + staff.Identity;

		// ---- workbench: matching (Prepare) ----------------------------------------------------------------------------------

		public async Task<ImportMappingView> GetMappingAsync(StaffActor staff, int departmentId, string workspaceId, string datasetId,
			CancellationToken cancellationToken = default)
		{
			var grant = await RequirePrepareAsync(staff, departmentId, workspaceId, cancellationToken);
			await RequireDatasetAsync(departmentId, workspaceId, datasetId, cancellationToken);
			var view = await _mapping.GetMappingAsync(departmentId, datasetId, cancellationToken);
			// A new view: the masked profile never replaces the one the mapping service handed back.
			return new ImportMappingView
			{
				Dataset = view.Dataset,
				Contract = view.Contract,
				Map = view.Map,
				Body = view.Body,
				Profile = MaskProfile(view.Profile, view.Body, view.Contract, grant.Level >= (int)ImportSupportAccessLevel.Operate),
				Destinations = view.Destinations,
				Transforms = view.Transforms,
				DateFormats = view.DateFormats
			};
		}

		public async Task<ImportDataset> SetDestinationAsync(StaffActor staff, int departmentId, string workspaceId, string datasetId, string entityKey,
			int expectedRevision, CancellationToken cancellationToken = default)
		{
			await RequirePrepareAsync(staff, departmentId, workspaceId, cancellationToken);
			await RequireDatasetAsync(departmentId, workspaceId, datasetId, cancellationToken);
			return await _imports.SetDestinationAsync(departmentId, StaffIdentity(staff), ImportActorKind.ResgridStaff, datasetId, entityKey, expectedRevision,
				cancellationToken);
		}

		public async Task<ImportDataset> ExcludeDatasetAsync(StaffActor staff, int departmentId, string workspaceId, string datasetId, bool excluded,
			CancellationToken cancellationToken = default)
		{
			await RequirePrepareAsync(staff, departmentId, workspaceId, cancellationToken);
			await RequireDatasetAsync(departmentId, workspaceId, datasetId, cancellationToken);
			return await _imports.ExcludeDatasetAsync(departmentId, StaffIdentity(staff), ImportActorKind.ResgridStaff, datasetId, excluded, cancellationToken);
		}

		public async Task<ImportWorkspace> SetWriteModeAsync(StaffActor staff, int departmentId, string workspaceId, ImportWriteMode mode,
			CancellationToken cancellationToken = default)
		{
			await RequirePrepareAsync(staff, departmentId, workspaceId, cancellationToken);
			return await _imports.SetWriteModeAsync(departmentId, StaffIdentity(staff), ImportActorKind.ResgridStaff, workspaceId, mode, cancellationToken);
		}

		public async Task<List<ImportContinuationCandidate>> GetContinuationCandidatesAsync(StaffActor staff, int departmentId, string workspaceId,
			CancellationToken cancellationToken = default)
		{
			await RequirePrepareAsync(staff, departmentId, workspaceId, cancellationToken);
			return await _imports.GetContinuationCandidatesAsync(departmentId, workspaceId, cancellationToken);
		}

		public async Task<ImportSource> SetContinuationAsync(StaffActor staff, int departmentId, string workspaceId, string earlierWorkspaceId,
			CancellationToken cancellationToken = default)
		{
			await RequirePrepareAsync(staff, departmentId, workspaceId, cancellationToken);
			return await _imports.SetContinuationAsync(departmentId, StaffIdentity(staff), ImportActorKind.ResgridStaff, workspaceId, earlierWorkspaceId,
				cancellationToken);
		}

		public async Task<ImportStaffProductRow> AddProductAsync(StaffActor staff, int departmentId, string workspaceId, string productKey,
			CancellationToken cancellationToken = default)
		{
			await RequirePrepareAsync(staff, departmentId, workspaceId, cancellationToken);
			return Product(await _imports.AddSourceAsync(departmentId, StaffIdentity(staff), ImportActorKind.ResgridStaff, workspaceId, productKey, cancellationToken));
		}

		public async Task RemoveProductAsync(StaffActor staff, int departmentId, string workspaceId, string sourceId, CancellationToken cancellationToken = default)
		{
			await RequirePrepareAsync(staff, departmentId, workspaceId, cancellationToken);
			await _imports.RemoveSourceAsync(departmentId, StaffIdentity(staff), ImportActorKind.ResgridStaff, workspaceId, sourceId, cancellationToken);
		}

		private static ImportStaffProductRow Product(ImportSource source) =>
			new ImportStaffProductRow { Id = source.Id, ProductKey = source.ProductKey, Label = source.Label, CreatedOn = source.CreatedOn };

		public async Task<ImportColumnMap> SaveColumnMapAsync(StaffActor staff, int departmentId, string workspaceId, string datasetId, ImportColumnMapBody body,
			int expectedRevision, CancellationToken cancellationToken = default)
		{
			await RequirePrepareAsync(staff, departmentId, workspaceId, cancellationToken);
			await RequireDatasetAsync(departmentId, workspaceId, datasetId, cancellationToken);
			return await _mapping.SaveColumnMapAsync(departmentId, StaffIdentity(staff), ImportActorKind.ResgridStaff, datasetId, body, expectedRevision,
				cancellationToken);
		}

		public async Task<List<ImportColumnMap>> GetSavedMapsAsync(StaffActor staff, int departmentId, string workspaceId, string datasetId,
			CancellationToken cancellationToken = default)
		{
			await RequirePrepareAsync(staff, departmentId, workspaceId, cancellationToken);
			var dataset = await RequireDatasetAsync(departmentId, workspaceId, datasetId, cancellationToken);
			return string.IsNullOrWhiteSpace(dataset.EntityKey)
				? new List<ImportColumnMap>()
				: await _mapping.GetSavedMapsAsync(departmentId, dataset.EntityKey, cancellationToken);
		}

		public async Task<ImportColumnMap> ApplySavedMapAsync(StaffActor staff, int departmentId, string workspaceId, string datasetId, string mapId,
			CancellationToken cancellationToken = default)
		{
			await RequirePrepareAsync(staff, departmentId, workspaceId, cancellationToken);
			await RequireDatasetAsync(departmentId, workspaceId, datasetId, cancellationToken);
			return await _mapping.ApplySavedMapAsync(departmentId, StaffIdentity(staff), ImportActorKind.ResgridStaff, datasetId, mapId, cancellationToken);
		}

		public async Task<List<ImportValueCandidate>> GetValueCandidatesAsync(StaffActor staff, int departmentId, string workspaceId, string datasetId, string fieldKey,
			CancellationToken cancellationToken = default)
		{
			await RequirePrepareAsync(staff, departmentId, workspaceId, cancellationToken);
			await RequireDatasetAsync(departmentId, workspaceId, datasetId, cancellationToken);
			await RequireMatchableFieldAsync(departmentId, datasetId, fieldKey, cancellationToken);
			return await _mapping.GetValueCandidatesAsync(departmentId, datasetId, fieldKey, cancellationToken);
		}

		public async Task<int> SaveValueMapsAsync(StaffActor staff, int departmentId, string workspaceId, string datasetId, string fieldKey,
			IReadOnlyList<ImportValueDecision> decisions, CancellationToken cancellationToken = default)
		{
			await RequirePrepareAsync(staff, departmentId, workspaceId, cancellationToken);
			await RequireDatasetAsync(departmentId, workspaceId, datasetId, cancellationToken);
			await RequireMatchableFieldAsync(departmentId, datasetId, fieldKey, cancellationToken);
			return await _mapping.SaveValueMapsAsync(departmentId, StaffIdentity(staff), ImportActorKind.ResgridStaff, datasetId, fieldKey,
				decisions ?? Array.Empty<ImportValueDecision>(), cancellationToken);
		}

		public async Task<int> ApplyValueMapsAsync(StaffActor staff, int departmentId, string workspaceId, string sourceDatasetId, string fieldKey,
			IReadOnlyCollection<string> targetDatasetIds, CancellationToken cancellationToken = default)
		{
			await RequirePrepareAsync(staff, departmentId, workspaceId, cancellationToken);
			var source = await RequireDatasetAsync(departmentId, workspaceId, sourceDatasetId, cancellationToken);
			await RequireMatchableFieldAsync(departmentId, sourceDatasetId, fieldKey, cancellationToken);

			var decided = (await _mapping.GetValueCandidatesAsync(departmentId, sourceDatasetId, fieldKey, cancellationToken))
				.Where(c => c.Decision.HasValue && c.SourceValue != null)
				.GroupBy(c => c.SourceValue, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
			if (decided.Count == 0)
				return 0;

			var saved = 0;
			foreach (var targetId in (targetDatasetIds ?? Array.Empty<string>()).Where(id => !string.Equals(id, sourceDatasetId, StringComparison.OrdinalIgnoreCase))
				.Distinct(StringComparer.OrdinalIgnoreCase))
			{
				var target = await RequireDatasetAsync(departmentId, workspaceId, targetId, cancellationToken);
				// Only where the field means the same thing: the same destination.
				if (!string.Equals(target.EntityKey, source.EntityKey, StringComparison.OrdinalIgnoreCase))
					throw new DepartmentImportException("import_unknown_destination");

				var decisions = (await _mapping.GetValueCandidatesAsync(departmentId, targetId, fieldKey, cancellationToken))
					.Where(c => c.SourceValue != null && decided.ContainsKey(c.SourceValue))
					.Select(c => new ImportValueDecision
					{
						SourceValue = c.SourceValue,
						Action = decided[c.SourceValue].Decision.Value,
						TargetCode = decided[c.SourceValue].DecidedCode,
						Confidence = 100
					}).ToList();
				if (decisions.Count > 0)
					saved += await _mapping.SaveValueMapsAsync(departmentId, StaffIdentity(staff), ImportActorKind.ResgridStaff, targetId, fieldKey, decisions,
						cancellationToken);
			}

			return saved;
		}

		// ---- workbench: fixing (Operate) -------------------------------------------------------------------------------------

		public async Task<List<ImportIssueGroup>> GetIssueGroupsAsync(StaffActor staff, int departmentId, string workspaceId, string datasetId, bool openOnly,
			CancellationToken cancellationToken = default)
		{
			await RequireFixAsync(staff, departmentId, workspaceId, cancellationToken);
			if (!string.IsNullOrWhiteSpace(datasetId))
				await RequireDatasetAsync(departmentId, workspaceId, datasetId, cancellationToken);

			var groups = await _issueService.GetGroupsAsync(departmentId, workspaceId, string.IsNullOrWhiteSpace(datasetId) ? null : datasetId, openOnly, cancellationToken);
			var contracts = new Dictionary<string, ImportEntityContract>(StringComparer.OrdinalIgnoreCase);
			foreach (var group in groups)
				if (await IsProtectedAsync(departmentId, group.DatasetId, group.FieldKey, contracts, cancellationToken))
					group.Parameters = null;
			return groups;
		}

		public async Task<List<ImportIssue>> GetIssuesAsync(StaffActor staff, int departmentId, string workspaceId, string datasetId, string code, int skip, int take,
			CancellationToken cancellationToken = default)
		{
			await RequireFixAsync(staff, departmentId, workspaceId, cancellationToken);
			await RequireDatasetAsync(departmentId, workspaceId, datasetId, cancellationToken);

			var issues = await _issueService.GetIssuesAsync(departmentId, workspaceId, datasetId, code, null, Math.Max(0, skip), Math.Clamp(take, 1, 200), cancellationToken);
			var contracts = new Dictionary<string, ImportEntityContract>(StringComparer.OrdinalIgnoreCase);
			foreach (var issue in issues)
				if (await IsProtectedAsync(departmentId, issue.DatasetId, issue.FieldKey, contracts, cancellationToken))
					issue.Parameters = null;
			return issues;
		}

		public async Task<int> ResolveIssuesAsync(StaffActor staff, int departmentId, string workspaceId, ImportStaffIssueAction action, IReadOnlyCollection<string> issueIds,
			string value, CancellationToken cancellationToken = default)
		{
			await RequireFixAsync(staff, departmentId, workspaceId, cancellationToken);
			var ids = issueIds ?? Array.Empty<string>();
			var actor = StaffIdentity(staff);
			switch (action)
			{
				case ImportStaffIssueAction.Accept:
					return await _issueService.AcceptAsync(departmentId, actor, ImportActorKind.ResgridStaff, workspaceId, ids, cancellationToken);
				case ImportStaffIssueAction.ExcludeRows:
					return await _issueService.ExcludeRowsAsync(departmentId, actor, ImportActorKind.ResgridStaff, workspaceId, ids, cancellationToken);
				case ImportStaffIssueAction.Reopen:
					return await _issueService.ReopenAsync(departmentId, actor, ImportActorKind.ResgridStaff, workspaceId, ids, cancellationToken);
				case ImportStaffIssueAction.SetValue:
					// Staff never write a protected field's value, even blind.
					var wanted = new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
					var contracts = new Dictionary<string, ImportEntityContract>(StringComparer.OrdinalIgnoreCase);
					foreach (var issue in (await _issues.GetAllForWorkspaceAsync(departmentId, workspaceId, cancellationToken)).Where(i => wanted.Contains(i.Id)))
						if (await IsProtectedAsync(departmentId, issue.DatasetId, issue.FieldKey, contracts, cancellationToken))
							throw new DepartmentImportException("import_field_protected", 403);
					return await _issueService.SetValueForIssuesAsync(departmentId, actor, ImportActorKind.ResgridStaff, workspaceId, ids, value ?? string.Empty,
						cancellationToken);
				default:
					throw new DepartmentImportException("import_value_action_invalid");
			}
		}

		public async Task<ImportStaffRowView> RevealRowAsync(StaffActor staff, int departmentId, string workspaceId, string stagedRowId, string reason,
			CancellationToken cancellationToken = default)
		{
			await RequireFixAsync(staff, departmentId, workspaceId, cancellationToken);
			if (string.IsNullOrWhiteSpace(reason))
				throw new DepartmentImportException("import_request_subject_required");

			var row = await _issueService.GetRowAsync(departmentId, stagedRowId, cancellationToken) ?? throw new DepartmentImportException("import_row_not_found", 404);
			var dataset = await RequireDatasetAsync(departmentId, workspaceId, row.DatasetId, cancellationToken);
			var mapping = await _mapping.GetMappingAsync(departmentId, dataset.Id, cancellationToken);

			var view = new ImportStaffRowView
			{
				StagedRowId = row.StagedRowId,
				DatasetId = row.DatasetId,
				RowNumber = row.RowNumber,
				Excluded = row.Excluded,
				ValidationState = row.ValidationState
			};
			for (var i = 0; i < row.Headers.Count; i++)
			{
				var field = FieldFor(mapping.Body, mapping.Contract, row.Headers[i]);
				var withheld = field?.Protected != null;
				view.Cells.Add(new ImportStaffCell
				{
					Column = row.Headers[i],
					FieldKey = field?.Key,
					Value = withheld || i >= row.Cells.Count ? null : row.Cells[i],
					Withheld = withheld
				});
			}

			// The department sees every staff reveal, and why (section 17.2).
			await StaffEventAsync(staff, departmentId, workspaceId, "StaffValuesViewed",
				new { datasetId = dataset.Id, row = row.RowNumber, reason = Clip(reason.Trim(), 200) }, cancellationToken);
			return view;
		}

		public async Task EditValueAsync(StaffActor staff, int departmentId, string workspaceId, string stagedRowId, string column, string value,
			CancellationToken cancellationToken = default)
		{
			await RequireFixAsync(staff, departmentId, workspaceId, cancellationToken);
			var row = await _issueService.GetRowAsync(departmentId, stagedRowId, cancellationToken) ?? throw new DepartmentImportException("import_row_not_found", 404);
			var dataset = await RequireDatasetAsync(departmentId, workspaceId, row.DatasetId, cancellationToken);
			var mapping = await _mapping.GetMappingAsync(departmentId, dataset.Id, cancellationToken);
			if (FieldFor(mapping.Body, mapping.Contract, column)?.Protected != null)
				throw new DepartmentImportException("import_field_protected", 403);

			await _issueService.EditValueAsync(departmentId, StaffIdentity(staff), ImportActorKind.ResgridStaff, stagedRowId, column, value ?? string.Empty,
				cancellationToken);
		}

		// ---- workbench: configuration ---------------------------------------------------------------------------------------

		public async Task<ImportConfigurationView> GetConfigurationAsync(StaffActor staff, int departmentId, string workspaceId,
			CancellationToken cancellationToken = default)
		{
			RequireStaff(staff);
			await RequireWorkspaceAsync(departmentId, workspaceId, cancellationToken);
			var grant = await RequireGrantAsync(departmentId, workspaceId, ImportSupportAccessLevel.Observe, cancellationToken);
			// Observe shows no staged values (section 17.2): the map centre worked out from the imported stations waits for Inspect.
			return await _configuration.GetForStaffAsync(departmentId, workspaceId, grant.Level >= (int)ImportSupportAccessLevel.Inspect, cancellationToken);
		}

		public async Task<ImportConfigurationChange> DecideConfigurationAsync(StaffActor staff, int departmentId, string workspaceId,
			ImportConfigurationDecision decision, CancellationToken cancellationToken = default)
		{
			await RequirePrepareAsync(staff, departmentId, workspaceId, cancellationToken);
			return await _configuration.DecideAsync(departmentId, StaffIdentity(staff), ImportActorKind.ResgridStaff, workspaceId, decision, cancellationToken);
		}

		public async Task<int> AcceptConfigurationSuggestionsAsync(StaffActor staff, int departmentId, string workspaceId, string group,
			CancellationToken cancellationToken = default)
		{
			await RequirePrepareAsync(staff, departmentId, workspaceId, cancellationToken);
			return await _configuration.AcceptSuggestionsAsync(departmentId, StaffIdentity(staff), ImportActorKind.ResgridStaff, workspaceId, group, cancellationToken);
		}

		public async Task<bool> ResetConfigurationAsync(StaffActor staff, int departmentId, string workspaceId, string itemKey,
			CancellationToken cancellationToken = default)
		{
			await RequirePrepareAsync(staff, departmentId, workspaceId, cancellationToken);
			return await _configuration.ResetAsync(departmentId, StaffIdentity(staff), ImportActorKind.ResgridStaff, workspaceId, itemKey, cancellationToken);
		}

		public async Task<ImportConfigurationChange> UploadConfigurationLogoAsync(StaffActor staff, int departmentId, string workspaceId, string fileName,
			string contentType, byte[] data, CancellationToken cancellationToken = default)
		{
			await RequirePrepareAsync(staff, departmentId, workspaceId, cancellationToken);
			var logo = await _configuration.UploadLogoAsync(departmentId, StaffIdentity(staff), ImportActorKind.ResgridStaff, workspaceId, fileName, contentType, data,
				cancellationToken);
			return await _configuration.DecideAsync(departmentId, StaffIdentity(staff), ImportActorKind.ResgridStaff, workspaceId,
				new ImportConfigurationDecision { ItemKey = "branding.logo", Value = logo.Id }, cancellationToken);
		}

		// ---- workbench helpers ------------------------------------------------------------------------------------------------

		private async Task<ImportSupportGrant> RequirePrepareAsync(StaffActor staff, int departmentId, string workspaceId, CancellationToken cancellationToken)
		{
			RequireSupport(staff);
			await RequireWorkspaceAsync(departmentId, workspaceId, cancellationToken);
			return await RequireGrantAsync(departmentId, workspaceId, ImportSupportAccessLevel.Prepare, cancellationToken);
		}

		private async Task RequireFixAsync(StaffActor staff, int departmentId, string workspaceId, CancellationToken cancellationToken)
		{
			RequireOperator(staff);
			await RequireWorkspaceAsync(departmentId, workspaceId, cancellationToken);
			await RequireOperateAsync(departmentId, workspaceId, ImportOperateActions.Prepare, cancellationToken);
		}

		/// <summary>The data set, only when it belongs to this workspace (a grant never reaches another workspace).</summary>
		private async Task<ImportDataset> RequireDatasetAsync(int departmentId, string workspaceId, string datasetId, CancellationToken cancellationToken)
		{
			var dataset = string.IsNullOrWhiteSpace(datasetId) ? null : await _datasets.GetByIdAsync(departmentId, datasetId, cancellationToken);
			if (dataset == null || !string.Equals(dataset.WorkspaceId, workspaceId, StringComparison.OrdinalIgnoreCase))
				throw new DepartmentImportException("import_dataset_not_found", 404);
			return dataset;
		}

		/// <summary>Value matching shows source values: never for a protected or restricted field.</summary>
		private async Task RequireMatchableFieldAsync(int departmentId, string datasetId, string fieldKey, CancellationToken cancellationToken)
		{
			var field = (await _mapping.GetMappingAsync(departmentId, datasetId, cancellationToken)).Contract?.Field(fieldKey)
				?? throw new DepartmentImportException("import_unknown_field");
			if (field.Protected != null || field.SensitivityLevel == ImportFieldSensitivity.Restricted)
				throw new DepartmentImportException("import_field_protected", 403);
		}

		private async Task<bool> IsProtectedAsync(int departmentId, string datasetId, string fieldKey, Dictionary<string, ImportEntityContract> contracts,
			CancellationToken cancellationToken)
		{
			if (string.IsNullOrWhiteSpace(datasetId) || string.IsNullOrWhiteSpace(fieldKey))
				return false;
			if (!contracts.TryGetValue(datasetId, out var contract))
				contracts[datasetId] = contract = (await _mapping.GetMappingAsync(departmentId, datasetId, cancellationToken)).Contract;
			return contract?.Field(fieldKey)?.Protected != null;
		}

		/// <summary>
		/// The field a column's values are treated as. A column can feed several fields (a full name split into first and last),
		/// so the most sensitive one wins: protected, then restricted, then personal, then any.
		/// </summary>
		private static ImportFieldContract FieldFor(ImportColumnMapBody body, ImportEntityContract contract, string column) =>
			(body?.Columns ?? new List<ImportColumnBinding>())
			.Where(c => c.FieldKey != null && c.Kind != (int)ImportColumnBindingKind.Ignore && string.Equals(c.SourceColumn, column, StringComparison.OrdinalIgnoreCase))
			.Select(c => contract?.Field(c.FieldKey))
			.Where(f => f != null)
			.OrderByDescending(f => f.Protected != null)
			.ThenByDescending(f => (int)f.SensitivityLevel)
			.FirstOrDefault();

		/// <summary>
		/// Source examples as staff may see them: never for a protected field; masked for a restricted field or a column that
		/// looks sensitive at any level, and for a personal field below Operate.
		/// </summary>
		internal static ImportDatasetProfile MaskProfile(ImportDatasetProfile profile, ImportColumnMapBody body, ImportEntityContract contract, bool operate)
		{
			var copy = JsonConvert.DeserializeObject<ImportDatasetProfile>(JsonConvert.SerializeObject(profile ?? new ImportDatasetProfile()));
			foreach (var column in copy.Columns)
			{
				var field = FieldFor(body, contract, column.Header);
				if (field?.Protected != null)
					column.Examples = new List<string>();
				else if (column.LooksSensitive || field?.SensitivityLevel == ImportFieldSensitivity.Restricted ||
					!operate && field?.SensitivityLevel == ImportFieldSensitivity.Personal)
					column.Examples = (column.Examples ?? new List<string>()).Select(Mask).ToList();
			}
			return copy;
		}

		private static string Mask(string value) =>
			string.IsNullOrEmpty(value) ? value : value.Substring(0, 1) + new string('\u2022', Math.Clamp(value.Length - 1, 1, 6));

		// ---- managed migration ------------------------------------------------------------------------------------------------

		public async Task<ImportWorkspace> CreateManagedWorkspaceAsync(StaffActor staff, int departmentId, ImportWorkspaceMode mode, string name,
			string engagementReference, CancellationToken cancellationToken = default)
		{
			RequireOperator(staff);
			if (string.IsNullOrWhiteSpace(engagementReference))
				throw new DepartmentImportException("import_request_subject_required");
			if (!await _access.IsManagedMigrationEnabledAsync(departmentId))
				throw new DepartmentImportException("import_grant_level_invalid");

			var department = await _departments.GetDepartmentByIdAsync(departmentId, false) ?? throw new DepartmentImportException("import_department_missing", 404);
			var created = await _imports.CreateWorkspaceAsync(departmentId, department.ManagingUserId, mode, name, Clip(engagementReference.Trim(), 200), null,
				cancellationToken);

			var workspace = await RequireWorkspaceAsync(departmentId, created.Id, cancellationToken);
			workspace.OperatingModel = (int)ImportOperatingModel.ManagedMigration;
			workspace.UpdatedOn = DateTime.UtcNow;
			await _workspaces.UpdateWithRevisionAsync(workspace, workspace.Revision, cancellationToken);
			await StaffEventAsync(staff, departmentId, workspace.Id, "ManagedWorkspaceCreated", new { mode = mode.ToString() }, cancellationToken);
			return workspace;
		}

		public async Task<ImportPreview> GetManagedPreviewAsync(StaffActor staff, int departmentId, string workspaceId, CancellationToken cancellationToken = default)
		{
			RequireOperator(staff);
			await RequireGrantAsync(departmentId, workspaceId, ImportSupportAccessLevel.Operate, cancellationToken);
			return await _approval.GetPreviewAsync(departmentId, workspaceId, cancellationToken);
		}

		public async Task<ImportSupportGrant> RequestContractAuthorizationAsync(StaffActor staff, int departmentId, string workspaceId, string engagementReference,
			string reason, int days, ImportOperateActions allowedActions, CancellationToken cancellationToken = default)
		{
			if (staff == null || !staff.IsSuperAdmin)
				throw new DepartmentImportException("import_not_allowed", 403);
			if (string.IsNullOrWhiteSpace(engagementReference) || string.IsNullOrWhiteSpace(reason))
				throw new DepartmentImportException("import_request_subject_required");
			if (!await _access.IsManagedMigrationEnabledAsync(departmentId))
				throw new DepartmentImportException("import_grant_level_invalid");

			await RequireWorkspaceAsync(departmentId, workspaceId, cancellationToken);
			var department = await _departments.GetDepartmentByIdAsync(departmentId, false) ?? throw new DepartmentImportException("import_department_missing", 404);
			var now = DateTime.UtcNow;
			var existing = await _grants.GetForWorkspaceAsync(departmentId, workspaceId, cancellationToken);
			var grant = new ImportSupportGrant
			{
				DepartmentId = departmentId,
				WorkspaceId = workspaceId,
				Level = (int)ImportSupportAccessLevel.Operate,
				AllowedActions = (int)((allowedActions == ImportOperateActions.None ? ImportOperateActions.Default : allowedActions) | ImportOperateActions.Prepare),
				AuthorizationKind = (int)ImportGrantAuthorizationKind.Contract,
				Status = (int)ImportGrantStatus.PendingApproval,
				EngagementReference = Clip(engagementReference, 200),
				Reason = Clip(reason, 2000),
				RequestedByStaff = Clip(staff.Identity, 256),
				// The department's managing user is the authority writes are attributed to (section 17.8).
				OnBehalfOfUserId = department.ManagingUserId,
				RequireDepartmentSignOff = false,
				GrantedOn = now,
				ExpiresOn = now.AddDays(Math.Clamp(days <= 0 ? DepartmentImportConfig.MaxOperateGrantDays : days, 1, DepartmentImportConfig.MaxOperateGrantDays)),
				Version = (existing.Max(g => (int?)g.Version) ?? 0) + 1
			};
			await _grants.InsertAsync(grant, cancellationToken, true);
			await StaffEventAsync(staff, departmentId, workspaceId, "ContractAuthorizationRequested", new { grantId = grant.Id, grant.EngagementReference }, cancellationToken);
			return grant;
		}

		public async Task<ImportSupportGrant> ApproveContractAuthorizationAsync(StaffActor staff, int departmentId, string grantId, bool approve,
			CancellationToken cancellationToken = default)
		{
			if (staff == null || !staff.IsSuperAdmin)
				throw new DepartmentImportException("import_not_allowed", 403);

			var grant = await _grants.GetByIdAsync(departmentId, grantId, cancellationToken) ?? throw new DepartmentImportException("import_request_not_found", 404);
			if (grant.Status != (int)ImportGrantStatus.PendingApproval)
				throw new DepartmentImportException("import_suggestion_decided", 409);
			// Break-glass rule: a different SuperAdmin approves; self-approval is impossible.
			if (string.Equals(grant.RequestedByStaff, staff.Identity, StringComparison.OrdinalIgnoreCase))
				throw new DepartmentImportException("import_checker_must_differ", 409);

			var now = DateTime.UtcNow;
			if (approve)
			{
				// One active grant per workspace: the contract authorization replaces any department-granted access.
				foreach (var previous in (await _grants.GetForWorkspaceAsync(departmentId, grant.WorkspaceId, cancellationToken))
					.Where(g => g.Status == (int)ImportGrantStatus.Active))
				{
					previous.Status = (int)ImportGrantStatus.Revoked;
					previous.RevokedBy = "staff:" + staff.Identity;
					previous.RevokedOn = now;
					previous.Version++;
					await _grants.UpdateAsync(previous, cancellationToken, true);
				}
			}

			grant.Status = (int)(approve ? ImportGrantStatus.Active : ImportGrantStatus.Rejected);
			grant.ApprovedByStaff = Clip(staff.Identity, 256);
			grant.GrantedOn = approve ? now : grant.GrantedOn;
			grant.Version++;
			await _grants.UpdateAsync(grant, cancellationToken, true);

			if (approve)
			{
				var workspace = await RequireWorkspaceAsync(departmentId, grant.WorkspaceId, cancellationToken);
				workspace.SupportAccessLevel = grant.Level;
				workspace.SupportAccessExpiresOn = grant.ExpiresOn;
				workspace.OperatingModel = (int)ImportOperatingModel.ManagedMigration;
				workspace.UpdatedOn = now;
				await _workspaces.UpdateWithRevisionAsync(workspace, workspace.Revision, cancellationToken);
				// Every administrator hears about it immediately, with a way to revoke (section 17.8).
				await _notifications.NotifyAdministratorsAsync(departmentId, grant.WorkspaceId, "contract-authorization", cancellationToken);
			}

			await StaffEventAsync(staff, departmentId, grant.WorkspaceId, approve ? "ContractAuthorizationApproved" : "ContractAuthorizationRejected",
				new { grantId }, cancellationToken);
			return grant;
		}

		public Task<List<ImportSupportGrant>> GetPendingAuthorizationsAsync(StaffActor staff, int take, CancellationToken cancellationToken = default)
		{
			RequireStaff(staff);
			return _grants.GetPendingApprovalAsync(Math.Clamp(take, 1, 200), cancellationToken);
		}

		public async Task<List<ImportRun>> GetAwaitingCheckerAsync(StaffActor staff, int take, CancellationToken cancellationToken = default)
		{
			RequireStaff(staff);
			return (await _runs.GetRecentAsync(DateTime.UtcNow.Subtract(QueueWindow), 500, cancellationToken))
				.Where(r => r.Status == (int)ImportRunStatus.AwaitingChecker && Authority(r)?.IsStaff == true)
				.OrderBy(r => r.RequestedOn).Take(Math.Clamp(take, 1, 200)).ToList();
		}

		public async Task<ImportRun> RequestManagedCommitAsync(StaffActor staff, int departmentId, string workspaceId, ImportApprovalRequest request,
			CancellationToken cancellationToken = default)
		{
			RequireOperator(staff);
			var grant = await RequireOperateAsync(departmentId, workspaceId, ImportOperateActions.Commit, cancellationToken);
			var run = await _approval.RequestStaffCommitAsync(departmentId, StaffAuthority(staff, departmentId, grant), workspaceId, request, cancellationToken);
			await StaffEventAsync(staff, departmentId, workspaceId, "ManagedApprovalRequested", new { runId = run.Id, kind = "commit" }, cancellationToken);
			return run;
		}

		public async Task<ImportRun> RequestManagedRollbackAsync(StaffActor staff, int departmentId, string commitRunId, CancellationToken cancellationToken = default)
		{
			RequireOperator(staff);
			var commit = await RequireRunAsync(departmentId, commitRunId, cancellationToken);
			var grant = await RequireOperateAsync(departmentId, commit.WorkspaceId, ImportOperateActions.Rollback, cancellationToken);
			var preview = await _approval.GetRollbackPreviewAsync(departmentId, commitRunId, cancellationToken);
			if (!preview.CanRollback)
				throw new DepartmentImportException(preview.RefusalCode ?? "import_rollback_commit_not_completed", 409);

			var now = DateTime.UtcNow;
			var run = new ImportRun
			{
				DepartmentId = departmentId,
				WorkspaceId = commit.WorkspaceId,
				Kind = (int)ImportRunKind.Rollback,
				ParentRunId = commit.Id,
				DatasetIds = commit.DatasetIds,
				WriteMode = commit.WriteMode,
				// Waits for a different staff checker before the worker may take it.
				Status = (int)ImportRunStatus.AwaitingChecker,
				RequestedBy = "staff:" + staff.Identity,
				RequestedOn = now,
				ApprovedBy = "staff:" + staff.Identity,
				ApprovedOn = now,
				Authority = JsonConvert.SerializeObject(StaffAuthority(staff, departmentId, grant)),
				CorrelationId = Guid.NewGuid().ToString("N"),
				UpdatedOn = now
			};
			await _runs.InsertAsync(run, cancellationToken, true);
			await StaffEventAsync(staff, departmentId, commit.WorkspaceId, "ManagedApprovalRequested", new { runId = run.Id, kind = "rollback" }, cancellationToken);
			return run;
		}

		public async Task<ImportRun> ApproveManagedRunAsync(StaffActor staff, int departmentId, string runId, string reason, CancellationToken cancellationToken = default)
		{
			RequireOperator(staff);
			if (string.IsNullOrWhiteSpace(reason))
				throw new DepartmentImportException("import_request_subject_required");

			var run = await RequireRunAsync(departmentId, runId, cancellationToken);
			var authority = Authority(run);
			if (run.Status != (int)ImportRunStatus.AwaitingChecker || authority?.IsStaff != true || !string.IsNullOrWhiteSpace(authority.StaffChecker))
				throw new DepartmentImportException("import_run_not_awaiting_checker", 409);
			// Two people: the operator who asked can never approve.
			if (string.Equals(authority.StaffOperator, staff.Identity, StringComparison.OrdinalIgnoreCase))
				throw new DepartmentImportException("import_checker_must_differ", 409);

			var action = ActionOf(run);
			var grant = await RequireOperateAsync(departmentId, run.WorkspaceId, action, cancellationToken);
			if (grant.Id != authority.GrantId)
				throw new DepartmentImportException("IMP-APPROVER-NOT-AUTHORIZED", 409);

			// The staged state is what was approved (L9 at approval, again at commit start).
			if (run.Kind == (int)ImportRunKind.Commit)
			{
				var ids = JsonConvert.DeserializeObject<List<string>>(run.DatasetIds ?? "[]");
				var digest = ImportConfigurationService.CombineDigest(await _plan.ComputeApprovalDigestAsync(departmentId, ids, (ImportWriteMode)run.WriteMode, cancellationToken),
					await _configurationChanges.GetForRunAsync(departmentId, run.Id, cancellationToken));
				if (digest != run.ApprovalDigest)
					throw new DepartmentImportException("IMP-APPROVAL-STALE", 409);
			}

			authority.StaffChecker = staff.Identity;
			run.Authority = JsonConvert.SerializeObject(authority);
			run.SecondApprovedBy = "staff:" + staff.Identity;
			run.SecondApprovedOn = DateTime.UtcNow;
			run.UpdatedOn = DateTime.UtcNow;
			// With department sign-off required, the run waits for an administrator's approval in Core as the third signature.
			if (!grant.RequireDepartmentSignOff)
			{
				run.Status = (int)ImportRunStatus.Queued;
				// An activation or invitation run keeps the department's notice: the worker does not take it before then.
				var notBefore = run.Kind == (int)ImportRunKind.Activation ? ImportActivationService.ManagedRequest(run).NotBefore : null;
				run.NextAttemptOn = notBefore.HasValue && notBefore.Value > DateTime.UtcNow ? notBefore : null;
			}
			await _runs.UpdateAsync(run, cancellationToken, true);
			if (run.Status == (int)ImportRunStatus.Queued && !run.NextAttemptOn.HasValue)
				await _queue.NudgeAsync(run);

			await StaffEventAsync(staff, departmentId, run.WorkspaceId, "ManagedApprovalGranted", new { runId, kind = action.ToString() }, cancellationToken);
			await _notifications.NotifySupportActivityAsync(departmentId, run.WorkspaceId, grant.RequireDepartmentSignOff ? "signoff-needed" : "managed-run", cancellationToken);
			return run;
		}

		public async Task<ImportRun> RejectManagedRunAsync(StaffActor staff, int departmentId, string runId, string reason, CancellationToken cancellationToken = default)
		{
			RequireOperator(staff);
			if (string.IsNullOrWhiteSpace(reason))
				throw new DepartmentImportException("import_request_subject_required");

			var run = await RequireRunAsync(departmentId, runId, cancellationToken);
			var authority = Authority(run);
			var now = DateTime.UtcNow;
			var inNotice = run.Kind == (int)ImportRunKind.Activation && run.Status == (int)ImportRunStatus.Queued && run.NextAttemptOn.HasValue && run.NextAttemptOn.Value > now;
			if (!(run.Status == (int)ImportRunStatus.AwaitingChecker || inNotice) || authority?.IsStaff != true)
				throw new DepartmentImportException("import_run_not_awaiting_checker", 409);

			run.Status = (int)ImportRunStatus.Cancelled;
			run.CompletedOn = now;
			run.UpdatedOn = now;
			await _runs.UpdateAsync(run, cancellationToken, true);
			var withdrawn = string.Equals(authority.StaffOperator, staff.Identity, StringComparison.OrdinalIgnoreCase);
			await StaffEventAsync(staff, departmentId, run.WorkspaceId, withdrawn ? "ManagedApprovalWithdrawn" : "ManagedApprovalRejected", new { runId }, cancellationToken);
			return run;
		}

		// ---- managed activation and invitations ----------------------------------------------------------------------------------

		public async Task<ImportManagedActivationSnapshot> GetManagedActivationAsync(StaffActor staff, int departmentId, string workspaceId,
			CancellationToken cancellationToken = default)
		{
			RequireOperator(staff);
			await RequireWorkspaceAsync(departmentId, workspaceId, cancellationToken);
			var grant = await RequireOperateAsync(departmentId, workspaceId, ImportOperateActions.Prepare, cancellationToken);
			return new ImportManagedActivationSnapshot
			{
				Checklist = await _activation.GetChecklistAsync(departmentId, workspaceId, cancellationToken),
				Runs = await _activation.GetManagedRunsAsync(departmentId, workspaceId, cancellationToken),
				NoticeRequired = grant.AuthorizationKind == (int)ImportGrantAuthorizationKind.Contract,
				DepartmentSignOffRequired = grant.RequireDepartmentSignOff,
				AllowedActions = (ImportOperateActions)grant.AllowedActions
			};
		}

		public async Task<ImportRun> RequestManagedActivationAsync(StaffActor staff, int departmentId, string workspaceId, ImportManagedActivationRequest request,
			CancellationToken cancellationToken = default)
		{
			RequireOperator(staff);
			if (request == null || !Enum.IsDefined(typeof(ImportManagedActivationKind), request.Kind))
				throw new DepartmentImportException("import_value_action_invalid");

			var invitations = request.Kind == ImportManagedActivationKind.Invitations;
			await RequireWorkspaceAsync(departmentId, workspaceId, cancellationToken);
			var grant = await RequireOperateAsync(departmentId, workspaceId, invitations ? ImportOperateActions.SendInvitations : ImportOperateActions.Activate,
				cancellationToken);

			// One staff activation step at a time per workspace: the department reads one plan, not several overlapping ones.
			if ((await _runs.GetAllForWorkspaceAsync(departmentId, workspaceId, cancellationToken)).Any(r => r.Kind == (int)ImportRunKind.Activation &&
				(r.Status == (int)ImportRunStatus.AwaitingChecker || r.Status == (int)ImportRunStatus.Queued || r.Status == (int)ImportRunStatus.Running)))
				throw new DepartmentImportException("import_run_open", 409);

			var checklist = await _activation.GetChecklistAsync(departmentId, workspaceId, cancellationToken);
			var clean = new ImportManagedActivationRequest { Kind = request.Kind };
			if (invitations)
			{
				var dormant = checklist.Dormant.Select(p => p.UserId).ToHashSet(StringComparer.OrdinalIgnoreCase);
				var chosen = (request.Invitations?.UserIds ?? new List<string>()).Where(dormant.Contains).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
				if (request.Invitations?.UserIds?.Count > 0 && chosen.Count == 0)
					throw new DepartmentImportException("import_nothing_to_invite", 409);
				clean.Invitations = new ImportInvitationRequest
				{
					UserIds = chosen,
					IncludeExistingAccounts = request.Invitations?.IncludeExistingAccounts == true,
					SendAfter = request.Invitations?.SendAfter
				};
				clean.People = (chosen.Count > 0 ? chosen.Count : checklist.PeopleDormant) + (clean.Invitations.IncludeExistingAccounts ? checklist.ExistingAccountsToInvite : 0);
				if (clean.People == 0)
					throw new DepartmentImportException("import_nothing_to_invite", 409);
			}
			else
			{
				var pending = checklist.PendingAdministrators.Select(p => p.UserId).ToHashSet(StringComparer.OrdinalIgnoreCase);
				clean.AdministratorUserIds = (request.AdministratorUserIds ?? new List<string>()).Where(pending.Contains).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
				clean.SignOff = request.SignOff && !checklist.SignedOff;
				if (clean.AdministratorUserIds.Count == 0 && !clean.SignOff)
					throw new DepartmentImportException("import_nothing_to_activate", 409);
				if (clean.SignOff && !checklist.CanSignOff)
					throw new DepartmentImportException("import_signoff_not_ready", 409);
				clean.People = clean.AdministratorUserIds.Count;
			}

			var now = DateTime.UtcNow;
			// Under contract nobody in the department is paged by surprise: every administrator is told now, 24 hours ahead.
			if (grant.AuthorizationKind == (int)ImportGrantAuthorizationKind.Contract)
				clean.NotBefore = now.AddHours(DepartmentImportConfig.ManagedActivationNoticeHours);

			var run = new ImportRun
			{
				DepartmentId = departmentId,
				WorkspaceId = workspaceId,
				Kind = (int)ImportRunKind.Activation,
				DatasetIds = "[]",
				Status = (int)ImportRunStatus.AwaitingChecker,
				RequestedBy = StaffIdentity(staff),
				RequestedOn = now,
				ApprovedBy = StaffIdentity(staff),
				ApprovedOn = now,
				Authority = JsonConvert.SerializeObject(StaffAuthority(staff, departmentId, grant)),
				Request = JsonConvert.SerializeObject(clean),
				CorrelationId = Guid.NewGuid().ToString("N"),
				UpdatedOn = now
			};
			await _runs.InsertAsync(run, cancellationToken, true);

			await StaffEventAsync(staff, departmentId, workspaceId, "ManagedActivationRequested",
				new { runId = run.Id, kind = clean.Kind.ToString(), people = clean.People, notBefore = clean.NotBefore }, cancellationToken);
			await _notifications.NotifyAdministratorsAsync(departmentId, workspaceId,
				$"{(invitations ? "managed-invitations" : "managed-activation")};{clean.People};{clean.NotBefore?.ToString("o", System.Globalization.CultureInfo.InvariantCulture) ?? "-"}",
				cancellationToken);
			return run;
		}

		private static ImportOperateActions ActionOf(ImportRun run) => (ImportRunKind)run.Kind switch
		{
			ImportRunKind.Rollback => ImportOperateActions.Rollback,
			ImportRunKind.Activation => ImportActivationService.ManagedRequest(run).Kind == ImportManagedActivationKind.Invitations
				? ImportOperateActions.SendInvitations
				: ImportOperateActions.Activate,
			_ => ImportOperateActions.Commit
		};

		// ---- helpers --------------------------------------------------------------------------------------------------------

		private static ImportAuthority StaffAuthority(StaffActor staff, int departmentId, ImportSupportGrant grant) => new ImportAuthority
		{
			DepartmentId = departmentId,
			Kind = (int)ImportActorKind.ResgridStaff,
			OnBehalfOfUserId = grant.OnBehalfOfUserId,
			StaffOperator = staff.Identity,
			GrantId = grant.Id,
			EngagementReference = grant.EngagementReference
		};

		private static ImportAuthority Authority(ImportRun run) =>
			string.IsNullOrWhiteSpace(run.Authority) ? null : JsonConvert.DeserializeObject<ImportAuthority>(run.Authority);

		private async Task<ImportSupportGrant> RequireGrantAsync(int departmentId, string workspaceId, ImportSupportAccessLevel minimum, CancellationToken cancellationToken)
		{
			var grant = DepartmentImportSupportService.ActiveGrant(await _grants.GetForWorkspaceAsync(departmentId, workspaceId, cancellationToken));
			if (grant == null || grant.Level < (int)minimum)
				throw new DepartmentImportException("import_grant_level_invalid", 403);
			return grant;
		}

		private async Task<ImportSupportGrant> RequireOperateAsync(int departmentId, string workspaceId, ImportOperateActions action, CancellationToken cancellationToken)
		{
			var grant = await RequireGrantAsync(departmentId, workspaceId, ImportSupportAccessLevel.Operate, cancellationToken);
			if (!((ImportOperateActions)grant.AllowedActions).HasFlag(action))
				throw new DepartmentImportException("import_grant_level_invalid", 403);
			return grant;
		}

		private async Task StaffEventAsync(StaffActor staff, int departmentId, string workspaceId, string action, object details, CancellationToken cancellationToken) =>
			await _timeline.RecordAsync(departmentId, workspaceId, ImportActorKind.ResgridStaff, staff.Identity, action, details, null, cancellationToken);

		private async Task<ImportWorkspace> RequireWorkspaceAsync(int departmentId, string workspaceId, CancellationToken cancellationToken) =>
			await _workspaces.GetByIdAsync(departmentId, workspaceId, cancellationToken) ?? throw new DepartmentImportException("import_workspace_not_found", 404);

		private async Task<ImportRun> RequireRunAsync(int departmentId, string runId, CancellationToken cancellationToken) =>
			await _runs.GetByIdAsync(departmentId, runId, cancellationToken) ?? throw new DepartmentImportException("import_run_not_found", 404);

		private static void RequireStaff(StaffActor staff)
		{
			if (staff == null || string.IsNullOrWhiteSpace(staff.Identity) || string.IsNullOrWhiteSpace(staff.Role))
				throw new DepartmentImportException("import_not_allowed", 403);
		}

		private static void RequireSupport(StaffActor staff)
		{
			RequireStaff(staff);
			if (!staff.CanSupport)
				throw new DepartmentImportException("import_not_allowed", 403);
		}

		private static void RequireOperator(StaffActor staff)
		{
			RequireStaff(staff);
			if (!staff.CanOperate)
				throw new DepartmentImportException("import_not_allowed", 403);
		}

		private static string Clip(string value, int length) =>
			string.IsNullOrEmpty(value) || value.Length <= length ? value : value.Substring(0, length);
	}
}
