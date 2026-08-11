using System.Collections.Generic;
using System.Threading;
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

		/// <summary>
		/// Deletes every scheduled task (and their logs) owned by a user, across all departments.
		/// Used when a user account is deleted/deactivated.
		/// </summary>
		Task<bool> DeleteAllTasksForUserAsync(string userId, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Deletes every scheduled task (and their logs) owned by a user that is scoped to a single
		/// department. Legacy rows with DepartmentId = 0 are left alone; the active-task queries
		/// resolve those through non-deleted department memberships.
		/// </summary>
		Task<bool> DeleteAllTasksForUserInDepartmentAsync(string userId, int departmentId, CancellationToken cancellationToken = default(CancellationToken));
	}
}
