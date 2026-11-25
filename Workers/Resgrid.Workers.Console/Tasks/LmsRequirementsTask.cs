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
	/// <summary>Worker 93 (LmsRequirementsLogic): learning requirements, training-backed enforcement and learning paths.</summary>
	public class LmsRequirementsTask : IQuidjiboHandler<LmsRequirementsCommand>
	{
		public string Name => "LMS Requirements";
		public int Priority => 1;
		public ILogger _logger;

		public LmsRequirementsTask(ILogger logger)
		{
			_logger = logger;
		}

		public async Task ProcessAsync(LmsRequirementsCommand command, IQuidjiboProgress progress, CancellationToken cancellationToken)
		{
			try
			{
				progress.Report(1, $"Starting the {Name} Task");

				var result = await new LmsRequirementsLogic().Process(cancellationToken);
				if (result.DailyPasses > 0 || result.Undone > 0 || result.Failures > 0)
					_logger.LogInformation($"LmsRequirements::Passes {result.DailyPasses}, transitions {result.Transitions}, notified {result.Notified}, assigned {result.Assigned}, " +
						$"enforced {result.Enforced}, refused {result.Refused}, undone {result.Undone}, failures {result.Failures}");

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
