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
	public class ParEvaluationTask : IQuidjiboHandler<ParEvaluationCommand>
	{
		public string Name => "PAR Evaluation";
		public int Priority => 1;
		public ILogger _logger;

		public ParEvaluationTask(ILogger logger)
		{
			_logger = logger;
		}

		public async Task ProcessAsync(ParEvaluationCommand command, IQuidjiboProgress progress, CancellationToken cancellationToken)
		{
			try
			{
				progress.Report(1, $"Starting the {Name} Task");

				var logic = new ParEvaluationLogic();
				var result = await logic.Process(cancellationToken);

				if (result.Item1)
					_logger.LogInformation($"ParEvaluation::{result.Item2}");
				else
					_logger.LogInformation($"ParEvaluation::Failed to sweep accountability. {result.Item2}");

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
