using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Chatbot.Interfaces;
using Resgrid.Chatbot.Models;
using Resgrid.Providers.Chatbot.Adapters;

namespace Resgrid.Tests.Chatbot
{
	/// <summary>
	/// Tests for the WebChat adapter: inbound DTO parsing (authenticated user → ChatbotMessage) and
	/// outbound push through the eventing abstraction.
	/// </summary>
	[TestFixture]
	public class WebChatAdapterTests
	{
		[Test]
		public async Task ParseInbound_AuthenticatedUser_MapsToChatbotMessage()
		{
			var adapter = new WebChatAdapter(Mock.Of<IChatbotWebChatNotifier>());

			var msg = await adapter.ParseInboundMessageAsync(new WebChatInboundMessage
			{
				UserId = "user-1",
				DepartmentId = 7,
				Text = "calls",
				MessageId = "m1"
			});

			msg.Should().NotBeNull();
			msg.From.Should().Be("user-1");
			msg.Text.Should().Be("calls");
			msg.Platform.Should().Be(ChatbotPlatform.WebChat);
		}

		[Test]
		public async Task ParseInbound_MissingUser_ReturnsNull()
		{
			var adapter = new WebChatAdapter(Mock.Of<IChatbotWebChatNotifier>());

			var msg = await adapter.ParseInboundMessageAsync(new WebChatInboundMessage { UserId = "", Text = "hi" });

			msg.Should().BeNull();
		}

		[Test]
		public async Task SendRichResponse_PushesToUserViaNotifier()
		{
			var notifier = new Mock<IChatbotWebChatNotifier>();
			notifier.Setup(n => n.PushToUserAsync("user-1", It.IsAny<string>())).Returns(Task.CompletedTask);

			var adapter = new WebChatAdapter(notifier.Object);
			await adapter.SendRichResponseAsync("user-1", new ChatbotResponse { Text = "Active calls: none", Processed = true });

			notifier.Verify(n => n.PushToUserAsync("user-1", "Active calls: none"), Times.Once);
		}

		[Test]
		public void Capabilities_AreRich()
		{
			var adapter = new WebChatAdapter(Mock.Of<IChatbotWebChatNotifier>());
			var caps = adapter.GetCapabilities();

			caps.SupportsMarkdown.Should().BeTrue();
			caps.SupportsButtons.Should().BeTrue();
		}
	}
}
