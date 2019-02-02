using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using System;
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

		public Tuple<bool, string> Process(StaffingScheduleQueueItem item)
		{
			bool success = true;
			string result = "";

			if (item != null && item.ScheduledTask != null)
			{
				try
				{
					if (item.ScheduledTask.TaskType == (int)TaskTypes.UserStaffingLevel)
						_userStateService.CreateUserState(item.ScheduledTask.UserId, item.ScheduledTask.DepartmentId, int.Parse(item.ScheduledTask.Data), item.ScheduledTask.Note);
					else if (item.ScheduledTask.TaskType == (int)TaskTypes.DepartmentStaffingReset)
					{
						//var department = _departmentsService.GetDepartmentByUserId(item.ScheduledTask.UserId);
						//var users = _departmentsService.GetAllUsersForDepartment(department.DepartmentId, true);
						var users = _departmentsService.GetAllUsersForDepartment(item.ScheduledTask.DepartmentId, true);

						foreach (var user in users)
						{
							_userStateService.CreateUserState(user.UserId, item.ScheduledTask.DepartmentId, int.Parse(item.ScheduledTask.Data));
						}
					}
					else if (item.ScheduledTask.TaskType == (int)TaskTypes.DepartmentStatusReset)
					{
						//var department = _departmentsService.GetDepartmentByUserId(item.ScheduledTask.UserId);
						_actionLogsService.SetActionForEntireDepartment(item.ScheduledTask.DepartmentId, int.Parse(item.ScheduledTask.Data));
					}
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
					success = false;
					result = ex.ToString();
				}

				if (success)
					_scheduledTasksService.CreateScheduleTaskLog(item.ScheduledTask);
			}

			return new Tuple<bool, string>(success, result);
		}
	}
}
