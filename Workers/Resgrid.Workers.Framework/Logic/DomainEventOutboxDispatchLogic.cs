using System;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Resgrid.Framework;
using Resgrid.Model.Services;

namespace Resgrid.Workers.Framework.Logic
{
	/// <summary>
	/// Worker command 40: the durable catch-up sweep of the DomainEventOutbox (RMS plan section 5.6).
	/// Drains pending rows in bounded batches under a single-flight lease per row, alerts on a sustained
	/// backlog, and purges dispatched rows after a retention window. The in-process post-commit path is
	/// the fast path; this sweep guarantees delivery when that path was skipped or crashed.
	/// </summary>
	public sealed class DomainEventOutboxDispatchLogic
	{
		private const int BatchSize = 200;
		private const int MaximumBatchesPerRun = 25;
		private const int DispatchedRetentionDays = 14;
		private static readonly TimeSpan BacklogAlertThreshold = TimeSpan.FromSeconds(60);

		public async Task<Tuple<bool, string>> Process(CancellationToken cancellationToken)
		{
			try
			{
				var outboxService = Bootstrapper.GetKernel().Resolve<IDomainEventOutboxService>();
				var leaseOwner = "worker40:" + Environment.MachineName;

				var total = 0;
				for (var batch = 0; batch < MaximumBatchesPerRun; batch++)
				{
					cancellationToken.ThrowIfCancellationRequested();

					var dispatched = await outboxService.DispatchPendingAsync(leaseOwner, BatchSize, cancellationToken);
					total += dispatched;

					if (dispatched < BatchSize)
						break;
				}

				var health = await outboxService.GetHealthAsync();
				if (health.Backlog.HasValue && health.Backlog.Value > BacklogAlertThreshold)
					Logging.LogError($"Domain event outbox backlog is {health.Backlog.Value.TotalSeconds:F0}s with {health.Pending} pending and {health.Failed} failed rows.");

				var purged = await outboxService.PurgeDispatchedAsync(DispatchedRetentionDays, cancellationToken);

				return new Tuple<bool, string>(true, $"Dispatched {total} event(s); {health.Pending} pending, {health.Failed} failed; purged {purged} dispatched row(s) older than {DispatchedRetentionDays} days.");
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return new Tuple<bool, string>(false, ex.ToString());
			}
		}
	}
}
