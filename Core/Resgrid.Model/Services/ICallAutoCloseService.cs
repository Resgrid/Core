using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Closes an active call when its last dispatched unit reports back in service or out of service, for departments that
	/// turned on <see cref="DepartmentSettingTypes.CloseCallWhenUnitsClear"/>.
	/// </summary>
	public interface ICallAutoCloseService
	{
		/// <summary>
		/// Runs after a unit status is saved. When the status takes the unit from working a call to back in service or out of
		/// service, and every other unit dispatched to that call has also reported finished since its dispatch, the call is
		/// closed (release statuses applied, call-closed event raised, nobody notified). A call under an active incident
		/// command stays open. Never throws; returns the ids of the calls it closed.
		/// </summary>
		/// <param name="saved">The unit status just saved (with its destination after call attribution).</param>
		/// <param name="previous">The unit's status before it.</param>
		Task<List<int>> CloseCallsFinishedByUnitStateAsync(int departmentId, UnitState saved, UnitState previous,
			CancellationToken cancellationToken = default(CancellationToken));
	}
}
