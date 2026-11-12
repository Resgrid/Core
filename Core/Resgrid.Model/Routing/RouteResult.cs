using System;
using System.Collections.Generic;

namespace Resgrid.Model
{
	/// <summary>One route from a routing provider, normalized so callers never depend on the provider.</summary>
	public class RouteResult
	{
		public bool Successful { get; set; }

		/// <summary>The provider that produced the route (<see cref="RoutingProviderNames"/>).</summary>
		public string Provider { get; set; }

		/// <summary>Attribution the provider requires wherever the route is shown.</summary>
		public string Attribution { get; set; }

		public double DistanceMeters { get; set; }

		public double DurationSeconds { get; set; }

		/// <summary>Duration without live traffic when the provider reports both (Mapbox duration_typical).</summary>
		public double? TypicalDurationSeconds { get; set; }

		/// <summary>True when the duration reflects live traffic.</summary>
		public bool IsTrafficAware { get; set; }

		/// <summary>True when the route was computed with the vehicle's size and weight limits.</summary>
		public bool VehicleRestricted { get; set; }

		/// <summary>The route line as a GeoJSON LineString string ([lon, lat] positions).</summary>
		public string GeometryGeoJson { get; set; }

		/// <summary>One leg per pair of consecutive stops.</summary>
		public List<RouteLegSummary> Legs { get; set; } = new List<RouteLegSummary>();

		/// <summary>
		/// The full route in the OSRM v5 / Mapbox Directions v5 shape (route object: distance, duration, geometry as
		/// polyline6, legs with steps, maneuvers, voiceInstructions and bannerInstructions when requested, incidents
		/// when the provider reports them). Clients that navigate or print directions read this; null when steps
		/// were not requested.
		/// </summary>
		public string NormalizedJson { get; set; }

		/// <summary>Incidents along the route (Mapbox driving-traffic only).</summary>
		public List<RouteIncident> Incidents { get; set; } = new List<RouteIncident>();

		/// <summary>Why the lookup failed, for logs; never shown to users.</summary>
		public string Error { get; set; }

		/// <summary>Seconds road closures added to <see cref="DurationSeconds"/> (slowdowns, and detours the provider could not take).</summary>
		public int ClosureDelaySeconds { get; set; }

		/// <summary>The road closures near the route and what each did to it.</summary>
		public List<RouteClosureEffect> Closures { get; set; } = new List<RouteClosureEffect>();

		public DateTime ComputedOn { get; set; } = DateTime.UtcNow;
	}

	public class RouteLegSummary
	{
		public double DistanceMeters { get; set; }

		public double DurationSeconds { get; set; }
	}

	/// <summary>A traffic incident on the route (accident, construction, closure, ...).</summary>
	public class RouteIncident
	{
		/// <summary>Provider type, e.g. accident, construction, road_closure, congestion, lane_restriction.</summary>
		public string Type { get; set; }

		public string Description { get; set; }

		/// <summary>low, minor, major, critical or unknown.</summary>
		public string Impact { get; set; }

		public double? Latitude { get; set; }

		public double? Longitude { get; set; }

		public bool Closed { get; set; }
	}

	public class RouteMatrixResult
	{
		public bool Successful { get; set; }

		public string Provider { get; set; }

		/// <summary>[origin][destination] travel seconds; null when no route exists or the pair failed.</summary>
		public double?[][] Durations { get; set; }

		/// <summary>[origin][destination] metres; null when no route exists or the pair failed.</summary>
		public double?[][] Distances { get; set; }

		public string Error { get; set; }
	}

	public class RouteOptimizationResult
	{
		public bool Successful { get; set; }

		public string Provider { get; set; }

		/// <summary>Indexes into the request's waypoints, in the recommended visiting order.</summary>
		public List<int> Order { get; set; } = new List<int>();

		public double DistanceMeters { get; set; }

		public double DurationSeconds { get; set; }

		public string Error { get; set; }
	}
}
