using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Resgrid.Chatbot.Models;
using Resgrid.Providers.Chatbot.Interfaces;
using Twilio.AspNet.Common;

namespace Resgrid.Providers.Chatbot.Adapters
{
	public class TwilioSmsAdapter : IChatbotPlatformAdapter
	{
		public ChatbotPlatform Platform => ChatbotPlatform.SmsTwilio;

		public ChatbotPlatformCapabilities GetCapabilities() => ChatbotPlatformCapabilities.ForPlatform(Platform);

		// SMS proactive sends are delivered through ICommunicationService/ISmsService (the chat
		// adapter does not own an SMS transport), so this is a no-op for the SMS adapters.
		public Task SendRichResponseAsync(string platformUserId, ChatbotResponse response) => Task.CompletedTask;

		public Task SendTypingIndicatorAsync(string platformUserId) => Task.CompletedTask;

		public Task<ChatbotMessage> ParseInboundMessageAsync(object rawRequest)
		{
			if (rawRequest is TwilioRequest twilioRequest)
			{
				// Check if it's a TwilioMessage with Body property
				string body = null;
				string messageSid = null;

				if (rawRequest is TwilioMessage twilioMessage)
				{
					body = twilioMessage.Body;
					messageSid = twilioMessage.MessageSid;
				}
				else
				{
					// Try to get body from query/form values
					var type = rawRequest.GetType();
					var bodyProp = type.GetProperty("Body");
					var sidProp = type.GetProperty("MessageSid") ?? type.GetProperty("SmsMessageSid");

					if (bodyProp != null)
						body = bodyProp.GetValue(rawRequest)?.ToString();
					if (sidProp != null)
						messageSid = sidProp.GetValue(rawRequest)?.ToString();
				}

				var message = new ChatbotMessage
				{
					MessageId = messageSid ?? Guid.NewGuid().ToString("N"),
					From = twilioRequest.From?.Replace("+", ""),
					To = twilioRequest.To?.Replace("+", ""),
					Text = body,
					Platform = ChatbotPlatform.SmsTwilio,
					Timestamp = DateTime.UtcNow,
					PlatformMetadata = new Dictionary<string, object>
					{
						["AccountSid"] = twilioRequest.AccountSid,
						["FromCity"] = twilioRequest.FromCity,
						["FromCountry"] = twilioRequest.FromCountry
					}
				};

				return Task.FromResult(message);
			}

			return Task.FromResult<ChatbotMessage>(null);
		}

		public Task<string> FormatOutboundResponseAsync(ChatbotResponse response)
		{
			return Task.FromResult(response?.Text ?? string.Empty);
		}
	}

	/// <summary>
	/// Strongly-typed Twilio message model for SMS parsing.
	/// </summary>
	public class TwilioMessage : TwilioRequest
	{
		public string MessageSid { get; set; }
		public string SmsMessageSid { get; set; }
		public string Body { get; set; }
	}
}
