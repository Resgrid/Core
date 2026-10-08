using System;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.Logging;
using Quidjibo.Handlers;
using Quidjibo.Misc;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Workers.Console.Commands;
using Resgrid.Workers.Console.SystemOperations;
using Resgrid.Workers.Framework;

namespace Resgrid.Workers.Console.Tasks
{
	/// <summary>
	/// Worker 76, every minute. Notices when Redis came back without its data (and queues a security matrix rebuild for
	/// every department), fails requests whose worker died mid-run, then claims and runs the waiting
	/// BackOffice -> System Operations requests one at a time, heartbeating each so the page shows progress.
	/// </summary>
	public class SystemOperationsTask : IQuidjiboHandler<SystemOperationsCommand>
	{
		/// <summary>Well inside SystemOperationsService.AbandonedAfter, so a live run is never mistaken for a dead one.</summary>
		public static TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);

		/// <summary>Per tick: the rest wait for the next minute rather than holding this tick open indefinitely.</summary>
		public const int MaxRequestsPerTick = 20;

		// One run at a time per worker process: a long rebuild spans many one-minute ticks, and the ticks queued behind it
		// have nothing to add. Claims are conditional writes, so a second worker process still never runs the same request.
		private static readonly SemaphoreSlim SingleFlight = new SemaphoreSlim(1, 1);

		public string Name => "System Operations";
		public int Priority => 1;

		private readonly ILogger _logger;

		public SystemOperationsTask(ILogger logger)
		{
			_logger = logger;
		}

		public async Task ProcessAsync(SystemOperationsCommand command, IQuidjiboProgress progress, CancellationToken cancellationToken)
		{
			if (!await SingleFlight.WaitAsync(0, cancellationToken))
				return;

			try
			{
				var abandoned = await WithServiceAsync(s => s.FailAbandonedRequestsAsync(cancellationToken));
				if (abandoned > 0)
					_logger.LogWarning("SystemOperations::Failed {Count} request(s) whose worker stopped mid-run", abandoned);

				await QueueRebuildIfCacheLostDataAsync(cancellationToken);

				var runner = new SystemOperationRunner(_logger);
				var workerName = $"{Environment.MachineName}:{Environment.ProcessId}";

				for (var i = 0; i < MaxRequestsPerTick && !cancellationToken.IsCancellationRequested; i++)
				{
					var request = await WithServiceAsync(s => s.ClaimNextRequestAsync(workerName, cancellationToken));
					if (request == null)
						break;

					await RunRequestAsync(runner, request, cancellationToken);
				}
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
			}
			catch (Exception ex)
			{
				Resgrid.Framework.Logging.LogException(ex);
				_logger.LogError(ex.ToString());
			}
			finally
			{
				SingleFlight.Release();
			}
		}

		private async Task QueueRebuildIfCacheLostDataAsync(CancellationToken cancellationToken)
		{
			if (!await WithServiceAsync(s => s.DetectCacheDataLossAsync()))
				return;

			// Matrix readers already answer from the permission rows on a miss, so this restores speed, not correctness.
			// The other Redis entries are cache-aside and refill on their own; the BackOffice page can clear or rerun the rest.
			var queued = await WithServiceAsync(s => s.RequestAsync(SystemOperationTypes.RebuildSecurityMatrices, null,
				SystemOperationSources.CacheDataLossDetected, SystemOperationsService.SystemRequester,
				"Redis came back without its data (the cache sentinel was missing). Rebuilding every department's security matrices.",
				cancellationToken));

			_logger.LogWarning("SystemOperations::Redis cache sentinel was missing; queued a security matrix rebuild ({RequestId}). {Error}",
				queued?.Request?.SystemOperationRequestId, queued?.Error);
		}

		private async Task RunRequestAsync(SystemOperationRunner runner, SystemOperationRequest request, CancellationToken cancellationToken)
		{
			var name = SystemOperationCatalog.GetName(request.OperationType);
			_logger.LogInformation("SystemOperations::Starting {Operation} ({RequestId}) requested by {RequestedBy}",
				name, request.SystemOperationRequestId, request.RequestedBy);

			var progress = new SystemOperationProgress();
			using var stopHeartbeat = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			var heartbeat = HeartbeatAsync(request.SystemOperationRequestId, progress, stopHeartbeat.Token);

			SystemOperationOutcome outcome;

			try
			{
				outcome = await runner.RunAsync(request, progress, cancellationToken);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				outcome = SystemOperationOutcome.Fail("The worker shut down before the operation finished. Request it again.");
			}
			catch (Exception ex)
			{
				Resgrid.Framework.Logging.LogException(ex, $"System operation {name} ({request.SystemOperationRequestId}) failed.");
				outcome = SystemOperationOutcome.Fail(ex.Message);
			}
			finally
			{
				stopHeartbeat.Cancel();
				await heartbeat;
			}

			// Recorded even while shutting down: otherwise the row sits in Running until it is failed as abandoned.
			await WithServiceAsync(s => s.CompleteRequestAsync(request.SystemOperationRequestId, outcome.Succeeded, outcome.Message, CancellationToken.None));

			_logger.LogInformation("SystemOperations::Finished {Operation} ({RequestId}): {Succeeded} {Message}",
				name, request.SystemOperationRequestId, outcome.Succeeded ? "succeeded" : "failed", outcome.Message);
		}

		private async Task HeartbeatAsync(string requestId, SystemOperationProgress progress, CancellationToken stop)
		{
			string lastSent = null;

			while (!stop.IsCancellationRequested)
			{
				try
				{
					await Task.Delay(HeartbeatInterval, stop);
				}
				catch (OperationCanceledException)
				{
					return;
				}

				var latest = progress.Latest;

				try
				{
					await WithServiceAsync(s => s.ReportProgressAsync(requestId, latest != lastSent ? latest : null, CancellationToken.None));
					lastSent = latest;
				}
				catch (Exception ex)
				{
					// A missed beat is not fatal; several in a row and the row is failed as abandoned.
					Resgrid.Framework.Logging.LogException(ex, $"System operation heartbeat failed for {requestId}.");
				}
			}
		}

		/// <summary>
		/// Each call gets its own scope: the heartbeat runs alongside the operation, and two concurrent calls on one
		/// scope would share its unit of work and connection.
		/// </summary>
		private static async Task<T> WithServiceAsync<T>(Func<ISystemOperationsService, Task<T>> call)
		{
			using var scope = Bootstrapper.GetKernel().BeginLifetimeScope();
			return await call(scope.Resolve<ISystemOperationsService>());
		}
	}
}
