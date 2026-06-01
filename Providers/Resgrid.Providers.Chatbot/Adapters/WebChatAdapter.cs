using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Resgrid.Chatbot.Interfaces;
using Resgrid.Chatbot.Models;
using Resgrid.Providers.Chatbot.Interfaces;

namespace Resgrid.Providers.Chatbot.Adapters
{
	/// <summary>
	/// Web Chat adapter for the Resgrid web UI. The user is already authenticated via their web session,
	/// so the platform user id is the Resgrid user id. Inbound messages arrive as a
	/// <see cref="WebChatInboundMessage"/> from the SignalR hub; outbound responses are pushed back to the
	/// user's live connection via <see cref="IChatbotWebChatNotifier"/> (SignalR EventingHub in the web
	/// layer). Online-only: with no live connection the notifier is a no-op and the outbound channel
	/// falls back to other transports.
	/// </summary>
	public class WebChatAdapter : IChatbotPlatformAdapter
	{
		private readonly IChatbotWebChatNotifier _notifier;

		public WebChatAdapter(IChatbotWebChatNotifier notifier)
		{
			_notifier = notifier;
		}

		public ChatbotPlatform Platform => ChatbotPlatform.WebChat;

		public ChatbotPlatformCapabilities GetCapabilities() => ChatbotPlatformCapabilities.ForPlatform(Platform);

		public Task<ChatbotMessage> ParseInboundMessageAsync(object rawRequest)
		{
			if (rawRequest is WebChatInboundMessage input && !string.IsNullOrWhiteSpace(input.UserId))
			{
				var message = new ChatbotMessage
				{
					MessageId = string.IsNullOrWhiteSpace(input.MessageId) ? Guid.NewGuid().ToString("N") : input.MessageId,
					From = input.UserId,
					Text = input.Text,
					Platform = ChatbotPlatform.WebChat,
					Timestamp = DateTime.UtcNow,
					PlatformMetadata = new Dictionary<string, object>
					{
						["departmentId"] = input.DepartmentId,
						["channel"] = "webchat"
					}
				};
				return Task.FromResult(message);
			}

			return Task.FromResult<ChatbotMessage>(null);
		}

		public Task<string> FormatOutboundResponseAsync(ChatbotResponse response)
			=> Task.FromResult(response?.Text ?? string.Empty);

		public async Task SendRichResponseAsync(string platformUserId, ChatbotResponse response)
		{
			if (string.IsNullOrWhiteSpace(platformUserId) || response == null)
				return;

			await _notifier.PushToUserAsync(platformUserId, response.Text ?? string.Empty);
		}

		public Task SendTypingIndicatorAsync(string platformUserId) => Task.CompletedTask;
	}

	/// <summary>Inbound Web Chat message from the authenticated SignalR client (user already authenticated).</summary>
	public class WebChatInboundMessage
	{
		public string UserId { get; set; }
		public int DepartmentId { get; set; }
		public string Text { get; set; }
		public string MessageId { get; set; }
	}
}
