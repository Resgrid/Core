using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services.Routing
{
	/// <summary>Server-side route plan routing (maplibre-mapping-routing-navigation-plan.md §10.4).</summary>
	[TestFixture]
	public class RoutePlanRoutingServiceTests
	{
		private Mock<IRoutingService> _routing;
		private Mock<IRouteService> _routes;
		private Mock<IDepartmentGroupsService> _groups;
		private Mock<IUnitsService> _units;
		private Mock<IGeoService> _geo;
		private RoutePlanRoutingService _service;

		private RoutePlan _plan;
		private List<RouteStop> _stops;
		private RoutePlan _saved;
		private List<RouteRequest> _requests;

		[SetUp]
		public void SetUp()
		{
			_routing = new Mock<IRoutingService>();
			_routes = new Mock<IRouteService>();
			_groups = new Mock<IDepartmentGroupsService>();
			_units = new Mock<IUnitsService>();
			_geo = new Mock<IGeoService>();
			_requests = new List<RouteRequest>();
			_saved = null;

			_routing.SetupGet(r => r.ProviderName).Returns(RoutingProviderNames.Valhalla);
			_routing.SetupGet(r => r.Capabilities).Returns(new RoutingCapabilities { Steps = true, VehicleSize = true, VehicleFull = true });
			_routing.SetupGet(r => r.Attribution).Returns("© OpenStreetMap contributors");
			_routing.Setup(r => r.GetRouteAsync(It.IsAny<RouteRequest>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((RouteRequest request, CancellationToken _) =>
				{
					_requests.Add(request);
					return Route(request, RoutingProviderNames.Valhalla);
				});

			_plan = new RoutePlan
			{
				RoutePlanId = "plan",
				DepartmentId = 7,
				UnitId = 30,
				UseStationAsStart = true,
				UseStationAsEnd = false,
				EndLatitude = 39.60m,
				EndLongitude = -119.60m,
				RoutingProfile = (int)RoutingProfile.Driving
			};
			_stops = new List<RouteStop>
			{
				new RouteStop { RouteStopId = "a", RoutePlanId = "plan", StopOrder = 1, Name = "A", Latitude = 39.51m, Longitude = -119.81m, EstimatedDwellMinutes = 5 },
				new RouteStop { RouteStopId = "b", RoutePlanId = "plan", StopOrder = 2, Name = "B", Latitude = 39.52m, Longitude = -119.82m, EstimatedDwellMinutes = 10 }
			};

			_routes.Setup(r => r.GetRoutePlanByIdAsync("plan")).ReturnsAsync(() => _plan);
			_routes.Setup(r => r.GetRouteStopsForPlanAsync("plan")).ReturnsAsync(() => _stops.OrderBy(s => s.StopOrder).ToList());
			_routes.Setup(r => r.SaveGeneratedRouteAsync(It.IsAny<RoutePlan>(), It.IsAny<IEnumerable<RouteStop>>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((RoutePlan plan, IEnumerable<RouteStop> _, CancellationToken __) => _saved = plan);

			_units.Setup(u => u.GetUnitByIdAsync(30)).ReturnsAsync(new Unit { UnitId = 30, DepartmentId = 7, StationGroupId = 4 });
			_groups.Setup(g => g.GetGroupByIdAsync(4, false)).ReturnsAsync(new DepartmentGroup { DepartmentGroupId = 4, DepartmentId = 7, Name = "Station 4" });
			_geo.Setup(g => g.GetStationCoordinatesAsync(It.Is<DepartmentGroup>(d => d.DepartmentGroupId == 4))).ReturnsAsync(new GeoMath.GeoPoint(39.50, -119.80));

			_service = new RoutePlanRoutingService(_routing.Object, _routes.Object, _groups.Object, _units.Object, _geo.Object);
		}

		/// <summary>One leg per consecutive pair: 600 s and 1000 m each, straight-line geometry through the points.</summary>
		private static RouteResult Route(RouteRequest request, string provider)
		{
			var legs = Enumerable.Range(0, request.Waypoints.Count - 1).Select(_ => new RouteLegSummary { DistanceMeters = 1000, DurationSeconds = 600 }).ToList();
			var coordinates = string.Join(",", request.Waypoints.Select(w => $"[{w.Longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)},{w.Latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}]"));

			return new RouteResult
			{
				Successful = true,
				Provider = provider,
				DistanceMeters = legs.Sum(l => l.DistanceMeters),
				DurationSeconds = legs.Sum(l => l.DurationSeconds),
				Legs = legs,
				GeometryGeoJson = "{\"type\":\"LineString\",\"coordinates\":[" + coordinates + "]}",
				NormalizedJson = "{\"distance\":" + legs.Sum(l => l.DistanceMeters) + ",\"duration\":" + legs.Sum(l => l.DurationSeconds) + ",\"geometry\":\"\",\"legs\":[" +
					string.Join(",", legs.Select(_ => "{\"steps\":[]}")) + "]}"
			};
		}

		[Test]
		public async Task generate_routes_from_the_unit_station_through_the_stops_to_the_end_and_stores_it()
		{
			var result = await _service.GenerateAsync("plan");

			result.Successful.Should().BeTrue();
			result.Stored.Should().BeTrue();
			_requests.Should().HaveCount(1);
			_requests[0].Waypoints.Should().HaveCount(4);
			_requests[0].Waypoints[0].Latitude.Should().Be(39.50);
			_requests[0].Waypoints[3].Latitude.Should().Be(39.60);
			_requests[0].Steps.Should().BeTrue();

			_saved.Should().NotBeNull();
			_saved.MapboxRouteGeometry.Should().StartWith("{\"type\":\"LineString\"");
			_saved.EstimatedDistanceMeters.Should().Be(3000);
			_saved.EstimatedDurationSeconds.Should().Be(1800);
			_saved.RouteProvider.Should().Be(RoutingProviderNames.Valhalla);
			_saved.RouteIsStale.Should().BeFalse();
			_saved.RouteInputHash.Should().HaveLength(64);
			_saved.RouteDirectionsJson.Should().NotBeNullOrWhiteSpace();

			// Planned offsets: legs plus the dwell of every stop already passed.
			_stops[0].LegDurationSeconds.Should().Be(600);
			_stops[0].PlannedOffsetSeconds.Should().Be(600);
			_stops[1].PlannedOffsetSeconds.Should().Be(600 + 5 * 60 + 600);
			result.DurationWithDwellSeconds.Should().Be(1800 + 15 * 60);
		}

		[Test]
		public async Task the_legacy_profile_string_is_never_rewritten()
		{
			_plan.MapboxRouteProfile = "driving-traffic";
			_plan.RoutingProfile = (int)RoutingProfile.DrivingTraffic;

			await _service.GenerateAsync("plan");

			_saved.MapboxRouteProfile.Should().Be("driving-traffic");
			_requests[0].Profile.Should().Be(RoutingProfile.DrivingTraffic);
		}

		[Test]
		public async Task the_unit_vehicle_limits_apply_and_truck_without_limits_drives_as_a_car()
		{
			var vehicle = new VehicleProfile { HeightMeters = 3.8 };
			_units.Setup(u => u.GetUnitVehicleProfileAsync(It.Is<Unit>(x => x.UnitId == 30))).ReturnsAsync(vehicle);

			await _service.GenerateAsync("plan");
			_requests.Last().Vehicle.Should().BeSameAs(vehicle);

			_units.Setup(u => u.GetUnitVehicleProfileAsync(It.IsAny<Unit>())).ReturnsAsync((VehicleProfile)null);
			_plan.RoutingProfile = (int)RoutingProfile.Truck;
			_plan.RouteIsStale = true;

			await _service.GenerateAsync("plan");
			_requests.Last().Profile.Should().Be(RoutingProfile.Driving);
			_requests.Last().Vehicle.Should().BeNull();
		}

		[Test]
		public async Task an_estimate_is_returned_but_never_stored()
		{
			_routing.Setup(r => r.GetRouteAsync(It.IsAny<RouteRequest>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((RouteRequest request, CancellationToken _) => Route(request, RoutingProviderNames.Estimate));

			var result = await _service.GenerateAsync("plan");

			result.Successful.Should().BeTrue();
			result.IsEstimate.Should().BeTrue();
			result.Stored.Should().BeFalse();
			_saved.Should().BeNull();
		}

		[Test]
		public async Task a_current_stored_route_is_served_without_routing_again()
		{
			await _service.GenerateAsync("plan");
			_requests.Clear();

			var current = await _service.GetCurrentRouteAsync("plan");

			_requests.Should().BeEmpty();
			current.Successful.Should().BeTrue();
			current.Stored.Should().BeTrue();
			current.Legs.Select(l => l.RouteStopId).Should().Equal("a", "b", null);
			current.Legs[1].OffsetSeconds.Should().Be(1500);
		}

		[Test]
		public async Task a_moved_stop_or_a_stale_flag_regenerates()
		{
			await _service.GenerateAsync("plan");
			_requests.Clear();

			_stops[1].Latitude = 39.53m;
			await _service.GetCurrentRouteAsync("plan");
			_requests.Should().HaveCount(1);

			_plan.RouteIsStale = true;
			await _service.GetCurrentRouteAsync("plan");
			_requests.Should().HaveCount(2);
		}

		[Test]
		public async Task without_a_start_point_the_first_stop_starts_the_route()
		{
			_plan.UseStationAsStart = false;
			_plan.StartLatitude = null;
			_plan.StartLongitude = null;

			await _service.GenerateAsync("plan");

			_requests[0].Waypoints.Should().HaveCount(3);
			_stops[0].PlannedOffsetSeconds.Should().Be(0);
			// The first stop's dwell happens before the first leg.
			_stops[1].PlannedOffsetSeconds.Should().Be(5 * 60 + 600);
		}

		[Test]
		public async Task long_plans_are_routed_in_overlapping_chunks_and_joined()
		{
			_stops = Enumerable.Range(1, 40).Select(i => new RouteStop
			{
				RouteStopId = "s" + i, RoutePlanId = "plan", StopOrder = i, Name = "S" + i,
				Latitude = 39.5m + i * 0.001m, Longitude = -119.8m
			}).ToList();

			var result = await _service.GenerateAsync("plan");

			// 42 waypoints (station, 40 stops, end): chunks of 25 sharing a point.
			_requests.Should().HaveCount(2);
			_requests[0].Waypoints.Should().HaveCount(25);
			_requests[1].Waypoints.Should().HaveCount(18);
			_requests[1].Waypoints[0].Latitude.Should().Be(_requests[0].Waypoints[24].Latitude);
			result.Legs.Should().HaveCount(41);
			_saved.MapboxRouteGeometry.Should().Contain("coordinates");
			_stops.Last().PlannedOffsetSeconds.Should().Be(40 * 600);
		}

		[Test]
		public void apply_order_must_list_every_stop_once()
		{
			Func<Task> missing = () => _service.ApplyOrderAsync("plan", new List<string> { "a" });
			Func<Task> duplicate = () => _service.ApplyOrderAsync("plan", new List<string> { "a", "a" });

			missing.Should().ThrowAsync<ArgumentException>();
			duplicate.Should().ThrowAsync<ArgumentException>();
		}

		[Test]
		public async Task apply_order_renumbers_then_regenerates()
		{
			await _service.ApplyOrderAsync("plan", new List<string> { "b", "a" });

			_routes.Verify(r => r.ReorderRouteStopsAsync("plan", It.Is<List<string>>(l => l.SequenceEqual(new[] { "b", "a" })), It.IsAny<CancellationToken>()), Times.Once);
			_requests.Should().HaveCount(1);
		}

		[Test]
		public async Task preview_proposes_an_order_with_start_and_end_fixed_and_writes_nothing()
		{
			_stops.Add(new RouteStop { RouteStopId = "c", RoutePlanId = "plan", StopOrder = 3, Name = "C", Latitude = 39.505m, Longitude = -119.805m });
			RouteOptimizationRequest asked = null;
			_routing.Setup(r => r.OptimizeAsync(It.IsAny<RouteOptimizationRequest>(), It.IsAny<CancellationToken>()))
				.Callback((RouteOptimizationRequest request, CancellationToken _) => asked = request)
				.ReturnsAsync(new RouteOptimizationResult { Successful = true, Provider = RoutingProviderNames.Valhalla, Order = new List<int> { 0, 3, 1, 2, 4 } });

			var preview = await _service.PreviewOptimizationAsync("plan");

			asked.KeepFirst.Should().BeTrue();
			asked.KeepLast.Should().BeTrue();
			preview.Successful.Should().BeTrue();
			preview.Changed.Should().BeTrue();
			preview.ProposedOrder.Should().Equal("c", "a", "b");
			_routes.Verify(r => r.ReorderRouteStopsAsync(It.IsAny<string>(), It.IsAny<List<string>>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task live_directions_route_from_the_position_through_remaining_stops_with_etas()
		{
			_routes.Setup(r => r.GetInstanceByIdAsync("inst")).ReturnsAsync(new RouteInstance { RouteInstanceId = "inst", RoutePlanId = "plan", UnitId = 30, DepartmentId = 7 });
			_routes.Setup(r => r.GetInstanceStopsAsync("inst")).ReturnsAsync(new List<RouteInstanceStop>
			{
				new RouteInstanceStop { RouteInstanceStopId = "ia", RouteStopId = "a", StopOrder = 1, Status = 2 },
				new RouteInstanceStop { RouteInstanceStopId = "ib", RouteStopId = "b", StopOrder = 2, Status = 0 }
			});

			var before = DateTime.UtcNow;
			var live = await _service.GetLiveDirectionsAsync("inst", 39.515, -119.815, 90, navigation: true, language: "de", metric: true);

			live.Successful.Should().BeTrue();
			var request = _requests.Single();
			request.BypassCache.Should().BeTrue();
			request.Language.Should().Be("de");
			request.Units.Should().Be(RoutingUnits.Metric);
			request.Waypoints.Should().HaveCount(3); // position, B, end
			request.Waypoints[0].Heading.Should().Be(90);

			live.Legs[0].RouteStopId.Should().Be("b");
			live.Legs[0].RouteInstanceStopId.Should().Be("ib");
			live.Legs[0].EtaUtc.Should().BeOnOrAfter(before.AddSeconds(600));
			// The end leg comes after B's 10 minutes of dwell.
			live.Legs[1].OffsetSeconds.Should().Be(600 + 600 + 600);
			_routes.Verify(r => r.SetInstanceLastRoutedOnAsync("inst", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
			_saved.Should().BeNull();
		}

		[Test]
		public void joined_geojson_drops_the_shared_point()
		{
			var joined = RoutePlanRoutingService.JoinGeoJson(new[]
			{
				"{\"type\":\"LineString\",\"coordinates\":[[1,1],[2,2]]}",
				"{\"type\":\"LineString\",\"coordinates\":[[2,2],[3,3]]}"
			});

			joined.Should().Be("{\"type\":\"LineString\",\"coordinates\":[[1,1],[2,2],[3,3]]}");
		}

		[Test]
		public void joined_directions_append_legs_and_sum_totals()
		{
			var joined = RoutePlanRoutingService.JoinDirections(new List<string>
			{
				"{\"distance\":100,\"duration\":10,\"geometry\":\"\",\"legs\":[{\"a\":1}]}",
				"{\"distance\":50,\"duration\":5,\"geometry\":\"\",\"legs\":[{\"b\":2}]}"
			});

			joined.Should().Contain("\"distance\":150").And.Contain("\"duration\":15").And.Contain("{\"a\":1},{\"b\":2}");
		}
	}
}
