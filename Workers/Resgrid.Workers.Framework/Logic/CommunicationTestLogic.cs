using System;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Resgrid.Framework;
using Resgrid.Model.Queue;
using Resgrid.Model.Services;

namespace Resgrid.Workers.Framework.Logic
{
	/// <summary>
	/// Runs a communication test off the request thread: resolves the run's audience, writes the
	/// per-recipient result rows, and sends on every enabled channel. Enqueued by whoever started
	/// the run (the on-demand API/web action, or the scheduled-test sweep) so that caller returns
	/// immediately no matter how large the department is.
	/// </summary>
	public class CommunicationTestLogic
	{
		public static async Task<bool> ProcessCommunicationTestQueueItem(CommunicationTestQueueItem item)
		{
			if (item == null)
				return true;

			var runId = item.GetRunId();
			if (runId == Guid.Empty)
			{
				Logging.LogInfo($"CommunicationTest: dropping queue item with an unparsable run id '{item.CommunicationTestRunId}'.");
				return true;
			}

			try
			{
				var communicationTestService = Bootstrapper.GetKernel().Resolve<ICommunicationTestService>();

				// Both halves are idempotent — building no-ops once the run has results and each result
				// is only ever sent once — so an at-least-once redelivery cannot double-send.
				await communicationTestService.ProcessRunAsync(runId, CancellationToken.None);
			}
			catch (Exception ex)
			{
				// Same convention as the other queue logic classes: a failed item must not take down
				// the queue processor. Any results left unsent are picked up by the worker's recovery
				// sweep (DeliverPendingRunsAsync) on its next cycle.
				Logging.LogException(ex);
			}

			return true;
		}
	}
}
