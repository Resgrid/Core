using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Resgrid.Chatbot.Models;
using Resgrid.Framework;

namespace Resgrid.Providers.Chatbot.Adapters
{
	/// <summary>
	/// Slack Bot adapter using Socket Mode for receiving direct messages.
	/// Requires: SlackNet NuGet package.
	/// 
	/// Phase 2 implementation: DM-only bot with Block Kit support.
	/// Uses SlackNet with Socket Mode (no public HTTP endpoint needed).
	/// </summary>
	public class SlackBotAdapter : Interfaces.IChatbotPlatformAdapter, IAsyncDisposable
	{
		public ChatbotPlatform Platform => ChatbotPlatform.Slack;

		public ChatbotPlatformCapabilities GetCapabilities() => ChatbotPlatformCapabilities.ForPlatform(Platform);

		// Proactive/rich send. Delegates to the Slack client send (SendResponseAsync). Until the
		// SlackNet client is wired (P3.12), this routes through the existing stub send path.
		public Task SendRichResponseAsync(string platformUserId, ChatbotResponse response)
			=> SendResponseAsync(platformUserId, response);

		public Task SendTypingIndicatorAsync(string platformUserId) => Task.CompletedTask;

		public Task<bool> ValidateAsync(Dictionary<string, string> parameters)
		{
			// Slack Socket Mode apps don't use HTTP webhooks for message events.
			// Validation is handled at the Socket Mode connection level.
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
			// Called from the Socket Mode event handler when a DM is received.
			// Parameters should contain: from (user ID), text, channel_id
			
			parameters.TryGetValue("from", out var from);
			parameters.TryGetValue("text", out var text);
			parameters.TryGetValue("channel_id", out var channelId);
			parameters.TryGetValue("team_id", out var teamId);
			parameters.TryGetValue("ts", out var ts);

			var message = new ChatbotMessage
			{
				From = $"slack:{from}",
				Text = text ?? string.Empty,
				Platform = ChatbotPlatform.Slack,
				Timestamp = DateTime.UtcNow,
				PlatformMetadata = new Dictionary<string, object>
				{
					{ "slack_channel_id", channelId },
					{ "slack_team_id", teamId ?? string.Empty },
					{ "slack_ts", ts }
				}
			};

			return Task.FromResult(message);
		}

		public Task<ChatbotResponse> SendResponseAsync(string recipientId, ChatbotResponse response)
		{
			// SlackNet sends via ISlackApiClient. Rich Block Kit formatting
			// is composed separately based on response.ResponseFormat.
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
