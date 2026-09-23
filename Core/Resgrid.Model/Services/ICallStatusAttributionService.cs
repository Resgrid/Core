using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Ties unit states and personnel statuses to calls when the client didn't say which call they were for, and finds the
	/// statuses a call's dispatched units and personnel set while working it. See <see cref="CallStatusAttribution"/> for
	/// the rules.
	/// </summary>
	public interface ICallStatusAttributionService
	{
		/// <summary>
		/// Before a unit state is saved: marks a sent destination as explicit, or links the state to the previous state's
		/// still-open call or to the one open call the unit is dispatched to; a state replayed from an offline queue is left
		/// unlinked for the read-time walk. Never throws; a failure saves the state as sent.
		/// </summary>
		Task AttributeUnitStateAsync(UnitState state, UnitState previousState, int departmentId);

		/// <summary>
		/// Before a personnel status is saved: marks a sent destination as explicit, or links the status to the call of the
		/// unit state that placed the person on a unit, the previous status's still-open call, or the one open call the
		/// person is dispatched to; a status replayed from an offline queue is only linked through its unit state. Never
		/// throws; a failure saves the status as sent.
		/// </summary>
		Task AttributeActionLogAsync(ActionLog actionLog, ActionLog previousActionLog);

		/// <summary>
		/// Statuses with no destination that the call's dispatched units set while working it, as inferred copies linked
		/// to the call (<see cref="StatusDestinationSources.Inferred"/>).
		/// </summary>
		Task<List<UnitState>> GetInferredUnitStatesForCallAsync(int departmentId, int callId);

		/// <summary>
		/// Statuses with no destination that the call's dispatched personnel (directly, or through a dispatched group or
		/// role once they engaged) set while working it, as inferred copies linked to the call.
		/// </summary>
		Task<List<ActionLog>> GetInferredActionLogsForCallAsync(int departmentId, int callId);

		/// <summary>
		/// For reports over many calls: each call's unit states, linked and inferred, from one read of each department
		/// unit's history instead of per-call queries. Units are populated on every row.
		/// </summary>
		Task<Dictionary<int, List<UnitState>>> GetUnitStatesForCallsAsync(int departmentId, IReadOnlyCollection<Call> calls);

		/// <summary>
		/// For reports over many calls: each call's personnel statuses, linked and inferred, from one read of the
		/// department's statuses and dispatches for the period instead of per-call queries.
		/// </summary>
		Task<Dictionary<int, List<ActionLog>>> GetActionLogsForCallsAsync(int departmentId, IReadOnlyCollection<Call> calls);

		/// <summary>
		/// The unit dispatches of the given calls (all logged in one department), keyed by call.
		/// </summary>
		Task<Dictionary<int, List<CallDispatchUnit>>> GetUnitDispatchesForCallsAsync(int departmentId, IReadOnlyCollection<Call> calls);
	}
}
