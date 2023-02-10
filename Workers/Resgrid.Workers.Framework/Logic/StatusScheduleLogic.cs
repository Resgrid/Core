using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using System;
using System.Threading.Tasks;
using Autofac;

namespace Resgrid.Workers.Framework.Logic
{
	public class StatusScheduleLogic
	{
		private IUserStateService _userStateService;
		private IScheduledTasksService _scheduledTasksService;
		private IDepartmentsService _departmentsService;
		private IActionLogsService _actionLogsService;

		public StatusScheduleLogic()
		{
			_userStateService = Bootstrapper.GetKernel().Resolve<IUserStateService>();
			_scheduledTasksService = Bootstrapper.GetKernel().Resolve<IScheduledTasksService>();
			_departmentsService = Bootstrapper.GetKernel().Resolve<IDepartmentsService>();
			_actionLogsService = Bootstrapper.GetKernel().Resolve<IActionLogsService>();
		}

		public async Task<Tuple<bool, string>> Process(StatusScheduleQueueItem item)
		{
			bool success = true;
			string result = "";

			if (item != null && item.ScheduledTask != null)
			{
				try
				{
					if (item.ScheduledTask.TaskType == (int)TaskTypes.DepartmentStatusReset)
					{
						await _actionLogsService.SetActionForEntireDepartmentAsync(item.ScheduledTask.DepartmentId, int.Parse(item.ScheduledTask.Data), $"Department Status Reset {item.ScheduledTask.ScheduledTaskId}");
					}
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
					success = false;
					result = ex.ToString();
				}

				if (success)
					await _scheduledTasksService.CreateScheduleTaskLogAsync(item.ScheduledTask);
			}

			return new Tuple<bool, string>(success, result);
		}
	}
}
