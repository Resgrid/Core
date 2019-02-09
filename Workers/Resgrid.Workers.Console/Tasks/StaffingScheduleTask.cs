using Autofac;
using Microsoft.Extensions.Logging;
using Quidjibo.Handlers;
using Quidjibo.Misc;
using Resgrid.Model.Helpers;
using Resgrid.Model.Services;
using Resgrid.Workers.Framework;
using Resgrid.Workers.Framework.Logic;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Workers.Console.Tasks
{
	public class StaffingScheduleTask : IQuidjiboHandler<Commands.StaffingScheduleCommand>
	{
		public string Name => "Staffing Scheduler";
		public int Priority => 1;
		public ILogger _logger;

		public StaffingScheduleTask(ILogger logger)
		{
			_logger = logger;
		}

		public async Task ProcessAsync(Commands.StaffingScheduleCommand command, IQuidjiboProgress progress, CancellationToken cancellationToken)
		{
			progress.Report(1, $"Starting the {Name} Task");

			await Task.Factory.StartNew(() =>
			{
				var _departmentsService = Bootstrapper.GetKernel().Resolve<IDepartmentsService>();
				var _scheduledTasksService = Bootstrapper.GetKernel().Resolve<IScheduledTasksService>();
				var logic = new StaffingScheduleLogic();

				var allItems = _scheduledTasksService.GetAllUpcomingStaffingScheduledTaks();

				if (allItems != null && allItems.Any())
				{
					// Filter only the past items and ones that are 5 minutes 30 seconds in the future
					var items = (from st in allItems
								 let department = _departmentsService.GetDepartmentByUserId(st.UserId)
								 let runTime = st.WhenShouldJobBeRun(TimeConverterHelper.TimeConverter(DateTime.UtcNow, department))
								 where
									 runTime.HasValue && runTime.Value >= TimeConverterHelper.TimeConverter(DateTime.UtcNow, department) &&
									 runTime.Value <= TimeConverterHelper.TimeConverter(DateTime.UtcNow, department).AddMinutes(5).AddSeconds(30)
								 select new
								 {
									 ScheduledTask = st,
									 Department = department
								 }).ToList();

					if (items != null && items.Any())
					{
						_logger.LogInformation("StaffingSchedule::Staffing Levels to Change: " + items.Count());

						foreach (var i in items)
						{
							var qi = new StaffingScheduleQueueItem();
							qi.ScheduledTask = i.ScheduledTask;
							qi.Department = i.Department;

							_logger.LogInformation("StaffingSchedule::Processing Staffing Schedule:" + qi.ScheduledTask.ScheduledTaskId);

							var result = logic.Process(qi);

							if (result.Item1)
							{
								_logger.LogInformation($"StaffingSchedule::Processed Staffing Schedule {qi.ScheduledTask.ScheduledTaskId} successfully.");
							}
							else
							{
								_logger.LogInformation($"StaffingSchedule::Failed to Process staffing schedule {qi.ScheduledTask.ScheduledTaskId} error {result.Item2}");
							}
						}
					}

				}
			}, cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);



			progress.Report(100, $"Finishing the {Name} Task");
		}
	}
}
