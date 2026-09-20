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
	/// Meta-approved message template outside the 24h session window. Unsolicited messages use the
	/// configured static notification template; interactive replies use the WhatsApp transport.
	/// </summary>
	public class WhatsAppAdapter : HttpChatbotAdapter
	{
		public WhatsAppAdapter() : this(new Services.ChatbotHttpClient()) { }
		public WhatsAppAdapter(Services.ChatbotHttpClient http) : base(http) { }
		public override ChatbotPlatform Platform => ChatbotPlatform.WhatsApp;
		public override bool IsConfigured => !string.IsNullOrWhiteSpace(Config.NumberProviderConfig.TwilioAccountSid)
			&& !string.IsNullOrWhiteSpace(Config.NumberProviderConfig.TwilioAuthToken)
			&& !string.IsNullOrWhiteSpace(Config.ChatbotConfig.WhatsAppFromNumber);
		public override bool CanInitiateProactively => IsConfigured && !string.IsNullOrWhiteSpace(Config.ChatbotConfig.WhatsAppNotificationContentSid);
		protected override int MessageLength => 1500;
		// One static, approved notice per proactive event, regardless of the original content length.
		public override Task SendRichResponseAsync(string recipient, ChatbotResponse response)
			=> base.SendRichResponseAsync(recipient, new ChatbotResponse { Text = "Resgrid notification" });

		// Twilio signs the form but supplies no original timestamp. Retrieve it from the
		// authenticated Message resource so replaying a signed old form cannot renew a command.
		public async Task<DateTime> GetInboundTimestampAsync(string messageSid, string from, string to)
		{
			if (!System.Text.RegularExpressions.Regex.IsMatch(messageSid ?? "", @"^(?:SM|MM)[a-fA-F0-9]{32}$"))
				throw new ArgumentException("Invalid WhatsApp message identity.");
			var credentials = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(
				Config.NumberProviderConfig.TwilioAccountSid + ":" + Config.NumberProviderConfig.TwilioAuthToken));
			var resource = await Http.GetAsync("https://api.twilio.com/2010-04-01/Accounts/" +
				Uri.EscapeDataString(Config.NumberProviderConfig.TwilioAccountSid) + "/Messages/" + messageSid + ".json", "Basic " + credentials);
			if ((string)resource["sid"] != messageSid || (string)resource["account_sid"] != Config.NumberProviderConfig.TwilioAccountSid
				|| (string)resource["direction"] != "inbound" || (string)resource["from"] != from || (string)resource["to"] != to
				|| !DateTimeOffset.TryParse((string)resource["date_created"], System.Globalization.CultureInfo.InvariantCulture,
					System.Globalization.DateTimeStyles.AssumeUniversal, out var created))
				throw new InvalidOperationException("Unable to verify the original WhatsApp message.");
			return created.UtcDateTime;
		}

		public override Task<ChatbotMessage> ParseInboundMessageAsync(object rawRequest)
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

			return base.ParseInboundMessageAsync(rawRequest);
		}

		protected override async Task SendTextAsync(string recipient, string text, ChatbotMessage inbound)
		{
			var number = NormalizeWhatsAppNumber(recipient);
			if (!System.Text.RegularExpressions.Regex.IsMatch(number ?? "", @"^[1-9][0-9]{6,14}$"))
				throw new InvalidOperationException("Invalid WhatsApp recipient.");
			var fields = new Dictionary<string, string>
			{
				["To"] = "whatsapp:+" + number,
				["From"] = "whatsapp:+" + NormalizeWhatsAppNumber(Config.ChatbotConfig.WhatsAppFromNumber)
			};
			if (inbound == null)
			{
				if (!CanInitiateProactively) throw new InvalidOperationException("WhatsApp requires an approved notification template.");
				fields["ContentSid"] = Config.ChatbotConfig.WhatsAppNotificationContentSid;
			}
			else
			{
				if (DateTime.UtcNow - inbound.Timestamp > TimeSpan.FromHours(23))
					throw new InvalidOperationException("WhatsApp reply window expired.");
				fields["Body"] = text;
			}
			var credentials = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(
				Config.NumberProviderConfig.TwilioAccountSid + ":" + Config.NumberProviderConfig.TwilioAuthToken));
			var sent = await Http.SendAsync("https://api.twilio.com/2010-04-01/Accounts/" +
				Uri.EscapeDataString(Config.NumberProviderConfig.TwilioAccountSid) + "/Messages.json",
				new System.Net.Http.FormUrlEncodedContent(fields), "Basic " + credentials);
			if (string.IsNullOrWhiteSpace((string)sent["sid"])) throw new InvalidOperationException("WhatsApp rejected the message.");
		}

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
