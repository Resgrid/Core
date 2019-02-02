using System;
using System.Collections.Generic;

namespace Resgrid.Model.Services
{
	public interface IScheduledTasksService
	{
		List<ScheduledTask> GetScheduledStaffingTasksForUser(string userId);
		ScheduledTask SaveScheduledTask(ScheduledTask scheduledTask);
		ScheduledTask GetScheduledTaskById(int scheduledTaskId);
		void DisabledScheduledTaskById(int scheduledTaskId);
		void EnableScheduledTaskById(int scheduledTaskId);
		void DeleteScheduledTask(int scheduledTaskId);
		List<ScheduledTask> GetUpcomingScheduledTaks();
		List<ScheduledTask> GetAllUpcomingStaffingScheduledTaks();
		void CreateScheduleTaskLog(ScheduledTask task);
		List<ScheduledTask> GetUpcomingScheduledTasksByUserId(string userId);
		List<ScheduledTask> GetUpcomingScheduledTaks(DateTime currentTime, List<ScheduledTask> tasks);
		List<ScheduledTask> GetScheduledTasksByUserType(string userId, int taskType);
		void DeleteDepartmentStaffingResetJob(int departmentId);
		List<ScheduledTask> GetUpcomingScheduledTasksByUserIdTaskType(string userId, TaskTypes? type);
		List<ScheduledTask> GetScheduledReportingTasksForUser(string userId);
		void DeleteDepartmentStatusResetJob(int departmentId);
		List<ScheduledTask> GetAllScheduledTasks(bool bypassCache = false);
		void InvalidateScheduledTasksCache();
	}
}