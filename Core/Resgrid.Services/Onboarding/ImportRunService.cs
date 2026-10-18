using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model.Onboarding;
using Resgrid.Model.Queue;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.Onboarding
{
	/// <summary>
	/// Inserts Queued runs and publishes the start nudge (wizard plan section 10.4). The row is the durable source: a lost
	/// or failed nudge only delays the start until worker 47's minute sweep claims the run.
	/// </summary>
	public class ImportRunQueue : IImportRunQueue
	{
		private readonly IImportRunsRepository _runs;
		private readonly IQueueService _queue;

		public ImportRunQueue(IImportRunsRepository runs, IQueueService queue)
		{
			_runs = runs;
			_queue = queue;
		}

		public async Task<ImportRun> QueueAsync(ImportRun run, CancellationToken cancellationToken = default)
		{
			var now = DateTime.UtcNow;
			run.Status = (int)ImportRunStatus.Queued;
			run.RequestedOn = run.RequestedOn == default ? now : run.RequestedOn;
			run.UpdatedOn = now;
			run.CorrelationId ??= Guid.NewGuid().ToString("N");
			if (run.WriteMode == 0)
				run.WriteMode = (int)ImportWriteMode.CreateOnly;

			await _runs.InsertAsync(run, cancellationToken, true);
			await NudgeAsync(run);
			return run;
		}

		public async Task NudgeAsync(ImportRun run)
		{
			try
			{
				await _queue.EnqueueDepartmentImportAsync(new DepartmentImportQueueItem
				{
					DepartmentId = run.DepartmentId,
					RunId = run.Id,
					QueuedOnUtc = DateTime.UtcNow
				});
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Department import nudge for run {run.Id} failed; worker 47 will sweep it", run.CorrelationId);
			}
		}
	}

	/// <summary>
	/// Leases and executes runs (wizard plan sections 10.4, 11.4 and 13). A claim holds a 2-minute lease renewed by a
	/// 30-second heartbeat and does at most a budget's worth of work before checkpointing and releasing, so departments
	/// are served round-robin. Handlers persist their cursor; nothing crosses a slice boundary in memory, so a crash at
	/// any point resumes from the last saved state. Transient failures retry with backoff (1, 4, 16 … minutes, capped at
	/// 6 hours, at most ImportConfig.MaxRunAttempts); deterministic failures pause for an administrator.
	/// </summary>
	public class ImportRunService : IImportRunService
	{
		private readonly IImportRunsRepository _runs;
		private readonly IImportRunQueue _queue;
		private readonly IImportTimelineService _timeline;
		private readonly IEnumerable<IImportRunHandler> _handlers;
		private readonly IDepartmentImportNotificationService _notifications;

		public ImportRunService(IImportRunsRepository runs, IImportRunQueue queue, IImportTimelineService timeline, IEnumerable<IImportRunHandler> handlers,
			IDepartmentImportNotificationService notifications)
		{
			_runs = runs;
			_queue = queue;
			_timeline = timeline;
			_handlers = handlers;
			_notifications = notifications;
		}

		public Task<ImportRun> QueueAsync(ImportRun run, CancellationToken cancellationToken = default) => _queue.QueueAsync(run, cancellationToken);

		public Task<ImportRun> GetRunAsync(int departmentId, string runId, CancellationToken cancellationToken = default) =>
			_runs.GetByIdAsync(departmentId, runId, cancellationToken);

		public Task<List<ImportRun>> GetRunsAsync(int departmentId, string workspaceId, CancellationToken cancellationToken = default) =>
			_runs.GetAllForWorkspaceAsync(departmentId, workspaceId, cancellationToken);

		public async Task<ImportRun> RequestCancelAsync(int departmentId, string userId, string runId, ImportRunCancelRequest request, CancellationToken cancellationToken = default)
		{
			var run = await _runs.GetByIdAsync(departmentId, runId, cancellationToken) ?? throw new DepartmentImportException("import_run_not_found", 404);
			var status = (ImportRunStatus)run.Status;
			if (status == ImportRunStatus.Completed || status == ImportRunStatus.Cancelled)
				throw new DepartmentImportException("import_run_finished", 409);

			await _runs.RequestCancelAsync(departmentId, runId, request, DateTime.UtcNow, cancellationToken);

			// A run nobody is working on (queued, paused, failed-retrying) stops now; a leased run stops at its next chunk boundary.
			if (status == ImportRunStatus.Queued || status == ImportRunStatus.Paused || status == ImportRunStatus.Failed || status == ImportRunStatus.AwaitingChecker)
			{
				run.LeaseOwner = "user:" + userId;
				run.LeaseExpiresOn = DateTime.UtcNow.AddSeconds(5);
				var stopped = await _runs.ClaimByIdAsync(run.Id, run.LeaseOwner, run.LeaseExpiresOn.Value, DateTime.UtcNow, int.MaxValue, cancellationToken);
				var target = stopped ?? run;
				target.Status = request == ImportRunCancelRequest.Cancel ? (int)ImportRunStatus.Cancelled : (int)ImportRunStatus.Paused;
				target.CompletedOn = request == ImportRunCancelRequest.Cancel ? DateTime.UtcNow : (DateTime?)null;
				target.LeaseExpiresOn = null;
				target.UpdatedOn = DateTime.UtcNow;
				if (stopped != null)
				{
					target.LeaseOwner = null;
					await _runs.SaveLeasedAsync(target, run.LeaseOwner, cancellationToken);
				}
				else
				{
					await _runs.UpdateAsync(target, cancellationToken, true);
				}
				run = target;
			}

			await _timeline.RecordAsync(departmentId, run.WorkspaceId, ImportActorKind.Administrator, userId,
				request == ImportRunCancelRequest.Cancel ? "RunCancelRequested" : "RunPauseRequested", new { runId, kind = run.Kind }, run.CorrelationId, cancellationToken);
			return run;
		}

		public async Task<ImportRun> ResumeAsync(int departmentId, string userId, string runId, CancellationToken cancellationToken = default)
		{
			var run = await _runs.GetByIdAsync(departmentId, runId, cancellationToken) ?? throw new DepartmentImportException("import_run_not_found", 404);
			if (run.Status != (int)ImportRunStatus.Paused && run.Status != (int)ImportRunStatus.Failed)
				throw new DepartmentImportException("import_run_not_paused", 409);

			await _runs.RequestCancelAsync(departmentId, runId, ImportRunCancelRequest.None, DateTime.UtcNow, cancellationToken);
			run.CancelRequested = (int)ImportRunCancelRequest.None;
			run.Status = (int)ImportRunStatus.Queued;
			run.NextAttemptOn = null;
			run.LastErrorCode = null;
			run.UpdatedOn = DateTime.UtcNow;
			await _runs.UpdateAsync(run, cancellationToken, true);
			await _queue.NudgeAsync(run);

			await _timeline.RecordAsync(departmentId, run.WorkspaceId, ImportActorKind.Administrator, userId, "RunResumed", new { runId, kind = run.Kind },
				run.CorrelationId, cancellationToken);
			return run;
		}

		public async Task<int> ProcessAvailableAsync(string workerName, TimeSpan budget, CancellationToken cancellationToken = default)
		{
			var deadline = DateTime.UtcNow.Add(budget);
			var served = 0;

			// Several short runs may fit in one budget; each claim takes the least recently served runnable run.
			while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
			{
				var now = DateTime.UtcNow;
				var run = await _runs.ClaimNextAsync(workerName, now.AddSeconds(ImportConfig.RunLeaseSeconds), now, ImportConfig.MaxRunAttempts, cancellationToken);
				if (run == null)
					break;

				served++;
				await ExecuteLeasedAsync(run, workerName, deadline, cancellationToken);
			}

			return served;
		}

		public async Task<bool> ProcessRunAsync(string runId, string workerName, TimeSpan budget, CancellationToken cancellationToken = default)
		{
			var now = DateTime.UtcNow;
			var run = await _runs.ClaimByIdAsync(runId, workerName, now.AddSeconds(ImportConfig.RunLeaseSeconds), now, ImportConfig.MaxRunAttempts, cancellationToken);
			if (run == null)
				return false;

			await ExecuteLeasedAsync(run, workerName, now.Add(budget), cancellationToken);
			return true;
		}

		public async Task<int> FailStalledRunsAsync(CancellationToken cancellationToken = default)
		{
			var stalled = await _runs.GetStalledAsync(DateTime.UtcNow.AddMinutes(-ImportConfig.StalledRunMinutes), 50, cancellationToken);
			var failed = 0;
			foreach (var run in stalled)
			{
				// Take the run over first: a live worker with a fresh lease is not touched.
				var owner = "watchdog:" + Environment.MachineName;
				if (run.LeaseExpiresOn.HasValue && run.LeaseExpiresOn.Value > DateTime.UtcNow)
				{
					// Heartbeat present but the cursor has not moved for the stall window: livelock (section 13.1).
					var taken = await _runs.ClaimByIdAsync(run.Id, owner, DateTime.UtcNow.AddSeconds(30), DateTime.UtcNow.AddSeconds(ImportConfig.RunLeaseSeconds + 1),
						ImportConfig.MaxRunAttempts, cancellationToken);
					if (taken == null)
						continue;
					run.LeaseOwner = owner;
				}

				run.Status = (int)ImportRunStatus.Failed;
				run.LastErrorCode = "IMP-RUN-STALLED";
				run.Attempts += 1;
				run.NextAttemptOn = run.Attempts < ImportConfig.MaxRunAttempts ? DateTime.UtcNow.Add(DepartmentImportStateMachine.Backoff(run.Attempts)) : (DateTime?)null;
				run.LeaseExpiresOn = null;
				run.UpdatedOn = DateTime.UtcNow;
				var owned = run.LeaseOwner;
				run.LeaseOwner = null;
				if (owned == null ? await TryUpdateAsync(run, cancellationToken) : await _runs.SaveLeasedAsync(run, owned, cancellationToken))
				{
					failed++;
					Logging.LogError($"Department import run {run.Id} (department {run.DepartmentId}) stalled and was failed for retry.");
					await _timeline.RecordAsync(run.DepartmentId, run.WorkspaceId, ImportActorKind.System, "watchdog", "RunStalled", new { runId = run.Id, run.Attempts },
						run.CorrelationId, cancellationToken);
				}
			}

			return failed;
		}

		private async Task<bool> TryUpdateAsync(ImportRun run, CancellationToken cancellationToken)
		{
			await _runs.UpdateAsync(run, cancellationToken, true);
			return true;
		}

		private async Task ExecuteLeasedAsync(ImportRun run, string workerName, DateTime deadline, CancellationToken cancellationToken)
		{
			var handler = _handlers.FirstOrDefault(h => (int)h.Kind == run.Kind);
			var lastHeartbeat = DateTime.UtcNow;
			var leaseLost = false;

			async Task<bool> Heartbeat(CancellationToken ct)
			{
				if (DateTime.UtcNow - lastHeartbeat < TimeSpan.FromSeconds(ImportConfig.RunHeartbeatSeconds))
					return !leaseLost;

				lastHeartbeat = DateTime.UtcNow;
				leaseLost = !await _runs.HeartbeatAsync(run.Id, workerName, DateTime.UtcNow.AddSeconds(ImportConfig.RunLeaseSeconds), DateTime.UtcNow, ct);
				return !leaseLost;
			}

			async Task<bool> Save(CancellationToken ct)
			{
				run.UpdatedOn = DateTime.UtcNow;
				run.HeartbeatOn = DateTime.UtcNow;
				run.LeaseExpiresOn = DateTime.UtcNow.AddSeconds(ImportConfig.RunLeaseSeconds);
				leaseLost = !await _runs.SaveLeasedAsync(run, workerName, ct);
				return !leaseLost;
			}

			var lease = new ImportRunLease(workerName, Heartbeat, Save);
			ImportStepResult result;

			if (handler == null)
			{
				result = ImportStepResult.Paused("IMP-RUN-NO-HANDLER");
			}
			else
			{
				try
				{
					result = await handler.ExecuteAsync(run, lease, deadline, cancellationToken);
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					// Graceful shutdown: keep the cursor, release the lease so another worker resumes at once.
					result = ImportStepResult.More();
				}
				catch (ImportArtifactIntegrityException ex)
				{
					result = ex.Code == "IMP-STORE-UNAVAILABLE" ? ImportStepResult.Transient(ex.Code) : ImportStepResult.Paused(ex.Code);
				}
				catch (DepartmentImportException ex)
				{
					result = ImportStepResult.Paused(ex.Code);
				}
				catch (Exception ex)
				{
					// Unclassified: logged with the correlation id only, retried with backoff (a writer bug pauses after the attempts run out).
					Logging.LogException(ex, $"Department import run {run.Id} ({(ImportRunKind)run.Kind}) failed", run.CorrelationId);
					result = ImportStepResult.Transient("IMP-RUN-ERROR");
				}
			}

			if (leaseLost)
				return;

			await FinishSliceAsync(run, workerName, result, cancellationToken);
		}

		private async Task FinishSliceAsync(ImportRun run, string workerName, ImportStepResult result, CancellationToken cancellationToken)
		{
			var now = DateTime.UtcNow;
			var previousStatus = (ImportRunStatus)run.Status;
			string timelineAction = null;
			var notify = false;

			if (result.Completed)
			{
				// A commit hands over to its own reconcile phase; the handler sets Reconciling and returns More until then.
				run.Status = (int)ImportRunStatus.Completed;
				run.CompletedOn = now;
				run.LastErrorCode = result.ErrorCode;
				timelineAction = "RunCompleted";
				notify = run.Kind != (int)ImportRunKind.Analysis;
			}
			else if (result.Pause)
			{
				run.Status = (int)ImportRunStatus.Paused;
				run.LastErrorCode = result.ErrorCode;
				timelineAction = "RunPaused";
				notify = true;
			}
			else if (result.Retry)
			{
				run.Attempts += 1;
				run.LastErrorCode = result.ErrorCode;
				if (run.Attempts >= ImportConfig.MaxRunAttempts)
				{
					// Out of attempts: stop retrying blindly and wait for a person ("Needs engineering" in BackOffice).
					run.Status = (int)ImportRunStatus.Paused;
					run.NextAttemptOn = null;
					timelineAction = "RunPaused";
					notify = true;
				}
				else
				{
					run.Status = (int)ImportRunStatus.Failed;
					run.NextAttemptOn = now.Add(DepartmentImportStateMachine.Backoff(run.Attempts));
					timelineAction = previousStatus == ImportRunStatus.Failed ? null : "RunRetrying";
				}
			}
			else if (result.ErrorCode != null)
			{
				run.Status = (int)ImportRunStatus.Failed;
				run.NextAttemptOn = null;
				run.LastErrorCode = result.ErrorCode;
				timelineAction = "RunFailed";
				notify = true;
			}
			else
			{
				// More work: release the lease and leave it runnable for the next claim (round-robin across departments).
				if (run.Status == (int)ImportRunStatus.Running && run.CancelRequested == (int)ImportRunCancelRequest.None)
					run.Status = (int)ImportRunStatus.Queued;
				else if (run.CancelRequested == (int)ImportRunCancelRequest.Pause)
				{
					run.Status = (int)ImportRunStatus.Paused;
					timelineAction = "RunPaused";
				}
				else if (run.CancelRequested == (int)ImportRunCancelRequest.Cancel)
				{
					run.Status = (int)ImportRunStatus.Cancelled;
					run.CompletedOn = now;
					timelineAction = "RunCancelled";
				}
			}

			// A commit between slices of its reconcile phase stays Reconciling (the workspace shows "Verifying"); an
			// already-expired lease is what makes it claimable again (the claim predicate's "Leased" branch).
			var reconcilingContinues = run.Status == (int)ImportRunStatus.Reconciling && !result.Completed && !result.Pause && !result.Retry &&
				result.ErrorCode == null && run.CancelRequested == (int)ImportRunCancelRequest.None;
			run.LeaseExpiresOn = reconcilingContinues ? now : (DateTime?)null;
			run.UpdatedOn = now;
			run.LeaseOwner = null;
			if (!await _runs.SaveLeasedAsync(run, workerName, cancellationToken))
				return;

			if (timelineAction != null)
				await _timeline.RecordAsync(run.DepartmentId, run.WorkspaceId, ImportActorKind.System, workerName, timelineAction,
					new { runId = run.Id, kind = run.Kind, code = run.LastErrorCode }, run.CorrelationId, cancellationToken);

			if (notify)
				await _notifications.NotifyRunFinishedAsync(run, cancellationToken);
		}
	}
}
