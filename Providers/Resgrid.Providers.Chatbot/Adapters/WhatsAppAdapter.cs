using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Resgrid.Chatbot.Models;
using Resgrid.Providers.Chatbot.Interfaces;
using Twilio.AspNet.Common;

namespace Resgrid.Providers.Chatbot.Adapters
{
	/// <summary>
	/// WhatsApp adapter via the Twilio WhatsApp API. Inbound webhooks share Twilio's SMS request shape
	/// but carry a "whatsapp:" channel prefix on the From/To numbers. Outbound proactive sends require a
	/// Meta-approved message template outside the 24h session window (Phase 3 §15.3), so the adapter
	/// registry reports WhatsApp as not-proactively-initiable for now; inbound replies are delivered by
	/// the Twilio send path like SMS.
	/// </summary>
	public class WhatsAppAdapter : IChatbotPlatformAdapter
	{
		public ChatbotPlatform Platform => ChatbotPlatform.WhatsApp;

		public ChatbotPlatformCapabilities GetCapabilities() => ChatbotPlatformCapabilities.ForPlatform(Platform);

		public Task<ChatbotMessage> ParseInboundMessageAsync(object rawRequest)
		{
			if (rawRequest is TwilioRequest twilioRequest)
			{
				string body = null;
				string messageSid = null;

				if (rawRequest is WhatsAppMessage waMessage)
				{
					body = waMessage.Body;
					messageSid = waMessage.MessageSid;
				}
				else
				{
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
					From = NormalizeWhatsAppNumber(twilioRequest.From),
					To = NormalizeWhatsAppNumber(twilioRequest.To),
					Text = body,
					Platform = ChatbotPlatform.WhatsApp,
					Timestamp = DateTime.UtcNow,
					PlatformMetadata = new Dictionary<string, object>
					{
						["AccountSid"] = twilioRequest.AccountSid,
						["FromCountry"] = twilioRequest.FromCountry,
						["channel"] = "whatsapp"
					}
				};

				return Task.FromResult(message);
			}

			return Task.FromResult<ChatbotMessage>(null);
		}

		public Task<string> FormatOutboundResponseAsync(ChatbotResponse response)
			=> Task.FromResult(response?.Text ?? string.Empty);

		// Proactive WhatsApp sends require an approved template (outside the 24h window). Until template
		// registration is wired, the registry marks WhatsApp non-initiable; inbound replies go via Twilio.
		public Task SendRichResponseAsync(string platformUserId, ChatbotResponse response) => Task.CompletedTask;

		public Task SendTypingIndicatorAsync(string platformUserId) => Task.CompletedTask;

		private static string NormalizeWhatsAppNumber(string number)
			=> number?.Replace("whatsapp:", "").Replace("+", "").Trim();
	}

	/// <summary>
	/// Strongly-typed Twilio WhatsApp inbound message (the WhatsApp webhook payload mirrors the SMS one).
	/// </summary>
	public class WhatsAppMessage : TwilioRequest
	{
		public string MessageSid { get; set; }
		public string SmsMessageSid { get; set; }
		public string Body { get; set; }
	}
}
