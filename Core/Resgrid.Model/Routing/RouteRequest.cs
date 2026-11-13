using System.Collections.Generic;

namespace Resgrid.Model
{
	/// <summary>A point a route passes through, in travel order.</summary>
	public class RoutingWaypoint
	{
		public RoutingWaypoint()
		{
		}

		public RoutingWaypoint(double latitude, double longitude)
		{
			Latitude = latitude;
			Longitude = longitude;
		}

		public double Latitude { get; set; }

		public double Longitude { get; set; }

		/// <summary>Direction of travel at this point in degrees (0 = north); used for the first point when rerouting.</summary>
		public double? Heading { get; set; }

		/// <summary>False for a pass-through point (no leg ends here, no arrival instruction).</summary>
		public bool IsStop { get; set; } = true;
	}

	/// <summary>A request for one route through two or more waypoints.</summary>
	public class RouteRequest
	{
		/// <summary>The department asking; its own routing account (Mapping Settings) is tried first. 0 = system providers only.</summary>
		public int DepartmentId { get; set; }

		public List<RoutingWaypoint> Waypoints { get; set; } = new List<RoutingWaypoint>();

		public RoutingProfile Profile { get; set; } = RoutingProfile.Driving;

		/// <summary>Size and weight limits; ignored for walking and cycling and by providers that cannot apply them.</summary>
		public VehicleProfile Vehicle { get; set; }

		public RouteAvoidOptions Avoid { get; set; } = RouteAvoidOptions.None;

		/// <summary>Return legs with turn-by-turn steps (printed directions, navigation).</summary>
		public bool Steps { get; set; }

		/// <summary>Return spoken and on-screen (banner) instructions for in-app navigation.</summary>
		public bool NavigationInstructions { get; set; }

		/// <summary>BCP-47 language for instructions (e.g. "en", "de").</summary>
		public string Language { get; set; } = "en";

		public RoutingUnits Units { get; set; } = RoutingUnits.Imperial;

		/// <summary>Skip the cache (navigation reroutes, traffic refreshes).</summary>
		public bool BypassCache { get; set; }

		/// <summary>
		/// Road closures in effect for this trip: full ones are routed around where the provider can, partial ones (and a full
		/// one the trip starts or ends in) slow the route down. Empty for callers that don't apply closures.
		/// </summary>
		public List<RoutingClosure> Closures { get; set; } = new List<RoutingClosure>();

		/// <summary>Set by the routing service for the provider: areas the route must not enter (Valhalla exclude_polygons).</summary>
		public List<List<RoutingWaypoint>> ExcludeAreas { get; set; } = new List<List<RoutingWaypoint>>();

		/// <summary>Set by the routing service for the provider: points on roads the route must not use (Valhalla exclude_locations, Mapbox exclude=point).</summary>
		public List<RoutingWaypoint> ExcludePoints { get; set; } = new List<RoutingWaypoint>();
	}

	/// <summary>Travel times and distances between every origin and every destination.</summary>
	public class RouteMatrixRequest
	{
		/// <summary>The department asking; its own routing account (Mapping Settings) is tried first. 0 = system providers only.</summary>
		public int DepartmentId { get; set; }

		public List<RoutingWaypoint> Origins { get; set; } = new List<RoutingWaypoint>();

		public List<RoutingWaypoint> Destinations { get; set; } = new List<RoutingWaypoint>();

		public RoutingProfile Profile { get; set; } = RoutingProfile.Driving;

		public VehicleProfile Vehicle { get; set; }

		public RouteAvoidOptions Avoid { get; set; } = RouteAvoidOptions.None;
	}

	/// <summary>Best visiting order for a set of stops.</summary>
	public class RouteOptimizationRequest
	{
		/// <summary>The department asking; its own routing account (Mapping Settings) is tried first. 0 = system providers only.</summary>
		public int DepartmentId { get; set; }

		/// <summary>All points in their current order, including the start and end.</summary>
		public List<RoutingWaypoint> Waypoints { get; set; } = new List<RoutingWaypoint>();

		/// <summary>The first waypoint stays first (route start).</summary>
		public bool KeepFirst { get; set; } = true;

		/// <summary>The last waypoint stays last (route end).</summary>
		public bool KeepLast { get; set; } = true;

		public RoutingProfile Profile { get; set; } = RoutingProfile.Driving;

		public VehicleProfile Vehicle { get; set; }

		public RouteAvoidOptions Avoid { get; set; } = RouteAvoidOptions.None;
	}
}
