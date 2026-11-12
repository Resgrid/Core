using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Providers;

namespace Resgrid.Providers.GeoLocationProvider.Routing
{
	/// <summary>
	/// Valhalla (plan §6.1), self-hosted or a hosted Valhalla service such as SimpleRouting (Bearer key, see
	/// MappingConfig.ValhallaAuthScheme). Routes come back in the OSRM shape (format=osrm) with banner and voice
	/// instructions, so navigation clients read them like Mapbox Directions. Truck costing applies every vehicle limit.
	/// No live traffic. The URL may be on a private network. A request the server refuses (outside its map data, no road
	/// near a point, over its limits) is marked RequestRejected so the next provider answers it without benching Valhalla.
	/// </summary>
	public class ValhallaRoutingProvider : IRoutingProvider
	{
		private const string AttributionText = "© OpenStreetMap contributors";
		private const string OutsideCoverageError = "Outside MappingConfig.ValhallaCoverage";

		private static readonly object CoverageLock = new object();
		private static string _coverageSource;
		private static List<(double West, double South, double East, double North)> _coverageBoxes = new List<(double, double, double, double)>();

		public string Name => RoutingProviderNames.Valhalla;

		// Built per read: the stop and closure limits follow the server's service_limits through MappingConfig.
		public RoutingCapabilities Capabilities => new RoutingCapabilities
		{
			Steps = true,
			Navigation = true,
			Traffic = false,
			Incidents = false,
			Matrix = true,
			Optimize = true,
			VehicleSize = true,
			VehicleFull = true,
			MaxMatrixCoordinates = 1000,
			MaxWaypoints = Math.Max(2, MappingConfig.ValhallaMaxWaypoints),
			// optimized_route is bounded by the matrix limit (stock 2,500 pairs), not by max_locations.
			MaxOptimizeWaypoints = 50,
			// Road closures (M0314): exclude_polygons for areas, exclude_locations for points on closed roads.
			ExcludeAreas = true,
			MaxExcludePoints = Math.Max(0, MappingConfig.ValhallaMaxExcludePoints)
		};

		public bool IsConfigured => !string.IsNullOrWhiteSpace(MappingConfig.ValhallaUrl);

		public async Task<RouteResult> GetRouteAsync(RouteRequest request, CancellationToken cancellationToken)
		{
			if (!IsCovered(request.Waypoints))
				return new RouteResult { Provider = Name, Error = OutsideCoverageError, RequestRejected = true };

			var body = new Dictionary<string, object>
			{
				["locations"] = request.Waypoints.Select((w, i) => Location(w, i, request.Waypoints.Count)).ToList(),
				["costing"] = Costing(request.Profile, request.Vehicle),
				["costing_options"] = CostingOptions(request.Profile, request.Vehicle, request.Avoid),
				["directions_options"] = new Dictionary<string, object>
				{
					["units"] = request.Units == RoutingUnits.Imperial ? "miles" : "kilometers",
					["language"] = string.IsNullOrWhiteSpace(request.Language) ? "en-US" : request.Language
				},
				["format"] = "osrm",
				["shape_format"] = "polyline6"
			};

			if (request.NavigationInstructions)
			{
				body["banner_instructions"] = true;
				body["voice_instructions"] = true;
			}

			if (!request.Steps && !request.NavigationInstructions)
				body["directions_type"] = "none";

			AddExclusions(body, request);

			var (_, status, document) = await RoutingHttp.PostJsonAsync(Url("/route"), body, cancellationToken, Authorize);

			using (document)
			{
				if (document != null && document.RootElement.TryGetProperty("error", out var error))
					return new RouteResult { Provider = Name, Error = $"{status}: {error}", RequestRejected = IsRejection(status, document) };

				var result = OsrmShapeParser.Parse(document, Name, AttributionText, request.Steps || request.NavigationInstructions);
				if (!result.Successful)
				{
					result.Error = $"{status}: {result.Error}";
					result.RequestRejected = IsRejection(status, document);
					return result;
				}

				result.VehicleRestricted = UsesTruckCosting(request.Profile, request.Vehicle);
				return result;
			}
		}

		public async Task<RouteMatrixResult> GetMatrixAsync(RouteMatrixRequest request, CancellationToken cancellationToken)
		{
			if (!IsCovered(request.Origins) || !IsCovered(request.Destinations))
				return new RouteMatrixResult { Provider = Name, Error = OutsideCoverageError, RequestRejected = true };

			var body = new Dictionary<string, object>
			{
				["sources"] = request.Origins.Select(o => new Dictionary<string, object> { ["lat"] = o.Latitude, ["lon"] = o.Longitude }).ToList(),
				["targets"] = request.Destinations.Select(d => new Dictionary<string, object> { ["lat"] = d.Latitude, ["lon"] = d.Longitude }).ToList(),
				["costing"] = Costing(request.Profile, request.Vehicle),
				["costing_options"] = CostingOptions(request.Profile, request.Vehicle, request.Avoid),
				["units"] = "kilometers"
			};

			var (_, status, document) = await RoutingHttp.PostJsonAsync(Url("/sources_to_targets"), body, cancellationToken, Authorize);

			using (document)
			{
				if (document == null || !document.RootElement.TryGetProperty("sources_to_targets", out var rows) || rows.ValueKind != JsonValueKind.Array)
					return new RouteMatrixResult { Provider = Name, Error = $"Matrix failed ({status}){ErrorDetail(document)}", RequestRejected = IsRejection(status, document) };

				var durations = new double?[request.Origins.Count][];
				var distances = new double?[request.Origins.Count][];
				for (var i = 0; i < request.Origins.Count; i++)
				{
					durations[i] = new double?[request.Destinations.Count];
					distances[i] = new double?[request.Destinations.Count];
				}

				foreach (var row in rows.EnumerateArray())
				{
					foreach (var cell in row.EnumerateArray())
					{
						var from = cell.TryGetProperty("from_index", out var f) && f.ValueKind == JsonValueKind.Number ? f.GetInt32() : -1;
						var to = cell.TryGetProperty("to_index", out var t) && t.ValueKind == JsonValueKind.Number ? t.GetInt32() : -1;
						if (from < 0 || to < 0 || from >= durations.Length || to >= request.Destinations.Count)
							continue;

						if (cell.TryGetProperty("time", out var time) && time.ValueKind == JsonValueKind.Number)
							durations[from][to] = time.GetDouble();
						if (cell.TryGetProperty("distance", out var distance) && distance.ValueKind == JsonValueKind.Number)
							distances[from][to] = distance.GetDouble() * 1000d;
					}
				}

				return new RouteMatrixResult { Successful = true, Provider = Name, Durations = durations, Distances = distances };
			}
		}

		public async Task<RouteOptimizationResult> OptimizeAsync(RouteOptimizationRequest request, CancellationToken cancellationToken)
		{
			// Valhalla's optimizer always keeps the first and last locations in place.
			if (!request.KeepFirst || !request.KeepLast)
				return new RouteOptimizationResult { Provider = Name, Error = "Valhalla keeps the first and last stops fixed" };

			if (!IsCovered(request.Waypoints))
				return new RouteOptimizationResult { Provider = Name, Error = OutsideCoverageError, RequestRejected = true };

			var body = new Dictionary<string, object>
			{
				["locations"] = request.Waypoints.Select(w => new Dictionary<string, object> { ["lat"] = w.Latitude, ["lon"] = w.Longitude }).ToList(),
				["costing"] = Costing(request.Profile, request.Vehicle),
				["costing_options"] = CostingOptions(request.Profile, request.Vehicle, request.Avoid)
			};

			var (_, status, document) = await RoutingHttp.PostJsonAsync(Url("/optimized_route"), body, cancellationToken, Authorize);

			using (document)
			{
				if (document == null || !document.RootElement.TryGetProperty("trip", out var trip) || !trip.TryGetProperty("locations", out var locations))
					return new RouteOptimizationResult { Provider = Name, Error = $"Optimization failed ({status}){ErrorDetail(document)}", RequestRejected = IsRejection(status, document) };

				var order = new List<int>();
				foreach (var location in locations.EnumerateArray())
				{
					if (!location.TryGetProperty("original_index", out var index) || index.ValueKind != JsonValueKind.Number)
						return new RouteOptimizationResult { Provider = Name, Error = "Optimization result had no original_index" };
					order.Add(index.GetInt32());
				}

				var result = new RouteOptimizationResult { Successful = true, Provider = Name, Order = order };
				if (trip.TryGetProperty("summary", out var summary))
				{
					result.DistanceMeters = OsrmShapeParser.GetDouble(summary, "length") * 1000d;
					result.DurationSeconds = OsrmShapeParser.GetDouble(summary, "time");
				}

				return result;
			}
		}

		private static string Url(string path)
		{
			return RoutingHttp.TrimBase(MappingConfig.ValhallaUrl) + path;
		}

		/// <summary>Adds ValhallaApiKey as an X-Api-Key header, or as "Authorization: Bearer key" when ValhallaAuthScheme is "bearer".</summary>
		public static void Authorize(HttpRequestMessage request)
		{
			var key = MappingConfig.ValhallaApiKey?.Trim();
			if (string.IsNullOrWhiteSpace(key))
				return;

			if (string.Equals(MappingConfig.ValhallaAuthScheme?.Trim(), MappingConfig.ValhallaBearerAuthScheme, StringComparison.OrdinalIgnoreCase))
			{
				// A key pasted together with its scheme ("Bearer abc") is sent once, not as "Bearer Bearer abc".
				if (key.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
					key = key.Substring("Bearer ".Length).Trim();

				request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
			}
			else
			{
				request.Headers.TryAddWithoutValidation("X-Api-Key", key);
			}
		}

		/// <summary>
		/// True for a Valhalla 400 with an error body (JSON error_code, or an OSRM-format code other than "Ok"): the server is
		/// up but cannot route this request. Authentication (401/403), rate limits (429), server errors and unreadable
		/// replies are provider failures instead.
		/// </summary>
		public static bool IsRejection(int status, JsonDocument document)
		{
			if (status != 400 || document == null || document.RootElement.ValueKind != JsonValueKind.Object)
				return false;

			var root = document.RootElement;
			if (root.TryGetProperty("error_code", out var errorCode) && errorCode.ValueKind == JsonValueKind.Number)
				return true;

			return root.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.String && code.GetString() != "Ok";
		}

		/// <summary>True when every point is inside one of the MappingConfig.ValhallaCoverage boxes (always, when none are set).</summary>
		public static bool IsCovered(IEnumerable<RoutingWaypoint> points)
		{
			var boxes = CoverageBoxes();
			if (boxes.Count == 0 || points == null)
				return true;

			return points.All(p => boxes.Any(b => InBox(b, p.Latitude, p.Longitude)));
		}

		private static bool InBox((double West, double South, double East, double North) box, double latitude, double longitude)
		{
			if (latitude < box.South || latitude > box.North)
				return false;

			// A box whose west edge is east of its east edge crosses the antimeridian.
			return box.West <= box.East
				? longitude >= box.West && longitude <= box.East
				: longitude >= box.West || longitude <= box.East;
		}

		private static List<(double West, double South, double East, double North)> CoverageBoxes()
		{
			var source = MappingConfig.ValhallaCoverage ?? string.Empty;

			lock (CoverageLock)
			{
				if (string.Equals(source, _coverageSource, StringComparison.Ordinal))
					return _coverageBoxes;

				var boxes = new List<(double, double, double, double)>();
				foreach (var part in source.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
				{
					var values = part.Split(',', StringSplitOptions.TrimEntries);
					if (values.Length != 4)
						continue;

					var numbers = new double[4];
					var valid = true;
					for (var i = 0; i < 4 && valid; i++)
						valid = double.TryParse(values[i], NumberStyles.Float, CultureInfo.InvariantCulture, out numbers[i]);

					// west, south, east, north; a malformed box is ignored rather than sending every request elsewhere.
					if (valid && numbers[1] < numbers[3] && numbers[1] >= -90 && numbers[3] <= 90 &&
					    numbers[0] >= -180 && numbers[0] <= 180 && numbers[2] >= -180 && numbers[2] <= 180)
						boxes.Add((numbers[0], numbers[1], numbers[2], numbers[3]));
				}

				_coverageSource = source;
				_coverageBoxes = boxes;
				return boxes;
			}
		}

		private static string ErrorDetail(JsonDocument document)
		{
			if (document == null || document.RootElement.ValueKind != JsonValueKind.Object)
				return string.Empty;

			var root = document.RootElement;
			if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
				return ": " + error.GetString();
			if (root.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
				return ": " + message.GetString();

			return string.Empty;
		}

		private static Dictionary<string, object> Location(RoutingWaypoint waypoint, int index, int count)
		{
			var location = new Dictionary<string, object>
			{
				["lat"] = waypoint.Latitude,
				["lon"] = waypoint.Longitude,
				// The first and last locations must be stops; others may pass through.
				["type"] = index == 0 || index == count - 1 || waypoint.IsStop ? "break" : "through"
			};

			if (waypoint.Heading.HasValue)
			{
				location["heading"] = Math.Round(waypoint.Heading.Value);
				location["heading_tolerance"] = 45;
			}

			return location;
		}

		/// <summary>Road closures the route must not use: areas as closed [lon, lat] rings, road points as locations.</summary>
		public static void AddExclusions(Dictionary<string, object> body, RouteRequest request)
		{
			var areas = (request.ExcludeAreas ?? new List<List<RoutingWaypoint>>()).Where(a => a != null && a.Count >= 3).ToList();
			if (areas.Count > 0)
			{
				body["exclude_polygons"] = areas.Select(ring =>
				{
					var positions = ring.Select(p => new[] { p.Longitude, p.Latitude }).ToList();
					positions.Add(new[] { ring[0].Longitude, ring[0].Latitude });
					return positions;
				}).ToList();
			}

			var points = (request.ExcludePoints ?? new List<RoutingWaypoint>()).ToList();
			if (points.Count > 0)
				body["exclude_locations"] = points.Select(p => new Dictionary<string, object> { ["lat"] = p.Latitude, ["lon"] = p.Longitude }).ToList();
		}

		internal static bool UsesTruckCosting(RoutingProfile profile, VehicleProfile vehicle)
		{
			if (profile == RoutingProfile.Walking || profile == RoutingProfile.Cycling)
				return false;

			return vehicle != null && vehicle.HasAny;
		}

		private static string Costing(RoutingProfile profile, VehicleProfile vehicle)
		{
			switch (profile)
			{
				case RoutingProfile.Walking:
					return "pedestrian";
				case RoutingProfile.Cycling:
					return "bicycle";
				default:
					return UsesTruckCosting(profile, vehicle) ? "truck" : "auto";
			}
		}

		private static Dictionary<string, object> CostingOptions(RoutingProfile profile, VehicleProfile vehicle, RouteAvoidOptions avoid)
		{
			var costing = Costing(profile, vehicle);
			var options = new Dictionary<string, object>();

			if (costing == "auto" || costing == "truck")
			{
				if (avoid.HasFlag(RouteAvoidOptions.Tolls))
					options["use_tolls"] = 0;
				if (avoid.HasFlag(RouteAvoidOptions.Highways))
					options["use_highways"] = 0;
				if (avoid.HasFlag(RouteAvoidOptions.Ferries))
					options["use_ferry"] = 0;
				if (avoid.HasFlag(RouteAvoidOptions.Unpaved))
					options["exclude_unpaved"] = true;
			}

			if (costing == "truck")
			{
				if (vehicle.HeightMeters.HasValue)
					options["height"] = vehicle.HeightMeters.Value;
				if (vehicle.WidthMeters.HasValue)
					options["width"] = vehicle.WidthMeters.Value;
				if (vehicle.LengthMeters.HasValue)
					options["length"] = vehicle.LengthMeters.Value;
				// Valhalla takes weights in metric tons.
				if (vehicle.GrossWeightKg.HasValue)
					options["weight"] = Math.Round(vehicle.GrossWeightKg.Value / 1000d, 3);
				if (vehicle.AxleLoadKg.HasValue)
					options["axle_load"] = Math.Round(vehicle.AxleLoadKg.Value / 1000d, 3);
				if (vehicle.AxleCount.HasValue)
					options["axle_count"] = vehicle.AxleCount.Value;
				if (vehicle.Hazmat == true)
					options["hazmat"] = true;
			}

			return new Dictionary<string, object> { [costing] = options };
		}
	}
}
