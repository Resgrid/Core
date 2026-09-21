using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Chatbot.Interfaces;
using Resgrid.Chatbot.Models;
using Resgrid.Chatbot.Services;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Queue;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Providers.Chatbot;
using Resgrid.Providers.Chatbot.Adapters;
using Resgrid.Providers.Chatbot.Interfaces;
using Resgrid.Providers.Chatbot.Services;
using Resgrid.Web.Services.Controllers;

namespace Resgrid.Tests.Chatbot
{
	[TestFixture, NonParallelizable]
	public class SignalMessagingTests
	{
		private const string Account = "+12025550123";
		private const string Sender = "aa519fd1-44bd-4ab3-91c1-322650fd73bf";
		private const string ApiToken = "signal-outbound-test-token-1234567890";
		private const string WebhookSecret = "signal-inbound-test-secret-1234567890";
		private Dictionary<FieldInfo, object> _config;
		private RecordingHandler _handler;
		private HttpClient _client;
		private SignalBotAdapter _adapter;
		private ChatbotAdapterRegistry _registry;
		private Mock<IQueueService> _queue;
		private List<ChatbotMessageQueueItem> _messages;

		[SetUp]
		public void SetUp()
		{
			_config = typeof(ChatbotConfig).GetFields(BindingFlags.Public | BindingFlags.Static)
				.Where(f => !f.IsLiteral && !f.IsInitOnly).ToDictionary(f => f, f => f.GetValue(null));
			ChatbotConfig.SignalBridgeUrl = "https://signal.example.test";
			ChatbotConfig.SignalAccountNumber = Account;
			ChatbotConfig.SignalBridgeApiToken = ApiToken;
			ChatbotConfig.SignalWebhookSecret = WebhookSecret;
			_handler = new();
			_client = new HttpClient(_handler);
			_adapter = new(new ChatbotHttpClient(_client));
			_registry = new(new Lazy<IEnumerable<IChatbotPlatformAdapter>>(() => new[] { _adapter }));
			_queue = new();
			_messages = new();
			_queue.Setup(q => q.EnqueueChatbotMessageAsync(It.IsAny<ChatbotMessageQueueItem>(), It.IsAny<CancellationToken>()))
				.Callback<ChatbotMessageQueueItem, CancellationToken>((item, _) => _messages.Add(item)).ReturnsAsync(true);
		}

		[TearDown]
		public void TearDown()
		{
			_client.Dispose();
			foreach (var field in _config) field.Key.SetValue(null, field.Value);
		}

		[Test]
		public void Provider_module_registers_signal_on_existing_platform_id()
		{
			var builder = new ContainerBuilder();
			builder.RegisterModule<ChatbotProviderModule>();
			using var container = builder.Build();
			var registration = container.ComponentRegistry.Registrations.Single(r => r.Activator.LimitType == typeof(SignalBotAdapter));
			Assert.That(registration.Services.OfType<Autofac.Core.TypedService>().Select(s => s.ServiceType), Does.Contain(typeof(IChatbotPlatformAdapter)));
			Assert.That((int)_adapter.Platform, Is.EqualTo(8));
			Assert.That(_registry.CanInitiateProactively(ChatbotPlatform.Signal), Is.True);
			Assert.That(_adapter.IsInboundConfigured, Is.True);
			Assert.That(ChatbotPlatformCapabilities.ForPlatform(ChatbotPlatform.Signal).MaxMessageLength, Is.EqualTo(_adapter.GetCapabilities().MaxMessageLength));
			Assert.That(ChatbotPlatformCapabilities.ForPlatform(ChatbotPlatform.Signal).SupportsImages, Is.False);
		}

		[TestCase("http://signal.example.test")]
		[TestCase("https://user:password@signal.example.test")]
		[TestCase("https://signal.example.test?token=secret")]
		[TestCase("https://signal.example.test#fragment")]
		[TestCase("https://signal.example.test/redirect")]
		[TestCase("file:///tmp/bridge")]
		[TestCase("")]
		public void Invalid_bridge_configuration_disables_delivery(string url)
		{
			ChatbotConfig.SignalBridgeUrl = url;
			Assert.That(_adapter.IsConfigured, Is.False);
			Assert.That(_registry.CanInitiateProactively(ChatbotPlatform.Signal), Is.False);
		}

		[TestCase("http://127.0.0.1:8088")]
		[TestCase("http://[::1]:8088/")]
		[TestCase("https://signal.example.test/")]
		public void Loopback_or_https_origins_are_supported(string url)
		{
			ChatbotConfig.SignalBridgeUrl = url;
			Assert.That(_adapter.IsConfigured, Is.True);
		}

		[TestCase("")]
		[TestCase("short")]
		[TestCase("test-token-with-newline-1234567890\r\n")]
		public void Missing_or_unsafe_gateway_credentials_disable_send(string token)
		{
			ChatbotConfig.SignalBridgeApiToken = token;
			Assert.That(_adapter.IsConfigured, Is.False);
		}

		[TestCase("group.abc")]
		[TestCase("+12025550999")]
		[TestCase("user.123")]
		[TestCase("00000000-0000-0000-0000-000000000000")]
		public void Outbound_requires_a_linked_uuid_not_a_phone_or_shared_destination(string recipient)
		{
			Assert.ThrowsAsync<ArgumentException>(() => _adapter.SendRichResponseAsync(recipient, new() { Text = "Test" }));
			Assert.That(_handler.Bodies, Is.Empty);
		}

		[Test]
		public async Task Replies_use_configured_bridge_account_and_private_uuid_and_split_unicode_safely()
		{
			var text = new string('x', 1499) + "🚒 more";
			await _adapter.SendReplyAsync(new() { From = Sender, PlatformMetadata = new() { ["url"] = "https://untrusted.test" } }, new() { Text = text });
			Assert.That(_handler.Bodies, Has.Count.EqualTo(2));
			Assert.That(string.Concat(_handler.Bodies.Select(b => b["message"].ToString())), Is.EqualTo(text));
			Assert.That(_handler.Bodies[1]["message"].ToString(), Does.StartWith("🚒"));
			Assert.That(_handler.Urls, Is.All.EqualTo("https://signal.example.test/v2/send"));
			Assert.That(_handler.Authorizations, Is.All.EqualTo("Bearer " + ApiToken));
			foreach (var payload in _handler.Bodies)
			{
				Assert.That(payload["number"].ToString(), Is.EqualTo(Account));
				Assert.That(payload["recipients"].Values<string>(), Is.EqualTo(new[] { Sender }));
				Assert.That(payload["text_mode"].ToString(), Is.EqualTo("normal"));
				Assert.That(payload["notify_self"].Value<bool>(), Is.False);
			}
		}

		[TestCase("{}")]
		[TestCase("[]")]
		[TestCase("[{}]")]
		[TestCase("[null]")]
		[TestCase("[{\"timestamp\":\"1234\"},{\"timestamp\":\"5678\"}]")]
		[TestCase("[{\"timestamp\":\"1234\",\"errors\":[{\"reason\":\"UNREGISTERED_FAILURE\"}]}]")]
		[TestCase("{\"timestamp\":\"0\"}")]
		[TestCase("{\"timestamp\":\"1234\",\"errors\":[{\"reason\":\"UNREGISTERED_FAILURE\"}]}")]
		[TestCase("{\"timestamp\":\"1234\",\"error\":\"sensitive provider error\"}")]
		public void Missing_or_failed_acceptance_is_not_reported_as_success(string response)
		{
			_handler.Response = response;
			var error = Assert.ThrowsAsync<InvalidOperationException>(() => _adapter.SendRichResponseAsync(Sender, new() { Text = "Test" }));
			Assert.That(error.Message, Is.EqualTo("Signal bridge did not accept the message."));
		}

		[Test]
		public async Task Older_bridge_object_response_is_supported()
		{
			_handler.Response = "{\"timestamp\":\"1750000000000\"}";
			await _adapter.SendRichResponseAsync(Sender, new() { Text = "Test" });
			Assert.That(_handler.Bodies, Has.Count.EqualTo(1));
		}

		[TestCase(HttpStatusCode.Unauthorized)]
		[TestCase(HttpStatusCode.TooManyRequests)]
		[TestCase(HttpStatusCode.ServiceUnavailable)]
		public void Rejected_send_does_not_expose_provider_body_or_credentials(HttpStatusCode status)
		{
			_handler.Status = status;
			_handler.Response = "{\"error\":\"private message and secret\"}";
			var error = Assert.ThrowsAsync<InvalidOperationException>(() => _adapter.SendRichResponseAsync(Sender, new() { Text = "Test" }));
			Assert.That(error.Message, Is.EqualTo($"Messaging provider rejected the request (HTTP {(int)status})."));
		}

		[Test]
		public void Other_provider_object_contract_is_preserved()
		{
			var error = Assert.ThrowsAsync<InvalidOperationException>(() => new ChatbotHttpClient(_client)
				.PostAsync("https://other.example.test", new { message = "Test" }));
			Assert.That(error.Message, Is.EqualTo("Messaging provider returned an invalid response."));
		}

		[TestCase(ChatbotOutboundType.Dispatch)]
		[TestCase(ChatbotOutboundType.Message)]
		[TestCase(ChatbotOutboundType.Notification)]
		public async Task Linked_people_receive_broadcasts_without_an_inbound_conversation(ChatbotOutboundType type)
		{
			var identities = new Mock<IChatbotUserIdentityService>();
			identities.Setup(i => i.GetUserIdentitiesAsync("user-1")).ReturnsAsync(new List<ChatbotUserIdentity>
			{ new() { UserId = "user-1", Platform = ChatbotPlatform.Signal, PlatformUserId = Sender, IsActive = true } });
			var policy = new ChatbotDepartmentConfig { IsEnabled = true, ProactiveNotificationsEnabled = true, AllowedPlatforms = "Signal" };
			var config = new Mock<IChatbotDepartmentConfigService>();
			config.Setup(c => c.GetConfigAsync(7, It.IsAny<bool>())).ReturnsAsync(policy);
			var service = new ChatbotOutboundService(identities.Object, _registry, config.Object);
			var message = new ChatbotOutboundMessage { Type = type, Title = "Resgrid notification", Body = "Test details" };
			var result = await service.SendToUserAsync("user-1", 7, message);
			Assert.That(result.DeliveredPlatforms, Is.EqualTo(new[] { "Signal" }));
			Assert.That(_handler.Bodies.Single()["message"].ToString(), Is.EqualTo("Resgrid notification\nTest details"));
			policy.AllowedPlatforms = "Telegram";
			Assert.That((await service.SendToUserAsync("user-1", 7, message)).AnyDelivered, Is.False);
			policy.AllowedPlatforms = "Signal";
			policy.ProactiveNotificationsEnabled = false;
			Assert.That((await service.SendToUserAsync("user-1", 7, message)).AnyDelivered, Is.False);
			Assert.That(_handler.Bodies, Has.Count.EqualTo(1));
		}

		[TestCase(false)]
		[TestCase(true)]
		public async Task Authenticated_private_intake_preserves_uuid_and_original_time(bool subscription)
		{
			var time = DateTimeOffset.UtcNow.AddSeconds(-20).ToUnixTimeMilliseconds();
			var body = Event(time);
			if (subscription) body["params"] = new JObject { ["subscription"] = 0, ["result"] = body["params"] };
			Assert.That(await Receive(body), Is.TypeOf<OkResult>());
			var queued = _messages.Single();
			Assert.That(queued.Platform, Is.EqualTo(8));
			Assert.That(queued.From, Is.EqualTo(Sender));
			Assert.That(queued.MessageId, Is.EqualTo(Account + ":" + time));
			Assert.That(queued.Body, Is.EqualTo("LINK ABC123"));
			Assert.That(queued.ReceivedAtUtc, Is.EqualTo(DateTimeOffset.FromUnixTimeMilliseconds(time).UtcDateTime));
			Assert.That(queued.DepartmentId, Is.Zero);
		}

		[TestCase("")]
		[TestCase("wrong-secret")]
		public async Task Unauthenticated_webhooks_cannot_link_or_execute(string secret)
		{
			Assert.That(await Receive(Event(), secret), Is.TypeOf<UnauthorizedResult>());
			Assert.That(_messages, Is.Empty);
		}

		[Test]
		public async Task Unconfigured_intake_is_unavailable_even_when_outbound_is_configured()
		{
			ChatbotConfig.SignalWebhookSecret = "";
			Assert.That(_adapter.IsInboundConfigured, Is.False);
			Assert.That(((StatusCodeResult)await Receive(Event())).StatusCode, Is.EqualTo(503));
		}

		[Test]
		public async Task Other_bridge_accounts_are_rejected()
		{
			var body = Event();
			body["params"]["account"] = "+12025550999";
			Assert.That(await Receive(body), Is.TypeOf<UnauthorizedResult>());
			Assert.That(_messages, Is.Empty);
		}

		[TestCase("group")]
		[TestCase("sync")]
		[TestCase("receipt")]
		[TestCase("typing")]
		[TestCase("edit")]
		[TestCase("attachment")]
		public async Task Shared_and_nontext_events_do_not_enter_the_command_queue(string kind)
		{
			var body = Event();
			var envelope = (JObject)body["params"]["envelope"];
			if (kind == "group") envelope["dataMessage"]["groupInfo"] = new JObject { ["groupId"] = "private-group-id" };
			else if (kind == "attachment") ((JObject)envelope["dataMessage"]).Remove("message");
			else { envelope.Remove("dataMessage"); envelope[kind + "Message"] = new JObject { ["message"] = "LINK ABC123" }; }
			Assert.That(await Receive(body), Is.TypeOf<OkResult>());
			Assert.That(_messages, Is.Empty);
		}

		[TestCase(null)]
		[TestCase("+12025550999")]
		[TestCase("00000000-0000-0000-0000-000000000000")]
		public async Task Missing_sender_uuid_never_falls_back_to_phone_identity(string sender)
		{
			var body = Event();
			body["params"]["envelope"]["sourceUuid"] = sender;
			body["params"]["envelope"]["sourceNumber"] = "+12025550999";
			Assert.That(await Receive(body), Is.TypeOf<BadRequestResult>());
			Assert.That(_messages, Is.Empty);
		}

		[Test]
		public async Task Replay_and_invalid_timestamps_are_not_queued()
		{
			Assert.That(await Receive(Event(DateTimeOffset.UtcNow.AddHours(-2).ToUnixTimeMilliseconds())), Is.TypeOf<OkResult>());
			Assert.That(await Receive(Event(DateTimeOffset.UtcNow.AddHours(2).ToUnixTimeMilliseconds())), Is.TypeOf<BadRequestResult>());
			var missing = Event();
			((JObject)missing["params"]["envelope"]["dataMessage"]).Remove("timestamp");
			Assert.That(await Receive(missing), Is.TypeOf<BadRequestResult>());
			var inconsistent = Event();
			inconsistent["params"]["envelope"]["timestamp"] = DateTimeOffset.UtcNow.AddDays(-2).ToUnixTimeMilliseconds();
			Assert.That(await Receive(inconsistent), Is.TypeOf<BadRequestResult>());
			Assert.That(_messages, Is.Empty);
		}

		[Test]
		public async Task Queue_failure_is_not_acknowledged_as_success()
		{
			_queue.Setup(q => q.EnqueueChatbotMessageAsync(It.IsAny<ChatbotMessageQueueItem>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
			Assert.That(((StatusCodeResult)await Receive(Event())).StatusCode, Is.EqualTo(503));
		}

		[Test]
		public async Task Private_signal_link_code_binds_the_resgrid_owner_to_the_signal_uuid()
		{
			await Receive(Event());
			var item = _messages.Single();
			var codes = new Mock<IChatbotLinkingCodeRepository>();
			codes.Setup(c => c.GetByCodeAsync("ABC123")).ReturnsAsync(new ChatbotLinkingCode
			{ Id = "code-id", Code = "ABC123", UserId = "user-1", ExpiresAt = DateTime.UtcNow.AddMinutes(5) });
			codes.Setup(c => c.TryConsumeAsync("code-id", 8, Sender, It.IsAny<DateTime>())).ReturnsAsync(true);
			var identities = new Mock<IChatbotUserIdentityService>();
			await new CodeLinkingService(identities.Object, codes.Object).ProcessCodeAsync(item.Body.Substring(5), (ChatbotPlatform)item.Platform, item.From, null);
			identities.Verify(i => i.LinkUserAsync("user-1", ChatbotPlatform.Signal, Sender, null, "code", "ABC123"), Times.Once);
		}

		private Task<IActionResult> Receive(JObject body, string secret = WebhookSecret)
		{
			var context = new DefaultHttpContext();
			context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body.ToString(Formatting.None)));
			context.Request.Headers["X-Resgrid-Signal-Secret"] = secret;
			var controller = new ChatbotPlatformsController(_queue.Object, _registry, new ChatbotJwtValidator())
			{ ControllerContext = new ControllerContext { HttpContext = context } };
			return controller.Receive("Signal");
		}

		private static JObject Event(long? time = null)
		{
			var timestamp = time ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
			return JObject.FromObject(new { jsonrpc = "2.0", method = "receive", @params = new
			{ account = Account, envelope = new { sourceUuid = Sender.ToUpperInvariant(), sourceNumber = (string)null,
				timestamp, dataMessage = new { message = "LINK ABC123", timestamp, groupInfo = (object)null } } } });
		}

		private sealed class RecordingHandler : HttpMessageHandler
		{
			public readonly List<JObject> Bodies = new();
			public readonly List<string> Urls = new();
			public readonly List<string> Authorizations = new();
			public string Response = "[{\"timestamp\":\"1750000000000\"}]";
			public HttpStatusCode Status = HttpStatusCode.Created;
			protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
			{
				Urls.Add(request.RequestUri.ToString());
				Authorizations.Add(request.Headers.Authorization?.ToString());
				Bodies.Add(JObject.Parse(await request.Content.ReadAsStringAsync(cancellationToken)));
				return new HttpResponseMessage(Status) { Content = new StringContent(Response, Encoding.UTF8, "application/json") };
			}
		}
	}
}
