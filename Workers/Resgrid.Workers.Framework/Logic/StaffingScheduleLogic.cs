using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using System;
using System.Threading.Tasks;
using Autofac;

namespace Resgrid.Workers.Framework.Logic
{
	public class StaffingScheduleLogic
	{
		private IUserStateService _userStateService;
		private IScheduledTasksService _scheduledTasksService;
		private IDepartmentsService _departmentsService;
		private IActionLogsService _actionLogsService;

		public StaffingScheduleLogic()
		{
			_userStateService = Bootstrapper.GetKernel().Resolve<IUserStateService>();
			_scheduledTasksService = Bootstrapper.GetKernel().Resolve<IScheduledTasksService>();
			_departmentsService = Bootstrapper.GetKernel().Resolve<IDepartmentsService>();
			_actionLogsService = Bootstrapper.GetKernel().Resolve<IActionLogsService>();
		}

		public async Task<Tuple<bool, string>> Process(StaffingScheduleQueueItem item)
		{
			bool success = true;
			string result = "";

			if (item != null && item.ScheduledTask != null)
			{
				try
				{
					if (item.ScheduledTask.TaskType == (int)TaskTypes.UserStaffingLevel)
					{
						await _userStateService.CreateUserState(item.ScheduledTask.UserId, item.ScheduledTask.DepartmentId, int.Parse(item.ScheduledTask.Data), item.ScheduledTask.Note);
					}
					else if (item.ScheduledTask.TaskType == (int)TaskTypes.DepartmentStaffingReset)
					{
						var users = await _departmentsService.GetAllUsersForDepartment(item.ScheduledTask.DepartmentId, true);

						foreach (var user in users)
						{
							await _userStateService.CreateUserState(user.UserId, item.ScheduledTask.DepartmentId, int.Parse(item.ScheduledTask.Data), $"Department Staffing Reset {item.ScheduledTask.ScheduledTaskId}");
						}
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
