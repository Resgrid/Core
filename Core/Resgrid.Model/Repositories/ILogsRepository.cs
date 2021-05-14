using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface ILogsRepository
	/// Implements the <see cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.Log}" />
	/// </summary>
	/// <seealso cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.Log}" />
	public interface ILogsRepository: IRepository<Log>
	{
		/// <summary>
		/// Gets the logs for user asynchronous.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;Log&gt;&gt;.</returns>
		Task<IEnumerable<Log>> GetLogsForUserAsync(string userId);

		/// <summary>
		/// Gets the logs for call asynchronous.
		/// </summary>
		/// <param name="callId">The call identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;Log&gt;&gt;.</returns>
		Task<IEnumerable<Log>> GetLogsForCallAsync(int callId);

		/// <summary>
		/// Gets the logs for group asynchronous.
		/// </summary>
		/// <param name="groupId">The group identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;Log&gt;&gt;.</returns>
		Task<IEnumerable<Log>> GetLogsForGroupAsync(int groupId);

		/// <summary>
		/// Gets the log years for department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;string&gt;&gt;.</returns>
		Task<IEnumerable<string>> SelectLogYearsByDeptAsync(int departmentId);

		/// <summary>
		/// Gets the logs for a department by year asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="year">The year to get logs for.</param>
		/// <returns>Task&lt;IEnumerable&lt;Log&gt;&gt;.</returns>
		Task<IEnumerable<Log>> GetAllLogsByDepartmentIdYearAsync(int departmentId, string year);
	}
}
