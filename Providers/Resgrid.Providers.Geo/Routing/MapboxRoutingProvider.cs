using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
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
	/// (MappingConfig.MapboxRoutingAccessToken), else the web app's Mapbox map token. Live traffic and incidents on
	/// driving-traffic. Applies vehicle height, width and weight (max_height/max_width/max_weight); Mapbox has no length,
	/// axle or hazmat restrictions.
	/// </summary>
	public class MapboxRoutingProvider : IRoutingProvider
	{
		private const string BaseUrl = "https://api.mapbox.com";
		private const string AttributionText = "© Mapbox © OpenStreetMap contributors";

		// A department's own Mapbox account (Mapping Settings); null = the install's token.
		private readonly RoutingProviderCredentials _department;

		public MapboxRoutingProvider()
		{
		}

		private MapboxRoutingProvider(RoutingProviderCredentials department)
		{
			_department = department;
		}

		public IRoutingProvider WithCredentials(RoutingProviderCredentials credentials)
		{
			return credentials == null ? null : new MapboxRoutingProvider(credentials);
		}

		private string AccessToken => _department != null ? _department.ApiKey : InstallToken().Token;

		/// <summary>
		/// The install's token for Directions, Matrix and Optimization: MapboxRoutingAccessToken, else the web app's Mapbox
		/// map token (WebsiteMapboxAccessToken, then WebsiteMapboxKey), which those APIs accept as well — an install with
		/// Mapbox maps routes on Mapbox without a separate routing token. Only a Mapbox token (pk. or sk.) is borrowed from the
		/// web settings, since WebsiteMapboxKey may hold another tile service's key. The web map token is public and usually
		/// URL-restricted to the web app, which Mapbox checks against the Referer, so it comes with the install's own web
		/// address to send as one; the routing token comes with none. Token null: no Mapbox token at all.
		/// </summary>
		public static (string Token, Uri Referer) InstallToken()
		{
			var routing = MappingConfig.MapboxRoutingAccessToken?.Trim();
			if (!string.IsNullOrEmpty(routing))
				return (routing, null);

			var website = new[] { MappingConfig.WebsiteMapboxAccessToken, MappingConfig.WebsiteMapboxKey }
				.Select(t => t?.Trim())
				.FirstOrDefault(t => !string.IsNullOrEmpty(t) &&
				                     (t.StartsWith("pk.", StringComparison.Ordinal) || t.StartsWith("sk.", StringComparison.Ordinal)));
			if (website == null)
				return (null, null);

			Uri.TryCreate(SystemBehaviorConfig.ResgridBaseUrl?.Trim().TrimEnd('/') + "/", UriKind.Absolute, out var site);
			return (website, site);
		}

		/// <summary>Sets the Referer the install's web map token needs; nothing for a routing or department token.</summary>
		private Action<HttpRequestMessage> Referer()
		{
			var referer = _department == null ? InstallToken().Referer : null;
			return referer == null ? null : request => request.Headers.Referrer = referer;
		}

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

		public bool IsConfigured => !string.IsNullOrWhiteSpace(AccessToken);

		public async Task<RouteResult> GetRouteAsync(RouteRequest request, CancellationToken cancellationToken)
		{
			var profile = Profile(request.Profile);
			var query = new StringBuilder();
			query.Append("?access_token=").Append(WebUtility.UrlEncode(AccessToken));
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
			var (_, status, document) = await RoutingHttp.GetJsonAsync(url, cancellationToken, Referer());

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
			var destinationCount = request.Destinations.Count;

			var durations = Enumerable.Range(0, request.Origins.Count).Select(_ => new double?[destinationCount]).ToArray();
			var distances = Enumerable.Range(0, request.Origins.Count).Select(_ => new double?[destinationCount]).ToArray();

			// Origins and destinations are both split, so a matrix with as many destinations as the limit (or more) is still
			// answered by Mapbox rather than handed on to the straight-line estimate.
			foreach (var chunk in MatrixChunks(request.Origins.Count, destinationCount, MatrixCoordinateLimit(profile)))
			{
				var origins = request.Origins.Skip(chunk.OriginStart).Take(chunk.OriginCount).ToList();
				var coordinates = origins.Concat(request.Destinations.Skip(chunk.DestinationStart).Take(chunk.DestinationCount)).ToList();
				var sources = string.Join(";", Enumerable.Range(0, chunk.OriginCount));
				var destinations = string.Join(";", Enumerable.Range(chunk.OriginCount, chunk.DestinationCount));

				var query = new StringBuilder();
				query.Append("?access_token=").Append(WebUtility.UrlEncode(AccessToken));
				query.Append("&annotations=duration,distance");
				query.Append("&sources=").Append(sources).Append("&destinations=").Append(destinations);

				var url = $"{BaseUrl}/directions-matrix/v1/mapbox/{profile}/{RoutingHttp.LonLatPath(coordinates)}{query}";
				var (ok, status, document) = await RoutingHttp.GetJsonAsync(url, cancellationToken, Referer());

				using (document)
				{
					if (!ok || document == null)
						return new RouteMatrixResult { Provider = Name, Error = $"Matrix failed ({status})" };

					var chunkDurations = OsrmShapeParser.ReadTable(document.RootElement, "durations");
					var chunkDistances = OsrmShapeParser.ReadTable(document.RootElement, "distances");

					for (var i = 0; i < chunk.OriginCount; i++)
					{
						for (var j = 0; j < chunk.DestinationCount; j++)
						{
							durations[chunk.OriginStart + i][chunk.DestinationStart + j] = Cell(chunkDurations, i, j);
							distances[chunk.OriginStart + i][chunk.DestinationStart + j] = Cell(chunkDistances, i, j);
						}
					}
				}
			}

			return new RouteMatrixResult { Successful = true, Provider = Name, Durations = durations, Distances = distances };
		}

		/// <summary>Coordinates (origins + destinations) one Mapbox matrix request may carry: 10 on driving-traffic, else 25.</summary>
		public static int MatrixCoordinateLimit(string profile) => profile == "driving-traffic" ? 10 : 25;

		/// <summary>One Mapbox matrix request: a run of origins against a run of destinations.</summary>
		public readonly struct MatrixChunk
		{
			public MatrixChunk(int originStart, int originCount, int destinationStart, int destinationCount)
			{
				OriginStart = originStart;
				OriginCount = originCount;
				DestinationStart = destinationStart;
				DestinationCount = destinationCount;
			}

			public int OriginStart { get; }
			public int OriginCount { get; }
			public int DestinationStart { get; }
			public int DestinationCount { get; }
		}

		/// <summary>
		/// Splits an origins × destinations matrix into requests of at most <paramref name="limit"/> coordinates each, covering
		/// every pair exactly once with the fewest requests (all destinations in one run whenever they fit beside an origin).
		/// </summary>
		public static List<MatrixChunk> MatrixChunks(int originCount, int destinationCount, int limit)
		{
			var chunks = new List<MatrixChunk>();
			if (originCount <= 0 || destinationCount <= 0)
				return chunks;

			limit = Math.Max(2, limit);

			// The destination run size that needs the fewest requests; ties keep the larger run (fewer distinct origin runs).
			var bestPerDestination = 1;
			var bestRequests = long.MaxValue;
			for (var perDestination = Math.Min(destinationCount, limit - 1); perDestination >= 1; perDestination--)
			{
				var perOrigin = limit - perDestination;
				var requests = (long)((originCount + perOrigin - 1) / perOrigin) * ((destinationCount + perDestination - 1) / perDestination);
				if (requests < bestRequests)
				{
					bestRequests = requests;
					bestPerDestination = perDestination;
				}
			}

			var perOriginRun = limit - bestPerDestination;
			for (var destinationStart = 0; destinationStart < destinationCount; destinationStart += bestPerDestination)
			{
				var destinationRun = Math.Min(bestPerDestination, destinationCount - destinationStart);
				for (var originStart = 0; originStart < originCount; originStart += perOriginRun)
					chunks.Add(new MatrixChunk(originStart, Math.Min(perOriginRun, originCount - originStart), destinationStart, destinationRun));
			}

			return chunks;
		}

		private static double? Cell(double?[][] table, int row, int column)
		{
			return table != null && row < table.Length && table[row] != null && column < table[row].Length ? table[row][column] : null;
		}

		public async Task<RouteOptimizationResult> OptimizeAsync(RouteOptimizationRequest request, CancellationToken cancellationToken)
		{
			if (request.Waypoints.Count > Capabilities.MaxOptimizeWaypoints)
				return new RouteOptimizationResult { Provider = Name, Error = "Too many stops for Mapbox optimization", NotSent = true };

			if (!request.KeepFirst || !request.KeepLast)
				return new RouteOptimizationResult { Provider = Name, Error = "Only fixed start and end are supported", NotSent = true };

			var profile = Profile(request.Profile);
			var query = new StringBuilder();
			query.Append("?access_token=").Append(WebUtility.UrlEncode(AccessToken));
			query.Append("&source=first&destination=last&roundtrip=false&overview=false");

			var url = $"{BaseUrl}/optimized-trips/v1/mapbox/{profile}/{RoutingHttp.LonLatPath(request.Waypoints)}{query}";
			var (ok, status, document) = await RoutingHttp.GetJsonAsync(url, cancellationToken, Referer());

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
