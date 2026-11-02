using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services.Routing
{
	/// <summary>A responder's ETA from their last log: the routed drive time less the time travelled since the log.</summary>
	[TestFixture]
	public class PersonnelEtaTests
	{
		private static GeoService Service(RouteResult route)
		{
			var calls = new Mock<ICallsService>();
			calls.Setup(c => c.GetCallByIdAsync(7, false)).ReturnsAsync(new Call { CallId = 7, GeoLocationData = "39.80,-104.80" });

			var routing = new Mock<IRoutingService>();
			routing.Setup(r => r.GetRouteAsync(It.IsAny<RouteRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(route);

			return new GeoService(Mock.Of<IGeoLocationProvider>(), calls.Object, Mock.Of<IDepartmentGroupsService>(), Mock.Of<IAddressService>(),
				Mock.Of<IMappingService>(), routing.Object);
		}

		private static ActionLog RespondingLog(DateTime timestamp) => new ActionLog
		{
			ActionTypeId = (int)ActionTypes.RespondingToScene,
			DestinationId = 7,
			DestinationType = (int)DestinationEntityTypes.Call,
			GeoLocationData = "39.70,-104.90",
			Timestamp = timestamp
		};

		[Test]
		public async Task Minutes_travelled_since_the_log_come_off_the_drive_time_in_full()
		{
			var route = new RouteResult { Successful = true, Provider = RoutingProviderNames.Valhalla, DurationSeconds = 600 };

			var eta = await Service(route).GetPersonnelEtaInSecondsAsync(RespondingLog(DateTime.UtcNow.AddMinutes(-3)));

			// 600 s less 180 s travelled (Seconds would have taken off only the 0–59 component, leaving ~600).
			eta.Should().BeApproximately(420, 5);
		}

		[Test]
		public async Task A_cached_route_older_than_the_log_does_not_add_time()
		{
			var route = new RouteResult { Successful = true, Provider = RoutingProviderNames.Valhalla, DurationSeconds = 600, ComputedOn = DateTime.UtcNow.AddHours(-2) };

			var eta = await Service(route).GetPersonnelEtaInSecondsAsync(RespondingLog(DateTime.UtcNow));

			eta.Should().BeApproximately(600, 5);
		}

		[Test]
		public async Task A_responder_past_the_drive_time_is_due_now()
		{
			var route = new RouteResult { Successful = true, Provider = RoutingProviderNames.Valhalla, DurationSeconds = 600 };

			var eta = await Service(route).GetPersonnelEtaInSecondsAsync(RespondingLog(DateTime.UtcNow.AddMinutes(-30)));

			eta.Should().Be(0);
		}
	}
}
