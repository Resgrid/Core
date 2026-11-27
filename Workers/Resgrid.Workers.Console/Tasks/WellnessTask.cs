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
	/// <summary>Worker 91 (WellnessLogic): post-call exposure suggestions, anniversary reminders and exposure retention.</summary>
	public class WellnessTask : IQuidjiboHandler<WellnessCommand>
	{
		public string Name => "Member Wellness";
		public int Priority => 1;
		public ILogger _logger;

		public WellnessTask(ILogger logger)
		{
			_logger = logger;
		}

		public async Task ProcessAsync(WellnessCommand command, IQuidjiboProgress progress, CancellationToken cancellationToken)
		{
			try
			{
				progress.Report(1, $"Starting the {Name} Task");

				var result = await new WellnessLogic().Process(cancellationToken);

				if (result.FlagsProcessed > 0 || result.RemindersSent > 0 || result.EntriesErased > 0 || result.HealthNoticesSent > 0 || result.HealthRecordsErased > 0 || result.Failures > 0)
					_logger.LogInformation($"Wellness::Flags {result.FlagsProcessed}, suggestions {result.SuggestionsCreated}, reminders {result.RemindersSent}, erased {result.EntriesErased}, health notices {result.HealthNoticesSent}, health records erased {result.HealthRecordsErased}, failures {result.Failures}");

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
