using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Routes, matrices and stop-order optimization through the install's routing provider
	/// (maplibre-mapping-routing-navigation-plan.md §6.1): the provider MappingConfig.RoutingProvider names, then
	/// RoutingFallbackProvider, then the straight-line estimate. Each call is bounded by RoutingTimeoutMs; results are
	/// cached in Redis by provider, profile, vehicle and coordinates (30 days, 5 minutes when traffic-aware); a provider
	/// that just failed is skipped for a short back-off so a broken key or server does not slow every request.
	/// </summary>
	public class RoutingService : IRoutingService
	{
		private const string RouteCachePrefix = "RtR1_";
		private const string PairCachePrefix = "RtP1_";
		private static readonly TimeSpan SuccessCacheLength = TimeSpan.FromDays(30);
		private static readonly TimeSpan TrafficCacheLength = TimeSpan.FromMinutes(5);
		private static readonly TimeSpan FailureBackoff = TimeSpan.FromSeconds(60);
		private static readonly TimeSpan ErrorLogInterval = TimeSpan.FromMinutes(10);

		private static readonly ConcurrentDictionary<string, DateTime> ProviderBackoffUntil = new ConcurrentDictionary<string, DateTime>();
		private static readonly ConcurrentDictionary<string, DateTime> LastErrorLogged = new ConcurrentDictionary<string, DateTime>();

		private readonly IReadOnlyList<IRoutingProvider> _providers;
		private readonly ICacheProvider _cacheProvider;

		public RoutingService(IEnumerable<IRoutingProvider> providers, ICacheProvider cacheProvider)
		{
			_providers = (providers ?? Enumerable.Empty<IRoutingProvider>()).ToList();
			_cacheProvider = cacheProvider;
		}

		public string ProviderName => Primary?.Name ?? RoutingProviderNames.Estimate;

		public RoutingCapabilities Capabilities => (Primary ?? Estimate)?.Capabilities ?? new RoutingCapabilities();

		public string Attribution
		{
			get
			{
				switch (ProviderName)
				{
					case RoutingProviderNames.Mapbox:
						return "© Mapbox © OpenStreetMap contributors";
					case RoutingProviderNames.Google:
						return "Map data © Google";
					case RoutingProviderNames.Valhalla:
					case RoutingProviderNames.Osrm:
						return "© OpenStreetMap contributors";
					default:
						return string.Empty;
				}
			}
		}

		public bool IsNavigationAvailable =>
			MappingConfig.NavigationEnabled && Primary != null && Primary.Capabilities.Navigation &&
			(Primary.Name == RoutingProviderNames.Valhalla || Primary.Name == RoutingProviderNames.Mapbox);

		/// <summary>The configured primary provider, or null when it is the estimate or not configured.</summary>
		private IRoutingProvider Primary
		{
			get
			{
				var provider = Find(MappingConfig.RoutingProvider);
				return provider != null && provider.Name != RoutingProviderNames.Estimate && provider.IsConfigured ? provider : null;
			}
		}

		private IRoutingProvider Estimate => Find(RoutingProviderNames.Estimate);

		private IRoutingProvider Find(string name)
		{
			var normalized = RoutingProviderNames.Normalize(name);
			return _providers.FirstOrDefault(p => p.Name == normalized);
		}

		/// <summary>Primary, fallback, estimate — skipping duplicates, unconfigured providers and ones in back-off.</summary>
		private List<IRoutingProvider> Chain()
		{
			var chain = new List<IRoutingProvider>();

			void Add(IRoutingProvider provider)
			{
				if (provider == null || !provider.IsConfigured || chain.Any(p => p.Name == provider.Name))
					return;

				if (provider.Name != RoutingProviderNames.Estimate &&
				    ProviderBackoffUntil.TryGetValue(provider.Name, out var until) && until > DateTime.UtcNow)
					return;

				chain.Add(provider);
			}

			Add(Find(MappingConfig.RoutingProvider));
			Add(Find(MappingConfig.RoutingFallbackProvider));
			Add(Estimate);
			return chain;
		}

		public async Task<RouteResult> GetRouteAsync(RouteRequest request, CancellationToken cancellationToken = default)
		{
			if (request?.Waypoints == null || request.Waypoints.Count < 2)
				return new RouteResult { Error = "A route needs at least two points" };

			foreach (var provider in Chain())
			{
				var trafficAware = IsTrafficAware(provider, request.Profile);

				// Road closures: what this provider is told to avoid, and what is measured on the route afterwards (M0314).
				var closurePlan = RoadClosureRouting.Plan(request, provider.Capabilities, provider.Name == RoutingProviderNames.Estimate);
				var cacheKey = RouteCachePrefix + Hash(provider.Name + "\n" + RouteKey(request) + closurePlan.Key);

				if (!request.BypassCache)
				{
					var cached = await ReadCacheAsync<RouteResult>(cacheKey);
					if (cached != null)
						return cached;
				}

				if (request.Waypoints.Count > provider.Capabilities.MaxWaypoints)
					continue;

				var result = await CallProviderAsync(provider, closurePlan.ProviderRequest ?? request, cancellationToken);

				if (result != null && result.Successful)
				{
					result.Provider = provider.Name;

					if (closurePlan.HasClosures)
					{
						// A provider that excludes by point gets the places the route went into a full closure, and tries again.
						for (var retry = 0; retry < RoadClosureRouting.MaxAvoidanceRetries; retry++)
						{
							var entries = RoadClosureRouting.EntryPointsToExclude(result, closurePlan);
							if (entries.Count == 0)
								break;

							var rerouted = await CallProviderAsync(provider, RoadClosureRouting.WithExtraExcludePoints(closurePlan, entries), cancellationToken);
							if (rerouted == null || !rerouted.Successful)
								break;

							rerouted.Provider = provider.Name;
							result = rerouted;
						}

						RoadClosureRouting.Apply(result, closurePlan);
					}

					// Closure-shaped routes are kept briefly: windows open and close, and edits change the closure set.
					if (provider.Name != RoutingProviderNames.Estimate)
						await WriteCacheAsync(cacheKey, result, trafficAware || result.IsTrafficAware || closurePlan.HasClosures ? TrafficCacheLength : SuccessCacheLength);

					return result;
				}

				RecordFailure(provider, result?.Error);
			}

			return new RouteResult { Error = "No routing provider returned a route" };
		}

		private static async Task<RouteResult> CallProviderAsync(IRoutingProvider provider, RouteRequest request, CancellationToken cancellationToken)
		{
			try
			{
				using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
				timeout.CancelAfter(Math.Max(500, MappingConfig.RoutingTimeoutMs));
				return await provider.GetRouteAsync(request, timeout.Token);
			}
			catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
			{
				return new RouteResult { Provider = provider.Name, Error = "Timed out" };
			}
			catch (Exception ex)
			{
				return new RouteResult { Provider = provider.Name, Error = ex.Message };
			}
		}

		public async Task<RouteMatrixResult> GetMatrixAsync(RouteMatrixRequest request, CancellationToken cancellationToken = default)
		{
			if (request?.Origins == null || request.Destinations == null || request.Origins.Count == 0 || request.Destinations.Count == 0)
				return new RouteMatrixResult { Successful = true, Durations = new double?[0][], Distances = new double?[0][] };

			var origins = request.Origins.Count;
			var destinations = request.Destinations.Count;
			var durations = Enumerable.Range(0, origins).Select(_ => new double?[destinations]).ToArray();
			var distances = Enumerable.Range(0, origins).Select(_ => new double?[destinations]).ToArray();
			var pending = Enumerable.Range(0, origins).ToList();
			string provided = null;

			foreach (var provider in Chain())
			{
				var trafficAware = IsTrafficAware(provider, request.Profile);
				var keys = new string[origins, destinations];

				// Pairs already in the cache (from this or an earlier board) are not asked for again.
				if (provider.Name != RoutingProviderNames.Estimate)
				{
					foreach (var i in pending.ToList())
					{
						var complete = true;
						for (var j = 0; j < destinations; j++)
						{
							keys[i, j] = PairCachePrefix + Hash(provider.Name + "\n" + PairKey(request, request.Origins[i], request.Destinations[j]));
							var cached = await ReadCacheAsync<CachedPair>(keys[i, j]);
							if (cached != null)
							{
								durations[i][j] = cached.Seconds;
								distances[i][j] = cached.Meters;
							}
							else
							{
								complete = false;
							}
						}

						if (complete)
							pending.Remove(i);
					}
				}

				if (pending.Count == 0)
					return new RouteMatrixResult { Successful = true, Provider = provided ?? provider.Name, Durations = durations, Distances = distances };

				var batchSize = provider.Name == RoutingProviderNames.Valhalla || provider.Name == RoutingProviderNames.Osrm
					? Math.Max(1, MappingConfig.RoutingMatrixMaxOrigins)
					: Math.Max(1, provider.Capabilities.MaxMatrixCoordinates - destinations);

				var failed = false;
				foreach (var batch in pending.ToList().Chunk(batchSize))
				{
					RouteMatrixResult result;
					try
					{
						using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
						timeout.CancelAfter(Math.Max(1000, MappingConfig.RoutingTimeoutMs * 2));
						result = await provider.GetMatrixAsync(new RouteMatrixRequest
						{
							Origins = batch.Select(i => request.Origins[i]).ToList(),
							Destinations = request.Destinations,
							Profile = request.Profile,
							Vehicle = request.Vehicle,
							Avoid = request.Avoid
						}, timeout.Token);
					}
					catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
					{
						result = new RouteMatrixResult { Provider = provider.Name, Error = "Timed out" };
					}
					catch (Exception ex)
					{
						result = new RouteMatrixResult { Provider = provider.Name, Error = ex.Message };
					}

					if (result == null || !result.Successful || result.Durations == null)
					{
						RecordFailure(provider, result?.Error);
						failed = true;
						break;
					}

					for (var b = 0; b < batch.Length; b++)
					{
						var i = batch[b];
						for (var j = 0; j < destinations; j++)
						{
							var seconds = b < result.Durations.Length && result.Durations[b] != null && j < result.Durations[b].Length ? result.Durations[b][j] : null;
							var meters = result.Distances != null && b < result.Distances.Length && result.Distances[b] != null && j < result.Distances[b].Length ? result.Distances[b][j] : null;
							durations[i][j] = seconds;
							distances[i][j] = meters;

							if (seconds.HasValue && provider.Name != RoutingProviderNames.Estimate && keys[i, j] != null)
								await WriteCacheAsync(keys[i, j], new CachedPair { Seconds = seconds, Meters = meters }, trafficAware ? TrafficCacheLength : SuccessCacheLength);
						}

						pending.Remove(i);
					}

					provided ??= provider.Name;
				}

				if (!failed && pending.Count == 0)
					return new RouteMatrixResult { Successful = true, Provider = provided ?? provider.Name, Durations = durations, Distances = distances };
			}

			return new RouteMatrixResult { Successful = pending.Count < origins, Provider = provided, Durations = durations, Distances = distances, Error = pending.Count > 0 ? "Some origins could not be routed" : null };
		}

		public async Task<RouteOptimizationResult> OptimizeAsync(RouteOptimizationRequest request, CancellationToken cancellationToken = default)
		{
			if (request?.Waypoints == null || request.Waypoints.Count <= 2)
				return new RouteOptimizationResult { Successful = true, Order = Enumerable.Range(0, request?.Waypoints?.Count ?? 0).ToList() };

			var primary = Primary;
			if (primary != null && primary.Capabilities.Optimize && request.Waypoints.Count <= primary.Capabilities.MaxOptimizeWaypoints)
			{
				try
				{
					using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
					timeout.CancelAfter(Math.Max(1000, MappingConfig.RoutingTimeoutMs * 2));
					var optimized = await primary.OptimizeAsync(request, timeout.Token);

					if (optimized != null && optimized.Successful && IsPermutation(optimized.Order, request.Waypoints.Count))
						return optimized;
				}
				catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
				{
					// Fall through to the heuristic.
				}
				catch (Exception ex)
				{
					RecordFailure(primary, ex.Message);
				}
			}

			// Heuristic on a travel-time matrix (provider matrix when it can, else the estimate).
			var matrix = await GetMatrixAsync(new RouteMatrixRequest
			{
				Origins = request.Waypoints,
				Destinations = request.Waypoints,
				Profile = request.Profile,
				Vehicle = request.Vehicle,
				Avoid = request.Avoid
			}, cancellationToken);

			var order = RouteOrderOptimizer.Optimize(matrix.Durations, request.KeepFirst, request.KeepLast);
			return new RouteOptimizationResult
			{
				Successful = true,
				Provider = matrix.Provider ?? RoutingProviderNames.Estimate,
				Order = order,
				DurationSeconds = RouteOrderOptimizer.TotalCost(order, matrix.Durations),
				DistanceMeters = matrix.Distances != null ? RouteOrderOptimizer.TotalCost(order, matrix.Distances) : 0
			};
		}

		private static bool IsPermutation(List<int> order, int count)
		{
			return order != null && order.Count == count && order.Distinct().Count() == count && order.All(i => i >= 0 && i < count);
		}

		private static bool IsTrafficAware(IRoutingProvider provider, RoutingProfile profile)
		{
			return profile == RoutingProfile.DrivingTraffic && MappingConfig.RoutingTrafficAware && provider.Capabilities.Traffic;
		}

		private static void RecordFailure(IRoutingProvider provider, string error)
		{
			if (provider == null || provider.Name == RoutingProviderNames.Estimate)
				return;

			ProviderBackoffUntil[provider.Name] = DateTime.UtcNow.Add(FailureBackoff);

			var now = DateTime.UtcNow;
			if (LastErrorLogged.TryGetValue(provider.Name, out var last) && now - last < ErrorLogInterval)
				return;

			LastErrorLogged[provider.Name] = now;
			Logging.LogError($"Routing provider '{provider.Name}' failed: {error}. Check MappingConfig.RoutingProvider and the provider's settings ({SettingsFor(provider.Name)}); falling back to {MappingConfig.RoutingFallbackProvider} then the straight-line estimate.");
		}

		private static string SettingsFor(string provider)
		{
			switch (provider)
			{
				case RoutingProviderNames.Valhalla:
					return "MappingConfig.ValhallaUrl, MappingConfig.ValhallaApiKey";
				case RoutingProviderNames.Mapbox:
					return "MappingConfig.MapboxRoutingAccessToken";
				case RoutingProviderNames.Google:
					return "MappingConfig.GoogleMapsApiKey, MappingConfig.GoogleRoutingApi";
				case RoutingProviderNames.Osrm:
					return "MappingConfig.OsrmUrl";
				default:
					return "none";
			}
		}

		private async Task<T> ReadCacheAsync<T>(string key) where T : class
		{
			if (!SystemBehaviorConfig.CacheEnabled || _cacheProvider == null)
				return null;

			try
			{
				var text = await _cacheProvider.GetStringAsync(key);
				return string.IsNullOrWhiteSpace(text) ? null : JsonSerializer.Deserialize<T>(text);
			}
			catch
			{
				return null;
			}
		}

		private async Task WriteCacheAsync(string key, object value, TimeSpan length)
		{
			if (!SystemBehaviorConfig.CacheEnabled || _cacheProvider == null)
				return;

			try
			{
				await _cacheProvider.SetStringAsync(key, JsonSerializer.Serialize(value), length);
			}
			catch
			{
				// A cache outage only costs a repeat lookup.
			}
		}

		private sealed class CachedPair
		{
			public double? Seconds { get; set; }
			public double? Meters { get; set; }
		}

		private static string RouteKey(RouteRequest request)
		{
			var builder = new StringBuilder();
			builder.Append((int)request.Profile).Append('|').Append((int)request.Avoid).Append('|')
				.Append(request.Vehicle?.Key() ?? string.Empty).Append('|')
				.Append(request.Steps ? 1 : 0).Append(request.NavigationInstructions ? 1 : 0).Append('|')
				.Append(request.Language ?? string.Empty).Append('|').Append((int)request.Units).Append('|');

			foreach (var waypoint in request.Waypoints)
			{
				builder.Append(Coordinate(waypoint.Latitude)).Append(',').Append(Coordinate(waypoint.Longitude))
					.Append(waypoint.IsStop ? 's' : 't')
					.Append(waypoint.Heading.HasValue ? Math.Round(waypoint.Heading.Value).ToString(CultureInfo.InvariantCulture) : string.Empty)
					.Append(';');
			}

			return builder.ToString();
		}

		private static string PairKey(RouteMatrixRequest request, RoutingWaypoint origin, RoutingWaypoint destination)
		{
			return string.Join("|", (int)request.Profile, (int)request.Avoid, request.Vehicle?.Key() ?? string.Empty,
				Coordinate(origin.Latitude), Coordinate(origin.Longitude), Coordinate(destination.Latitude), Coordinate(destination.Longitude));
		}

		// Five decimals (about a metre) so a unit that has not moved shares its cached times.
		private static string Coordinate(double value)
		{
			return Math.Round(value, 5).ToString("0.#####", CultureInfo.InvariantCulture);
		}

		private static string Hash(string value)
		{
			return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)), 0, 16).ToLowerInvariant();
		}
	}
}
