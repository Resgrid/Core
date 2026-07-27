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
	public sealed class UnitTrackingRetentionTask :
		IQuidjiboHandler<UnitTrackingRetentionCommand>
	{
		private readonly ILogger _logger;

		public UnitTrackingRetentionTask(ILogger logger)
		{
			_logger = logger ??
				throw new ArgumentNullException(nameof(logger));
		}

		public string Name =>
			"Unit Tracking Location Retention";
		public int Priority => 1;

		public async Task ProcessAsync(
			UnitTrackingRetentionCommand command,
			IQuidjiboProgress progress,
			CancellationToken cancellationToken)
		{
			progress?.Report(
				1,
				$"Starting the {Name} Task");
			try
			{
				var logic =
					new UnitTrackingRetentionLogic();
				var result = await logic.Process(
					cancellationToken);
				if (!result.Item1)
				throw new InvalidOperationException(
					result.Item2);

				_logger.LogInformation(
					"UnitTrackingRetention::{Summary}",
					result.Item2);
				progress?.Report(
					100,
					$"Finishing the {Name} Task");
			}
			catch (Exception ex)
			{
				Resgrid.Framework.Logging.LogException(ex);
				_logger.LogError(
					ex,
					"UnitTrackingRetention::Failed");
				throw;
			}
		}
	}
}
