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
	}

	/// <summary>
	/// Per-unit dispatched / en route / on scene / staging / cleared times for a call from its unit dispatches and the unit
	/// states on the call's record (linked and inferred). Custom statuses count through their base type. Cleared is the
	/// first clearing status (available, returning, released, cancelled, out of service) at or after the unit engaged (or
	/// after dispatch when it never did).
	/// </summary>
	public static class CallUnitTimesCalculator
	{
		public static List<CallUnitTimesRow> Compute(IEnumerable<CallDispatchUnit> dispatches, IEnumerable<UnitState> states, IReadOnlyDictionary<int, int> customBaseTypes)
		{
			var dispatchList = (dispatches ?? Enumerable.Empty<CallDispatchUnit>()).Where(x => x != null).ToList();
			var stateList = (states ?? Enumerable.Empty<UnitState>()).Where(x => x != null).ToList();

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

				rows.Add(row);
			}

			return rows;
		}
	}
}
