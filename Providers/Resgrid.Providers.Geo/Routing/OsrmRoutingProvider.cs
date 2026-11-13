using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Providers;

namespace Resgrid.Providers.GeoLocationProvider.Routing
{
	/// <summary>
	/// A self-hosted OSRM server (MappingConfig.OsrmUrl). Route lines, drive times, tables and trips; no voice or banner
	/// instructions and no vehicle limits, so in-app navigation is off on OSRM installs (owner Q6, plan D26).
	/// </summary>
	public class OsrmRoutingProvider : IRoutingProvider
	{
		private const string AttributionText = "© OpenStreetMap contributors";

		// A department's own OSRM server (Mapping Settings); null = the install's. Only reached on a public address.
		private readonly RoutingProviderCredentials _department;

		public OsrmRoutingProvider()
		{
		}

		private OsrmRoutingProvider(RoutingProviderCredentials department)
		{
			_department = department;
		}

		public IRoutingProvider WithCredentials(RoutingProviderCredentials credentials)
		{
			return credentials == null ? null : new OsrmRoutingProvider(credentials);
		}

		private string BaseUrl => RoutingHttp.TrimBase(_department != null ? _department.Url : MappingConfig.OsrmUrl);

		public string Name => RoutingProviderNames.Osrm;

		public RoutingCapabilities Capabilities { get; } = new RoutingCapabilities
		{
			Steps = true,
			Navigation = false,
			Traffic = false,
			Incidents = false,
			Matrix = true,
			Optimize = true,
			VehicleSize = false,
			VehicleFull = false,
			MaxMatrixCoordinates = 100,
			MaxWaypoints = 100,
			MaxOptimizeWaypoints = 100
		};

		public bool IsConfigured => _department != null
			? RoutingHttp.IsAcceptableDepartmentUrl(_department.Url)
			: !string.IsNullOrWhiteSpace(MappingConfig.OsrmUrl);

		public async Task<RouteResult> GetRouteAsync(RouteRequest request, CancellationToken cancellationToken)
		{
			var steps = request.Steps || request.NavigationInstructions;
			var url = $"{BaseUrl}/route/v1/{Profile(request.Profile)}/{RoutingHttp.LonLatPath(request.Waypoints)}" +
			          $"?overview=full&geometries=polyline6&steps={(steps ? "true" : "false")}";

			var (_, status, document) = await RoutingHttp.GetJsonAsync(url, cancellationToken, null, _department != null);

			using (document)
			{
				var result = OsrmShapeParser.Parse(document, Name, AttributionText, steps);
				if (!result.Successful && string.IsNullOrEmpty(result.Error))
					result.Error = $"Route failed ({status})";
				return result;
			}
		}

		public async Task<RouteMatrixResult> GetMatrixAsync(RouteMatrixRequest request, CancellationToken cancellationToken)
		{
			var coordinates = request.Origins.Concat(request.Destinations).ToList();
			var query = new StringBuilder("?annotations=duration,distance");
			query.Append("&sources=").Append(string.Join(";", Enumerable.Range(0, request.Origins.Count)));
			query.Append("&destinations=").Append(string.Join(";", Enumerable.Range(request.Origins.Count, request.Destinations.Count)));

			var url = $"{BaseUrl}/table/v1/{Profile(request.Profile)}/{RoutingHttp.LonLatPath(coordinates)}{query}";
			var (ok, status, document) = await RoutingHttp.GetJsonAsync(url, cancellationToken, null, _department != null);

			using (document)
			{
				if (!ok || document == null)
					return new RouteMatrixResult { Provider = Name, Error = $"Table failed ({status})" };

				return new RouteMatrixResult
				{
					Successful = true,
					Provider = Name,
					Durations = OsrmShapeParser.ReadTable(document.RootElement, "durations"),
					Distances = OsrmShapeParser.ReadTable(document.RootElement, "distances")
				};
			}
		}

		public async Task<RouteOptimizationResult> OptimizeAsync(RouteOptimizationRequest request, CancellationToken cancellationToken)
		{
			if (!request.KeepFirst || !request.KeepLast)
				return new RouteOptimizationResult { Provider = Name, Error = "Only fixed start and end are supported", NotSent = true };

			var url = $"{BaseUrl}/trip/v1/{Profile(request.Profile)}/{RoutingHttp.LonLatPath(request.Waypoints)}" +
			          "?source=first&destination=last&roundtrip=false&overview=false";
			var (ok, status, document) = await RoutingHttp.GetJsonAsync(url, cancellationToken, null, _department != null);

			using (document)
			{
				if (!ok || document == null)
					return new RouteOptimizationResult { Provider = Name, Error = $"Trip failed ({status})" };

				var order = OsrmShapeParser.ReadTripOrder(document.RootElement);
				if (order == null)
					return new RouteOptimizationResult { Provider = Name, Error = "Trip returned no order" };

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
					return "foot";
				case RoutingProfile.Cycling:
					return "bike";
				default:
					return "driving";
			}
		}
	}
}
