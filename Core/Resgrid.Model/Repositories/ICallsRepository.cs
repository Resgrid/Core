using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface ICallsRepository
	/// Implements the <see cref="Call" />
	/// </summary>
	/// <seealso cref="Call" />
	public interface ICallsRepository: IRepository<Call>
	{
		Task<IEnumerable<Call>> SearchCallCandidatesAsync(int departmentId, CallSearchQuery query);

		/// <summary>
		/// Gets all calls by department date range asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="startDate">The start date.</param>
		/// <param name="endDate">The end date.</param>
		/// <returns>Task&lt;IEnumerable&lt;Call&gt;&gt;.</returns>
		Task<IEnumerable<Call>> GetAllCallsByDepartmentDateRangeAsync(int departmentId, DateTime startDate, DateTime endDate);

		/// <summary>
		/// Gets all closed calls by department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;Call&gt;&gt;.</returns>
		Task<IEnumerable<Call>> GetAllClosedCallsByDepartmentAsync(int departmentId);

		/// <summary>
		/// Gets all closed calls by department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// /// <param name="year">The year.</param>
		/// <returns>Task&lt;IEnumerable&lt;Call&gt;&gt;.</returns>
		Task<IEnumerable<Call>> GetAllClosedCallsByDepartmentYearAsync(int departmentId, string year);

		/// <summary>
		/// Gets all open calls by department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;Call&gt;&gt;.</returns>
		Task<IEnumerable<Call>> GetAllOpenCallsByDepartmentAsync(int departmentId);

		/// <summary>
		/// Gets all calls by department identifier logged on asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="loggedOn">The logged on.</param>
		/// <returns>Task&lt;IEnumerable&lt;Call&gt;&gt;.</returns>
		Task<IEnumerable<Call>> GetAllCallsByDepartmentIdLoggedOnAsync(int departmentId, DateTime loggedOn);

		/// <summary>
		/// Gets all years a call was logged in for a department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;string&gt;&gt;.</returns>
		Task<IEnumerable<string>> SelectCallYearsByDeptAsync(int departmentId);

		/// <summary>
		/// Gets all calls by date range that are to be dispatched asynchronous.
		/// </summary>
		/// <param name="startDate">The start date.</param>
		/// <param name="endDate">The end date.</param>
		/// <returns>Task&lt;IEnumerable&lt;Call&gt;&gt;.</returns>
		Task<IEnumerable<Call>> GetAllNonDispatchedScheduledCallsWithinDateRange(DateTime startDate, DateTime endDate);

		/// <summary>
		/// Gets all non-dispatched scheduled calls by department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;Call&gt;&gt;.</returns>
		Task<IEnumerable<Call>> GetAllNonDispatchedScheduledCallsByDepartmentIdAsync(int departmentId);

		/// <summary>
		/// Gets the department's pending calls (<see cref="CallStates.Pending"/>): saved, not deleted and waiting for a dispatcher.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		Task<IEnumerable<Call>> GetPendingCallsByDepartmentIdAsync(int departmentId);

		/// <summary>
		/// Gets all calls by department and contact asynchronous.
		/// </summary>
		/// <param name="contactId">The contact identifier.</param>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;Call&gt;&gt;.</returns>
		Task<IEnumerable<Call>> GetAllCallsByContactIdAsync(string contactId, int departmentId);

		Task<int> GetCallsCountByDepartmentDateRangeAsync(int departmentId, DateTime startDate, DateTime endDate);

		/// <summary>
		/// Gets all active calls for a department that have check-in timers enabled
		/// and that the specified user has been dispatched on.
		/// Optimised as a single JOIN query to avoid N+1 lookups.
		/// </summary>
		/// <param name="userId">The identity user identifier to filter dispatches by.</param>
		/// <param name="departmentId">The department identifier (used to scope the result).</param>
		/// <returns>Active calls with check-in timers that the user is dispatched on.</returns>
		Task<IEnumerable<Call>> GetActiveCallsWithCheckInTimersForUserAsync(string userId, int departmentId);

		/// <summary>
		/// Sets Calls.SubjectIdentifiers only when the stored value is still <paramref name="expectedValue"/> (null matches
		/// NULL): a concurrent edit is never overwritten by a stale merge. True when the row was updated.
		/// </summary>
		Task<bool> TryUpdateSubjectIdentifiersAsync(int callId, int departmentId, string expectedValue, string newValue,
			System.Threading.CancellationToken cancellationToken = default);

		/// <summary>
		/// Marks a call dispatched (HasBeenDispatched) only when the stored row is still waiting: pending, or active with a
		/// scheduled dispatch not yet sent. True for the one caller that changed the row; a concurrent Dispatch Now or the
		/// scheduled-calls worker gets false and must not broadcast.
		/// </summary>
		Task<bool> TryClaimCallForDispatchAsync(int callId, int departmentId, System.Threading.CancellationToken cancellationToken = default);

		/// <summary>
		/// Gives back a claim from <see cref="TryClaimCallForDispatchAsync"/> when the dispatch did not go out, restoring the
		/// waiting state. Only a row that is still claimed and still active or pending is changed, so a call closed in the
		/// meantime stays closed. True when the row was updated.
		/// </summary>
		Task<bool> ReleaseCallDispatchClaimAsync(int callId, int departmentId, int state, DateTime? dispatchOn, bool? hasBeenDispatched,
			System.Threading.CancellationToken cancellationToken = default);
	}
}
