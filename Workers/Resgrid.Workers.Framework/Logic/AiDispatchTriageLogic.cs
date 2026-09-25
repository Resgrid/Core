using System;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Resgrid.Framework;
using Resgrid.Model.AiDispatch;
using Resgrid.Model.Queue;

namespace Resgrid.Workers.Framework.Logic
{
	/// <summary>
	/// Enrich-mode AI dispatch off the request thread (ai-dispatch-template-plan.md; enhanced-ai-addon-plan.md §4). The call was
	/// created and dispatched deterministically before this item was queued, so every failure here leaves it as it was.
	/// </summary>
	public class AiDispatchTriageLogic
	{
		public static async Task<bool> ProcessAiDispatchQueueItem(AiDispatchQueueItem item)
		{
			if (item == null || item.DepartmentId <= 0 || item.CallId <= 0)
				return true;

			try
			{
				// Scoped resolve: the enrichment service holds a unit of work and an inference client for this one item.
				using var scope = Bootstrapper.GetKernel().BeginLifetimeScope();
				var outcome = await scope.Resolve<IAiDispatchEnrichmentService>().EnrichAsync(item, CancellationToken.None);
				Logging.LogInfo($"AiDispatch: call {item.CallId} in department {item.DepartmentId} finished with {outcome}.");
			}
			catch (Exception ex)
			{
				// Same convention as the other queue logic classes: a failed item never takes down the queue processor.
				Logging.LogException(ex);
			}

			return true;
		}
	}
}
