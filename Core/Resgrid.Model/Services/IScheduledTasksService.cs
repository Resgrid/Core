using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Interface IScheduledTasksService
	/// </summary>
	public interface IScheduledTasksService
	{
		/// <summary>
		/// Gets the scheduled staffing tasks for user asynchronous.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <returns>Task&lt;List&lt;ScheduledTask&gt;&gt;.</returns>
		Task<List<ScheduledTask>> GetScheduledStaffingTasksForUserAsync(string userId);

		/// <summary>
		/// Gets the scheduled reporting tasks for user asynchronous.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <returns>Task&lt;List&lt;ScheduledTask&gt;&gt;.</returns>
		Task<List<ScheduledTask>> GetScheduledReportingTasksForUserAsync(string userId);

		/// <summary>
		/// Saves the scheduled task asynchronous.
		/// </summary>
		/// <param name="scheduledTask">The scheduled task.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;ScheduledTask&gt;.</returns>
		Task<ScheduledTask> SaveScheduledTaskAsync(ScheduledTask scheduledTask,
			CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Gets the scheduled task by identifier asynchronous.
		/// </summary>
		/// <param name="scheduledTaskId">The scheduled task identifier.</param>
		/// <returns>Task&lt;ScheduledTask&gt;.</returns>
		Task<ScheduledTask> GetScheduledTaskByIdAsync(int scheduledTaskId);

		/// <summary>
		/// Gets the scheduled tasks by user type asynchronous.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <param name="taskType">Type of the task.</param>
		/// <returns>Task&lt;List&lt;ScheduledTask&gt;&gt;.</returns>
		Task<List<ScheduledTask>> GetScheduledTasksByUserTypeAsync(string userId, int taskType);

		/// <summary>
		/// Deletes the department staffing reset job asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> DeleteDepartmentStaffingResetJobAsync(int departmentId,
			CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Deletes the department status reset job.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> DeleteDepartmentStatusResetJob(int departmentId,
			CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Disableds the scheduled task by identifier asynchronous.
		/// </summary>
		/// <param name="scheduledTaskId">The scheduled task identifier.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;ScheduledTask&gt;.</returns>
		Task<ScheduledTask> DisabledScheduledTaskByIdAsync(int scheduledTaskId,
			CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Enables the scheduled task by identifier asynchronous.
		/// </summary>
		/// <param name="scheduledTaskId">The scheduled task identifier.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;ScheduledTask&gt;.</returns>
		Task<ScheduledTask> EnableScheduledTaskByIdAsync(int scheduledTaskId,
			CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Deletes the scheduled task.
		/// </summary>
		/// <param name="scheduledTaskId">The scheduled task identifier.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> DeleteScheduledTask(int scheduledTaskId,
			CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Invalidates the scheduled tasks cache.
		/// </summary>
		void InvalidateScheduledTasksCache();

		/// <summary>
		/// Gets all scheduled tasks asynchronous.
		/// </summary>
		/// <param name="bypassCache">if set to <c>true</c> [bypass cache].</param>
		/// <returns>Task&lt;List&lt;ScheduledTask&gt;&gt;.</returns>
		Task<List<ScheduledTask>> GetAllScheduledTasksAsync(bool bypassCache = false);

		/// <summary>
		/// Gets all upcoming staffing scheduled tasks asynchronous.
		/// </summary>
		/// <returns>Task&lt;List&lt;ScheduledTask&gt;&gt;.</returns>
		Task<List<ScheduledTask>> GetAllUpcomingStaffingScheduledTasksAsync();

		/// <summary>
		/// Gets the upcoming scheduled tasks asynchronous.
		/// </summary>
		/// <returns>Task&lt;List&lt;ScheduledTask&gt;&gt;.</returns>
		Task<List<ScheduledTask>> GetUpcomingScheduledTasksAsync();

		/// <summary>
		/// Gets the upcoming scheduled tasks asynchronous.
		/// </summary>
		/// <param name="currentTime">The current time.</param>
		/// <param name="tasks">The tasks.</param>
		/// <returns>Task&lt;List&lt;ScheduledTask&gt;&gt;.</returns>
		Task<List<ScheduledTask>> GetUpcomingScheduledTasksAsync(DateTime currentTime, List<ScheduledTask> tasks);

		/// <summary>
		/// Gets the upcoming scheduled tasks by user identifier asynchronous.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <returns>Task&lt;List&lt;ScheduledTask&gt;&gt;.</returns>
		Task<List<ScheduledTask>> GetUpcomingScheduledTasksByUserIdAsync(string userId);

		/// <summary>
		/// Gets the upcoming scheduled tasks by user identifier task type asynchronous.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <param name="type">The type.</param>
		/// <returns>Task&lt;List&lt;ScheduledTask&gt;&gt;.</returns>
		Task<List<ScheduledTask>> GetUpcomingScheduledTasksByUserIdTaskTypeAsync(string userId, TaskTypes? type);

		/// <summary>
		/// Creates the schedule task log asynchronous.
		/// </summary>
		/// <param name="task">The task.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;ScheduledTaskLog&gt;.</returns>
		Task<ScheduledTaskLog> CreateScheduleTaskLogAsync(ScheduledTask task,
			CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Gets all upcoming status scheduled tasks asynchronous.
		/// </summary>
		/// <returns>Task&lt;List&lt;ScheduledTask&gt;&gt;.</returns>
		Task<List<ScheduledTask>> GetAllUpcomingStatusScheduledTasksAsync();

		Task<List<ScheduledTask>> GetAllUpcomingOrRecurringReportDeliveryTasksAsync();
	}
}
