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
	/// <summary>Worker 84 (ProtocolLifecycleLogic): scheduled publishes, expiries, review reminders and retention.</summary>
	public class ProtocolLifecycleTask : IQuidjiboHandler<ProtocolLifecycleCommand>
	{
		public string Name => "Protocol Lifecycle";
		public int Priority => 1;
		public ILogger _logger;

		public ProtocolLifecycleTask(ILogger logger)
		{
			_logger = logger;
		}

		public async Task ProcessAsync(ProtocolLifecycleCommand command, IQuidjiboProgress progress, CancellationToken cancellationToken)
		{
			try
			{
				progress.Report(1, $"Starting the {Name} Task");

				var result = await new ProtocolLifecycleLogic().Process(cancellationToken);

				if (result.Activated > 0 || result.Expired > 0 || result.RemindersSent > 0 || result.Purged > 0 || result.Failures > 0)
					_logger.LogInformation($"ProtocolLifecycle::Activated {result.Activated}, expired {result.Expired}, reminders {result.RemindersSent}, purged {result.Purged}, failures {result.Failures}");

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
