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
	/// Rules 2 and 3 read the unit's/person's state now, so they only run for a status set now (<see cref="IsLiveStatus"/>);
	/// an offline status replayed later is left for the read-time walk, which places it by its own time.
	///
	/// On read, a call's record also shows the statuses without any destination that a unit or person dispatched to the
	/// call set while working it (<see cref="StatusDestinationSources.Inferred"/>, never stored). The walk starts at the
	/// dispatch, stops at the first status that points anywhere else, stops after the first clearing status, and ends at
	/// the call's close. Personnel paged as a group or role only count from their first engaging status, so a station's
	/// members who never turned out don't appear. When the unit/person was also dispatched to another call around the same
	/// time, a status that other call's walk would take as well could be for either call: like rule 3 it is only kept when
	/// it follows a status already on this call's record (rule 2); otherwise it is skipped, and such a clearing status ends
	/// the walk. Dispatches up to <see cref="OverlapLookback"/> apart are checked this way, so a status is not inferred onto
	/// both calls.
	/// </summary>
	public static class CallStatusAttribution
	{
		private static readonly MethodInfo MemberwiseCloneMethod =
			typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);

		/// <summary>
		/// How old a status may be and still count as set now for the write-time rules; anything older is an offline status
		/// replayed after the fact.
		/// </summary>
		public static readonly TimeSpan MaxLiveStatusAge = TimeSpan.FromMinutes(5);

		/// <summary>
		/// How long before a unit's/person's dispatch to a call their dispatch to another call is still checked for overlap. An
		/// older dispatch whose call was never closed is treated as long cleared.
		/// </summary>
		public static readonly TimeSpan OverlapLookback = TimeSpan.FromHours(24);

		/// <summary>
		/// How long before such an overlapping dispatch its call may have been logged (the calls are read by logged time).
		/// </summary>
		public static readonly TimeSpan MaxOverlapCallAge = TimeSpan.FromDays(30);

		/// <summary>
		/// True when a status was set now rather than replayed from an offline queue: it is no older than
		/// <see cref="MaxLiveStatusAge"/>. Device clocks may run ahead, so a time in the future still counts as now.
		/// </summary>
		public static bool IsLiveStatus(DateTime timestampUtc, DateTime nowUtc)
		{
			return timestampUtc >= nowUtc.Subtract(MaxLiveStatusAge);
		}

		/// <summary>
		/// One unit's/person's dispatches, one span per call, as that call's own walk sees them: from the earliest dispatch (the
		/// call's logging when the dispatch has no time) to the call's close, or now while it is open; engagement is required
		/// only when every dispatch to the call was a group or role page.
		/// </summary>
		public static List<CallDispatchSpan> DispatchSpans(IEnumerable<CallDispatchWindow> windows, DateTime nowUtc)
		{
			return (windows ?? Enumerable.Empty<CallDispatchWindow>())
				.Where(x => x != null)
				.GroupBy(x => x.CallId)
				.Select(g => new CallDispatchSpan(g.Key,
					g.Min(x => x.DispatchedOn == default(DateTime) ? x.LoggedOn : x.DispatchedOn),
					g.First().ClosedOn ?? nowUtc,
					g.All(x => x.Paged)))
				.ToList();
		}

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
		/// <param name="belongsToCall">For a row whose destination id is this call: whether it really is on this call's record
		/// (an untyped legacy row may name a station or POI whose id equals the call id); null accepts it.</param>
		/// <param name="otherDispatches">The unit's/person's dispatches to other calls (<see cref="DispatchSpans"/>); null or
		/// empty when there were none. <paramref name="rows"/> must then also cover them from their start.</param>
		public static List<T> InferForCall<T>(int callId, DateTime start, DateTime end, bool requireEngagement, IEnumerable<T> rows,
			Func<T, DateTime> timestamp, Func<T, int> id, Func<T, int?> destinationId, Func<T, int?> destinationType,
			Func<T, bool> isClearing, Action<T> markInferred, Func<T, bool> belongsToCall = null,
			IReadOnlyCollection<CallDispatchSpan> otherDispatches = null) where T : class
		{
			var inferred = new List<T>();
			if (rows == null)
				return inferred;

			var all = rows.Where(x => x != null).ToList();

			// The statuses another call's walk would take too (that walk run on its own, without this rule).
			HashSet<int> takenElsewhere = null;
			foreach (var other in otherDispatches ?? Array.Empty<CallDispatchSpan>())
			{
				if (other.CallId == callId)
					continue;

				takenElsewhere ??= new HashSet<int>();
				foreach (var row in Walk(other.CallId, other.Start, other.End, other.RequireEngagement, all, timestamp, id, destinationId, destinationType, isClearing, belongsToCall, null))
					takenElsewhere.Add(id(row));
			}

			foreach (var row in Walk(callId, start, end, requireEngagement, all, timestamp, id, destinationId, destinationType, isClearing, belongsToCall, takenElsewhere))
			{
				var copy = (T)MemberwiseCloneMethod.Invoke(row, null);
				markInferred(copy);
				inferred.Add(copy);
			}

			return inferred;
		}

		/// <summary>The rows (originals) the walk for one call takes; see <see cref="InferForCall{T}"/>.</summary>
		private static List<T> Walk<T>(int callId, DateTime start, DateTime end, bool requireEngagement, IEnumerable<T> rows,
			Func<T, DateTime> timestamp, Func<T, int> id, Func<T, int?> destinationId, Func<T, int?> destinationType,
			Func<T, bool> isClearing, Func<T, bool> belongsToCall, ISet<int> takenElsewhere) where T : class
		{
			var taken = new List<T>();
			var engaged = !requireEngagement;
			// True while the previous status in the walk is on this call's record (sent, linked or taken): the write-time
			// carry-forward rule would have kept the next status on this call whatever else was open.
			var followsThisCall = false;
			foreach (var row in rows.Where(x => timestamp(x) >= start && timestamp(x) <= end).OrderBy(timestamp).ThenBy(id))
			{
				if (destinationId(row).HasValue && destinationId(row).Value > 0)
				{
					// Already on this call's record, or pointing at another call, station or POI: the unit/person has moved on.
					if (CallStatusLinkage.LinkedCallId(destinationId(row), destinationType(row)) != callId || (belongsToCall != null && !belongsToCall(row)))
						break;

					engaged = true;
					if (isClearing(row))
						break;

					followsThisCall = true;
					continue;
				}

				var clearing = isClearing(row);

				// Another call's walk takes this status too: it could be for either call.
				if (!followsThisCall && takenElsewhere != null && takenElsewhere.Contains(id(row)))
				{
					if (clearing)
						break;

					// It still shows the unit/person turned out, so the walk never takes more than the other call's would.
					engaged = true;
					continue;
				}

				if (!engaged)
				{
					if (clearing)
						continue;

					engaged = true;
				}

				taken.Add(row);
				followsThisCall = true;

				if (clearing)
					break;
			}

			return taken;
		}

		public static List<UnitState> InferUnitStates(int callId, DateTime start, DateTime end, IEnumerable<UnitState> states, Func<UnitState, bool> isClearing,
			IReadOnlyDictionary<int, CustomStateDetail> statusLookup = null, IReadOnlyCollection<CallDispatchSpan> otherDispatches = null)
		{
			return InferForCall(callId, start, end, false, states, x => x.Timestamp, x => x.UnitStateId, x => x.DestinationId, x => x.DestinationType, isClearing,
				x =>
				{
					x.DestinationId = callId;
					x.DestinationType = (int)DestinationEntityTypes.Call;
					x.DestinationSource = (int)StatusDestinationSources.Inferred;
				},
				x => x.BelongsToCall(statusLookup), otherDispatches);
		}

		public static List<ActionLog> InferActionLogs(int callId, DateTime start, DateTime end, bool requireEngagement, IEnumerable<ActionLog> logs, Func<ActionLog, bool> isClearing,
			IReadOnlyDictionary<int, CustomStateDetail> statusLookup = null, IReadOnlyCollection<CallDispatchSpan> otherDispatches = null)
		{
			return InferForCall(callId, start, end, requireEngagement, logs, x => x.Timestamp, x => x.ActionLogId, x => x.DestinationId, x => x.DestinationType, isClearing,
				x =>
				{
					x.DestinationId = callId;
					x.DestinationType = (int)DestinationEntityTypes.Call;
					x.DestinationSource = (int)StatusDestinationSources.Inferred;
				},
				x => x.BelongsToCall(statusLookup), otherDispatches);
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
