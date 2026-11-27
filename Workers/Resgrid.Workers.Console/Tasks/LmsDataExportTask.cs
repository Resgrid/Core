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
	/// <summary>Worker 99 (LmsDataExportLogic): builds queued training data exports and removes expired ones.</summary>
	public class LmsDataExportTask : IQuidjiboHandler<LmsDataExportCommand>
	{
		public string Name => "LMS Data Export";
		public int Priority => 1;
		public ILogger _logger;

		public LmsDataExportTask(ILogger logger)
		{
			_logger = logger;
		}

		public async Task ProcessAsync(LmsDataExportCommand command, IQuidjiboProgress progress, CancellationToken cancellationToken)
		{
			try
			{
				progress.Report(1, $"Starting the {Name} Task");

				var result = await new LmsDataExportLogic().Process(cancellationToken);
				if (result.Completed > 0 || result.Failed > 0 || result.Purged > 0 || result.Requeued > 0)
					_logger.LogInformation($"LmsDataExport::Completed {result.Completed}, failed {result.Failed}, purged {result.Purged}, requeued {result.Requeued}");

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
