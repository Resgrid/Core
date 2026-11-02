using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Providers;

namespace Resgrid.Providers.GeoLocationProvider.Routing
{
	/// <summary>
	/// Straight-line estimate (plan §6.1): great-circle distance × 1.3 at 40 km/h (UnitResponseOrigin), the same numbers
	/// the nearest-unit board shows when no routed time is available. No external service, so it is always configured
	/// and is the last fallback. No steps, so never navigation.
	/// </summary>
	public class EstimateRoutingProvider : IRoutingProvider
	{
		public string Name => RoutingProviderNames.Estimate;

		public RoutingCapabilities Capabilities { get; } = new RoutingCapabilities
		{
			Steps = false,
			Navigation = false,
			Traffic = false,
			Incidents = false,
			Matrix = true,
			Optimize = true,
			VehicleSize = false,
			VehicleFull = false,
			MaxMatrixCoordinates = int.MaxValue,
			MaxWaypoints = int.MaxValue,
			MaxOptimizeWaypoints = 200
		};

		public bool IsConfigured => true;

		public Task<RouteResult> GetRouteAsync(RouteRequest request, CancellationToken cancellationToken)
		{
			var result = new RouteResult { Successful = request.Waypoints.Count >= 2, Provider = Name };

			for (var i = 1; i < request.Waypoints.Count; i++)
			{
				var meters = GeoMath.HaversineMeters(request.Waypoints[i - 1].Latitude, request.Waypoints[i - 1].Longitude, request.Waypoints[i].Latitude, request.Waypoints[i].Longitude);
				var leg = new RouteLegSummary
				{
					DistanceMeters = meters * UnitResponseOrigin.EstimatedRoadDistanceFactor,
					DurationSeconds = UnitResponseOrigin.EstimateTravelSeconds(meters)
				};
				result.Legs.Add(leg);
				result.DistanceMeters += leg.DistanceMeters;
				result.DurationSeconds += leg.DurationSeconds;
			}

			result.GeometryGeoJson = PolylineCodec.ToGeoJsonLineString(request.Waypoints.Select(w => (w.Latitude, w.Longitude)).ToList());
			return Task.FromResult(result);
		}

		public Task<RouteMatrixResult> GetMatrixAsync(RouteMatrixRequest request, CancellationToken cancellationToken)
		{
			var durations = new double?[request.Origins.Count][];
			var distances = new double?[request.Origins.Count][];

			for (var i = 0; i < request.Origins.Count; i++)
			{
				durations[i] = new double?[request.Destinations.Count];
				distances[i] = new double?[request.Destinations.Count];

				for (var j = 0; j < request.Destinations.Count; j++)
				{
					var meters = GeoMath.HaversineMeters(request.Origins[i].Latitude, request.Origins[i].Longitude, request.Destinations[j].Latitude, request.Destinations[j].Longitude);
					durations[i][j] = UnitResponseOrigin.EstimateTravelSeconds(meters);
					distances[i][j] = meters * UnitResponseOrigin.EstimatedRoadDistanceFactor;
				}
			}

			return Task.FromResult(new RouteMatrixResult { Successful = true, Provider = Name, Durations = durations, Distances = distances });
		}

		public Task<RouteOptimizationResult> OptimizeAsync(RouteOptimizationRequest request, CancellationToken cancellationToken)
		{
			// RoutingService runs its matrix heuristic for providers without a real optimizer; doing it here would only
			// duplicate that code.
			return Task.FromResult(new RouteOptimizationResult { Provider = Name, Error = "Estimate uses the service heuristic" });
		}
	}
}
