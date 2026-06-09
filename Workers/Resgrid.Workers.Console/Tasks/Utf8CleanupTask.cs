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
	public class Utf8CleanupTask : IQuidjiboHandler<Utf8CleanupCommand>
	{
		public string Name => "UTF-8 Data Cleanup";
		public int Priority => 1;
		public ILogger _logger;

		public Utf8CleanupTask(ILogger logger)
		{
			_logger = logger;
		}

		public async Task ProcessAsync(Utf8CleanupCommand command, IQuidjiboProgress progress, CancellationToken cancellationToken)
		{
			try
			{
				progress.Report(1, $"Starting the {Name} Task");

				var logic = new Utf8CleanupLogic();
				var result = await logic.Process(cancellationToken);

				if (result.Item1)
					_logger.LogInformation("Utf8Cleanup::" + result.Item2);
				else
					_logger.LogError("Utf8Cleanup::Failed: " + result.Item2);

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
