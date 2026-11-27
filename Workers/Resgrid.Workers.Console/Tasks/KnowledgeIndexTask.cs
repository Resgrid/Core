using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Quidjibo.Handlers;
using Quidjibo.Misc;
using Resgrid.Workers.Console.Commands;
using Resgrid.Workers.Framework.Logic;

namespace Resgrid.Workers.Console.Tasks
{
	/// <summary>Worker 89 (KnowledgeIndexLogic): knowledge reindex requests and the nightly repair pass.</summary>
	public class KnowledgeIndexTask : IQuidjiboHandler<KnowledgeIndexCommand>
	{
		public string Name => "Knowledge Index";
		public int Priority => 1;
		public ILogger _logger;

		public KnowledgeIndexTask(ILogger logger)
		{
			_logger = logger;
		}

		public async Task ProcessAsync(KnowledgeIndexCommand command, IQuidjiboProgress progress, CancellationToken cancellationToken)
		{
			try
			{
				progress.Report(1, $"Starting the {Name} Task");

				var result = await new KnowledgeIndexLogic().Process(cancellationToken);

				if (result.Departments > 0 || result.NotEntitled > 0 || result.Failures > 0)
					_logger.LogInformation($"KnowledgeIndex::Departments {result.Departments}, not entitled {result.NotEntitled}, indexed {result.ItemsIndexed}, removed {result.ItemsRemoved}, embedded {result.ChunksEmbedded}, repairs {result.Repairs}, embeddings unavailable {result.EmbeddingsUnavailable}, failures {result.Failures}");

				progress.Report(100, $"Finishing the {Name} Task");
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				Resgrid.Framework.Logging.LogException(ex);
				_logger.LogError(ex.ToString());
			}
		}
	}
}
