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
	/// <summary>Worker 95 (LmsClassesLogic): class reminders, waitlist promotion and the roster retention purge.</summary>
	public class LmsClassesTask : IQuidjiboHandler<LmsClassesCommand>
	{
		public string Name => "LMS Classes";
		public int Priority => 1;
		public ILogger _logger;

		public LmsClassesTask(ILogger logger)
		{
			_logger = logger;
		}

		public async Task ProcessAsync(LmsClassesCommand command, IQuidjiboProgress progress, CancellationToken cancellationToken)
		{
			try
			{
				progress.Report(1, $"Starting the {Name} Task");

				var result = await new LmsClassesLogic().Process(cancellationToken);
				if (result.Reminders > 0 || result.Promoted > 0 || result.Purged > 0 || result.Failures > 0)
					_logger.LogInformation($"LmsClasses::Reminders {result.Reminders}, promoted {result.Promoted}, purged {result.Purged}, gate closed {result.GateClosed}, failures {result.Failures}");

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
