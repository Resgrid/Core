using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Chatbot.Models;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Providers.Chatbot.Interfaces;
using Resgrid.Providers.Chatbot.Services;

namespace Resgrid.Providers.Chatbot.Adapters
{
	public abstract class HttpChatbotAdapter : IExternalChatbotAdapter
	{
		protected readonly ChatbotHttpClient Http;
		protected HttpChatbotAdapter(ChatbotHttpClient http) { Http = http; }
		public abstract ChatbotPlatform Platform { get; }
		public abstract bool IsConfigured { get; }
		public bool IsInboundConfigured => IsConfigured && (Platform switch
		{
			ChatbotPlatform.Slack => !string.IsNullOrWhiteSpace(Config.ChatbotConfig.SlackSigningSecret),
			ChatbotPlatform.Discord => !string.IsNullOrWhiteSpace(Config.ChatbotConfig.DiscordPublicKey) && !string.IsNullOrWhiteSpace(Config.ChatbotConfig.DiscordClientId),
			ChatbotPlatform.Telegram => !string.IsNullOrWhiteSpace(Config.ChatbotConfig.TelegramWebhookSecretToken),
			ChatbotPlatform.WhatsApp => Uri.TryCreate(Config.ChatbotConfig.WhatsAppWebhookUrl, UriKind.Absolute, out var url) && url.Scheme == "https",
			ChatbotPlatform.Line => !string.IsNullOrWhiteSpace(Config.ChatbotConfig.LineChannelSecret),
			ChatbotPlatform.Signal => SignalBotAdapter.IsValidSecret(Config.ChatbotConfig.SignalWebhookSecret),
			_ => true
		});
		public virtual bool CanInitiateProactively => IsConfigured;
		public ChatbotPlatformCapabilities GetCapabilities() => new() { MaxMessageLength = MessageLength };
		protected abstract int MessageLength { get; }
		public virtual Task<ChatbotMessage> ParseInboundMessageAsync(object rawRequest)
		{
			if (rawRequest is not Dictionary<string, string> p || !p.TryGetValue("from", out var from)
				|| string.IsNullOrWhiteSpace(from) || !p.TryGetValue("text", out var text) || string.IsNullOrWhiteSpace(text))
				return Task.FromResult<ChatbotMessage>(null);
			p.TryGetValue("message_id", out var id);
			p.TryGetValue("to", out var to);
			return Task.FromResult(new ChatbotMessage
			{
				Platform = Platform, From = from, To = to, Text = text, MessageId = id,
				Timestamp = DateTime.UtcNow, PlatformMetadata = p.ToDictionary(x => x.Key, x => (object)x.Value)
			});
		}
		public Task<string> FormatOutboundResponseAsync(ChatbotResponse response) => Task.FromResult(response?.Text ?? "");
		public Task SendTypingIndicatorAsync(string platformUserId) => Task.CompletedTask;
		public virtual Task SendRichResponseAsync(string platformUserId, ChatbotResponse response) => SendAsync(platformUserId, response, null);
		public Task SendReplyAsync(ChatbotMessage message, ChatbotResponse response) => SendAsync(message.From, response, message);
		public async Task<ChatbotResponse> SendResponseAsync(string recipientId, ChatbotResponse response)
		{ await SendRichResponseAsync(recipientId, response); return response; }
		private async Task SendAsync(string recipient, ChatbotResponse response, ChatbotMessage inbound)
		{
			if (!IsConfigured) throw new InvalidOperationException($"{Platform} messaging is not configured.");
			if (string.IsNullOrWhiteSpace(recipient)) throw new ArgumentException("A messaging recipient is required.");
			var text = ProtectedOutboundGuard.Scrub(response?.Text, out _);
			if (string.IsNullOrWhiteSpace(text)) return;
			// Split before the provider limit without breaking a UTF-16 surrogate pair.
			for (var start = 0; start < text.Length;)
			{
				var length = Math.Min(MessageLength, text.Length - start);
				if (start + length < text.Length && char.IsHighSurrogate(text[start + length - 1])) length--;
				await SendTextAsync(recipient, text.Substring(start, length), inbound);
				start += length;
			}
		}
		protected abstract Task SendTextAsync(string recipient, string text, ChatbotMessage inbound);
		protected static string Id(string id, string prefix) => id.StartsWith(prefix + ":", StringComparison.OrdinalIgnoreCase) ? id.Substring(prefix.Length + 1) : id;
	}
}
