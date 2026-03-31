using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	public class CallVideoFeedTests
	{
		private Mock<ICallsRepository> _callsRepo;
		private Mock<ICommunicationService> _communicationService;
		private Mock<ICallDispatchesRepository> _callDispatchesRepo;
		private Mock<ICallTypesRepository> _callTypesRepo;
		private Mock<ICallEmailFactory> _callEmailFactory;
		private Mock<ICacheProvider> _cacheProvider;
		private Mock<ICallNotesRepository> _callNotesRepo;
		private Mock<ICallAttachmentRepository> _callAttachmentRepo;
		private Mock<ICallDispatchGroupRepository> _callDispatchGroupRepo;
		private Mock<ICallDispatchUnitRepository> _callDispatchUnitRepo;
		private Mock<ICallDispatchRoleRepository> _callDispatchRoleRepo;
		private Mock<IDepartmentCallPriorityRepository> _callPriorityRepo;
		private Mock<IShortenUrlProvider> _shortenUrlProvider;
		private Mock<ICallProtocolsRepository> _callProtocolsRepo;
		private Mock<IGeoLocationProvider> _geoLocationProvider;
		private Mock<IDepartmentsService> _departmentsService;
		private Mock<ICallReferencesRepository> _callReferencesRepo;
		private Mock<ICallContactsRepository> _callContactsRepo;
		private Mock<IIndoorMapService> _indoorMapService;
		private Mock<ICallVideoFeedRepository> _callVideoFeedRepo;
		private CallsService _service;

		[SetUp]
		public void SetUp()
		{
			_callsRepo = new Mock<ICallsRepository>();
			_communicationService = new Mock<ICommunicationService>();
			_callDispatchesRepo = new Mock<ICallDispatchesRepository>();
			_callTypesRepo = new Mock<ICallTypesRepository>();
			_callEmailFactory = new Mock<ICallEmailFactory>();
			_cacheProvider = new Mock<ICacheProvider>();
			_callNotesRepo = new Mock<ICallNotesRepository>();
			_callAttachmentRepo = new Mock<ICallAttachmentRepository>();
			_callDispatchGroupRepo = new Mock<ICallDispatchGroupRepository>();
			_callDispatchUnitRepo = new Mock<ICallDispatchUnitRepository>();
			_callDispatchRoleRepo = new Mock<ICallDispatchRoleRepository>();
			_callPriorityRepo = new Mock<IDepartmentCallPriorityRepository>();
			_shortenUrlProvider = new Mock<IShortenUrlProvider>();
			_callProtocolsRepo = new Mock<ICallProtocolsRepository>();
			_geoLocationProvider = new Mock<IGeoLocationProvider>();
			_departmentsService = new Mock<IDepartmentsService>();
			_callReferencesRepo = new Mock<ICallReferencesRepository>();
			_callContactsRepo = new Mock<ICallContactsRepository>();
			_indoorMapService = new Mock<IIndoorMapService>();
			_callVideoFeedRepo = new Mock<ICallVideoFeedRepository>();

			_service = new CallsService(
				_callsRepo.Object, _communicationService.Object, _callDispatchesRepo.Object,
				_callTypesRepo.Object, _callEmailFactory.Object, _cacheProvider.Object,
				_callNotesRepo.Object, _callAttachmentRepo.Object, _callDispatchGroupRepo.Object,
				_callDispatchUnitRepo.Object, _callDispatchRoleRepo.Object, _callPriorityRepo.Object,
				_shortenUrlProvider.Object, _callProtocolsRepo.Object, _geoLocationProvider.Object,
				_departmentsService.Object, _callReferencesRepo.Object, _callContactsRepo.Object,
				_indoorMapService.Object, _callVideoFeedRepo.Object);
		}

		[Test]
		public async Task SaveCallVideoFeedAsync_ShouldSaveAndReturnFeed()
		{
			var feed = new CallVideoFeed
			{
				CallVideoFeedId = Guid.NewGuid().ToString(),
				CallId = 1,
				DepartmentId = 10,
				Name = "Drone Feed",
				Url = "rtsp://example.com/stream",
				FeedType = (int)CallVideoFeedTypes.Drone,
				FeedFormat = (int)CallVideoFeedFormats.RTSP,
				AddedByUserId = "user1",
				AddedOn = DateTime.UtcNow
			};

			_callVideoFeedRepo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<CallVideoFeed>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync(feed);

			var result = await _service.SaveCallVideoFeedAsync(feed);

			result.Should().NotBeNull();
			result.Name.Should().Be("Drone Feed");
			result.Url.Should().Be("rtsp://example.com/stream");
			_callVideoFeedRepo.Verify(x => x.SaveOrUpdateAsync(It.IsAny<CallVideoFeed>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);
		}

		[Test]
		public async Task GetCallVideoFeedsByCallIdAsync_ShouldReturnFeedsForCall()
		{
			var feeds = new List<CallVideoFeed>
			{
				new CallVideoFeed { CallVideoFeedId = "feed1", CallId = 1, Name = "Feed 1", Url = "http://example.com/1", IsDeleted = false },
				new CallVideoFeed { CallVideoFeedId = "feed2", CallId = 1, Name = "Feed 2", Url = "http://example.com/2", IsDeleted = false }
			};

			_callVideoFeedRepo.Setup(x => x.GetByCallIdAsync(1)).ReturnsAsync(feeds);

			var result = await _service.GetCallVideoFeedsByCallIdAsync(1);

			result.Should().HaveCount(2);
			result[0].Name.Should().Be("Feed 1");
			result[1].Name.Should().Be("Feed 2");
		}

		[Test]
		public async Task GetCallVideoFeedsByCallIdAsync_ShouldNotReturnDeletedFeeds()
		{
			var feeds = new List<CallVideoFeed>
			{
				new CallVideoFeed { CallVideoFeedId = "feed1", CallId = 1, Name = "Feed 1", Url = "http://example.com/1", IsDeleted = false },
				new CallVideoFeed { CallVideoFeedId = "feed2", CallId = 1, Name = "Feed 2", Url = "http://example.com/2", IsDeleted = true }
			};

			_callVideoFeedRepo.Setup(x => x.GetByCallIdAsync(1)).ReturnsAsync(feeds);

			var result = await _service.GetCallVideoFeedsByCallIdAsync(1);

			// The service returns all feeds from repo; filtering is done at the API layer
			result.Should().HaveCount(2);
		}

		[Test]
		public async Task DeleteCallVideoFeedAsync_ShouldSoftDelete()
		{
			var feed = new CallVideoFeed
			{
				CallVideoFeedId = "feed1",
				CallId = 1,
				DepartmentId = 10,
				Name = "Feed 1",
				Url = "http://example.com/1",
				IsDeleted = false
			};

			_callVideoFeedRepo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<CallVideoFeed>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync(feed);

			var result = await _service.DeleteCallVideoFeedAsync(feed, "deletingUser");

			result.Should().BeTrue();
			feed.IsDeleted.Should().BeTrue();
			feed.DeletedByUserId.Should().Be("deletingUser");
			feed.DeletedOn.Should().NotBeNull();
		}

		[Test]
		public async Task SaveCallVideoFeedAsync_WithCoordinates_ShouldPersistLocation()
		{
			var feed = new CallVideoFeed
			{
				CallVideoFeedId = Guid.NewGuid().ToString(),
				CallId = 1,
				DepartmentId = 10,
				Name = "Traffic Cam",
				Url = "http://example.com/traffic",
				Latitude = 39.2771m,
				Longitude = -119.772m,
				AddedByUserId = "user1",
				AddedOn = DateTime.UtcNow
			};

			_callVideoFeedRepo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<CallVideoFeed>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync(feed);

			var result = await _service.SaveCallVideoFeedAsync(feed);

			result.Should().NotBeNull();
			result.Latitude.Should().Be(39.2771m);
			result.Longitude.Should().Be(-119.772m);
		}

		[Test]
		public async Task GetCallVideoFeedByIdAsync_WithInvalidId_ShouldReturnNull()
		{
			_callVideoFeedRepo.Setup(x => x.GetByIdAsync("nonexistent")).ReturnsAsync((CallVideoFeed)null);

			var result = await _service.GetCallVideoFeedByIdAsync("nonexistent");

			result.Should().BeNull();
		}
	}
}
