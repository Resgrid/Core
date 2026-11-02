using System;
using System.Collections.Generic;

namespace Resgrid.Model
{
	/// <summary>
	/// What a route deviation records (<c>RouteDeviations.DeviationType</c>). Persisted, so values are never renumbered.
	/// </summary>
	public enum RouteDeviationTypes
	{
		/// <summary>The unit left the planned line while navigating (reported by the navigating app, with the distance).</summary>
		OffRoute = 0,
		/// <summary>A required stop was still pending when the route ended.</summary>
		MissedStop = 1,
		/// <summary>A stop was checked in while earlier required stops were still pending.</summary>
		OutOfOrder = 2,
		/// <summary>Arrival later than the planned ETA by more than the department threshold.</summary>
		LateArrival = 3,
		/// <summary>Time at a stop longer than its estimated dwell by more than the department threshold.</summary>
		LongDwell = 4
	}

	/// <summary>A resolved route start or end point.</summary>
	public class RoutePlanEndpoint
	{
		public double Latitude { get; set; }

		public double Longitude { get; set; }

		/// <summary>The station the point came from; null for an explicit point.</summary>
		public int? DepartmentGroupId { get; set; }

		public string Name { get; set; }
	}

	/// <summary>One leg of a generated route: the travel that arrives at a stop (or at the route end).</summary>
	public class RoutePlanLeg
	{
		/// <summary>The stop the leg arrives at; null for the leg to the route end.</summary>
		public string RouteStopId { get; set; }

		/// <summary>The instance stop the leg arrives at (live instance directions only).</summary>
		public string RouteInstanceStopId { get; set; }

		public string Name { get; set; }

		public double DistanceMeters { get; set; }

		public double DurationSeconds { get; set; }

		/// <summary>Seconds from the route start (or now, for live directions) to arrival, including earlier stops' dwell.</summary>
		public double OffsetSeconds { get; set; }

		/// <summary>Arrival time (live instance directions only).</summary>
		public DateTime? EtaUtc { get; set; }
	}

	/// <summary>A route plan's route: stored, freshly generated, or live from a unit's position.</summary>
	public class RoutePlanRouteResult
	{
		public bool Successful { get; set; }

		/// <summary>True when only the straight-line estimate answered; such routes are never stored.</summary>
		public bool IsEstimate { get; set; }

		/// <summary>True when this result was written to the plan.</summary>
		public bool Stored { get; set; }

		public string Provider { get; set; }

		public string Attribution { get; set; }

		/// <summary>Travel distance (no dwell).</summary>
		public double DistanceMeters { get; set; }

		/// <summary>Travel time (no dwell).</summary>
		public double DurationSeconds { get; set; }

		/// <summary>Travel time plus every stop's estimated dwell: start to arrival at the end.</summary>
		public double DurationWithDwellSeconds { get; set; }

		public bool IsTrafficAware { get; set; }

		public bool VehicleRestricted { get; set; }

		/// <summary>GeoJSON LineString string.</summary>
		public string GeometryGeoJson { get; set; }

		/// <summary>Normalized route (OSRM v5 / Mapbox Directions shape, polyline6) with legs and steps; null when unavailable.</summary>
		public string DirectionsJson { get; set; }

		public RoutePlanEndpoint Start { get; set; }

		public RoutePlanEndpoint End { get; set; }

		public List<RoutePlanLeg> Legs { get; set; } = new List<RoutePlanLeg>();

		public List<RouteIncident> Incidents { get; set; } = new List<RouteIncident>();

		public DateTime? ComputedOn { get; set; }

		/// <summary>Why no route came back, for logs and error codes; never shown to users verbatim.</summary>
		public string Error { get; set; }
	}

	/// <summary>A proposed stop order for a route plan; nothing is written until it is applied.</summary>
	public class RoutePlanOptimizationPreview
	{
		public bool Successful { get; set; }

		public string Provider { get; set; }

		public List<string> CurrentOrder { get; set; } = new List<string>();

		public List<string> ProposedOrder { get; set; } = new List<string>();

		public double CurrentDistanceMeters { get; set; }

		public double CurrentDurationSeconds { get; set; }

		public double ProposedDistanceMeters { get; set; }

		public double ProposedDurationSeconds { get; set; }

		public bool Changed { get; set; }

		public string Error { get; set; }
	}
}
