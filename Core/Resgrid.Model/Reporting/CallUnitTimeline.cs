using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Resgrid.Model.Reporting
{
	/// <summary>
	/// A unit's whole part in one call, from its dispatch until it is back in service: every status on the call's record
	/// (sent, linked or inferred) plus the ones the unit set after it left that record. The record stops at the first status
	/// that points somewhere else (a hospital, a station), after the first clearing status and at the call's close, so a
	/// transport, the hospital turnaround and the drive back were missing from it; on the timeline they are
	/// <see cref="StatusDestinationSources.FollowOn"/> copies.
	///
	/// The statuses after the record end:
	///   - after the first one that finishes the call (back in service or out of service, the caller's rule). A release
	///     status Resgrid applied when the call closed (<see cref="StatusSetOrigins.DispatchAutomation"/>) only ends it when
	///     the crew's next status finishes the call too; a working status after it (transporting, at hospital) means the unit
	///     was still on the call;
	///   - before a status that points at another call, or one set once the unit was dispatched to another call;
	///   - <see cref="MaxFollowOn"/> after the call closed (or after its last status on the record, when that is later).
	/// The statuses on the call's record are always kept, whenever they were set.
	/// </summary>
	public static class CallUnitTimeline
	{
		private static readonly MethodInfo MemberwiseCloneMethod =
			typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);

		/// <summary>How long after the call a unit's statuses can still belong to it; a unit never put back in service ends here.</summary>
		public static readonly TimeSpan MaxFollowOn = TimeSpan.FromHours(12);

		/// <param name="callId">The call.</param>
		/// <param name="dispatchedOn">The unit's first dispatch to the call, null when it was never dispatched to it.</param>
		/// <param name="callEnd">When the call closed, or now while it is open.</param>
		/// <param name="callStates">The unit's statuses on the call's record (sent, linked and inferred).</param>
		/// <param name="unitStates">The unit's statuses from its dispatch to at least <see cref="MaxFollowOn"/> after the call.</param>
		/// <param name="nextDispatchOn">When the unit was next dispatched to another call after this dispatch, if it was.</param>
		/// <param name="finishesCall">True for a status that ends the unit's part in a call (UnitCallInvolvement.HasFinishedCall).</param>
		public static List<UnitState> Build(int callId, DateTime? dispatchedOn, DateTime callEnd, IEnumerable<UnitState> callStates,
			IEnumerable<UnitState> unitStates, DateTime? nextDispatchOn, Func<UnitState, bool> finishesCall)
		{
			var timeline = new List<UnitState>();
			var onRecord = (callStates ?? Enumerable.Empty<UnitState>())
				.Where(x => x != null)
				.GroupBy(x => x.UnitStateId)
				.ToDictionary(g => g.Key, g => g.First());

			DateTime? start = dispatchedOn;
			if (onRecord.Count > 0)
			{
				var firstOnRecord = onRecord.Values.Min(x => x.Timestamp);
				if (!start.HasValue || firstOnRecord < start.Value)
					start = firstOnRecord;
			}

			if (!start.HasValue)
				return timeline;

			var lastOnRecord = onRecord.Count > 0 ? onRecord.Values.Max(x => x.Timestamp) : start.Value;
			var followOnEnd = (lastOnRecord > callEnd ? lastOnRecord : callEnd).Add(MaxFollowOn);

			var rows = (unitStates ?? Enumerable.Empty<UnitState>())
				.Where(x => x != null && x.Timestamp >= start.Value && !onRecord.ContainsKey(x.UnitStateId))
				.Concat(onRecord.Values)
				.OrderBy(x => x.Timestamp)
				.ThenBy(x => x.UnitStateId);

			// stopped: the unit moved on (another call, or too long after), so nothing off the record follows.
			// finished: it is back in service; only the call's own statuses can still follow.
			// released: the last status was Resgrid's release at the call's close; the crew's next status decides.
			var stopped = false;
			var finished = false;
			var released = false;

			foreach (var row in rows)
			{
				var isOnRecord = onRecord.ContainsKey(row.UnitStateId);

				if (!isOnRecord)
				{
					if (stopped || finished)
						continue;

					var otherCallId = CallStatusLinkage.LinkedCallId(row.DestinationId, row.DestinationType);
					if ((otherCallId.HasValue && otherCallId.Value != callId)
						|| (nextDispatchOn.HasValue && row.Timestamp >= nextDispatchOn.Value)
						|| row.Timestamp > followOnEnd)
					{
						stopped = true;
						continue;
					}

					if (released && finishesCall(row))
					{
						finished = true;
						continue;
					}
				}

				timeline.Add(isOnRecord ? row : FollowOnCopy(row));

				if (finishesCall(row))
				{
					released = row.SetByOrigin == (int)StatusSetOrigins.DispatchAutomation;
					finished = !released;
				}
				else
				{
					released = false;
					finished = false;
				}
			}

			return timeline;
		}

		private static UnitState FollowOnCopy(UnitState state)
		{
			var copy = (UnitState)MemberwiseCloneMethod.Invoke(state, null);
			copy.DestinationSource = (int)StatusDestinationSources.FollowOn;

			return copy;
		}
	}
}
