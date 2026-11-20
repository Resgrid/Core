using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Services;
using Resgrid.Web.Services.Controllers.v4;
using Resgrid.Web.Services.Models.v4.Routes;
using Resgrid.Web.ServicesCore.Helpers;
using IAuthorizationService = Resgrid.Model.Services.IAuthorizationService;

namespace Resgrid.Tests.Services.Routing
{
	/// <summary>Call start and destination locations (M0322): one destination, defaults from the location POI and the route.</summary>
	[TestFixture]
	public class CallLocationsTests
	{
		[Test]
		public void A_destination_poi_clears_the_free_destination()
		{
			var call = new Call { DestinationPoiId = 5, DestinationAddress = "12 Main St", DestinationGeoLocationData = "39.5,-119.8", DestinationW3W = "a.b.c" };

			CallLocations.NormalizeDestination(call);

			call.DestinationPoiId.Should().Be(5);
			call.DestinationAddress.Should().BeNull();
			call.DestinationGeoLocationData.Should().BeNull();
			call.DestinationW3W.Should().BeNull();
		}

		[Test]
		public void A_free_destination_is_trimmed_and_its_point_normalized_and_blank_ids_dropped()
		{
			var call = new Call { DestinationPoiId = 0, LocationPoiId = 0, RoutePlanId = "  ", DestinationAddress = "  12 Main St ", DestinationGeoLocationData = " 39.5 , -119.8 " };

			CallLocations.NormalizeDestination(call);

			call.DestinationPoiId.Should().BeNull();
			call.LocationPoiId.Should().BeNull();
			call.RoutePlanId.Should().BeNull();
			call.DestinationAddress.Should().Be("12 Main St");
			call.DestinationGeoLocationData.Should().Be("39.5,-119.8");
			CallLocations.HasDestination(call).Should().BeTrue();
		}

		[TestCase("", false)]
		[TestCase(",", false)]
		[TestCase("0,0", false)]
		[TestCase("91,10", false)]
		[TestCase("abc,1", false)]
		[TestCase("39.5,-119.8", true)]
		public void Points_must_be_usable(string value, bool valid)
		{
			CallLocations.TryParsePoint(value, out _, out _).Should().Be(valid);
			(CallLocations.NormalizePoint(value) != null).Should().Be(valid);
		}

		[Test]
		public void Route_endpoints_fill_only_what_the_call_lacks()
		{
			var endpoints = new RoutePlanEndpoints
			{
				Start = new RoutePlanEndpoint { Latitude = 39.1, Longitude = -119.1, Name = "Station 1" },
				End = new RoutePlanEndpoint { Latitude = 39.9, Longitude = -119.9, Name = "Hospital" },
				StopsCount = 2
			};

			var blank = new Call();
			CallLocations.ApplyRouteEndpoints(blank, endpoints);
			blank.GeoLocationData.Should().Be("39.1,-119.1");
			blank.Address.Should().Be("Station 1");
			blank.DestinationGeoLocationData.Should().Be("39.9,-119.9");
			blank.DestinationAddress.Should().Be("Hospital");

			var located = new Call { Address = "5 Elm St", DestinationPoiId = 3 };
			CallLocations.ApplyRouteEndpoints(located, endpoints);
			located.Address.Should().Be("5 Elm St");
			located.GeoLocationData.Should().BeNull();
			located.DestinationGeoLocationData.Should().BeNull("the call already has a destination POI");
		}

		[Test]
		public void A_location_poi_fills_a_blank_address_and_point()
		{
			var call = new Call { LocationPoiId = 9 };
			CallLocations.ApplyLocationPoi(call, new Poi { PoiId = 9, Name = "Mercy", Address = "1 Hospital Way", Latitude = 39.2, Longitude = -119.3 });

			call.Address.Should().Be("1 Hospital Way");
			call.GeoLocationData.Should().Be("39.2,-119.3");

			var typed = new Call { LocationPoiId = 9, Address = "Bay 3", GeoLocationData = "39.25,-119.35" };
			CallLocations.ApplyLocationPoi(typed, new Poi { PoiId = 9, Name = "Mercy", Address = "1 Hospital Way", Latitude = 39.2, Longitude = -119.3 });
			typed.Address.Should().Be("Bay 3");
			typed.GeoLocationData.Should().Be("39.25,-119.35");
		}

		[Test]
		public void A_one_stop_plan_with_no_start_or_end_is_not_complete()
		{
			var point = new RoutePlanEndpoint { Latitude = 1, Longitude = 1 };
			new RoutePlanEndpoints { Start = point, End = point, StartIsStop = true, EndIsStop = true, StopsCount = 1 }.IsComplete.Should().BeFalse();
			new RoutePlanEndpoints { Start = point, End = point, StartIsStop = true, EndIsStop = true, StopsCount = 2 }.IsComplete.Should().BeTrue();
			new RoutePlanEndpoints { Start = point, End = null, StopsCount = 0 }.IsComplete.Should().BeFalse();
		}

		[Test]
		public void A_free_destination_satisfies_a_required_destination()
		{
			var policy = new NewCallFieldPolicy { Rules = new List<NewCallFieldRule> { new NewCallFieldRule { Key = NewCallFieldKeys.DestinationPoi, Visible = true, Required = true } } };

			NewCallFieldPolicyValidator.Validate(policy, new NewCallFieldValues { HasDispatchList = true }).Select(x => x.Key)
				.Should().Contain(NewCallFieldKeys.DestinationPoi);
			NewCallFieldPolicyValidator.Validate(policy, new NewCallFieldValues { HasDispatchList = true, DestinationGeolocation = "39.5,-119.8" }).Select(x => x.Key)
				.Should().NotContain(NewCallFieldKeys.DestinationPoi);
			NewCallFieldPolicyValidator.Validate(policy, new NewCallFieldValues { HasDispatchList = true, DestinationAddress = "1 Hospital Way" }).Select(x => x.Key)
				.Should().NotContain(NewCallFieldKeys.DestinationPoi);
		}
	}

	/// <summary>Plan endpoints for calls and the leading "route start" leg of a call route's live directions (M0322).</summary>
	[TestFixture]
	public class CallRouteDirectionsTests
	{
		private Mock<IRoutingService> _routing;
		private Mock<IRouteService> _routes;
		private Mock<IUnitsService> _units;
		private Mock<IDepartmentGroupsService> _groups;
		private Mock<IGeoService> _geo;
		private RoutePlanRoutingService _service;
		private RoutePlan _plan;
		private List<RouteStop> _stops;
		private List<RouteInstanceStop> _instanceStops;
		private RouteInstance _instance;
		private List<RouteRequest> _requests;

		[SetUp]
		public void SetUp()
		{
			_routing = new Mock<IRoutingService>();
			_routes = new Mock<IRouteService>();
			_units = new Mock<IUnitsService>();
			_groups = new Mock<IDepartmentGroupsService>();
			_geo = new Mock<IGeoService>();
			_requests = new List<RouteRequest>();

			_routing.Setup(r => r.GetProviderInfoAsync(It.IsAny<int>())).ReturnsAsync(new RoutingProviderInfo
			{
				Name = RoutingProviderNames.Valhalla, Capabilities = new RoutingCapabilities { Steps = true }, Attribution = "OSM"
			});
			_routing.Setup(r => r.GetRouteAsync(It.IsAny<RouteRequest>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((RouteRequest request, CancellationToken _) =>
				{
					_requests.Add(request);
					var legs = Enumerable.Range(0, request.Waypoints.Count - 1).Select(__ => new RouteLegSummary { DistanceMeters = 1000, DurationSeconds = 600 }).ToList();
					return new RouteResult
					{
						Successful = true,
						Provider = RoutingProviderNames.Valhalla,
						DistanceMeters = legs.Sum(l => l.DistanceMeters),
						DurationSeconds = legs.Sum(l => l.DurationSeconds),
						Legs = legs,
						GeometryGeoJson = "{\"type\":\"LineString\",\"coordinates\":[]}",
						NormalizedJson = "{\"legs\":[]}"
					};
				});

			// Starts at the unit's station (39.50,-119.80), stops A and B, ends at an explicit point.
			_plan = new RoutePlan
			{
				RoutePlanId = "plan", DepartmentId = 7, UnitId = 30, Name = "Transfer", RouteStatus = (int)RouteStatus.Active,
				UseStationAsStart = true, EndLatitude = 39.60m, EndLongitude = -119.60m, RoutingProfile = (int)RoutingProfile.Driving
			};
			_stops = new List<RouteStop>
			{
				new RouteStop { RouteStopId = "a", RoutePlanId = "plan", StopOrder = 1, Name = "A", Latitude = 39.51m, Longitude = -119.81m },
				new RouteStop { RouteStopId = "b", RoutePlanId = "plan", StopOrder = 2, Name = "B", Latitude = 39.52m, Longitude = -119.82m }
			};
			_instance = new RouteInstance { RouteInstanceId = "inst", RoutePlanId = "plan", UnitId = 30, DepartmentId = 7, CallId = 42, Status = (int)RouteInstanceStatus.InProgress };
			_instanceStops = new List<RouteInstanceStop>
			{
				new RouteInstanceStop { RouteInstanceStopId = "ia", RouteStopId = "a", StopOrder = 1, Status = 0 },
				new RouteInstanceStop { RouteInstanceStopId = "ib", RouteStopId = "b", StopOrder = 2, Status = 0 }
			};

			_routes.Setup(r => r.GetRoutePlanByIdAsync("plan")).ReturnsAsync(() => _plan);
			_routes.Setup(r => r.GetRouteStopsForPlanAsync("plan")).ReturnsAsync(() => _stops.ToList());
			_routes.Setup(r => r.GetInstanceByIdAsync("inst")).ReturnsAsync(() => _instance);
			_routes.Setup(r => r.GetInstanceStopsAsync("inst")).ReturnsAsync(() => _instanceStops.ToList());

			_units.Setup(u => u.GetUnitByIdAsync(30)).ReturnsAsync(new Unit { UnitId = 30, DepartmentId = 7, StationGroupId = 4 });
			_groups.Setup(g => g.GetGroupByIdAsync(4, false)).ReturnsAsync(new DepartmentGroup { DepartmentGroupId = 4, DepartmentId = 7, Name = "Station 4" });
			_geo.Setup(g => g.GetStationCoordinatesAsync(It.Is<DepartmentGroup>(d => d.DepartmentGroupId == 4))).ReturnsAsync(new GeoMath.GeoPoint(39.50, -119.80));

			var closures = new Mock<IRoadClosureService>();
			closures.Setup(c => c.GetRoutingClosuresAsync(It.IsAny<int>(), It.IsAny<UnitTravelModes>(), It.IsAny<DateTime>())).ReturnsAsync(new List<RoutingClosure>());
			_service = new RoutePlanRoutingService(_routing.Object, _routes.Object, _groups.Object, _units.Object, _geo.Object, closures.Object);
		}

		[Test]
		public async Task Endpoints_are_the_station_and_explicit_end_else_the_first_and_last_stop()
		{
			var endpoints = await _service.ResolveEndpointsAsync(_plan);
			endpoints.Start.Name.Should().Be("Station 4");
			endpoints.StartIsStop.Should().BeFalse();
			endpoints.End.Latitude.Should().Be(39.60);
			endpoints.EndIsStop.Should().BeFalse();
			endpoints.IsComplete.Should().BeTrue();

			_plan.UseStationAsStart = false;
			_plan.UnitId = null;
			_plan.EndLatitude = null;
			_plan.EndLongitude = null;
			endpoints = await _service.ResolveEndpointsAsync(_plan);
			endpoints.Start.Name.Should().Be("A");
			endpoints.StartIsStop.Should().BeTrue();
			endpoints.End.Name.Should().Be("B");
			endpoints.EndIsStop.Should().BeTrue();
			endpoints.IsComplete.Should().BeTrue();
		}

		[Test]
		public async Task A_call_route_not_yet_at_its_start_leads_there_first()
		{
			var live = await _service.GetLiveDirectionsAsync("inst", 39.40, -119.70, null, true, "en", false, includeStart: true);

			live.Successful.Should().BeTrue();
			_requests.Single().Waypoints.Should().HaveCount(5); // position, start, A, B, end
			_requests.Single().Waypoints[1].Latitude.Should().Be(39.50);
			live.RouteStart.Should().NotBeNull();
			live.Legs[0].IsRouteStart.Should().BeTrue();
			live.Legs[0].RouteInstanceStopId.Should().BeNull();
			live.Legs[0].Name.Should().Be("Station 4");
			live.Legs[1].RouteInstanceStopId.Should().Be("ia");
			live.Legs[2].RouteInstanceStopId.Should().Be("ib");
			live.Legs[3].IsRouteStart.Should().BeFalse();
		}

		[Test]
		public async Task Without_includeStart_the_live_route_is_as_before()
		{
			var live = await _service.GetLiveDirectionsAsync("inst", 39.40, -119.70, null, true, "en", false);

			_requests.Single().Waypoints.Should().HaveCount(4); // position, A, B, end
			live.Legs.Should().NotContain(l => l.IsRouteStart);
			live.RouteStart.Should().BeNull();
		}

		[Test]
		public async Task An_ordinary_run_a_reached_start_or_a_worked_stop_never_leads_to_the_start()
		{
			_instance.CallId = null;
			(await _service.GetLiveDirectionsAsync("inst", 39.40, -119.70, null, true, "en", false, includeStart: true)).Legs.Should().NotContain(l => l.IsRouteStart);

			_instance.CallId = 42;
			_instance.StartReachedOn = DateTime.UtcNow;
			(await _service.GetLiveDirectionsAsync("inst", 39.40, -119.70, null, true, "en", false, includeStart: true)).Legs.Should().NotContain(l => l.IsRouteStart);

			_instance.StartReachedOn = null;
			_instanceStops[0].Status = 1;
			(await _service.GetLiveDirectionsAsync("inst", 39.40, -119.70, null, true, "en", false, includeStart: true)).Legs.Should().NotContain(l => l.IsRouteStart);
		}

		[Test]
		public async Task A_unit_at_the_start_has_reached_it()
		{
			var live = await _service.GetLiveDirectionsAsync("inst", 39.5001, -119.8001, null, true, "en", false, includeStart: true);

			live.Legs.Should().NotContain(l => l.IsRouteStart);
			_routes.Verify(r => r.MarkStartReachedAsync("inst", It.IsAny<CancellationToken>()), Times.Once);
		}
	}

	/// <summary>Which plans a call can follow, call defaults and starting a call's route (M0322).</summary>
	[TestFixture]
	public class CallRoutesServiceTests
	{
		private Mock<IRouteService> _routes;
		private Mock<IRoutePlanRoutingService> _routing;
		private Mock<IMappingService> _mapping;
		private Mock<ICallsService> _calls;
		private CallRoutesService _service;
		private RoutePlanEndpoints _complete;

		[SetUp]
		public void SetUp()
		{
			_routes = new Mock<IRouteService>();
			_routing = new Mock<IRoutePlanRoutingService>();
			_mapping = new Mock<IMappingService>();
			_complete = new RoutePlanEndpoints
			{
				Start = new RoutePlanEndpoint { Latitude = 39.1, Longitude = -119.1, Name = "Start" },
				End = new RoutePlanEndpoint { Latitude = 39.9, Longitude = -119.9, Name = "End" },
				StopsCount = 1
			};

			_routing.Setup(r => r.ResolveEndpointsAsync(It.IsAny<RoutePlan>(), It.IsAny<int?>())).ReturnsAsync((RoutePlan plan, int? _) =>
				plan.RoutePlanId == "one-stop"
					? new RoutePlanEndpoints { Start = _complete.Start, End = _complete.Start, StartIsStop = true, EndIsStop = true, StopsCount = 1 }
					: _complete);

			_calls = new Mock<ICallsService>();
			_service = new CallRoutesService(_routes.Object, _routing.Object, _mapping.Object, new Lazy<ICallsService>(() => _calls.Object));
		}

		private static RoutePlan Plan(string id, int status = (int)RouteStatus.Active, int departmentId = 7, bool deleted = false) =>
			new RoutePlan { RoutePlanId = id, Name = id, DepartmentId = departmentId, RouteStatus = status, IsDeleted = deleted };

		[Test]
		public async Task Only_active_complete_plans_of_the_department_can_be_picked()
		{
			_routes.Setup(r => r.GetRoutePlansForDepartmentAsync(7)).ReturnsAsync(new List<RoutePlan>
			{
				Plan("zeta"), Plan("alpha"), Plan("draft", (int)RouteStatus.Draft), Plan("archived", (int)RouteStatus.Archived),
				Plan("deleted", deleted: true), Plan("elsewhere", departmentId: 8), Plan("one-stop")
			});

			var plans = await _service.GetCallRoutePlansAsync(7);

			plans.Select(p => p.Plan.RoutePlanId).Should().Equal("alpha", "zeta");
		}

		[Test]
		public async Task Starting_the_route_of_a_call_links_the_run_to_it()
		{
			var call = new Call { CallId = 42, DepartmentId = 7, RoutePlanId = "alpha" };
			_routes.Setup(r => r.GetRoutePlanByIdAsync("alpha")).ReturnsAsync(Plan("alpha"));
			_routes.Setup(r => r.StartRouteForCallAsync("alpha", 30, 42, "user", It.IsAny<CancellationToken>()))
				.ReturnsAsync(new RouteInstance { RouteInstanceId = "new", CallId = 42 });

			var instance = await _service.StartCallRouteAsync(call, 30, "user");

			instance.RouteInstanceId.Should().Be("new");
			_routing.Verify(r => r.GetCurrentRouteAsync("alpha", It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task The_units_run_of_the_same_call_is_returned_not_started_again()
		{
			var call = new Call { CallId = 42, DepartmentId = 7, RoutePlanId = "alpha" };
			_routes.Setup(r => r.GetActiveOrPausedInstanceForUnitAsync(7, 30))
				.ReturnsAsync(new RouteInstance { RouteInstanceId = "running", CallId = 42, Status = (int)RouteInstanceStatus.Paused });

			var instance = await _service.StartCallRouteAsync(call, 30, "user");

			instance.RouteInstanceId.Should().Be("running");
			_routes.Verify(r => r.StartRouteForCallAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Another_running_route_or_a_route_that_cannot_be_followed_is_refused()
		{
			var call = new Call { CallId = 42, DepartmentId = 7, RoutePlanId = "alpha" };
			_routes.Setup(r => r.GetRoutePlanByIdAsync("alpha")).ReturnsAsync(Plan("alpha"));
			_routes.Setup(r => r.GetActiveOrPausedInstanceForUnitAsync(7, 30))
				.ReturnsAsync(new RouteInstance { RouteInstanceId = "patrol", CallId = null, Status = (int)RouteInstanceStatus.InProgress });

			var conflict = (await FluentActions.Awaiting(() => _service.StartCallRouteAsync(call, 30, "user")).Should().ThrowAsync<RouteInProgressException>()).Which;
			conflict.RouteInstanceId.Should().Be("patrol");

			_routes.Setup(r => r.GetRoutePlanByIdAsync("alpha")).ReturnsAsync(Plan("alpha", (int)RouteStatus.Archived));
			await FluentActions.Awaiting(() => _service.StartCallRouteAsync(call, 31, "user")).Should().ThrowAsync<InvalidOperationException>();

			await FluentActions.Awaiting(() => _service.StartCallRouteAsync(new Call { CallId = 43, DepartmentId = 7 }, 31, "user")).Should().ThrowAsync<InvalidOperationException>();
		}

		[Test]
		public async Task A_run_left_from_a_closed_call_is_cancelled_instead_of_blocking()
		{
			var call = new Call { CallId = 42, DepartmentId = 7, RoutePlanId = "alpha" };
			_routes.Setup(r => r.GetRoutePlanByIdAsync("alpha")).ReturnsAsync(Plan("alpha"));
			_routes.Setup(r => r.GetActiveOrPausedInstanceForUnitAsync(7, 30))
				.ReturnsAsync(new RouteInstance { RouteInstanceId = "old", CallId = 41, Status = (int)RouteInstanceStatus.InProgress });
			_calls.Setup(c => c.GetCallByIdAsync(41, It.IsAny<bool>())).ReturnsAsync(new Call { CallId = 41, DepartmentId = 7, State = (int)CallStates.Closed });
			_routes.Setup(r => r.StartRouteForCallAsync("alpha", 30, 42, "user", It.IsAny<CancellationToken>())).ReturnsAsync(new RouteInstance { RouteInstanceId = "new", CallId = 42 });

			(await _service.StartCallRouteAsync(call, 30, "user")).RouteInstanceId.Should().Be("new");
			_routes.Verify(r => r.CancelRouteAsync("old", "user", "Call closed", It.IsAny<CancellationToken>()), Times.Once);

			// Still open: the unit is told about the run instead.
			_calls.Setup(c => c.GetCallByIdAsync(41, It.IsAny<bool>())).ReturnsAsync(new Call { CallId = 41, DepartmentId = 7, State = (int)CallStates.Active });
			await FluentActions.Awaiting(() => _service.StartCallRouteAsync(call, 30, "user")).Should().ThrowAsync<RouteInProgressException>();
		}

		[Test]
		public async Task Defaults_come_from_the_location_poi_and_the_route()
		{
			_mapping.Setup(m => m.GetPOIByIdAsync(9)).ReturnsAsync(new Poi { PoiId = 9, PoiTypeId = 2, Name = "Mercy", Address = "1 Hospital Way", Latitude = 39.2, Longitude = -119.3 });
			_mapping.Setup(m => m.GetTypeByIdAsync(2)).ReturnsAsync(new PoiType { PoiTypeId = 2, DepartmentId = 7 });
			_routes.Setup(r => r.GetRoutePlanByIdAsync("alpha")).ReturnsAsync(Plan("alpha"));

			var call = new Call { DepartmentId = 7, LocationPoiId = 9, RoutePlanId = "alpha" };
			await _service.ApplyDefaultsAsync(call);

			call.Address.Should().Be("1 Hospital Way", "the location POI wins over the route start");
			call.GeoLocationData.Should().Be("39.2,-119.3");
			call.DestinationGeoLocationData.Should().Be("39.9,-119.9");
			call.DestinationAddress.Should().Be("End");
		}

		[Test]
		public async Task A_poi_of_another_department_is_not_a_location_poi()
		{
			_mapping.Setup(m => m.GetPOIByIdAsync(9)).ReturnsAsync(new Poi { PoiId = 9, PoiTypeId = 2 });
			_mapping.Setup(m => m.GetTypeByIdAsync(2)).ReturnsAsync(new PoiType { PoiTypeId = 2, DepartmentId = 8 });

			(await _service.GetLocationPoiAsync(7, 9)).Should().BeNull();
			(await _service.GetLocationPoiAsync(8, 9)).Should().NotBeNull();
		}
	}

	/// <summary>RouteService: a run started for a call carries the call; one route per unit; the start reached is recorded once.</summary>
	[TestFixture]
	public class RouteServiceCallRunTests
	{
		private Mock<IRoutePlansRepository> _plans;
		private Mock<IRouteStopsRepository> _stops;
		private Mock<IRouteInstancesRepository> _instances;
		private Mock<IRouteInstanceStopsRepository> _instanceStops;
		private RouteService _service;
		private List<RouteInstance> _saved;

		[SetUp]
		public void SetUp()
		{
			_plans = new Mock<IRoutePlansRepository>();
			_stops = new Mock<IRouteStopsRepository>();
			_instances = new Mock<IRouteInstancesRepository>();
			_instanceStops = new Mock<IRouteInstanceStopsRepository>();
			_saved = new List<RouteInstance>();

			_plans.Setup(p => p.GetByIdAsync("plan")).ReturnsAsync(new RoutePlan { RoutePlanId = "plan", DepartmentId = 7, RouteStatus = (int)RouteStatus.Active });
			_stops.Setup(s => s.GetStopsByRoutePlanIdAsync("plan")).ReturnsAsync(new List<RouteStop> { new RouteStop { RouteStopId = "a", StopOrder = 1 } });
			_instances.Setup(i => i.GetActiveInstancesByUnitIdAsync(It.IsAny<int>())).ReturnsAsync(new List<RouteInstance>());
			_instances.Setup(i => i.GetActiveInstancesByDepartmentIdAsync(It.IsAny<int>())).ReturnsAsync(new List<RouteInstance>());
			_instances.Setup(i => i.SaveOrUpdateAsync(It.IsAny<RouteInstance>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RouteInstance instance, CancellationToken _, bool __) =>
				{
					instance.RouteInstanceId ??= "inst";
					_saved.Add(instance);
					return instance;
				});

			var settings = new Mock<IDepartmentSettingsService>();
			settings.Setup(x => x.GetMappingNavigationConfigAsync(It.IsAny<int>(), It.IsAny<bool>())).ReturnsAsync(new MappingNavigationConfig());
			_service = new RouteService(_plans.Object, _stops.Object, new Mock<IRouteSchedulesRepository>().Object, _instances.Object, _instanceStops.Object,
				new Mock<IRouteDeviationsRepository>().Object, new Mock<IUnitLocationsDocRepository>().Object, settings.Object);
		}

		[Test]
		public async Task A_run_for_a_call_carries_the_call()
		{
			var instance = await _service.StartRouteForCallAsync("plan", 30, 42, "user");

			instance.CallId.Should().Be(42);
			instance.StopsTotal.Should().Be(1);
			(await _service.StartRouteAsync("plan", 31, "user")).CallId.Should().BeNull();
		}

		[Test]
		public async Task A_unit_already_running_a_route_is_refused_with_that_run()
		{
			_instances.Setup(i => i.GetActiveInstancesByUnitIdAsync(30)).ReturnsAsync(new List<RouteInstance> { new RouteInstance { RouteInstanceId = "running", DepartmentId = 7 } });

			var ex = (await FluentActions.Awaiting(() => _service.StartRouteForCallAsync("plan", 30, 42, "user")).Should().ThrowAsync<RouteInProgressException>()).Which;

			ex.RouteInstanceId.Should().Be("running");
			ex.Should().BeAssignableTo<InvalidOperationException>("the web Start Route page catches InvalidOperationException");
		}

		[Test]
		public async Task The_start_reached_is_recorded_once()
		{
			var instance = new RouteInstance { RouteInstanceId = "inst", DepartmentId = 7 };
			_instances.Setup(i => i.GetByIdAsync("inst")).ReturnsAsync(instance);
			_instances.Setup(i => i.SetStartReachedOnAsync("inst", It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

			var first = await _service.MarkStartReachedAsync("inst");
			var when = first.StartReachedOn;
			await _service.MarkStartReachedAsync("inst");

			when.Should().NotBeNull();
			instance.StartReachedOn.Should().Be(when);
			_instances.Verify(i => i.SetStartReachedOnAsync("inst", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
			_saved.Should().BeEmpty("only the start time is written, never the whole row");
		}
	}

	/// <summary>v4 Routes for a call's route (M0322): crews without route rights run the call's route; nothing else opens up.</summary>
	[TestFixture]
	[NonParallelizable]
	public class CallRoutesApiTests
	{
		private const int DepartmentId = 10;
		private const string UserId = "crew-1";

		private Dictionary<Type, Mock> _mocks;
		private Activity _activity;

		[SetUp]
		public void SetUp()
		{
			_mocks = new Dictionary<Type, Mock>();
			_activity = new Activity(nameof(CallRoutesApiTests)).Start();

			M<IRouteService>().Setup(x => x.GetInstanceStopByIdAsync("call-stop")).ReturnsAsync(new RouteInstanceStop { RouteInstanceStopId = "call-stop", RouteInstanceId = "call-run" });
			M<IRouteService>().Setup(x => x.GetInstanceStopByIdAsync("patrol-stop")).ReturnsAsync(new RouteInstanceStop { RouteInstanceStopId = "patrol-stop", RouteInstanceId = "patrol-run" });
			M<IRouteService>().Setup(x => x.GetInstanceByIdAsync("call-run")).ReturnsAsync(new RouteInstance
			{
				RouteInstanceId = "call-run", DepartmentId = DepartmentId, UnitId = 7, CallId = 42, Status = (int)RouteInstanceStatus.InProgress
			});
			M<IRouteService>().Setup(x => x.GetInstanceByIdAsync("patrol-run")).ReturnsAsync(new RouteInstance
			{
				RouteInstanceId = "patrol-run", DepartmentId = DepartmentId, UnitId = 7, Status = (int)RouteInstanceStatus.InProgress
			});
			M<IAuthorizationService>().Setup(x => x.CanUserViewCallAsync(UserId, 42)).ReturnsAsync(true);
			M<IAuthorizationService>().Setup(x => x.CanUserViewUnitAsync(UserId, 7)).ReturnsAsync(true);
			M<ICallsService>().Setup(x => x.GetCallByIdAsync(42, It.IsAny<bool>())).ReturnsAsync(new Call { CallId = 42, DepartmentId = DepartmentId, RoutePlanId = "alpha" });
			M<IUnitsService>().Setup(x => x.GetUnitByIdAsync(7)).ReturnsAsync(new Unit { UnitId = 7, DepartmentId = DepartmentId, Name = "Medic 7" });
		}

		[TearDown]
		public void TearDown()
		{
			ClaimsAuthorizationHelper._httpContextAccessor = null;
			_activity?.Stop();
		}

		private Mock<T> M<T>() where T : class
		{
			if (!_mocks.TryGetValue(typeof(T), out var mock))
			{
				mock = new Mock<T>();
				_mocks[typeof(T)] = mock;
			}

			return (Mock<T>)mock;
		}

		private RoutesController Build(params Claim[] claims)
		{
			var constructor = typeof(RoutesController).GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
			var arguments = constructor.GetParameters().Select(p =>
			{
				if (!_mocks.TryGetValue(p.ParameterType, out var mock))
				{
					mock = (Mock)Activator.CreateInstance(typeof(Mock<>).MakeGenericType(p.ParameterType));
					_mocks[p.ParameterType] = mock;
				}

				return mock.Object;
			}).ToArray();

			var identity = new ClaimsIdentity(new[]
			{
				new Claim(ClaimTypes.PrimarySid, UserId),
				new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString())
			}.Concat(claims), "test");
			var http = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = http };

			var controller = (RoutesController)constructor.Invoke(arguments);
			controller.ControllerContext = new ControllerContext { HttpContext = http };
			return controller;
		}

		private static Claim RouteClaim(string action) => new Claim(ResgridClaimTypes.Resources.Route, action);

		[Test]
		public async Task The_crew_checks_in_on_a_calls_route_without_route_rights_but_not_on_another_route()
		{
			var controller = Build();

			(await controller.CheckInAtStop(new CheckInInput { RouteInstanceStopId = "call-stop", Latitude = 1, Longitude = 1 })).Should().BeOfType<OkResult>();
			(await controller.CheckInAtStop(new CheckInInput { RouteInstanceStopId = "patrol-stop", Latitude = 1, Longitude = 1 })).Should().BeOfType<UnauthorizedResult>();

			M<IRouteService>().Verify(x => x.CheckInAtStopAsync("call-stop", 1, 1, RouteStopCheckInType.Manual, It.IsAny<CancellationToken>()), Times.Once);
			M<IRouteService>().Verify(x => x.CheckInAtStopAsync("patrol-stop", It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<RouteStopCheckInType>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Route_update_rights_still_work_every_route()
		{
			var controller = Build(RouteClaim(ResgridClaimTypes.Actions.Update));

			(await controller.CheckInAtStop(new CheckInInput { RouteInstanceStopId = "patrol-stop", Latitude = 1, Longitude = 1 })).Should().BeOfType<OkResult>();
		}

		[Test]
		public async Task Someone_who_cannot_see_the_unit_cannot_work_its_call_route()
		{
			M<IAuthorizationService>().Setup(x => x.CanUserViewUnitAsync(UserId, 7)).ReturnsAsync(false);

			(await Build().SkipStop(new SkipStopInput { RouteInstanceStopId = "call-stop", Reason = "closed" })).Should().BeOfType<UnauthorizedResult>();
		}

		[Test]
		public async Task Starting_a_calls_route_returns_the_run_and_a_running_route_is_a_conflict()
		{
			M<ICallRoutesService>().Setup(x => x.StartCallRouteAsync(It.Is<Call>(c => c.CallId == 42), 7, UserId, It.IsAny<CancellationToken>()))
				.ReturnsAsync(new RouteInstance { RouteInstanceId = "call-run", RoutePlanId = "alpha", UnitId = 7, CallId = 42 });

			var started = await Build().StartCallRoute(new StartCallRouteInput { CallId = 42, UnitId = 7 });
			var data = ((GetRouteInstanceResult)started.Result.Should().BeOfType<OkObjectResult>().Which.Value).Data;
			data.CallId.Should().Be(42);

			M<ICallRoutesService>().Setup(x => x.StartCallRouteAsync(It.IsAny<Call>(), 7, UserId, It.IsAny<CancellationToken>()))
				.ThrowsAsync(new RouteInProgressException("patrol-run", false));
			var conflict = await Build().StartCallRoute(new StartCallRouteInput { CallId = 42, UnitId = 7 });
			var problem = conflict.Result.Should().BeOfType<ObjectResult>().Which;
			problem.StatusCode.Should().Be(StatusCodes.Status409Conflict);
			((ProblemDetails)problem.Value).Type.Should().Be("route_in_progress");
			((ProblemDetails)problem.Value).Detail.Should().Be("patrol-run");
		}

		[Test]
		public async Task An_active_plan_can_be_previewed_with_call_rights_but_a_draft_needs_route_rights()
		{
			M<IRouteService>().Setup(x => x.GetRoutePlanByIdAsync("draft")).ReturnsAsync(new RoutePlan { RoutePlanId = "draft", DepartmentId = DepartmentId, RouteStatus = (int)RouteStatus.Draft });

			(await Build().GetDirections("draft", CancellationToken.None)).Result.Should().BeOfType<UnauthorizedResult>();
		}
	}
}
