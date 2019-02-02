using System;
using System.Collections.Generic;

namespace Resgrid.Model.Repositories
{
	public interface IScheduledTaskLogsRepository : IRepository<ScheduledTaskLog>
	{
		List<ScheduledTaskLog> GetAllLogs();
		List<ScheduledTaskLog> GetAllLogsForTask(int scheduledTaskId);
		ScheduledTaskLog GetLogForTaskAndDate(int scheduledTaskId, DateTime timeStamp);
		List<ScheduledTaskLog> GetAllLogForDate(DateTime timeStamp);
	}
}
