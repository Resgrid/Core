using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	public class ChatMessageServiceTests
	{
		[TestCase("sender", false, ChatMessageEditType.SenderDelete)]
		[TestCase("moderator", true, ChatMessageEditType.ModeratorDelete)]
		public async Task DeleteMessageAsync_should_use_one_effective_actor_classification(
			string deletingUserId, bool expectedModerated, ChatMessageEditType expectedEditType)
		{
			var message = new ChatMessage
			{
				ChatMessageId = "message-1",
				ChatChannelId = "channel-1",
				DepartmentId = 1,
				SenderUserId = "sender",
				Body = "original body"
			};
			var channel = new ChatChannel { ChatChannelId = message.ChatChannelId, DepartmentId = message.DepartmentId };
			var channelRepository = new Mock<IChatChannelRepository>();
			var messageRepository = new Mock<IChatMessageRepository>();
			var editRepository = new Mock<IChatMessageEditRepository>();
			var eventAggregator = new Mock<IEventAggregator>();
			ChatEventRaised deleteEvent = null;

			messageRepository.Setup(x => x.GetByIdAsync(message.ChatMessageId)).ReturnsAsync(message);
			messageRepository
				.Setup(x => x.TombstoneAsync(message.ChatMessageId, It.IsAny<DateTime>(), deletingUserId, expectedModerated, It.IsAny<CancellationToken>()))
				.ReturnsAsync(true);
			channelRepository.Setup(x => x.GetByIdAsync(channel.ChatChannelId)).ReturnsAsync(channel);
			eventAggregator
				.Setup(x => x.SendMessage<ChatEventRaised>(It.IsAny<ChatEventRaised>()))
				.Callback<ChatEventRaised>(raised => deleteEvent = raised);

			var service = new ChatMessageService(
				channelRepository.Object,
				messageRepository.Object,
				editRepository.Object,
				Mock.Of<IChatAttachmentRepository>(),
				Mock.Of<IChatMessageReactionRepository>(),
				Mock.Of<IChatMessageMentionRepository>(),
				Mock.Of<IChatMessageAckRepository>(),
				Mock.Of<IChatChannelMemberRepository>(),
				Mock.Of<IChatChannelService>(),
				Mock.Of<IChatPermissionService>(),
				Mock.Of<IUserProfileService>(),
				Mock.Of<IUnitsService>(),
				eventAggregator.Object);

			var result = await service.DeleteMessageAsync(message.ChatMessageId, deletingUserId, true, null);

			result.Should().BeTrue();
			message.IsModerated.Should().Be(expectedModerated);
			messageRepository.Verify(x => x.TombstoneAsync(
				message.ChatMessageId, It.IsAny<DateTime>(), deletingUserId, expectedModerated, It.IsAny<CancellationToken>()), Times.Once);
			editRepository.Verify(x => x.InsertAsync(It.Is<ChatMessageEdit>(edit => edit.EditType == (int)expectedEditType),
				It.IsAny<CancellationToken>(), false), Times.Once);
			deleteEvent.Should().NotBeNull();
			var payload = JObject.Parse(deleteEvent.PayloadJson);
			payload.Value<bool>("DeletedByModerator").Should().Be(expectedModerated);
			payload.Value<bool>("IsModerated").Should().Be(expectedModerated);
		}
	}
}
