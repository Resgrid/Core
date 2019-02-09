using Autofac;
using Microsoft.Extensions.Logging;
using Quidjibo.Handlers;
using Quidjibo.Misc;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Services;
using Resgrid.Workers.Console.Commands;
using Resgrid.Workers.Framework;
using Resgrid.Workers.Framework.Logic;
using Resgrid.Workers.Framework.Workers.ReportDelivery;
using Serilog.Core;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Workers.Console.Tasks
{
	public class ReportDeliveryTask : IQuidjiboHandler<ReportDeliveryTaskCommand>
	{
		public string Name => "Report Delivery";
		public int Priority => 1;
		public ILogger _logger;

		public ReportDeliveryTask(ILogger logger)
		{
			_logger = logger;
		}

		public async Task ProcessAsync(ReportDeliveryTaskCommand command, IQuidjiboProgress progress, CancellationToken cancellationToken)
		{
			progress.Report(1, $"Starting the {Name} Task");

			await Task.Factory.StartNew(() =>
			{
				var _departmentsService = Bootstrapper.GetKernel().Resolve<IDepartmentsService>();
				var _scheduledTasksService = Bootstrapper.GetKernel().Resolve<IScheduledTasksService>();
				var _usersService = Bootstrapper.GetKernel().Resolve<IUsersService>();
				var logic = new ReportDeliveryLogic();

				var allItems = _scheduledTasksService.GetUpcomingScheduledTaks();

				if (allItems != null)
				{
					// Filter only the past items and ones that are 5 minutes 30 seconds in the future
					var items = from st in allItems
								let department = _departmentsService.GetDepartmentByUserId(st.UserId)
								let email = _usersService.GetMembershipByUserId(st.UserId).Email
								let runTime = st.WhenShouldJobBeRun(TimeConverterHelper.TimeConverter(DateTime.UtcNow, department))
								where
									st.TaskType == (int)TaskTypes.ReportDelivery && runTime.HasValue &&
									runTime.Value >= TimeConverterHelper.TimeConverter(DateTime.UtcNow, department) &&
									runTime.Value <= TimeConverterHelper.TimeConverter(DateTime.UtcNow, department).AddMinutes(5).AddSeconds(30)
								select new
								{
									ScheduledTask = st,
									Department = department,
									Email = email
								};

					if (items != null && items.Count() > 0)
					{
						_logger.LogInformation("ReportDelivery::Reports to Deliver: " + items.Count());

						foreach (var i in items)
						{
							var qi = new ReportDeliveryQueueItem();
							qi.ScheduledTask = i.ScheduledTask;
							qi.Department = i.Department;
							qi.Email = i.Email;

							_logger.LogInformation("ReportDelivery::Processing Report:" + qi.ScheduledTask.ScheduledTaskId);

							var result = logic.Process(qi);

							if (result.Item1)
							{
								_logger.LogInformation($"ReportDelivery::Processed Report {qi.ScheduledTask.ScheduledTaskId} successfully.");
							}
							else
							{
								_logger.LogInformation($"ReportDelivery::Failed to Process report {qi.ScheduledTask.ScheduledTaskId} error {result.Item2}");
							}
						}

					}
				}
			}, cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);

			progress.Report(100, $"Finishing the {Name} Task");
		}
	}
}
