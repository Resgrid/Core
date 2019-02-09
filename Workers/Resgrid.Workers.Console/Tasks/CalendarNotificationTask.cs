using Autofac;
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
					progress.Report(2, "CalendarNotification::Calendar Items to Notify: " + calendarItems.Count);

					foreach (var calendarItem in calendarItems)
					{
						var qi = new CalendarNotifierQueueItem();
						qi.CalendarItem = calendarItem;

						progress.Report(3, "CalendarNotification::Processing Notification for CalendarId:" + qi.CalendarItem.CalendarItemId);

						var result = logic.Process(qi);

						if (result.Item1)
						{
							progress.Report(4, $"CalendarNotification::Processed Calendar Notification {qi.CalendarItem.CalendarItemId} successfully.");
						}
						else
						{
							progress.Report(5, $"CalendarNotification::Failed to Processed Calendar Notification {qi.CalendarItem.CalendarItemId} error {result.Item2}");
						}
					}
				}
				else
				{
					progress.Report(6, "CalendarNotification::No Calendar Items to Notify");
				}
			}, cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);

			progress.Report(7, $"Finishing the {Name} Task");
		}
	}
}
