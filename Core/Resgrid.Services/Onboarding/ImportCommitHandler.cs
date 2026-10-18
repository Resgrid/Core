using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Newtonsoft.Json;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Onboarding;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;
using Resgrid.Services.Onboarding.Writers;

namespace Resgrid.Services.Onboarding
{
	/// <summary>
	/// Executes Commit runs (wizard plan sections 11 and 13). Phases, each checkpointed on the run cursor:
	/// <list type="number">
	/// <item>Prepare: L9 (digest, approver), the department lock, the approved configuration (section 7.5, written before any
	/// data set so a module it turns on unblocks that module's data), the gates, one fresh capacity read, custom field definitions.</item>
	/// <item>Write: datasets in dependency order, chunk by chunk. Each chunk runs in its own lifetime scope and unit-of-work
	/// transaction with claim-before-create, the journal and row outcomes; a chunk that fails deterministically is replayed
	/// row by row and the failing row quarantined (the hydrant pattern).</item>
	/// <item>Deferred: work that needs the whole dataset written (group parents, next statuses, roles held for certifications).</item>
	/// <item>Effects: apps resync, search rebuild request, cache clear, one summary audit per entity.</item>
	/// <item>Reconcile (L10): every writer verifies what the run wrote; datasets become Verified, the lock is released.</item>
	/// </list>
	/// </summary>
	public class ImportCommitHandler : IImportRunHandler
	{
		private const int PhasePrepare = 0;
		private const int PhaseWrite = 1;
		private const int PhaseDeferred = 2;
		private const int PhaseEffects = 3;
		private const int PhaseReconcile = 4;
		private const int DeferredPageSize = 500;
		private const int ChunkAttempts = 3;
		private static readonly TimeSpan LockLease = TimeSpan.FromMinutes(10);

		private readonly ILifetimeScope _scope;
		private readonly IImportWorkspacesRepository _workspaces;
		private readonly IImportDatasetsRepository _datasets;
		private readonly IImportRunsRepository _runs;
		private readonly IImportExternalIdsRepository _externalIds;
		private readonly IImportStagedRowsRepository _rows;
		private readonly IImportIssuesRepository _issues;
		private readonly IImportChangeJournalRepository _journal;
		private readonly IImportReconciliationsRepository _reconciliations;
		private readonly IImportCatalog _catalog;
		private readonly IImportValidationService _validation;
		private readonly IImportPlanService _plan;
		private readonly IDepartmentImportAccessService _access;
		private readonly IDepartmentLockService _locks;
		private readonly IDepartmentsService _departments;
		private readonly IUserDefinedFieldsService _customFields;
		private readonly IImportTimelineService _timeline;
		private readonly ImportEffectsRunner _effects;
		private readonly Lazy<IAdminAssistWorklistService> _worklist;
		private readonly IImportSupportGrantsRepository _grants;
		private readonly IImportArtifactsRepository _artifacts;
		private readonly IDepartmentSettingsService _settings;
		private readonly IDepartmentDataProtectionService _protection;
		private readonly IImportConfigurationService _configuration;
		private readonly IImportConfigurationChangesRepository _configurationChanges;

		public ImportCommitHandler(ILifetimeScope scope, IImportWorkspacesRepository workspaces, IImportDatasetsRepository datasets, IImportRunsRepository runs,
			IImportExternalIdsRepository externalIds, IImportStagedRowsRepository rows, IImportIssuesRepository issues, IImportChangeJournalRepository journal,
			IImportReconciliationsRepository reconciliations, IImportCatalog catalog, IImportValidationService validation, IImportPlanService plan,
			IDepartmentImportAccessService access, IDepartmentLockService locks, IDepartmentsService departments, IUserDefinedFieldsService customFields,
			IImportTimelineService timeline, ImportEffectsRunner effects, Lazy<IAdminAssistWorklistService> worklist, IImportSupportGrantsRepository grants,
			IImportArtifactsRepository artifacts, IDepartmentSettingsService settings, IDepartmentDataProtectionService protection,
			IImportConfigurationService configuration, IImportConfigurationChangesRepository configurationChanges)
		{
			_configuration = configuration;
			_configurationChanges = configurationChanges;
			_grants = grants;
			_artifacts = artifacts;
			_settings = settings;
			_protection = protection;
			_scope = scope;
			_workspaces = workspaces;
			_datasets = datasets;
			_runs = runs;
			_externalIds = externalIds;
			_rows = rows;
			_issues = issues;
			_journal = journal;
			_reconciliations = reconciliations;
			_catalog = catalog;
			_validation = validation;
			_plan = plan;
			_access = access;
			_locks = locks;
			_departments = departments;
			_customFields = customFields;
			_timeline = timeline;
			_effects = effects;
			_worklist = worklist;
		}

		public ImportRunKind Kind => ImportRunKind.Commit;

		/// <summary>Per-slice state shared by the phases.</summary>
		private sealed class Slice
		{
			public ImportRun Run;
			public ImportRunLease Lease;
			public ImportRunCursor Cursor;
			public ImportRunCounts Counts;
			public ImportWorkspace Workspace;
			public Department Department;
			public ImportAuthority Authority;
			public ImportConsequenceDecisions Consequences;
			public IImportCatalog Catalog;
			public List<ImportDataset> Datasets;
			/// <summary>Entity key → the source-system keys of the workspace's live datasets of that entity (references may point at any).</summary>
			public Dictionary<string, List<string>> SourceKeysByEntity;
			public Dictionary<string, string> SourceKeyByDataset;
			public long NextSequence;
			public DateTime Deadline;
		}

		public async Task<ImportStepResult> ExecuteAsync(ImportRun run, ImportRunLease lease, DateTime deadline, CancellationToken cancellationToken)
		{
			var workspace = await _workspaces.GetByIdAsync(run.DepartmentId, run.WorkspaceId, cancellationToken);
			if (workspace == null || DepartmentImportStateMachine.IsTerminal((ImportWorkspaceStatus)workspace.Status))
				return ImportStepResult.Failed("IMP-WORKSPACE-CLOSED");

			var slice = new Slice
			{
				Run = run,
				Lease = lease,
				Deadline = deadline,
				Workspace = workspace,
				Cursor = string.IsNullOrWhiteSpace(run.Cursor) ? new ImportRunCursor() : JsonConvert.DeserializeObject<ImportRunCursor>(run.Cursor),
				Counts = string.IsNullOrWhiteSpace(run.Counts) ? new ImportRunCounts() : JsonConvert.DeserializeObject<ImportRunCounts>(run.Counts),
				Authority = string.IsNullOrWhiteSpace(run.Authority) ? ImportAuthority.ForAdministrator(run.DepartmentId, run.ApprovedBy) : JsonConvert.DeserializeObject<ImportAuthority>(run.Authority),
				Consequences = string.IsNullOrWhiteSpace(run.ConsequenceDecisions) ? new ImportConsequenceDecisions() : JsonConvert.DeserializeObject<ImportConsequenceDecisions>(run.ConsequenceDecisions),
				Catalog = _catalog.GetVersion(workspace.CatalogVersion) ?? _catalog,
				Department = await _departments.GetDepartmentByIdAsync(run.DepartmentId, false)
			};
			slice.Datasets = await RunDatasetsAsync(slice, cancellationToken);
			slice.NextSequence = await _journal.GetMaxSequenceAsync(run.DepartmentId, run.Id, cancellationToken) + 1;
			await LoadSourceKeysAsync(slice, cancellationToken);

			if (slice.Cursor.Phase <= PhasePrepare)
			{
				var refused = await PrepareAsync(slice, cancellationToken);
				if (refused != null)
					return refused;
				if (!await CheckpointAsync(slice, PhaseWrite, slice.Datasets.FirstOrDefault()?.Id, 0, cancellationToken))
					return ImportStepResult.More();
			}
			else
			{
				// Every later slice re-takes or extends the department lock and re-checks the approver (L9 per slice).
				var refused = await EnsureLockAsync(slice, cancellationToken) ?? await ReauthorizeAsync(slice);
				if (refused != null)
					return refused;
			}

			if (slice.Cursor.Phase == PhaseWrite)
			{
				var step = await WritePhaseAsync(slice, cancellationToken);
				if (step != null)
					return step;
			}

			if (slice.Cursor.Phase == PhaseDeferred)
			{
				var step = await DeferredPhaseAsync(slice, cancellationToken);
				if (step != null)
					return step;
			}

			if (slice.Cursor.Phase == PhaseEffects)
			{
				await EffectsPhaseAsync(slice, cancellationToken);
				run.Status = (int)ImportRunStatus.Reconciling;
				if (!await CheckpointAsync(slice, PhaseReconcile, null, 0, cancellationToken))
					return ImportStepResult.More();
			}

			if (slice.Cursor.Phase == PhaseReconcile)
			{
				run.Status = (int)ImportRunStatus.Reconciling;
				await ReconcileAsync(slice, cancellationToken);
				await ReleaseLockAsync(slice, DepartmentOperationLockReleaseKind.Completed, cancellationToken);
				await RefreshWorkspaceAsync(slice, cancellationToken);
				return ImportStepResult.Done();
			}

			return ImportStepResult.More();
		}

		// ---- Prepare --------------------------------------------------------------------------------------------------

		private async Task<ImportStepResult> PrepareAsync(Slice slice, CancellationToken cancellationToken)
		{
			var run = slice.Run;
			var configuration = await _configurationChanges.GetForRunAsync(run.DepartmentId, run.Id, cancellationToken);
			if (slice.Datasets.Count == 0 && configuration.Count == 0)
				return ImportStepResult.Failed("IMP-RUN-NOTHING-APPROVED");

			// L9: the staged state and the configuration values are what was approved.
			var ids = slice.Datasets.Select(d => d.Id).ToList();
			var digest = ImportConfigurationService.CombineDigest(
				await _plan.ComputeApprovalDigestAsync(run.DepartmentId, ids, (ImportWriteMode)run.WriteMode, cancellationToken), configuration);
			if (!string.Equals(digest, run.ApprovalDigest, StringComparison.Ordinal))
				return await PauseWithIssueAsync(slice, "IMP-APPROVAL-STALE", cancellationToken);

			var refused = await ReauthorizeAsync(slice);
			if (refused != null)
				return refused;

			refused = await EnsureLockAsync(slice, cancellationToken);
			if (refused != null)
				return refused;

			// Configuration first (section 7.5), outside any chunk transaction (module switches commit their own). A value
			// written by an earlier slice is already Applied and is not written twice.
			if (configuration.Count > 0)
				await ApplyConfigurationAsync(slice, cancellationToken);

			foreach (var dataset in slice.Datasets)
			{
				var gate = await _plan.CheckGateAsync(run.DepartmentId, dataset.EntityKey, slice.Workspace.CatalogVersion);
				if (gate != null)
					return await PauseWithIssueAsync(slice, gate, cancellationToken);
			}

			// One fresh capacity read at commit start; the run keeps its own counter from here (section 11.6).
			var capacity = await _plan.ReadCapacityAsync(run.DepartmentId);
			slice.Counts.PersonnelCapacityRemaining = capacity.Personnel;
			slice.Counts.UnitCapacityRemaining = capacity.Units;
			slice.Counts.SharedCapacity = capacity.Shared;

			// Custom field definitions are configuration written before any row (dependency order item 2). Publishing
			// commits its own unit of work, so it happens here and never inside a chunk transaction.
			foreach (var dataset in slice.Datasets.Where(d => slice.Catalog.GetContract(d.EntityKey)?.SupportsCustomFields == true))
			{
				var labels = await _plan.GetKeptCustomFieldLabelsAsync(dataset, cancellationToken);
				if (labels.Count > 0)
					await ImportCustomFields.PublishFirstDefinitionAsync(_customFields, run.DepartmentId, ImportPlanService.CustomFieldEntity(dataset.EntityKey), labels,
						slice.Authority.OnBehalfOfUserId, cancellationToken);
			}

			var now = DateTime.UtcNow;
			foreach (var dataset in slice.Datasets)
			{
				dataset.Status = (int)ImportDatasetStatus.Importing;
				dataset.UpdatedOn = now;
				await _datasets.UpdateWithRevisionAsync(dataset, dataset.Revision, cancellationToken);
			}

			run.StartedOn ??= now;
			await RefreshWorkspaceAsync(slice, cancellationToken);
			await _timeline.RecordAsync(run.DepartmentId, run.WorkspaceId, ImportActorKind.System, slice.Lease.Owner, "CommitStarted",
				new { runId = run.Id, datasets = ids.Count, capacityKnown = capacity.Known }, run.CorrelationId, cancellationToken);
			return null;
		}

		/// <summary>Writes the run's approved configuration values, journaling each before-value in the run's sequence.</summary>
		private async Task ApplyConfigurationAsync(Slice slice, CancellationToken cancellationToken)
		{
			var run = slice.Run;
			var result = await _configuration.ApplyAsync(run, slice.Authority.OnBehalfOfUserId, (entry, ct) =>
			{
				entry.DepartmentId = run.DepartmentId;
				entry.WorkspaceId = run.WorkspaceId;
				entry.RunId = run.Id;
				entry.Sequence = slice.NextSequence++;
				entry.CreatedOn = DateTime.UtcNow;
				return _journal.InsertAsync(entry, ct, true);
			}, cancellationToken);

			if (result.Written + result.Unchanged + result.Failed == 0)
				return;
			if (!slice.Counts.Entities.TryGetValue(ImportConfigurationCatalog.JournalEntityType, out var counts))
				slice.Counts.Entities[ImportConfigurationCatalog.JournalEntityType] = counts = new ImportEntityCounts();
			counts.Planned += result.Written + result.Unchanged + result.Failed;
			counts.Updated += result.Written;
			counts.Unchanged += result.Unchanged;
			counts.Failed += result.Failed;
		}

		private async Task<ImportStepResult> ReauthorizeAsync(Slice slice)
		{
			// The approving administrator is re-authorized at commit start and every slice (section 14.2).
			if (!await _access.IsEnabledAsync(slice.Run.DepartmentId))
				return ImportStepResult.Paused("IMP-ENTITLEMENT-MISSING");
			if (!await _access.CanManageAsync(slice.Run.DepartmentId, slice.Authority.OnBehalfOfUserId))
				return ImportStepResult.Paused("IMP-APPROVER-NOT-AUTHORIZED");
			// A staff-run import stops at the next slice once its Operate grant is revoked, expires or no longer allows commits.
			if (slice.Authority.IsStaff && !await StaffGrantAllowsAsync(_grants, slice.Run, slice.Authority, ImportOperateActions.Commit))
				return ImportStepResult.Paused("IMP-APPROVER-NOT-AUTHORIZED");
			return null;
		}

		/// <summary>The run's Operate grant is active for its workspace and allows <paramref name="action"/> (section 17.8).</summary>
		public static async Task<bool> StaffGrantAllowsAsync(IImportSupportGrantsRepository grants, ImportRun run, ImportAuthority authority, ImportOperateActions action)
		{
			var grant = DepartmentImportSupportService.ActiveGrant(await grants.GetForWorkspaceAsync(run.DepartmentId, run.WorkspaceId));
			return grant != null && grant.Id == authority.GrantId && grant.Level == (int)ImportSupportAccessLevel.Operate &&
				((ImportOperateActions)grant.AllowedActions).HasFlag(action);
		}

		private async Task<ImportStepResult> EnsureLockAsync(Slice slice, CancellationToken cancellationToken)
		{
			var run = slice.Run;
			var until = DateTime.UtcNow.Add(LockLease);
			if (run.DepartmentLockId.HasValue && await _locks.HeartbeatAsync(run.DepartmentLockId.Value, until, cancellationToken))
				return null;

			var acquired = await _locks.ApplyLockAsync(run.DepartmentId, DepartmentOperationLockType.DepartmentImport, "Department import",
				run.CorrelationId, "import:" + run.Id, until, null, cancellationToken);
			if (acquired == null)
				// An ADP migration or another import holds the department: retry with backoff (section 11.7).
				return ImportStepResult.Transient("IMP-DEPARTMENT-LOCKED");

			run.DepartmentLockId = acquired.DepartmentOperationLockId;
			return null;
		}

		private async Task ReleaseLockAsync(Slice slice, DepartmentOperationLockReleaseKind kind, CancellationToken cancellationToken)
		{
			if (!slice.Run.DepartmentLockId.HasValue)
				return;
			try
			{
				await _locks.ReleaseLockAsync(slice.Run.DepartmentLockId.Value, kind, "import:" + slice.Run.Id, cancellationToken);
			}
			catch (Exception ex)
			{
				// The lock expires on its own; the release is best effort.
				Logging.LogException(ex, null, slice.Run.CorrelationId);
			}
			slice.Run.DepartmentLockId = null;
		}

		// ---- Write ----------------------------------------------------------------------------------------------------

		private async Task<ImportStepResult> WritePhaseAsync(Slice slice, CancellationToken cancellationToken)
		{
			var context = BaseContext(slice);
			var start = Math.Max(0, slice.Datasets.FindIndex(d => d.Id == slice.Cursor.DatasetId));

			for (var i = start; i < slice.Datasets.Count; i++)
			{
				var dataset = slice.Datasets[i];
				var after = dataset.Id == slice.Cursor.DatasetId ? slice.Cursor.LastRowNumber : 0;

				while (true)
				{
					var control = await ControlAsync(slice, cancellationToken);
					if (control != null)
						return control;

					var page = await _validation.ReadCanonicalRowsAsync(dataset, after, ImportConfig.CommitChunkRows, cancellationToken);
					if (page.Count == 0)
						break;

					var lastRow = page[page.Count - 1].RowNumber;
					var rows = page.Where(r => r.IsWritable && r.Outcome == ImportRowOutcome.None).ToList();
					if (rows.Count > 0)
					{
						var outcome = await WriteChunkAsync(slice, context, dataset, rows, cancellationToken);
						if (outcome != null)
							return outcome;
					}

					after = lastRow;
					if (!await CheckpointAsync(slice, PhaseWrite, dataset.Id, after, cancellationToken))
						return ImportStepResult.More();
					await _locks.HeartbeatAsync(slice.Run.DepartmentLockId ?? 0, DateTime.UtcNow.Add(LockLease), cancellationToken);
				}

				await CompleteDatasetAsync(slice, dataset, cancellationToken);
				var next = i + 1 < slice.Datasets.Count ? slice.Datasets[i + 1].Id : null;
				if (!await CheckpointAsync(slice, next == null ? PhaseDeferred : PhaseWrite, next, 0, cancellationToken))
					return ImportStepResult.More();
			}

			slice.Cursor.Phase = PhaseDeferred;
			slice.Cursor.DatasetId = null;
			slice.Cursor.CompletedEntities.Clear();
			return null;
		}

		/// <summary>Writes one chunk; returns a terminal step result, or null to continue.</summary>
		private async Task<ImportStepResult> WriteChunkAsync(Slice slice, ImportWriteContext baseContext, ImportDataset dataset, List<ImportCanonicalRow> rows,
			CancellationToken cancellationToken)
		{
			var run = slice.Run;
			var sourceKey = slice.SourceKeyByDataset[dataset.Id];
			var templateWriter = Writer(_scope, dataset.EntityKey);

			// Identity accounts are created outside the chunk transaction: their claims (with the pre-generated key) commit first.
			if (templateWriter is IImportPregeneratedKeys pregenerated)
				await ClaimPregeneratedAsync(slice, dataset, sourceKey, pregenerated, rows, cancellationToken);

			for (var attempt = 1; ; attempt++)
			{
				try
				{
					await WriteInScopeAsync(slice, baseContext, dataset, sourceKey, rows, cancellationToken);
					await _effects.RunChunkEffectsAsync(run.DepartmentId, templateWriter.AffectedEffects);
					return null;
				}
				catch (Exception ex) when (IsTransient(ex) && attempt < ChunkAttempts && !cancellationToken.IsCancellationRequested)
				{
					await _effects.RunChunkEffectsAsync(run.DepartmentId, templateWriter.AffectedEffects);
					await Task.Delay(TimeSpan.FromMilliseconds(250 * Math.Pow(2, attempt) + Random.Shared.Next(0, 250)), cancellationToken);
				}
				catch (Exception ex) when (IsTransient(ex))
				{
					await _effects.RunChunkEffectsAsync(run.DepartmentId, templateWriter.AffectedEffects);
					Logging.LogException(ex, $"Department import run {run.Id}: chunk failed transiently", run.CorrelationId);
					return ImportStepResult.Transient("IMP-RUN-TRANSIENT");
				}
				catch (Exception ex) when (!(ex is OperationCanceledException))
				{
					// Deterministic: replay row by row, quarantine what fails, continue with the rest (hydrant pattern).
					await _effects.RunChunkEffectsAsync(run.DepartmentId, templateWriter.AffectedEffects);
					Logging.LogException(ex, $"Department import run {run.Id}: chunk replayed row by row", run.CorrelationId);
					foreach (var row in rows)
					{
						try
						{
							await WriteInScopeAsync(slice, baseContext, dataset, sourceKey, new List<ImportCanonicalRow> { row }, cancellationToken);
						}
						catch (Exception rowEx) when (IsTransient(rowEx))
						{
							return ImportStepResult.Transient("IMP-RUN-TRANSIENT");
						}
						catch (Exception rowEx) when (!(rowEx is OperationCanceledException))
						{
							var code = rowEx is ImportRowRejectedException rejected ? rejected.Code : "IMP-ROW-WRITE-FAILED";
							if (!(rowEx is ImportRowRejectedException))
								Logging.LogException(rowEx, $"Department import run {run.Id}: row {row.RowNumber} of dataset {dataset.Id} quarantined", run.CorrelationId);
							await QuarantineAsync(dataset, row, code, cancellationToken);
						}
					}

					await _effects.RunChunkEffectsAsync(run.DepartmentId, templateWriter.AffectedEffects);
					return null;
				}
			}
		}

		private async Task ClaimPregeneratedAsync(Slice slice, ImportDataset dataset, string sourceKey, IImportPregeneratedKeys keys, List<ImportCanonicalRow> rows,
			CancellationToken cancellationToken)
		{
			var creating = rows.Where(r => r.Decision == ImportRowDecision.Create).ToList();
			if (creating.Count == 0)
				return;

			var existing = (await _externalIds.GetForSourceIdsAsync(slice.Run.DepartmentId, sourceKey, dataset.EntityKey, creating.Select(RowKey).Distinct().ToList(), cancellationToken))
				.GroupBy(e => e.SourceId, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
			var now = DateTime.UtcNow;
			foreach (var row in creating.Where(r => !existing.ContainsKey(RowKey(r))))
				await _externalIds.TryClaimAsync(NewClaim(slice, sourceKey, dataset.EntityKey, RowKey(row), keys.NewTargetKey(), now), cancellationToken);
		}

		/// <summary>One unit-of-work transaction over <paramref name="rows"/>: claims, writes, journal and outcomes commit together.</summary>
		private async Task WriteInScopeAsync(Slice slice, ImportWriteContext baseContext, ImportDataset dataset, string sourceKey, List<ImportCanonicalRow> rows,
			CancellationToken cancellationToken)
		{
			var run = slice.Run;
			await using var scope = _scope.BeginLifetimeScope();
			var unitOfWork = scope.Resolve<IUnitOfWork>();
			await unitOfWork.CreateOrGetConnectionAsync(cancellationToken);

			var capacity = (slice.Counts.PersonnelCapacityRemaining, slice.Counts.UnitCapacityRemaining);
			try
			{
				var writers = scope.Resolve<IEnumerable<IImportTargetWriter>>().ToList();
				var writer = writers.First(w => string.Equals(w.EntityKey, dataset.EntityKey, StringComparison.OrdinalIgnoreCase));
				var ids = scope.Resolve<IImportExternalIdsRepository>();
				var journalRepository = scope.Resolve<IImportChangeJournalRepository>();
				var staged = scope.Resolve<IImportStagedRowsRepository>();
				var now = DateTime.UtcNow;
				ImportCanonicalRow currentRow = null;

				var context = Copy(baseContext, dataset);
				var journal = new ImportJournalWriter((entry, ct) =>
				{
					entry.DepartmentId = run.DepartmentId;
					entry.WorkspaceId = run.WorkspaceId;
					entry.RunId = run.Id;
					entry.Sequence = slice.NextSequence++;
					entry.StagedRowId ??= currentRow?.StagedRowId;
					entry.CreatedOn = DateTime.UtcNow;
					return journalRepository.InsertAsync(entry, ct, true);
				});
				context.ResolveReference = ReferenceResolver(slice, context, writers, ids, journal);

				var claims = (await ids.GetForSourceIdsAsync(run.DepartmentId, sourceKey, dataset.EntityKey, rows.Select(RowKey).Distinct().ToList(), cancellationToken))
					.GroupBy(e => e.SourceId, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

				foreach (var row in rows)
				{
					currentRow = row;
					var rowKey = RowKey(row);
					claims.TryGetValue(rowKey, out var claim);

					if (claim != null && (claim.State == (int)ImportExternalIdState.Written || claim.State == (int)ImportExternalIdState.Linked))
					{
						// Written before: by this run (a replay after a crash) or by an earlier run of this source.
						var outcome = claim.RunId == run.Id ? (claim.State == (int)ImportExternalIdState.Linked ? ImportRowOutcome.Linked : ImportRowOutcome.Created) : ImportRowOutcome.Unchanged;
						await staged.SetOutcomeAsync(run.DepartmentId, row.StagedRowId, (int)outcome, claim.TargetKey, now, cancellationToken);
						continue;
					}

					if (row.Decision == ImportRowDecision.Skip)
					{
						await staged.SetOutcomeAsync(run.DepartmentId, row.StagedRowId, (int)ImportRowOutcome.Skipped, row.TargetKey, now, cancellationToken);
						continue;
					}

					if (claim == null)
					{
						claim = NewClaim(slice, sourceKey, dataset.EntityKey, rowKey, null, now);
						if (!await ids.TryClaimAsync(claim, cancellationToken))
							claim = await ids.GetByIdentityAsync(run.DepartmentId, sourceKey, dataset.EntityKey, rowKey, cancellationToken) ?? claim;
					}

					if (writer is IImportPregeneratedKeys && row.Decision == ImportRowDecision.Create)
						row.TargetKey = claim.TargetKey;

					var result = await writer.WriteRowAsync(context, row, journal, cancellationToken);
					switch (result.Outcome)
					{
						case ImportRowOutcome.Created:
						case ImportRowOutcome.Updated:
							await ids.SetStateAsync(run.DepartmentId, claim.Id, ImportExternalIdState.Written, result.TargetKey, run.Id, now, cancellationToken);
							break;
						case ImportRowOutcome.Linked:
						case ImportRowOutcome.Unchanged:
							await ids.SetStateAsync(run.DepartmentId, claim.Id, ImportExternalIdState.Linked, result.TargetKey, run.Id, now, cancellationToken);
							break;
						default:
							// Not written: the identity is free for a later run (an account claim keeps its key for a retry).
							if (!(writer is IImportPregeneratedKeys) || result.Code == "IMP-PERSON-EXISTING-ACCOUNT" || result.Code == "IMP-PLAN-CAPACITY")
								await ids.SetStateAsync(run.DepartmentId, claim.Id, ImportExternalIdState.Released, claim.TargetKey, run.Id, now, cancellationToken);
							break;
					}

					await staged.SetOutcomeAsync(run.DepartmentId, row.StagedRowId, (int)result.Outcome, result.TargetKey, now, cancellationToken);
					if (result.Code != null && result.Outcome != ImportRowOutcome.Created && result.Outcome != ImportRowOutcome.Updated)
						await scope.Resolve<IImportIssuesRepository>().InsertManyAsync(new List<ImportIssue> { OutcomeIssue(dataset, row, result) }, cancellationToken);
				}

				unitOfWork.CommitChanges();
				slice.Counts.PersonnelCapacityRemaining = context.PersonnelCapacity;
				slice.Counts.UnitCapacityRemaining = context.UnitCapacity;
			}
			catch
			{
				unitOfWork.DiscardChanges();
				(slice.Counts.PersonnelCapacityRemaining, slice.Counts.UnitCapacityRemaining) = capacity;
				throw;
			}
		}

		/// <summary>
		/// Resolves a reference token to the referenced record's key (section 8.4): "id:" is an existing record, "src:" a row
		/// of another dataset written earlier (external-id map), "new:" a record created on demand from a "create new" decision
		/// (claimed under the workspace's own instance so every row naming it shares one record), "label:" is not a record.
		/// </summary>
		private Func<string, string, CancellationToken, Task<string>> ReferenceResolver(Slice slice, ImportWriteContext context, List<IImportTargetWriter> writers,
			IImportExternalIdsRepository ids, ImportJournalWriter journal)
		{
			var memo = new Dictionary<string, string>(StringComparer.Ordinal);
			return async (entityKey, token, ct) =>
			{
				if (string.IsNullOrWhiteSpace(token) || ImportReferenceToken.Is(token, ImportReferenceToken.Label))
					return null;
				var memoKey = entityKey + "|" + token;
				if (memo.TryGetValue(memoKey, out var known))
					return known;

				string key = null;
				if (ImportReferenceToken.Is(token, ImportReferenceToken.Existing))
				{
					key = ImportReferenceToken.Value(token, ImportReferenceToken.Existing);
				}
				else if (ImportReferenceToken.Is(token, ImportReferenceToken.Staged))
				{
					var sourceId = ImportReferenceToken.Value(token, ImportReferenceToken.Staged);
					foreach (var sourceKey in slice.SourceKeysByEntity.TryGetValue(entityKey, out var keys) ? keys : new List<string>())
					{
						var entry = await ids.GetByIdentityAsync(slice.Run.DepartmentId, sourceKey, entityKey, sourceId, ct);
						if (entry != null && (entry.State == (int)ImportExternalIdState.Written || entry.State == (int)ImportExternalIdState.Linked))
						{
							key = entry.TargetKey;
							break;
						}
					}
				}
				else if (ImportReferenceToken.Is(token, ImportReferenceToken.CreateNew))
				{
					key = await EnsureImplicitAsync(slice, context, writers, ids, journal, entityKey, ImportReferenceToken.Value(token, ImportReferenceToken.CreateNew), ct);
				}

				if (key != null)
					memo[memoKey] = key;
				return key;
			};
		}

		private async Task<string> EnsureImplicitAsync(Slice slice, ImportWriteContext context, List<IImportTargetWriter> writers, IImportExternalIdsRepository ids,
			ImportJournalWriter journal, string entityKey, string name, CancellationToken cancellationToken)
		{
			var creator = writers.OfType<IImportImplicitCreator>().FirstOrDefault(c => string.Equals(c.EntityKey, entityKey, StringComparison.OrdinalIgnoreCase));
			var normalized = ImportFieldConverter.Normalize(name);
			if (creator == null || normalized.Length == 0)
				return null;

			var run = slice.Run;
			var sourceKey = "workspace:" + run.WorkspaceId;
			var sourceId = ImportReferenceToken.CreateNew + normalized;
			var claim = await ids.GetByIdentityAsync(run.DepartmentId, sourceKey, entityKey, sourceId, cancellationToken);
			if (claim != null && (claim.State == (int)ImportExternalIdState.Written || claim.State == (int)ImportExternalIdState.Linked))
				return claim.TargetKey;

			var now = DateTime.UtcNow;
			if (claim == null)
			{
				claim = NewClaim(slice, sourceKey, entityKey, sourceId, null, now);
				if (!await ids.TryClaimAsync(claim, cancellationToken))
					claim = await ids.GetByIdentityAsync(run.DepartmentId, sourceKey, entityKey, sourceId, cancellationToken) ?? claim;
			}

			var (target, created) = await creator.EnsureAsync(context, name, cancellationToken);
			if (target == null)
				return null;

			await ids.SetStateAsync(run.DepartmentId, claim.Id, created ? ImportExternalIdState.Written : ImportExternalIdState.Linked, target, run.Id, now, cancellationToken);
			await journal.AppendAsync(entityKey, target, created ? ImportJournalOperation.Create : ImportJournalOperation.Link, cancellationToken: cancellationToken);
			return target;
		}

		private async Task QuarantineAsync(ImportDataset dataset, ImportCanonicalRow row, string code, CancellationToken cancellationToken)
		{
			var now = DateTime.UtcNow;
			await _rows.SetOutcomeAsync(dataset.DepartmentId, row.StagedRowId, (int)ImportRowOutcome.Failed, null, now, cancellationToken);
			await _issues.InsertManyAsync(new List<ImportIssue>
			{
				OutcomeIssue(dataset, row, new ImportRowWriteResult { StagedRowId = row.StagedRowId, Outcome = ImportRowOutcome.Failed, Code = code })
			}, cancellationToken);
		}

		private static ImportIssue OutcomeIssue(ImportDataset dataset, ImportCanonicalRow row, ImportRowWriteResult result)
		{
			var issue = ImportIssueFactory.Create(dataset, null, null, null, result.Code, ImportLadderLevel.L10Reconciliation,
				result.Outcome == ImportRowOutcome.Failed ? ImportIssueSeverity.Blocking : ImportIssueSeverity.Info);
			issue.StagedRowId = row.StagedRowId;
			issue.RowNumber = row.RowNumber;
			issue.IdentityKey = ImportIssueFactory.IdentityKey(dataset.Id, row.RowNumber, "commit", result.Code);
			issue.NeedsDepartment = result.Outcome == ImportRowOutcome.Failed;
			return issue;
		}

		private async Task CompleteDatasetAsync(Slice slice, ImportDataset dataset, CancellationToken cancellationToken)
		{
			var inputs = await _rows.GetDigestInputsAsync(dataset.DepartmentId, dataset.Id, cancellationToken);
			var fresh = await _datasets.GetByIdAsync(dataset.DepartmentId, dataset.Id, cancellationToken) ?? dataset;
			fresh.CreatedCount = inputs.Count(r => r.Outcome == (int)ImportRowOutcome.Created);
			fresh.UpdatedCount = inputs.Count(r => r.Outcome == (int)ImportRowOutcome.Updated);
			fresh.LinkedCount = inputs.Count(r => r.Outcome == (int)ImportRowOutcome.Linked);
			fresh.UnchangedCount = inputs.Count(r => r.Outcome == (int)ImportRowOutcome.Unchanged);
			fresh.SkippedCount = inputs.Count(r => r.Outcome == (int)ImportRowOutcome.Skipped);
			fresh.FailedCount = inputs.Count(r => r.Outcome == (int)ImportRowOutcome.Failed);
			fresh.Status = (int)ImportDatasetStatus.Imported;
			fresh.UpdatedOn = DateTime.UtcNow;
			await _datasets.UpdateWithRevisionAsync(fresh, fresh.Revision, cancellationToken);

			slice.Counts.Entities[fresh.EntityKey] = new ImportEntityCounts
			{
				Planned = inputs.Count(r => r.Decision != (int)ImportRowDecision.Undecided && !r.Excluded),
				Created = fresh.CreatedCount,
				Updated = fresh.UpdatedCount,
				Linked = fresh.LinkedCount,
				Unchanged = fresh.UnchangedCount,
				Skipped = fresh.SkippedCount,
				Failed = fresh.FailedCount
			};
		}

		// ---- Deferred ---------------------------------------------------------------------------------------------------

		private async Task<ImportStepResult> DeferredPhaseAsync(Slice slice, CancellationToken cancellationToken)
		{
			var context = BaseContext(slice);
			foreach (var dataset in slice.Datasets)
			{
				if (slice.Cursor.CompletedEntities.Contains(dataset.Id))
					continue;
				if (!(Writer(_scope, dataset.EntityKey) is IImportDeferredWork))
				{
					slice.Cursor.CompletedEntities.Add(dataset.Id);
					continue;
				}

				var after = dataset.Id == slice.Cursor.DatasetId ? slice.Cursor.LastRowNumber : 0;
				while (true)
				{
					var control = await ControlAsync(slice, cancellationToken);
					if (control != null)
						return control;

					var page = await _validation.ReadCanonicalRowsAsync(dataset, after, DeferredPageSize, cancellationToken);
					if (page.Count == 0)
						break;
					after = page[page.Count - 1].RowNumber;

					var written = page.Where(r => r.TargetKey != null &&
						(r.Outcome == ImportRowOutcome.Created || r.Outcome == ImportRowOutcome.Updated || r.Outcome == ImportRowOutcome.Linked)).ToList();
					if (written.Count > 0)
						await DeferredInScopeAsync(slice, context, dataset, written, cancellationToken);

					slice.Cursor.DatasetId = dataset.Id;
					if (!await CheckpointAsync(slice, PhaseDeferred, dataset.Id, after, cancellationToken))
						return ImportStepResult.More();
				}

				slice.Cursor.CompletedEntities.Add(dataset.Id);
				if (!await CheckpointAsync(slice, PhaseDeferred, null, 0, cancellationToken))
					return ImportStepResult.More();
			}

			if (slice.Run.WriteMode == (int)ImportWriteMode.Replace)
				await RetirePhaseAsync(slice, context, cancellationToken);

			slice.Cursor.Phase = PhaseEffects;
			slice.Cursor.CompletedEntities.Clear();
			return null;
		}

		/// <summary>
		/// Replace (sections 5.2 and 12.4): after the rows, the records on the acknowledged retire list are retired, but only
		/// those still live and still unmatched by any row; each is journaled so rollback restores it. Records added since the
		/// approval are never on the list, and a resumed run finds the retired ones gone, so the step is idempotent.
		/// </summary>
		private async Task RetirePhaseAsync(Slice slice, ImportWriteContext baseContext, CancellationToken cancellationToken)
		{
			var run = slice.Run;
			foreach (var dataset in slice.Datasets)
			{
				var acknowledged = (await _issues.GetForDatasetAsync(run.DepartmentId, dataset.Id, cancellationToken))
					.FirstOrDefault(i => i.Code == "IMP-CONSEQUENCE-UNDECIDED" && i.FieldKey == ImportPlanService.ReplaceRetiresConsequence &&
						i.Status != (int)ImportIssueStatus.Open);
				var frozen = new HashSet<string>((ImportPlanService.RetireKeysOf(acknowledged) ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries),
					StringComparer.Ordinal);
				if (frozen.Count == 0)
					continue;

				var kept = new List<string>();
				var names = new HashSet<string>(StringComparer.Ordinal);
				var after = 0;
				while (true)
				{
					var page = await _validation.ReadCanonicalRowsAsync(dataset, after, DeferredPageSize, cancellationToken);
					if (page.Count == 0)
						break;
					after = page[page.Count - 1].RowNumber;
					kept.AddRange(page.Where(r => r.TargetKey != null).Select(r => r.TargetKey));
					foreach (var name in page.Select(r => ImportFieldConverter.Normalize(r.Text("name"))).Where(n => n.Length > 0))
						names.Add(name);
				}

				await using var scope = _scope.BeginLifetimeScope();
				var unitOfWork = scope.Resolve<IUnitOfWork>();
				await unitOfWork.CreateOrGetConnectionAsync(cancellationToken);
				var writer = scope.Resolve<IEnumerable<IImportTargetWriter>>().First(w => string.Equals(w.EntityKey, dataset.EntityKey, StringComparison.OrdinalIgnoreCase));
				if (!(writer is IImportReplaceableWriter replaceable) || !replaceable.SupportsReplace)
					continue;

				var failed = 0;
				try
				{
					var existing = await writer.FindExistingAsync(run.DepartmentId, cancellationToken);
					kept.AddRange(names.Where(existing.ContainsKey).Select(n => existing[n]));
					var journalRepository = scope.Resolve<IImportChangeJournalRepository>();
					var journal = new ImportJournalWriter((entry, ct) =>
					{
						entry.DepartmentId = run.DepartmentId;
						entry.WorkspaceId = run.WorkspaceId;
						entry.RunId = run.Id;
						entry.Sequence = slice.NextSequence++;
						entry.CreatedOn = DateTime.UtcNow;
						return journalRepository.InsertAsync(entry, ct, true);
					});
					var context = Copy(baseContext, dataset);

					foreach (var candidate in (await replaceable.GetRetireCandidatesAsync(context, kept, cancellationToken))
						.Where(c => c.CanRetire && frozen.Contains(c.TargetKey)))
					{
						try
						{
							if (await replaceable.RetireAsync(context, candidate.TargetKey, journal, cancellationToken) != null)
								failed++;
						}
						catch (InvalidOperationException)
						{
							// Taken into use since the preview: it stays.
							failed++;
						}
					}

					unitOfWork.CommitChanges();
				}
				catch
				{
					unitOfWork.DiscardChanges();
					throw;
				}
				finally
				{
					await _effects.RunChunkEffectsAsync(run.DepartmentId, writer.AffectedEffects);
				}

				if (failed > 0)
					await _issues.InsertManyAsync(new List<ImportIssue>
					{
						ImportIssueFactory.Create(dataset, null, null, null, "IMP-REPLACE-RETIRE-FAILED", ImportLadderLevel.L10Reconciliation, ImportIssueSeverity.Warning,
							new Dictionary<string, string> { ["count"] = failed.ToString(System.Globalization.CultureInfo.InvariantCulture) })
					}, cancellationToken);
			}
		}

		private async Task DeferredInScopeAsync(Slice slice, ImportWriteContext baseContext, ImportDataset dataset, List<ImportCanonicalRow> written, CancellationToken cancellationToken)
		{
			var run = slice.Run;
			await using var scope = _scope.BeginLifetimeScope();
			var unitOfWork = scope.Resolve<IUnitOfWork>();
			await unitOfWork.CreateOrGetConnectionAsync(cancellationToken);
			try
			{
				var writers = scope.Resolve<IEnumerable<IImportTargetWriter>>().ToList();
				var writer = writers.First(w => string.Equals(w.EntityKey, dataset.EntityKey, StringComparison.OrdinalIgnoreCase));
				var ids = scope.Resolve<IImportExternalIdsRepository>();
				var journalRepository = scope.Resolve<IImportChangeJournalRepository>();
				var journal = new ImportJournalWriter((entry, ct) =>
				{
					entry.DepartmentId = run.DepartmentId;
					entry.WorkspaceId = run.WorkspaceId;
					entry.RunId = run.Id;
					entry.Sequence = slice.NextSequence++;
					entry.CreatedOn = DateTime.UtcNow;
					return journalRepository.InsertAsync(entry, ct, true);
				});
				var context = Copy(baseContext, dataset);
				context.ResolveReference = ReferenceResolver(slice, context, writers, ids, journal);

				await ((IImportDeferredWork)writer).CompleteDeferredAsync(context, written, journal, cancellationToken);
				unitOfWork.CommitChanges();
			}
			catch (Exception ex) when (!IsTransient(ex) && !(ex is OperationCanceledException))
			{
				// Deferred work is an improvement on rows already written; a failure is reported, never fatal to the run.
				unitOfWork.DiscardChanges();
				Logging.LogException(ex, $"Department import run {run.Id}: deferred work for dataset {dataset.Id} failed", run.CorrelationId);
				await _issues.InsertManyAsync(new List<ImportIssue>
				{
					ImportIssueFactory.Create(dataset, null, null, null, "IMP-DEFERRED-FAILED", ImportLadderLevel.L10Reconciliation, ImportIssueSeverity.Warning)
				}, cancellationToken);
			}
			catch
			{
				unitOfWork.DiscardChanges();
				throw;
			}
			finally
			{
				await _effects.RunChunkEffectsAsync(run.DepartmentId, Writer(_scope, dataset.EntityKey).AffectedEffects);
			}
		}

		// ---- Effects and reconcile -------------------------------------------------------------------------------------

		private async Task EffectsPhaseAsync(Slice slice, CancellationToken cancellationToken)
		{
			var run = slice.Run;
			var touched = slice.Datasets.Select(d => Writer(_scope, d.EntityKey)?.AffectedEffects ?? ImportEffects.None)
				.Aggregate(ImportEffects.None, (all, e) => all | e);
			await _effects.RunRunEffectsAsync(run.DepartmentId, touched, cancellationToken);

			foreach (var (entity, counts) in slice.Counts.Entities)
				_effects.Audit(run.DepartmentId, slice.Authority.OnBehalfOfUserId, AuditLogTypes.DepartmentImportCommitted,
					JsonConvert.SerializeObject(new { runId = run.Id, workspaceId = run.WorkspaceId, entity, counts }));

			await _timeline.RecordAsync(run.DepartmentId, run.WorkspaceId, ImportActorKind.System, slice.Lease.Owner, "CommitWritten",
				new { runId = run.Id, entities = slice.Counts.Entities }, run.CorrelationId, cancellationToken);
		}

		private async Task ReconcileAsync(Slice slice, CancellationToken cancellationToken)
		{
			var run = slice.Run;
			var context = BaseContext(slice);
			var written = await _externalIds.GetForRunAsync(run.DepartmentId, run.Id, cancellationToken);
			await _reconciliations.DeleteForRunAsync(run.DepartmentId, run.Id, cancellationToken);

			var unexplained = 0;
			var checks = new List<ImportReconciliation>();
			foreach (var dataset in slice.Datasets)
			{
				var writer = Writer(_scope, dataset.EntityKey);
				var entityWritten = written.Where(w => string.Equals(w.EntityType, dataset.EntityKey, StringComparison.OrdinalIgnoreCase) &&
					(w.State == (int)ImportExternalIdState.Written || w.State == (int)ImportExternalIdState.Linked)).ToList();
				var verify = await writer.VerifyAsync(Copy(context, dataset), entityWritten, cancellationToken);

				// The staged outcomes and the external-id map tell the same story.
				var inputs = await _rows.GetDigestInputsAsync(run.DepartmentId, dataset.Id, cancellationToken);
				var outcomes = inputs.Count(r => r.Outcome == (int)ImportRowOutcome.Created || r.Outcome == (int)ImportRowOutcome.Updated || r.Outcome == (int)ImportRowOutcome.Linked);
				var mapped = inputs.Where(r => r.Outcome == (int)ImportRowOutcome.Created || r.Outcome == (int)ImportRowOutcome.Updated || r.Outcome == (int)ImportRowOutcome.Linked)
					.Count(r => r.TargetKey != null);
				verify.Checks[dataset.EntityKey + ".outcomes-mapped"] = (outcomes, mapped);

				var datasetMismatch = false;
				foreach (var (checkId, (expected, actual)) in verify.Checks)
				{
					var matched = expected == actual;
					datasetMismatch |= !matched;
					checks.Add(new ImportReconciliation
					{
						DepartmentId = run.DepartmentId,
						WorkspaceId = run.WorkspaceId,
						RunId = run.Id,
						CheckId = checkId,
						EntityKey = dataset.EntityKey,
						Expected = expected,
						Actual = actual,
						State = (int)(matched ? ImportReconciliationState.Matched : ImportReconciliationState.Unexplained),
						CreatedOn = DateTime.UtcNow
					});
				}

				var fresh = await _datasets.GetByIdAsync(run.DepartmentId, dataset.Id, cancellationToken) ?? dataset;
				if (datasetMismatch)
				{
					unexplained++;
					await _issues.InsertManyAsync(new List<ImportIssue>
					{
						ImportIssueFactory.Create(fresh, null, null, null, "IMP-RECONCILE-MISMATCH", ImportLadderLevel.L10Reconciliation, ImportIssueSeverity.Warning)
					}, cancellationToken);
				}
				else if (fresh.Status == (int)ImportDatasetStatus.Imported)
				{
					fresh.Status = (int)ImportDatasetStatus.Verified;
					fresh.UpdatedOn = DateTime.UtcNow;
					await _datasets.UpdateWithRevisionAsync(fresh, fresh.Revision, cancellationToken);
				}
			}

			// The configuration the run wrote still reads back as written (section 7.5).
			var (configurationExpected, configurationActual) = await _configuration.VerifyAsync(run, cancellationToken);
			if (configurationExpected > 0)
			{
				var matched = configurationExpected == configurationActual;
				if (!matched)
					unexplained++;
				checks.Add(new ImportReconciliation
				{
					DepartmentId = run.DepartmentId,
					WorkspaceId = run.WorkspaceId,
					RunId = run.Id,
					CheckId = ImportConfigurationCatalog.JournalEntityType + ".applied",
					EntityKey = ImportConfigurationCatalog.JournalEntityType,
					Expected = configurationExpected,
					Actual = configurationActual,
					State = (int)(matched ? ImportReconciliationState.Matched : ImportReconciliationState.Unexplained),
					CreatedOn = DateTime.UtcNow
				});
			}

			await _reconciliations.InsertManyAsync(checks, cancellationToken);
			await _effects.ClearDepartmentCachesAsync(run.DepartmentId);
			await ReverifySetupReportAsync(run.DepartmentId, cancellationToken);
			await ShortenArtifactRetentionAsync(run.DepartmentId, slice.Datasets, cancellationToken);

			run.CompletedOn = DateTime.UtcNow;
			await _timeline.RecordAsync(run.DepartmentId, run.WorkspaceId, ImportActorKind.System, slice.Lease.Owner, "CommitReconciled",
				new { runId = run.Id, checks = checks.Count, unexplained }, run.CorrelationId, cancellationToken);
		}

		/// <summary>
		/// Once a commit is reconciled, the uploaded files behind its data sets are kept only for the success window
		/// (DepartmentImportConfig: 7 days by default, 72 hours with Advanced Data Protection), then worker 48 purges them.
		/// A deadline that is already earlier is left alone.
		/// </summary>
		private async Task ShortenArtifactRetentionAsync(int departmentId, IReadOnlyCollection<ImportDataset> datasets, CancellationToken cancellationToken)
		{
			var artifactIds = datasets.Select(d => d.ArtifactId).Where(id => !string.IsNullOrWhiteSpace(id)).ToHashSet(StringComparer.OrdinalIgnoreCase);
			if (artifactIds.Count == 0)
				return;

			var config = await _settings.GetDepartmentImportConfigAsync(departmentId);
			var deadline = DateTime.UtcNow.Add(config.ArtifactRetentionAfterSuccess(await _protection.IsProtectionEnforcedAsync(departmentId)));
			foreach (var id in artifactIds)
			{
				var artifact = await _artifacts.GetByIdAsync(departmentId, id, cancellationToken);
				if (artifact == null || artifact.PurgedOn.HasValue || artifact.RetentionDeadline.HasValue && artifact.RetentionDeadline.Value <= deadline)
					continue;

				artifact.RetentionDeadline = deadline;
				artifact.UpdatedOn = DateTime.UtcNow;
				await _artifacts.UpdateAsync(artifact, cancellationToken, true);
			}
		}

		/// <summary>Admin Assist's Setup Report reflects the import at once (the call worker 72 makes; best effort).</summary>
		private async Task ReverifySetupReportAsync(int departmentId, CancellationToken cancellationToken)
		{
			try
			{
				var admin = (await _departments.GetActiveAdminsForDepartmentAsync(departmentId))?.OrderBy(a => a.UserId, StringComparer.Ordinal).FirstOrDefault();
				if (admin != null)
					await _worklist.Value.VerifyAsync(new AdminAssistActor(departmentId, admin.UserId), cancellationToken);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
			}
		}

		// ---- Shared -----------------------------------------------------------------------------------------------------

		/// <summary>Stops at a pause or cancel request and at the slice budget; null to keep going.</summary>
		private async Task<ImportStepResult> ControlAsync(Slice slice, CancellationToken cancellationToken)
		{
			var stored = await _runs.GetByIdAsync(slice.Run.DepartmentId, slice.Run.Id, cancellationToken);
			if (stored != null && stored.CancelRequested != (int)ImportRunCancelRequest.None)
			{
				slice.Run.CancelRequested = stored.CancelRequested;
				// Paused or cancelled at a chunk boundary: everything written stays journaled (rollback stays possible).
				await ReleaseLockAsync(slice, DepartmentOperationLockReleaseKind.Checkpoint, cancellationToken);
				await SaveAsync(slice, cancellationToken);
				return ImportStepResult.More();
			}

			if (DateTime.UtcNow >= slice.Deadline || !await slice.Lease.Heartbeat(cancellationToken))
				return ImportStepResult.More();

			return null;
		}

		private async Task<bool> CheckpointAsync(Slice slice, int phase, string datasetId, int lastRow, CancellationToken cancellationToken)
		{
			slice.Cursor.Phase = phase;
			slice.Cursor.DatasetId = datasetId;
			slice.Cursor.LastRowNumber = lastRow;
			slice.Cursor.JournalSequence = slice.NextSequence;
			return await SaveAsync(slice, cancellationToken);
		}

		private Task<bool> SaveAsync(Slice slice, CancellationToken cancellationToken)
		{
			slice.Run.Cursor = JsonConvert.SerializeObject(slice.Cursor);
			slice.Run.Counts = JsonConvert.SerializeObject(slice.Counts);
			slice.Run.CursorMovedOn = DateTime.UtcNow;
			return slice.Lease.Save(cancellationToken);
		}

		private async Task<ImportStepResult> PauseWithIssueAsync(Slice slice, string code, CancellationToken cancellationToken)
		{
			var dataset = slice.Datasets.FirstOrDefault();
			if (dataset != null)
			{
				var issue = ImportIssueFactory.Create(dataset, null, null, null, code, ImportLadderLevel.L9ApprovalIntegrity, ImportIssueSeverity.Blocking);
				issue.IdentityKey = ImportIssueFactory.IdentityKey("run:" + slice.Run.Id, null, null, code);
				var existing = await _issues.GetForDatasetAsync(dataset.DepartmentId, dataset.Id, cancellationToken);
				if (existing.All(i => i.IdentityKey != issue.IdentityKey))
					await _issues.InsertManyAsync(new List<ImportIssue> { issue }, cancellationToken);
			}

			await ReleaseLockAsync(slice, DepartmentOperationLockReleaseKind.Aborted, cancellationToken);
			return ImportStepResult.Paused(code);
		}

		private ImportWriteContext BaseContext(Slice slice) => new ImportWriteContext
		{
			DepartmentId = slice.Run.DepartmentId,
			Department = slice.Department,
			Run = slice.Run,
			Workspace = slice.Workspace,
			Authority = slice.Authority,
			Effects = ImportEffectPolicy.Import,
			WriteMode = (ImportWriteMode)slice.Run.WriteMode,
			Consequences = slice.Consequences,
			Now = DateTime.UtcNow,
			PersonnelCapacity = slice.Counts.PersonnelCapacityRemaining,
			UnitCapacity = slice.Counts.UnitCapacityRemaining,
			SharedCapacity = slice.Counts.SharedCapacity
		};

		private static ImportWriteContext Copy(ImportWriteContext source, ImportDataset dataset) => new ImportWriteContext
		{
			DepartmentId = source.DepartmentId,
			Department = source.Department,
			Run = source.Run,
			Workspace = source.Workspace,
			Authority = source.Authority,
			Effects = source.Effects,
			WriteMode = source.WriteMode,
			Consequences = source.Consequences,
			Now = DateTime.UtcNow,
			PersonnelCapacity = source.PersonnelCapacity,
			UnitCapacity = source.UnitCapacity,
			SharedCapacity = source.SharedCapacity,
			Dataset = dataset,
			SourceSystemKey = source.SourceSystemKey
		};

		private static IImportTargetWriter Writer(IComponentContext scope, string entityKey) =>
			scope.Resolve<IEnumerable<IImportTargetWriter>>().FirstOrDefault(w => string.Equals(w.EntityKey, entityKey, StringComparison.OrdinalIgnoreCase));

		private static string RowKey(ImportCanonicalRow row) => row.SourceKey ?? $"row:{row.DatasetId}:{row.RowNumber}";

		private static ImportExternalId NewClaim(Slice slice, string sourceKey, string entityKey, string sourceId, string targetKey, DateTime now) => new ImportExternalId
		{
			DepartmentId = slice.Run.DepartmentId,
			WorkspaceId = slice.Run.WorkspaceId,
			SourceSystemKey = sourceKey,
			EntityType = entityKey,
			SourceId = sourceId,
			TargetKey = targetKey,
			State = (int)ImportExternalIdState.Claimed,
			RunId = slice.Run.Id,
			CreatedOn = now,
			UpdatedOn = now
		};

		public static bool IsTransient(Exception ex) =>
			ex is TimeoutException || ex is DbException db && db.IsTransient || ex.InnerException != null && IsTransient(ex.InnerException);

		/// <summary>The run's datasets in dependency order (the catalog's topological order, then wave).</summary>
		private async Task<List<ImportDataset>> RunDatasetsAsync(Slice slice, CancellationToken cancellationToken)
		{
			var ids = string.IsNullOrWhiteSpace(slice.Run.DatasetIds) ? new List<string>() : JsonConvert.DeserializeObject<List<string>>(slice.Run.DatasetIds);
			var order = slice.Catalog.InDependencyOrder().Select((c, i) => (c.Key, i)).ToDictionary(x => x.Key, x => x.i, StringComparer.OrdinalIgnoreCase);
			var datasets = new List<ImportDataset>();
			foreach (var id in ids.Distinct())
			{
				var dataset = await _datasets.GetByIdAsync(slice.Run.DepartmentId, id, cancellationToken);
				if (dataset != null && dataset.EntityKey != null && Writer(_scope, dataset.EntityKey) != null)
					datasets.Add(dataset);
			}

			return datasets.OrderBy(d => order.TryGetValue(d.EntityKey, out var i) ? i : int.MaxValue).ThenBy(d => d.CreatedOn).ThenBy(d => d.Id, StringComparer.Ordinal).ToList();
		}

		private async Task LoadSourceKeysAsync(Slice slice, CancellationToken cancellationToken)
		{
			slice.SourceKeyByDataset = new Dictionary<string, string>(StringComparer.Ordinal);
			slice.SourceKeysByEntity = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
			foreach (var dataset in (await _datasets.GetAllForWorkspaceAsync(slice.Run.DepartmentId, slice.Run.WorkspaceId, cancellationToken))
				.Where(d => d.EntityKey != null && (DepartmentImportStateMachine.IsLive(d) || slice.Datasets.Any(r => r.Id == d.Id))))
			{
				var key = await _plan.SourceSystemKeyAsync(dataset, cancellationToken);
				slice.SourceKeyByDataset[dataset.Id] = key;
				if (!slice.SourceKeysByEntity.TryGetValue(dataset.EntityKey, out var keys))
					slice.SourceKeysByEntity[dataset.EntityKey] = keys = new List<string>();
				if (!keys.Contains(key))
					keys.Add(key);
			}

			// "Create new" records live under the workspace's own instance key.
			foreach (var keys in slice.SourceKeysByEntity.Values)
				if (!keys.Contains("workspace:" + slice.Run.WorkspaceId))
					keys.Add("workspace:" + slice.Run.WorkspaceId);
		}

		private async Task RefreshWorkspaceAsync(Slice slice, CancellationToken cancellationToken)
		{
			var workspace = await _workspaces.GetByIdAsync(slice.Run.DepartmentId, slice.Run.WorkspaceId, cancellationToken);
			if (workspace == null)
				return;
			var datasets = await _datasets.GetAllForWorkspaceAsync(workspace.DepartmentId, workspace.Id, cancellationToken);
			var runs = (await _runs.GetAllForWorkspaceAsync(workspace.DepartmentId, workspace.Id, cancellationToken))
				.Select(r => r.Id == slice.Run.Id ? slice.Run : r).ToList();
			var status = DepartmentImportStateMachine.Derive(workspace, datasets, runs);
			if (status == (ImportWorkspaceStatus)workspace.Status)
				return;
			workspace.Status = (int)status;
			workspace.UpdatedOn = DateTime.UtcNow;
			workspace.LastActivityOn = DateTime.UtcNow;
			await _workspaces.UpdateWithRevisionAsync(workspace, workspace.Revision, cancellationToken);
		}
	}
}
