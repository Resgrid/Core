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
	/// <summary>
	/// A closed incident's command and lane chat becomes a point-in-time record: nobody posts, nobody
	/// rewrites what is already there, and moderation still works. Posting is enforced by
	/// <see cref="ChatPermissionService.CanPostAsync"/>; this fixture covers the matching gates on the
	/// mutation paths, which previously let an author keep editing history in an archived channel.
	/// </summary>
	[TestFixture]
	public class ChatFrozenChannelTests
	{
		private const string ChannelId = "channel-1";
		private const string MessageId = "message-1";
		private const string SenderId = "sender";

		private Mock<IChatChannelRepository> _channelRepository;
		private Mock<IChatMessageRepository> _messageRepository;
		private Mock<IChatMessageReactionRepository> _reactionRepository;
		private Mock<IChatMessageEditRepository> _editRepository;
		private ChatMessage _message;

		[SetUp]
		public void Setup()
		{
			_message = new ChatMessage
			{
				ChatMessageId = MessageId,
				ChatChannelId = ChannelId,
				DepartmentId = 1,
				SenderUserId = SenderId,
				Body = "original body"
			};

			_channelRepository = new Mock<IChatChannelRepository>();
			_messageRepository = new Mock<IChatMessageRepository>();
			_reactionRepository = new Mock<IChatMessageReactionRepository>();
			_editRepository = new Mock<IChatMessageEditRepository>();

			_messageRepository.Setup(x => x.GetByIdAsync(MessageId)).ReturnsAsync(_message);
			_messageRepository
				.Setup(x => x.UpdateBodyAsync(MessageId, It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(true);
			_messageRepository
				.Setup(x => x.TombstoneAsync(MessageId, It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(true);
		}

		private void GivenChannelArchived(bool archived)
		{
			_channelRepository
				.Setup(x => x.GetByIdAsync(ChannelId))
				.ReturnsAsync(new ChatChannel { ChatChannelId = ChannelId, DepartmentId = 1, IsArchived = archived });
		}

		private ChatMessageService BuildService()
			=> new ChatMessageService(
				_channelRepository.Object,
				_messageRepository.Object,
				_editRepository.Object,
				Mock.Of<IChatAttachmentRepository>(),
				_reactionRepository.Object,
				Mock.Of<IChatMessageMentionRepository>(),
				Mock.Of<IChatMessageAckRepository>(),
				Mock.Of<IChatChannelMemberRepository>(),
				Mock.Of<IChatChannelService>(),
				Mock.Of<IChatPermissionService>(),
				Mock.Of<IUserProfileService>(),
				Mock.Of<IUnitsService>(),
				Mock.Of<IEventAggregator>());

		[Test]
		public async Task EditMessageAsync_is_refused_once_the_channel_is_frozen()
		{
			GivenChannelArchived(true);

			var result = await BuildService().EditMessageAsync(MessageId, SenderId, "rewritten after the fact");

			result.Should().BeNull();
			_message.Body.Should().Be("original body");
			_messageRepository.Verify(x => x.UpdateBodyAsync(MessageId, It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task EditMessageAsync_still_works_while_the_incident_is_active()
		{
			GivenChannelArchived(false);

			var result = await BuildService().EditMessageAsync(MessageId, SenderId, "corrected");

			result.Should().NotBeNull();
			result.Body.Should().Be("corrected");
		}

		[Test]
		public async Task DeleteMessageAsync_refuses_the_author_once_the_channel_is_frozen()
		{
			GivenChannelArchived(true);

			var result = await BuildService().DeleteMessageAsync(MessageId, SenderId, asModerator: false, reason: null);

			result.Should().BeFalse();
			_messageRepository.Verify(x => x.TombstoneAsync(MessageId, It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task DeleteMessageAsync_still_lets_a_moderator_remove_flagged_content_when_frozen()
		{
			GivenChannelArchived(true);

			// Moderation has to keep working on a closed incident — that is the whole point of leaving
			// flagging available on a frozen record.
			var result = await BuildService().DeleteMessageAsync(MessageId, "moderator", asModerator: true, reason: "policy");

			result.Should().BeTrue();
			_message.IsModerated.Should().BeTrue();
			_messageRepository.Verify(x => x.TombstoneAsync(MessageId, It.IsAny<DateTime>(), "moderator", true, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task Reactions_are_refused_both_ways_once_the_channel_is_frozen()
		{
			GivenChannelArchived(true);
			var service = BuildService();

			(await service.AddReactionAsync(MessageId, SenderId, null, "👍")).Should().BeFalse();
			(await service.RemoveReactionAsync(MessageId, SenderId, null, "👍")).Should().BeFalse();

			_reactionRepository.Verify(x => x.InsertAsync(It.IsAny<ChatMessageReaction>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
			_reactionRepository.Verify(
				x => x.DeleteReactionAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
				Times.Never);
		}

		[Test]
		public async Task A_missing_channel_reads_as_frozen_so_an_unanchored_edit_cannot_slip_through()
		{
			_channelRepository.Setup(x => x.GetByIdAsync(ChannelId)).ReturnsAsync((ChatChannel)null);

			var result = await BuildService().EditMessageAsync(MessageId, SenderId, "rewritten");

			result.Should().BeNull();
		}
	}
}
