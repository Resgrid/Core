using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Chatbot.Models;
using Resgrid.Config;
using Resgrid.Model.Providers;
using Resgrid.Providers.Chatbot.Adapters;
using Resgrid.Providers.Chatbot.Services;

namespace Resgrid.Tests.Chatbot
{
	[TestFixture]
	[NonParallelizable]
	public class NativeChatbotTransportTests
	{
		private Dictionary<FieldInfo, object> _configuration;

		[SetUp]
		public void SaveConfiguration()
		{
			_configuration = new[] { typeof(ChatbotConfig), typeof(NumberProviderConfig) }
				.SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.Static))
				.Where(field => !field.IsLiteral && !field.IsInitOnly)
				.ToDictionary(field => field, field => field.GetValue(null));
		}

		[TearDown]
		public void RestoreConfiguration()
		{
			foreach (var field in _configuration)
				field.Key.SetValue(null, field.Value);
		}

		[Test]
		public async Task Slack_opens_a_private_conversation_and_posts_plain_text()
		{
			ChatbotConfig.SlackBotToken = "test-slack-token";
			ChatbotConfig.SlackTeamId = "T123";
			using var handler = new RecordingHandler("{\"ok\":true,\"channel\":{\"id\":\"D456\"}}", "{\"ok\":true}");
			using var client = new HttpClient(handler);
			await new SlackBotAdapter(new ChatbotHttpClient(client)).SendRichResponseAsync("slack:U123", Response("<@everyone> Station update"));

			Assert.That(handler.Requests.Select(r => r.Url), Is.EqualTo(new[]
				{ "https://slack.com/api/conversations.open", "https://slack.com/api/chat.postMessage" }));
			Assert.That((string)handler.Requests[0].Json["users"], Is.EqualTo("U123"));
			var posted = handler.Requests[1];
			Assert.That(posted.Authorization, Is.EqualTo("Bearer test-slack-token"));
			Assert.That((string)posted.Json["channel"], Is.EqualTo("D456"));
			Assert.That((string)posted.Json["text"], Is.EqualTo("<@everyone> Station update"));
			Assert.That((bool)posted.Json["mrkdwn"], Is.False);
			Assert.That((string)posted.Json["parse"], Is.EqualTo("none"));
			Assert.That((bool)posted.Json["unfurl_links"], Is.False);
		}

		[TestCase(false)]
		[TestCase(true)]
		public void Slack_rejects_API_errors_even_when_HTTP_succeeds(bool openingSucceeded)
		{
			ChatbotConfig.SlackBotToken = "test-slack-token";
			ChatbotConfig.SlackTeamId = "T123";
			using var handler = openingSucceeded
				? new RecordingHandler("{\"ok\":true,\"channel\":{\"id\":\"D456\"}}", "{\"ok\":false,\"error\":\"private-details\"}")
				: new RecordingHandler("{\"ok\":false,\"error\":\"private-details\"}");
			using var client = new HttpClient(handler);
			var error = Assert.ThrowsAsync<InvalidOperationException>(() => new SlackBotAdapter(new ChatbotHttpClient(client))
				.SendRichResponseAsync("U123", Response("Hello")));
			Assert.That(error.Message, Does.Not.Contain("private-details"));
			Assert.That(handler.Requests.Count, Is.EqualTo(openingSucceeded ? 2 : 1));
		}

		[Test]
		public async Task Discord_opens_a_DM_and_disables_mentions()
		{
			ChatbotConfig.DiscordBotToken = "test-discord-token";
			using var handler = new RecordingHandler("{\"id\":\"456\"}", "{\"id\":\"789\"}");
			using var client = new HttpClient(handler);
			await new DiscordBotAdapter(new ChatbotHttpClient(client)).SendRichResponseAsync("discord:123", Response("@everyone <@123>"));
			Assert.That(handler.Requests[0].Url, Is.EqualTo("https://discord.com/api/v10/users/@me/channels"));
			Assert.That((string)handler.Requests[0].Json["recipient_id"], Is.EqualTo("123"));
			Assert.That(handler.Requests[1].Url, Is.EqualTo("https://discord.com/api/v10/channels/456/messages"));
			Assert.That(handler.Requests[1].Authorization, Is.EqualTo("Bot test-discord-token"));
			Assert.That((string)handler.Requests[1].Json["content"], Is.EqualTo("@everyone <@123>"));
			Assert.That((JArray)handler.Requests[1].Json["allowed_mentions"]["parse"], Is.Empty);
		}

		[Test]
		public async Task Telegram_splits_unicode_without_losing_or_breaking_characters()
		{
			ChatbotConfig.TelegramBotToken = "123:test-token";
			using var handler = new RecordingHandler("{\"ok\":true}", "{\"ok\":true}");
			using var client = new HttpClient(handler);
			var text = new string('é', 4095) + "🚒漢字";
			await new TelegramBotAdapter(new ChatbotHttpClient(client)).SendRichResponseAsync("telegram:123", Response(text));
			var chunks = handler.Requests.Select(request => (string)request.Json["text"]).ToArray();
			Assert.That(chunks, Has.Length.EqualTo(2));
			Assert.That(string.Concat(chunks), Is.EqualTo(text));
			foreach (var chunk in chunks)
			{
				Assert.That(chunk.Length, Is.LessThanOrEqualTo(4096));
				Assert.That(char.IsHighSurrogate(chunk[chunk.Length - 1]), Is.False);
				Assert.That(char.IsLowSurrogate(chunk[0]), Is.False);
			}
			Assert.That(handler.Requests.All(r => r.Url == "https://api.telegram.org/bot123:test-token/sendMessage"), Is.True);
			Assert.That((string)handler.Requests[0].Json["chat_id"], Is.EqualTo("123"));
			Assert.That((bool)handler.Requests[0].Json["link_preview_options"]["is_disabled"], Is.True);
		}

		[Test]
		public void Telegram_API_error_is_a_delivery_failure()
		{
			ChatbotConfig.TelegramBotToken = "123:test-token";
			using var handler = new RecordingHandler("{\"ok\":false,\"description\":\"private-details\"}");
			using var client = new HttpClient(handler);
			var error = Assert.ThrowsAsync<InvalidOperationException>(() => new TelegramBotAdapter(new ChatbotHttpClient(client))
				.SendRichResponseAsync("123", Response("Hello")));
			Assert.That(error.Message, Does.Not.Contain("private-details"));
		}

		[Test]
		public async Task WhatsApp_replies_keep_the_channel_prefix_and_send_session_text()
		{
			ConfigureWhatsApp();
			using var handler = new RecordingHandler("{\"sid\":\"SM123\"}");
			using var client = new HttpClient(handler);
			await new WhatsAppAdapter(new ChatbotHttpClient(client)).SendReplyAsync(Inbound("whatsapp:+15551234567"), Response("Call details"));
			var form = handler.Requests.Single().Form;
			Assert.That(form["To"], Is.EqualTo("whatsapp:+15551234567"));
			Assert.That(form["From"], Is.EqualTo("whatsapp:+15557654321"));
			Assert.That(form["Body"], Is.EqualTo("Call details"));
			Assert.That(form.ContainsKey("ContentSid"), Is.False);
			Assert.That(handler.Requests[0].Authorization, Is.EqualTo("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("ACtest:test-token"))));
		}

		[Test]
		public async Task WhatsApp_proactive_notifications_send_one_static_template_without_private_content()
		{
			ConfigureWhatsApp();
			ChatbotConfig.WhatsAppNotificationContentSid = "HXapproved";
			using var handler = new RecordingHandler("{\"sid\":\"SM123\"}");
			using var client = new HttpClient(handler);
			await new WhatsAppAdapter(new ChatbotHttpClient(client)).SendRichResponseAsync("15551234567", Response(new string('x', 4000)));
			Assert.That(handler.Requests, Has.Count.EqualTo(1));
			var form = handler.Requests[0].Form;
			Assert.That(form["ContentSid"], Is.EqualTo("HXapproved"));
			Assert.That(form.ContainsKey("Body"), Is.False);
			Assert.That(form.ContainsKey("ContentVariables"), Is.False);
		}

		[TestCase(false)]
		[TestCase(true)]
		public void WhatsApp_rejects_missing_templates_or_expired_reply_windows(bool reply)
		{
			ConfigureWhatsApp();
			ChatbotConfig.WhatsAppNotificationContentSid = "";
			using var handler = new RecordingHandler();
			using var client = new HttpClient(handler);
			var adapter = new WhatsAppAdapter(new ChatbotHttpClient(client));
			Assert.ThrowsAsync<InvalidOperationException>(() => reply
				? adapter.SendReplyAsync(Inbound("15551234567", DateTime.UtcNow.AddDays(-2)), Response("Hello"))
				: adapter.SendRichResponseAsync("15551234567", Response("Hello")));
			Assert.That(handler.Requests, Is.Empty);
		}

		[Test]
		public async Task Line_pushes_text_to_the_linked_account()
		{
			ChatbotConfig.LineChannelAccessToken = "test-line-token";
			using var handler = new RecordingHandler("{}");
			using var client = new HttpClient(handler);
			await new LineBotAdapter(new ChatbotHttpClient(client)).SendRichResponseAsync("U123", Response("Hello"));
			var request = handler.Requests.Single();
			Assert.That(request.Url, Is.EqualTo("https://api.line.me/v2/bot/message/push"));
			Assert.That(request.Authorization, Is.EqualTo("Bearer test-line-token"));
			Assert.That((string)request.Json["to"], Is.EqualTo("U123"));
			Assert.That((string)request.Json["messages"][0]["text"], Is.EqualTo("Hello"));
		}

		[TestCase(0)]
		[TestCase(6)]
		public async Task Viber_uses_its_token_header_and_checks_provider_status(int status)
		{
			ChatbotConfig.ViberBotToken = "test-viber-token";
			using var handler = new RecordingHandler("{\"status\":" + status + "}");
			using var client = new HttpClient(handler);
			var adapter = new ViberBotAdapter(new ChatbotHttpClient(client));
			if (status == 0) await adapter.SendRichResponseAsync("viber-user", Response("Hello"));
			else Assert.ThrowsAsync<InvalidOperationException>(() => adapter.SendRichResponseAsync("viber-user", Response("Hello")));
			var request = handler.Requests.Single();
			Assert.That(request.Headers["X-Viber-Auth-Token"], Is.EqualTo("test-viber-token"));
			Assert.That((string)request.Json["receiver"], Is.EqualTo("viber-user"));
			Assert.That((string)request.Json["text"], Is.EqualTo("Hello"));
		}

		[Test]
		public async Task Teams_remembers_the_private_route_for_proactive_delivery()
		{
			ConfigureTeams();
			var cache = new Mock<ICacheProvider>();
			string savedRoute = null;
			cache.Setup(c => c.SetStringAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>()))
				.Callback<string, string, TimeSpan>((key, value, ttl) => savedRoute = value).ReturnsAsync(true);
			cache.Setup(c => c.GetStringAsync(It.IsAny<string>())).ReturnsAsync(() => savedRoute);
			using var handler = new RecordingHandler("{\"access_token\":\"teams-access\"}", "{\"id\":\"1\"}", "{\"access_token\":\"teams-access\"}", "{\"id\":\"2\"}");
			using var client = new HttpClient(handler);
			var adapter = new TeamsBotAdapter(new ChatbotHttpClient(client), cache.Object);
			var inbound = Inbound(ChatbotConfig.TeamsTenantId + ":29:member");
			inbound.PlatformMetadata = new Dictionary<string, object>
			{
				["service_url"] = "https://smba.trafficmanager.net/amer/", ["conversation_id"] = "a:conversation",
				["bot_id"] = "28:bot", ["teams_user_id"] = "29:member", ["tenant_id"] = ChatbotConfig.TeamsTenantId
			};
			await adapter.SendReplyAsync(inbound, Response("Reply"));
			await adapter.SendRichResponseAsync(inbound.From, Response("Dispatch"));
			Assert.That(savedRoute, Is.Not.Null);
			Assert.That(handler.Requests, Has.Count.EqualTo(4));
			var request = handler.Requests[3];
			Assert.That(request.Url, Is.EqualTo("https://smba.trafficmanager.net/amer/v3/conversations/a%3Aconversation/activities"));
			Assert.That(request.Authorization, Is.EqualTo("Bearer teams-access"));
			Assert.That((string)request.Json["recipient"]["id"], Is.EqualTo("29:member"));
			Assert.That((string)request.Json["text"], Is.EqualTo("Dispatch"));
			Assert.That((string)request.Json["textFormat"], Is.EqualTo("plain"));
		}

		[Test]
		public void Teams_will_not_send_without_a_saved_private_conversation()
		{
			ConfigureTeams();
			using var handler = new RecordingHandler();
			using var client = new HttpClient(handler);
			var adapter = new TeamsBotAdapter(new ChatbotHttpClient(client), Mock.Of<ICacheProvider>());
			Assert.ThrowsAsync<InvalidOperationException>(() => adapter.SendRichResponseAsync(ChatbotConfig.TeamsTenantId + ":29:member", Response("Dispatch")));
			Assert.That(handler.Requests, Is.Empty);
		}

		[Test]
		public async Task Google_Chat_signs_its_token_request_and_uses_the_saved_private_space()
		{
			using var rsa = RSA.Create(2048);
			ChatbotConfig.GoogleChatServiceAccountEmail = "bot@example.iam.gserviceaccount.com";
			ChatbotConfig.GoogleChatPrivateKey = rsa.ExportPkcs8PrivateKeyPem();
			ChatbotConfig.GoogleChatAudience = "https://resgrid.example/chat";
			var cache = new Mock<ICacheProvider>();
			cache.Setup(c => c.GetStringAsync(It.IsAny<string>())).ReturnsAsync("spaces/private123");
			using var handler = new RecordingHandler("{\"access_token\":\"google-access\"}", "{\"name\":\"spaces/private123/messages/1\"}");
			using var client = new HttpClient(handler);
			await new GoogleChatBotAdapter(new ChatbotHttpClient(client), cache.Object).SendRichResponseAsync("users/123", Response("Dispatch"));
			var assertion = handler.Requests[0].Form["assertion"].Split('.');
			var signature = Convert.FromBase64String(assertion[2].Replace('-', '+').Replace('_', '/').PadRight((assertion[2].Length + 3) / 4 * 4, '='));
			Assert.That(rsa.VerifyData(Encoding.ASCII.GetBytes(assertion[0] + "." + assertion[1]), signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1), Is.True);
			Assert.That(handler.Requests[0].Url, Is.EqualTo("https://oauth2.googleapis.com/token"));
			Assert.That(handler.Requests[1].Url, Is.EqualTo("https://chat.googleapis.com/v1/spaces/private123/messages"));
			Assert.That(handler.Requests[1].Authorization, Is.EqualTo("Bearer google-access"));
			Assert.That((string)handler.Requests[1].Json["text"], Is.EqualTo("Dispatch"));
		}

		[TestCase(HttpStatusCode.TooManyRequests)]
		[TestCase(HttpStatusCode.Unauthorized)]
		public void HTTP_failures_do_not_disclose_provider_body_or_credentials(HttpStatusCode status)
		{
			using var handler = new RecordingHandler("private-provider-body") { Status = status };
			using var client = new HttpClient(handler);
			var error = Assert.ThrowsAsync<InvalidOperationException>(() => new ChatbotHttpClient(client)
				.PostAsync("https://api.example.test/secret-token/send", new { text = "private-content" }, "Bearer secret-token"));
			Assert.That(error.Message, Does.Contain(((int)status).ToString()));
			Assert.That(error.ToString(), Does.Not.Contain("private-provider-body").And.Not.Contain("secret-token").And.Not.Contain("private-content"));
		}

		private static ChatbotResponse Response(string text) => new ChatbotResponse { Text = text };
		private static ChatbotMessage Inbound(string from, DateTime? timestamp = null)
			=> new ChatbotMessage { From = from, Timestamp = timestamp ?? DateTime.UtcNow };
		private static void ConfigureWhatsApp()
		{
			NumberProviderConfig.TwilioAccountSid = "ACtest";
			NumberProviderConfig.TwilioAuthToken = "test-token";
			ChatbotConfig.WhatsAppFromNumber = "whatsapp:+15557654321";
		}
		private static void ConfigureTeams()
		{
			ChatbotConfig.TeamsAppId = "b8d3393e-9d4c-4d26-9e41-1d6273182d35";
			ChatbotConfig.TeamsTenantId = "2fe6143b-7cda-48a5-ab62-9d972e5d1e73";
			ChatbotConfig.TeamsAppPassword = "test-teams-password";
			ChatbotConfig.TeamsServiceUrls = "https://smba.trafficmanager.net/amer/";
		}

		private sealed class RecordedRequest
		{
			public string Url { get; set; }
			public string Authorization { get; set; }
			public string Body { get; set; }
			public Dictionary<string, string> Headers { get; set; }
			public JObject Json => JObject.Parse(Body);
			public Dictionary<string, string> Form => Body.Split('&').Select(field => field.Split('=', 2))
				.ToDictionary(field => WebUtility.UrlDecode(field[0]), field => WebUtility.UrlDecode(field[1]));
		}

		private sealed class RecordingHandler : HttpMessageHandler
		{
			private readonly Queue<string> _responses;
			public RecordingHandler(params string[] responses) { _responses = new Queue<string>(responses); }
			public List<RecordedRequest> Requests { get; } = new List<RecordedRequest>();
			public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
			protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
			{
				Requests.Add(new RecordedRequest
				{
					Url = request.RequestUri.AbsoluteUri, Authorization = request.Headers.Authorization?.ToString(),
					Headers = request.Headers.ToDictionary(header => header.Key, header => string.Join(",", header.Value)),
					Body = await request.Content.ReadAsStringAsync(cancellationToken)
				});
				Assert.That(_responses.Count, Is.GreaterThan(0), "Unexpected additional provider request.");
				return new HttpResponseMessage(Status) { Content = new StringContent(_responses.Dequeue(), Encoding.UTF8, "application/json") };
			}
		}
	}
}
