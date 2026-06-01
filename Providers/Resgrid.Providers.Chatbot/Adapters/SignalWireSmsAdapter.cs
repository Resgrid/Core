using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Resgrid.Chatbot.Models;
using Resgrid.Providers.Chatbot.Interfaces;

namespace Resgrid.Providers.Chatbot.Adapters
{
	public class SignalWireSmsAdapter : IChatbotPlatformAdapter
	{
		public ChatbotPlatform Platform => ChatbotPlatform.SmsSignalWire;

		public ChatbotPlatformCapabilities GetCapabilities() => ChatbotPlatformCapabilities.ForPlatform(Platform);

		// SMS proactive sends are delivered through ICommunicationService/ISmsService (the chat
		// adapter does not own an SMS transport), so this is a no-op for the SMS adapters.
		public Task SendRichResponseAsync(string platformUserId, ChatbotResponse response) => Task.CompletedTask;

		public Task SendTypingIndicatorAsync(string platformUserId) => Task.CompletedTask;

		public Task<ChatbotMessage> ParseInboundMessageAsync(object rawRequest)
		{
			if (rawRequest is IQueryCollection queryValues)
			{
				var getValue = new Func<string, string>(key =>
					queryValues.TryGetValue(key, out var val) ? val.ToString() : null);

				var message = new ChatbotMessage
				{
					MessageId = getValue("MessageSid") ?? Guid.NewGuid().ToString("N"),
					From = getValue("From")?.Replace("+", ""),
					To = getValue("To")?.Replace("+", ""),
					Text = getValue("Body"),
					Platform = ChatbotPlatform.SmsSignalWire,
					Timestamp = DateTime.UtcNow,
					PlatformMetadata = new Dictionary<string, object>
					{
						["AccountSid"] = getValue("AccountSid"),
						["NumMedia"] = getValue("NumMedia")
					}
				};

				return Task.FromResult(message);
			}

			// Fallback: try query dictionary
			if (rawRequest is System.Collections.Generic.Dictionary<string, string> dict)
			{
				var message = new ChatbotMessage
				{
					MessageId = (dict.TryGetValue("MessageSid", out var sid) ? sid : null) ?? Guid.NewGuid().ToString("N"),
					From = (dict.TryGetValue("From", out var from) ? from : null)?.Replace("+", ""),
					To = (dict.TryGetValue("To", out var to) ? to : null)?.Replace("+", ""),
					Text = dict.TryGetValue("Body", out var body) ? body : null,
					Platform = ChatbotPlatform.SmsSignalWire,
					Timestamp = DateTime.UtcNow
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
}
