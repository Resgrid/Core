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
	/// <summary>Worker 98 (LmsStandardsLogic): unpacks queued SCORM / cmi5 packages, delivers the xAPI outbox and purges expired launches.</summary>
	public class LmsStandardsTask : IQuidjiboHandler<LmsStandardsCommand>
	{
		public string Name => "LMS Standards";
		public int Priority => 1;
		public ILogger _logger;

		public LmsStandardsTask(ILogger logger)
		{
			_logger = logger;
		}

		public async Task ProcessAsync(LmsStandardsCommand command, IQuidjiboProgress progress, CancellationToken cancellationToken)
		{
			try
			{
				progress.Report(1, $"Starting the {Name} Task");

				var result = await new LmsStandardsLogic().Process(cancellationToken);
				if (result.PackagesProcessed > 0 || result.StatementsDelivered > 0 || result.Failures > 0)
					_logger.LogInformation($"LmsStandards::Packages {result.PackagesProcessed}, statements delivered {result.StatementsDelivered}, purged {result.Purged}, failures {result.Failures}");

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
