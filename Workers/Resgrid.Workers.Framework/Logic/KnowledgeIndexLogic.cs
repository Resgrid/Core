using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model.Services;

namespace Resgrid.Workers.Framework.Logic
{
	/// <summary>The work one run of worker 89 did.</summary>
	public class KnowledgeIndexRunSummary
	{
		public int Departments { get; set; }
		public int NotEntitled { get; set; }
		public int GateChecks { get; set; }
		public int ItemsIndexed { get; set; }
		public int ItemsRemoved { get; set; }
		public int ChunksEmbedded { get; set; }
		public int Repairs { get; set; }
		public bool EmbeddingsUnavailable { get; set; }
		public int Failures { get; set; }
	}

	/// <summary>
	/// Worker 89 (K1, protocols-training-knowledge-plan §16.2): drains the knowledge reindex queue (publish, retire, audience and AI-flag
	/// changes) and runs each department's nightly repair pass. It starts from a query for departments with due work (queued requests or a
	/// repair due) and never sweeps every department; it asks the Enhanced AI entitlement gate at most once per department per run, and a
	/// department no longer entitled has its requests dropped and its repair pass stopped. The Academy system corpus (department 0) is
	/// Resgrid-authored and needs no entitlement. Embedding calls happen here only, bounded by their deadline; an outage leaves chunks
	/// keyword-only until the next pass.
	/// </summary>
	public class KnowledgeIndexLogic
	{
		public async Task<KnowledgeIndexRunSummary> Process(CancellationToken cancellationToken = default)
		{
			using var scope = Bootstrapper.GetKernel().BeginLifetimeScope();
			var knowledge = scope.Resolve<IAiKnowledgeService>();
			return await Process(scope.Resolve<IKnowledgeIndexService>(), knowledge.IsEntitledAsync, DateTime.UtcNow, cancellationToken);
		}

		/// <summary>The run itself, with the index and the entitlement gate passed in (tests).</summary>
		public static async Task<KnowledgeIndexRunSummary> Process(IKnowledgeIndexService index, Func<int, Task<bool>> isEntitled, DateTime utcNow,
			CancellationToken cancellationToken = default)
		{
			var summary = new KnowledgeIndexRunSummary();

			try
			{
				// The system corpus is queued once; afterwards its own repair pass keeps it current.
				if (await index.GetStateAsync(0, cancellationToken) == null)
					await index.RequestRebuildAsync(0, null, cancellationToken);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "The Academy knowledge corpus could not be queued.");
				summary.Failures++;
			}

			var due = (await index.GetDueDepartmentsAsync(utcNow, Math.Clamp(KnowledgeConfig.WorkerDepartmentsPerRun, 1, 200), cancellationToken) ?? new List<int>())
				.Distinct().ToList();
			var gate = new Dictionary<int, bool>();
			foreach (var departmentId in due)
			{
				cancellationToken.ThrowIfCancellationRequested();
				try
				{
					if (departmentId != 0)
					{
						if (!gate.TryGetValue(departmentId, out var entitled))
						{
							entitled = await isEntitled(departmentId);
							gate[departmentId] = entitled;
							summary.GateChecks++;
						}

						if (!entitled)
						{
							await index.DeactivateAsync(departmentId, cancellationToken);
							summary.NotEntitled++;
							continue;
						}
					}

					var result = await index.ProcessDepartmentAsync(departmentId, utcNow, cancellationToken);
					summary.Departments++;
					summary.ItemsIndexed += result?.ItemsIndexed ?? 0;
					summary.ItemsRemoved += result?.ItemsRemoved ?? 0;
					summary.ChunksEmbedded += result?.ChunksEmbedded ?? 0;
					summary.Repairs += result?.Repairs ?? 0;
					summary.Failures += result?.Failures ?? 0;
					summary.EmbeddingsUnavailable |= result?.EmbeddingsUnavailable == true;
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception ex)
				{
					// One department failing does not stop the others; its requests stay queued for the next run.
					Logging.LogException(ex, $"Knowledge indexing failed for department {departmentId}.");
					summary.Failures++;
				}
			}

			return summary;
		}
	}
}
