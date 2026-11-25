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
	/// <summary>Worker 85 (TrainingRecordsLogic): runs the queued training history backfills.</summary>
	public class TrainingRecordsTask : IQuidjiboHandler<TrainingRecordsCommand>
	{
		public string Name => "Training Records";
		public int Priority => 1;
		public ILogger _logger;

		public TrainingRecordsTask(ILogger logger)
		{
			_logger = logger;
		}

		public async Task ProcessAsync(TrainingRecordsCommand command, IQuidjiboProgress progress, CancellationToken cancellationToken)
		{
			try
			{
				progress.Report(1, $"Starting the {Name} Task");

				var result = await new TrainingRecordsLogic().Process(cancellationToken);

				if (result.Jobs > 0 || result.Failures > 0)
					_logger.LogInformation($"TrainingRecords::Jobs {result.Jobs}, completed {result.Completed}, written {result.Written}, failures {result.Failures}");

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
