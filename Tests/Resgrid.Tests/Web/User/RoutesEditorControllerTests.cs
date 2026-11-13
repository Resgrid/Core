using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Areas.User.Models.Routes;

namespace Resgrid.Tests.Web.User
{
	/// <summary>
	/// The route plan editor's server side (maplibre-mapping-routing-navigation-plan.md §10.6): stop reorder and optimized
	/// order apply only to the department's own plan and its full stop list, dragged stop locations parse culture-free, and
	/// the editor context carries stations, unit stations and vehicle sizes.
	/// </summary>
	[TestFixture]
	[NonParallelizable]
	public class RoutesEditorControllerTests
	{
		private const int DepartmentId = 31;
		private const string UserId = "routes-admin";

		private Mock<IRouteService> _routes;
		private Mock<IRoutePlanRoutingService> _routing;
		private Mock<IUnitsService> _units;
		private Mock<IDepartmentGroupsService> _groups;
		private Mock<IGeoService> _geo;
		private RoutePlan _plan;
		private List<RouteStop> _stops;
		private RoutesController _controller;
		private CultureInfo _culture;

		[SetUp]
		public void SetUp()
		{
			_culture = CultureInfo.CurrentCulture;
			_plan = new RoutePlan { RoutePlanId = "plan-1", DepartmentId = DepartmentId, RouteStatus = (int)RouteStatus.Active };
			_stops = new List<RouteStop>
			{
				new RouteStop { RouteStopId = "a", RoutePlanId = "plan-1", Name = "Alpha", StopOrder = 1 },
				new RouteStop { RouteStopId = "b", RoutePlanId = "plan-1", Name = "Bravo", StopOrder = 2 },
				new RouteStop { RouteStopId = "c", RoutePlanId = "plan-1", Name = "Charlie", StopOrder = 3 }
			};

			_routes = new Mock<IRouteService>();
			_routes.Setup(r => r.GetRoutePlanByIdAsync("plan-1")).ReturnsAsync(() => _plan);
			_routes.Setup(r => r.GetRouteStopsForPlanAsync("plan-1")).ReturnsAsync(() => _stops);
			_routes.Setup(r => r.GetRouteStopByIdAsync(It.IsAny<string>())).ReturnsAsync((string id) => _stops.FirstOrDefault(s => s.RouteStopId == id));
			_routes.Setup(r => r.SaveRouteStopAsync(It.IsAny<RouteStop>(), It.IsAny<CancellationToken>())).ReturnsAsync((RouteStop s, CancellationToken _) => s);

			_routing = new Mock<IRoutePlanRoutingService>();
			_units = new Mock<IUnitsService>();
			_groups = new Mock<IDepartmentGroupsService>();
			_geo = new Mock<IGeoService>();

			var departments = new Mock<IDepartmentsService>();
			departments.Setup(d => d.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = DepartmentId, TimeZone = "Eastern Standard Time" });

			var routingService = new Mock<IRoutingService>();
			routingService.SetupGet(r => r.ProviderName).Returns(RoutingProviderNames.Mapbox);
			routingService.SetupGet(r => r.Capabilities).Returns(new RoutingCapabilities { Traffic = true });
			routingService.Setup(r => r.GetProviderInfoAsync(It.IsAny<int>()))
				.ReturnsAsync(new RoutingProviderInfo { Name = RoutingProviderNames.Mapbox, Capabilities = new RoutingCapabilities { Traffic = true } });

			var httpContext = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, UserId),
					new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString())
				}, "test"))
			};
			Resgrid.Web.Helpers.ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = httpContext };

			var contacts = new Mock<IContactsService>();
			contacts.Setup(c => c.GetAllContactsForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<Contact>());

			_controller = new RoutesController(_routes.Object, _units.Object, Mock.Of<ICallsService>(), contacts.Object, departments.Object,
				_routing.Object, _groups.Object, Mock.Of<IAuthorizationService>(), routingService.Object, _geo.Object)
			{
				ControllerContext = new ControllerContext { HttpContext = httpContext }
			};
		}

		[TearDown]
		public void TearDown()
		{
			CultureInfo.CurrentCulture = _culture;
			Resgrid.Web.Helpers.ClaimsAuthorizationHelper._httpContextAccessor = null;
		}

		private static JObject Json(IActionResult result) => JObject.FromObject(((JsonResult)result).Value);

		[Test]
		public async Task A_full_reorder_of_the_plans_stops_is_applied()
		{
			var result = Json(await _controller.ApplyStopOrder("plan-1", new List<string> { "c", "a", "b" }, CancellationToken.None));

			result["success"].Value<bool>().Should().BeTrue();
			_routing.Verify(r => r.ApplyOrderAsync("plan-1", It.Is<IList<string>>(o => o.SequenceEqual(new[] { "c", "a", "b" })), CancellationToken.None), Times.Once);
		}

		[TestCase("a", "b")]
		[TestCase("a", "b", "c", "c")]
		[TestCase("a", "b", "other-plans-stop")]
		public async Task An_order_that_is_not_exactly_the_plans_stops_is_refused(params string[] order)
		{
			var result = Json(await _controller.ApplyStopOrder("plan-1", order.ToList(), CancellationToken.None));

			result["success"].Value<bool>().Should().BeFalse();
			_routing.Verify(r => r.ApplyOrderAsync(It.IsAny<string>(), It.IsAny<IList<string>>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Another_departments_plan_is_neither_previewed_nor_reordered()
		{
			_plan.DepartmentId = 99;

			Json(await _controller.PreviewOptimization("plan-1", CancellationToken.None))["successful"].Value<bool>().Should().BeFalse();
			Json(await _controller.ApplyStopOrder("plan-1", new List<string> { "a", "b", "c" }, CancellationToken.None))["success"].Value<bool>().Should().BeFalse();
			_routing.Verify(r => r.PreviewOptimizationAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
			_routing.Verify(r => r.ApplyOrderAsync(It.IsAny<string>(), It.IsAny<IList<string>>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task The_optimization_preview_names_the_stops_in_both_orders()
		{
			_routing.Setup(r => r.PreviewOptimizationAsync("plan-1", It.IsAny<CancellationToken>())).ReturnsAsync(new RoutePlanOptimizationPreview
			{
				Successful = true,
				Changed = true,
				CurrentOrder = new List<string> { "a", "b", "c" },
				ProposedOrder = new List<string> { "b", "a", "c" },
				CurrentDistanceMeters = 9000,
				ProposedDistanceMeters = 7000
			});

			var result = Json(await _controller.PreviewOptimization("plan-1", CancellationToken.None));

			result["changed"].Value<bool>().Should().BeTrue();
			result["proposed"].Select(s => s["name"].Value<string>()).Should().Equal("Bravo", "Alpha", "Charlie");
			result["proposedDistanceMeters"].Value<double>().Should().Be(7000);
		}

		[Test]
		public async Task A_dragged_stop_location_parses_the_same_under_a_german_ui()
		{
			CultureInfo.CurrentCulture = new CultureInfo("de-DE");

			var result = Json(await _controller.MoveStop("b", "39.512345", "-119.801234"));

			result["success"].Value<bool>().Should().BeTrue();
			_stops[1].Latitude.Should().Be(39.512345m, "\"39.512345\" must not read as 39512345 where the comma is the decimal separator");
			_stops[1].Longitude.Should().Be(-119.801234m);
			_routing.Verify(r => r.GenerateAsync("plan-1", It.IsAny<CancellationToken>()), Times.Once);
		}

		[TestCase("91", "0")]
		[TestCase("0", "-181")]
		[TestCase("north", "0")]
		public async Task A_location_off_the_map_is_refused(string latitude, string longitude)
		{
			Json(await _controller.MoveStop("b", latitude, longitude))["success"].Value<bool>().Should().BeFalse();
			_routes.Verify(r => r.SaveRouteStopAsync(It.IsAny<RouteStop>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task An_added_stop_reads_its_location_culture_free_and_ignores_a_zero_radius()
		{
			CultureInfo.CurrentCulture = new CultureInfo("de-DE");

			await _controller.AddStop("plan-1", "Delta", null, 0, 0, "39.5", "-119.75", null, null, 0, null, null, null, null, null, null, null, CancellationToken.None);

			_routes.Verify(r => r.SaveRouteStopAsync(It.Is<RouteStop>(s => s.Name == "Delta" && s.Latitude == 39.5m && s.Longitude == -119.75m &&
				s.GeofenceRadiusMeters == null && s.StopOrder == 4), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task The_editor_gets_station_points_unit_stations_and_only_real_vehicle_sizes()
		{
			var station = new DepartmentGroup { DepartmentGroupId = 7, DepartmentId = DepartmentId, Name = "Station 7" };
			_groups.Setup(g => g.GetAllStationGroupsForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<DepartmentGroup> { station });
			_geo.Setup(g => g.GetStationCoordinatesAsync(station)).ReturnsAsync(new GeoMath.GeoPoint(39.5, -119.8));
			_units.Setup(u => u.GetUnitsForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<Unit>
			{
				new Unit { UnitId = 1, DepartmentId = DepartmentId, Name = "Ladder 7", Type = "Ladder", StationGroupId = 7 },
				new Unit { UnitId = 2, DepartmentId = DepartmentId, Name = "Car 1", Type = "Car" }
			});
			_units.Setup(u => u.GetUnitTypesForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<UnitType>
			{
				new UnitType { Type = "Ladder", VehicleHeightMeters = 3.6 },
				new UnitType { Type = "Car" }
			});
			_routes.Setup(r => r.GetSchedulesForPlanAsync("plan-1")).ReturnsAsync(new List<RouteSchedule>());

			var model = (RouteEditView)((ViewResult)await _controller.Edit("plan-1")).Model;

			model.Editor.Stations.Should().ContainSingle().Which.Latitude.Should().Be(39.5);
			model.Editor.UnitStations.Should().Equal(new Dictionary<int, int> { [1] = 7 });
			model.Editor.UnitVehicles.Keys.Should().Equal(1);
			model.Editor.UnitVehicles[1].HeightMeters.Should().Be(3.6);
			model.Editor.TrafficAvailable.Should().BeTrue();
			model.Editor.VehicleLimits.Should().Be("size", "Mapbox applies height, width and weight only");
		}

		[Test]
		public async Task A_unit_without_vehicle_limits_does_not_stop_the_units_after_it()
		{
			_groups.Setup(g => g.GetAllStationGroupsForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<DepartmentGroup>());
			_units.Setup(u => u.GetUnitsForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<Unit>
			{
				new Unit { UnitId = 2, DepartmentId = DepartmentId, Name = "Car 1", Type = "Car", StationGroupId = 3 },
				new Unit { UnitId = 1, DepartmentId = DepartmentId, Name = "Ladder 7", Type = "Ladder", StationGroupId = 7 }
			});
			_units.Setup(u => u.GetUnitTypesForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<UnitType>
			{
				new UnitType { Type = "Ladder", VehicleHeightMeters = 3.6 },
				new UnitType { Type = "Car" }
			});
			_routes.Setup(r => r.GetSchedulesForPlanAsync("plan-1")).ReturnsAsync(new List<RouteSchedule>());

			var model = (RouteEditView)((ViewResult)await _controller.Edit("plan-1")).Model;

			model.Editor.UnitStations.Should().Equal(new Dictionary<int, int> { [2] = 3, [1] = 7 });
			model.Editor.UnitVehicles.Keys.Should().Equal(1);
		}
	}
}
