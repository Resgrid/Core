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
using Resgrid.Model;
using Resgrid.Model.Queue;
using Resgrid.Model.Services;
using Resgrid.Providers.Chatbot.Adapters;
using Resgrid.Providers.Chatbot.Interfaces;
using Resgrid.Providers.Chatbot.Services;
using Resgrid.Web.Services.Intake;
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
		private readonly IIntakeChannelsService _intakeChannels;
		private readonly IIntakeService _intake;
		public ChatbotPlatformsController(IQueueService queue, IChatbotAdapterRegistry registry, ChatbotJwtValidator jwt,
			IIntakeChannelsService intakeChannels, IIntakeService intake)
		{ _queue = queue; _registry = registry; _jwt = jwt; _intakeChannels = intakeChannels; _intake = intake; }

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
						// Group, supergroup and channel posts in a chat linked as an intake channel (ChatIntakeRouting); DMs below.
						var telegramIntake = await IntakeAsync(kind, ChatIntakeRouting.Telegram(root));
						if (telegramIntake != null) return telegramIntake;
						var tm = root["message"];
						if (S(tm, "chat.type") != "private" || S(tm, "from.is_bot") == "True"
							|| S(tm, "chat.id") != S(tm, "from.id")) return Ok();
						await EnqueueAsync(kind, S(root, "update_id"), S(tm, "from.id"), S(tm, "text"), occurredAt: Epoch(S(tm, "date")));
						break;
					case ChatbotPlatform.Slack:
						if (S(root, "type") == "url_verification") return Ok(new { challenge = S(root, "challenge") });
						// A linked intake channel may live in any workspace that installed the app (the signature proves the app);
						// everything else keeps the single configured workspace.
						var slackIntake = await IntakeAsync(kind, ChatIntakeRouting.Slack(root));
						if (slackIntake != null) return slackIntake;
						if (S(root, "team_id") != ChatbotConfig.SlackTeamId) return Unauthorized();
						var se = root["event"];
						if (S(se, "type") != "message" || S(se, "channel_type") != "im" || se?["subtype"] != null || se?["bot_id"] != null) return Ok();
						await EnqueueAsync(kind, S(root, "event_id"), S(se, "user"), S(se, "text"), occurredAt: Epoch(S(root, "event_time")));
						break;
					case ChatbotPlatform.Discord:
						if (S(root, "type") == "1") return Ok(new { type = 1 });
						if (S(root, "application_id") != ChatbotConfig.DiscordClientId) return Unauthorized();
						// /resgrid in a server channel linked as an intake channel; any other server use is refused below as before.
						var discordIntake = await IntakeAsync(kind, ChatIntakeRouting.Discord(root));
						if (discordIntake != null) return discordIntake;
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
						// Channel and group chat messages that mention the bot, in a conversation linked as an intake channel.
						if (TeamsBotAdapter.IsServiceUrlAllowed(S(root, "serviceUrl")))
						{
							var teamsIntake = await IntakeAsync(kind, ChatIntakeRouting.Teams(root));
							if (teamsIntake != null) return teamsIntake;
						}
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
						// Messages that mention the app in a space linked as an intake channel.
						var googleIntake = await IntakeAsync(kind, ChatIntakeRouting.GoogleChat(root));
						if (googleIntake != null) return googleIntake;
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
			if (!new RequestValidator(NumberProviderConfig.TwilioAuthToken).Validate(SignedUrl(ChatbotConfig.WhatsAppWebhookUrl), fields, Header("X-Twilio-Signature"))) return Unauthorized();
			if (form["AccountSid"] != NumberProviderConfig.TwilioAccountSid || !form["From"].ToString().StartsWith("whatsapp:+", StringComparison.Ordinal)) return Unauthorized();
			// A WhatsApp sender of the Twilio account that a department linked as an intake channel: the message is intake.
			var whatsAppChannel = await _intakeChannels.ResolveChannelAsync(IntakeChannelTypes.WhatsApp, form["To"].ToString());
			if (whatsAppChannel != null)
			{
				var result = await _intake.ReceiveAsync(new IntakeEnvelope
				{
					ChannelType = IntakeChannelTypes.WhatsApp, IntakeChannelId = whatsAppChannel.IntakeChannelId, ChannelAddress = form["To"].ToString(),
					ProviderMessageId = form["MessageSid"].ToString(), Sender = IntakeChannelAddresses.PhoneNumber(form["From"].ToString()),
					SenderName = form.TryGetValue("ProfileName", out var profileName) ? profileName.ToString() : null,
					Body = form["Body"].ToString(), ReceivedOnUtc = DateTime.UtcNow
				}, HttpContext?.RequestAborted ?? default);
				// 503 so the message is delivered again and intake takes the failed claim over. Twilio retries a 5xx only when the sender's
				// webhook asks for it: REQUIRED operator setup for every WhatsApp sender linked as an intake channel, see
				// ChatbotConfig.WhatsAppWebhookUrl (by default Twilio retries connection failures only, and the message would be lost).
				if (result.State == IntakeMessageStates.Failed) return StatusCode(503);
				if (result.State == IntakeMessageStates.Duplicate && result.Reason == IntakeResultReasons.DuplicateInProgress) return StatusCode(503);
				// A WhatsApp reply inside the conversation window carries no A2P registration cost, so it goes from the linked sender.
				var ack = result.State == IntakeMessageStates.CallCreated ? result.Acknowledgement : null;
				return Content(string.IsNullOrWhiteSpace(ack) ? "<Response></Response>"
					: "<Response><Message>" + System.Security.SecurityElement.Escape(ack) + "</Message></Response>", "application/xml");
			}
			var target = "whatsapp:+" + ChatbotConfig.WhatsAppFromNumber.Replace("whatsapp:", "").TrimStart('+');
			if (form["To"] != target) return Unauthorized();
			if (_registry.GetAdapter(ChatbotPlatform.WhatsApp) is not WhatsAppAdapter transport) return StatusCode(503);
			var created = await transport.GetInboundTimestampAsync(form["MessageSid"], form["From"], form["To"]);
			await EnqueueAsync(ChatbotPlatform.WhatsApp, form["MessageSid"], form["From"].ToString().Substring(10), form["Body"], occurredAt: created);
			return Content("<Response></Response>", "application/xml");
		}
		/// <summary>
		/// Hands a channel / group message from a chat linked as an intake channel to intake (call-intake plan D3; rules on
		/// ChatIntakeRouting). Null when the message is not intake (no message, or the chat is not linked), so the caller keeps its
		/// existing handling. The acknowledgement, when the channel asks for one, answers in the same chat: inline where the platform
		/// takes a reply in the webhook response (Discord, Google Chat, Telegram), through the adapter otherwise (Slack, Teams).
		/// A failed intake answers 503 so the platform redelivers; intake takes the failed claim over.
		/// </summary>
		private async Task<IActionResult> IntakeAsync(ChatbotPlatform platform, ChatIntakeMessage chat)
		{
			if (chat == null)
				return null;

			var channel = await _intakeChannels.ResolveChannelAsync(chat.ChannelType, chat.Address);
			if (channel == null)
				return null;

			var result = await _intake.ReceiveAsync(new IntakeEnvelope
			{
				ChannelType = chat.ChannelType,
				IntakeChannelId = channel.IntakeChannelId,
				ChannelAddress = chat.Address,
				ProviderMessageId = chat.ProviderMessageId,
				Sender = chat.Sender,
				SenderName = chat.SenderName,
				Body = chat.Text,
				ReceivedOnUtc = DateTime.UtcNow
			}, HttpContext?.RequestAborted ?? default);

			// Discord never redelivers an interaction and shows a bare 503 as "This interaction failed": it gets its answer below.
			if (result.State == IntakeMessageStates.Failed && platform != ChatbotPlatform.Discord)
				return StatusCode(503);

			// A redelivery while the first delivery is still creating its call: ask for another delivery rather than acknowledge an
			// attempt that may still fail (Discord never redelivers an interaction, so it gets its answer below).
			if (result.State == IntakeMessageStates.Duplicate && result.Reason == IntakeResultReasons.DuplicateInProgress && platform != ChatbotPlatform.Discord)
				return StatusCode(503);

			var ack = result.State == IntakeMessageStates.CallCreated ? result.Acknowledgement : null;
			switch (platform)
			{
				case ChatbotPlatform.Discord:
					// An interaction always needs an answer; it is shown only to the person who used the command. A duplicate whose first
					// delivery has not saved its call yet is not confirmed: say so, rather than "received" for a request that may still fail.
					var text = ack ?? (result.State == IntakeMessageStates.Failed
						? "Your request could not be recorded. Please send it again."
						: result.State == IntakeMessageStates.Duplicate && result.Reason == IntakeResultReasons.DuplicateInProgress && !result.CallId.HasValue
							? "Your request is still being processed and is not confirmed yet. If it does not show up as a call soon, send it again."
							: result.State == IntakeMessageStates.Duplicate || result.State == IntakeMessageStates.CallCreated
								? "Your request was received." : "This channel is not accepting requests right now.");
					return Ok(new { type = 4, data = new { content = text, flags = 64 } });
				case ChatbotPlatform.GoogleChat:
					return ack == null ? Ok(new { }) : Ok(new { text = ack });
				case ChatbotPlatform.Telegram:
					if (ack == null || !long.TryParse(chat.Reply.GetValueOrDefault("chat_id"), out var chatId) || !long.TryParse(chat.Reply.GetValueOrDefault("message_id"), out var messageId))
						return Ok();
					return Ok(new { method = "sendMessage", chat_id = chatId, text = ack, reply_parameters = new { message_id = messageId } });
				case ChatbotPlatform.Slack:
				case ChatbotPlatform.MicrosoftTeams:
					if (ack != null)
					{
						try
						{
							if (_registry.GetAdapter(platform) is SlackBotAdapter slack)
								await slack.PostToConversationAsync(chat.Reply, ack);
							else if (_registry.GetAdapter(platform) is TeamsBotAdapter teams)
								await teams.PostToConversationAsync(chat.Reply, ack);
						}
						catch (Exception ex)
						{
							// The pending call exists; a reply that cannot be posted (another Slack workspace, an unknown Teams tenant) is not retried.
							Framework.Logging.LogException(ex, $"Intake acknowledgement could not be posted on {platform}.");
						}
					}
					return Ok();
				default:
					return Ok();
			}
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

        /// <summary>
        /// The URL Twilio signed: a connection-override fragment ("#rp=5xx,ct,rt") is Twilio's own setting, never sent and never part of
        /// X-Twilio-Signature, so a configured URL that carries one still validates.
        /// </summary>
        public static string SignedUrl(string configuredUrl)
        {
            if (string.IsNullOrEmpty(configuredUrl))
                return configuredUrl;

            var hash = configuredUrl.IndexOf('#');
            return hash < 0 ? configuredUrl : configuredUrl.Substring(0, hash);
        }
		private static string S(JToken token, string path) => token?.SelectToken(path)?.ToString();
	}
}
