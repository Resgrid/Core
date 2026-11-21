using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using Resgrid.Config;
using Resgrid.Model;

namespace Resgrid.Web.Services.Intake
{
	/// <summary>A channel / group chat message that may be intake, read from a platform webhook the controller already verified.</summary>
	public sealed class ChatIntakeMessage
	{
		public IntakeChannelTypes ChannelType { get; set; }

		/// <summary>The chat's intake address (IntakeChannel.Address format for the platform).</summary>
		public string Address { get; set; }

		/// <summary>Stable per message across the platform's retries and event types (Slack message + app_mention share it).</summary>
		public string ProviderMessageId { get; set; }

		public string Sender { get; set; }

		public string SenderName { get; set; }

		public string Text { get; set; }

		/// <summary>The message addresses the bot (mention, slash command, reply to the bot).</summary>
		public bool AddressesBot { get; set; }

		/// <summary>What the platform needs to answer in the same chat (acknowledgement).</summary>
		public Dictionary<string, string> Reply { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);
	}

	/// <summary>
	/// Which channel / group chat messages become intake (call-intake-and-ai-destinations-plan.md D3). Direct messages are never
	/// read here: they keep today's chatbot path. The rule, for a chat the department linked as an intake channel:
	/// <list type="bullet">
	/// <item>a linked chat is intake-only: every top-level human message in it is intake, with or without a mention;</item>
	/// <item>a thread reply (Slack thread, Telegram reply to someone else) is intake only when it addresses the bot, so people
	/// discussing a request under it do not create more calls;</item>
	/// <item>bot, edited, joined/left and other system messages are ignored;</item>
	/// <item>platforms that only deliver messages that address the bot in channels (Teams, Google Chat spaces, Discord slash
	/// commands, Telegram groups with privacy mode on) therefore need the mention or command there.</item>
	/// </list>
	/// Unlinked chats are ignored exactly as before (the caller falls through to its existing handling).
	/// </summary>
	public static class ChatIntakeRouting
	{
		private static readonly Regex SlackMention = new Regex(@"<@[UW][A-Z0-9]+(\|[^>]*)?>", RegexOptions.Compiled);
		private static readonly Regex TeamsMention = new Regex(@"<at>.*?</at>", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
		private static readonly Regex Html = new Regex(@"<[^>]{0,200}>", RegexOptions.Compiled);
		private static readonly Regex TelegramCommand = new Regex(@"^/[A-Za-z0-9_]+(@[A-Za-z0-9_]+)?\s*", RegexOptions.Compiled);

		/// <summary>Slack Events API callback: a message (or app_mention) in a public or private channel.</summary>
		public static ChatIntakeMessage Slack(JObject root)
		{
			var e = root?["event"];
			var type = S(e, "type");
			if (e == null || type != "message" && type != "app_mention")
				return null;
			if (e["subtype"] != null || e["bot_id"] != null || string.IsNullOrWhiteSpace(S(e, "user")))
				return null;

			var channel = S(e, "channel");
			var channelType = S(e, "channel_type");
			if (string.IsNullOrWhiteSpace(channel) || channel.StartsWith("D", StringComparison.Ordinal))
				return null;
			if (type == "message" && channelType != "channel" && channelType != "group")
				return null;

			var team = S(root, "team_id") ?? S(e, "team");
			var ts = S(e, "ts");
			var threadTs = S(e, "thread_ts");
			var text = S(e, "text") ?? string.Empty;
			var botUser = S(root, "authorizations[0].user_id");
			var addressesBot = type == "app_mention" || !string.IsNullOrWhiteSpace(botUser) && text.Contains("<@" + botUser, StringComparison.Ordinal);
			var threadReply = !string.IsNullOrWhiteSpace(threadTs) && threadTs != ts;
			if (string.IsNullOrWhiteSpace(team) || string.IsNullOrWhiteSpace(ts) || threadReply && !addressesBot)
				return null;

			var message = new ChatIntakeMessage
			{
				ChannelType = IntakeChannelTypes.Slack,
				Address = team + "/" + channel,
				ProviderMessageId = channel + ":" + ts,
				Sender = S(e, "user"),
				Text = Clean(SlackMention.Replace(text, " ")),
				AddressesBot = addressesBot
			};
			message.Reply["team_id"] = team;
			message.Reply["channel"] = channel;
			message.Reply["thread_ts"] = threadReply ? threadTs : ts;
			return message.Text == null ? null : message;
		}

		/// <summary>Bot Framework activity: a message in a team channel or group chat (Teams delivers only those that mention the bot).</summary>
		public static ChatIntakeMessage Teams(JObject root)
		{
			if (root == null || S(root, "channelId") != "msteams" || S(root, "type") != "message")
				return null;

			var conversationType = S(root, "conversation.conversationType");
			if (conversationType != "channel" && conversationType != "groupChat")
				return null;
			if (!Guid.TryParse(S(root, "channelData.tenant.id") ?? S(root, "conversation.tenantId"), out var tenant))
				return null;

			var conversationId = S(root, "conversation.id");
			var chat = conversationType == "channel"
				? S(root, "channelData.channel.id") ?? IntakeChannelAddresses.TeamsChannelId(conversationId)
				: IntakeChannelAddresses.TeamsChannelId(conversationId);
			if (string.IsNullOrWhiteSpace(chat) || string.IsNullOrWhiteSpace(S(root, "id")) || string.IsNullOrWhiteSpace(S(root, "from.id")))
				return null;

			var text = Clean(Html.Replace(TeamsMention.Replace(S(root, "text") ?? string.Empty, " "), " "));
			var message = new ChatIntakeMessage
			{
				ChannelType = IntakeChannelTypes.Teams,
				Address = tenant.ToString("D") + "/" + chat,
				ProviderMessageId = conversationId + ":" + S(root, "id"),
				Sender = tenant.ToString("D") + ":" + S(root, "from.id"),
				SenderName = S(root, "from.name"),
				Text = text != null ? System.Net.WebUtility.HtmlDecode(text) : null,
				AddressesBot = true
			};
			message.Reply["service_url"] = S(root, "serviceUrl");
			message.Reply["conversation_id"] = conversationId;
			message.Reply["bot_id"] = S(root, "recipient.id");
			message.Reply["tenant_id"] = tenant.ToString("D");
			return message.Text == null ? null : message;
		}

		/// <summary>Telegram update: a group / supergroup message or a channel post.</summary>
		public static ChatIntakeMessage Telegram(JObject root)
		{
			var m = root?["message"] ?? root?["channel_post"];
			var chatType = S(m, "chat.type");
			if (m == null || chatType != "group" && chatType != "supergroup" && chatType != "channel")
				return null;
			if (S(m, "from.is_bot") == "True")
				return null;

			var text = S(m, "text") ?? S(m, "caption");
			if (string.IsNullOrWhiteSpace(text))
				return null;

			var entities = (m["entities"] as JArray ?? m["caption_entities"] as JArray ?? new JArray()).ToList();
			var botId = TelegramBotId(ChatbotConfig.TelegramBotToken);
			// A reply to this bot (by its id, from the token; to any bot only when the id is unknown), a mention of it or a command
			// addressed to it. A mention of someone else, or a command or reply for another bot, does not address this one.
			var replyToBot = botId != null ? S(m, "reply_to_message.from.id") == botId : S(m, "reply_to_message.from.is_bot") == "True";
			var addressesBot = replyToBot || entities.Any(entity => TelegramEntityAddressesBot(entity, text, botId, ChatbotConfig.TelegramBotUsername));
			var reply = m["reply_to_message"] != null && !replyToBot;
			if (reply && !addressesBot)
				return null;

			var chatId = S(m, "chat.id");
			var messageId = S(m, "message_id");
			if (string.IsNullOrWhiteSpace(chatId) || string.IsNullOrWhiteSpace(messageId))
				return null;

			var name = string.Join(" ", new[] { S(m, "from.first_name"), S(m, "from.last_name") }.Where(x => !string.IsNullOrWhiteSpace(x)));
			var message = new ChatIntakeMessage
			{
				ChannelType = IntakeChannelTypes.Telegram,
				Address = chatId,
				ProviderMessageId = chatId + ":" + messageId,
				Sender = S(m, "from.id") ?? S(m, "sender_chat.id") ?? chatId,
				SenderName = string.IsNullOrWhiteSpace(name) ? S(m, "from.username") ?? S(m, "chat.title") : name,
				Text = Clean(TelegramCommand.Replace(text.Trim(), string.Empty)),
				AddressesBot = addressesBot
			};
			message.Reply["chat_id"] = chatId;
			message.Reply["message_id"] = messageId;
			return message.Text == null ? null : message;
		}

		/// <summary>Discord interaction: the /resgrid (or /intake) slash command used in a server channel.</summary>
		public static ChatIntakeMessage Discord(JObject root)
		{
			if (root == null || S(root, "type") != "2" || root["guild_id"] == null || root["guild_id"].Type == JTokenType.Null)
				return null;

			var command = S(root, "data.name");
			if (command != "resgrid" && command != "intake")
				return null;

			var channel = S(root, "channel_id") ?? S(root, "channel.id");
			var option = root.SelectToken("data.options")?.Children().FirstOrDefault(x => S(x, "name") == "message");
			var user = S(root, "member.user.id") ?? S(root, "user.id");
			if (string.IsNullOrWhiteSpace(channel) || string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(S(root, "id")))
				return null;

			return new ChatIntakeMessage
			{
				ChannelType = IntakeChannelTypes.Discord,
				Address = channel,
				ProviderMessageId = S(root, "id"),
				Sender = user,
				SenderName = S(root, "member.user.global_name") ?? S(root, "member.user.username"),
				Text = Clean(S(option, "value")),
				AddressesBot = true
			};
		}

		/// <summary>Google Chat event: a message in a space or group chat (the app receives those that mention it).</summary>
		public static ChatIntakeMessage GoogleChat(JObject root)
		{
			if (root == null || S(root, "type") != "MESSAGE" || S(root, "user.type") != "HUMAN")
				return null;

			var spaceType = S(root, "space.spaceType") ?? S(root, "space.type");
			if (spaceType == null || spaceType == "DM" || spaceType == "DIRECT_MESSAGE")
				return null;

			var space = S(root, "space.name");
			var name = S(root, "message.name");
			if (string.IsNullOrWhiteSpace(space) || string.IsNullOrWhiteSpace(name))
				return null;

			var message = new ChatIntakeMessage
			{
				ChannelType = IntakeChannelTypes.GoogleChat,
				Address = space,
				ProviderMessageId = name,
				Sender = S(root, "user.name"),
				SenderName = S(root, "user.displayName"),
				Text = Clean(S(root, "message.argumentText") ?? S(root, "message.text")),
				AddressesBot = true
			};
			return message.Text == null ? null : message;
		}

		/// <summary>The bot's numeric id: the part of its token before the colon ("123456789:AA…"), or null.</summary>
		public static string TelegramBotId(string token)
		{
			if (string.IsNullOrWhiteSpace(token))
				return null;

			var colon = token.IndexOf(':');
			var id = colon > 0 ? token.Substring(0, colon).Trim() : null;
			return id != null && id.Length > 0 && id.All(char.IsDigit) ? id : null;
		}

		/// <summary>
		/// Whether one message entity addresses this bot: a mention (@name), a text mention of its id, or a bot command with no
		/// "@name" suffix or with this bot's name. With no configured username, a name only counts when it is a bot's (Telegram bot
		/// usernames end in "bot"), so "@anotheruser" never does.
		/// </summary>
		public static bool TelegramEntityAddressesBot(JToken entity, string text, string botId, string botUsername)
		{
			var type = S(entity, "type");
			if (type == "text_mention")
				return botId != null && S(entity, "user.id") == botId;
			if (type != "mention" && type != "bot_command")
				return false;

			if (!int.TryParse(S(entity, "offset"), out var offset) || !int.TryParse(S(entity, "length"), out var length) ||
				offset < 0 || length <= 0 || text == null || offset + length > text.Length)
				return false;

			var value = text.Substring(offset, length);
			string name;
			if (type == "mention")
				name = value.TrimStart('@');
			else
			{
				var at = value.IndexOf('@');
				if (at < 0)
					return true;
				name = value.Substring(at + 1);
			}

			var configured = (botUsername ?? string.Empty).Trim().TrimStart('@');
			return configured.Length > 0
				? string.Equals(name, configured, StringComparison.OrdinalIgnoreCase)
				: name.EndsWith("bot", StringComparison.OrdinalIgnoreCase);
		}

		private static string Clean(string value)
		{
			if (string.IsNullOrWhiteSpace(value))
				return null;

			var text = Regex.Replace(value, @"[ \t]+", " ").Trim();
			return text.Length == 0 ? null : text;
		}

		private static string S(JToken token, string path) => token?.SelectToken(path)?.ToString();
	}
}
