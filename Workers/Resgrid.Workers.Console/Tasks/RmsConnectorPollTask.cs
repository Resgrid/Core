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
	public sealed class RmsConnectorPollTask : IQuidjiboHandler<RmsConnectorPollCommand>
	{
		private readonly ILogger _logger;

		public RmsConnectorPollTask(ILogger logger)
		{
			_logger = logger ?? throw new ArgumentNullException(nameof(logger));
		}

		public string Name => "RMS Connector Poll";
		public int Priority => 1;

		public async Task ProcessAsync(RmsConnectorPollCommand command, IQuidjiboProgress progress, CancellationToken cancellationToken)
		{
			progress?.Report(1, $"Starting the {Name} Task");

			try
			{
				var logic = new RmsConnectorPollLogic();
				var result = await logic.Process(cancellationToken);

				if (!result.Item1)
					throw new InvalidOperationException(result.Item2);

				_logger.LogInformation("RmsConnectorPoll::{Summary}", result.Item2);
				progress?.Report(100, $"Finishing the {Name} Task");
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
		}
	}
}
