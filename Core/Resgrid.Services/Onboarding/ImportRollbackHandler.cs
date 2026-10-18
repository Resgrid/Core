using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Onboarding;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;

namespace Resgrid.Services.Onboarding
{
	/// <summary>
	/// Executes Rollback runs (wizard plan section 11.8): a commit run's journal replayed backwards, under the same
	/// department lock and leasing. Each journal entry is undone in its own unit-of-work transaction (some owning delete
	/// paths commit the unit of work themselves) and marked with its rollback outcome in that transaction, so a resumed
	/// run continues with the first entry not yet marked. What cannot be undone safely (later activity, a field changed
	/// since) becomes a manual-reconciliation item. Afterwards the datasets return to Ready and are re-planned.
	/// </summary>
	public class ImportRollbackHandler : IImportRunHandler
	{
		private const int PhaseEntries = 1;
		private const int PhaseFinish = 2;
		private static readonly TimeSpan LockLease = TimeSpan.FromMinutes(10);

		private readonly ILifetimeScope _scope;
		private readonly IImportWorkspacesRepository _workspaces;
		private readonly IImportDatasetsRepository _datasets;
		private readonly IImportRunsRepository _runs;
		private readonly IImportExternalIdsRepository _externalIds;
		private readonly IImportStagedRowsRepository _rows;
		private readonly IImportChangeJournalRepository _journal;
		private readonly IImportReconciliationsRepository _reconciliations;
		private readonly IDepartmentImportAccessService _access;
		private readonly IDepartmentLockService _locks;
		private readonly IDepartmentsService _departments;
		private readonly IImportTimelineService _timeline;
		private readonly IImportRunQueue _queue;
		private readonly ImportEffectsRunner _effects;
		private readonly IImportSupportGrantsRepository _grants;

		public ImportRollbackHandler(ILifetimeScope scope, IImportWorkspacesRepository workspaces, IImportDatasetsRepository datasets, IImportRunsRepository runs,
			IImportExternalIdsRepository externalIds, IImportStagedRowsRepository rows, IImportChangeJournalRepository journal,
			IImportReconciliationsRepository reconciliations, IDepartmentImportAccessService access, IDepartmentLockService locks, IDepartmentsService departments,
			IImportTimelineService timeline, IImportRunQueue queue, ImportEffectsRunner effects, IImportSupportGrantsRepository grants)
		{
			_grants = grants;
			_scope = scope;
			_workspaces = workspaces;
			_datasets = datasets;
			_runs = runs;
			_externalIds = externalIds;
			_rows = rows;
			_journal = journal;
			_reconciliations = reconciliations;
			_access = access;
			_locks = locks;
			_departments = departments;
			_timeline = timeline;
			_queue = queue;
			_effects = effects;
		}

		public ImportRunKind Kind => ImportRunKind.Rollback;

		public async Task<ImportStepResult> ExecuteAsync(ImportRun run, ImportRunLease lease, DateTime deadline, CancellationToken cancellationToken)
		{
			var cursor = string.IsNullOrWhiteSpace(run.Cursor) ? new ImportRunCursor() : JsonConvert.DeserializeObject<ImportRunCursor>(run.Cursor);
			var commit = string.IsNullOrWhiteSpace(run.ParentRunId) ? null : await _runs.GetByIdAsync(run.DepartmentId, run.ParentRunId, cancellationToken);
			if (commit == null || commit.Kind != (int)ImportRunKind.Commit)
				return ImportStepResult.Failed("IMP-ROLLBACK-NO-COMMIT");

			var authority = string.IsNullOrWhiteSpace(run.Authority) ? ImportAuthority.ForAdministrator(run.DepartmentId, run.RequestedBy)
				: JsonConvert.DeserializeObject<ImportAuthority>(run.Authority);
			if (!await _access.IsEnabledAsync(run.DepartmentId))
				return ImportStepResult.Paused("IMP-ENTITLEMENT-MISSING");
			if (!await _access.CanManageAsync(run.DepartmentId, authority.OnBehalfOfUserId))
				return ImportStepResult.Paused("IMP-APPROVER-NOT-AUTHORIZED");
			if (authority.IsStaff && !await ImportCommitHandler.StaffGrantAllowsAsync(_grants, run, authority, ImportOperateActions.Rollback))
				return ImportStepResult.Paused("IMP-APPROVER-NOT-AUTHORIZED");

			// The same lock as the commit: nothing else writes the department in bulk while it is undone.
			var until = DateTime.UtcNow.Add(LockLease);
			if (!(run.DepartmentLockId.HasValue && await _locks.HeartbeatAsync(run.DepartmentLockId.Value, until, cancellationToken)))
			{
				var acquired = await _locks.ApplyLockAsync(run.DepartmentId, DepartmentOperationLockType.DepartmentImport, "Department import rollback",
					run.CorrelationId, "import:" + run.Id, until, null, cancellationToken);
				if (acquired == null)
					return ImportStepResult.Transient("IMP-DEPARTMENT-LOCKED");
				run.DepartmentLockId = acquired.DepartmentOperationLockId;
			}

			var context = new ImportWriteContext
			{
				DepartmentId = run.DepartmentId,
				Department = await _departments.GetDepartmentByIdAsync(run.DepartmentId, false),
				Run = run,
				Workspace = await _workspaces.GetByIdAsync(run.DepartmentId, run.WorkspaceId, cancellationToken),
				Authority = authority,
				Effects = ImportEffectPolicy.Import,
				WriteMode = (ImportWriteMode)commit.WriteMode,
				Now = DateTime.UtcNow
			};

			if (cursor.Phase <= PhaseEntries)
			{
				run.StartedOn ??= DateTime.UtcNow;
				var entries = (await _journal.GetForRunDescendingAsync(run.DepartmentId, commit.Id, cancellationToken))
					.Where(e => e.RollbackOutcome == (int)ImportRollbackOutcome.None && e.RolledBackOn == null).ToList();
				var released = (await _externalIds.GetForRunAsync(run.DepartmentId, commit.Id, cancellationToken))
					.Where(e => e.State == (int)ImportExternalIdState.Written || e.State == (int)ImportExternalIdState.Linked)
					.GroupBy(e => e.EntityType + "|" + e.TargetKey, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
				var touched = ImportEffects.None;

				foreach (var entry in entries)
				{
					var stored = await _runs.GetByIdAsync(run.DepartmentId, run.Id, cancellationToken);
					if (stored != null && stored.CancelRequested != (int)ImportRunCancelRequest.None)
					{
						run.CancelRequested = stored.CancelRequested;
						break;
					}
					if (DateTime.UtcNow >= deadline || !await lease.Heartbeat(cancellationToken))
						break;

					var (outcome, effects) = await UndoAsync(run, context, entry, cancellationToken);
					touched |= effects;

					// The identity is free again once its record is gone: a later commit writes it anew.
					if ((outcome == ImportRollbackOutcome.Deleted || outcome == ImportRollbackOutcome.SoftDeleted || outcome == ImportRollbackOutcome.MembershipRemoved) &&
						released.TryGetValue(entry.EntityType + "|" + entry.TargetKey, out var claims))
						foreach (var claim in claims)
							await _externalIds.SetStateAsync(run.DepartmentId, claim.Id, ImportExternalIdState.Released, claim.TargetKey, run.Id, DateTime.UtcNow, cancellationToken);
				}

				await _effects.RunChunkEffectsAsync(run.DepartmentId, touched);
				var remaining = (await _journal.GetForRunDescendingAsync(run.DepartmentId, commit.Id, cancellationToken))
					.Count(e => e.RollbackOutcome == (int)ImportRollbackOutcome.None && e.RolledBackOn == null);
				cursor.Phase = remaining == 0 ? PhaseFinish : PhaseEntries;
				run.Cursor = JsonConvert.SerializeObject(cursor);
				run.CursorMovedOn = DateTime.UtcNow;
				if (!await lease.Save(cancellationToken) || remaining > 0 || run.CancelRequested != (int)ImportRunCancelRequest.None)
					return await StopAsync(run, cancellationToken);
			}

			await FinishAsync(run, commit, cancellationToken);
			return ImportStepResult.Done();
		}

		/// <summary>Undoes one journal entry in its own transaction and marks it there.</summary>
		private async Task<(ImportRollbackOutcome, ImportEffects)> UndoAsync(ImportRun run, ImportWriteContext context, ImportChangeJournalEntry entry,
			CancellationToken cancellationToken)
		{
			await using var scope = _scope.BeginLifetimeScope();
			if (string.Equals(entry.EntityType, ImportConfigurationCatalog.JournalEntityType, StringComparison.Ordinal))
				return await UndoConfigurationAsync(run, context, entry, scope, cancellationToken);

			var unitOfWork = scope.Resolve<IUnitOfWork>();
			var writer = scope.Resolve<IEnumerable<IImportTargetWriter>>()
				.FirstOrDefault(w => string.Equals(w.EntityKey, entry.EntityType, StringComparison.OrdinalIgnoreCase));
			var journal = scope.Resolve<IImportChangeJournalRepository>();
			var now = DateTime.UtcNow;

			if (writer == null)
			{
				await journal.MarkRolledBackAsync(run.DepartmentId, entry.Id, ImportRollbackOutcome.ManualReconciliation, run.Id, now, cancellationToken);
				await RecordManualAsync(run, entry, "IMP-ROLLBACK-NO-WRITER", cancellationToken);
				return (ImportRollbackOutcome.ManualReconciliation, ImportEffects.None);
			}

			await unitOfWork.CreateOrGetConnectionAsync(cancellationToken);
			ImportRollbackEntryResult result;
			try
			{
				result = await writer.RollbackAsync(context, entry, cancellationToken);
				await journal.MarkRolledBackAsync(run.DepartmentId, entry.Id, result.Outcome, run.Id, DateTime.UtcNow, cancellationToken);
				// A delete path that committed the unit of work itself already ended the transaction; this commits the rest.
				unitOfWork.CommitChanges();
			}
			catch (Exception ex) when (!ImportCommitHandler.IsTransient(ex) && !(ex is OperationCanceledException))
			{
				unitOfWork.DiscardChanges();
				Logging.LogException(ex, $"Department import rollback {run.Id}: entry {entry.Id} needs manual reconciliation", run.CorrelationId);
				result = new ImportRollbackEntryResult { JournalEntryId = entry.Id, Outcome = ImportRollbackOutcome.ManualReconciliation, Code = "IMP-ROLLBACK-FAILED" };
				await _journal.MarkRolledBackAsync(run.DepartmentId, entry.Id, result.Outcome, run.Id, DateTime.UtcNow, cancellationToken);
			}
			catch
			{
				unitOfWork.DiscardChanges();
				throw;
			}

			if (result.Outcome == ImportRollbackOutcome.ManualReconciliation)
				await RecordManualAsync(run, entry, result.Code ?? "IMP-ROLLBACK-MANUAL", cancellationToken);

			return (result.Outcome, writer.AffectedEffects);
		}

		/// <summary>
		/// A configuration entry (section 7.5): restored through the owning setter while the setting still reads as the
		/// commit wrote it, outside any transaction (module switches commit their own). Its effects run with it.
		/// </summary>
		private async Task<(ImportRollbackOutcome, ImportEffects)> UndoConfigurationAsync(ImportRun run, ImportWriteContext context, ImportChangeJournalEntry entry,
			ILifetimeScope scope, CancellationToken cancellationToken)
		{
			var journal = scope.Resolve<IImportChangeJournalRepository>();
			ImportRollbackEntryResult result;
			try
			{
				result = await scope.Resolve<IImportConfigurationService>().RollbackAsync(run, context.Authority.OnBehalfOfUserId, entry, cancellationToken);
			}
			catch (Exception ex) when (!ImportCommitHandler.IsTransient(ex) && !(ex is OperationCanceledException))
			{
				Logging.LogException(ex, $"Department import rollback {run.Id}: configuration entry {entry.Id} needs manual reconciliation", run.CorrelationId);
				result = new ImportRollbackEntryResult { JournalEntryId = entry.Id, Outcome = ImportRollbackOutcome.ManualReconciliation, Code = "IMP-ROLLBACK-FAILED" };
			}

			await journal.MarkRolledBackAsync(run.DepartmentId, entry.Id, result.Outcome, run.Id, DateTime.UtcNow, cancellationToken);
			if (result.Outcome == ImportRollbackOutcome.ManualReconciliation)
				await RecordManualAsync(run, entry, result.Code ?? "IMP-ROLLBACK-MANUAL", cancellationToken);
			return (result.Outcome, ImportEffects.None);
		}

		private Task RecordManualAsync(ImportRun run, ImportChangeJournalEntry entry, string code, CancellationToken cancellationToken) =>
			_reconciliations.InsertManyAsync(new List<ImportReconciliation>
			{
				new ImportReconciliation
				{
					DepartmentId = run.DepartmentId,
					WorkspaceId = run.WorkspaceId,
					RunId = run.Id,
					CheckId = "rollback:" + entry.Id,
					EntityKey = entry.EntityType,
					Expected = 1,
					Actual = 0,
					State = (int)ImportReconciliationState.Unexplained,
					ExplanationKey = code,
					CreatedOn = DateTime.UtcNow
				}
			}, cancellationToken);

		private async Task<ImportStepResult> StopAsync(ImportRun run, CancellationToken cancellationToken)
		{
			if (run.CancelRequested != (int)ImportRunCancelRequest.None && run.DepartmentLockId.HasValue)
			{
				await _locks.ReleaseLockAsync(run.DepartmentLockId.Value, DepartmentOperationLockReleaseKind.Checkpoint, "import:" + run.Id, cancellationToken);
				run.DepartmentLockId = null;
			}
			return ImportStepResult.More();
		}

		private async Task FinishAsync(ImportRun run, ImportRun commit, CancellationToken cancellationToken)
		{
			var datasetIds = string.IsNullOrWhiteSpace(commit.DatasetIds) ? new List<string>() : JsonConvert.DeserializeObject<List<string>>(commit.DatasetIds);
			var now = DateTime.UtcNow;
			var touched = ImportEffects.AppsResync | ImportEffects.SearchRebuild;

			foreach (var id in datasetIds)
			{
				var dataset = await _datasets.GetByIdAsync(run.DepartmentId, id, cancellationToken);
				if (dataset == null)
					continue;

				await _rows.ResetOutcomesAsync(run.DepartmentId, dataset.Id, now, cancellationToken);
				dataset.Status = (int)ImportDatasetStatus.Ready;
				dataset.ApprovedRunId = null;
				dataset.CreatedCount = dataset.UpdatedCount = dataset.LinkedCount = dataset.UnchangedCount = dataset.SkippedCount = dataset.FailedCount = 0;
				dataset.UpdatedOn = now;
				await _datasets.UpdateWithRevisionAsync(dataset, dataset.Revision, cancellationToken);
			}

			await _effects.RunRunEffectsAsync(run.DepartmentId, touched, cancellationToken);

			var entries = await _journal.GetForRunDescendingAsync(run.DepartmentId, commit.Id, cancellationToken);
			var summary = entries.GroupBy(e => ((ImportRollbackOutcome)e.RollbackOutcome).ToString()).ToDictionary(g => g.Key, g => g.Count());
			_effects.Audit(run.DepartmentId, run.RequestedBy, AuditLogTypes.DepartmentImportRolledBack,
				JsonConvert.SerializeObject(new { runId = run.Id, commitRunId = commit.Id, workspaceId = run.WorkspaceId, outcomes = summary }));

			if (run.DepartmentLockId.HasValue)
			{
				await _locks.ReleaseLockAsync(run.DepartmentLockId.Value, DepartmentOperationLockReleaseKind.Completed, "import:" + run.Id, cancellationToken);
				run.DepartmentLockId = null;
			}

			run.CompletedOn = DateTime.UtcNow;
			await _timeline.RecordAsync(run.DepartmentId, run.WorkspaceId, ImportActorKind.System, "worker47", "RollbackCompleted",
				new { runId = run.Id, commitRunId = commit.Id, outcomes = summary }, run.CorrelationId, cancellationToken);

			// The datasets are plannable again: the external-id map changed, so L7 decisions are re-derived.
			if (datasetIds.Count > 0)
				await _queue.QueueAsync(new ImportRun
				{
					DepartmentId = run.DepartmentId,
					WorkspaceId = run.WorkspaceId,
					Kind = (int)ImportRunKind.Analysis,
					DatasetIds = JsonConvert.SerializeObject(datasetIds),
					RequestedBy = run.RequestedBy,
					CorrelationId = run.CorrelationId
				}, cancellationToken);
		}
	}
}
