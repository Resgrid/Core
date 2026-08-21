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
			var permissionService = new Mock<IChatPermissionService>();
			var eventAggregator = new Mock<IEventAggregator>();
			ChatEventRaised deleteEvent = null;

			messageRepository.Setup(x => x.GetByIdAsync(message.ChatMessageId)).ReturnsAsync(message);
			messageRepository
				.Setup(x => x.TombstoneAsync(message.ChatMessageId, It.IsAny<DateTime>(), deletingUserId, expectedModerated, It.IsAny<CancellationToken>()))
				.ReturnsAsync(true);
			channelRepository.Setup(x => x.GetByIdAsync(channel.ChatChannelId)).ReturnsAsync(channel);
			permissionService.Setup(x => x.CanAccessChannelAsync(channel, deletingUserId, null)).ReturnsAsync(true);
			permissionService.Setup(x => x.CanModerateChannelAsync(channel, deletingUserId)).ReturnsAsync(true);
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
				permissionService.Object,
				Mock.Of<IUserProfileService>(),
				Mock.Of<IUnitsService>(),
				eventAggregator.Object);

			var result = await service.DeleteMessageAsync(message.DepartmentId, message.ChatMessageId, deletingUserId, true, null);

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

		// Double-tapping a reaction fires two AddReaction calls; the second must no-op without
		// attempting the insert (the unique-index violation would flood the error log).
		[TestCase("🙏", true, false)]  // same emoji already present -> success, no insert
		[TestCase("🔥", true, true)]   // different emoji -> insert proceeds
		public async Task AddReactionAsync_is_idempotent_for_duplicate_reactions(string emoji, bool expectedResult, bool expectInsert)
		{
			var message = new ChatMessage
			{
				ChatMessageId = "message-1",
				ChatChannelId = "channel-1",
				DepartmentId = 1,
				SenderUserId = "sender",
				Body = "body"
			};
			var channel = new ChatChannel { ChatChannelId = message.ChatChannelId, DepartmentId = message.DepartmentId };
			var channelRepository = new Mock<IChatChannelRepository>();
			var messageRepository = new Mock<IChatMessageRepository>();
			var reactionRepository = new Mock<IChatMessageReactionRepository>();
			var permissionService = new Mock<IChatPermissionService>();

			messageRepository.Setup(x => x.GetByIdAsync(message.ChatMessageId)).ReturnsAsync(message);
			channelRepository.Setup(x => x.GetByIdAsync(channel.ChatChannelId)).ReturnsAsync(channel);
			permissionService.Setup(x => x.CanAccessChannelAsync(channel, "user-1", null)).ReturnsAsync(true);
			reactionRepository
				.Setup(x => x.GetByMessageIdsAsync(It.IsAny<System.Collections.Generic.IEnumerable<string>>()))
				.ReturnsAsync(new[]
				{
					new ChatMessageReaction
					{
						ChatMessageId = message.ChatMessageId,
						ParticipantType = (int)ChatParticipantType.User,
						UserId = "USER-1",
						Emoji = "🙏"
					}
				});
			reactionRepository
				.Setup(x => x.InsertAsync(It.IsAny<ChatMessageReaction>(), It.IsAny<CancellationToken>(), false))
				.ReturnsAsync((ChatMessageReaction reaction, CancellationToken _, bool __) => reaction);

			var service = new ChatMessageService(
				channelRepository.Object,
				messageRepository.Object,
				Mock.Of<IChatMessageEditRepository>(),
				Mock.Of<IChatAttachmentRepository>(),
				reactionRepository.Object,
				Mock.Of<IChatMessageMentionRepository>(),
				Mock.Of<IChatMessageAckRepository>(),
				Mock.Of<IChatChannelMemberRepository>(),
				Mock.Of<IChatChannelService>(),
				permissionService.Object,
				Mock.Of<IUserProfileService>(),
				Mock.Of<IUnitsService>(),
				Mock.Of<IEventAggregator>());

			// Case-insensitive user match: stored UserId is "USER-1", caller sends "user-1".
			var result = await service.AddReactionAsync(message.DepartmentId, message.ChatMessageId, "user-1", null, emoji);

			result.Should().Be(expectedResult);
			reactionRepository.Verify(
				x => x.InsertAsync(It.IsAny<ChatMessageReaction>(), It.IsAny<CancellationToken>(), false),
				expectInsert ? Times.Once() : Times.Never());
		}

		[Test]
		public async Task SendMessageAsync_should_not_trust_the_department_in_the_client_request()
		{
			var channel = new ChatChannel { ChatChannelId = "channel-2", DepartmentId = 2 };
			var channelRepository = new Mock<IChatChannelRepository>();
			var permissionService = new Mock<IChatPermissionService>();
			channelRepository.Setup(x => x.GetByIdAsync(channel.ChatChannelId)).ReturnsAsync(channel);

			var service = new ChatMessageService(
				channelRepository.Object, Mock.Of<IChatMessageRepository>(), Mock.Of<IChatMessageEditRepository>(),
				Mock.Of<IChatAttachmentRepository>(), Mock.Of<IChatMessageReactionRepository>(), Mock.Of<IChatMessageMentionRepository>(),
				Mock.Of<IChatMessageAckRepository>(), Mock.Of<IChatChannelMemberRepository>(), Mock.Of<IChatChannelService>(),
				permissionService.Object, Mock.Of<IUserProfileService>(), Mock.Of<IUnitsService>(), Mock.Of<IEventAggregator>());

			var result = await service.SendMessageAsync(1, "user-1", new ChatMessageSendRequest
			{
				ChatChannelId = channel.ChatChannelId,
				DepartmentId = 2,
				Body = "forged cross-tenant send",
				MessageType = ChatMessageType.Text
			});

			result.Should().BeNull();
			permissionService.Verify(x => x.CanPostAsync(It.IsAny<ChatChannel>(), It.IsAny<string>(), It.IsAny<int?>()), Times.Never);
		}

		[Test]
		public async Task EditMessageAsync_should_reject_an_owned_message_outside_the_authenticated_department()
		{
			var message = new ChatMessage
			{
				ChatMessageId = "message-2",
				ChatChannelId = "channel-2",
				DepartmentId = 2,
				SenderUserId = "user-1",
				Body = "department two"
			};
			var messageRepository = new Mock<IChatMessageRepository>();
			var permissionService = new Mock<IChatPermissionService>();
			messageRepository.Setup(x => x.GetByIdAsync(message.ChatMessageId)).ReturnsAsync(message);

			var service = new ChatMessageService(
				Mock.Of<IChatChannelRepository>(), messageRepository.Object, Mock.Of<IChatMessageEditRepository>(),
				Mock.Of<IChatAttachmentRepository>(), Mock.Of<IChatMessageReactionRepository>(), Mock.Of<IChatMessageMentionRepository>(),
				Mock.Of<IChatMessageAckRepository>(), Mock.Of<IChatChannelMemberRepository>(), Mock.Of<IChatChannelService>(),
				permissionService.Object, Mock.Of<IUserProfileService>(), Mock.Of<IUnitsService>(), Mock.Of<IEventAggregator>());

			var result = await service.EditMessageAsync(1, message.ChatMessageId, message.SenderUserId, "forged edit");

			result.Should().BeNull();
			messageRepository.Verify(x => x.UpdateBodyAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
			permissionService.Verify(x => x.CanAccessChannelAsync(It.IsAny<ChatChannel>(), It.IsAny<string>(), It.IsAny<int?>()), Times.Never);
		}

		[Test]
		public async Task DeleteMessageAsync_should_revalidate_requested_moderator_authority()
		{
			var message = new ChatMessage
			{
				ChatMessageId = "message-1",
				ChatChannelId = "channel-1",
				DepartmentId = 1,
				SenderUserId = "sender"
			};
			var channel = new ChatChannel { ChatChannelId = message.ChatChannelId, DepartmentId = 1 };
			var channelRepository = new Mock<IChatChannelRepository>();
			var messageRepository = new Mock<IChatMessageRepository>();
			var permissionService = new Mock<IChatPermissionService>();
			messageRepository.Setup(x => x.GetByIdAsync(message.ChatMessageId)).ReturnsAsync(message);
			channelRepository.Setup(x => x.GetByIdAsync(channel.ChatChannelId)).ReturnsAsync(channel);
			permissionService.Setup(x => x.CanModerateChannelAsync(channel, "not-a-moderator")).ReturnsAsync(false);

			var service = new ChatMessageService(
				channelRepository.Object, messageRepository.Object, Mock.Of<IChatMessageEditRepository>(),
				Mock.Of<IChatAttachmentRepository>(), Mock.Of<IChatMessageReactionRepository>(), Mock.Of<IChatMessageMentionRepository>(),
				Mock.Of<IChatMessageAckRepository>(), Mock.Of<IChatChannelMemberRepository>(), Mock.Of<IChatChannelService>(),
				permissionService.Object, Mock.Of<IUserProfileService>(), Mock.Of<IUnitsService>(), Mock.Of<IEventAggregator>());

			var result = await service.DeleteMessageAsync(1, message.ChatMessageId, "not-a-moderator", true, "forged role");

			result.Should().BeFalse();
			messageRepository.Verify(x => x.TombstoneAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		/// <summary>
		/// Metadata url validation is a pure static helper on the service, so it is exercised directly
		/// rather than through the full SendMessageAsync dependency graph.
		/// </summary>
		private static string ValidateMetadataJson(ChatMessageType messageType, string metadataJson)
		{
			var method = typeof(ChatMessageService).GetMethod(
				"ValidateMetadataJson",
				System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

			return (string)method.Invoke(null, new object[] { messageType, metadataJson });
		}

		// Clients send GIF urls nested under "gif"; the allowlist has to reach into that section.
		// Reading only the root url meant every real payload skipped validation entirely.
		[TestCase("{\"gif\":{\"url\":\"https://media.giphy.com/media/a/giphy.gif\"}}", true)]
		[TestCase("{\"gif\":{\"url\":\"https://media3.giphy.com/media/a/giphy.gif\"}}", true)]
		[TestCase("{\"gif\":{\"url\":\"https://evil.example.com/a.gif\"}}", false)]
		[TestCase("{\"gif\":{\"url\":\"http://media.giphy.com/media/a/giphy.gif\"}}", false)]
		[TestCase("{\"GifUrl\":\"https://evil.example.com/a.gif\"}", false)]
		[TestCase("{\"GifUrl\":\"https://media.giphy.com/media/a/giphy.gif\"}", true)]
		public void ValidateMetadataJson_should_enforce_the_gif_cdn_allowlist(string metadataJson, bool expectKept)
		{
			var result = ValidateMetadataJson(ChatMessageType.Gif, metadataJson);

			if (expectKept)
				result.Should().Be(metadataJson);
			else
				result.Should().BeNull();
		}

		[Test]
		public void ValidateMetadataJson_should_reject_a_gif_preview_url_off_the_allowlist()
		{
			var metadataJson = "{\"gif\":{\"url\":\"https://media.giphy.com/media/a/giphy.gif\",\"previewUrl\":\"https://evil.example.com/p.gif\"}}";

			ValidateMetadataJson(ChatMessageType.Gif, metadataJson).Should().BeNull();
		}

		// Location payloads carry no url at all and must survive untouched.
		[Test]
		public void ValidateMetadataJson_should_keep_location_metadata()
		{
			var metadataJson = "{\"location\":{\"latitude\":37.7,\"longitude\":-122.4}}";

			ValidateMetadataJson(ChatMessageType.Location, metadataJson).Should().Be(metadataJson);
		}

		[TestCase("{\"link\":{\"url\":\"https://resgrid.com\"}}", true)]
		[TestCase("{\"link\":{\"url\":\"javascript:alert(1)\"}}", false)]
		[TestCase("{\"url\":\"https://resgrid.com\"}", true)]
		public void ValidateMetadataJson_should_require_http_schemes_for_link_urls(string metadataJson, bool expectKept)
		{
			var result = ValidateMetadataJson(ChatMessageType.Text, metadataJson);

			if (expectKept)
				result.Should().Be(metadataJson);
			else
				result.Should().BeNull();
		}
	}
}
