using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Resgrid.Model
{
	/// <summary>
	/// Rules that tie statuses to a call when the client didn't say which call they were for.
	///
	/// On write (the status is saved with the result, see <see cref="StatusDestinationSources"/>):
	///   1. a destination the client sent is kept as is;
	///   2. otherwise the call of the unit's/person's previous status is kept when that call is still open and the
	///      previous status wasn't a clearing one (the new status may itself be clearing: that records the clear time);
	///   3. otherwise, for a non-clearing status, the one open call the unit/person is dispatched to (other than a call
	///      their previous, clearing status already cleared); more than one candidate leaves the status unlinked.
	///
	/// On read, a call's record also shows the statuses without any destination that a unit or person dispatched to the
	/// call set while working it (<see cref="StatusDestinationSources.Inferred"/>, never stored). The walk starts at the
	/// dispatch, stops at the first status that points anywhere else, stops after the first clearing status, and ends at
	/// the call's close. Personnel paged as a group or role only count from their first engaging status, so a station's
	/// members who never turned out don't appear.
	/// </summary>
	public static class CallStatusAttribution
	{
		private static readonly MethodInfo MemberwiseCloneMethod =
			typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);

		/// <summary>
		/// Rule 2: the previous status's call is kept when it is linked, still open and the previous status wasn't clearing.
		/// </summary>
		public static bool CanCarryForward(int? previousCallId, bool previousIsClearing, bool previousCallOpen)
		{
			return previousCallId.HasValue && previousCallOpen && !previousIsClearing;
		}

		/// <summary>
		/// Rule 3: the one open call the unit/person is dispatched to, ignoring the call their previous (clearing) status
		/// already cleared. Null when there is none or more than one.
		/// </summary>
		public static int? PickDispatchCall(IEnumerable<int> openDispatchedCallIds, int? clearedCallId)
		{
			if (openDispatchedCallIds == null)
				return null;

			var candidates = openDispatchedCallIds.Where(x => x > 0 && (!clearedCallId.HasValue || x != clearedCallId.Value)).Distinct().Take(2).ToList();

			return candidates.Count == 1 ? candidates[0] : (int?)null;
		}

		/// <summary>
		/// The inferred statuses one dispatched unit or person contributes to a call's record.
		/// </summary>
		/// <param name="callId">The call.</param>
		/// <param name="start">When the unit/person was dispatched to the call.</param>
		/// <param name="end">When the call closed, or now while it is open.</param>
		/// <param name="requireEngagement">True for group/role paging: nothing counts before the first non-clearing status.</param>
		/// <param name="rows">The unit's/person's statuses (any order); rows outside [start, end] are ignored.</param>
		public static List<T> InferForCall<T>(int callId, DateTime start, DateTime end, bool requireEngagement, IEnumerable<T> rows,
			Func<T, DateTime> timestamp, Func<T, int> id, Func<T, int?> destinationId, Func<T, int?> destinationType,
			Func<T, bool> isClearing, Action<T> markInferred) where T : class
		{
			var inferred = new List<T>();
			if (rows == null)
				return inferred;

			var engaged = !requireEngagement;
			foreach (var row in rows.Where(x => x != null && timestamp(x) >= start && timestamp(x) <= end).OrderBy(timestamp).ThenBy(id))
			{
				if (destinationId(row).HasValue && destinationId(row).Value > 0)
				{
					// Already on this call's record, or pointing at another call, station or POI: the unit/person has moved on.
					if (CallStatusLinkage.LinkedCallId(destinationId(row), destinationType(row)) != callId)
						break;

					engaged = true;
					if (isClearing(row))
						break;

					continue;
				}

				var clearing = isClearing(row);
				if (!engaged)
				{
					if (clearing)
						continue;

					engaged = true;
				}

				var copy = (T)MemberwiseCloneMethod.Invoke(row, null);
				markInferred(copy);
				inferred.Add(copy);

				if (clearing)
					break;
			}

			return inferred;
		}

		public static List<UnitState> InferUnitStates(int callId, DateTime start, DateTime end, IEnumerable<UnitState> states, Func<UnitState, bool> isClearing)
		{
			return InferForCall(callId, start, end, false, states, x => x.Timestamp, x => x.UnitStateId, x => x.DestinationId, x => x.DestinationType, isClearing,
				x =>
				{
					x.DestinationId = callId;
					x.DestinationType = (int)DestinationEntityTypes.Call;
					x.DestinationSource = (int)StatusDestinationSources.Inferred;
				});
		}

		public static List<ActionLog> InferActionLogs(int callId, DateTime start, DateTime end, bool requireEngagement, IEnumerable<ActionLog> logs, Func<ActionLog, bool> isClearing)
		{
			return InferForCall(callId, start, end, requireEngagement, logs, x => x.Timestamp, x => x.ActionLogId, x => x.DestinationId, x => x.DestinationType, isClearing,
				x =>
				{
					x.DestinationId = callId;
					x.DestinationType = (int)DestinationEntityTypes.Call;
					x.DestinationSource = (int)StatusDestinationSources.Inferred;
				});
		}

		/// <summary>True when a status row reached the call's record without the client naming the call.</summary>
		public static bool IsAutoLinked(int? destinationSource)
		{
			return destinationSource == (int)StatusDestinationSources.CarryForward || destinationSource == (int)StatusDestinationSources.Dispatch
				|| destinationSource == (int)StatusDestinationSources.Unit;
		}

		public static bool IsInferred(int? destinationSource)
		{
			return destinationSource == (int)StatusDestinationSources.Inferred;
		}
	}
}
