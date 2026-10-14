using System.Collections.Generic;
using System.Linq;
using Resgrid.Model.Reporting;

namespace Resgrid.Model
{
	/// <summary>
	/// A person whose status is the built-in On Unit is riding a unit: the department turned on "personnel on a unit take
	/// the unit's status", and each unit status writes On Unit for its crew linked to that unit status
	/// (<see cref="ActionLog.UnitStateId"/>). On Unit has no meaning of its own (<see cref="BuiltInStatusBaseTypes"/>), so
	/// the person is as available as the unit: crew on a responding engine are not free to send somewhere else.
	/// </summary>
	public static class PersonnelOnUnit
	{
		/// <summary>
		/// The availability of each unit's latest status, keyed by its <see cref="UnitState.UnitStateId"/>, for following
		/// On Unit statuses to their unit.
		/// </summary>
		public static Dictionary<int, AvailabilityClass> LatestUnitAvailability(IEnumerable<UnitState> latestUnitStates,
			IReadOnlyDictionary<int, int> unitCustomBaseTypes)
		{
			var map = new Dictionary<int, AvailabilityClass>();

			foreach (var state in (latestUnitStates ?? Enumerable.Empty<UnitState>()).Where(x => x != null && x.UnitStateId > 0))
				map[state.UnitStateId] = UnitCallInvolvement.Classify(state.State, unitCustomBaseTypes);

			return map;
		}

		/// <summary>True when the status is the built-in On Unit linked to a unit status (not a department's own option).</summary>
		public static bool IsLinkedOnUnit(ActionLog log, bool isCustomStatus)
		{
			return log != null && !isCustomStatus && log.ActionTypeId == (int)ActionTypes.OnUnit && log.UnitStateId.HasValue;
		}

		/// <summary>
		/// The availability of the unit an On Unit status is linked to, when that link is still the unit's latest status;
		/// null when the status is not On Unit or the unit has moved on since (the caller keeps its own rule).
		/// </summary>
		public static AvailabilityClass? FollowUnit(ActionLog log, bool isCustomStatus, IReadOnlyDictionary<int, AvailabilityClass> latestUnitAvailability)
		{
			if (!IsLinkedOnUnit(log, isCustomStatus) || latestUnitAvailability == null)
				return null;

			return latestUnitAvailability.TryGetValue(log.UnitStateId.Value, out var availability) ? availability : (AvailabilityClass?)null;
		}
	}
}
