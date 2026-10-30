using System;
using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model.Reporting
{
	/// <summary>How the times on a call unit times row were established, most trustworthy first.</summary>
	public enum CallUnitTimesSources
	{
		/// <summary>Every time came from statuses the sender linked to the call.</summary>
		Explicit = 0,

		/// <summary>At least one time came from a status Resgrid linked to the call on save (carried forward, dispatch, unit).</summary>
		AutoLinked = 1,

		/// <summary>At least one time came from an inferred status (dispatched unit, no destination, while working the call).</summary>
		Inferred = 2,

		/// <summary>The unit was dispatched but has no status on the call.</summary>
		DispatchOnly = 3
	}

	/// <summary>One unit's key times on one call.</summary>
	public class CallUnitTimesRow
	{
		public int UnitId { get; set; }
		public DateTime? DispatchedOn { get; set; }
		public DateTime? EnrouteOn { get; set; }
		public DateTime? OnSceneOn { get; set; }
		public DateTime? StagingOn { get; set; }
		public DateTime? ClearedOn { get; set; }
		public CallUnitTimesSources Source { get; set; }

		/// <summary>Every status from dispatch until the unit was back in service (<see cref="CallUnitTimeline"/>), oldest first.</summary>
		public List<CallUnitTimelineEntry> Timeline { get; set; } = new List<CallUnitTimelineEntry>();

		/// <summary>When the timeline ends with the unit back in service (or out of service); null while it is still on the call.</summary>
		public DateTime? InServiceOn { get; set; }

		/// <summary>From dispatch (or its first status) to <see cref="InServiceOn"/>.</summary>
		public double? CommittedSeconds { get; set; }
	}

	/// <summary>One status on a unit's call timeline.</summary>
	public class CallUnitTimelineEntry
	{
		public int UnitStateId { get; set; }

		/// <summary>The raw state: a built-in <see cref="UnitStateTypes"/> value or a custom status detail id.</summary>
		public int State { get; set; }

		public DateTime Timestamp { get; set; }

		/// <summary>Until the next status on the timeline; null for the last one.</summary>
		public double? DurationSeconds { get; set; }

		/// <summary>How the status is tied to the call (<see cref="StatusDestinationSources"/>); FollowOn when it is only on the timeline.</summary>
		public int? DestinationSource { get; set; }

		public int? DestinationId { get; set; }

		public int? DestinationType { get; set; }

		/// <summary>Resgrid set it (the call's dispatch or release statuses) rather than a person.</summary>
		public bool SetByAutomation { get; set; }

		public bool IsFollowOn => DestinationSource == (int)StatusDestinationSources.FollowOn;
	}

	/// <summary>
	/// Per-unit dispatched / en route / on scene / staging / cleared times for a call from its unit dispatches and the unit
	/// states on the call's record (linked and inferred). Custom statuses count through their base type. Cleared is the
	/// first clearing status (available, returning, released, cancelled, out of service) at or after the unit engaged (or
	/// after dispatch when it never did).
	/// </summary>
	public static class CallUnitTimesCalculator
	{
		/// <param name="timeline">
		/// The call's unit timelines (<see cref="CallUnitTimeline"/>), all units together; null leaves every row's timeline
		/// empty. Its statuses on the call's record (every one that isn't FollowOn) are the ones the key times come from, so
		/// <paramref name="states"/> may be null when it is given.
		/// </param>
		public static List<CallUnitTimesRow> Compute(IEnumerable<CallDispatchUnit> dispatches, IEnumerable<UnitState> states, IReadOnlyDictionary<int, int> customBaseTypes,
			IEnumerable<UnitState> timeline = null)
		{
			var dispatchList = (dispatches ?? Enumerable.Empty<CallDispatchUnit>()).Where(x => x != null).ToList();
			var timelineList = timeline?.Where(x => x != null).ToList();
			var stateList = (states ?? timelineList?.Where(x => x.DestinationSource != (int)StatusDestinationSources.FollowOn) ?? Enumerable.Empty<UnitState>())
				.Where(x => x != null).ToList();

			var unitIds = dispatchList.Select(x => x.UnitId).Concat(stateList.Select(x => x.UnitId)).Distinct();
			var rows = new List<CallUnitTimesRow>();

			foreach (var unitId in unitIds)
			{
				var unitDispatches = dispatchList.Where(x => x.UnitId == unitId).ToList();
				var ordered = stateList.Where(x => x.UnitId == unitId).OrderBy(x => x.Timestamp).ThenBy(x => x.UnitStateId).ToList();

				UnitStateTypes? KindOf(UnitState s) => CallStatusLinkage.ResolveUnitStateKind(s.State, customBaseTypes);
				UnitState First(params UnitStateTypes[] kinds) => ordered.FirstOrDefault(s => KindOf(s) is UnitStateTypes kind && kinds.Contains(kind));

				var row = new CallUnitTimesRow { UnitId = unitId };
				row.DispatchedOn = unitDispatches.Count > 0 ? unitDispatches.Min(x => x.DispatchedOn) : (DateTime?)null;

				var enroute = First(UnitStateTypes.Responding, UnitStateTypes.Enroute);
				var onScene = First(UnitStateTypes.OnScene);
				var staging = First(UnitStateTypes.Staging);

				var engagedAt = new[] { enroute?.Timestamp, onScene?.Timestamp, staging?.Timestamp }.Where(x => x.HasValue).Select(x => x.Value).DefaultIfEmpty(DateTime.MinValue).Min();
				var clearFrom = engagedAt != DateTime.MinValue ? engagedAt : (row.DispatchedOn ?? DateTime.MinValue);
				var cleared = ordered.FirstOrDefault(s => s.Timestamp >= clearFrom && CallStatusLinkage.IsClearingUnitState(s.State, customBaseTypes));

				row.EnrouteOn = enroute?.Timestamp;
				row.OnSceneOn = onScene?.Timestamp;
				row.StagingOn = staging?.Timestamp;
				row.ClearedOn = cleared?.Timestamp;

				var used = new[] { enroute, onScene, staging, cleared }.Where(x => x != null).ToList();
				if (used.Count == 0)
					row.Source = CallUnitTimesSources.DispatchOnly;
				else if (used.Any(x => CallStatusAttribution.IsInferred(x.DestinationSource)))
					row.Source = CallUnitTimesSources.Inferred;
				else if (used.Any(x => CallStatusAttribution.IsAutoLinked(x.DestinationSource)))
					row.Source = CallUnitTimesSources.AutoLinked;
				else
					row.Source = CallUnitTimesSources.Explicit;

				if (timelineList != null)
					FillTimeline(row, timelineList.Where(x => x.UnitId == unitId), customBaseTypes);

				rows.Add(row);
			}

			return rows;
		}

		private static void FillTimeline(CallUnitTimesRow row, IEnumerable<UnitState> states, IReadOnlyDictionary<int, int> customBaseTypes)
		{
			var ordered = states.OrderBy(x => x.Timestamp).ThenBy(x => x.UnitStateId).ToList();

			for (var i = 0; i < ordered.Count; i++)
			{
				var state = ordered[i];

				row.Timeline.Add(new CallUnitTimelineEntry
				{
					UnitStateId = state.UnitStateId,
					State = state.State,
					Timestamp = state.Timestamp,
					DurationSeconds = i + 1 < ordered.Count ? (ordered[i + 1].Timestamp - state.Timestamp).TotalSeconds : (double?)null,
					DestinationSource = state.DestinationSource,
					DestinationId = state.DestinationId,
					DestinationType = state.DestinationType,
					SetByAutomation = state.SetByOrigin == (int)StatusSetOrigins.DispatchAutomation
				});
			}

			var last = ordered.LastOrDefault();
			if (last == null || !UnitCallInvolvement.HasFinishedCall(last.State, customBaseTypes))
				return;

			row.InServiceOn = last.Timestamp;

			var from = row.DispatchedOn ?? ordered[0].Timestamp;
			if (row.InServiceOn.Value >= from)
				row.CommittedSeconds = (row.InServiceOn.Value - from).TotalSeconds;
		}
	}
}
