using System;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.Logging;
using Quidjibo.Handlers;
using Quidjibo.Misc;
using Resgrid.Model.Services;
using Resgrid.Workers.Console.Commands;
using Resgrid.Workers.Framework;

namespace Resgrid.Workers.Console.Tasks
{
	public class GdprExportTask : IQuidjiboHandler<GdprExportCommand>
	{
		public string Name => "GDPR Data Export";
		public int Priority => 1;
		public ILogger _logger;

		public GdprExportTask(ILogger logger)
		{
			_logger = logger;
		}

		public async Task ProcessAsync(GdprExportCommand command, IQuidjiboProgress progress, CancellationToken cancellationToken)
		{
			try
			{
				progress.Report(1, $"Starting the {Name} Task");

				var gdprService = Bootstrapper.GetKernel().Resolve<IGdprDataExportService>();

				_logger.LogInformation("GdprExport::Expiring old requests");
				await gdprService.ExpireOldRequestsAsync(cancellationToken);

				_logger.LogInformation("GdprExport::Processing pending requests");
				await gdprService.ProcessPendingRequestsAsync(cancellationToken);

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
