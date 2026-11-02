using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using GoogleMapsApi.Entities.Directions.Request;
using GoogleMapsApi.Entities.Directions.Response;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Providers;

namespace Resgrid.Providers.GeoLocationProvider.Routing
{
	/// <summary>
	/// Google routing (plan §6.1): the legacy Directions API (today's calls, MappingConfig.GoogleRoutingApi = "legacy")
	/// or the Routes API ("routes", needed for Cloud projects created after March 2025). Route lines, drive times and
	/// printed steps only: Google's terms forbid real-time navigation on this content outside its Navigation SDK, so
	/// in-app navigation is off (plan D26). No vehicle limits.
	/// </summary>
	public class GoogleRoutingProvider : IRoutingProvider
	{
		private const string AttributionText = "Map data © Google";
		private const string RoutesUrl = "https://routes.googleapis.com/directions/v2:computeRoutes";
		private const string MatrixUrl = "https://routes.googleapis.com/distanceMatrix/v2:computeRouteMatrix";
		private static readonly Regex HtmlTags = new Regex("<[^>]+>", RegexOptions.Compiled);

		public string Name => RoutingProviderNames.Google;

		public RoutingCapabilities Capabilities => new RoutingCapabilities
		{
			Steps = true,
			Navigation = false,
			Traffic = UseRoutesApi,
			Incidents = false,
			Matrix = true,
			Optimize = true,
			VehicleSize = false,
			VehicleFull = false,
			MaxMatrixCoordinates = 25,
			MaxWaypoints = 25,
			MaxOptimizeWaypoints = 25
		};

		public bool IsConfigured => !string.IsNullOrWhiteSpace(MappingConfig.GoogleMapsApiKey);

		private static bool UseRoutesApi => string.Equals(MappingConfig.GoogleRoutingApi?.Trim(), "routes", StringComparison.OrdinalIgnoreCase);

		public Task<RouteResult> GetRouteAsync(RouteRequest request, CancellationToken cancellationToken)
		{
			return UseRoutesApi ? GetRoutesApiRouteAsync(request, false, cancellationToken) : GetLegacyRouteAsync(request, false);
		}

		public async Task<RouteMatrixResult> GetMatrixAsync(RouteMatrixRequest request, CancellationToken cancellationToken)
		{
			if (UseRoutesApi)
				return await GetRoutesApiMatrixAsync(request, cancellationToken);

			// The legacy path keeps today's behaviour: one Directions request per origin.
			var durations = new double?[request.Origins.Count][];
			var distances = new double?[request.Origins.Count][];

			for (var i = 0; i < request.Origins.Count; i++)
			{
				durations[i] = new double?[request.Destinations.Count];
				distances[i] = new double?[request.Destinations.Count];

				for (var j = 0; j < request.Destinations.Count; j++)
				{
					cancellationToken.ThrowIfCancellationRequested();
					var route = await GetLegacyRouteAsync(new RouteRequest
					{
						Waypoints = new List<RoutingWaypoint> { request.Origins[i], request.Destinations[j] },
						Profile = request.Profile,
						Avoid = request.Avoid
					}, false);

					if (route.Successful)
					{
						durations[i][j] = route.DurationSeconds;
						distances[i][j] = route.DistanceMeters;
					}
				}
			}

			return new RouteMatrixResult { Successful = true, Provider = Name, Durations = durations, Distances = distances };
		}

		public async Task<RouteOptimizationResult> OptimizeAsync(RouteOptimizationRequest request, CancellationToken cancellationToken)
		{
			if (!request.KeepFirst || !request.KeepLast)
				return new RouteOptimizationResult { Provider = Name, Error = "Only fixed start and end are supported" };

			var routeRequest = new RouteRequest { Waypoints = request.Waypoints, Profile = request.Profile, Avoid = request.Avoid };
			var (route, intermediateOrder) = UseRoutesApi
				? await GetRoutesApiRouteWithOrderAsync(routeRequest, cancellationToken)
				: await GetLegacyRouteWithOrderAsync(routeRequest);

			if (!route.Successful || intermediateOrder == null)
				return new RouteOptimizationResult { Provider = Name, Error = route.Error ?? "Optimization failed" };

			var order = new List<int> { 0 };
			order.AddRange(intermediateOrder.Select(i => i + 1));
			order.Add(request.Waypoints.Count - 1);

			return new RouteOptimizationResult
			{
				Successful = true,
				Provider = Name,
				Order = order,
				DistanceMeters = route.DistanceMeters,
				DurationSeconds = route.DurationSeconds
			};
		}

		private async Task<RouteResult> GetLegacyRouteAsync(RouteRequest request, bool optimize)
		{
			var (route, _) = await GetLegacyRouteWithOrderAsync(request, optimize);
			return route;
		}

		private async Task<(RouteResult, int[])> GetLegacyRouteWithOrderAsync(RouteRequest request, bool optimize = true)
		{
			var result = new RouteResult { Provider = Name, Attribution = AttributionText };

			try
			{
				var waypoints = request.Waypoints;
				var directionsRequest = new DirectionsRequest
				{
					Origin = LatLon(waypoints[0]),
					Destination = LatLon(waypoints[waypoints.Count - 1]),
					ApiKey = MappingConfig.GoogleMapsApiKey,
					TravelMode = request.Profile == RoutingProfile.Walking ? TravelMode.Walking :
						request.Profile == RoutingProfile.Cycling ? TravelMode.Bicycling : TravelMode.Driving,
					Language = request.Language
				};

				if (waypoints.Count > 2)
				{
					directionsRequest.Waypoints = waypoints.Skip(1).Take(waypoints.Count - 2).Select(LatLon).ToArray();
					directionsRequest.OptimizeWaypoints = optimize;
				}

				var avoid = AvoidWay.Nothing;
				if (request.Avoid.HasFlag(RouteAvoidOptions.Tolls))
					avoid |= AvoidWay.Tolls;
				if (request.Avoid.HasFlag(RouteAvoidOptions.Highways))
					avoid |= AvoidWay.Highways;
				directionsRequest.Avoid = avoid;

				var directions = await GoogleMapsApi.GoogleMaps.Directions.QueryAsync(directionsRequest);
				var route = directions?.Status == DirectionsStatusCodes.OK ? directions.Routes?.FirstOrDefault() : null;

				if (route?.Legs == null)
				{
					result.Error = directions?.Status.ToString() ?? "No response";
					return (result, null);
				}

				var legs = route.Legs.ToList();
				var overview = Points(route.OverviewPath);
				result.DistanceMeters = legs.Sum(l => (double)(l.Distance?.Value ?? 0));
				result.DurationSeconds = legs.Sum(l => l.Duration?.Value.TotalSeconds ?? 0);
				result.Legs = legs.Select(l => new RouteLegSummary
				{
					DistanceMeters = l.Distance?.Value ?? 0,
					DurationSeconds = l.Duration?.Value.TotalSeconds ?? 0
				}).ToList();
				result.GeometryGeoJson = PolylineCodec.ToGeoJsonLineString(overview);

				if (request.Steps)
				{
					var normalizedLegs = legs.Select(l => new NormalizedLeg
					{
						Distance = l.Distance?.Value ?? 0,
						Duration = l.Duration?.Value.TotalSeconds ?? 0,
						Steps = (l.Steps ?? Enumerable.Empty<Step>()).Select(s => new NormalizedStep
						{
							Distance = s.Distance?.Value ?? 0,
							Duration = s.Duration?.Value.TotalSeconds ?? 0,
							Points = Points(s.PolyLine),
							Latitude = s.StartLocation?.Latitude ?? 0,
							Longitude = s.StartLocation?.Longitude ?? 0,
							Instruction = StripHtml(s.HtmlInstructions)
						}).ToList()
					}).ToList();

					result.NormalizedJson = BuildNormalizedRoute(result.DistanceMeters, result.DurationSeconds, overview, normalizedLegs);
				}

				result.Successful = true;
				return (result, route.WaypointOrder);
			}
			catch (Exception ex)
			{
				result.Error = ex.Message;
				return (result, null);
			}
		}

		private async Task<RouteResult> GetRoutesApiRouteAsync(RouteRequest request, bool optimize, CancellationToken cancellationToken)
		{
			var (route, _) = await GetRoutesApiRouteWithOrderAsync(request, cancellationToken, optimize);
			return route;
		}

		private async Task<(RouteResult, int[])> GetRoutesApiRouteWithOrderAsync(RouteRequest request, CancellationToken cancellationToken, bool optimize = true)
		{
			var result = new RouteResult { Provider = Name, Attribution = AttributionText };
			var waypoints = request.Waypoints;
			var trafficAware = request.Profile == RoutingProfile.DrivingTraffic && MappingConfig.RoutingTrafficAware;

			var body = new Dictionary<string, object>
			{
				["origin"] = Waypoint(waypoints[0]),
				["destination"] = Waypoint(waypoints[waypoints.Count - 1]),
				["travelMode"] = TravelModeName(request.Profile),
				["polylineEncoding"] = "ENCODED_POLYLINE",
				["languageCode"] = string.IsNullOrWhiteSpace(request.Language) ? "en" : request.Language,
				["units"] = request.Units == RoutingUnits.Imperial ? "IMPERIAL" : "METRIC"
			};

			if (TravelModeName(request.Profile) == "DRIVE")
				body["routingPreference"] = trafficAware ? "TRAFFIC_AWARE" : "TRAFFIC_UNAWARE";

			if (waypoints.Count > 2)
			{
				body["intermediates"] = waypoints.Skip(1).Take(waypoints.Count - 2).Select(Waypoint).ToList();
				body["optimizeWaypointOrder"] = optimize;
			}

			var modifiers = new Dictionary<string, object>();
			if (request.Avoid.HasFlag(RouteAvoidOptions.Tolls))
				modifiers["avoidTolls"] = true;
			if (request.Avoid.HasFlag(RouteAvoidOptions.Highways))
				modifiers["avoidHighways"] = true;
			if (request.Avoid.HasFlag(RouteAvoidOptions.Ferries))
				modifiers["avoidFerries"] = true;
			if (modifiers.Count > 0)
				body["routeModifiers"] = modifiers;

			var fields = "routes.duration,routes.distanceMeters,routes.polyline.encodedPolyline,routes.legs.duration,routes.legs.distanceMeters,routes.optimizedIntermediateWaypointIndex";
			if (request.Steps)
				fields += ",routes.legs.steps.distanceMeters,routes.legs.steps.staticDuration,routes.legs.steps.polyline.encodedPolyline,routes.legs.steps.startLocation,routes.legs.steps.navigationInstruction";

			try
			{
				var (ok, status, document) = await RoutingHttp.PostJsonAsync(RoutesUrl, body, cancellationToken, message =>
				{
					message.Headers.TryAddWithoutValidation("X-Goog-Api-Key", MappingConfig.GoogleMapsApiKey);
					message.Headers.TryAddWithoutValidation("X-Goog-FieldMask", fields);
				});

				using (document)
				{
					if (!ok || document == null || !document.RootElement.TryGetProperty("routes", out var routes) || routes.GetArrayLength() == 0)
					{
						result.Error = $"Routes API failed ({status})";
						return (result, null);
					}

					var route = routes[0];
					result.DistanceMeters = OsrmShapeParser.GetDouble(route, "distanceMeters");
					result.DurationSeconds = ParseDuration(route, "duration");
					result.IsTrafficAware = trafficAware;

					var overview = route.TryGetProperty("polyline", out var polyline)
						? PolylineCodec.Decode(OsrmShapeParser.GetString(polyline, "encodedPolyline"), 5)
						: new List<(double, double)>();
					result.GeometryGeoJson = PolylineCodec.ToGeoJsonLineString(overview);

					var normalizedLegs = new List<NormalizedLeg>();
					if (route.TryGetProperty("legs", out var legs))
					{
						foreach (var leg in legs.EnumerateArray())
						{
							var normalizedLeg = new NormalizedLeg
							{
								Distance = OsrmShapeParser.GetDouble(leg, "distanceMeters"),
								Duration = ParseDuration(leg, "duration")
							};
							result.Legs.Add(new RouteLegSummary { DistanceMeters = normalizedLeg.Distance, DurationSeconds = normalizedLeg.Duration });

							if (request.Steps && leg.TryGetProperty("steps", out var steps))
							{
								foreach (var step in steps.EnumerateArray())
								{
									var location = step.TryGetProperty("startLocation", out var start) && start.TryGetProperty("latLng", out var latLng) ? latLng : default;
									normalizedLeg.Steps.Add(new NormalizedStep
									{
										Distance = OsrmShapeParser.GetDouble(step, "distanceMeters"),
										Duration = ParseDuration(step, "staticDuration"),
										Points = step.TryGetProperty("polyline", out var stepLine) ? PolylineCodec.Decode(OsrmShapeParser.GetString(stepLine, "encodedPolyline"), 5) : new List<(double, double)>(),
										Latitude = location.ValueKind == JsonValueKind.Object ? OsrmShapeParser.GetDouble(location, "latitude") : 0,
										Longitude = location.ValueKind == JsonValueKind.Object ? OsrmShapeParser.GetDouble(location, "longitude") : 0,
										Instruction = step.TryGetProperty("navigationInstruction", out var instruction) ? OsrmShapeParser.GetString(instruction, "instructions") : null
									});
								}
							}

							normalizedLegs.Add(normalizedLeg);
						}
					}

					if (request.Steps)
						result.NormalizedJson = BuildNormalizedRoute(result.DistanceMeters, result.DurationSeconds, overview, normalizedLegs);

					int[] order = null;
					if (route.TryGetProperty("optimizedIntermediateWaypointIndex", out var optimized) && optimized.ValueKind == JsonValueKind.Array)
						order = optimized.EnumerateArray().Select(e => e.GetInt32()).ToArray();
					else if (waypoints.Count <= 2)
						order = Array.Empty<int>();

					result.Successful = true;
					return (result, order);
				}
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception ex)
			{
				result.Error = ex.Message;
				return (result, null);
			}
		}

		private async Task<RouteMatrixResult> GetRoutesApiMatrixAsync(RouteMatrixRequest request, CancellationToken cancellationToken)
		{
			var trafficAware = request.Profile == RoutingProfile.DrivingTraffic && MappingConfig.RoutingTrafficAware;
			var body = new Dictionary<string, object>
			{
				["origins"] = request.Origins.Select(o => new Dictionary<string, object> { ["waypoint"] = Waypoint(o) }).ToList(),
				["destinations"] = request.Destinations.Select(d => new Dictionary<string, object> { ["waypoint"] = Waypoint(d) }).ToList(),
				["travelMode"] = TravelModeName(request.Profile)
			};

			if (TravelModeName(request.Profile) == "DRIVE")
				body["routingPreference"] = trafficAware ? "TRAFFIC_AWARE" : "TRAFFIC_UNAWARE";

			var (ok, status, document) = await RoutingHttp.PostJsonAsync(MatrixUrl, body, cancellationToken, message =>
			{
				message.Headers.TryAddWithoutValidation("X-Goog-Api-Key", MappingConfig.GoogleMapsApiKey);
				message.Headers.TryAddWithoutValidation("X-Goog-FieldMask", "originIndex,destinationIndex,duration,distanceMeters,condition");
			});

			using (document)
			{
				if (!ok || document == null || document.RootElement.ValueKind != JsonValueKind.Array)
					return new RouteMatrixResult { Provider = Name, Error = $"Route matrix failed ({status})" };

				var durations = new double?[request.Origins.Count][];
				var distances = new double?[request.Origins.Count][];
				for (var i = 0; i < request.Origins.Count; i++)
				{
					durations[i] = new double?[request.Destinations.Count];
					distances[i] = new double?[request.Destinations.Count];
				}

				foreach (var element in document.RootElement.EnumerateArray())
				{
					if (OsrmShapeParser.GetString(element, "condition") != "ROUTE_EXISTS")
						continue;

					var origin = element.TryGetProperty("originIndex", out var o) ? o.GetInt32() : 0;
					var destination = element.TryGetProperty("destinationIndex", out var d) ? d.GetInt32() : 0;
					if (origin >= durations.Length || destination >= request.Destinations.Count)
						continue;

					durations[origin][destination] = ParseDuration(element, "duration");
					distances[origin][destination] = OsrmShapeParser.GetDouble(element, "distanceMeters");
				}

				return new RouteMatrixResult { Successful = true, Provider = Name, Durations = durations, Distances = distances };
			}
		}

		private static string TravelModeName(RoutingProfile profile)
		{
			switch (profile)
			{
				case RoutingProfile.Walking:
					return "WALK";
				case RoutingProfile.Cycling:
					return "BICYCLE";
				default:
					return "DRIVE";
			}
		}

		private static Dictionary<string, object> Waypoint(RoutingWaypoint waypoint)
		{
			return new Dictionary<string, object>
			{
				["location"] = new Dictionary<string, object>
				{
					["latLng"] = new Dictionary<string, object> { ["latitude"] = waypoint.Latitude, ["longitude"] = waypoint.Longitude }
				}
			};
		}

		private static List<(double Latitude, double Longitude)> Points(OverviewPolyline polyline)
		{
			try
			{
				return polyline?.Points?.Select(p => (p.Latitude, p.Longitude)).ToList() ?? new List<(double, double)>();
			}
			catch (PointsDecodingException)
			{
				return new List<(double, double)>();
			}
		}

		private static string LatLon(RoutingWaypoint waypoint)
		{
			return string.Format(CultureInfo.InvariantCulture, "{0},{1}", waypoint.Latitude, waypoint.Longitude);
		}

		/// <summary>Routes API durations are strings like "123s".</summary>
		private static double ParseDuration(JsonElement element, string name)
		{
			var text = OsrmShapeParser.GetString(element, name);
			if (string.IsNullOrWhiteSpace(text))
				return 0;

			return double.TryParse(text.TrimEnd('s'), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) ? seconds : 0;
		}

		private static string StripHtml(string html)
		{
			if (string.IsNullOrWhiteSpace(html))
				return null;

			return System.Net.WebUtility.HtmlDecode(HtmlTags.Replace(html.Replace("<div", " <div"), string.Empty)).Trim();
		}

		private sealed class NormalizedLeg
		{
			public double Distance { get; set; }
			public double Duration { get; set; }
			public List<NormalizedStep> Steps { get; set; } = new List<NormalizedStep>();
		}

		private sealed class NormalizedStep
		{
			public double Distance { get; set; }
			public double Duration { get; set; }
			public List<(double Latitude, double Longitude)> Points { get; set; }
			public double Latitude { get; set; }
			public double Longitude { get; set; }
			public string Instruction { get; set; }
		}

		/// <summary>The OSRM v5 route shape (polyline6 geometry) with each step's text in maneuver.instruction.</summary>
		private static string BuildNormalizedRoute(double distance, double duration, List<(double Latitude, double Longitude)> overview, List<NormalizedLeg> legs)
		{
			using var stream = new MemoryStream();
			using (var writer = new Utf8JsonWriter(stream))
			{
				writer.WriteStartObject();
				writer.WriteNumber("distance", distance);
				writer.WriteNumber("duration", duration);
				writer.WriteString("geometry", PolylineCodec.Encode(overview, 6));
				writer.WriteStartArray("legs");

				foreach (var leg in legs)
				{
					writer.WriteStartObject();
					writer.WriteNumber("distance", leg.Distance);
					writer.WriteNumber("duration", leg.Duration);
					writer.WriteString("summary", string.Empty);
					writer.WriteStartArray("steps");

					foreach (var step in leg.Steps)
					{
						writer.WriteStartObject();
						writer.WriteNumber("distance", step.Distance);
						writer.WriteNumber("duration", step.Duration);
						writer.WriteString("geometry", PolylineCodec.Encode(step.Points ?? new List<(double, double)>(), 6));
						writer.WriteString("name", string.Empty);
						writer.WriteStartObject("maneuver");
						writer.WriteStartArray("location");
						writer.WriteNumberValue(step.Longitude);
						writer.WriteNumberValue(step.Latitude);
						writer.WriteEndArray();
						writer.WriteString("type", "turn");
						writer.WriteString("instruction", step.Instruction ?? string.Empty);
						writer.WriteEndObject();
						writer.WriteEndObject();
					}

					writer.WriteEndArray();
					writer.WriteEndObject();
				}

				writer.WriteEndArray();
				writer.WriteEndObject();
			}

			return Encoding.UTF8.GetString(stream.ToArray());
		}
	}
}
