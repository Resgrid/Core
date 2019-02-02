using System;
using System.Collections.Generic;

namespace Resgrid.Model.Repositories
{
	public interface IActionLogsRepository : IRepository<ActionLog>
	{
		ActionLog GetActionlogById(int actionLogId);
		List<ActionLog> GetLastActionLogsForDepartment(int departmentId, bool disableAutoAvailable, DateTime timeStamp);
	}
}