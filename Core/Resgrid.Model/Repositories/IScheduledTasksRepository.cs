using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface IScheduledTasksRepository
	/// Implements the <see cref="ScheduledTask" />
	/// </summary>
	/// <seealso cref="ScheduledTask" />
	public interface IScheduledTasksRepository: IRepository<ScheduledTask>
	{
		/// <summary>
		/// Gets all active tasks for types asynchronous.
		/// </summary>
		/// <param name="types">The types.</param>
		/// <returns>Task&lt;IEnumerable&lt;ScheduledTask&gt;&gt;.</returns>
		Task<IEnumerable<ScheduledTask>> GetAllActiveTasksForTypesAsync(List<int> types);

		Task<IEnumerable<ScheduledTask>> GetAllUpcomingOrRecurringReportDeliveryTasksAsync();
	}
}
