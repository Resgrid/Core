using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Resolves which part of a department a user dispatches, for departments that turned on
	/// group-scoped dispatch (<see cref="GroupDispatchScopeConfig"/>). With it off, every user is
	/// department-wide and nothing changes.
	/// <para>
	/// With it on: department admins and holders of a configured department-wide role see the
	/// whole department (a central dispatch center); everyone else sees their own group and the
	/// groups beneath it (an area supervisor over the stations and units in their area). A call belongs to that slice when
	/// its location falls inside one of those groups' boundaries, or one of those groups, their
	/// units or their members was dispatched on it. A user always keeps the calls they reported or
	/// were dispatched to.
	/// </para>
	/// <para>
	/// Nothing is stamped on the call: scope is recomputed from roles and membership on every
	/// request, so a hand-off between dispatch desks is a role change, not a data change.
	/// </para>
	/// </summary>
	public interface IDispatchScopeService
	{
		/// <summary>The user's scope right now. Never null.</summary>
		Task<DispatchScope> GetScopeForUserAsync(int departmentId, string userId);

		/// <summary>Whether the call is in the scope. Loads the call's dispatches when they aren't populated yet.</summary>
		Task<bool> IsCallInScopeAsync(DispatchScope scope, Call call);

		/// <summary>Convenience for <see cref="GetScopeForUserAsync"/> + <see cref="IsCallInScopeAsync"/>.</summary>
		Task<bool> CanUserAccessCallAsync(int departmentId, string userId, Call call);

		/// <summary>The calls in the scope, in their original order. Returns the same list when department-wide.</summary>
		Task<List<Call>> FilterCallsAsync(DispatchScope scope, List<Call> calls);

		/// <summary>Convenience for <see cref="GetScopeForUserAsync"/> + <see cref="FilterCallsAsync"/>.</summary>
		Task<List<Call>> FilterCallsForUserAsync(int departmentId, string userId, List<Call> calls);
	}
}
