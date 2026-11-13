namespace Resgrid.Model
{
	/// <summary>What a routing provider can do; drives fallbacks on the server and features shown in the clients.</summary>
	public class RoutingCapabilities
	{
		/// <summary>Turn-by-turn steps (printed directions).</summary>
		public bool Steps { get; set; }

		/// <summary>
		/// Navigation-grade voice and banner instructions that the licence allows to drive in-app guidance. Only
		/// Valhalla and Mapbox (plan D26): OSRM has no instructions, Google's terms forbid real-time guidance.
		/// </summary>
		public bool Navigation { get; set; }

		/// <summary>Durations reflect live traffic.</summary>
		public bool Traffic { get; set; }

		/// <summary>Reports incidents along the route.</summary>
		public bool Incidents { get; set; }

		public bool Matrix { get; set; }

		public bool Optimize { get; set; }

		/// <summary>Applies vehicle height, width and weight.</summary>
		public bool VehicleSize { get; set; }

		/// <summary>Also applies vehicle length, axle load, axle count and hazmat.</summary>
		public bool VehicleFull { get; set; }

		/// <summary>Most coordinates (origins + destinations) in one matrix request.</summary>
		public int MaxMatrixCoordinates { get; set; } = 25;

		/// <summary>Most waypoints in one route request.</summary>
		public int MaxWaypoints { get; set; } = 25;

		/// <summary>Most waypoints the optimizer accepts in one request.</summary>
		public int MaxOptimizeWaypoints { get; set; } = 12;

		/// <summary>Routes around drawn areas (<see cref="RouteRequest.ExcludeAreas"/>; Valhalla only).</summary>
		public bool ExcludeAreas { get; set; }

		/// <summary>Most road points a route can be told to avoid (<see cref="RouteRequest.ExcludePoints"/>); 0 when it can't.</summary>
		public int MaxExcludePoints { get; set; }

		/// <summary>Point exclusions only work for driving profiles (Mapbox).</summary>
		public bool ExcludePointsDrivingOnly { get; set; }
	}
}
