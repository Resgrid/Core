using Microsoft.Extensions.Logging;
using Quidjibo.Handlers;
using Quidjibo.Misc;
using Resgrid.Workers.Console.Commands;
using Resgrid.Workers.Framework.Logic;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Workers.Console.Tasks
{
	public class DispatchRenotifyTask : IQuidjiboHandler<DispatchRenotifyCommand>
	{
		public string Name => "Dispatch Re-notify";
		public int Priority => 1;
		private readonly ILogger _logger;

		public DispatchRenotifyTask(ILogger logger)
		{
			_logger = logger;
		}

		public async Task ProcessAsync(DispatchRenotifyCommand command, IQuidjiboProgress progress, CancellationToken cancellationToken)
		{
			try
			{
				progress.Report(1, $"Starting the {Name} Task");

				var logic = new DispatchRenotifyLogic();
				var result = await logic.Process(cancellationToken);

				if (result.Item1)
					_logger.LogInformation($"DispatchRenotify::{result.Item2}");
				else
					_logger.LogError($"DispatchRenotify::Failed to sweep dispatch re-notifications. {result.Item2}");

				progress.Report(100, $"Finishing the {Name} Task");
			}
			catch (Exception ex)
			{
				Resgrid.Framework.Logging.LogException(ex);
				_logger.LogError(ex.ToString());
			}
		}
	}
}
