using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface IScheduledTaskLogsRepository
	/// Implements the <see cref="ScheduledTaskLog" />
	/// </summary>
	/// <seealso cref="ScheduledTaskLog" />
	public interface IScheduledTaskLogsRepository: IRepository<ScheduledTaskLog>
	{
		/// <summary>
		/// Gets the log for task and date asynchronous.
		/// </summary>
		/// <param name="scheduledTaskId">The scheduled task identifier.</param>
		/// <param name="timeStamp">The time stamp.</param>
		/// <returns>Task&lt;ScheduledTaskLog&gt;.</returns>
		Task<ScheduledTaskLog> GetLogForTaskAndDateAsync(int scheduledTaskId, DateTime timeStamp);

		/// <summary>
		/// Gets all log for date asynchronous.
		/// </summary>
		/// <param name="timeStamp">The time stamp.</param>
		/// <returns>Task&lt;List&lt;ScheduledTaskLog&gt;&gt;.</returns>
		Task<IEnumerable<ScheduledTaskLog>> GetAllLogForDateAsync(DateTime timeStamp);
	}
}
