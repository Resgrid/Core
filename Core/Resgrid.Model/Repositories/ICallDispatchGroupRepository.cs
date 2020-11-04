using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface ICallDispatchGroupRepository/
	/// Implements the <see cref="CallDispatchGroup" />
	/// </summary>
	/// <seealso cref="CallDispatchGroup" />
	public interface ICallDispatchGroupRepository: IRepository<CallDispatchGroup>
	{
		/// <summary>
		/// Gets all closed calls by department asynchronous.
		/// </summary>
		/// <param name="groupId">The group identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;CallDispatchGroup&gt;&gt;.</returns>
		Task<IEnumerable<CallDispatchGroup>> GetAllCallDispatchGroupByGroupIdAsync(int groupId);

		/// <summary>
		/// Gets all call dispatch group by call identifier asynchronous.
		/// </summary>
		/// <param name="callId">The call identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;CallDispatchGroup&gt;&gt;.</returns>
		Task<IEnumerable<CallDispatchGroup>> GetAllCallDispatchGroupByCallIdAsync(int callId);
	}
}
