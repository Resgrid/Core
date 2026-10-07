using System;
using System.Collections.Generic;

namespace Resgrid.Model
{
	/// <summary>Where a unit is measured from and how long it needs before it is moving.</summary>
	public readonly struct UnitResponseOriginResult
	{
		public UnitResponseOriginResult(GeoMath.GeoPoint? point, UnitPositionSources source, int turnoutSeconds)
		{
			Point = point;
			Source = source;
			TurnoutSeconds = turnoutSeconds;
		}

		public GeoMath.GeoPoint? Point { get; }

		public UnitPositionSources Source { get; }

		public int TurnoutSeconds { get; }
	}

	/// <summary>
	/// Places a unit and times its turnout for dispatch ranking. The run card engine and the nearest-unit board both
	/// rank on this, so a unit is measured the same way in each: response time = turnout + travel.
	/// </summary>
	public static class UnitResponseOrigin
	{
		/// <summary>Roads run roughly a third longer than the straight line between two points.</summary>
		public const double EstimatedRoadDistanceFactor = 1.3;

		/// <summary>A blended urban/suburban response speed (40 km/h) for straight-line travel estimates.</summary>
		public const double EstimatedSpeedMetersPerSecond = 40000d / 3600d;

		/// <summary>
		/// True when the unit's current status is a custom status whose base type is In Quarters. Built-in statuses are
		/// never in quarters: only ids found among the department's custom status details count.
		/// </summary>
		public static bool IsInQuarters(int stateId, IReadOnlyDictionary<int, CustomStateDetail> customDetails)
		{
			return customDetails != null
				&& customDetails.TryGetValue(stateId, out var detail)
				&& detail != null
				&& detail.BaseType == (int)ActionBaseTypes.InQuarters;
		}

		/// <summary>
		/// A unit in an In Quarters status is measured from its station whatever its GPS says: a tablet on a desk or a
		/// crew phone that went home is not where the vehicle is. Otherwise its live fix wins, and a unit with no fix is
		/// taken to be at its station. The turnout follows the position: from a station the crew still has to get to the
		/// vehicle and out the door; a unit already out only has to turn toward the call.
		/// </summary>
		public static UnitResponseOriginResult Resolve(bool inQuarters, GeoMath.GeoPoint? liveFix, GeoMath.GeoPoint? station,
			DispatchRecommendationConfig config)
		{
			var inQuartersTurnout = Math.Max(0, config?.InQuartersTurnoutSeconds ?? 0);
			var mobileTurnout = Math.Max(0, config?.MobileTurnoutSeconds ?? 0);

			if (inQuarters && station.HasValue)
				return new UnitResponseOriginResult(station, UnitPositionSources.Station, inQuartersTurnout);

			if (liveFix.HasValue)
				return new UnitResponseOriginResult(liveFix, UnitPositionSources.Live, mobileTurnout);

			if (station.HasValue)
				return new UnitResponseOriginResult(station, UnitPositionSources.Station, inQuartersTurnout);

			return new UnitResponseOriginResult(null, UnitPositionSources.None, 0);
		}

		/// <summary>Straight-line travel time at <see cref="EstimatedSpeedMetersPerSecond"/> over a road-adjusted distance.</summary>
		public static double EstimateTravelSeconds(double distanceMeters)
		{
			return Math.Round(distanceMeters * EstimatedRoadDistanceFactor / EstimatedSpeedMetersPerSecond);
		}
	}
}
