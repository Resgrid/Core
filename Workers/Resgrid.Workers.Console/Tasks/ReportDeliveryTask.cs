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
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Driver.Linq;
using Resgrid.Framework;

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

		public async Task ProcessAsync(ReportDeliveryTaskCommand command, IQuidjiboProgress progress,
			CancellationToken cancellationToken)
		{
			try
			{
				progress.Report(1, $"Starting the {Name} Task");

				var _scheduledTasksService = Bootstrapper.GetKernel().Resolve<IScheduledTasksService>();
				var logic = new ReportDeliveryLogic();

				/* Ok, I legit don't know what happened here. It was working and now it's kinda not.
				 * So I'm replacing the Linq query with this loop, not as fancy but it works. Also
				 * I'm pushing the join back down to the repo to improve perf. -SJ
				 */
				var allItems = await _scheduledTasksService.GetAllUpcomingOrRecurringReportDeliveryTasksAsync();

				var items = new List<(ScheduledTask ScheduledTask, Department Department, string Email)>();
				if (allItems != null && allItems.Any())
				{
					foreach (var st in allItems)
					{
						var department = new Department();
						department.DepartmentId = st.DepartmentId;
						department.TimeZone = st.DepartmentTimeZone;

						var convertedUtcNow = DateTime.UtcNow.TimeConverter(department);
						var runTime = st.WhenShouldJobBeRun(convertedUtcNow);

						if (runTime != null)
						{
							// Report delivery task runs every 14 minutes normally, so we'll pick up anything that's within 6 minutes 45 seconds of the current time
							if (runTime.Value == convertedUtcNow.Within(new TimeSpan(0, 6, 45)))
							{
								items.Add((st, department, st.UserEmailAddress));
							}
						}
					}

					if (items.Any())
					{
						_logger.LogInformation("ReportDelivery::Reports to Deliver: " + items.Count());

						foreach (var i in items)
						{
							var qi = new ReportDeliveryQueueItem();
							qi.ScheduledTask = i.ScheduledTask;
							qi.Department = i.Department;
							qi.Email = i.Email;

							_logger.LogInformation("ReportDelivery::Processing Report:" +
							                       qi.ScheduledTask.ScheduledTaskId);

							var result = await logic.Process(qi);

							if (result.Item1)
							{
								_logger.LogInformation(
									$"ReportDelivery::Processed Report {qi.ScheduledTask.ScheduledTaskId} successfully.");
							}
							else
							{
								_logger.LogInformation(
									$"ReportDelivery::Failed to Process report {qi.ScheduledTask.ScheduledTaskId} error {result.Item2}");
							}
						}
					}
				}

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
