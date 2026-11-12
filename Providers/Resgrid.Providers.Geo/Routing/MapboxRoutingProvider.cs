using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Providers;

namespace Resgrid.Providers.GeoLocationProvider.Routing
{
	/// <summary>
	/// Mapbox Directions v5, Matrix v1 and Optimization v1 (plan §6.1) with the server-side token
	/// (MappingConfig.MapboxRoutingAccessToken). Live traffic and incidents on driving-traffic. Applies vehicle height,
	/// width and weight (max_height/max_width/max_weight); Mapbox has no length, axle or hazmat restrictions.
	/// </summary>
	public class MapboxRoutingProvider : IRoutingProvider
	{
		private const string BaseUrl = "https://api.mapbox.com";
		private const string AttributionText = "© Mapbox © OpenStreetMap contributors";

		public string Name => RoutingProviderNames.Mapbox;

		public RoutingCapabilities Capabilities { get; } = new RoutingCapabilities
		{
			Steps = true,
			Navigation = true,
			Traffic = true,
			Incidents = true,
			Matrix = true,
			Optimize = true,
			VehicleSize = true,
			VehicleFull = false,
			MaxMatrixCoordinates = 25,
			MaxWaypoints = 25,
			MaxOptimizeWaypoints = 12,
			// Directions "exclude=point(lon lat)": snapped to the nearest road, at most 50, driving profiles only (beta).
			MaxExcludePoints = 50,
			ExcludePointsDrivingOnly = true
		};

		public bool IsConfigured => !string.IsNullOrWhiteSpace(MappingConfig.MapboxRoutingAccessToken);

		public async Task<RouteResult> GetRouteAsync(RouteRequest request, CancellationToken cancellationToken)
		{
			var profile = Profile(request.Profile);
			var query = new StringBuilder();
			query.Append("?access_token=").Append(WebUtility.UrlEncode(MappingConfig.MapboxRoutingAccessToken));
			query.Append("&geometries=polyline6&overview=full");
			query.Append("&steps=").Append(request.Steps || request.NavigationInstructions ? "true" : "false");

			if (request.NavigationInstructions)
			{
				query.Append("&voice_instructions=true&banner_instructions=true&roundabout_exits=true");
				query.Append("&voice_units=").Append(request.Units == RoutingUnits.Imperial ? "imperial" : "metric");
			}

			if (!string.IsNullOrWhiteSpace(request.Language))
				query.Append("&language=").Append(WebUtility.UrlEncode(request.Language));

			if (profile == "driving-traffic" && (request.Steps || request.NavigationInstructions))
				query.Append("&annotations=congestion,duration,distance,maxspeed");

			AppendExclude(query, request.Profile, request.Avoid, request.ExcludePoints);
			var restricted = AppendVehicle(query, request.Profile, request.Vehicle);
			AppendBearings(query, request.Waypoints);
			AppendStopIndexes(query, request.Waypoints);

			var url = $"{BaseUrl}/directions/v5/mapbox/{profile}/{RoutingHttp.LonLatPath(request.Waypoints)}{query}";
			var (_, status, document) = await RoutingHttp.GetJsonAsync(url, cancellationToken);

			using (document)
			{
				var result = OsrmShapeParser.Parse(document, Name, AttributionText, request.Steps || request.NavigationInstructions);
				if (!result.Successful && string.IsNullOrEmpty(result.Error))
					result.Error = $"Directions failed ({status})";

				result.IsTrafficAware = profile == "driving-traffic";
				result.VehicleRestricted = restricted;
				return result;
			}
		}

		public async Task<RouteMatrixResult> GetMatrixAsync(RouteMatrixRequest request, CancellationToken cancellationToken)
		{
			var profile = Profile(request.Profile);
			// driving-traffic matrices take at most 10 coordinates, the others 25 (origins + destinations).
			var limit = profile == "driving-traffic" ? 10 : 25;
			var perChunk = Math.Max(1, limit - request.Destinations.Count);

			if (request.Destinations.Count >= limit)
				return new RouteMatrixResult { Provider = Name, Error = "Too many destinations for one Mapbox matrix" };

			var durations = new double?[request.Origins.Count][];
			var distances = new double?[request.Origins.Count][];

			for (var start = 0; start < request.Origins.Count; start += perChunk)
			{
				var origins = request.Origins.Skip(start).Take(perChunk).ToList();
				var coordinates = origins.Concat(request.Destinations).ToList();
				var sources = string.Join(";", Enumerable.Range(0, origins.Count));
				var destinations = string.Join(";", Enumerable.Range(origins.Count, request.Destinations.Count));

				var query = new StringBuilder();
				query.Append("?access_token=").Append(WebUtility.UrlEncode(MappingConfig.MapboxRoutingAccessToken));
				query.Append("&annotations=duration,distance");
				query.Append("&sources=").Append(sources).Append("&destinations=").Append(destinations);

				var url = $"{BaseUrl}/directions-matrix/v1/mapbox/{profile}/{RoutingHttp.LonLatPath(coordinates)}{query}";
				var (ok, status, document) = await RoutingHttp.GetJsonAsync(url, cancellationToken);

				using (document)
				{
					if (!ok || document == null)
						return new RouteMatrixResult { Provider = Name, Error = $"Matrix failed ({status})" };

					var chunkDurations = OsrmShapeParser.ReadTable(document.RootElement, "durations");
					var chunkDistances = OsrmShapeParser.ReadTable(document.RootElement, "distances");

					for (var i = 0; i < origins.Count; i++)
					{
						durations[start + i] = chunkDurations != null && i < chunkDurations.Length ? chunkDurations[i] : new double?[request.Destinations.Count];
						distances[start + i] = chunkDistances != null && i < chunkDistances.Length ? chunkDistances[i] : new double?[request.Destinations.Count];
					}
				}
			}

			return new RouteMatrixResult { Successful = true, Provider = Name, Durations = durations, Distances = distances };
		}

		public async Task<RouteOptimizationResult> OptimizeAsync(RouteOptimizationRequest request, CancellationToken cancellationToken)
		{
			if (request.Waypoints.Count > Capabilities.MaxOptimizeWaypoints)
				return new RouteOptimizationResult { Provider = Name, Error = "Too many stops for Mapbox optimization" };

			if (!request.KeepFirst || !request.KeepLast)
				return new RouteOptimizationResult { Provider = Name, Error = "Only fixed start and end are supported" };

			var profile = Profile(request.Profile);
			var query = new StringBuilder();
			query.Append("?access_token=").Append(WebUtility.UrlEncode(MappingConfig.MapboxRoutingAccessToken));
			query.Append("&source=first&destination=last&roundtrip=false&overview=false");

			var url = $"{BaseUrl}/optimized-trips/v1/mapbox/{profile}/{RoutingHttp.LonLatPath(request.Waypoints)}{query}";
			var (ok, status, document) = await RoutingHttp.GetJsonAsync(url, cancellationToken);

			using (document)
			{
				if (!ok || document == null)
					return new RouteOptimizationResult { Provider = Name, Error = $"Optimization failed ({status})" };

				var order = OsrmShapeParser.ReadTripOrder(document.RootElement);
				if (order == null)
					return new RouteOptimizationResult { Provider = Name, Error = "Optimization returned no order" };

				var result = new RouteOptimizationResult { Successful = true, Provider = Name, Order = order };
				if (document.RootElement.TryGetProperty("trips", out var trips) && trips.GetArrayLength() > 0)
				{
					result.DistanceMeters = OsrmShapeParser.GetDouble(trips[0], "distance");
					result.DurationSeconds = OsrmShapeParser.GetDouble(trips[0], "duration");
				}

				return result;
			}
		}

		private static string Profile(RoutingProfile profile)
		{
			switch (profile)
			{
				case RoutingProfile.Walking:
					return "walking";
				case RoutingProfile.Cycling:
					return "cycling";
				case RoutingProfile.DrivingTraffic:
					return MappingConfig.RoutingTrafficAware ? "driving-traffic" : "driving";
				default:
					return "driving";
			}
		}

		public static void AppendExclude(StringBuilder query, RoutingProfile profile, RouteAvoidOptions avoid, IReadOnlyList<RoutingWaypoint> excludePoints = null)
		{
			if (profile == RoutingProfile.Walking || profile == RoutingProfile.Cycling)
				return;

			var exclude = new List<string>();

			// Road closures (M0314): points on closed roads, each snapped by Mapbox to the nearest road.
			foreach (var point in (excludePoints ?? new List<RoutingWaypoint>()).Take(50))
				exclude.Add(string.Format(CultureInfo.InvariantCulture, "point({0:0.######}%20{1:0.######})", point.Longitude, point.Latitude));

			if (avoid.HasFlag(RouteAvoidOptions.Tolls))
				exclude.Add("toll");
			if (avoid.HasFlag(RouteAvoidOptions.Highways))
				exclude.Add("motorway");
			if (avoid.HasFlag(RouteAvoidOptions.Ferries))
				exclude.Add("ferry");
			if (avoid.HasFlag(RouteAvoidOptions.Unpaved))
				exclude.Add("unpaved");

			if (exclude.Count > 0)
				query.Append("&exclude=").Append(string.Join(",", exclude));
		}

		private static bool AppendVehicle(StringBuilder query, RoutingProfile profile, VehicleProfile vehicle)
		{
			if (vehicle == null || profile == RoutingProfile.Walking || profile == RoutingProfile.Cycling)
				return false;

			var applied = false;
			if (vehicle.HeightMeters.HasValue)
			{
				query.Append("&max_height=").Append(vehicle.HeightMeters.Value.ToString("0.##", CultureInfo.InvariantCulture));
				applied = true;
			}
			if (vehicle.WidthMeters.HasValue)
			{
				query.Append("&max_width=").Append(vehicle.WidthMeters.Value.ToString("0.##", CultureInfo.InvariantCulture));
				applied = true;
			}
			if (vehicle.GrossWeightKg.HasValue)
			{
				// Metric tons.
				query.Append("&max_weight=").Append((vehicle.GrossWeightKg.Value / 1000d).ToString("0.###", CultureInfo.InvariantCulture));
				applied = true;
			}

			return applied;
		}

		private static void AppendBearings(StringBuilder query, List<RoutingWaypoint> waypoints)
		{
			if (waypoints.Count == 0 || !waypoints[0].Heading.HasValue)
				return;

			var bearings = new List<string> { $"{Math.Round(((waypoints[0].Heading.Value % 360) + 360) % 360).ToString(CultureInfo.InvariantCulture)},45" };
			bearings.AddRange(Enumerable.Repeat(string.Empty, waypoints.Count - 1));
			query.Append("&bearings=").Append(WebUtility.UrlEncode(string.Join(";", bearings)));
		}

		private static void AppendStopIndexes(StringBuilder query, List<RoutingWaypoint> waypoints)
		{
			if (waypoints.All(w => w.IsStop))
				return;

			var indexes = waypoints
				.Select((w, i) => new { w, i })
				.Where(x => x.i == 0 || x.i == waypoints.Count - 1 || x.w.IsStop)
				.Select(x => x.i.ToString(CultureInfo.InvariantCulture));

			query.Append("&waypoints=").Append(string.Join(";", indexes));
		}
	}
}
