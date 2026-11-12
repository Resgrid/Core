using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Resgrid.Web.Services.Models.v4.Routing
{
	/// <summary>A point in a route request.</summary>
	public class RoutePointInput
	{
		public double Latitude { get; set; }

		public double Longitude { get; set; }

		/// <summary>Direction of travel in degrees at this point (first point when rerouting).</summary>
		public double? Heading { get; set; }

		/// <summary>False for a pass-through point.</summary>
		public bool IsStop { get; set; } = true;
	}

	/// <summary>Body of POST Routing/GetRoute.</summary>
	public class GetRouteInput
	{
		/// <summary>Two or more points in travel order.</summary>
		public List<RoutePointInput> Points { get; set; } = new List<RoutePointInput>();

		/// <summary>RoutingProfile: 0 Driving, 1 Driving with traffic, 2 Truck, 3 Walking, 4 Cycling.</summary>
		public int Profile { get; set; }

		/// <summary>RouteAvoidOptions flags: 1 tolls, 2 highways, 4 ferries, 8 unpaved.</summary>
		public int Avoid { get; set; }

		/// <summary>Apply this unit's vehicle size and weight (must be in the caller's department).</summary>
		public int? UnitId { get; set; }

		/// <summary>Return turn-by-turn steps.</summary>
		public bool Steps { get; set; }

		/// <summary>Return navigation (voice and banner) instructions; ignored when the provider cannot navigate.</summary>
		public bool Navigation { get; set; }

		public string Language { get; set; }

		/// <summary>True for metric instructions; false (default) for imperial.</summary>
		public bool Metric { get; set; }
	}

	public class GetRouteResult : StandardApiResponseV4Base
	{
		public RouteResultData Data { get; set; }

		public GetRouteResult()
		{
			Data = new RouteResultData();
		}
	}

	public class RouteResultData
	{
		/// <summary>Provider that answered (valhalla, mapbox, google, osrm, estimate).</summary>
		public string Provider { get; set; }

		/// <summary>Attribution to show with the route.</summary>
		public string Attribution { get; set; }

		public double DistanceMeters { get; set; }

		public double DurationSeconds { get; set; }

		/// <summary>Duration without live traffic, when the provider reports it.</summary>
		public double? TypicalDurationSeconds { get; set; }

		public bool IsTrafficAware { get; set; }

		/// <summary>The vehicle's size and weight limits were applied.</summary>
		public bool VehicleRestricted { get; set; }

		/// <summary>Which vehicle limits the provider enforces: "all", "size" (height, width, weight) or "none".</summary>
		public string VehicleLimitsEnforced { get; set; }

		/// <summary>True when the route is a straight-line estimate, not a road route.</summary>
		public bool IsEstimate { get; set; }

		/// <summary>The route line as a GeoJSON LineString string, the same format the route screens already parse.</summary>
		public string Geometry { get; set; }

		public List<RouteLegData> Legs { get; set; } = new List<RouteLegData>();

		public List<RouteIncidentData> Incidents { get; set; } = new List<RouteIncidentData>();

		/// <summary>Seconds the department's active road closures added to DurationSeconds (slowdowns, detours the provider could not take); 0 when none (M0314).</summary>
		public int RoadClosureDelaySeconds { get; set; }

		/// <summary>The department's active road closures near the route and what each did to it (M0314).</summary>
		public List<RouteClosureData> RoadClosures { get; set; } = new List<RouteClosureData>();

		/// <summary>
		/// The full route in the OSRM v5 / Mapbox Directions shape (polyline6 geometry, legs, steps, maneuvers, and
		/// voiceInstructions / bannerInstructions when navigation was requested); null without steps.
		/// </summary>
		public JToken Route { get; set; }

		/// <summary>In-app navigation may use this route (Valhalla or Mapbox, navigation enabled).</summary>
		public bool NavigationAvailable { get; set; }

		/// <summary>When a client should ask again (traffic-aware routes change).</summary>
		public DateTime ExpiresUtc { get; set; }
	}

	/// <summary>A road closure as it affected a route (M0314).</summary>
	public class RouteClosureData
	{
		public string RoadClosureId { get; set; }

		public string Name { get; set; }

		/// <summary>0 avoided (routed around), 1 slowed (driven through at reduced speed), 2 crossed (the provider could not avoid it; a detour time was added).</summary>
		public int Effect { get; set; }

		public int DelaySeconds { get; set; }
	}

	public class RouteLegData
	{
		public double DistanceMeters { get; set; }

		public double DurationSeconds { get; set; }
	}

	public class RouteIncidentData
	{
		public string Type { get; set; }

		public string Description { get; set; }

		public string Impact { get; set; }

		public double? Latitude { get; set; }

		public double? Longitude { get; set; }

		public bool Closed { get; set; }
	}

	public class RoutingCapabilitiesResult : StandardApiResponseV4Base
	{
		public RoutingCapabilitiesData Data { get; set; }

		public RoutingCapabilitiesResult()
		{
			Data = new RoutingCapabilitiesData();
		}
	}

	public class RoutingCapabilitiesData
	{
		public string Provider { get; set; }

		public string Attribution { get; set; }

		public bool Steps { get; set; }

		public bool Traffic { get; set; }

		public bool Incidents { get; set; }

		public bool NavigationAvailable { get; set; }

		/// <summary>"all", "size" or "none".</summary>
		public string VehicleLimitsEnforced { get; set; }
	}

	public class ReportNavigationProgressInput
	{
		public int UnitId { get; set; }

		/// <summary>The call being navigated to; send either this or RouteInstanceId.</summary>
		public int? CallId { get; set; }

		/// <summary>The route instance being navigated; send either this or CallId.</summary>
		public string RouteInstanceId { get; set; }

		/// <summary>The route stop being navigated to (route navigation).</summary>
		public string RouteInstanceStopId { get; set; }

		/// <summary>UnitNavigationStates: Started=0, Progress=1, Rerouted=2, Arrived=3, Ended=4.</summary>
		public int State { get; set; }

		/// <summary>Estimated arrival (UTC); when omitted it is now + RemainingSeconds.</summary>
		public DateTime? EtaUtc { get; set; }

		public double RemainingSeconds { get; set; }

		public double RemainingMeters { get; set; }

		public bool IsTrafficAware { get; set; }

		/// <summary>The routing provider the app navigates with.</summary>
		public string Provider { get; set; }
	}

	public class ReportNavigationProgressResult : StandardApiResponseV4Base
	{
		public UnitNavigationData Data { get; set; }
	}

	public class GetActiveNavigationsResult : StandardApiResponseV4Base
	{
		public List<UnitNavigationData> Data { get; set; } = new List<UnitNavigationData>();
	}

	public class UnitNavigationData
	{
		public int UnitId { get; set; }

		public int? CallId { get; set; }

		public string RouteInstanceId { get; set; }

		public string RouteInstanceStopId { get; set; }

		/// <summary>UnitNavigationStates: Started=0, Progress=1, Rerouted=2, Arrived=3, Ended=4.</summary>
		public int State { get; set; }

		public DateTime EtaUtc { get; set; }

		public double RemainingSeconds { get; set; }

		public double RemainingMeters { get; set; }

		public bool IsTrafficAware { get; set; }

		public string Provider { get; set; }

		public DateTime ReportedOn { get; set; }

		/// <summary>No report for more than 60 seconds; show the ETA as stale.</summary>
		public bool IsStale { get; set; }
	}
}
