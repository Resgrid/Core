using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Resgrid.Chatbot.Models;
using Resgrid.Framework;

namespace Resgrid.Providers.Chatbot.Adapters
{
	/// <summary>
	/// Discord Bot adapter using Gateway Intents for receiving direct messages.
	/// Requires: Discord.Net NuGet package.
	/// 
	/// Phase 2 implementation: DM-only bot with slash command support.
	/// Uses Discord.Net WebSocket client with Gateway Intents (DirectMessages, MessageContent).
	/// </summary>
	public class DiscordBotAdapter : Interfaces.IChatbotPlatformAdapter, IAsyncDisposable
	{
		public ChatbotPlatform Platform => ChatbotPlatform.Discord;

		public ChatbotPlatformCapabilities GetCapabilities() => ChatbotPlatformCapabilities.ForPlatform(Platform);

		// Proactive/rich send. Delegates to the gateway client send (SendResponseAsync). Until the
		// Discord.Net client is wired (P3.12), this routes through the existing stub send path.
		public Task SendRichResponseAsync(string platformUserId, ChatbotResponse response)
			=> SendResponseAsync(platformUserId, response);

		public Task SendTypingIndicatorAsync(string platformUserId) => Task.CompletedTask;

		public Task<bool> ValidateAsync(Dictionary<string, string> parameters)
		{
			// Discord uses OAuth2 for validation, not webhook signatures.
			// For direct messages to the bot, validation is handled at the gateway level.
			return Task.FromResult(true);
		}

		public Task<ChatbotMessage> ParseInboundMessageAsync(object rawRequest)
		{
			// Discord.Net messages arrive via gateway event handler.
			// rawRequest is expected to be a Dictionary<string, string> of parameters.
			if (rawRequest is Dictionary<string, string> parameters)
				return ConvertToChatbotMessageAsync(parameters, null);

			var empty = new Dictionary<string, string>();
			return ConvertToChatbotMessageAsync(empty, null);
		}

		public Task<string> FormatOutboundResponseAsync(ChatbotResponse response)
		{
			// Discord sends plain text or embeds via Discord.Net client.
			// Rich formatting (embeds, buttons) is composed by the rich formatter.
			return Task.FromResult(response?.Text ?? string.Empty);
		}

		private Task<ChatbotMessage> ConvertToChatbotMessageAsync(Dictionary<string, string> parameters, string body)
		{
			// In Discord.Net's Socket Mode, messages arrive via the gateway event handler,
			// not via HTTP webhook. This adapter's conversion is called from the event handler.
			// Parameters should contain: from (snowflake user ID), text, channel_id
			
			parameters.TryGetValue("from", out var from);
			parameters.TryGetValue("text", out var text);
			parameters.TryGetValue("channel_id", out var channelId);
			parameters.TryGetValue("guild_id", out var guildId);
			parameters.TryGetValue("message_id", out var messageId);

			var message = new ChatbotMessage
			{
				From = $"discord:{from}",
				Text = text ?? string.Empty,
				Platform = ChatbotPlatform.Discord,
				Timestamp = DateTime.UtcNow,
				PlatformMetadata = new Dictionary<string, object>
				{
					{ "discord_channel_id", channelId },
					{ "discord_guild_id", guildId ?? string.Empty },
					{ "discord_message_id", messageId }
				}
			};

			return Task.FromResult(message);
		}

		public Task<ChatbotResponse> SendResponseAsync(string recipientId, ChatbotResponse response)
		{
			// Discord.Net sends via IDiscordClient. This is called from the bot's
			// internal message handler. The response text is sent as a direct message.
			// Rich formatting (embeds, buttons) is composed separately.
			return Task.FromResult(response);
		}

		public Task<bool> IsUserAuthorizedAsync(string platformUserId)
		{
			// Adapters do NOT authorize. Inbound authorization is enforced centrally in
			// ChatbotIngressService via IChatbotUserIdentityService.GetIdentityAsync(...) (account
			// linking) and IAuthorizationService.IsUserValidWithinLimitsAsync(...). This method is not
			// part of IChatbotPlatformAdapter and is never called; it fails loud so it can't be mistaken
			// for a working authorization check.
			throw new NotSupportedException(
				"Adapters do not perform authorization; it is enforced in ChatbotIngressService.");
		}

		public ValueTask DisposeAsync()
		{
			return ValueTask.CompletedTask;
		}
	}

	public interface IChatbotPlatformAdapterConfig
	{
		string GetToken(ChatbotPlatform platform);
		string GetBotId(ChatbotPlatform platform);
	}
}
