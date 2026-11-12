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
using Resgrid.Providers.GeoLocationProvider.Routing;
using Resgrid.Services;

namespace Resgrid.Tests.Services.Routing
{
	/// <summary>
	/// How routed trips meet road closures (M0314): full closures handed to the provider to avoid, partial ones (and a full
	/// one the trip starts or ends in) slowed through, and every provider's time adjusted by measuring the route line.
	/// </summary>
	[TestFixture]
	public class RoadClosureRoutingTests
	{
		private const string BlockPolygon = "{\"type\":\"Polygon\",\"coordinates\":[[[3.715,51.049],[3.725,51.049],[3.725,51.051],[3.715,51.051],[3.715,51.049]]]}";
		private const string AlongRouteLine = "{\"type\":\"LineString\",\"coordinates\":[[3.715,51.05],[3.725,51.05]]}";
		private const string StraightThrough = "{\"type\":\"LineString\",\"coordinates\":[[3.70,51.05],[3.74,51.05]]}";
		private const string AroundTheNorth = "{\"type\":\"LineString\",\"coordinates\":[[3.70,51.05],[3.70,51.06],[3.74,51.06],[3.74,51.05]]}";

		private static readonly RoutingCapabilities Valhalla = new RoutingCapabilities { ExcludeAreas = true, MaxExcludePoints = 200, MaxWaypoints = 25 };
		private static readonly RoutingCapabilities Mapbox = new RoutingCapabilities { MaxExcludePoints = 50, ExcludePointsDrivingOnly = true, MaxWaypoints = 25 };
		private static readonly RoutingCapabilities Google = new RoutingCapabilities { MaxWaypoints = 25 };

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
			MappingConfig.RoutingFallbackProvider = RoutingProviderNames.Estimate;
		}

		[TearDown]
		public void TearDown()
		{
			MappingConfig.RoutingProvider = _provider;
			MappingConfig.RoutingFallbackProvider = _fallback;
			SystemBehaviorConfig.CacheEnabled = _cacheEnabled;
		}

		private static RoutingClosure Closure(string id, string geoJson, RoadClosureSeverities severity, int speedPercent = 50, int detourMinutes = 5)
		{
			return new RoutingClosure
			{
				ClosureId = id,
				Name = "Closure " + id,
				Severity = severity,
				SpeedPercent = speedPercent,
				DetourSeconds = detourMinutes * 60,
				Shape = RoadClosureShape.ParseOrNull(geoJson),
				VersionKey = id + "@1"
			};
		}

		private static RouteRequest WestToEast(params RoutingClosure[] closures)
		{
			return new RouteRequest
			{
				Waypoints = new List<RoutingWaypoint> { new RoutingWaypoint(51.05, 3.70), new RoutingWaypoint(51.05, 3.74) },
				Profile = RoutingProfile.Driving,
				Closures = closures.ToList()
			};
		}

		private static RouteResult Route(string geometry, double meters, double seconds)
		{
			return new RouteResult { Successful = true, GeometryGeoJson = geometry, DistanceMeters = meters, DurationSeconds = seconds };
		}

		#region Plan

		[Test]
		public void full_closures_on_the_way_are_avoided_and_valhalla_gets_their_areas_and_road_points()
		{
			var plan = RoadClosureRouting.Plan(WestToEast(Closure("area", BlockPolygon, RoadClosureSeverities.Full), Closure("road", AlongRouteLine, RoadClosureSeverities.Full)), Valhalla, false);

			plan.Avoid.Select(c => c.ClosureId).Should().BeEquivalentTo(new[] { "area", "road" });
			plan.Slow.Should().BeEmpty();
			plan.ProviderRequest.ExcludeAreas.Should().ContainSingle().Which.Should().HaveCount(4);
			plan.ProviderRequest.ExcludePoints.Should().NotBeEmpty("the closed road line becomes points on it");
			plan.Key.Should().Contain("a:area@1").And.Contain("a:road@1");
		}

		[Test]
		public void a_trip_that_ends_inside_a_full_closure_is_slowed_through_it_not_sent_around()
		{
			var request = WestToEast(Closure("area", BlockPolygon, RoadClosureSeverities.Full));
			request.Waypoints[1] = new RoutingWaypoint(51.05, 3.72);

			var plan = RoadClosureRouting.Plan(request, Valhalla, false);

			plan.Avoid.Should().BeEmpty();
			plan.Slow.Should().ContainSingle(c => c.ClosureId == "area");
			plan.ProviderRequest.ExcludeAreas.Should().BeEmpty();
		}

		[Test]
		public void partial_closures_are_never_avoided_and_far_away_closures_are_left_out()
		{
			var far = "{\"type\":\"Polygon\",\"coordinates\":[[[5.0,52.0],[5.01,52.0],[5.01,52.01],[5.0,52.0]]]}";
			var plan = RoadClosureRouting.Plan(WestToEast(Closure("slow", BlockPolygon, RoadClosureSeverities.Partial), Closure("far", far, RoadClosureSeverities.Full)), Valhalla, false);

			plan.Avoid.Should().BeEmpty();
			plan.Slow.Should().ContainSingle(c => c.ClosureId == "slow");
			plan.ProviderRequest.Should().NotBeNull();
			plan.ProviderRequest.ExcludeAreas.Should().BeEmpty();
		}

		[Test]
		public void mapbox_point_exclusions_are_for_driving_only()
		{
			var walking = WestToEast(Closure("road", AlongRouteLine, RoadClosureSeverities.Full));
			walking.Profile = RoutingProfile.Walking;

			RoadClosureRouting.Plan(walking, Mapbox, false).ProviderRequest.ExcludePoints.Should().BeEmpty();
			RoadClosureRouting.Plan(WestToEast(Closure("road", AlongRouteLine, RoadClosureSeverities.Full)), Mapbox, false).ProviderRequest.ExcludePoints
				.Should().NotBeEmpty().And.HaveCountLessThanOrEqualTo(50);
		}

		[Test]
		public void the_estimate_provider_ignores_closures()
		{
			var plan = RoadClosureRouting.Plan(WestToEast(Closure("area", BlockPolygon, RoadClosureSeverities.Full)), Valhalla, true);

			plan.HasClosures.Should().BeFalse();
			plan.Key.Should().BeEmpty();
		}

		#endregion

		#region Apply

		[Test]
		public void a_partial_closure_slows_the_time_spent_inside_it()
		{
			// 2,800 m in 280 s; about 700 m (70 s) inside at half speed adds about 70 s.
			var plan = RoadClosureRouting.Plan(WestToEast(Closure("slow", BlockPolygon, RoadClosureSeverities.Partial, speedPercent: 50)), Valhalla, false);
			var route = Route(StraightThrough, 2800, 280);

			RoadClosureRouting.Apply(route, plan);

			route.ClosureDelaySeconds.Should().BeInRange(65, 75);
			route.DurationSeconds.Should().Be(280 + route.ClosureDelaySeconds);
			route.Closures.Should().ContainSingle(c => c.ClosureId == "slow" && c.Effect == RouteClosureEffects.Slowed);
		}

		[Test]
		public void a_full_closure_the_route_still_crosses_adds_its_detour_and_says_so()
		{
			var plan = RoadClosureRouting.Plan(WestToEast(Closure("area", BlockPolygon, RoadClosureSeverities.Full, detourMinutes: 4)), Google, false);
			var route = Route(StraightThrough, 2800, 280);

			RoadClosureRouting.Apply(route, plan);

			route.ClosureDelaySeconds.Should().Be(240);
			route.DurationSeconds.Should().Be(520);
			route.Closures.Should().ContainSingle(c => c.Effect == RouteClosureEffects.Crossed && c.DelaySeconds == 240);
		}

		[Test]
		public void a_full_closure_the_route_went_around_adds_nothing_and_is_listed_as_avoided()
		{
			var plan = RoadClosureRouting.Plan(WestToEast(Closure("area", BlockPolygon, RoadClosureSeverities.Full)), Valhalla, false);
			var route = Route(AroundTheNorth, 4900, 490);

			RoadClosureRouting.Apply(route, plan);

			route.ClosureDelaySeconds.Should().Be(0);
			route.DurationSeconds.Should().Be(490);
			route.Closures.Should().ContainSingle(c => c.Effect == RouteClosureEffects.Avoided);
		}

		#endregion

		#region Routing service

		private static Mock<IRoutingProvider> Provider(string name, RoutingCapabilities capabilities)
		{
			var provider = new Mock<IRoutingProvider>();
			provider.SetupGet(p => p.Name).Returns(name);
			provider.SetupGet(p => p.IsConfigured).Returns(true);
			provider.SetupGet(p => p.Capabilities).Returns(capabilities);
			return provider;
		}

		[Test]
		public async Task a_point_excluding_provider_is_sent_back_around_the_closure_it_drove_into()
		{
			MappingConfig.RoutingProvider = RoutingProviderNames.Mapbox;
			var mapbox = Provider(RoutingProviderNames.Mapbox, new RoutingCapabilities { MaxExcludePoints = 50, ExcludePointsDrivingOnly = true, MaxWaypoints = 25 });
			var requests = new List<RouteRequest>();
			mapbox.Setup(p => p.GetRouteAsync(It.IsAny<RouteRequest>(), It.IsAny<CancellationToken>()))
				.Callback((RouteRequest r, CancellationToken _) => requests.Add(r))
				.ReturnsAsync((RouteRequest r, CancellationToken _) => r.ExcludePoints.Count == 0 ? Route(StraightThrough, 2800, 280) : Route(AroundTheNorth, 4900, 490));

			var route = await new RoutingService(new[] { mapbox.Object, new EstimateRoutingProvider() }, null)
				.GetRouteAsync(WestToEast(Closure("area", BlockPolygon, RoadClosureSeverities.Full)));

			requests.Should().HaveCount(2);
			requests[1].ExcludePoints.Should().ContainSingle(p => p.Longitude > 3.714 && p.Longitude < 3.717, "where the first route went into the area");
			route.DurationSeconds.Should().Be(490);
			route.Closures.Should().ContainSingle(c => c.Effect == RouteClosureEffects.Avoided);
		}

		[Test]
		public async Task a_provider_that_cannot_avoid_is_charged_the_detour()
		{
			MappingConfig.RoutingProvider = RoutingProviderNames.Google;
			var google = Provider(RoutingProviderNames.Google, new RoutingCapabilities { MaxWaypoints = 25 });
			google.Setup(p => p.GetRouteAsync(It.IsAny<RouteRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => Route(StraightThrough, 2800, 280));

			var route = await new RoutingService(new[] { google.Object, new EstimateRoutingProvider() }, null)
				.GetRouteAsync(WestToEast(Closure("area", BlockPolygon, RoadClosureSeverities.Full, detourMinutes: 5)));

			google.Verify(p => p.GetRouteAsync(It.IsAny<RouteRequest>(), It.IsAny<CancellationToken>()), Times.Once);
			route.DurationSeconds.Should().Be(580);
			route.ClosureDelaySeconds.Should().Be(300);
			route.Closures.Should().ContainSingle(c => c.Effect == RouteClosureEffects.Crossed);
		}

		[Test]
		public async Task valhalla_is_sent_the_area_to_exclude()
		{
			MappingConfig.RoutingProvider = RoutingProviderNames.Valhalla;
			var valhalla = Provider(RoutingProviderNames.Valhalla, new RoutingCapabilities { ExcludeAreas = true, MaxExcludePoints = 200, MaxWaypoints = 25 });
			RouteRequest sent = null;
			valhalla.Setup(p => p.GetRouteAsync(It.IsAny<RouteRequest>(), It.IsAny<CancellationToken>()))
				.Callback((RouteRequest r, CancellationToken _) => sent = r)
				.ReturnsAsync(() => Route(AroundTheNorth, 4900, 490));

			var route = await new RoutingService(new[] { valhalla.Object, new EstimateRoutingProvider() }, null)
				.GetRouteAsync(WestToEast(Closure("area", BlockPolygon, RoadClosureSeverities.Full)));

			sent.ExcludeAreas.Should().ContainSingle();
			route.DurationSeconds.Should().Be(490);
		}

		[Test]
		public void valhalla_request_body_carries_polygons_and_locations()
		{
			var body = new Dictionary<string, object>();
			var request = new RouteRequest
			{
				ExcludeAreas = new List<List<RoutingWaypoint>> { new List<RoutingWaypoint> { new RoutingWaypoint(51.049, 3.715), new RoutingWaypoint(51.049, 3.725), new RoutingWaypoint(51.051, 3.725) } },
				ExcludePoints = new List<RoutingWaypoint> { new RoutingWaypoint(51.05, 3.72) }
			};

			ValhallaRoutingProvider.AddExclusions(body, request);

			var rings = (List<List<double[]>>)body["exclude_polygons"];
			rings.Should().ContainSingle().Which.Should().HaveCount(4, "the ring is closed");
			rings[0][0].Should().Equal(3.715, 51.049);
			((IEnumerable<Dictionary<string, object>>)body["exclude_locations"]).Should().ContainSingle();
		}

		[Test]
		public void mapbox_query_carries_the_exclude_points()
		{
			var query = new System.Text.StringBuilder();

			MapboxRoutingProvider.AppendExclude(query, RoutingProfile.Driving, RouteAvoidOptions.Tolls, new List<RoutingWaypoint> { new RoutingWaypoint(51.05, 3.72) });

			query.ToString().Should().Be("&exclude=point(3.72%2051.05),toll");
		}

		#endregion
	}
}
