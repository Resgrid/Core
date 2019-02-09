using Autofac;
using Microsoft.Extensions.Logging;
using Quidjibo.Commands;
using Quidjibo.Handlers;
using Quidjibo.Misc;
using Resgrid.Model.Services;
using Resgrid.Workers.Console.Commands;
using Resgrid.Workers.Framework;
using Resgrid.Workers.Framework.Logic;
using Resgrid.Workers.Framework.Workers.CalendarNotifier;
using Serilog.Core;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Workers.Console.Tasks
{
	public class CalendarNotificationTask : IQuidjiboHandler<CalendarNotificationCommand>
	{
		public string Name => "Calendar Notifier";
		public int Priority => 1;
		public ILogger _logger;

		public CalendarNotificationTask(ILogger logger)
		{
			_logger = logger;
		}

		public async Task ProcessAsync(CalendarNotificationCommand command, IQuidjiboProgress progress, CancellationToken cancellationToken)
		{
			progress.Report(1, $"Starting the {Name} Task");

			await Task.Factory.StartNew(() =>
			{
				var _calendarService = Bootstrapper.GetKernel().Resolve<ICalendarService>();
				var logic = new CalendarNotifierLogic();

				var calendarItems = _calendarService.GetV2CalendarItemsToNotify(DateTime.UtcNow);

				if (calendarItems != null)
				{
					_logger.LogInformation("CalendarNotification::Calendar Items to Notify: " + calendarItems.Count);

					foreach (var calendarItem in calendarItems)
					{
						var qi = new CalendarNotifierQueueItem();
						qi.CalendarItem = calendarItem;

						_logger.LogInformation("CalendarNotification::Processing Notification for CalendarId:" + qi.CalendarItem.CalendarItemId);

						var result = logic.Process(qi);

						if (result.Item1)
						{
							_logger.LogInformation($"CalendarNotification::Processed Calendar Notification {qi.CalendarItem.CalendarItemId} successfully.");
						}
						else
						{
							_logger.LogInformation($"CalendarNotification::Failed to Processed Calendar Notification {qi.CalendarItem.CalendarItemId} error {result.Item2}");
						}
					}
				}
				else
				{
					progress.Report(6, "CalendarNotification::No Calendar Items to Notify");
				}
			}, cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);

			progress.Report(100, $"Finishing the {Name} Task");
		}
	}
}
