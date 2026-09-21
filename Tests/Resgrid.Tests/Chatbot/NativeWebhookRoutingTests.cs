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
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Chatbot.Models;
using Resgrid.Config;
using Resgrid.Model.Queue;
using Resgrid.Model.Services;
using Resgrid.Providers.Chatbot.Interfaces;
using Resgrid.Providers.Chatbot.Services;
using Resgrid.Web.Services.Controllers;

namespace Resgrid.Tests.Chatbot
{
	[TestFixture]
	[NonParallelizable]
	public class NativeWebhookRoutingTests
	{
		private const string Secret = "native-routing-test-secret";
		private const string MessageText = "LINK A1B2C3 — 🚒";
		private Dictionary<FieldInfo, object> _configuration;

		[SetUp]
		public void Configure()
		{
			_configuration = typeof(ChatbotConfig).GetFields(BindingFlags.Public | BindingFlags.Static)
				.Where(field => !field.IsLiteral && !field.IsInitOnly)
				.ToDictionary(field => field, field => field.GetValue(null));
			ChatbotConfig.SlackSigningSecret = Secret;
			ChatbotConfig.SlackTeamId = "T_EXPECTED";
			ChatbotConfig.LineChannelSecret = Secret;
			ChatbotConfig.ViberBotToken = Secret;
			ChatbotConfig.DiscordClientId = "application-123";
		}

		[TearDown]
		public void RestoreConfiguration()
		{
			foreach (var field in _configuration) field.Key.SetValue(null, field.Value);
		}

		[TestCase(ChatbotPlatform.Slack, "U_NATIVE123", "Ev_NATIVE123")]
		[TestCase(ChatbotPlatform.Line, "U_line-native123", "line-event-123")]
		[TestCase(ChatbotPlatform.Viber, "viber+/native=", "987654321")]
		public async Task Signed_private_messages_queue_native_identity_event_and_time(ChatbotPlatform platform, string sender, string id)
		{
			var time = EventTime();
			var fixture = Controller(platform, Envelope(platform, time));

			var result = await fixture.Controller.Receive(platform.ToString());

			Assert.That(result, Is.TypeOf<OkResult>());
			Assert.That(fixture.Messages, Has.Count.EqualTo(1));
			var queued = fixture.Messages.Single();
			Assert.That(queued.Platform, Is.EqualTo((int)platform));
			Assert.That(queued.From, Is.EqualTo(sender));
			Assert.That(queued.MessageId, Is.EqualTo(id));
			Assert.That(queued.Body, Is.EqualTo(MessageText));
			Assert.That(queued.ReceivedAtUtc, Is.EqualTo(time.UtcDateTime));
			Assert.That(queued.DepartmentId, Is.Zero);
		}

		[TestCase("channel")]
		[TestCase("group")]
		[TestCase("mpim")]
		public async Task Slack_shared_conversations_are_ignored(string channelType)
		{
			var body = Envelope(ChatbotPlatform.Slack, EventTime());
			body["event"]["channel_type"] = channelType;
			var fixture = Controller(ChatbotPlatform.Slack, body);
			Assert.That(await fixture.Controller.Receive("Slack"), Is.TypeOf<OkResult>());
			Assert.That(fixture.Messages, Is.Empty);
		}

		[TestCase("group")]
		[TestCase("room")]
		public async Task Line_shared_conversations_are_ignored(string sourceType)
		{
			var body = Envelope(ChatbotPlatform.Line, EventTime());
			body["events"][0]["source"]["type"] = sourceType;
			var fixture = Controller(ChatbotPlatform.Line, body);
			Assert.That(await fixture.Controller.Receive("Line"), Is.TypeOf<OkResult>());
			Assert.That(fixture.Messages, Is.Empty);
		}

		[Test]
		public async Task Slack_wrong_workspace_is_rejected_even_with_a_valid_signature()
		{
			var body = Envelope(ChatbotPlatform.Slack, EventTime());
			body["team_id"] = "T_OTHER";
			var fixture = Controller(ChatbotPlatform.Slack, body);
			Assert.That(await fixture.Controller.Receive("Slack"), Is.TypeOf<UnauthorizedResult>());
			Assert.That(fixture.Messages, Is.Empty);
		}

		[TestCase("Messenger")]
		[TestCase("FacebookMessenger")]
		[TestCase("12")]
		public async Task Removed_platform_routes_cannot_enqueue_commands(string route)
		{
			var fixture = Controller(ChatbotPlatform.Slack, Envelope(ChatbotPlatform.Slack, EventTime()));
			Assert.That(await fixture.Controller.Receive(route), Is.TypeOf<NotFoundResult>());
			Assert.That(fixture.Messages, Is.Empty);
		}

		[TestCase("delivered", "text")]
		[TestCase("message", "picture")]
		public async Task Viber_non_text_or_non_message_callbacks_are_ignored(string eventType, string messageType)
		{
			var body = Envelope(ChatbotPlatform.Viber, EventTime());
			body["event"] = eventType;
			body["message"]["type"] = messageType;
			var fixture = Controller(ChatbotPlatform.Viber, body);
			Assert.That(await fixture.Controller.Receive("Viber"), Is.TypeOf<OkResult>());
			Assert.That(fixture.Messages, Is.Empty);
		}

		[TestCase(ChatbotPlatform.Slack)]
		[TestCase(ChatbotPlatform.Line)]
		[TestCase(ChatbotPlatform.Viber)]
		public async Task Tampering_with_a_signed_body_is_rejected_before_queueing(ChatbotPlatform platform)
		{
			var fixture = Controller(platform, Envelope(platform, EventTime()));
			using var reader = new StreamReader(fixture.Controller.Request.Body);
			var body = await reader.ReadToEndAsync();
			fixture.Controller.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body + " "));
			Assert.That(await fixture.Controller.Receive(platform.ToString()), Is.TypeOf<UnauthorizedResult>());
			Assert.That(fixture.Messages, Is.Empty);
		}

		[TestCase(ChatbotPlatform.Slack)]
		[TestCase(ChatbotPlatform.Line)]
		[TestCase(ChatbotPlatform.Viber)]
		public async Task A_fresh_signature_does_not_make_an_old_provider_event_current(ChatbotPlatform platform)
		{
			var fixture = Controller(platform, Envelope(platform, EventTime().AddHours(-1)));
			Assert.That(await fixture.Controller.Receive(platform.ToString()), Is.TypeOf<OkResult>());
			Assert.That(fixture.Messages, Is.Empty);
		}

		[Test]
		public async Task Missing_inbound_configuration_does_not_queue_a_signed_event()
		{
			var fixture = Controller(ChatbotPlatform.Slack, Envelope(ChatbotPlatform.Slack, EventTime()), configured: false);
			Assert.That((await fixture.Controller.Receive("Slack") as StatusCodeResult)?.StatusCode, Is.EqualTo(503));
			Assert.That(fixture.Messages, Is.Empty);
		}

		[Test]
		public async Task Discord_signed_private_command_selects_the_message_option_and_native_snowflake_time()
		{
			var time = EventTime();
			var body = DiscordEnvelope(time);
			var fixture = Controller(ChatbotPlatform.Discord, body);
			var result = await fixture.Controller.Receive("Discord") as OkObjectResult;
			Assert.That(result, Is.Not.Null);
			Assert.That(JObject.FromObject(result.Value)["type"].Value<int>(), Is.EqualTo(4));
			Assert.That(fixture.Messages, Has.Count.EqualTo(1));
			var message = fixture.Messages.Single();
			Assert.That(message.Platform, Is.EqualTo((int)ChatbotPlatform.Discord));
			Assert.That(message.From, Is.EqualTo("123456789012345678"));
			Assert.That(message.MessageId, Is.EqualTo((string)body["id"]));
			Assert.That(message.Body, Is.EqualTo(MessageText));
			Assert.That(message.ReceivedAtUtc, Is.EqualTo(time.UtcDateTime));
		}

		[Test]
		public async Task Discord_wrong_application_is_rejected_even_with_a_valid_signature()
		{
			var body = DiscordEnvelope(EventTime());
			body["application_id"] = "other-application";
			var fixture = Controller(ChatbotPlatform.Discord, body);
			Assert.That(await fixture.Controller.Receive("Discord"), Is.TypeOf<UnauthorizedResult>());
			Assert.That(fixture.Messages, Is.Empty);
		}

		[TestCase(true)]
		[TestCase(false)]
		public async Task Discord_guild_and_group_commands_are_ignored(bool guild)
		{
			var body = DiscordEnvelope(EventTime());
			if (guild) body["guild_id"] = "guild-123";
			else body["channel"]["type"] = 3;
			var fixture = Controller(ChatbotPlatform.Discord, body);
			Assert.That(await fixture.Controller.Receive("Discord"), Is.TypeOf<OkObjectResult>());
			Assert.That(fixture.Messages, Is.Empty);
		}

		private static JObject Envelope(ChatbotPlatform platform, DateTimeOffset time) => platform switch
		{
			ChatbotPlatform.Slack => JObject.FromObject(new
			{
				type = "event_callback", team_id = "T_EXPECTED", event_id = "Ev_NATIVE123", event_time = time.ToUnixTimeSeconds(),
				@event = new { type = "message", channel_type = "im", channel = "D_PRIVATE", user = "U_NATIVE123", text = MessageText }
			}),
			ChatbotPlatform.Line => JObject.FromObject(new
			{
				events = new[] { new { type = "message", webhookEventId = "line-event-123", timestamp = time.ToUnixTimeMilliseconds(),
					source = new { type = "user", userId = "U_line-native123" }, message = new { id = "line-message-123", type = "text", text = MessageText } } }
			}),
			ChatbotPlatform.Viber => JObject.FromObject(new
			{
				@event = "message", message_token = 987654321L, timestamp = time.ToUnixTimeMilliseconds(),
				sender = new { id = "viber+/native=" }, message = new { type = "text", text = MessageText }
			}),
			_ => throw new ArgumentOutOfRangeException(nameof(platform))
		};

		private static JObject DiscordEnvelope(DateTimeOffset time) => JObject.FromObject(new
		{
			type = 2, application_id = "application-123",
			id = ((ulong)(time.ToUnixTimeMilliseconds() - 1420070400000L) << 22).ToString(CultureInfo.InvariantCulture),
			channel = new { type = 1 }, user = new { id = "123456789012345678" },
			data = new { name = "resgrid", options = new[] { new { name = "unrelated", value = "must not be used" }, new { name = "message", value = MessageText } } }
		});

		private static (ChatbotPlatformsController Controller, List<ChatbotMessageQueueItem> Messages) Controller(
			ChatbotPlatform platform, JObject payload, bool configured = true)
		{
			var messages = new List<ChatbotMessageQueueItem>();
			var queue = new Mock<IQueueService>();
			queue.Setup(q => q.EnqueueChatbotMessageAsync(It.IsAny<ChatbotMessageQueueItem>(), It.IsAny<CancellationToken>()))
				.Callback<ChatbotMessageQueueItem, CancellationToken>((item, token) => messages.Add(item)).ReturnsAsync(true);
			var adapter = new Mock<IExternalChatbotAdapter>();
			adapter.SetupGet(a => a.IsInboundConfigured).Returns(configured);
			var registry = new Mock<IChatbotAdapterRegistry>();
			registry.Setup(r => r.GetAdapter(platform)).Returns(adapter.Object);
			var context = new DefaultHttpContext();
			var body = payload.ToString(Formatting.None);
			context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
			context.Request.ContentType = "application/json";
			var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
			switch (platform)
			{
				case ChatbotPlatform.Slack:
					context.Request.Headers["X-Slack-Request-Timestamp"] = timestamp;
					context.Request.Headers["X-Slack-Signature"] = "v0=" + HexHash("v0:" + timestamp + ":" + body);
					break;
				case ChatbotPlatform.Line:
					context.Request.Headers["X-Line-Signature"] = Convert.ToBase64String(Hash(body));
					break;
				case ChatbotPlatform.Viber:
					context.Request.Headers["X-Viber-Content-Signature"] = HexHash(body);
					break;
				case ChatbotPlatform.Discord:
					var signed = SignDiscord(timestamp, body);
					ChatbotConfig.DiscordPublicKey = signed.PublicKey;
					context.Request.Headers["X-Signature-Timestamp"] = timestamp;
					context.Request.Headers["X-Signature-Ed25519"] = signed.Signature;
					break;
			}
			return (new ChatbotPlatformsController(queue.Object, registry.Object, new ChatbotJwtValidator())
				{ ControllerContext = new ControllerContext { HttpContext = context } }, messages);
		}

		private static DateTimeOffset EventTime() => DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds());
		private static string HexHash(string body) => Convert.ToHexString(Hash(body)).ToLowerInvariant();
		private static byte[] Hash(string body)
		{
			using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Secret));
			return hmac.ComputeHash(Encoding.UTF8.GetBytes(body));
		}
		private static (string PublicKey, string Signature) SignDiscord(string timestamp, string body)
		{
			// Bind to the provider's modern assembly because the test graph also includes legacy BouncyCastle.
			var keyType = Type.GetType("Org.BouncyCastle.Crypto.Parameters.Ed25519PrivateKeyParameters, BouncyCastle.Cryptography", true);
			var signerType = Type.GetType("Org.BouncyCastle.Crypto.Signers.Ed25519Signer, BouncyCastle.Cryptography", true);
			dynamic key = Activator.CreateInstance(keyType, Enumerable.Range(1, 32).Select(value => (byte)value).ToArray(), 0);
			dynamic signer = Activator.CreateInstance(signerType);
			signer.Init(true, key);
			var bytes = Encoding.UTF8.GetBytes(timestamp + body);
			signer.BlockUpdate(bytes, 0, bytes.Length);
			return (Convert.ToHexString((byte[])key.GeneratePublicKey().GetEncoded()), Convert.ToHexString((byte[])signer.GenerateSignature()));
		}
	}
}
