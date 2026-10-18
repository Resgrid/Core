using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Model.Onboarding;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.Onboarding
{
	/// <summary>
	/// Workspace, source and dataset lifecycle for Department Import (wizard plan sections 6, 8.6 and 10.2). Every method
	/// loads by id AND department (never trusts a posted key, section 14.2); callers authorize first. The workspace status
	/// is derived from its datasets and runs; workspace edits are optimistic on Revision.
	/// </summary>
	public class DepartmentImportService : IDepartmentImportService
	{
		private readonly IImportWorkspacesRepository _workspaces;
		private readonly IImportSourcesRepository _sources;
		private readonly IImportArtifactsRepository _artifacts;
		private readonly IImportDatasetsRepository _datasets;
		private readonly IImportRunsRepository _runs;
		private readonly IImportIssuesRepository _issues;
		private readonly IImportMessagesRepository _messages;
		private readonly IImportSupportRequestsRepository _supportRequests;
		private readonly IImportColumnMapsRepository _columnMaps;
		private readonly IImportCatalog _catalog;
		private readonly IImportRunQueue _runQueue;
		private readonly IImportTimelineService _timeline;
		private readonly IDepartmentSettingsService _settings;
		private readonly IImportArtifactStore _store;
		private readonly IImportSupportGrantsRepository _grants;

		public DepartmentImportService(IImportWorkspacesRepository workspaces, IImportSourcesRepository sources, IImportArtifactsRepository artifacts,
			IImportDatasetsRepository datasets, IImportRunsRepository runs, IImportIssuesRepository issues, IImportMessagesRepository messages,
			IImportSupportRequestsRepository supportRequests, IImportColumnMapsRepository columnMaps, IImportCatalog catalog, IImportRunQueue runQueue,
			IImportTimelineService timeline, IDepartmentSettingsService settings, IImportArtifactStore store, IImportSupportGrantsRepository grants)
		{
			_grants = grants;
			_workspaces = workspaces;
			_sources = sources;
			_artifacts = artifacts;
			_datasets = datasets;
			_runs = runs;
			_issues = issues;
			_messages = messages;
			_supportRequests = supportRequests;
			_columnMaps = columnMaps;
			_catalog = catalog;
			_runQueue = runQueue;
			_timeline = timeline;
			_settings = settings;
			_store = store;
		}

		public Task<List<ImportWorkspace>> GetWorkspacesAsync(int departmentId, CancellationToken cancellationToken = default) =>
			_workspaces.GetAllForDepartmentAsync(departmentId, cancellationToken);

		public Task<ImportWorkspace> GetWorkspaceAsync(int departmentId, string workspaceId, CancellationToken cancellationToken = default) =>
			_workspaces.GetByIdAsync(departmentId, workspaceId, cancellationToken);

		public async Task<ImportWorkspace> CreateWorkspaceAsync(int departmentId, string userId, ImportWorkspaceMode mode, string name, string internalReference,
			DateTime? targetActivationDate, CancellationToken cancellationToken = default)
		{
			if (!Enum.IsDefined(typeof(ImportWorkspaceMode), mode) || mode == ImportWorkspaceMode.HistoricalArchive || mode == ImportWorkspaceMode.DepartmentPackage)
				throw new DepartmentImportException("import_mode_unavailable");

			var config = await _settings.GetDepartmentImportConfigAsync(departmentId);
			var now = DateTime.UtcNow;
			var workspace = new ImportWorkspace
			{
				DepartmentId = departmentId,
				Name = Clip(string.IsNullOrWhiteSpace(name) ? "Import " + now.ToString("yyyy-MM-dd") : name.Trim(), 200),
				InternalReference = Clip(internalReference?.Trim(), 200),
				Mode = (int)mode,
				Status = (int)ImportWorkspaceStatus.Draft,
				OperatingModel = (int)ImportOperatingModel.SelfService,
				OwnerUserId = userId,
				TargetActivationDate = targetActivationDate,
				CatalogVersion = _catalog.Version,
				DefaultWriteMode = (int)(mode == ImportWorkspaceMode.ExistingReviewedUpdate ? ImportWriteMode.ReviewedUpdate : config.EffectiveWriteMode()),
				RequireSecondApprover = config.RequireSecondApprover,
				CreatedBy = userId,
				CreatedOn = now,
				UpdatedOn = now,
				LastActivityOn = now
			};

			await _workspaces.InsertAsync(workspace, cancellationToken, true);
			await _timeline.RecordAsync(departmentId, workspace.Id, ImportActorKind.Administrator, userId, "WorkspaceCreated",
				new { mode = mode.ToString(), catalog = workspace.CatalogVersion }, cancellationToken: cancellationToken);

			// Concurrent workspaces are allowed; the hub warns when another one has a run in progress (step 1).
			return workspace;
		}

		public async Task<ImportWorkspace> UpdateWorkspaceAsync(int departmentId, string userId, string workspaceId, string name, string internalReference,
			DateTime? targetActivationDate, int expectedRevision, CancellationToken cancellationToken = default)
		{
			var workspace = await RequireOpenAsync(departmentId, workspaceId, cancellationToken);
			workspace.Name = Clip(string.IsNullOrWhiteSpace(name) ? workspace.Name : name.Trim(), 200);
			workspace.InternalReference = Clip(internalReference?.Trim(), 200);
			workspace.TargetActivationDate = targetActivationDate;
			return await SaveAsync(workspace, expectedRevision, userId, "WorkspaceUpdated", null, cancellationToken);
		}

		public async Task<ImportWorkspace> SaveDomainPlanAsync(int departmentId, string userId, string workspaceId, ImportDomainPlan plan, int expectedRevision,
			CancellationToken cancellationToken = default)
		{
			var workspace = await RequireOpenAsync(departmentId, workspaceId, cancellationToken);
			var catalog = _catalog.GetVersion(workspace.CatalogVersion) ?? _catalog;
			plan ??= new ImportDomainPlan();

			// Only known contracts; a domain claimed by two products needs an owner (step 3 blocking check).
			plan.Domains = plan.Domains.Where(d => catalog.GetContract(d.EntityKey) != null)
				.GroupBy(d => d.EntityKey, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
			foreach (var domain in plan.Domains)
			{
				domain.ProductKeys = domain.ProductKeys.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).Take(5).ToList();
				if (domain.ProductKeys.Count > 1 && string.IsNullOrWhiteSpace(domain.OwnerProductKey))
					throw new DepartmentImportException("import_domain_owner_required");
			}

			workspace.DomainPlan = JsonConvert.SerializeObject(plan);
			return await SaveAsync(workspace, expectedRevision, userId, "DomainPlanSaved", new { domains = plan.Domains.Count }, cancellationToken);
		}

		public async Task<ImportWorkspace> CancelWorkspaceAsync(int departmentId, string userId, string workspaceId, CancellationToken cancellationToken = default)
		{
			var workspace = await RequireOpenAsync(departmentId, workspaceId, cancellationToken);
			var open = await _runs.GetOpenForDepartmentAsync(departmentId, cancellationToken);
			if (open.Any(r => r.WorkspaceId == workspaceId && (r.Kind == (int)ImportRunKind.Commit || r.Kind == (int)ImportRunKind.Rollback) &&
				r.Status != (int)ImportRunStatus.Queued && r.Status != (int)ImportRunStatus.AwaitingChecker))
				throw new DepartmentImportException("import_run_in_progress", 409);

			foreach (var run in open.Where(r => r.WorkspaceId == workspaceId))
				await _runs.RequestCancelAsync(departmentId, run.Id, ImportRunCancelRequest.Cancel, DateTime.UtcNow, cancellationToken);

			workspace.Status = (int)ImportWorkspaceStatus.Cancelled;
			workspace.CancelledOn = DateTime.UtcNow;
			// Files of a cancelled workspace are kept only for the failure window (section 14.6).
			var config = await _settings.GetDepartmentImportConfigAsync(departmentId);
			foreach (var artifact in await _artifacts.GetAllForWorkspaceAsync(departmentId, workspaceId, cancellationToken))
			{
				if (artifact.PurgedOn.HasValue)
					continue;
				artifact.RetentionDeadline = DateTime.UtcNow.Add(config.ArtifactRetentionAfterFailure());
				artifact.UpdatedOn = DateTime.UtcNow;
				await _artifacts.UpdateAsync(artifact, cancellationToken, true);
			}
			workspace.StagingRetentionUntil = DateTime.UtcNow.Add(config.ArtifactRetentionAfterFailure());

			return await SaveAsync(workspace, workspace.Revision, userId, "WorkspaceCancelled", null, cancellationToken);
		}

		public async Task<ImportSource> AddSourceAsync(int departmentId, string actor, ImportActorKind actorKind, string workspaceId, string productKey,
			CancellationToken cancellationToken = default)
		{
			var workspace = await RequireOpenAsync(departmentId, workspaceId, cancellationToken);
			var key = productKey?.Trim().ToLowerInvariant();
			var catalog = _catalog.GetVersion(workspace.CatalogVersion) ?? _catalog;
			var pack = catalog.GetPack(key);
			if (pack == null || ImportGlossary.ReservedKeys.Contains(key, StringComparer.OrdinalIgnoreCase))
				throw new DepartmentImportException("import_source_unknown", 404);
			if ((await _sources.GetAllForWorkspaceAsync(departmentId, workspaceId, cancellationToken)).Any(s => string.Equals(s.ProductKey, pack.Key, StringComparison.OrdinalIgnoreCase)))
				throw new DepartmentImportException("import_source_exists", 409);

			var source = new ImportSource
			{
				DepartmentId = departmentId,
				WorkspaceId = workspaceId,
				ProductKey = Clip(pack.Key, 64),
				Label = Clip(pack.ProductName, 200),
				PackVersion = pack.Version,
				AdapterKey = pack.Kind,
				CreatedBy = actor,
				CreatedOn = DateTime.UtcNow
			};
			// The source-system instance key scopes the external-id map once a data set declares this source: the same product
			// in another workspace of this department continues the same instance (incremental continuation, section 5.2).
			// Choosing the product alone changes no data set's key (none declares a source until a published pack recognizes it).
			source.SourceSystemKey = Clip($"{source.ProductKey}:{departmentId}", 128);

			await _sources.InsertAsync(source, cancellationToken, true);
			await _workspaces.TouchAsync(departmentId, workspaceId, DateTime.UtcNow, cancellationToken);
			await _timeline.RecordAsync(departmentId, workspaceId, actorKind, actor, "SourceAdded", new { product = source.ProductKey },
				cancellationToken: cancellationToken);
			return source;
		}

		public async Task RemoveSourceAsync(int departmentId, string actor, ImportActorKind actorKind, string workspaceId, string sourceId,
			CancellationToken cancellationToken = default)
		{
			await RequireOpenAsync(departmentId, workspaceId, cancellationToken);
			var source = (await _sources.GetAllForWorkspaceAsync(departmentId, workspaceId, cancellationToken))
				.FirstOrDefault(s => string.Equals(s.Id, sourceId, StringComparison.OrdinalIgnoreCase));
			// The continuation has its own control: removing it re-plans every data set (SetContinuationAsync).
			if (source == null || string.Equals(source.ProductKey, ImportSourceProducts.Continuation, StringComparison.OrdinalIgnoreCase))
				throw new DepartmentImportException("import_source_unknown", 404);
			if ((await _datasets.GetAllForWorkspaceAsync(departmentId, workspaceId, cancellationToken)).Any(d => string.Equals(d.SourceId, source.Id, StringComparison.OrdinalIgnoreCase)))
				throw new DepartmentImportException("import_source_in_use", 409);

			await _sources.DeleteAsync(source, cancellationToken);
			await _workspaces.TouchAsync(departmentId, workspaceId, DateTime.UtcNow, cancellationToken);
			await _timeline.RecordAsync(departmentId, workspaceId, actorKind, actor, "SourceRemoved", new { product = source.ProductKey },
				cancellationToken: cancellationToken);
		}

		public Task<List<ImportSource>> GetSourcesAsync(int departmentId, string workspaceId, CancellationToken cancellationToken = default) =>
			_sources.GetAllForWorkspaceAsync(departmentId, workspaceId, cancellationToken);

		public async Task<List<ImportContinuationCandidate>> GetContinuationCandidatesAsync(int departmentId, string workspaceId,
			CancellationToken cancellationToken = default)
		{
			var candidates = new List<ImportContinuationCandidate>();
			foreach (var workspace in await _workspaces.GetAllForDepartmentAsync(departmentId, cancellationToken))
			{
				if (string.Equals(workspace.Id, workspaceId, StringComparison.OrdinalIgnoreCase))
					continue;

				var written = (await _datasets.GetAllForWorkspaceAsync(departmentId, workspace.Id, cancellationToken)).Where(Wrote).ToList();
				if (written.Count > 0)
					candidates.Add(new ImportContinuationCandidate
					{
						WorkspaceId = workspace.Id,
						Name = workspace.Name,
						DatasetsImported = written.Count,
						LastImportedOn = written.Max(d => d.UpdatedOn)
					});
			}

			return candidates.OrderByDescending(c => c.LastImportedOn).ToList();
		}

		public Task<ImportSource> GetContinuationAsync(int departmentId, string workspaceId, CancellationToken cancellationToken = default) =>
			ImportSourceKeys.ContinuationAsync(_sources, departmentId, workspaceId, cancellationToken);

		public async Task<ImportSource> SetContinuationAsync(int departmentId, string actor, ImportActorKind actorKind, string workspaceId, string earlierWorkspaceId,
			CancellationToken cancellationToken = default)
		{
			var workspace = await RequireOpenAsync(departmentId, workspaceId, cancellationToken);
			var datasets = await _datasets.GetAllForWorkspaceAsync(departmentId, workspaceId, cancellationToken);
			// Rows written (or approved to be written) under one key stay under it: switching would split the department's Source IDs.
			if (datasets.Any(d => Wrote(d) || d.Status == (int)ImportDatasetStatus.Importing) ||
				(await _runs.GetAllForWorkspaceAsync(departmentId, workspaceId, cancellationToken))
				.Any(r => r.Kind == (int)ImportRunKind.Commit && r.Status != (int)ImportRunStatus.Cancelled))
				throw new DepartmentImportException("import_continuation_locked", 409);

			var current = await ImportSourceKeys.ContinuationAsync(_sources, departmentId, workspaceId, cancellationToken);
			ImportWorkspace earlier = null;
			if (!string.IsNullOrWhiteSpace(earlierWorkspaceId))
			{
				earlier = string.Equals(earlierWorkspaceId, workspaceId, StringComparison.OrdinalIgnoreCase)
					? null
					: await _workspaces.GetByIdAsync(departmentId, earlierWorkspaceId, cancellationToken);
				if (earlier == null || !(await _datasets.GetAllForWorkspaceAsync(departmentId, earlier.Id, cancellationToken)).Any(Wrote))
					throw new DepartmentImportException("import_continuation_unknown", 404);
			}

			var now = DateTime.UtcNow;
			if (earlier == null)
			{
				if (current == null)
					return null;
				await _sources.DeleteAsync(current, cancellationToken);
			}
			else
			{
				var key = await SourceKeyOfAsync(departmentId, earlier.Id, cancellationToken);
				if (current == null)
				{
					current = new ImportSource
					{
						DepartmentId = departmentId,
						WorkspaceId = workspaceId,
						ProductKey = ImportSourceProducts.Continuation,
						CreatedBy = actor,
						CreatedOn = now
					};
					current.Label = Clip(earlier.Name, 200);
					current.SourceSystemKey = key;
					current.ConnectorReference = earlier.Id;
					await _sources.InsertAsync(current, cancellationToken, true);
				}
				else
				{
					current.Label = Clip(earlier.Name, 200);
					current.SourceSystemKey = key;
					current.ConnectorReference = earlier.Id;
					await _sources.UpdateAsync(current, cancellationToken, true);
				}
			}

			// Every decision and reference was worked out under the old key: withdraw approvals and check again.
			await ReplanAsync(datasets, cancellationToken);
			await _timeline.RecordAsync(departmentId, workspaceId, actorKind, actor, earlier == null ? "ContinuationCleared" : "ContinuationSet",
				earlier == null ? null : new { earlierWorkspaceId = earlier.Id }, cancellationToken: cancellationToken);
			await _workspaces.TouchAsync(departmentId, workspaceId, now, cancellationToken);
			return earlier == null ? null : current;
		}

		public async Task<ImportWorkspace> SetWriteModeAsync(int departmentId, string actor, ImportActorKind actorKind, string workspaceId, ImportWriteMode mode,
			CancellationToken cancellationToken = default)
		{
			if (mode != ImportWriteMode.CreateOnly && mode != ImportWriteMode.ReviewedUpdate && mode != ImportWriteMode.Replace)
				throw new DepartmentImportException("import_write_mode_unavailable");

			var workspace = await RequireOpenAsync(departmentId, workspaceId, cancellationToken);
			// Replace is support-assisted (section 5.2): only while Resgrid support holds Prepare access or more to this workspace.
			if (mode == ImportWriteMode.Replace && !await HasReplaceSupportAsync(departmentId, workspaceId, cancellationToken))
				throw new DepartmentImportException("import_replace_needs_support", 409);
			if (EffectiveWriteMode(workspace) == mode)
				return workspace;
			if ((await _runs.GetAllForWorkspaceAsync(departmentId, workspaceId, cancellationToken))
				.Any(r => r.Kind == (int)ImportRunKind.Commit && DepartmentImportStateMachine.OpenRunStatuses.Contains((ImportRunStatus)r.Status)))
				throw new DepartmentImportException("import_run_open", 409);

			workspace.DefaultWriteMode = (int)mode;
			workspace.UpdatedOn = DateTime.UtcNow;
			workspace.LastActivityOn = workspace.UpdatedOn;
			if (!await _workspaces.UpdateWithRevisionAsync(workspace, workspace.Revision, cancellationToken))
				throw new DepartmentImportException("import_changed_elsewhere", 409);

			// The preview's row decisions were planned for the old mode (section 10.2): plan them again before anything is approved.
			await ReplanAsync(await _datasets.GetAllForWorkspaceAsync(departmentId, workspaceId, cancellationToken), cancellationToken);
			await _timeline.RecordAsync(departmentId, workspaceId, actorKind, actor, "WriteModeChanged", new { mode = mode.ToString() },
				cancellationToken: cancellationToken);
			return workspace;
		}

		/// <summary>The mode a workspace's rows are planned for (the planner treats anything else as Add only).</summary>
		public static ImportWriteMode EffectiveWriteMode(ImportWorkspace workspace) => (ImportWriteMode)workspace.DefaultWriteMode switch
		{
			ImportWriteMode.ReviewedUpdate => ImportWriteMode.ReviewedUpdate,
			ImportWriteMode.Replace => ImportWriteMode.Replace,
			_ => ImportWriteMode.CreateOnly
		};

		/// <summary>Whether Replace may be chosen: an active support grant of Prepare or Operate on the workspace.</summary>
		public async Task<bool> HasReplaceSupportAsync(int departmentId, string workspaceId, CancellationToken cancellationToken = default) =>
			DepartmentImportSupportService.ActiveGrant(await _grants.GetForWorkspaceAsync(departmentId, workspaceId, cancellationToken))?.Level >=
			(int)ImportSupportAccessLevel.Prepare;

		/// <summary>A data set that wrote rows into the department (and so pins its workspace's source key).</summary>
		private static bool Wrote(ImportDataset dataset) =>
			dataset.Status == (int)ImportDatasetStatus.Imported || dataset.Status == (int)ImportDatasetStatus.Verified ||
			dataset.CreatedCount + dataset.UpdatedCount + dataset.LinkedCount > 0;

		/// <summary>The key most of a workspace's written data sets used: a continuation of a continuation keeps the first import's key.</summary>
		private async Task<string> SourceKeyOfAsync(int departmentId, string workspaceId, CancellationToken cancellationToken)
		{
			var keys = new List<string>();
			foreach (var dataset in (await _datasets.GetAllForWorkspaceAsync(departmentId, workspaceId, cancellationToken)).Where(Wrote))
				keys.Add(await ImportSourceKeys.ForDatasetAsync(_sources, dataset, cancellationToken));
			return keys.GroupBy(k => k, StringComparer.Ordinal).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault()
				?? ImportSourceKeys.Workspace(workspaceId);
		}

		/// <summary>Withdraws approvals and re-runs the checks (and so the plan) of the live data sets that can still change.</summary>
		private async Task ReplanAsync(IEnumerable<ImportDataset> datasets, CancellationToken cancellationToken)
		{
			foreach (var dataset in datasets.Where(d => DepartmentImportStateMachine.IsLive(d) && d.EntityKey != null && !string.IsNullOrWhiteSpace(d.ColumnMapId)))
			{
				var status = (ImportDatasetStatus)dataset.Status;
				if (status != ImportDatasetStatus.Ready && status != ImportDatasetStatus.Approved && status != ImportDatasetStatus.NeedsAttention)
					continue;

				DepartmentImportStateMachine.InvalidateApproval(dataset);
				dataset.Status = (int)ImportDatasetStatus.Analyzing;
				dataset.UpdatedOn = DateTime.UtcNow;
				if (!await _datasets.UpdateWithRevisionAsync(dataset, dataset.Revision, cancellationToken))
					throw new DepartmentImportException("import_changed_elsewhere", 409);
				await QueueAnalysisAsync(dataset, false, cancellationToken);
			}
		}

		/// <summary>The workspace's uploaded files (logos uploaded on the Configure step are listed there, not here).</summary>
		public async Task<List<ImportArtifact>> GetArtifactsAsync(int departmentId, string workspaceId, CancellationToken cancellationToken = default) =>
			(await _artifacts.GetAllForWorkspaceAsync(departmentId, workspaceId, cancellationToken)).Where(a => a.Kind != (int)ImportArtifactKind.Logo).ToList();

		public Task<List<ImportDataset>> GetDatasetsAsync(int departmentId, string workspaceId, CancellationToken cancellationToken = default) =>
			_datasets.GetAllForWorkspaceAsync(departmentId, workspaceId, cancellationToken);

		public Task<ImportDataset> GetDatasetAsync(int departmentId, string datasetId, CancellationToken cancellationToken = default) =>
			_datasets.GetByIdAsync(departmentId, datasetId, cancellationToken);

		public async Task<ImportDataset> SetDestinationAsync(int departmentId, string actor, ImportActorKind actorKind, string datasetId, string entityKey,
			int expectedRevision, CancellationToken cancellationToken = default)
		{
			var dataset = await RequireEditableDatasetAsync(departmentId, datasetId, cancellationToken);
			var workspace = await RequireOpenAsync(departmentId, dataset.WorkspaceId, cancellationToken);
			var catalog = _catalog.GetVersion(workspace.CatalogVersion) ?? _catalog;
			var contract = catalog.GetContract(entityKey) ?? throw new DepartmentImportException("import_unknown_destination");

			var changed = !string.Equals(dataset.EntityKey, contract.Key, StringComparison.OrdinalIgnoreCase);
			dataset.EntityKey = contract.Key;
			dataset.ContractVersion = contract.Version;
			dataset.Wave = contract.Order;
			dataset.DestinationConfirmed = true;
			dataset.UpdatedOn = DateTime.UtcNow;
			DepartmentImportStateMachine.InvalidateApproval(dataset);
			if (changed)
			{
				// A new destination gets a fresh proposed mapping on the next analysis.
				dataset.ColumnMapId = null;
				dataset.Status = (int)ImportDatasetStatus.Analyzing;
			}

			if (!await _datasets.UpdateWithRevisionAsync(dataset, expectedRevision, cancellationToken))
				throw new DepartmentImportException("import_changed_elsewhere", 409);

			await QueueAnalysisAsync(dataset, changed, cancellationToken);
			await _timeline.RecordAsync(departmentId, dataset.WorkspaceId, actorKind, actor, "DestinationSet",
				new { datasetId, entity = contract.Key }, cancellationToken: cancellationToken);
			return dataset;
		}

		public async Task<ImportDataset> ExcludeDatasetAsync(int departmentId, string actor, ImportActorKind actorKind, string datasetId, bool excluded,
			CancellationToken cancellationToken = default)
		{
			var dataset = await RequireEditableDatasetAsync(departmentId, datasetId, cancellationToken);
			dataset.Status = excluded ? (int)ImportDatasetStatus.Excluded : (int)ImportDatasetStatus.Analyzing;
			dataset.ApprovedRunId = null;
			dataset.UpdatedOn = DateTime.UtcNow;
			if (!await _datasets.UpdateWithRevisionAsync(dataset, dataset.Revision, cancellationToken))
				throw new DepartmentImportException("import_changed_elsewhere", 409);

			if (!excluded)
				await QueueAnalysisAsync(dataset, false, cancellationToken);

			await _timeline.RecordAsync(departmentId, dataset.WorkspaceId, actorKind, actor, excluded ? "DatasetExcluded" : "DatasetIncluded",
				new { datasetId }, cancellationToken: cancellationToken);
			await RefreshStatusAsync(departmentId, dataset.WorkspaceId, cancellationToken);
			return dataset;
		}

		public async Task<ImportWorkspace> RefreshStatusAsync(int departmentId, string workspaceId, CancellationToken cancellationToken = default)
		{
			var workspace = await _workspaces.GetByIdAsync(departmentId, workspaceId, cancellationToken);
			if (workspace == null)
				return null;

			var datasets = await _datasets.GetAllForWorkspaceAsync(departmentId, workspaceId, cancellationToken);
			var runs = await _runs.GetAllForWorkspaceAsync(departmentId, workspaceId, cancellationToken);
			var derived = DepartmentImportStateMachine.Derive(workspace, datasets, runs);
			if ((int)derived == workspace.Status)
				return workspace;

			workspace.Status = (int)derived;
			workspace.UpdatedOn = DateTime.UtcNow;
			await _workspaces.UpdateWithRevisionAsync(workspace, workspace.Revision, cancellationToken);
			return workspace;
		}

		public Task RecordEventAsync(int departmentId, string workspaceId, ImportActorKind actorKind, string actorIdentity, string action, object details = null,
			string correlationId = null, CancellationToken cancellationToken = default) =>
			_timeline.RecordAsync(departmentId, workspaceId, actorKind, actorIdentity, action, details, correlationId, cancellationToken);

		public Task<List<ImportEvent>> GetEventsAsync(int departmentId, string workspaceId, int skip, int take, CancellationToken cancellationToken = default) =>
			_timeline.GetAsync(departmentId, workspaceId, skip, take, cancellationToken);

		public async Task<ImportWorkspaceSummary> GetSummaryAsync(int departmentId, string workspaceId, CancellationToken cancellationToken = default)
		{
			var workspace = await RefreshStatusAsync(departmentId, workspaceId, cancellationToken) ?? throw new DepartmentImportException("import_workspace_not_found", 404);
			var datasets = await _datasets.GetAllForWorkspaceAsync(departmentId, workspaceId, cancellationToken);
			var runs = await _runs.GetAllForWorkspaceAsync(departmentId, workspaceId, cancellationToken);
			var live = datasets.Where(DepartmentImportStateMachine.IsLive).ToList();

			var summary = new ImportWorkspaceSummary
			{
				Workspace = workspace,
				Datasets = datasets,
				OpenRuns = runs.Where(r => DepartmentImportStateMachine.OpenRunStatuses.Contains((ImportRunStatus)r.Status)).ToList(),
				RowsAnalysed = live.Sum(d => d.RowCount),
				RowsReady = live.Sum(d => d.ReadyCount),
				RowsNeedingAttention = live.Sum(d => d.BlockedCount + d.WarningCount),
				RowsImported = live.Sum(d => d.CreatedCount + d.UpdatedCount + d.LinkedCount),
				OpenBlockingIssues = await _issues.CountOpenBlockingAsync(departmentId, workspaceId, null, cancellationToken),
				UnreadMessages = await _messages.CountUnreadByDepartmentAsync(departmentId, workspaceId, cancellationToken),
				OpenSupportRequests = (await _supportRequests.GetForWorkspaceAsync(departmentId, workspaceId, cancellationToken))
					.Count(r => r.Status < (int)ImportSupportRequestStatus.Resolved),
				ReadyDatasets = live.Count(d => d.Status >= (int)ImportDatasetStatus.Ready && d.Status <= (int)ImportDatasetStatus.Verified),
				TotalDatasets = live.Count,
				StoreHealth = await _store.CheckHealthAsync(cancellationToken)
			};

			summary.PeopleToInvite = live.Where(d => string.Equals(d.EntityKey, "personnel", StringComparison.OrdinalIgnoreCase)).Sum(d => d.CreatedCount);
			NextBestAction(summary, live);
			return summary;
		}

		/// <summary>The hub's single highlighted next step, from the dependency graph and the open blocking issues (section 6.2).</summary>
		private void NextBestAction(ImportWorkspaceSummary summary, List<ImportDataset> live)
		{
			var catalog = _catalog.GetVersion(summary.Workspace.CatalogVersion) ?? _catalog;
			var order = catalog.InDependencyOrder().Select(c => c.Key).ToList();
			int Rank(ImportDataset d) => d.EntityKey == null ? -1 : order.IndexOf(d.EntityKey);

			if (summary.OpenRuns.Any(r => r.Status == (int)ImportRunStatus.Paused))
			{
				summary.NextActionKey = "Next.RunPaused";
				return;
			}

			var unmapped = live.Where(d => d.Status == (int)ImportDatasetStatus.NeedsMapping).OrderBy(Rank).FirstOrDefault();
			if (unmapped != null)
			{
				summary.NextActionKey = "Next.MatchColumns";
				summary.NextActionDatasetId = unmapped.Id;
				summary.NextActionArgs.Add(unmapped.Name ?? string.Empty);
				return;
			}

			var attention = live.Where(d => d.Status == (int)ImportDatasetStatus.NeedsAttention).OrderBy(Rank).FirstOrDefault();
			if (attention != null)
			{
				summary.NextActionKey = "Next.FixIssues";
				summary.NextActionDatasetId = attention.Id;
				summary.NextActionArgs.Add(attention.BlockedCount.ToString());
				summary.NextActionArgs.Add(attention.Name ?? string.Empty);
				return;
			}

			var ready = live.Where(d => d.Status == (int)ImportDatasetStatus.Ready).OrderBy(Rank).FirstOrDefault();
			if (ready != null)
			{
				summary.NextActionKey = "Next.ReviewAndImport";
				summary.NextActionDatasetId = ready.Id;
				summary.NextActionArgs.Add(ready.Name ?? string.Empty);
				return;
			}

			if (live.Count > 0 && live.All(d => d.Status == (int)ImportDatasetStatus.Imported || d.Status == (int)ImportDatasetStatus.Verified))
			{
				summary.NextActionKey = summary.Workspace.Status == (int)ImportWorkspaceStatus.ReadyToActivate ? "Next.Activate" : "Next.Verify";
				return;
			}

			summary.NextActionKey = live.Count == 0 ? "Next.UploadFirst" : "Next.WaitForChecks";
		}

		private async Task QueueAnalysisAsync(ImportDataset dataset, bool redetect, CancellationToken cancellationToken)
		{
			if (redetect && !string.IsNullOrWhiteSpace(dataset.ArtifactId))
			{
				// A new destination re-runs detection for this sheet only, through a revalidation that first proposes a map.
				await _runQueue.QueueAsync(new ImportRun
				{
					DepartmentId = dataset.DepartmentId,
					WorkspaceId = dataset.WorkspaceId,
					Kind = (int)ImportRunKind.Analysis,
					ArtifactId = dataset.ArtifactId,
					DatasetIds = JsonConvert.SerializeObject(new[] { dataset.Id }),
					Cursor = JsonConvert.SerializeObject(new ImportRunCursor { Phase = 3 }),
					RequestedBy = "system"
				}, cancellationToken);
				return;
			}

			await _runQueue.QueueAsync(new ImportRun
			{
				DepartmentId = dataset.DepartmentId,
				WorkspaceId = dataset.WorkspaceId,
				Kind = (int)ImportRunKind.Analysis,
				DatasetIds = JsonConvert.SerializeObject(new[] { dataset.Id }),
				RequestedBy = "system"
			}, cancellationToken);
		}

		private async Task<ImportWorkspace> SaveAsync(ImportWorkspace workspace, int expectedRevision, string userId, string action, object details,
			CancellationToken cancellationToken)
		{
			workspace.UpdatedOn = DateTime.UtcNow;
			workspace.LastActivityOn = workspace.UpdatedOn;
			if (!await _workspaces.UpdateWithRevisionAsync(workspace, expectedRevision, cancellationToken))
				throw new DepartmentImportException("import_changed_elsewhere", 409);

			await _timeline.RecordAsync(workspace.DepartmentId, workspace.Id, ImportActorKind.Administrator, userId, action, details, cancellationToken: cancellationToken);
			return workspace;
		}

		private async Task<ImportWorkspace> RequireOpenAsync(int departmentId, string workspaceId, CancellationToken cancellationToken)
		{
			var workspace = await _workspaces.GetByIdAsync(departmentId, workspaceId, cancellationToken) ?? throw new DepartmentImportException("import_workspace_not_found", 404);
			if (DepartmentImportStateMachine.IsTerminal((ImportWorkspaceStatus)workspace.Status))
				throw new DepartmentImportException("import_workspace_closed", 409);
			return workspace;
		}

		private async Task<ImportDataset> RequireEditableDatasetAsync(int departmentId, string datasetId, CancellationToken cancellationToken)
		{
			var dataset = await _datasets.GetByIdAsync(departmentId, datasetId, cancellationToken) ?? throw new DepartmentImportException("import_dataset_not_found", 404);
			var status = (ImportDatasetStatus)dataset.Status;
			if (status == ImportDatasetStatus.Importing || status == ImportDatasetStatus.Imported || status == ImportDatasetStatus.Verified)
				throw new DepartmentImportException("import_dataset_locked", 409);
			return dataset;
		}

		private static string Clip(string value, int length) =>
			string.IsNullOrEmpty(value) || value.Length <= length ? value : value.Substring(0, length);
	}
}
