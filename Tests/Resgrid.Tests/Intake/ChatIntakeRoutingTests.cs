using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Chatbot.Models;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Queue;
using Resgrid.Model.Services;
using Resgrid.Providers.Chatbot.Interfaces;
using Resgrid.Providers.Chatbot.Services;
using Resgrid.Web.Services.Controllers;
using Resgrid.Web.Services.Intake;

namespace Resgrid.Tests.Intake
{
	/// <summary>
	/// Chat intake (plan D3): in a chat linked as an intake channel every top-level human message is intake and a thread reply is
	/// intake only when it addresses the bot; DMs and unlinked chats keep today's chatbot behaviour.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class ChatIntakeRoutingTests
	{
		private const string TenantId = "72f988bf-86f1-41af-91ab-2d7cd011db47";

		private static JObject SlackEvent(string type = "message", string channelType = "channel", string channel = "C0123ABCD", string ts = "1700000000.000100",
			string threadTs = null, string text = "Lift assist needed at 12 Oak St", string team = "T0TEAM1", object subtype = null, string botId = null)
		{
			var e = new JObject { ["type"] = type, ["channel"] = channel, ["user"] = "U0PERSON", ["text"] = text, ["ts"] = ts };
			if (channelType != null) e["channel_type"] = channelType;
			if (threadTs != null) e["thread_ts"] = threadTs;
			if (subtype != null) e["subtype"] = JToken.FromObject(subtype);
			if (botId != null) e["bot_id"] = botId;
			return new JObject
			{
				["type"] = "event_callback", ["team_id"] = team, ["event_id"] = "Ev1", ["event_time"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), ["event"] = e,
				["authorizations"] = new JArray(new JObject { ["user_id"] = "U0RESGRID", ["is_bot"] = true })
			};
		}

		[Test]
		public void slack_top_level_channel_message_is_intake()
		{
			var message = ChatIntakeRouting.Slack(SlackEvent());

			message.Should().NotBeNull();
			message.ChannelType.Should().Be(IntakeChannelTypes.Slack);
			message.Address.Should().Be("T0TEAM1/C0123ABCD");
			message.ProviderMessageId.Should().Be("C0123ABCD:1700000000.000100");
			message.Sender.Should().Be("U0PERSON");
			message.Text.Should().Be("Lift assist needed at 12 Oak St");
			message.AddressesBot.Should().BeFalse();
			message.Reply["thread_ts"].Should().Be("1700000000.000100");
		}

		[Test]
		public void slack_thread_reply_without_a_mention_is_ignored_but_with_one_is_intake()
		{
			ChatIntakeRouting.Slack(SlackEvent(ts: "1700000001.000200", threadTs: "1700000000.000100", text: "on it")).Should().BeNull();

			var mentioned = ChatIntakeRouting.Slack(SlackEvent(ts: "1700000001.000200", threadTs: "1700000000.000100", text: "<@U0RESGRID> second patient here"));
			mentioned.Should().NotBeNull();
			mentioned.AddressesBot.Should().BeTrue();
			mentioned.Text.Should().Be("second patient here");
			mentioned.Reply["thread_ts"].Should().Be("1700000000.000100");
		}

		[Test]
		public void slack_app_mention_and_message_events_share_one_provider_id()
		{
			var mention = ChatIntakeRouting.Slack(SlackEvent(type: "app_mention", channelType: null, text: "<@U0RESGRID> fire alarm"));
			var message = ChatIntakeRouting.Slack(SlackEvent(text: "<@U0RESGRID> fire alarm"));

			mention.ProviderMessageId.Should().Be(message.ProviderMessageId);
			mention.AddressesBot.Should().BeTrue();
		}

		[Test]
		public void slack_dms_bots_and_system_messages_are_not_intake()
		{
			ChatIntakeRouting.Slack(SlackEvent(channelType: "im", channel: "D0DM")).Should().BeNull();
			ChatIntakeRouting.Slack(SlackEvent(channelType: "mpim", channel: "G0MPIM")).Should().BeNull();
			ChatIntakeRouting.Slack(SlackEvent(botId: "B0BOT")).Should().BeNull();
			ChatIntakeRouting.Slack(SlackEvent(subtype: "message_changed")).Should().BeNull();
			ChatIntakeRouting.Slack(SlackEvent(subtype: "channel_join")).Should().BeNull();
		}

		private static JObject TeamsActivity(string conversationType = "channel", string text = "<at>Resgrid</at> Patient transfer to Mercy")
		{
			return JObject.FromObject(new
			{
				type = "message", channelId = "msteams", id = "1700000000123", serviceUrl = "https://smba.trafficmanager.net/amer/",
				text, timestamp = DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture),
				from = new { id = "29:user-aad", name = "Dana Nurse" }, recipient = new { id = "28:bot-id" },
				conversation = new { id = "19:abc123@thread.tacv2;messageid=1700000000123", conversationType, tenantId = TenantId },
				channelData = new { tenant = new { id = TenantId }, channel = new { id = "19:abc123@thread.tacv2" }, team = new { id = "19:team@thread.tacv2" } }
			});
		}

		[Test]
		public void teams_channel_mention_is_intake_addressed_by_tenant_and_channel()
		{
			var message = ChatIntakeRouting.Teams(TeamsActivity());

			message.Should().NotBeNull();
			message.Address.Should().Be(TenantId + "/19:abc123@thread.tacv2");
			IntakeChannelAddresses.Normalize(IntakeChannelTypes.Teams, message.Address).Should().Be(message.Address);
			message.Text.Should().Be("Patient transfer to Mercy");
			message.Sender.Should().Be(TenantId + ":29:user-aad");
			message.SenderName.Should().Be("Dana Nurse");
			message.ProviderMessageId.Should().Be("19:abc123@thread.tacv2;messageid=1700000000123:1700000000123");
			message.Reply["conversation_id"].Should().Be("19:abc123@thread.tacv2;messageid=1700000000123");
			message.Reply["bot_id"].Should().Be("28:bot-id");
		}

		[Test]
		public void teams_personal_chats_stay_with_the_chatbot()
		{
			ChatIntakeRouting.Teams(TeamsActivity(conversationType: "personal")).Should().BeNull();
		}

		[Test]
		public void teams_html_and_entities_are_reduced_to_text()
		{
			ChatIntakeRouting.Teams(TeamsActivity(text: "<at>Resgrid</at> <p>Smoke &amp; alarm</p>")).Text.Should().Be("Smoke & alarm");
		}

		[Test]
		public void telegram_group_messages_and_replies()
		{
			var root = JObject.FromObject(new
			{
				update_id = 1,
				message = new { message_id = 55, date = 1700000000, text = "/intake@ResgridBot Water rescue at the dam", chat = new { id = -1001234567890, type = "supergroup", title = "Mutual aid" },
					from = new { id = 4242, is_bot = false, first_name = "Ari" }, entities = new[] { new { type = "bot_command", offset = 0, length = 18 } } }
			});
			var message = ChatIntakeRouting.Telegram(root);
			message.Address.Should().Be("-1001234567890");
			message.Text.Should().Be("Water rescue at the dam");
			message.ProviderMessageId.Should().Be("-1001234567890:55");

			var reply = JObject.FromObject(new
			{
				update_id = 2,
				message = new { message_id = 56, date = 1700000000, text = "copy", chat = new { id = -1001234567890, type = "supergroup" }, from = new { id = 4243, is_bot = false },
					reply_to_message = new { message_id = 55, from = new { id = 4242, is_bot = false } } }
			});
			ChatIntakeRouting.Telegram(reply).Should().BeNull();

			var dm = JObject.FromObject(new { update_id = 3, message = new { message_id = 1, text = "hi", chat = new { id = 4242, type = "private" }, from = new { id = 4242, is_bot = false } } });
			ChatIntakeRouting.Telegram(dm).Should().BeNull();
		}

		[Test]
		public void discord_server_slash_command_is_intake_and_dm_commands_are_not()
		{
			var root = JObject.FromObject(new
			{
				type = 2, id = "1180000000000000000", application_id = "app", guild_id = "900000000000000000", channel_id = "1234567890123456789",
				member = new { user = new { id = "111111111111111111", username = "dispatch" } },
				data = new { name = "resgrid", options = new[] { new { name = "message", value = "Brush fire mile 12" } } }
			});
			var message = ChatIntakeRouting.Discord(root);
			message.Address.Should().Be("1234567890123456789");
			message.Text.Should().Be("Brush fire mile 12");
			message.Sender.Should().Be("111111111111111111");

			root.Remove("guild_id");
			ChatIntakeRouting.Discord(root).Should().BeNull();
		}

		[Test]
		public void google_chat_space_messages_use_the_argument_text()
		{
			var root = JObject.FromObject(new
			{
				type = "MESSAGE", eventTime = DateTimeOffset.UtcNow.ToString("o"), space = new { name = "spaces/AAAAbCdEfGh", type = "ROOM" },
				user = new { name = "users/123", displayName = "Lee", type = "HUMAN" },
				message = new { name = "spaces/AAAAbCdEfGh/messages/abc.def", text = "@Resgrid Chest pain at the gym", argumentText = " Chest pain at the gym" }
			});
			var message = ChatIntakeRouting.GoogleChat(root);
			message.Address.Should().Be("spaces/AAAAbCdEfGh");
			message.Text.Should().Be("Chest pain at the gym");

			root["space"]["type"] = "DM";
			ChatIntakeRouting.GoogleChat(root).Should().BeNull();
		}

		// --- Controller: a linked Slack channel goes to intake; an unlinked one keeps today's behaviour. ---

		private const string SigningSecret = "chat-intake-test-secret";
		private Dictionary<FieldInfo, object> _configuration;

		[SetUp]
		public void Configure()
		{
			_configuration = typeof(ChatbotConfig).GetFields(BindingFlags.Public | BindingFlags.Static)
				.Where(field => !field.IsLiteral && !field.IsInitOnly)
				.ToDictionary(field => field, field => field.GetValue(null));
			ChatbotConfig.SlackSigningSecret = SigningSecret;
			ChatbotConfig.SlackTeamId = "T0TEAM1";
		}

		[TearDown]
		public void Restore()
		{
			foreach (var field in _configuration) field.Key.SetValue(null, field.Value);
		}

		private static (ChatbotPlatformsController Controller, List<ChatbotMessageQueueItem> Queued, Mock<IIntakeService> Intake) SlackController(JObject payload, IntakeChannel linked,
			string linkedAddress = "T0TEAM1/C0123ABCD")
		{
			var queued = new List<ChatbotMessageQueueItem>();
			var queue = new Mock<IQueueService>();
			queue.Setup(q => q.EnqueueChatbotMessageAsync(It.IsAny<ChatbotMessageQueueItem>(), It.IsAny<CancellationToken>()))
				.Callback<ChatbotMessageQueueItem, CancellationToken>((item, _) => queued.Add(item)).ReturnsAsync(true);
			var adapter = new Mock<IExternalChatbotAdapter>();
			adapter.SetupGet(a => a.IsInboundConfigured).Returns(true);
			var registry = new Mock<IChatbotAdapterRegistry>();
			registry.Setup(r => r.GetAdapter(ChatbotPlatform.Slack)).Returns(adapter.Object);
			var channels = new Mock<IIntakeChannelsService>();
			channels.Setup(c => c.ResolveChannelAsync(IntakeChannelTypes.Slack, linkedAddress)).ReturnsAsync(linked);
			var intake = new Mock<IIntakeService>();
			intake.Setup(i => i.ReceiveAsync(It.IsAny<IntakeEnvelope>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new IntakeResult { State = IntakeMessageStates.CallCreated, CallId = 900 });

			var context = new DefaultHttpContext();
			var body = payload.ToString(Formatting.None);
			context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
			context.Request.ContentType = "application/json";
			var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
			using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(SigningSecret));
			context.Request.Headers["X-Slack-Request-Timestamp"] = timestamp;
			context.Request.Headers["X-Slack-Signature"] = "v0=" + Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes("v0:" + timestamp + ":" + body))).ToLowerInvariant();

			return (new ChatbotPlatformsController(queue.Object, registry.Object, new ChatbotJwtValidator(), channels.Object, intake.Object)
				{ ControllerContext = new ControllerContext { HttpContext = context } }, queued, intake);
		}

		[Test]
		public async Task a_message_in_a_linked_slack_channel_becomes_intake_not_a_chatbot_command()
		{
			var fixture = SlackController(SlackEvent(), new IntakeChannel { IntakeChannelId = "sl-1", DepartmentId = 55, ChannelType = (int)IntakeChannelTypes.Slack, IsEnabled = true });

			var result = await fixture.Controller.Receive("Slack");

			result.Should().BeOfType<OkResult>();
			fixture.Queued.Should().BeEmpty();
			fixture.Intake.Verify(i => i.ReceiveAsync(It.Is<IntakeEnvelope>(e => e.ChannelType == IntakeChannelTypes.Slack && e.IntakeChannelId == "sl-1"
				&& e.ProviderMessageId == "C0123ABCD:1700000000.000100" && e.Sender == "U0PERSON" && e.Body == "Lift assist needed at 12 Oak St"), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task a_linked_channel_in_another_workspace_is_accepted_for_intake()
		{
			var fixture = SlackController(SlackEvent(team: "T0OTHER"), new IntakeChannel { IntakeChannelId = "sl-2", IsEnabled = true }, "T0OTHER/C0123ABCD");

			(await fixture.Controller.Receive("Slack")).Should().BeOfType<OkResult>();
			fixture.Intake.Verify(i => i.ReceiveAsync(It.Is<IntakeEnvelope>(e => e.IntakeChannelId == "sl-2"), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task an_unlinked_channel_in_another_workspace_is_still_rejected()
		{
			var fixture = SlackController(SlackEvent(team: "T0OTHER"), null);

			(await fixture.Controller.Receive("Slack")).Should().BeOfType<UnauthorizedResult>();
			fixture.Intake.Verify(i => i.ReceiveAsync(It.IsAny<IntakeEnvelope>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task an_unlinked_slack_channel_keeps_todays_behaviour()
		{
			var fixture = SlackController(SlackEvent(), null);

			(await fixture.Controller.Receive("Slack")).Should().BeOfType<OkResult>();
			fixture.Queued.Should().BeEmpty();
			fixture.Intake.Verify(i => i.ReceiveAsync(It.IsAny<IntakeEnvelope>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task a_slack_dm_still_reaches_the_chatbot()
		{
			var fixture = SlackController(SlackEvent(channelType: "im", channel: "D0DM"), new IntakeChannel { IntakeChannelId = "sl-1", IsEnabled = true });

			await fixture.Controller.Receive("Slack");

			fixture.Queued.Should().ContainSingle(q => q.From == "U0PERSON");
			fixture.Intake.Verify(i => i.ReceiveAsync(It.IsAny<IntakeEnvelope>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task a_failed_intake_asks_slack_to_retry()
		{
			var fixture = SlackController(SlackEvent(), new IntakeChannel { IntakeChannelId = "sl-1", IsEnabled = true });
			fixture.Intake.Setup(i => i.ReceiveAsync(It.IsAny<IntakeEnvelope>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new IntakeResult { State = IntakeMessageStates.Failed });

			var result = await fixture.Controller.Receive("Slack");

			(result as StatusCodeResult)?.StatusCode.Should().Be(503);
		}
	}
}
