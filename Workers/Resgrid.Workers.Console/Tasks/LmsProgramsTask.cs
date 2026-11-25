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
	/// <summary>Worker 94 (LmsProgramsLogic): scheduled LMS reports, certification cycle auto-renew and alerts, familiarization credit.</summary>
	public class LmsProgramsTask : IQuidjiboHandler<LmsProgramsCommand>
	{
		public string Name => "LMS Programs";
		public int Priority => 1;
		public ILogger _logger;

		public LmsProgramsTask(ILogger logger)
		{
			_logger = logger;
		}

		public async Task ProcessAsync(LmsProgramsCommand command, IQuidjiboProgress progress, CancellationToken cancellationToken)
		{
			try
			{
				progress.Report(1, $"Starting the {Name} Task");

				var result = await new LmsProgramsLogic().Process(cancellationToken);
				if (result.ReportsSent > 0 || result.Renewed > 0 || result.Alerts > 0 || result.FamiliarizationWritten > 0 || result.Failures > 0)
					_logger.LogInformation($"LmsPrograms::Reports {result.ReportsSent}, renewed {result.Renewed}, alerts {result.Alerts}, familiarization {result.FamiliarizationWritten}, failures {result.Failures}");

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
