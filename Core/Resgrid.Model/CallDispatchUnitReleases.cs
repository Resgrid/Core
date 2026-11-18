using System;
using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model
{
	/// <summary>
	/// Rules for a call's unit dispatch list once a unit can be released (stood down) from a call that stays open
	/// (<see cref="CallDispatchUnit.ReleasedOn"/>, M0316). A released dispatch stays on the record; edits of the dispatch list
	/// never delete it, and putting the unit back on the list re-dispatches it.
	/// </summary>
	public static class CallDispatchUnitReleases
	{
		/// <summary>The dispatches still live: every one except units released from the call.</summary>
		public static List<CallDispatchUnit> Active(IEnumerable<CallDispatchUnit> dispatches)
		{
			return (dispatches ?? Enumerable.Empty<CallDispatchUnit>()).Where(x => x != null && !x.IsReleased).ToList();
		}

		/// <summary>The units still live on the call (see <see cref="Active"/>).</summary>
		public static HashSet<int> ActiveUnitIds(IEnumerable<CallDispatchUnit> dispatches)
		{
			return new HashSet<int>(Active(dispatches).Select(x => x.UnitId));
		}

		/// <summary>
		/// Applies an edited unit list to a call's unit dispatches in place. A live unit missing from <paramref name="unitIds"/>
		/// is removed (when <paramref name="canRemove"/> allows it); a released unit missing from it stays, released, because
		/// the record of its dispatch and stand-down is history, not a choice the editor made. A listed unit already live is
		/// kept as it is, a listed released unit is re-dispatched, and a listed unit with no dispatch is added (when
		/// <paramref name="canAdd"/> allows it).
		/// </summary>
		/// <returns>The units re-dispatched from released; the caller treats them like newly added units (notify, dispatch status).</returns>
		public static List<int> ApplyUnitList(ICollection<CallDispatchUnit> dispatches, int callId, IEnumerable<int> unitIds,
			DateTime nowUtc, Func<int, bool> canRemove = null, Func<int, bool> canAdd = null)
		{
			if (dispatches == null)
				throw new ArgumentNullException(nameof(dispatches));

			var wanted = new HashSet<int>(unitIds ?? Enumerable.Empty<int>());
			var redispatched = new List<int>();

			foreach (var dispatch in dispatches.Where(x => x != null && !x.IsReleased && !wanted.Contains(x.UnitId)).ToList())
			{
				if (canRemove == null || canRemove(dispatch.UnitId))
					dispatches.Remove(dispatch);
			}

			foreach (var unitId in wanted)
			{
				var existing = dispatches.Where(x => x != null && x.UnitId == unitId).ToList();

				if (existing.Any(x => !x.IsReleased))
					continue;

				if (existing.Count > 0)
				{
					existing.OrderByDescending(x => x.DispatchedOn).First().Redispatch(nowUtc);
					redispatched.Add(unitId);
					continue;
				}

				if (canAdd == null || canAdd(unitId))
					dispatches.Add(new CallDispatchUnit { CallId = callId, UnitId = unitId });
			}

			return redispatched;
		}
	}
}
