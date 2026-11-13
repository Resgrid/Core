using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Controllers.v4;
using Resgrid.Web.Services.Models.v4.Routing;
using Resgrid.Web.ServicesCore.Helpers;

namespace Resgrid.Tests.Web.Services
{
	/// <summary>
	/// v4 Routing and road closures (M0314, PR 585): a route that still runs through a full closure is shown but not
	/// navigable, and a route shaped by closures expires as soon as the server's copy does.
	/// </summary>
	[TestFixture]
	public class RoutingControllerRoadClosureTests
	{
		private const int DepartmentId = 10;
		private const string Line = "{\"type\":\"LineString\",\"coordinates\":[[3.70,51.05],[3.74,51.05]]}";

		private Mock<IRoutingService> _routing;
		private Mock<IRoadClosureService> _closures;
		private RoutingController _controller;
		private Activity _activity;

		[SetUp]
		public void SetUp()
		{
			_routing = new Mock<IRoutingService>();
			_routing.Setup(r => r.GetProviderInfoAsync(DepartmentId)).ReturnsAsync(new RoutingProviderInfo
			{
				Name = RoutingProviderNames.Valhalla,
				Capabilities = new RoutingCapabilities { Navigation = true },
				NavigationAvailable = true
			});

			_closures = new Mock<IRoadClosureService>();
			_closures.Setup(c => c.GetRoutingClosuresAsync(DepartmentId, It.IsAny<UnitTravelModes>(), It.IsAny<DateTime>())).ReturnsAsync(new List<RoutingClosure>());

			var httpContext = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, "unit-user"),
					new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString())
				}, "test"))
			};
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = httpContext };
			_activity = new Activity("RoutingControllerRoadClosureTests").Start();

			_controller = new RoutingController(_routing.Object, Mock.Of<ICallsService>(), Mock.Of<IUnitsService>(), Mock.Of<IAuthorizationService>(),
				Mock.Of<IUnitNavigationStateService>(), Mock.Of<IRouteService>(), _closures.Object)
			{
				ControllerContext = new ControllerContext { HttpContext = httpContext }
			};
		}

		[TearDown]
		public void TearDown()
		{
			ClaimsAuthorizationHelper._httpContextAccessor = null;
			_activity?.Stop();
		}

		private async Task<RouteResultData> RouteAsync(params RouteClosureEffect[] effects)
		{
			_routing.Setup(r => r.GetRouteAsync(It.IsAny<RouteRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new RouteResult
			{
				Successful = true,
				Provider = RoutingProviderNames.Valhalla,
				GeometryGeoJson = Line,
				DistanceMeters = 2800,
				DurationSeconds = 580,
				ClosureDelaySeconds = 300,
				Closures = new List<RouteClosureEffect>(effects)
			});

			var response = await _controller.GetRoute(new GetRouteInput
			{
				Points = new List<RoutePointInput> { new RoutePointInput { Latitude = 51.05, Longitude = 3.70 }, new RoutePointInput { Latitude = 51.05, Longitude = 3.74 } },
				Navigation = true
			}, CancellationToken.None);

			return response.Result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeOfType<GetRouteResult>().Which.Data;
		}

		[Test]
		public async Task a_route_still_through_a_full_closure_keeps_its_line_but_is_not_navigable()
		{
			var crossed = await RouteAsync(new RouteClosureEffect { ClosureId = "c1", Name = "Bridge", Effect = RouteClosureEffects.Crossed, DelaySeconds = 300 });

			crossed.NavigationAvailable.Should().BeFalse("its directions lead into the closed road");
			crossed.Geometry.Should().Be(Line);
			crossed.DurationSeconds.Should().Be(580);

			(await RouteAsync(new RouteClosureEffect { ClosureId = "c1", Name = "Bridge", Effect = RouteClosureEffects.Avoided })).NavigationAvailable.Should().BeTrue();
			(await RouteAsync()).NavigationAvailable.Should().BeTrue();
		}

		[Test]
		public async Task a_route_shaped_by_closures_expires_with_the_servers_copy()
		{
			var plain = await RouteAsync();
			plain.ExpiresUtc.Should().BeAfter(DateTime.UtcNow.AddHours(11));

			var avoided = await RouteAsync(new RouteClosureEffect { ClosureId = "c1", Name = "Bridge", Effect = RouteClosureEffects.Avoided });
			avoided.ExpiresUtc.Should().BeBefore(DateTime.UtcNow.AddMinutes(6));

			// Any closure in effect for the department keeps the client's copy short, as the routing service keeps its own.
			_closures.Setup(c => c.GetRoutingClosuresAsync(DepartmentId, It.IsAny<UnitTravelModes>(), It.IsAny<DateTime>()))
				.ReturnsAsync(new List<RoutingClosure> { new RoutingClosure { ClosureId = "far", Name = "Elsewhere" } });
			(await RouteAsync()).ExpiresUtc.Should().BeBefore(DateTime.UtcNow.AddMinutes(6));
		}
	}
}
