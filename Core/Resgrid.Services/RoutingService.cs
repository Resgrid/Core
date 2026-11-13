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
	/// A department with its own routing account (setting 125) has it tried first, then the system chain. Back-off, cache
	/// keys, logs and usage counts are scoped per account, so a department's bad key never benches the system provider.
	/// </summary>
	public class RoutingService : IRoutingService
	{
		/// <summary>One provider in a request's chain: the install's account, or a department's own.</summary>
		private sealed class Link
		{
			public IRoutingProvider Provider { get; init; }

			/// <summary>Back-off, cache and log scope: the provider name, or name#d{departmentId} for a department account.</summary>
			public string Key { get; init; }

			public int DepartmentId { get; init; }

			/// <summary>A department account's settings revision (<see cref="AccountRevision"/>); null for the install's providers.</summary>
			public string Revision { get; init; }

			public bool DepartmentAccount => DepartmentId > 0;
		}

		/// <summary>
		/// A short one-way digest of a department account's settings (provider, URL, key, options): it changes whenever any of
		/// them does and reveals none of them.
		/// </summary>
		private static string AccountRevision(RoutingProviderCredentials credentials)
		{
			return Hash(string.Join("\u001f", credentials.Provider, credentials.Url, credentials.ApiKey, credentials.AuthScheme, credentials.GoogleRoutingApi)).Substring(0, 12);
		}

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
		private readonly IRoutingUsageService _routingUsageService;
		// Lazy: department settings geocode through providers that read these overrides too (construction-time cycle).
		private readonly Lazy<IDepartmentMapProvidersService> _departmentMapProviders;

		public RoutingService(IEnumerable<IRoutingProvider> providers, ICacheProvider cacheProvider, IRoutingUsageService routingUsageService,
			Lazy<IDepartmentMapProvidersService> departmentMapProviders)
		{
			_providers = (providers ?? Enumerable.Empty<IRoutingProvider>()).ToList();
			_cacheProvider = cacheProvider;
			_routingUsageService = routingUsageService;
			_departmentMapProviders = departmentMapProviders;
		}

		public string ProviderName => Primary?.Name ?? RoutingProviderNames.Estimate;

		public RoutingCapabilities Capabilities => (Primary ?? Estimate)?.Capabilities ?? new RoutingCapabilities();

		public string Attribution => AttributionFor(ProviderName);

		private static string AttributionFor(string provider)
		{
			switch (provider)
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

		public bool IsNavigationAvailable => NavigationAvailableOn(Primary);

		private static bool NavigationAvailableOn(IRoutingProvider provider) =>
			MappingConfig.NavigationEnabled && provider != null && provider.Capabilities.Navigation &&
			(provider.Name == RoutingProviderNames.Valhalla || provider.Name == RoutingProviderNames.Mapbox);

		public async Task<RoutingProviderInfo> GetProviderInfoAsync(int departmentId)
		{
			var department = await DepartmentLinkAsync(departmentId);
			var provider = department?.Provider ?? Primary;
			var capabilities = (provider ?? Estimate)?.Capabilities ?? new RoutingCapabilities();
			var name = provider?.Name ?? RoutingProviderNames.Estimate;

			return new RoutingProviderInfo
			{
				Name = name,
				Capabilities = capabilities,
				Attribution = AttributionFor(name),
				NavigationAvailable = NavigationAvailableOn(provider),
				DepartmentAccount = department != null,
				AccountRevision = department?.Revision
			};
		}

		public RoutingCapabilities CapabilitiesOf(string providerName)
		{
			return Find(providerName)?.Capabilities ?? new RoutingCapabilities();
		}

		/// <summary>The department's own routing account as a chain link, or null (none, unusable, or department 0).</summary>
		private async Task<Link> DepartmentLinkAsync(int departmentId)
		{
			if (departmentId <= 0 || _departmentMapProviders == null)
				return null;

			try
			{
				var credentials = (await _departmentMapProviders.Value.GetAsync(departmentId))?.Routing;
				if (credentials == null)
					return null;

				var template = Find(credentials.Provider);
				if (template == null || template.Name == RoutingProviderNames.Estimate)
					return null;

				var provider = template.WithCredentials(credentials);
				if (provider == null || !provider.IsConfigured)
					return null;

				// The account's revision is part of the key: a department that points the same provider at a new server or key
				// gets fresh routes and a fresh back-off, not 30 days of answers from the old account.
				var revision = AccountRevision(credentials);
				return new Link { Provider = provider, Key = $"{provider.Name}#d{departmentId}@{revision}", DepartmentId = departmentId, Revision = revision };
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Reading department {departmentId}'s routing account failed; the system providers answer");
				return null;
			}
		}

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

		/// <summary>
		/// The department's own account (if any), then primary, fallback, estimate — skipping duplicates, unconfigured
		/// providers and ones in back-off.
		/// </summary>
		private async Task<List<Link>> ChainAsync(int departmentId)
		{
			var chain = new List<Link>();

			void Add(Link link)
			{
				if (link?.Provider == null || !link.Provider.IsConfigured || chain.Any(l => l.Key == link.Key))
					return;

				if (link.Provider.Name != RoutingProviderNames.Estimate &&
				    ProviderBackoffUntil.TryGetValue(link.Key, out var until) && until > DateTime.UtcNow)
					return;

				chain.Add(link);
			}

			Link System(IRoutingProvider provider) => provider == null ? null : new Link { Provider = provider, Key = provider.Name };

			Add(await DepartmentLinkAsync(departmentId));
			Add(System(Find(MappingConfig.RoutingProvider)));
			Add(System(Find(MappingConfig.RoutingFallbackProvider)));
			Add(System(Estimate));
			return chain;
		}

		public async Task<RouteResult> GetRouteAsync(RouteRequest request, CancellationToken cancellationToken = default)
		{
			if (request?.Waypoints == null || request.Waypoints.Count < 2)
				return new RouteResult { Error = "A route needs at least two points" };

			foreach (var link in await ChainAsync(request.DepartmentId))
			{
				var provider = link.Provider;
				var trafficAware = IsTrafficAware(provider, request.Profile);

				// Road closures: what this provider is told to avoid, and what is measured on the route afterwards (M0314).
				var closurePlan = RoadClosureRouting.Plan(request, provider.Capabilities, provider.Name == RoutingProviderNames.Estimate);
				var cacheKey = RouteCachePrefix + Hash(link.Key + "\n" + RouteKey(request) + closurePlan.Key);

				if (!request.BypassCache)
				{
					var cached = await ReadCacheAsync<RouteResult>(cacheKey);
					if (cached != null)
						return cached;
				}

				if (request.Waypoints.Count > provider.Capabilities.MaxWaypoints)
					continue;

				var result = await CallProviderAsync(provider, closurePlan.ProviderRequest ?? request, cancellationToken);
				await RecordUsageAsync(link, RoutingUsageOperation.Route, result?.Successful == true, result?.RequestRejected == true, result?.NotSent == true);

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
							await RecordUsageAsync(link, RoutingUsageOperation.Route, rerouted?.Successful == true, rerouted?.RequestRejected == true, rerouted?.NotSent == true);
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

				if (result?.RequestRejected == true)
					RecordRejection(link, result.Error);
				else
					RecordFailure(link, result?.Error);
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

			foreach (var link in await ChainAsync(request.DepartmentId))
			{
				var provider = link.Provider;
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
							keys[i, j] = PairCachePrefix + Hash(link.Key + "\n" + PairKey(request, request.Origins[i], request.Destinations[j]));
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

					var matrixOk = result != null && result.Successful && result.Durations != null;
					await RecordUsageAsync(link, RoutingUsageOperation.Matrix, matrixOk, result?.RequestRejected == true, result?.NotSent == true, batch.Length * destinations);

					if (!matrixOk)
					{
						// One origin outside the provider's coverage fails its whole batch; the next provider takes the
						// remaining origins for this request only.
						if (result?.RequestRejected == true)
							RecordRejection(link, result.Error);
						else
							RecordFailure(link, result?.Error);
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

			// Each provider in the chain that can optimize this many stops gets its turn (a department account that can't, or
			// that fails, hands over to the install's providers, as routes do); the matrix heuristic answers when none does.
			foreach (var link in (await ChainAsync(request.DepartmentId)).Where(l => l.Provider.Name != RoutingProviderNames.Estimate))
			{
				var provider = link.Provider;
				if (!provider.Capabilities.Optimize || request.Waypoints.Count > provider.Capabilities.MaxOptimizeWaypoints)
					continue;

				try
				{
					using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
					timeout.CancelAfter(Math.Max(1000, MappingConfig.RoutingTimeoutMs * 2));
					var optimized = await provider.OptimizeAsync(request, timeout.Token);
					var optimizedOk = optimized != null && optimized.Successful && IsPermutation(optimized.Order, request.Waypoints.Count);
					await RecordUsageAsync(link, RoutingUsageOperation.Optimize, optimizedOk, optimized?.RequestRejected == true, optimized?.NotSent == true);

					if (optimizedOk)
						return optimized;
				}
				catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
				{
					// The next provider, then the heuristic.
					await RecordUsageAsync(link, RoutingUsageOperation.Optimize, false, false, false);
				}
				catch (Exception ex)
				{
					await RecordUsageAsync(link, RoutingUsageOperation.Optimize, false, false, false);
					RecordFailure(link, ex.Message);
				}
			}

			// Heuristic on a travel-time matrix (provider matrix when it can, else the estimate).
			var matrix = await GetMatrixAsync(new RouteMatrixRequest
			{
				Origins = request.Waypoints,
				Destinations = request.Waypoints,
				DepartmentId = request.DepartmentId,
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

		/// <summary>Counts one provider call for the usage report (IRoutingUsageService); the estimate is never counted.</summary>
		private async Task RecordUsageAsync(Link link, RoutingUsageOperation operation, bool succeeded, bool rejected, bool notSent, int elements = 0)
		{
			if (_routingUsageService == null || link?.Provider == null || link.Provider.Name == RoutingProviderNames.Estimate)
				return;

			var outcome = succeeded ? RoutingUsageOutcome.Succeeded
				: notSent ? RoutingUsageOutcome.NotSent
				: rejected ? RoutingUsageOutcome.Rejected
				: RoutingUsageOutcome.Failed;

			await _routingUsageService.RecordAsync(link.Provider.Name, operation, outcome, elements, link.DepartmentAccount);
		}

		private static bool IsTrafficAware(IRoutingProvider provider, RoutingProfile profile)
		{
			return profile == RoutingProfile.DrivingTraffic && MappingConfig.RoutingTrafficAware && provider.Capabilities.Traffic;
		}

		private static void RecordFailure(Link link, string error)
		{
			var provider = link?.Provider;
			if (provider == null || provider.Name == RoutingProviderNames.Estimate)
				return;

			ProviderBackoffUntil[link.Key] = DateTime.UtcNow.Add(FailureBackoff);

			var now = DateTime.UtcNow;
			if (LastErrorLogged.TryGetValue(link.Key, out var last) && now - last < ErrorLogInterval)
				return;

			LastErrorLogged[link.Key] = now;

			// A department's own account failing is the department's to fix (Mapping Settings), not an install fault.
			if (link.DepartmentAccount)
			{
				Logging.LogInfo($"Department {link.DepartmentId}'s own routing account ({provider.Name}) failed: {error}; the system providers answer for a minute.");
				return;
			}

			Logging.LogError($"Routing provider '{provider.Name}' failed: {error}. Check MappingConfig.RoutingProvider and the provider's settings ({SettingsFor(provider.Name)}); falling back to {MappingConfig.RoutingFallbackProvider} then the straight-line estimate.");
		}

		/// <summary>
		/// The provider is up but cannot route this request (outside its coverage, no road near a point, over its limits):
		/// the next provider answers it and the provider stays in use for everything else. Logged at most every 10 minutes.
		/// </summary>
		private static void RecordRejection(Link link, string error)
		{
			var provider = link?.Provider;
			if (provider == null || provider.Name == RoutingProviderNames.Estimate)
				return;

			var key = link.Key + ":rejected";
			var now = DateTime.UtcNow;
			if (LastErrorLogged.TryGetValue(key, out var last) && now - last < ErrorLogInterval)
				return;

			LastErrorLogged[key] = now;
			Logging.LogInfo($"Routing provider '{provider.Name}' could not route a request ({error}); {MappingConfig.RoutingFallbackProvider} answered it instead. The provider stays in use for other requests.");
		}

		private static string SettingsFor(string provider)
		{
			switch (provider)
			{
				case RoutingProviderNames.Valhalla:
					return "MappingConfig.ValhallaUrl, MappingConfig.ValhallaApiKey, MappingConfig.ValhallaAuthScheme";
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

			// Exclusions a caller set itself change the route; none (every caller today) leaves the key as it was.
			builder.Append(RoadClosureRouting.ExcludePointsKey(request));

			if (request.ExcludeAreas?.Count > 0)
			{
				builder.Append("|xa:");
				foreach (var area in request.ExcludeAreas)
				{
					foreach (var point in area ?? new List<RoutingWaypoint>())
						builder.Append(Coordinate(point.Latitude)).Append(',').Append(Coordinate(point.Longitude)).Append(';');

					builder.Append('/');
				}
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
