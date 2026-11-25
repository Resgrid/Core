using System;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Resgrid.Framework;
using Resgrid.Model.Services;

namespace Resgrid.Workers.Framework.Logic
{
	/// <summary>The work one run of worker 90 did.</summary>
	public class SignaturesRunResult
	{
		public int Hints { get; set; }
		public int Reconciled { get; set; }
		public int Expired { get; set; }
		public int Sealed { get; set; }
		public int Retimestamped { get; set; }
		public int Purged { get; set; }
		/// <summary>S3: reminders sent to signers whose turn it still is.</summary>
		public int Reminded { get; set; }
		public int Total => Hints + Reconciled + Expired + Sealed + Retimestamped + Purged + Reminded;
	}

	/// <summary>
	/// Worker 90 (e-Signature S1/S2): processes webhook hints (each re-fetches its envelope; only that state counts), re-fetches
	/// open remote envelopes whose reconcile time has come, expires requests past their expiry, retries pending seals with
	/// backoff (nothing is attempted while sealing is not configured — it never seals with a local time), re-timestamps B-LTA
	/// archives that are due and purges old hints. S3 adds the reminders to signers of multi-signer requests whose turn it still is. Every step starts from a due-work query, never a sweep of departments, and
	/// asks no billing gate. One failing step never stops the others.
	/// </summary>
	public class SignaturesLogic
	{
		public const int BatchSize = 25;

		public async Task<SignaturesRunResult> Process(CancellationToken cancellationToken = default)
		{
			using var scope = Bootstrapper.GetKernel().BeginLifetimeScope();
			return await Process(scope.Resolve<ISignatureRequestService>(), scope.Resolve<IDocumentSealingService>(), scope.Resolve<IESignWebhookService>(),
				DateTime.UtcNow, cancellationToken);
		}

		/// <summary>The run itself, with its services passed in (tests).</summary>
		public static async Task<SignaturesRunResult> Process(ISignatureRequestService requests, IDocumentSealingService sealing, IESignWebhookService webhooks,
			DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var result = new SignaturesRunResult();
			result.Hints = await StepAsync(() => requests.ProcessWebhookEventsAsync(utcNow, BatchSize, cancellationToken), "webhook hints", cancellationToken);
			result.Reconciled = await StepAsync(() => requests.ReconcileDueAsync(utcNow, BatchSize, cancellationToken), "envelope reconcile", cancellationToken);
			result.Expired = await StepAsync(() => requests.ExpireDueAsync(utcNow, BatchSize, cancellationToken), "request expiry", cancellationToken);
			result.Reminded = await StepAsync(() => requests.RemindDueAsync(utcNow, BatchSize, cancellationToken), "signer reminders", cancellationToken);
			result.Sealed = await StepAsync(() => sealing.RetryPendingAsync(utcNow, BatchSize, cancellationToken), "pending seals", cancellationToken);
			result.Retimestamped = await StepAsync(() => sealing.RetimestampDueAsync(utcNow, BatchSize, cancellationToken), "re-timestamp", cancellationToken);
			result.Purged = await StepAsync(() => webhooks.PurgeExpiredAsync(utcNow, cancellationToken), "hint purge", cancellationToken);
			return result;
		}

		private static async Task<int> StepAsync(Func<Task<int>> step, string name, CancellationToken cancellationToken)
		{
			try
			{
				return await step();
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Worker 90 step failed: " + name);
				return 0;
			}
		}
	}
}
