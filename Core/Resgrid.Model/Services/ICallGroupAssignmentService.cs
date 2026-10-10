using System.Collections.Generic;
using System.Threading.Tasks;
using Resgrid.Model.Reporting;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Places calls in department groups for reporting: by the group geofence holding the call's location, else by the
	/// plurality of what was dispatched to it. See <see cref="CallGroupAssigner"/> for the rules.
	/// </summary>
	public interface ICallGroupAssignmentService
	{
		/// <summary>
		/// Each call's group, keyed by call id. Reads the department's groups, units and the dispatches of the calls' period
		/// once, not per call.
		/// </summary>
		/// <param name="departmentId">Calls from any other department are left out of the result.</param>
		/// <param name="calls">The calls to place.</param>
		/// <param name="responders">
		/// Optional, by call id: the units and personnel that set a status on the call. Only consulted for a call where
		/// nothing dispatched belongs to a group.
		/// </param>
		Task<Dictionary<int, CallGroupAssignment>> AssignCallsToGroupsAsync(int departmentId, IReadOnlyCollection<Call> calls,
			IReadOnlyDictionary<int, CallGroupResources> responders = null);
	}
}
