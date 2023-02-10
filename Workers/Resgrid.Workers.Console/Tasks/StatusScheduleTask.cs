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
	public class StatusScheduleTask : IQuidjiboHandler<Commands.StatusScheduleCommand>
	{
		public string Name => "Status Scheduler";
		public int Priority => 1;
		public ILogger _logger;

		public StatusScheduleTask(ILogger logger)
		{
			_logger = logger;
		}

		public async Task ProcessAsync(Commands.StatusScheduleCommand command, IQuidjiboProgress progress, CancellationToken cancellationToken)
		{
			try
			{
				progress.Report(1, $"Starting the {Name} Task");

				//await Task.Run(async () =>
				//{
				var _departmentsService = Bootstrapper.GetKernel().Resolve<IDepartmentsService>();
				var _scheduledTasksService = Bootstrapper.GetKernel().Resolve<IScheduledTasksService>();
				var logic = new StatusScheduleLogic();

				var allDepartments = await _departmentsService.GetAllAsync();
				var allItems = await _scheduledTasksService.GetAllUpcomingStatusScheduledTasksAsync();

				if (allItems != null && allItems.Any())
				{
					// Filter only the past items and ones that are 5 minutes 30 seconds in the future
					var items = (from st in allItems
								 let department = allDepartments.FirstOrDefault(x => x.DepartmentId == st.DepartmentId)
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
						_logger.LogInformation("StatusSchedule::Status Levels to Change: " + items.Count());

						foreach (var i in items)
						{
							var qi = new StatusScheduleQueueItem();
							qi.ScheduledTask = i.ScheduledTask;
							qi.Department = i.Department;

							_logger.LogInformation("StatusSchedule::Processing Status Schedule:" + qi.ScheduledTask.ScheduledTaskId);

							var result = await logic.Process(qi);

							if (result.Item1)
							{
								_logger.LogInformation($"StatusSchedule::Processed Status Schedule {qi.ScheduledTask.ScheduledTaskId} successfully.");
							}
							else
							{
								_logger.LogInformation($"StatusSchedule::Failed to Process status schedule {qi.ScheduledTask.ScheduledTaskId} error {result.Item2}");
							}
						}
					}

				}
				//}, cancellationToken);

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
