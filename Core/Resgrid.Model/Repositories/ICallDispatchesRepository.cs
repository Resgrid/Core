using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface ICallDispatchesRepository
	/// Implements the <see cref="CallDispatch" />
	/// </summary>
	/// <seealso cref="CallDispatch" />
	public interface ICallDispatchesRepository: IRepository<CallDispatch>
	{
		/// <summary>
		/// Marks the call dispatches as sent by call identifier users asynchronous.
		/// </summary>
		/// <param name="callId">The call identifier.</param>
		/// <param name="usersToMark">The users to mark.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> MarkCallDispatchesAsSentByCallIdUsersAsync(int callId, List<Guid> usersToMark);

		/// <summary>
		/// Gets the call dispatches by call identifier asynchronous.
		/// </summary>
		/// <param name="callId">The call identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;CallDispatch&gt;&gt;.</returns>
		Task<IEnumerable<CallDispatch>> GetCallDispatchesByCallIdAsync(int callId);
	}
}
