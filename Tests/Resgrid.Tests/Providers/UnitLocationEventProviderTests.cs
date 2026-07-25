using System.Threading.Tasks;
using System.Collections.Generic;
using System.Threading;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Providers.Bus;

namespace Resgrid.Tests.Providers
{
	[TestFixture]
	public class UnitLocationEventProviderTests
	{
		[TestCase(true)]
		[TestCase(false)]
		public async Task EnqueueUnitLocationEventAsync_should_return_the_confirmed_publish_result(bool publishResult)
		{
			var unitLocationEvent = new UnitLocationEvent { DepartmentId = 7, UnitId = 12 };
			var queueProvider = new Mock<IRabbitOutboundQueueProvider>();
			queueProvider
				.Setup(provider => provider.EnqueueUnitLocationEvent(unitLocationEvent))
				.ReturnsAsync(publishResult);
			var provider = new UnitLocationEventProvider(queueProvider.Object);

			var result = await provider.EnqueueUnitLocationEventAsync(unitLocationEvent);

			result.Should().Be(publishResult);
			queueProvider.Verify(
				outbound => outbound.EnqueueUnitLocationEvent(unitLocationEvent),
				Times.Once);
		}

		[TestCase(true)]
		[TestCase(false)]
		public async Task EnqueueUnitLocationEventsAsync_should_return_the_confirmed_batch_result(
			bool publishResult)
		{
			var events = new List<UnitLocationEvent>
			{
				new() { DepartmentId = 7, UnitId = 12 },
				new() { DepartmentId = 7, UnitId = 12 }
			};
			var queueProvider = new Mock<IRabbitOutboundQueueProvider>();
			queueProvider
				.Setup(provider => provider.EnqueueUnitLocationEvents(
					events,
					It.IsAny<CancellationToken>()))
				.ReturnsAsync(publishResult);
			var provider = new UnitLocationEventProvider(queueProvider.Object);

			var result = await provider.EnqueueUnitLocationEventsAsync(events);

			result.Should().Be(publishResult);
			queueProvider.Verify(
				outbound => outbound.EnqueueUnitLocationEvents(
					events,
					It.IsAny<CancellationToken>()),
				Times.Once);
		}
	}
}
