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
	public class WeatherAlertImportTask : IQuidjiboHandler<WeatherAlertImportCommand>
	{
		public string Name => "Weather Alert Import";
		public int Priority => 1;
		public ILogger _logger;

		public WeatherAlertImportTask(ILogger logger)
		{
			_logger = logger;
		}

		public async Task ProcessAsync(WeatherAlertImportCommand command, IQuidjiboProgress progress, CancellationToken cancellationToken)
		{
			try
			{
				progress.Report(1, $"Starting the {Name} Task");

				var weatherAlertService = Bootstrapper.GetKernel().Resolve<IWeatherAlertService>();

				_logger.LogInformation("WeatherAlertImport::Processing all active sources");
				await weatherAlertService.ProcessAllActiveSourcesAsync(cancellationToken);

				_logger.LogInformation("WeatherAlertImport::Expiring old alerts");
				await weatherAlertService.ExpireOldAlertsAsync(cancellationToken);

				_logger.LogInformation("WeatherAlertImport::Sending pending notifications");
				await weatherAlertService.SendPendingNotificationsAsync(cancellationToken);

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
