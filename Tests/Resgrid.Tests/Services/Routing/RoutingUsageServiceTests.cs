using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Providers.GeoLocationProvider.Routing;
using Resgrid.Services;

namespace Resgrid.Tests.Services.Routing
{
	/// <summary>
	/// Per-provider routing call counters: what reached each provider, what it refused, matrix elements for the Mapbox
	/// bill, and the price table the console report applies.
	/// </summary>
	[TestFixture]
	public class RoutingUsageServiceTests
	{
		private ConcurrentDictionary<string, long> _counters;
		private Mock<ICacheProvider> _cache;
		private RoutingUsageService _service;
		private bool _cacheEnabled;

		[SetUp]
		public void SetUp()
		{
			_cacheEnabled = SystemBehaviorConfig.CacheEnabled;
			SystemBehaviorConfig.CacheEnabled = true;

			_counters = new ConcurrentDictionary<string, long>();
			_cache = new Mock<ICacheProvider>();
			_cache.Setup(c => c.IsConnected()).Returns(true);
			_cache.Setup(c => c.IncrementByAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<TimeSpan>()))
				.ReturnsAsync((string key, long amount, TimeSpan _) => _counters.AddOrUpdate(key, amount, (_, value) => value + amount));
			_cache.Setup(c => c.GetStringAsync(It.IsAny<string>()))
				.ReturnsAsync((string key) => _counters.TryGetValue(key, out var value) ? value.ToString() : null);
			_service = new RoutingUsageService(_cache.Object);
		}

		[TearDown]
		public void TearDown()
		{
			SystemBehaviorConfig.CacheEnabled = _cacheEnabled;
		}

		[Test]
		public async Task an_unreachable_cache_fails_the_report_instead_of_showing_no_calls()
		{
			// PR 585: Redis returns null for every key while disconnected, which read as a month of zero calls and a $0 bill.
			await _service.RecordAsync(RoutingProviderNames.Mapbox, RoutingUsageOperation.Route, RoutingUsageOutcome.Succeeded);
			_cache.Setup(c => c.IsConnected()).Returns(false);

			Func<Task> read = () => _service.GetUsageAsync(DateTime.UtcNow.Date, DateTime.UtcNow.Date);

			await read.Should().ThrowAsync<InvalidOperationException>();
		}

		[Test]
		public async Task a_report_reads_every_counter_once_and_stops_when_cancelled()
		{
			var today = DateTime.UtcNow.Date;
			await _service.RecordAsync(RoutingProviderNames.Valhalla, RoutingUsageOperation.Route, RoutingUsageOutcome.Succeeded);

			(await _service.GetUsageAsync(today.AddDays(-2), today)).Should().ContainSingle();
			_cache.Verify(c => c.GetStringAsync(It.IsAny<string>()), Times.Exactly(3 * 4 * 3 * 2 * 5), "every counter of the three days, read once");

			using var cancelled = new CancellationTokenSource();
			cancelled.Cancel();
			Func<Task> read = () => _service.GetUsageAsync(today.AddDays(-400), today, cancelled.Token);
			await read.Should().ThrowAsync<OperationCanceledException>();
		}

		[Test]
		public async Task counts_outcomes_and_matrix_elements_per_provider()
		{
			await _service.RecordAsync(RoutingProviderNames.Valhalla, RoutingUsageOperation.Route, RoutingUsageOutcome.Succeeded);
			await _service.RecordAsync(RoutingProviderNames.Valhalla, RoutingUsageOperation.Route, RoutingUsageOutcome.Rejected);
			await _service.RecordAsync(RoutingProviderNames.Valhalla, RoutingUsageOperation.Route, RoutingUsageOutcome.NotSent);
			await _service.RecordAsync(RoutingProviderNames.Mapbox, RoutingUsageOperation.Matrix, RoutingUsageOutcome.Succeeded, 40);
			await _service.RecordAsync(RoutingProviderNames.Mapbox, RoutingUsageOperation.Matrix, RoutingUsageOutcome.Failed, 10);

			var today = DateTime.UtcNow.Date;
			var rows = await _service.GetUsageAsync(today, today);

			var valhalla = rows.Single(r => r.Provider == RoutingProviderNames.Valhalla);
			valhalla.Operation.Should().Be(RoutingUsageOperation.Route);
			valhalla.Succeeded.Should().Be(1);
			valhalla.Rejected.Should().Be(1);
			valhalla.NotSent.Should().Be(1);
			valhalla.Sent.Should().Be(2, "a call answered inside Resgrid never reached the provider");
			valhalla.BillableUnits.Should().Be(2);

			var mapbox = rows.Single(r => r.Provider == RoutingProviderNames.Mapbox);
			mapbox.Operation.Should().Be(RoutingUsageOperation.Matrix);
			mapbox.Sent.Should().Be(2);
			mapbox.Elements.Should().Be(50);
			mapbox.BillableUnits.Should().Be(50, "Mapbox bills a matrix per origin-destination pair");
		}

		[Test]
		public async Task elements_of_a_matrix_that_was_not_sent_are_not_counted()
		{
			await _service.RecordAsync(RoutingProviderNames.Valhalla, RoutingUsageOperation.Matrix, RoutingUsageOutcome.NotSent, 25);

			var today = DateTime.UtcNow.Date;
			var row = (await _service.GetUsageAsync(today, today)).Single();

			row.NotSent.Should().Be(1);
			row.Elements.Should().Be(0);
		}

		[TestCase(RoutingProviderNames.Estimate)]
		[TestCase("")]
		[TestCase(null)]
		public async Task the_estimate_is_not_counted(string provider)
		{
			await _service.RecordAsync(provider, RoutingUsageOperation.Route, RoutingUsageOutcome.Succeeded);

			_counters.Should().BeEmpty();
		}

		[Test]
		public async Task days_without_calls_are_left_out()
		{
			await _service.RecordAsync(RoutingProviderNames.Google, RoutingUsageOperation.Optimize, RoutingUsageOutcome.Succeeded);

			var today = DateTime.UtcNow.Date;
			var rows = await _service.GetUsageAsync(today.AddDays(-6), today);

			rows.Should().ContainSingle().Which.Day.Should().Be(today);
		}

		[Test]
		public async Task a_cache_failure_never_breaks_routing()
		{
			_cache.Setup(c => c.IncrementByAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<TimeSpan>())).ThrowsAsync(new InvalidOperationException("redis down"));

			Func<Task> record = () => _service.RecordAsync(RoutingProviderNames.Mapbox, RoutingUsageOperation.Route, RoutingUsageOutcome.Succeeded);

			await record.Should().NotThrowAsync();
		}

		[TestCase(0, 0)]
		[TestCase(100_000, 0)]
		[TestCase(100_500, 1)]
		[TestCase(250_000, 300)]
		[TestCase(500_000, 800)]
		[TestCase(1_000_000, 1600)]
		[TestCase(2_000_000, 2800)]
		public void mapbox_monthly_cost_follows_the_price_tiers(long units, decimal dollars)
		{
			MapboxRoutingPricing.MonthlyCost(units).Should().Be(dollars);
		}
	}

	/// <summary>RoutingService counts each provider call it makes, by outcome, and never the estimate.</summary>
	[TestFixture]
	public class RoutingServiceUsageTests
	{
		private string _provider;
		private string _fallback;
		private bool _cacheEnabled;

		[SetUp]
		public void SetUp()
		{
			_provider = MappingConfig.RoutingProvider;
			_fallback = MappingConfig.RoutingFallbackProvider;
			_cacheEnabled = SystemBehaviorConfig.CacheEnabled;
			SystemBehaviorConfig.CacheEnabled = false;
			MappingConfig.RoutingProvider = RoutingProviderNames.Valhalla;
			MappingConfig.RoutingFallbackProvider = RoutingProviderNames.Estimate;
		}

		[TearDown]
		public void TearDown()
		{
			MappingConfig.RoutingProvider = _provider;
			MappingConfig.RoutingFallbackProvider = _fallback;
			SystemBehaviorConfig.CacheEnabled = _cacheEnabled;
		}

		private static Mock<IRoutingProvider> Valhalla()
		{
			var provider = new Mock<IRoutingProvider>();
			provider.SetupGet(p => p.Name).Returns(RoutingProviderNames.Valhalla);
			provider.SetupGet(p => p.IsConfigured).Returns(true);
			provider.SetupGet(p => p.Capabilities).Returns(new RoutingCapabilities { Steps = true, Matrix = true, MaxWaypoints = 25, MaxMatrixCoordinates = 25 });
			return provider;
		}

		[Test]
		public async Task a_rejected_route_is_counted_as_rejected_and_the_estimate_is_not_counted()
		{
			var valhalla = Valhalla();
			valhalla.Setup(p => p.GetRouteAsync(It.IsAny<RouteRequest>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new RouteResult { Successful = false, Error = "400: NoSegment", RequestRejected = true });
			var usage = new Mock<IRoutingUsageService>();
			var service = new RoutingService(new[] { valhalla.Object, new EstimateRoutingProvider() }, null, usage.Object, null);

			var route = await service.GetRouteAsync(new RouteRequest
			{
				Waypoints = new List<RoutingWaypoint> { new RoutingWaypoint(39.53, -119.81), new RoutingWaypoint(39.54, -119.75) }
			});

			route.Provider.Should().Be(RoutingProviderNames.Estimate);
			usage.Verify(u => u.RecordAsync(RoutingProviderNames.Valhalla, RoutingUsageOperation.Route, RoutingUsageOutcome.Rejected, 0, false), Times.Once);
			usage.Verify(u => u.RecordAsync(RoutingProviderNames.Estimate, It.IsAny<RoutingUsageOperation>(), It.IsAny<RoutingUsageOutcome>(), It.IsAny<int>(), It.IsAny<bool>()), Times.Never);
		}

		[Test]
		public async Task a_coverage_skip_is_counted_as_not_sent()
		{
			var valhalla = Valhalla();
			valhalla.Setup(p => p.GetRouteAsync(It.IsAny<RouteRequest>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new RouteResult { Successful = false, Error = "Outside MappingConfig.ValhallaCoverage", RequestRejected = true, NotSent = true });
			var usage = new Mock<IRoutingUsageService>();
			var service = new RoutingService(new[] { valhalla.Object, new EstimateRoutingProvider() }, null, usage.Object, null);

			await service.GetRouteAsync(new RouteRequest
			{
				Waypoints = new List<RoutingWaypoint> { new RoutingWaypoint(-33.87, 151.21), new RoutingWaypoint(-33.88, 151.20) }
			});

			usage.Verify(u => u.RecordAsync(RoutingProviderNames.Valhalla, RoutingUsageOperation.Route, RoutingUsageOutcome.NotSent, 0, false), Times.Once);
		}

		[Test]
		public async Task a_matrix_counts_its_origin_destination_pairs()
		{
			var valhalla = Valhalla();
			valhalla.Setup(p => p.GetMatrixAsync(It.IsAny<RouteMatrixRequest>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((RouteMatrixRequest request, CancellationToken _) => new RouteMatrixResult
				{
					Successful = true,
					Provider = RoutingProviderNames.Valhalla,
					Durations = request.Origins.Select(_ => request.Destinations.Select(__ => (double?)60).ToArray()).ToArray(),
					Distances = request.Origins.Select(_ => request.Destinations.Select(__ => (double?)1000).ToArray()).ToArray()
				});
			var usage = new Mock<IRoutingUsageService>();
			var service = new RoutingService(new[] { valhalla.Object, new EstimateRoutingProvider() }, null, usage.Object, null);

			var matrix = await service.GetMatrixAsync(new RouteMatrixRequest
			{
				Origins = new List<RoutingWaypoint> { new RoutingWaypoint(39.53, -119.81), new RoutingWaypoint(39.60, -119.80), new RoutingWaypoint(39.70, -119.70) },
				Destinations = new List<RoutingWaypoint> { new RoutingWaypoint(39.54, -119.75), new RoutingWaypoint(39.50, -119.70) }
			});

			matrix.Provider.Should().Be(RoutingProviderNames.Valhalla);
			usage.Verify(u => u.RecordAsync(RoutingProviderNames.Valhalla, RoutingUsageOperation.Matrix, RoutingUsageOutcome.Succeeded, 6, false), Times.Once);
		}
	}
}
