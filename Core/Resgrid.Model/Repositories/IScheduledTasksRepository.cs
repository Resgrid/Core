using System.Collections.Generic;

namespace Resgrid.Model.Repositories
{
	public interface IScheduledTasksRepository : IRepository<ScheduledTask>
	{
		List<ScheduledTask> GetAllTasks();
		List<ScheduledTask> GetAllActiveTasksForTypes(List<int> types);
	}
}