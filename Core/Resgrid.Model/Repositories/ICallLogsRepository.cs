using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface ICallLogsRepository
	/// Implements the <see cref="CallLog" />
	/// </summary>
	/// <seealso cref="CallLog" />
	public interface ICallLogsRepository: IRepository<CallLog>
	{
		/// <summary>
		/// Gets the logs for user asynchronous.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;CallLog&gt;&gt;.</returns>
		Task<IEnumerable<CallLog>> GetLogsForUserAsync(string userId);

		/// <summary>
		/// Gets the logs for call asynchronous.
		/// </summary>
		/// <param name="callId">The call identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;CallLog&gt;&gt;.</returns>
		Task<IEnumerable<CallLog>> GetLogsForCallAsync(int callId);
	}
}
