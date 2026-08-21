using System.Collections.Generic;
using System;
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
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Controllers.v4;
using Resgrid.Web.Services.Models.v4.Chat;
using Resgrid.Web.ServicesCore.Helpers;

namespace Resgrid.Tests.Web.Services
{
	/// <summary>
	/// A commander line opened as a unit carries a single member row, and it is the unit's — the caller
	/// has no user row on the channel. The read pointer the response reports has to come from that row,
	/// or the client is told the whole conversation is unread every time it opens the line.
	/// </summary>
	[TestFixture]
	[NonParallelizable]
	public class ChatControllerCommanderLineTests
	{
		private const int DepartmentId = 10;
		private const string UserId = "requester-user";
		private const int UnitId = 7;
		private const int CallId = 42;
		private const string ChannelId = "commander-line-1";

		private Mock<IChatChannelService> _chatChannelService;
		private Mock<IChatPermissionService> _chatPermissionService;
		private Mock<IFeatureToggleService> _featureToggleService;
		private Mock<IAuthorizationService> _authorizationService;
		private Mock<ICacheProvider> _cacheProvider;
		private ChatController _controller;
		private Activity _activity;
		private bool? _chatEnabledCacheValue;

		[SetUp]
		public void SetUp()
		{
			_chatEnabledCacheValue = null;
			_chatChannelService = new Mock<IChatChannelService>();
			_chatPermissionService = new Mock<IChatPermissionService>();

			_featureToggleService = new Mock<IFeatureToggleService>();
			_featureToggleService
				.Setup(x => x.IsEnabledAsync(FeatureFlagKeys.ChatSystem, DepartmentId, It.IsAny<bool>(), It.IsAny<IDictionary<string, string>>()))
				.ReturnsAsync(true);
			_authorizationService = new Mock<IAuthorizationService>();
			_authorizationService.Setup(x => x.IsUserValidWithinLimitsAsync(UserId, DepartmentId)).ReturnsAsync(true);
			_cacheProvider = new Mock<ICacheProvider>();
			_cacheProvider
				.Setup(x => x.RetrieveAsync<bool>(It.IsAny<string>(), It.IsAny<Func<Task<bool>>>(), It.IsAny<TimeSpan>()))
				.Returns(async (string key, Func<Task<bool>> fallback, TimeSpan expiration) =>
				{
					if (_chatEnabledCacheValue.HasValue)
						return _chatEnabledCacheValue.Value;

					_chatEnabledCacheValue = await fallback();
					return _chatEnabledCacheValue.Value;
				});

			_chatPermissionService.Setup(x => x.CanSendAsUnitAsync(UserId, UnitId, DepartmentId)).ReturnsAsync(true);

			_chatChannelService
				.Setup(x => x.EnsureIncidentCommanderLineAsync(DepartmentId, CallId, UserId, It.IsAny<int?>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new ChatChannel
				{
					ChatChannelId = ChannelId,
					DepartmentId = DepartmentId,
					ChannelType = (int)ChatChannelType.IncidentCommanderLine,
					CallId = CallId,
					LastMessageSeq = 12
				});

			// The unit's row is the only one on the channel; a lookup by user id finds nothing.
			_chatChannelService
				.Setup(x => x.GetUserMembershipAsync(ChannelId, UserId))
				.ReturnsAsync((ChatChannelMember)null);

			_chatChannelService
				.Setup(x => x.GetUnitMembershipAsync(ChannelId, UnitId))
				.ReturnsAsync(new ChatChannelMember
				{
					ChatChannelMemberId = "member-1",
					ChatChannelId = ChannelId,
					DepartmentId = DepartmentId,
					ParticipantType = (int)ChatParticipantType.Unit,
					UnitId = UnitId,
					LastReadSeq = 9,
					NotificationPreference = 2
				});

			var httpContext = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, UserId),
					new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString())
				}, "test"))
			};
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = httpContext };
			_activity = new Activity("ChatControllerCommanderLineTests").Start();

			_controller = new ChatController(
				_chatChannelService.Object,
				_chatPermissionService.Object,
				Mock.Of<IChatMessageService>(),
				Mock.Of<IChatModerationService>(),
				Mock.Of<IModerationService>(),
				Mock.Of<IChatPresenceService>(),
				Mock.Of<IChatAttachmentRepository>(),
				Mock.Of<IGifProvider>(),
				_featureToggleService.Object,
				_authorizationService.Object,
				_cacheProvider.Object,
				Mock.Of<IEventAggregator>(),
				Mock.Of<IQueueService>(),
				Mock.Of<IUserProfileService>(),
				Mock.Of<ICallsService>())
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

		[Test]
		public async Task CreateIncidentCommanderLine_AsUnit_ReportsTheUnitsReadState()
		{
			var response = await _controller.CreateIncidentCommanderLine(
				new CreateIncidentCommanderLineInput { CallId = CallId, AsUnitId = UnitId }, CancellationToken.None);

			response.Value.Should().NotBeNull();
			response.Value.Data.MyLastReadSeq.Should().Be(9);
			response.Value.Data.UnreadCount.Should().Be(3, "12 sent, 9 read");
			response.Value.Data.NotificationPreference.Should().Be(2);

			_chatChannelService.Verify(x => x.GetUnitMembershipAsync(ChannelId, UnitId), Times.Once);
			_chatChannelService.Verify(x => x.GetUserMembershipAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
		}

		[Test]
		public async Task CreateIncidentCommanderLine_AsUser_StillUsesTheUserRow()
		{
			_chatChannelService
				.Setup(x => x.GetUserMembershipAsync(ChannelId, UserId))
				.ReturnsAsync(new ChatChannelMember
				{
					ChatChannelMemberId = "member-2",
					ChatChannelId = ChannelId,
					DepartmentId = DepartmentId,
					ParticipantType = (int)ChatParticipantType.User,
					UserId = UserId,
					LastReadSeq = 5,
					NotificationPreference = 1
				});

			var response = await _controller.CreateIncidentCommanderLine(
				new CreateIncidentCommanderLineInput { CallId = CallId }, CancellationToken.None);

			response.Value.Should().NotBeNull();
			response.Value.Data.MyLastReadSeq.Should().Be(5);
			response.Value.Data.UnreadCount.Should().Be(7, "12 sent, 5 read");
			response.Value.Data.NotificationPreference.Should().Be(1);

			_chatChannelService.Verify(x => x.GetUnitMembershipAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
		}

		[Test]
		public async Task CreateIncidentCommanderLine_ReusesTheShortLivedChatEnabledResult()
		{
			var input = new CreateIncidentCommanderLineInput { CallId = CallId, AsUnitId = UnitId };

			await _controller.CreateIncidentCommanderLine(input, CancellationToken.None);
			await _controller.CreateIncidentCommanderLine(input, CancellationToken.None);

			_authorizationService.Verify(x => x.IsUserValidWithinLimitsAsync(UserId, DepartmentId), Times.Once);
			_featureToggleService.Verify(
				x => x.IsEnabledAsync(FeatureFlagKeys.ChatSystem, DepartmentId, It.IsAny<bool>(), It.IsAny<IDictionary<string, string>>()),
				Times.Once);
			_cacheProvider.Verify(
				x => x.RetrieveAsync<bool>("chat:enabled:10:requester-user", It.IsAny<Func<Task<bool>>>(), TimeSpan.FromSeconds(30)),
				Times.Exactly(2));
		}

		[Test]
		public async Task CreateIncidentCommanderLine_ReusesTheShortLivedChatDisabledResult()
		{
			_authorizationService.Setup(x => x.IsUserValidWithinLimitsAsync(UserId, DepartmentId)).ReturnsAsync(false);
			var input = new CreateIncidentCommanderLineInput { CallId = CallId, AsUnitId = UnitId };

			var first = await _controller.CreateIncidentCommanderLine(input, CancellationToken.None);
			var second = await _controller.CreateIncidentCommanderLine(input, CancellationToken.None);

			first.Result.Should().BeOfType<NotFoundResult>();
			second.Result.Should().BeOfType<NotFoundResult>();
			_authorizationService.Verify(x => x.IsUserValidWithinLimitsAsync(UserId, DepartmentId), Times.Once);
			_featureToggleService.Verify(
				x => x.IsEnabledAsync(FeatureFlagKeys.ChatSystem, DepartmentId, It.IsAny<bool>(), It.IsAny<IDictionary<string, string>>()),
				Times.Never);
		}
	}
}
