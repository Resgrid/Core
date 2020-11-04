using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface ILogUsersRepository
	/// Implements the <see cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.LogUser}" />
	/// </summary>
	/// <seealso cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.LogUser}" />
	public interface ILogUsersRepository: IRepository<LogUser>
	{
		/// <summary>
		/// Gets the logs for user asynchronous.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;LogUser&gt;&gt;.</returns>
		Task<IEnumerable<LogUser>> GetLogsForUserAsync(string userId);

		/// <summary>
		/// Gets the logs by log identifier asynchronous.
		/// </summary>
		/// <param name="logId">The log identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;LogUser&gt;&gt;.</returns>
		Task<IEnumerable<LogUser>> GetLogsByLogIdAsync(int logId);
	}
}
