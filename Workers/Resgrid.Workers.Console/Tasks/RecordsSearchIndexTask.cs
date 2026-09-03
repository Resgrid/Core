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
	public sealed class RecordsSearchIndexTask : IQuidjiboHandler<RecordsSearchIndexCommand>
	{
		private readonly ILogger _logger;

		public RecordsSearchIndexTask(ILogger logger)
		{
			_logger = logger ?? throw new ArgumentNullException(nameof(logger));
		}

		public string Name => "Records Search Index";
		public int Priority => 1;

		public async Task ProcessAsync(RecordsSearchIndexCommand command, IQuidjiboProgress progress, CancellationToken cancellationToken)
		{
			progress?.Report(1, $"Starting the {Name} Task");

			try
			{
				var logic = new RecordsSearchIndexLogic();
				var result = await logic.Process(cancellationToken);

				if (!result.Item1)
					throw new InvalidOperationException(result.Item2);

				_logger.LogInformation("RecordsSearchIndex::{Summary}", result.Item2);
				progress?.Report(100, $"Finishing the {Name} Task");
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
		}
	}
}
