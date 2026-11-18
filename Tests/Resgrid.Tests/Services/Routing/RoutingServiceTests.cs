using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Providers.GeoLocationProvider.Routing;
using Resgrid.Services;

namespace Resgrid.Tests.Services.Routing
{
	/// <summary>
	/// The install's routing provider chain (maplibre-mapping-routing-navigation-plan.md §6.1): the configured provider,
	/// then the fallback, then the straight-line estimate; in-app navigation only on Valhalla and Mapbox (D26).
	/// </summary>
	[TestFixture]
	public class RoutingServiceTests
	{
		private string _provider;
		private string _fallback;
		private bool _navigation;
		private bool _cacheEnabled;

		[SetUp]
		public void SetUp()
		{
			_provider = MappingConfig.RoutingProvider;
			_fallback = MappingConfig.RoutingFallbackProvider;
			_navigation = MappingConfig.NavigationEnabled;
			_cacheEnabled = SystemBehaviorConfig.CacheEnabled;
			SystemBehaviorConfig.CacheEnabled = false;
			MappingConfig.NavigationEnabled = true;
			ClearProviderHealth();
		}

		[TearDown]
		public void TearDown()
		{
			MappingConfig.RoutingProvider = _provider;
			MappingConfig.RoutingFallbackProvider = _fallback;
			MappingConfig.NavigationEnabled = _navigation;
			SystemBehaviorConfig.CacheEnabled = _cacheEnabled;
			ClearProviderHealth();
		}

		/// <summary>
		/// A failed provider is benched for 60 s in process-wide state; clear it so one test's failure never decides
		/// another's chain.
		/// </summary>
		private static void ClearProviderHealth()
		{
			foreach (var name in new[] { "ProviderBackoffUntil", "LastErrorLogged" })
			{
				var field = typeof(RoutingService).GetField(name, BindingFlags.NonPublic | BindingFlags.Static)!;
				((ConcurrentDictionary<string, DateTime>)field.GetValue(null)!).Clear();
			}
		}

		private static Mock<IRoutingProvider> Provider(string name, bool navigation, RouteResult route = null, bool configured = true)
		{
			var provider = new Mock<IRoutingProvider>();
			provider.SetupGet(p => p.Name).Returns(name);
			provider.SetupGet(p => p.IsConfigured).Returns(configured);
			provider.SetupGet(p => p.Capabilities).Returns(new RoutingCapabilities { Steps = true, Navigation = navigation, Matrix = true, MaxWaypoints = 25, MaxMatrixCoordinates = 25 });
			provider.Setup(p => p.GetRouteAsync(It.IsAny<RouteRequest>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(route ?? new RouteResult { Successful = false, Error = "down" });
			return provider;
		}

		private static RouteRequest TwoPoints()
		{
			return new RouteRequest
			{
				Waypoints = new List<RoutingWaypoint> { new RoutingWaypoint(39.53, -119.81), new RoutingWaypoint(39.54, -119.75) }
			};
		}

		[Test]
		public async Task configured_provider_answers_first()
		{
			MappingConfig.RoutingProvider = RoutingProviderNames.Valhalla;
			MappingConfig.RoutingFallbackProvider = RoutingProviderNames.Estimate;
			var valhalla = Provider(RoutingProviderNames.Valhalla, true, new RouteResult { Successful = true, DurationSeconds = 420 });
			var service = new RoutingService(new[] { valhalla.Object, new EstimateRoutingProvider() }, null, null, null);

			var route = await service.GetRouteAsync(TwoPoints());

			route.Successful.Should().BeTrue();
			route.Provider.Should().Be(RoutingProviderNames.Valhalla);
			route.DurationSeconds.Should().Be(420);
		}

		[Test]
		public async Task failed_provider_falls_back_to_the_fallback_then_the_estimate()
		{
			MappingConfig.RoutingProvider = RoutingProviderNames.Mapbox;
			MappingConfig.RoutingFallbackProvider = RoutingProviderNames.Osrm;
			var mapbox = Provider(RoutingProviderNames.Mapbox, true);
			var osrm = Provider(RoutingProviderNames.Osrm, false);
			var service = new RoutingService(new[] { mapbox.Object, osrm.Object, new EstimateRoutingProvider() }, null, null, null);

			var route = await service.GetRouteAsync(TwoPoints());

			route.Successful.Should().BeTrue();
			route.Provider.Should().Be(RoutingProviderNames.Estimate);
			route.DurationSeconds.Should().BeGreaterThan(0);
			mapbox.Verify(p => p.GetRouteAsync(It.IsAny<RouteRequest>(), It.IsAny<CancellationToken>()), Times.Once);
			osrm.Verify(p => p.GetRouteAsync(It.IsAny<RouteRequest>(), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task unconfigured_provider_is_skipped()
		{
			MappingConfig.RoutingProvider = RoutingProviderNames.Valhalla;
			MappingConfig.RoutingFallbackProvider = RoutingProviderNames.Estimate;
			var valhalla = Provider(RoutingProviderNames.Valhalla, true, new RouteResult { Successful = true }, configured: false);
			var service = new RoutingService(new[] { valhalla.Object, new EstimateRoutingProvider() }, null, null, null);

			var route = await service.GetRouteAsync(TwoPoints());

			route.Provider.Should().Be(RoutingProviderNames.Estimate);
			valhalla.Verify(p => p.GetRouteAsync(It.IsAny<RouteRequest>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[TestCase(RoutingProviderNames.Valhalla, true)]
		[TestCase(RoutingProviderNames.Mapbox, true)]
		[TestCase(RoutingProviderNames.Google, false)]
		[TestCase(RoutingProviderNames.Osrm, false)]
		[TestCase(RoutingProviderNames.Estimate, false)]
		public void navigation_is_available_only_on_valhalla_and_mapbox(string providerName, bool expected)
		{
			MappingConfig.RoutingProvider = providerName;
			IRoutingProvider provider = providerName switch
			{
				RoutingProviderNames.Valhalla => Provider(providerName, true).Object,
				RoutingProviderNames.Mapbox => Provider(providerName, true).Object,
				RoutingProviderNames.Google => Provider(providerName, false).Object,
				RoutingProviderNames.Osrm => Provider(providerName, false).Object,
				_ => new EstimateRoutingProvider()
			};
			var providers = new List<IRoutingProvider> { provider };
			if (providerName != RoutingProviderNames.Estimate)
				providers.Add(new EstimateRoutingProvider());

			new RoutingService(providers, null, null, null).IsNavigationAvailable.Should().Be(expected);
		}

		[Test]
		public void navigation_switch_turns_it_off_everywhere()
		{
			MappingConfig.RoutingProvider = RoutingProviderNames.Valhalla;
			MappingConfig.NavigationEnabled = false;
			var service = new RoutingService(new[] { Provider(RoutingProviderNames.Valhalla, true).Object, new EstimateRoutingProvider() }, null, null, null);

			service.IsNavigationAvailable.Should().BeFalse();
		}

		[Test]
		public async Task matrix_from_the_estimate_reports_the_estimate_provider()
		{
			MappingConfig.RoutingProvider = RoutingProviderNames.Estimate;
			var service = new RoutingService(new IRoutingProvider[] { new EstimateRoutingProvider() }, null, null, null);

			var matrix = await service.GetMatrixAsync(new RouteMatrixRequest
			{
				Origins = new List<RoutingWaypoint> { new RoutingWaypoint(39.53, -119.81), new RoutingWaypoint(39.60, -119.80) },
				Destinations = new List<RoutingWaypoint> { new RoutingWaypoint(39.54, -119.75) }
			});

			matrix.Successful.Should().BeTrue();
			matrix.Provider.Should().Be(RoutingProviderNames.Estimate);
			matrix.Durations.Should().HaveCount(2);
			matrix.Durations[0][0].Should().BeLessThan(matrix.Durations[1][0].Value);
		}

		[Test]
		public async Task optimize_uses_the_heuristic_when_the_provider_has_none()
		{
			MappingConfig.RoutingProvider = RoutingProviderNames.Estimate;
			var service = new RoutingService(new IRoutingProvider[] { new EstimateRoutingProvider() }, null, null, null);

			// Start, a far stop, a near stop, end next to the far stop: near stop first is shorter.
			var result = await service.OptimizeAsync(new RouteOptimizationRequest
			{
				Waypoints = new List<RoutingWaypoint>
				{
					new RoutingWaypoint(39.50, -119.80),
					new RoutingWaypoint(39.60, -119.80),
					new RoutingWaypoint(39.51, -119.80),
					new RoutingWaypoint(39.61, -119.80)
				}
			});

			result.Successful.Should().BeTrue();
			result.Order.Should().Equal(0, 2, 1, 3);
		}

		[Test]
		public async Task rejected_route_goes_to_the_fallback_without_benching_the_provider()
		{
			// A hosted Valhalla that only covers some regions refuses one department's request; it must keep answering the rest.
			MappingConfig.RoutingProvider = RoutingProviderNames.Valhalla;
			MappingConfig.RoutingFallbackProvider = RoutingProviderNames.Estimate;
			var valhalla = Provider(RoutingProviderNames.Valhalla, true,
				new RouteResult { Successful = false, Error = "400: NoSegment", RequestRejected = true });
			var service = new RoutingService(new[] { valhalla.Object, new EstimateRoutingProvider() }, null, null, null);

			var first = await service.GetRouteAsync(TwoPoints());
			var second = await service.GetRouteAsync(TwoPoints());

			first.Successful.Should().BeTrue();
			first.Provider.Should().Be(RoutingProviderNames.Estimate);
			second.Provider.Should().Be(RoutingProviderNames.Estimate);
			valhalla.Verify(p => p.GetRouteAsync(It.IsAny<RouteRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
		}

		[Test]
		public async Task rejected_matrix_goes_to_the_fallback_without_benching_the_provider()
		{
			MappingConfig.RoutingProvider = RoutingProviderNames.Valhalla;
			MappingConfig.RoutingFallbackProvider = RoutingProviderNames.Estimate;
			var valhalla = Provider(RoutingProviderNames.Valhalla, true);
			valhalla.Setup(p => p.GetMatrixAsync(It.IsAny<RouteMatrixRequest>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new RouteMatrixResult { Successful = false, Error = "Outside MappingConfig.ValhallaCoverage", RequestRejected = true });
			var service = new RoutingService(new[] { valhalla.Object, new EstimateRoutingProvider() }, null, null, null);
			var request = new RouteMatrixRequest
			{
				Origins = new List<RoutingWaypoint> { new RoutingWaypoint(39.53, -119.81), new RoutingWaypoint(-33.8688, 151.2093) },
				Destinations = new List<RoutingWaypoint> { new RoutingWaypoint(39.54, -119.75) }
			};

			var first = await service.GetMatrixAsync(request);
			var second = await service.GetMatrixAsync(request);

			first.Successful.Should().BeTrue();
			first.Provider.Should().Be(RoutingProviderNames.Estimate);
			first.Durations[0][0].Should().NotBeNull();
			second.Provider.Should().Be(RoutingProviderNames.Estimate);
			valhalla.Verify(p => p.GetMatrixAsync(It.IsAny<RouteMatrixRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
		}

		[Test]
		public async Task failed_valhalla_falls_back_to_mapbox_before_the_estimate_even_with_an_estimate_fallback()
		{
			// Production (2026-11-13): SimpleRouting answered 429 and the dispatch recommendation got the straight-line estimate,
			// because RoutingFallbackProvider was left at "estimate" although the install has a Mapbox routing token.
			MappingConfig.RoutingProvider = RoutingProviderNames.Valhalla;
			MappingConfig.RoutingFallbackProvider = RoutingProviderNames.Estimate;
			var valhalla = Provider(RoutingProviderNames.Valhalla, true, new RouteResult { Successful = false, Error = "429: No route" });
			var mapbox = Provider(RoutingProviderNames.Mapbox, true, new RouteResult { Successful = true, DurationSeconds = 510 });
			var service = new RoutingService(new[] { valhalla.Object, mapbox.Object, new EstimateRoutingProvider() }, null, null, null);

			var first = await service.GetRouteAsync(TwoPoints());
			var second = await service.GetRouteAsync(TwoPoints());

			first.Successful.Should().BeTrue();
			first.Provider.Should().Be(RoutingProviderNames.Mapbox);
			first.DurationSeconds.Should().Be(510);
			second.Provider.Should().Be(RoutingProviderNames.Mapbox, "Mapbox keeps answering while Valhalla sits out its back-off");
			valhalla.Verify(p => p.GetRouteAsync(It.IsAny<RouteRequest>(), It.IsAny<CancellationToken>()), Times.Once);
			mapbox.Verify(p => p.GetRouteAsync(It.IsAny<RouteRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
		}

		[Test]
		public async Task rejected_valhalla_route_goes_to_mapbox_before_the_estimate()
		{
			MappingConfig.RoutingProvider = RoutingProviderNames.Valhalla;
			MappingConfig.RoutingFallbackProvider = RoutingProviderNames.Estimate;
			var valhalla = Provider(RoutingProviderNames.Valhalla, true,
				new RouteResult { Successful = false, Error = "Outside MappingConfig.ValhallaCoverage", RequestRejected = true, NotSent = true });
			var mapbox = Provider(RoutingProviderNames.Mapbox, true, new RouteResult { Successful = true, DurationSeconds = 300 });
			var service = new RoutingService(new[] { valhalla.Object, mapbox.Object, new EstimateRoutingProvider() }, null, null, null);

			var route = await service.GetRouteAsync(TwoPoints());

			route.Provider.Should().Be(RoutingProviderNames.Mapbox);
			route.DurationSeconds.Should().Be(300);
		}

		[Test]
		public async Task a_configured_fallback_is_tried_before_the_added_mapbox()
		{
			MappingConfig.RoutingProvider = RoutingProviderNames.Valhalla;
			MappingConfig.RoutingFallbackProvider = RoutingProviderNames.Osrm;
			var valhalla = Provider(RoutingProviderNames.Valhalla, true);
			var osrm = Provider(RoutingProviderNames.Osrm, false);
			var mapbox = Provider(RoutingProviderNames.Mapbox, true, new RouteResult { Successful = true, DurationSeconds = 300 });
			var service = new RoutingService(new[] { valhalla.Object, osrm.Object, mapbox.Object, new EstimateRoutingProvider() }, null, null, null);

			var route = await service.GetRouteAsync(TwoPoints());

			route.Provider.Should().Be(RoutingProviderNames.Mapbox);
			osrm.Verify(p => p.GetRouteAsync(It.IsAny<RouteRequest>(), It.IsAny<CancellationToken>()), Times.Once);
			mapbox.Verify(p => p.GetRouteAsync(It.IsAny<RouteRequest>(), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task failed_valhalla_without_a_mapbox_token_falls_back_to_the_estimate()
		{
			MappingConfig.RoutingProvider = RoutingProviderNames.Valhalla;
			MappingConfig.RoutingFallbackProvider = RoutingProviderNames.Estimate;
			var valhalla = Provider(RoutingProviderNames.Valhalla, true, new RouteResult { Successful = false, Error = "503: unavailable" });
			var mapbox = Provider(RoutingProviderNames.Mapbox, true, new RouteResult { Successful = true }, configured: false);
			var service = new RoutingService(new[] { valhalla.Object, mapbox.Object, new EstimateRoutingProvider() }, null, null, null);

			var route = await service.GetRouteAsync(TwoPoints());

			route.Successful.Should().BeTrue();
			route.Provider.Should().Be(RoutingProviderNames.Estimate);
			mapbox.Verify(p => p.GetRouteAsync(It.IsAny<RouteRequest>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task mapbox_is_not_added_when_valhalla_is_not_in_use()
		{
			// Mapbox is billed per call: an install on Google with an estimate fallback keeps exactly that chain.
			MappingConfig.RoutingProvider = RoutingProviderNames.Google;
			MappingConfig.RoutingFallbackProvider = RoutingProviderNames.Estimate;
			var google = Provider(RoutingProviderNames.Google, false);
			var mapbox = Provider(RoutingProviderNames.Mapbox, true, new RouteResult { Successful = true });
			var service = new RoutingService(new[] { google.Object, mapbox.Object, new EstimateRoutingProvider() }, null, null, null);

			var route = await service.GetRouteAsync(TwoPoints());

			route.Provider.Should().Be(RoutingProviderNames.Estimate);
			mapbox.Verify(p => p.GetRouteAsync(It.IsAny<RouteRequest>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task failed_valhalla_matrix_falls_back_to_the_mapbox_matrix()
		{
			MappingConfig.RoutingProvider = RoutingProviderNames.Valhalla;
			MappingConfig.RoutingFallbackProvider = RoutingProviderNames.Estimate;
			var valhalla = Provider(RoutingProviderNames.Valhalla, true);
			valhalla.Setup(p => p.GetMatrixAsync(It.IsAny<RouteMatrixRequest>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new RouteMatrixResult { Successful = false, Error = "Matrix failed (429)" });
			var mapbox = Provider(RoutingProviderNames.Mapbox, true);
			mapbox.Setup(p => p.GetMatrixAsync(It.IsAny<RouteMatrixRequest>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((RouteMatrixRequest r, CancellationToken _) => new RouteMatrixResult
				{
					Successful = true,
					Provider = RoutingProviderNames.Mapbox,
					Durations = r.Origins.Select(_ => r.Destinations.Select(__ => (double?)240).ToArray()).ToArray(),
					Distances = r.Origins.Select(_ => r.Destinations.Select(__ => (double?)3000).ToArray()).ToArray()
				});
			var service = new RoutingService(new[] { valhalla.Object, mapbox.Object, new EstimateRoutingProvider() }, null, null, null);

			var matrix = await service.GetMatrixAsync(new RouteMatrixRequest
			{
				Origins = new List<RoutingWaypoint> { new RoutingWaypoint(39.53, -119.81), new RoutingWaypoint(39.60, -119.80) },
				Destinations = new List<RoutingWaypoint> { new RoutingWaypoint(39.54, -119.75) }
			});

			matrix.Successful.Should().BeTrue();
			matrix.Provider.Should().Be(RoutingProviderNames.Mapbox);
			matrix.Durations[0][0].Should().Be(240);
			matrix.Durations[1][0].Should().Be(240);
		}
	}

	/// <summary>
	/// Mapbox routing without a routing token borrows the web app's Mapbox map token (Directions, Matrix and Optimization
	/// accept a public token), with the install's web address as Referer for a URL-restricted token.
	/// </summary>
	[TestFixture]
	public class MapboxInstallTokenTests
	{
		private (string Routing, string WebsiteAccess, string WebsiteKey, string BaseUrl) _saved;

		[SetUp]
		public void SetUp()
		{
			_saved = (MappingConfig.MapboxRoutingAccessToken, MappingConfig.WebsiteMapboxAccessToken, MappingConfig.WebsiteMapboxKey, SystemBehaviorConfig.ResgridBaseUrl);
			MappingConfig.MapboxRoutingAccessToken = MappingConfig.WebsiteMapboxAccessToken = MappingConfig.WebsiteMapboxKey = string.Empty;
			SystemBehaviorConfig.ResgridBaseUrl = "https://app.example.org";
		}

		[TearDown]
		public void TearDown()
		{
			(MappingConfig.MapboxRoutingAccessToken, MappingConfig.WebsiteMapboxAccessToken, MappingConfig.WebsiteMapboxKey, SystemBehaviorConfig.ResgridBaseUrl) = _saved;
		}

		[Test]
		public void the_routing_token_wins_and_needs_no_referer()
		{
			MappingConfig.MapboxRoutingAccessToken = "sk.routing";
			MappingConfig.WebsiteMapboxAccessToken = "pk.website";

			var (token, referer) = MapboxRoutingProvider.InstallToken();

			token.Should().Be("sk.routing");
			referer.Should().BeNull();
		}

		[Test]
		public void without_a_routing_token_the_web_map_token_answers_with_the_install_address_as_referer()
		{
			MappingConfig.WebsiteMapboxAccessToken = " pk.website ";
			SystemBehaviorConfig.ResgridBaseUrl = "https://app.example.org/";

			var (token, referer) = MapboxRoutingProvider.InstallToken();

			token.Should().Be("pk.website");
			referer.Should().Be(new Uri("https://app.example.org/"));
			new MapboxRoutingProvider().IsConfigured.Should().BeTrue();
		}

		[Test]
		public void the_older_website_key_is_used_when_the_access_token_is_blank()
		{
			MappingConfig.WebsiteMapboxKey = "pk.older";

			MapboxRoutingProvider.InstallToken().Token.Should().Be("pk.older");
		}

		[Test]
		public void a_non_mapbox_website_key_is_never_sent_to_mapbox()
		{
			// WebsiteMapboxKey has held MapTiler keys on Leaflet installs; sending one to Mapbox only earns a 401 and a back-off.
			MappingConfig.WebsiteMapboxKey = "maptiler-abc123";

			MapboxRoutingProvider.InstallToken().Token.Should().BeNull();
			new MapboxRoutingProvider().IsConfigured.Should().BeFalse();
		}

		[Test]
		public void a_department_account_uses_only_its_own_key()
		{
			MappingConfig.WebsiteMapboxAccessToken = "pk.website";

			new MapboxRoutingProvider().WithCredentials(new RoutingProviderCredentials { Provider = RoutingProviderNames.Mapbox, ApiKey = "" })
				.IsConfigured.Should().BeFalse("a department's blank key never borrows the install's web token");
		}
	}

	/// <summary>
	/// Mapbox matrix requests carry at most 25 coordinates (10 on driving-traffic), origins and destinations together; a
	/// Valhalla fallback with that many destinations or more must still be split rather than refused (PR #587).
	/// </summary>
	[TestFixture]
	public class MapboxMatrixChunkTests
	{
		[TestCase(1, 1, 25)]
		[TestCase(30, 1, 25)]
		[TestCase(1, 24, 25)]
		[TestCase(1, 25, 25)]
		[TestCase(3, 40, 25)]
		[TestCase(40, 40, 25)]
		[TestCase(4, 10, 10)]
		[TestCase(12, 23, 10)]
		public void every_pair_is_covered_once_and_no_request_exceeds_the_limit(int origins, int destinations, int limit)
		{
			var chunks = MapboxRoutingProvider.MatrixChunks(origins, destinations, limit);

			chunks.Should().NotBeEmpty();
			chunks.Should().OnlyContain(c => c.OriginCount > 0 && c.DestinationCount > 0 && c.OriginCount + c.DestinationCount <= limit);

			var covered = new int[origins, destinations];
			foreach (var chunk in chunks)
				for (var i = chunk.OriginStart; i < chunk.OriginStart + chunk.OriginCount; i++)
					for (var j = chunk.DestinationStart; j < chunk.DestinationStart + chunk.DestinationCount; j++)
						covered[i, j]++;

			covered.Cast<int>().Should().OnlyContain(x => x == 1);
		}

		[Test]
		public void destinations_that_fit_beside_an_origin_stay_in_one_run()
		{
			var chunks = MapboxRoutingProvider.MatrixChunks(30, 5, 25);

			chunks.Should().HaveCount(2);
			chunks.Should().OnlyContain(c => c.DestinationStart == 0 && c.DestinationCount == 5);
		}

		[Test]
		public void the_driving_traffic_limit_is_lower()
		{
			MapboxRoutingProvider.MatrixCoordinateLimit("driving-traffic").Should().Be(10);
			MapboxRoutingProvider.MatrixCoordinateLimit("driving").Should().Be(25);
		}

		[Test]
		public void an_empty_side_needs_no_request()
		{
			MapboxRoutingProvider.MatrixChunks(0, 5, 25).Should().BeEmpty();
			MapboxRoutingProvider.MatrixChunks(5, 0, 25).Should().BeEmpty();
		}

		[TestCase(1, 25, 25, new[] { 13, 12 })]
		[TestCase(1, 10, 10, new[] { 5, 5 })]
		[TestCase(1, 49, 25, new[] { 17, 16, 16 })]
		public void a_split_is_evened_out_so_no_request_carries_a_lone_destination(int origins, int destinations, int limit, int[] runs)
		{
			var chunks = MapboxRoutingProvider.MatrixChunks(origins, destinations, limit);

			chunks.Select(c => c.DestinationCount).Should().Equal(runs);
		}

		[Test]
		public void a_response_code_other_than_ok_is_read_so_it_fails_the_matrix()
		{
			using var noRoute = System.Text.Json.JsonDocument.Parse("{\"code\":\"NoRoute\",\"message\":\"No route found\"}");
			using var ok = System.Text.Json.JsonDocument.Parse("{\"code\":\"Ok\",\"durations\":[[1]]}");
			using var none = System.Text.Json.JsonDocument.Parse("{\"durations\":[[1]]}");

			MapboxRoutingProvider.MatrixResponseCode(noRoute.RootElement).Should().Be("NoRoute");
			MapboxRoutingProvider.MatrixResponseCode(ok.RootElement).Should().Be("Ok");
			MapboxRoutingProvider.MatrixResponseCode(none.RootElement).Should().BeNull();
		}
	}

	[TestFixture]
	public class RouteOrderOptimizerTests
	{
		[Test]
		public void keeps_first_and_last_fixed_and_returns_a_permutation()
		{
			double?[][] costs =
			{
				new double?[] { 0, 10, 1, 9, 20 },
				new double?[] { 10, 0, 9, 1, 2 },
				new double?[] { 1, 9, 0, 8, 19 },
				new double?[] { 9, 1, 8, 0, 3 },
				new double?[] { 20, 2, 19, 3, 0 }
			};

			var order = RouteOrderOptimizer.Optimize(costs, true, true);

			order.First().Should().Be(0);
			order.Last().Should().Be(4);
			order.Should().BeEquivalentTo(new[] { 0, 1, 2, 3, 4 });
			order.Should().Equal(0, 2, 3, 1, 4);
		}

		[Test]
		public void unroutable_pairs_are_avoided()
		{
			double?[][] costs =
			{
				new double?[] { 0, null, 5, 50 },
				new double?[] { null, 0, 5, 5 },
				new double?[] { 5, 5, 0, 50 },
				new double?[] { 50, 5, 50, 0 }
			};

			var order = RouteOrderOptimizer.Optimize(costs, true, true);

			order.Should().Equal(0, 2, 1, 3);
		}
	}

	[TestFixture]
	public class PolylineCodecTests
	{
		[Test]
		public void decodes_googles_reference_polyline()
		{
			// The example from Google's encoded polyline algorithm documentation.
			var points = PolylineCodec.Decode("_p~iF~ps|U_ulLnnqC_mqNvxq`@", 5);

			points.Should().HaveCount(3);
			points[0].Latitude.Should().BeApproximately(38.5, 1e-6);
			points[0].Longitude.Should().BeApproximately(-120.2, 1e-6);
			points[2].Latitude.Should().BeApproximately(43.252, 1e-6);
			points[2].Longitude.Should().BeApproximately(-126.453, 1e-6);
		}

		[Test]
		public void round_trips_at_precision_six()
		{
			var original = new List<(double, double)> { (39.529600, -119.813800), (39.534912, -119.752701), (39.5, -119.75) };

			var decoded = PolylineCodec.Decode(PolylineCodec.Encode(original, 6), 6);

			decoded.Should().HaveCount(3);
			for (var i = 0; i < 3; i++)
			{
				decoded[i].Latitude.Should().BeApproximately(original[i].Item1, 1e-6);
				decoded[i].Longitude.Should().BeApproximately(original[i].Item2, 1e-6);
			}
		}

		[Test]
		public void geojson_line_is_longitude_first()
		{
			var json = PolylineCodec.ToGeoJsonLineString(new List<(double, double)> { (39.5, -119.8) });

			json.Should().Be("{\"type\":\"LineString\",\"coordinates\":[[-119.8,39.5]]}");
		}
	}

	[TestFixture]
	public class OsrmShapeParserTests
	{
		[Test]
		public void reads_an_osrm_shaped_route_with_legs_incidents_and_typical_duration()
		{
			var geometry = PolylineCodec.Encode(new List<(double, double)> { (39.50, -119.80), (39.51, -119.79), (39.52, -119.78) }, 6);
			var json = "{\"code\":\"Ok\",\"routes\":[{\"distance\":2500.5,\"duration\":300.2,\"duration_typical\":250," +
			           "\"geometry\":\"" + geometry.Replace("\\", "\\\\") + "\"," +
			           "\"legs\":[{\"distance\":2500.5,\"duration\":300.2,\"steps\":[]," +
			           "\"incidents\":[{\"type\":\"accident\",\"description\":\"Crash\",\"impact\":\"major\",\"closed\":true,\"geometry_index_start\":1}]}]}]}";

			using var document = System.Text.Json.JsonDocument.Parse(json);
			var route = OsrmShapeParser.Parse(document, "mapbox", "© Mapbox", true);

			route.Successful.Should().BeTrue();
			route.DistanceMeters.Should().Be(2500.5);
			route.DurationSeconds.Should().Be(300.2);
			route.TypicalDurationSeconds.Should().Be(250);
			route.Legs.Should().HaveCount(1);
			route.GeometryGeoJson.Should().StartWith("{\"type\":\"LineString\"");
			route.NormalizedJson.Should().Contain("\"legs\"");
			route.Incidents.Should().ContainSingle();
			route.Incidents[0].Type.Should().Be("accident");
			route.Incidents[0].Closed.Should().BeTrue();
			route.Incidents[0].Latitude.Should().BeApproximately(39.51, 1e-6);
		}

		[Test]
		public void reports_a_provider_error_code()
		{
			using var document = System.Text.Json.JsonDocument.Parse("{\"code\":\"NoRoute\",\"message\":\"Impossible route\"}");

			var route = OsrmShapeParser.Parse(document, "osrm", null, false);

			route.Successful.Should().BeFalse();
			route.Error.Should().Contain("NoRoute");
		}

		[Test]
		public void reads_trip_order_from_waypoint_indexes()
		{
			using var document = System.Text.Json.JsonDocument.Parse("{\"waypoints\":[{\"waypoint_index\":0},{\"waypoint_index\":2},{\"waypoint_index\":1},{\"waypoint_index\":3}]}");

			OsrmShapeParser.ReadTripOrder(document.RootElement).Should().Equal(0, 2, 1, 3);
		}
	}

	[TestFixture]
	public class VehicleProfileTests
	{
		[Test]
		public void unit_values_override_the_type_field_by_field()
		{
			var type = new UnitType { VehicleHeightMeters = 3.6, VehicleGrossWeightKg = 20000, VehicleAxleCount = 3 };
			var unit = new Unit { VehicleHeightMeters = 3.9 };

			var profile = VehicleProfile.Resolve(unit, type);

			profile.HeightMeters.Should().Be(3.9);
			profile.GrossWeightKg.Should().Be(20000);
			profile.AxleCount.Should().Be(3);
			profile.WidthMeters.Should().BeNull();
		}

		[Test]
		public void no_limits_resolves_to_null()
		{
			VehicleProfile.Resolve(new Unit(), new UnitType()).Should().BeNull();
		}

		[Test]
		public void key_is_stable_and_culture_invariant()
		{
			var profile = new VehicleProfile { HeightMeters = 3.95, Hazmat = true };

			profile.Key().Should().Be("3.95||||||h");
		}
	}
}
