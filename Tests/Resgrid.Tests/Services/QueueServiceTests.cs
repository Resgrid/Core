using System;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Queue;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	public class QueueServiceTests
	{
		[Test]
		public async Task EnqueueCallBroadcastAsync_WhenPublisherReturnsFalse_Throws()
		{
			// Arrange
			var queueItem = new CallQueueItem
			{
				Call = new Call { Address = "123 Main Street" }
			};
			var outboundQueueProvider = new Mock<IOutboundQueueProvider>();
			outboundQueueProvider
				.Setup(provider => provider.EnqueueCall(queueItem))
				.ReturnsAsync(false);
			var service = new QueueService(
				new Mock<IQueueItemsRepository>().Object,
				outboundQueueProvider.Object,
				new Mock<IDepartmentSettingsService>().Object,
				new Mock<IDepartmentsService>().Object,
				new Mock<IGeoLocationProvider>().Object);

			// Act
			Func<Task> act = async () => await service.EnqueueCallBroadcastAsync(queueItem);

			// Assert
			await act.Should().ThrowAsync<InvalidOperationException>()
				.WithMessage("Failed to enqueue call broadcast for processing.");
			outboundQueueProvider.Verify(provider => provider.EnqueueCall(queueItem), Times.Once);
		}
	}
}
