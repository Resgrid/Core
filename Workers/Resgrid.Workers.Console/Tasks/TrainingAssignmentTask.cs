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
	/// <summary>Worker 83 (TrainingAssignmentLogic): drains the membership-change queue and runs due training repeats.</summary>
	public class TrainingAssignmentTask : IQuidjiboHandler<TrainingAssignmentCommand>
	{
		public string Name => "Training Assignment";
		public int Priority => 1;
		public ILogger _logger;

		public TrainingAssignmentTask(ILogger logger)
		{
			_logger = logger;
		}

		public async Task ProcessAsync(TrainingAssignmentCommand command, IQuidjiboProgress progress, CancellationToken cancellationToken)
		{
			try
			{
				progress.Report(1, $"Starting the {Name} Task");

				var result = await new TrainingAssignmentLogic().Process(cancellationToken);

				if (result.ChangesProcessed > 0 || result.Repeated > 0 || result.Failures > 0)
					_logger.LogInformation($"TrainingAssignment::Changes {result.ChangesProcessed}, added {result.Added}, withdrawn {result.Withdrawn}, repeated {result.Repeated}, failures {result.Failures}");

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
