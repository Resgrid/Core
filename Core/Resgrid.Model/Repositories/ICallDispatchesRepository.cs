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

		/// <summary>
		/// Gets the ids of the department's open (active, not deleted) calls the user is dispatched to, directly or through a
		/// dispatched group they belong to or a dispatched role they hold.
		/// </summary>
		Task<IEnumerable<int>> GetOpenCallIdsForUserAsync(int departmentId, string userId);

		/// <summary>
		/// Gets the personnel dispatches of every department call logged in the range (UTC, inclusive).
		/// </summary>
		Task<IEnumerable<CallDispatch>> GetCallDispatchesForCallsInRangeAsync(int departmentId, DateTime startDate, DateTime endDate);

		/// <summary>
		/// Gets the personnel dispatches made in the range (UTC, inclusive) — direct, or through a dispatched group the user
		/// belongs to or a dispatched role they hold — with each call's logged and closed times, on department calls logged
		/// from <paramref name="loggedFrom"/>.
		/// </summary>
		Task<IEnumerable<CallDispatchWindow>> GetPersonnelDispatchWindowsAsync(int departmentId, DateTime startDate, DateTime endDate, DateTime loggedFrom);
	}
}
