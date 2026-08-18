using System;
using System.Collections.Generic;
using System.Linq;
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

		[Test]
		public async Task EnqueueCallBroadcastAsync_StripsProfileImagesBeforePublishing()
		{
			// Arrange: avatar blobs on every profile are what pushed the serialized broadcast
			// past RabbitMQ's 16MB frame limit and killed the dispatch outright.
			var queueItem = new CallQueueItem
			{
				Call = new Call { Address = "123 Main Street" },
				Profiles = new List<UserProfile>
				{
					new UserProfile { UserId = "user-1", Image = new byte[] { 1, 2, 3 } },
					new UserProfile { UserId = "user-2", Image = new byte[] { 4, 5, 6 } },
					null
				}
			};
			byte[][] imagesAsPublished = null;
			var outboundQueueProvider = new Mock<IOutboundQueueProvider>();
			outboundQueueProvider
				.Setup(provider => provider.EnqueueCall(queueItem))
				.Callback<CallQueueItem>(cqi =>
					imagesAsPublished = cqi.Profiles.Where(x => x != null).Select(x => x.Image).ToArray())
				.ReturnsAsync(true);
			var service = new QueueService(
				new Mock<IQueueItemsRepository>().Object,
				outboundQueueProvider.Object,
				new Mock<IDepartmentSettingsService>().Object,
				new Mock<IDepartmentsService>().Object,
				new Mock<IGeoLocationProvider>().Object);

			// Act
			var result = await service.EnqueueCallBroadcastAsync(queueItem);

			// Assert
			result.Should().BeTrue();
			imagesAsPublished.Should().OnlyContain(image => image == null);
		}

		[Test]
		public async Task EnqueueCallBroadcastAsync_WithNullProfiles_DoesNotThrow()
		{
			// Arrange
			var queueItem = new CallQueueItem
			{
				Call = new Call { Address = "123 Main Street" },
				Profiles = null
			};
			var outboundQueueProvider = new Mock<IOutboundQueueProvider>();
			outboundQueueProvider
				.Setup(provider => provider.EnqueueCall(queueItem))
				.ReturnsAsync(true);
			var service = new QueueService(
				new Mock<IQueueItemsRepository>().Object,
				outboundQueueProvider.Object,
				new Mock<IDepartmentSettingsService>().Object,
				new Mock<IDepartmentsService>().Object,
				new Mock<IGeoLocationProvider>().Object);

			// Act
			var result = await service.EnqueueCallBroadcastAsync(queueItem);

			// Assert
			result.Should().BeTrue();
		}
	}
}
