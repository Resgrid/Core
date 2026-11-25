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
	/// <summary>Worker 88 (LmsSweepLogic): the Academy seed, in-progress lesson reminders and lapse-rule housekeeping.</summary>
	public class LmsSweepTask : IQuidjiboHandler<LmsSweepCommand>
	{
		public string Name => "LMS Sweeps";
		public int Priority => 1;
		public ILogger _logger;

		public LmsSweepTask(ILogger logger)
		{
			_logger = logger;
		}

		public async Task ProcessAsync(LmsSweepCommand command, IQuidjiboProgress progress, CancellationToken cancellationToken)
		{
			try
			{
				progress.Report(1, $"Starting the {Name} Task");

				var result = await new LmsSweepLogic().Process(cancellationToken);
				if (result.Seeded > 0 || result.Reminders > 0 || result.Archived > 0 || result.Failures > 0)
					_logger.LogInformation($"LmsSweep::Seeded {result.Seeded}, reminders {result.Reminders}, archived {result.Archived}, failures {result.Failures}");

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
