using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Resgrid.Chatbot.Models;
using Resgrid.Framework;

namespace Resgrid.Providers.Chatbot.Adapters
{
	/// <summary>
	/// Telegram Bot adapter using webhook mode (preferred) or long polling (fallback).
	/// Requires: Telegram.Bot NuGet package.
	/// 
	/// Phase 2 implementation: Webhook mode with inline keyboard support.
	/// Users link via a /start command with a linking code.
	/// </summary>
	public class TelegramBotAdapter : Interfaces.IChatbotPlatformAdapter, IAsyncDisposable
	{
		public ChatbotPlatform Platform => ChatbotPlatform.Telegram;

		public ChatbotPlatformCapabilities GetCapabilities() => ChatbotPlatformCapabilities.ForPlatform(Platform);

		// Proactive/rich send. Delegates to the Telegram client send (SendResponseAsync). Until the
		// Telegram.Bot client is wired (P3.12), this routes through the existing stub send path.
		public Task SendRichResponseAsync(string platformUserId, ChatbotResponse response)
			=> SendResponseAsync(platformUserId, response);

		public Task SendTypingIndicatorAsync(string platformUserId) => Task.CompletedTask;

		public Task<bool> ValidateAsync(Dictionary<string, string> parameters)
		{
			// Telegram uses a bot token for all API calls.
			// Webhook requests can be validated via X-Telegram-Bot-Api-Secret-Token header.
			// For simplicity in Phase 2, we handle this in the controller/webhook endpoint.
			return Task.FromResult(true);
		}

		public Task<ChatbotMessage> ParseInboundMessageAsync(object rawRequest)
		{
			if (rawRequest is Dictionary<string, string> parameters)
				return ConvertToChatbotMessageAsync(parameters, null);

			var empty = new Dictionary<string, string>();
			return ConvertToChatbotMessageAsync(empty, null);
		}

		public Task<string> FormatOutboundResponseAsync(ChatbotResponse response)
		{
			return Task.FromResult(response?.Text ?? string.Empty);
		}

		private Task<ChatbotMessage> ConvertToChatbotMessageAsync(Dictionary<string, string> parameters, string body)
		{
			// Called from the webhook controller or long polling handler.
			// Parameters should contain: from (Telegram user ID), text, chat_id, username
			
			parameters.TryGetValue("from", out var from);
			parameters.TryGetValue("text", out var text);
			parameters.TryGetValue("chat_id", out var chatId);
			parameters.TryGetValue("username", out var username);
			parameters.TryGetValue("first_name", out var firstName);
			parameters.TryGetValue("last_name", out var lastName);

			var message = new ChatbotMessage
			{
				From = $"telegram:{from}",
				Text = text ?? string.Empty,
				Platform = ChatbotPlatform.Telegram,
				Timestamp = DateTime.UtcNow,
				PlatformMetadata = new Dictionary<string, object>
				{
					{ "telegram_chat_id", chatId },
					{ "telegram_username", username ?? string.Empty },
					{ "telegram_first_name", firstName ?? string.Empty },
					{ "telegram_last_name", lastName ?? string.Empty }
				}
			};

			return Task.FromResult(message);
		}

		public Task<ChatbotResponse> SendResponseAsync(string recipientId, ChatbotResponse response)
		{
			// Telegram.Bot sends via ITelegramBotClient.
			// Inline keyboards are composed based on response.ResponseFormat and response.RichComponents.
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
}
