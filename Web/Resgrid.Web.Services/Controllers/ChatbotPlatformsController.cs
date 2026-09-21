using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Resgrid.Chatbot.Models;
using Resgrid.Config;
using Resgrid.Model.Queue;
using Resgrid.Model.Services;
using Resgrid.Providers.Chatbot.Adapters;
using Resgrid.Providers.Chatbot.Interfaces;
using Resgrid.Providers.Chatbot.Services;
using Twilio.Security;

namespace Resgrid.Web.Services.Controllers
{
	[AllowAnonymous]
	[Route("api/v{VersionId:apiVersion}/ChatbotPlatforms")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	[RequestSizeLimit(262144)]
	public class ChatbotPlatformsController : ControllerBase
	{
		private readonly IQueueService _queue;
		private readonly IChatbotAdapterRegistry _registry;
		private readonly ChatbotJwtValidator _jwt;
		public ChatbotPlatformsController(IQueueService queue, IChatbotAdapterRegistry registry, ChatbotJwtValidator jwt)
		{ _queue = queue; _registry = registry; _jwt = jwt; }

		[HttpPost("{platform}")]
		public async Task<IActionResult> Receive(string platform)
		{
			if (!Enum.TryParse<ChatbotPlatform>(platform == "Teams" ? "MicrosoftTeams" : platform,
				true, out var kind) || !Enum.IsDefined(kind) || kind is ChatbotPlatform.Unknown or ChatbotPlatform.SmsTwilio or ChatbotPlatform.SmsSignalWire or ChatbotPlatform.WebChat)
				return NotFound();
			if (_registry.GetAdapter(kind) is not IExternalChatbotAdapter adapter || !adapter.IsInboundConfigured) return StatusCode(503);
			try
			{
				if (kind == ChatbotPlatform.WhatsApp) return await WhatsAppAsync();
				using var reader = new StreamReader(Request.Body);
				var body = await reader.ReadToEndAsync();
				if (body.Length > 262144) return StatusCode(413);
				var valid = kind switch
				{
					ChatbotPlatform.Slack => ChatbotWebhookSignature.Slack(body, ChatbotConfig.SlackSigningSecret, Header("X-Slack-Request-Timestamp"), Header("X-Slack-Signature")),
					ChatbotPlatform.Discord => ChatbotWebhookSignature.Discord(body, ChatbotConfig.DiscordPublicKey, Header("X-Signature-Timestamp"), Header("X-Signature-Ed25519")),
					ChatbotPlatform.Telegram => ChatbotWebhookSignature.EqualsSecret(Header("X-Telegram-Bot-Api-Secret-Token"), ChatbotConfig.TelegramWebhookSecretToken),
					ChatbotPlatform.Line => ChatbotWebhookSignature.Hmac(body, ChatbotConfig.LineChannelSecret, Header("X-Line-Signature"), base64: true),
					ChatbotPlatform.Viber => ChatbotWebhookSignature.Hmac(body, ChatbotConfig.ViberBotToken, Header("X-Viber-Content-Signature")),
					ChatbotPlatform.Signal => SignalBotAdapter.IsValidSecret(ChatbotConfig.SignalWebhookSecret)
						&& ChatbotWebhookSignature.EqualsSecret(Header("X-Resgrid-Signal-Secret"), ChatbotConfig.SignalWebhookSecret),
					ChatbotPlatform.MicrosoftTeams or ChatbotPlatform.GoogleChat => true, // JWT also binds parsed routing below.
					_ => false
				};
				if (!valid) return Unauthorized();
				var root = JObject.Parse(body);
				switch (kind)
				{
					case ChatbotPlatform.Telegram:
						var tm = root["message"];
						if (S(tm, "chat.type") != "private" || S(tm, "from.is_bot") == "True"
							|| S(tm, "chat.id") != S(tm, "from.id")) return Ok();
						await EnqueueAsync(kind, S(root, "update_id"), S(tm, "from.id"), S(tm, "text"), occurredAt: Epoch(S(tm, "date")));
						break;
					case ChatbotPlatform.Slack:
						if (S(root, "type") == "url_verification") return Ok(new { challenge = S(root, "challenge") });
						if (S(root, "team_id") != ChatbotConfig.SlackTeamId) return Unauthorized();
						var se = root["event"];
						if (S(se, "type") != "message" || S(se, "channel_type") != "im" || se?["subtype"] != null || se?["bot_id"] != null) return Ok();
						await EnqueueAsync(kind, S(root, "event_id"), S(se, "user"), S(se, "text"), occurredAt: Epoch(S(root, "event_time")));
						break;
					case ChatbotPlatform.Discord:
						if (S(root, "type") == "1") return Ok(new { type = 1 });
						if (S(root, "application_id") != ChatbotConfig.DiscordClientId) return Unauthorized();
						// Keep commands and replies in private bot conversations. Group/server content is ignored.
						if (S(root, "type") != "2" || root["guild_id"] != null || S(root, "channel.type") != "1"
							|| S(root, "data.name") != "resgrid")
							return Ok(new { type = 4, data = new { content = "Use /resgrid in a direct message with the Resgrid bot.", flags = 64 } });
						var text = root.SelectToken("data.options")?.Children().FirstOrDefault(x => S(x, "name") == "message");
						await EnqueueAsync(kind, S(root, "id"), S(root, "user.id"), S(text, "value"), occurredAt: SnowflakeTime(S(root, "id")));
						return Ok(new { type = 4, data = new { content = "Your request was received. Resgrid will reply in this direct conversation.", flags = 64 } });
					case ChatbotPlatform.Line:
						foreach (var e in root["events"] as JArray ?? new JArray())
						{
							if (S(e, "source.type") != "user" || S(e, "type") != "message" || S(e, "message.type") != "text") continue;
							await EnqueueAsync(kind, S(e, "webhookEventId") ?? S(e, "message.id"), S(e, "source.userId"), S(e, "message.text"), occurredAt: Epoch(S(e, "timestamp"), true));
						}
						break;
					case ChatbotPlatform.Viber:
						if (S(root, "event") == "message" && S(root, "message.type") == "text")
							await EnqueueAsync(kind, S(root, "message_token"), S(root, "sender.id"), S(root, "message.text"), occurredAt: Epoch(S(root, "timestamp"), true));
						break;
					case ChatbotPlatform.Signal:
						// RECEIVE_WEBHOOK_URL forwards the complete JSON-RPC receive notification.
						if (S(root, "jsonrpc") != "2.0" || S(root, "method") != "receive") return Ok();
						var signalEvent = root.SelectToken("params.result") ?? root["params"];
						if (S(signalEvent, "account") != ChatbotConfig.SignalAccountNumber) return Unauthorized();
						var envelope = signalEvent?["envelope"];
						var data = envelope?["dataMessage"];
						if (data?["message"]?.Type != JTokenType.String || string.IsNullOrWhiteSpace(S(data, "message"))) return Ok();
						if (data["groupInfo"] is { Type: not JTokenType.Null }
							|| envelope["syncMessage"] is { Type: not JTokenType.Null }
							|| envelope["editMessage"] is { Type: not JTokenType.Null }) return Ok();
						if (!Guid.TryParseExact(S(envelope, "sourceUuid"), "D", out var signalUser) || signalUser == Guid.Empty)
							return BadRequest();
						var signalTime = Epoch(S(data, "timestamp"), true);
						if (signalTime != Epoch(S(envelope, "timestamp"), true)) return BadRequest();
						await EnqueueAsync(kind, ChatbotConfig.SignalAccountNumber + ":" + S(data, "timestamp"),
							signalUser.ToString("D"), S(data, "message"), occurredAt: signalTime);
						break;
					case ChatbotPlatform.MicrosoftTeams:
						if (!await _jwt.ValidateTeamsAsync(Header("Authorization"), S(root, "serviceUrl"))) return Unauthorized();
						if (S(root, "channelId") != "msteams" || S(root, "type") != "message"
							|| S(root, "conversation.conversationType") != "personal" || root.SelectToken("conversation.isGroup")?.Value<bool>() == true) return Ok();
						if (!Guid.TryParse(S(root, "channelData.tenant.id"), out var tenantId)
							|| !Guid.TryParse(ChatbotConfig.TeamsTenantId, out var configuredTenant) || tenantId != configuredTenant
							|| !TeamsBotAdapter.IsServiceUrlAllowed(S(root, "serviceUrl"))) return Unauthorized();
						var tenant = tenantId.ToString("D");
						await EnqueueAsync(kind, S(root, "conversation.id") + ":" + S(root, "id"), tenant + ":" + S(root, "from.id"), S(root, "text"),
							new() { ["service_url"] = S(root, "serviceUrl"), ["conversation_id"] = S(root, "conversation.id"),
								["bot_id"] = S(root, "recipient.id"), ["teams_user_id"] = S(root, "from.id"), ["tenant_id"] = tenant }, occurredAt: DateTimeOffset.Parse(S(root, "timestamp"), System.Globalization.CultureInfo.InvariantCulture).UtcDateTime);
						break;
					case ChatbotPlatform.GoogleChat:
						if (!await _jwt.ValidateGoogleChatAsync(Header("Authorization"), ChatbotConfig.GoogleChatAudience)) return Unauthorized();
						if (S(root, "type") != "MESSAGE" || S(root, "space.type") != "DM" || S(root, "user.type") != "HUMAN") return Ok();
						await EnqueueAsync(kind, S(root, "message.name"), S(root, "user.name"), S(root, "message.text"), new() { ["space"] = S(root, "space.name") }, occurredAt: DateTimeOffset.Parse(S(root, "eventTime"), System.Globalization.CultureInfo.InvariantCulture).UtcDateTime);
						break;
				}
				return Ok();
			}
			catch (JsonException) { return BadRequest(); }
			catch (ArgumentException) { return BadRequest(); }
			catch (FormatException) { return BadRequest(); }
			// A failed publish must prompt a provider retry, not acknowledge and lose the user's command.
			catch (Exception) { return StatusCode(503); }
		}

		private async Task<IActionResult> WhatsAppAsync()
		{
			if (!Request.HasFormContentType || string.IsNullOrWhiteSpace(ChatbotConfig.WhatsAppWebhookUrl)) return StatusCode(503);
			var form = await Request.ReadFormAsync();
			if (form.Any(x => x.Value.Count != 1)) return BadRequest();
			var fields = form.ToDictionary(x => x.Key, x => x.Value.ToString());
			if (!new RequestValidator(NumberProviderConfig.TwilioAuthToken).Validate(ChatbotConfig.WhatsAppWebhookUrl, fields, Header("X-Twilio-Signature"))) return Unauthorized();
			if (form["AccountSid"] != NumberProviderConfig.TwilioAccountSid || !form["From"].ToString().StartsWith("whatsapp:+", StringComparison.Ordinal)) return Unauthorized();
			var target = "whatsapp:+" + ChatbotConfig.WhatsAppFromNumber.Replace("whatsapp:", "").TrimStart('+');
			if (form["To"] != target) return Unauthorized();
			if (_registry.GetAdapter(ChatbotPlatform.WhatsApp) is not WhatsAppAdapter transport) return StatusCode(503);
			var created = await transport.GetInboundTimestampAsync(form["MessageSid"], form["From"], form["To"]);
			await EnqueueAsync(ChatbotPlatform.WhatsApp, form["MessageSid"], form["From"].ToString().Substring(10), form["Body"], occurredAt: created);
			return Content("<Response></Response>", "application/xml");
		}
		private async Task EnqueueAsync(ChatbotPlatform platform, string id, string from, string text, Dictionary<string, string> metadata = null, DateTime? occurredAt = null)
		{
			if (string.IsNullOrWhiteSpace(text)) return;
			var eventTime = occurredAt ?? DateTime.UtcNow;
			if (DateTime.UtcNow - eventTime > TimeSpan.FromMinutes(15)) return;
			if (eventTime - DateTime.UtcNow > TimeSpan.FromMinutes(5)) throw new ArgumentException("Invalid event time.");
			if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(from) || from.Length > 200 || id.Length > 500 || text.Length > 8000)
				throw new ArgumentException("Invalid external message.");
			if (!await _queue.EnqueueChatbotMessageAsync(new ChatbotMessageQueueItem
			{ Platform = (int)platform, MessageId = id, From = from, Body = text, PlatformMetadata = metadata, ReceivedAtUtc = eventTime }))
				throw new InvalidOperationException("Unable to enqueue external message.");
		}
		private static DateTime Epoch(string value, bool milliseconds = false)
        {
            if (!long.TryParse(value, out var time)) throw new ArgumentException("Missing event time.");
            return (milliseconds ? DateTimeOffset.FromUnixTimeMilliseconds(time) : DateTimeOffset.FromUnixTimeSeconds(time)).UtcDateTime;
        }
        private static DateTime SnowflakeTime(string id)
        {
            if (!ulong.TryParse(id, out var value)) throw new ArgumentException("Invalid interaction id.");
            return DateTimeOffset.FromUnixTimeMilliseconds((long)(value >> 22) + 1420070400000L).UtcDateTime;
        }
        private string Header(string name) => Request.Headers[name].ToString();
		private static string S(JToken token, string path) => token?.SelectToken(path)?.ToString();
	}
}
