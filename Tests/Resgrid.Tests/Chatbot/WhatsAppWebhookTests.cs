using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Chatbot.Models;
using Resgrid.Config;
using Resgrid.Model.Queue;
using Resgrid.Model.Services;
using Resgrid.Providers.Chatbot.Adapters;
using Resgrid.Providers.Chatbot.Interfaces;
using Resgrid.Providers.Chatbot.Services;
using Resgrid.Web.Services.Controllers;
using Twilio.Security;

namespace Resgrid.Tests.Chatbot
{
    [TestFixture, NonParallelizable]
    public class WhatsAppWebhookTests
    {
        private const string AccountSid = "ACaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        private const string MessageSid = "SMbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        private const string From = "whatsapp:+15551234567";
        private const string To = "whatsapp:+15557654321";
        private const string PublicUrl = "https://api.example.test/api/v4/ChatbotPlatforms/WhatsApp?source=wa";
        private string _originalAccountSid;
        private string _originalAuthToken;
        private string _originalFromNumber;
        private string _originalWebhookUrl;
        private DateTime _created;
        private LookupHandler _handler;
        private HttpClient _client;
        private Mock<IQueueService> _queue;

        [SetUp]
        public void Setup()
        {
            _originalAccountSid = NumberProviderConfig.TwilioAccountSid;
            _originalAuthToken = NumberProviderConfig.TwilioAuthToken;
            _originalFromNumber = ChatbotConfig.WhatsAppFromNumber;
            _originalWebhookUrl = ChatbotConfig.WhatsAppWebhookUrl;
            NumberProviderConfig.TwilioAccountSid = AccountSid;
            NumberProviderConfig.TwilioAuthToken = "test-whatsapp-auth-token";
            ChatbotConfig.WhatsAppFromNumber = "+15557654321";
            ChatbotConfig.WhatsAppWebhookUrl = PublicUrl;

            _created = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.AddMinutes(-2).ToUnixTimeSeconds()).UtcDateTime;
            _handler = new LookupHandler
            {
                Resource = new JObject
                {
                    ["sid"] = MessageSid,
                    ["account_sid"] = AccountSid,
                    ["direction"] = "inbound",
                    ["from"] = From,
                    ["to"] = To,
                    ["date_created"] = _created.ToString("ddd, dd MMM yyyy HH:mm:ss +0000", CultureInfo.InvariantCulture)
                }
            };
            _client = new HttpClient(_handler);
            _queue = new Mock<IQueueService>();
            _queue.Setup(x => x.EnqueueChatbotMessageAsync(It.IsAny<ChatbotMessageQueueItem>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
        }

        [TearDown]
        public void Cleanup()
        {
            _client?.Dispose();
            NumberProviderConfig.TwilioAccountSid = _originalAccountSid;
            NumberProviderConfig.TwilioAuthToken = _originalAuthToken;
            ChatbotConfig.WhatsAppFromNumber = _originalFromNumber;
            ChatbotConfig.WhatsAppWebhookUrl = _originalWebhookUrl;
        }

        [Test]
        public async Task Signed_current_message_queues_native_sender_with_original_provider_time()
        {
            var controller = await CreateControllerAsync();
            var result = await controller.Receive("WhatsApp");

            Assert.That(result, Is.TypeOf<ContentResult>());
            Assert.That(((ContentResult)result).Content, Is.EqualTo("<Response></Response>"));
            _queue.Verify(x => x.EnqueueChatbotMessageAsync(It.Is<ChatbotMessageQueueItem>(item =>
                item.Platform == (int)ChatbotPlatform.WhatsApp && item.From == "15551234567"
                && item.MessageId == MessageSid && item.Body == "calls"
                && item.ReceivedAtUtc == _created && item.ReceivedAtUtc.Kind == DateTimeKind.Utc),
                It.IsAny<CancellationToken>()), Times.Once);
            Assert.That(_handler.RequestCount, Is.EqualTo(1));
            Assert.That(_handler.Method, Is.EqualTo(HttpMethod.Get));
            Assert.That(_handler.Url, Is.EqualTo($"https://api.twilio.com/2010-04-01/Accounts/{AccountSid}/Messages/{MessageSid}.json"));
            Assert.That(_handler.Authorization, Is.EqualTo("Basic " + Convert.ToBase64String(
                Encoding.UTF8.GetBytes(AccountSid + ":test-whatsapp-auth-token"))));
            Assert.That(_handler.HasContent, Is.False);
        }

        [Test]
        public async Task Old_signed_replay_is_not_freshened_after_the_deduplication_window()
        {
            _handler.Resource["date_created"] = DateTime.UtcNow.AddHours(-25).ToString("r", CultureInfo.InvariantCulture);
            var controller = await CreateControllerAsync();

            Assert.That(await controller.Receive("WhatsApp"), Is.TypeOf<ContentResult>());
            Assert.That(_handler.RequestCount, Is.EqualTo(1));
            VerifyNoEnqueue();
        }

        [TestCase("sid", "SMcccccccccccccccccccccccccccccccc")]
        [TestCase("account_sid", "ACcccccccccccccccccccccccccccccccc")]
        [TestCase("direction", "outbound-api")]
        [TestCase("from", "whatsapp:+15559999999")]
        [TestCase("to", "whatsapp:+15558888888")]
        [TestCase("date_created", "not-a-date")]
        [TestCase("date_created", null)]
        public async Task Unverified_message_resource_fails_closed(string field, string value)
        {
            if (value == null) _handler.Resource.Remove(field);
            else _handler.Resource[field] = value;
            var controller = await CreateControllerAsync();

            Assert.That((await controller.Receive("WhatsApp") as StatusCodeResult)?.StatusCode, Is.EqualTo(503));
            Assert.That(_handler.RequestCount, Is.EqualTo(1));
            VerifyNoEnqueue();
        }

        [Test]
        public async Task Invalid_signature_never_looks_up_message_or_enqueues_command()
        {
            var controller = await CreateControllerAsync(validSignature: false);

            Assert.That(await controller.Receive("WhatsApp"), Is.TypeOf<UnauthorizedResult>());
            Assert.That(_handler.RequestCount, Is.Zero);
            VerifyNoEnqueue();
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task Lookup_failure_returns_retryable_status_without_enqueueing(bool connectionFailure)
        {
            _handler.ConnectionFailure = connectionFailure;
            _handler.Status = HttpStatusCode.ServiceUnavailable;
            var controller = await CreateControllerAsync();

            Assert.That((await controller.Receive("WhatsApp") as StatusCodeResult)?.StatusCode, Is.EqualTo(503));
            Assert.That(_handler.RequestCount, Is.EqualTo(1));
            VerifyNoEnqueue();
        }

        private void VerifyNoEnqueue() => _queue.Verify(x => x.EnqueueChatbotMessageAsync(
            It.IsAny<ChatbotMessageQueueItem>(), It.IsAny<CancellationToken>()), Times.Never);

        private async Task<ChatbotPlatformsController> CreateControllerAsync(bool validSignature = true)
        {
            var fields = new Dictionary<string, string>
            {
                ["AccountSid"] = AccountSid,
                ["MessageSid"] = MessageSid,
                ["From"] = From,
                ["To"] = To,
                ["Body"] = "calls"
            };
            var context = new DefaultHttpContext();
            // A reverse proxy's internal URL must not replace the configured public signing URL.
            context.Request.Scheme = "http";
            context.Request.Host = new HostString("internal.example.test");
            context.Request.Path = "/api/v4/ChatbotPlatforms/WhatsApp";
            context.Request.Method = "POST";
            using var content = new FormUrlEncodedContent(fields);
            var body = await content.ReadAsByteArrayAsync();
            context.Request.ContentType = "application/x-www-form-urlencoded";
            context.Request.ContentLength = body.Length;
            context.Request.Body = new MemoryStream(body);
            context.Request.Headers["X-Twilio-Signature"] = validSignature ? Sign(fields) : "invalid-signature";

            var adapter = new WhatsAppAdapter(new ChatbotHttpClient(_client));
            var registry = new Mock<IChatbotAdapterRegistry>();
            registry.Setup(x => x.GetAdapter(ChatbotPlatform.WhatsApp)).Returns(adapter);
            return new ChatbotPlatformsController(_queue.Object, registry.Object, new ChatbotJwtValidator())
                { ControllerContext = new ControllerContext { HttpContext = context } };
        }

        private static string Sign(IDictionary<string, string> fields)
        {
            // Twilio 7.9.1 keeps its signing helper private. Use that SDK implementation to
            // produce realistic fixtures instead of duplicating its URL/form canonicalization.
            var signingMethod = typeof(RequestValidator).GetMethod("GetValidationSignature", BindingFlags.Instance | BindingFlags.NonPublic,
                null, new[] { typeof(string), typeof(IDictionary<string, string>) }, null);
            Assert.That(signingMethod, Is.Not.Null, "The installed Twilio SDK signing helper changed.");
            return (string)signingMethod.Invoke(new RequestValidator(NumberProviderConfig.TwilioAuthToken), new object[] { PublicUrl, fields });
        }

        private sealed class LookupHandler : HttpMessageHandler
        {
            public JObject Resource { get; set; }
            public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
            public bool ConnectionFailure { get; set; }
            public int RequestCount { get; private set; }
            public HttpMethod Method { get; private set; }
            public string Url { get; private set; }
            public string Authorization { get; private set; }
            public bool HasContent { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                RequestCount++;
                Method = request.Method;
                Url = request.RequestUri.AbsoluteUri;
                Authorization = request.Headers.Authorization?.ToString();
                HasContent = request.Content != null;
                if (ConnectionFailure) throw new HttpRequestException("Simulated lookup connection failure.");
                return Task.FromResult(new HttpResponseMessage(Status)
                    { Content = new StringContent(Resource.ToString(), Encoding.UTF8, "application/json") });
            }
        }
    }
}
