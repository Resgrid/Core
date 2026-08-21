using System;
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
	public class ChatModerationServiceTests
	{
		private const string ChannelId = "channel-1";
		private const string ModeratorUserId = "moderator";
		private const string TargetUserId = "target";

		private ChatChannel _channel;
		private Mock<IChatChannelRepository> _channelRepository;
		private Mock<IChatChannelMemberRepository> _memberRepository;
		private Mock<IChatChannelService> _channelService;
		private Mock<IChatPermissionService> _permissionService;
		private ChatModerationService _service;

		[SetUp]
		public void SetUp()
		{
			_channel = new ChatChannel { ChatChannelId = ChannelId, DepartmentId = 1 };
			_channelRepository = new Mock<IChatChannelRepository>();
			_memberRepository = new Mock<IChatChannelMemberRepository>();
			_channelService = new Mock<IChatChannelService>();
			_permissionService = new Mock<IChatPermissionService>();
			var actionRepository = new Mock<IChatModerationActionRepository>();
			var auditService = new Mock<IAuditService>();

			_channelRepository.Setup(x => x.GetByIdAsync(ChannelId)).ReturnsAsync(_channel);
			_permissionService.Setup(x => x.CanModerateChannelAsync(_channel, ModeratorUserId)).ReturnsAsync(true);
			_permissionService.Setup(x => x.InvalidateChannelCacheAsync(ChannelId)).Returns(Task.CompletedTask);
			_memberRepository.Setup(x => x.SetMemberMutedAsync(It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
			_memberRepository.Setup(x => x.SetMemberBannedAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
			actionRepository.Setup(x => x.InsertAsync(It.IsAny<ChatModerationAction>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((ChatModerationAction action, CancellationToken _, bool _) => action);
			auditService.Setup(x => x.SaveAuditLogAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((AuditLog auditLog, CancellationToken _) => auditLog);

			_service = new ChatModerationService(
				Mock.Of<IChatMessageFlagRepository>(),
				actionRepository.Object,
				Mock.Of<IChatExportRepository>(),
				_channelRepository.Object,
				_memberRepository.Object,
				Mock.Of<IChatMessageRepository>(),
				Mock.Of<IChatMessageService>(),
				_channelService.Object,
				_permissionService.Object,
				auditService.Object,
				Mock.Of<IEventAggregator>());
		}

		[Test]
		public async Task Existing_banned_member_can_be_unbanned_without_current_channel_access()
		{
			var member = CreateMember();
			member.IsBanned = true;
			_memberRepository.Setup(x => x.GetUserMemberAsync(ChannelId, TargetUserId)).ReturnsAsync(member);
			_permissionService.Setup(x => x.CanAccessChannelAsync(_channel, TargetUserId, null)).ReturnsAsync(false);

			var result = await _service.SetUserBannedAsync(1, ChannelId, TargetUserId, false, ModeratorUserId, "appeal accepted");

			result.Should().BeTrue();
			_memberRepository.Verify(x => x.SetMemberBannedAsync(member.ChatChannelMemberId, false, null, It.IsAny<CancellationToken>()), Times.Once);
			_permissionService.Verify(x => x.CanAccessChannelAsync(It.IsAny<ChatChannel>(), It.IsAny<string>(), It.IsAny<int?>()), Times.Never);
			_channelService.Verify(x => x.EnsureMemberStateAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Existing_member_can_be_unmuted_without_current_channel_access()
		{
			var member = CreateMember();
			member.MutedUntil = DateTime.UtcNow.AddHours(1);
			_memberRepository.Setup(x => x.GetUserMemberAsync(ChannelId, TargetUserId)).ReturnsAsync(member);
			_permissionService.Setup(x => x.CanAccessChannelAsync(_channel, TargetUserId, null)).ReturnsAsync(false);

			var result = await _service.SetUserMutedAsync(1, ChannelId, TargetUserId, null, ModeratorUserId, "mute lifted");

			result.Should().BeTrue();
			_memberRepository.Verify(x => x.SetMemberMutedAsync(member.ChatChannelMemberId, null, It.IsAny<CancellationToken>()), Times.Once);
			_permissionService.Verify(x => x.CanAccessChannelAsync(It.IsAny<ChatChannel>(), It.IsAny<string>(), It.IsAny<int?>()), Times.Never);
			_channelService.Verify(x => x.EnsureMemberStateAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Missing_unauthorized_member_is_not_created_for_moderation()
		{
			_memberRepository.Setup(x => x.GetUserMemberAsync(ChannelId, TargetUserId)).ReturnsAsync((ChatChannelMember)null);
			_permissionService.Setup(x => x.CanAccessChannelAsync(_channel, TargetUserId, null)).ReturnsAsync(false);

			var result = await _service.SetUserBannedAsync(1, ChannelId, TargetUserId, true, ModeratorUserId, "policy violation");

			result.Should().BeFalse();
			_channelService.Verify(x => x.EnsureMemberStateAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
			_memberRepository.Verify(x => x.SetMemberBannedAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Missing_authorized_member_is_created_before_moderation_update()
		{
			var member = CreateMember();
			var mutedUntil = DateTime.UtcNow.AddHours(1);
			_memberRepository.Setup(x => x.GetUserMemberAsync(ChannelId, TargetUserId)).ReturnsAsync((ChatChannelMember)null);
			_permissionService.Setup(x => x.CanAccessChannelAsync(_channel, TargetUserId, null)).ReturnsAsync(true);
			_channelService.Setup(x => x.EnsureMemberStateAsync(ChannelId, 1, TargetUserId, null, It.IsAny<CancellationToken>())).ReturnsAsync(member);

			var result = await _service.SetUserMutedAsync(1, ChannelId, TargetUserId, mutedUntil, ModeratorUserId, "cooldown");

			result.Should().BeTrue();
			_channelService.Verify(x => x.EnsureMemberStateAsync(ChannelId, 1, TargetUserId, null, It.IsAny<CancellationToken>()), Times.Once);
			_memberRepository.Verify(x => x.SetMemberMutedAsync(member.ChatChannelMemberId, mutedUntil, It.IsAny<CancellationToken>()), Times.Once);
		}

		private static ChatChannelMember CreateMember()
		{
			return new ChatChannelMember
			{
				ChatChannelMemberId = "member-1",
				ChatChannelId = ChannelId,
				DepartmentId = 1,
				UserId = TargetUserId
			};
		}
	}
}
