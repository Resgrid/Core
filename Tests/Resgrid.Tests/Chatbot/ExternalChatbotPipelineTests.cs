using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using Resgrid.Chatbot.Interfaces;
using Resgrid.Chatbot.Models;
using Resgrid.Chatbot.Services;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Queue;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Providers.Chatbot.Interfaces;
using Resgrid.Providers.Chatbot.Services;
using Resgrid.Web.Services.Controllers;

namespace Resgrid.Tests.Chatbot
{
    [TestFixture, NonParallelizable]
    public class ExternalChatbotPipelineTests
    {
        private Mock<IChatbotIngressService> _ingress;
        private Mock<IChatbotUserIdentityService> _identities;
        private Mock<IExternalChatbotAdapter> _adapter;
        private Mock<IProtectedProjectionService> _protection;
        private Mock<ICacheProvider> _cache;
        private Mock<IDepartmentsService> _departments;
        private Mock<IDepartmentLockService> _locks;
        private ExternalChatbotMessageProcessor _processor;
        private ChatbotMessageQueueItem _item;
        private ChatbotUserIdentity _identity;
        private Dictionary<string, string> _store;

        [SetUp]
        public void Setup()
        {
            _identity = new ChatbotUserIdentity { Id = "link-1", UserId = "user-1", PlatformUserId = "1234", Platform = ChatbotPlatform.Telegram, IsActive = true };
            _identities = new();
            _identities.Setup(x => x.GetIdentityAsync(ChatbotPlatform.Telegram, "1234")).ReturnsAsync(() => _identity);
            _ingress = new();
            _ingress.Setup(x => x.ProcessMessageAsync(It.IsAny<ChatbotMessage>())).ReturnsAsync(new ChatbotResponse { Text = "Private call details", Processed = true });
            _adapter = new();
            _adapter.SetupGet(x => x.IsConfigured).Returns(true);
            _adapter.Setup(x => x.SendReplyAsync(It.IsAny<ChatbotMessage>(), It.IsAny<ChatbotResponse>())).Returns(Task.CompletedTask);
            var registry = new Mock<IChatbotAdapterRegistry>();
            registry.Setup(x => x.GetAdapter(ChatbotPlatform.Telegram)).Returns(_adapter.Object);
            var rate = new Mock<IChatbotRateLimiter>();
            rate.Setup(x => x.TryAcquireAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(true);
            _departments = new();
            _departments.Setup(x => x.GetAllDepartmentsForUserAsync("user-1")).ReturnsAsync(new List<DepartmentMember> { new() { DepartmentId = 7, IsActive = true } });
            _protection = new();
            _cache = new();
            _store = new();
            var counters = new Dictionary<string, long>();
            _cache.Setup(x => x.IsConnected()).Returns(true);
            _cache.Setup(x => x.GetStringAsync(It.IsAny<string>())).ReturnsAsync((string k) => _store.TryGetValue(k, out var v) ? v : null);
            _cache.Setup(x => x.SetStringAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>()))
                .ReturnsAsync((string k, string v, TimeSpan t) => { _store[k] = v; return true; });
            _cache.Setup(x => x.IncrementAsync(It.IsAny<string>(), It.IsAny<TimeSpan>()))
                .ReturnsAsync((string k, TimeSpan t) => counters[k] = counters.TryGetValue(k, out var v) ? v + 1 : 1);
            var encryption = new Mock<IEncryptionService>();
            encryption.Setup(x => x.Encrypt(It.IsAny<string>())).Returns((string s) => s);
            encryption.Setup(x => x.Decrypt(It.IsAny<string>())).Returns((string s) => s);
            var config = new Mock<IChatbotDepartmentConfigService>();
            config.Setup(x => x.IsChatbotUsableForDepartmentAsync(It.IsAny<int>(), ChatbotPlatform.Telegram)).ReturnsAsync(true);
            var auth = new Mock<IAuthorizationService>();
            auth.Setup(x => x.IsUserValidWithinLimitsAsync("user-1", It.IsAny<int>())).ReturnsAsync(true);
            _locks = new();
            _processor = new(registry.Object, _ingress.Object, new CodeLinkingService(_identities.Object, Mock.Of<IChatbotLinkingCodeRepository>()),
                _identities.Object, rate.Object, _departments.Object, _protection.Object, _cache.Object, encryption.Object, config.Object, auth.Object, _locks.Object);
            _item = new() { Platform = (int)ChatbotPlatform.Telegram, From = "1234", MessageId = "event1", Body = "calls", ReceivedAtUtc = DateTime.UtcNow };
        }

        [Test]
        public async Task Failed_delivery_retries_saved_reply_without_repeating_command()
        {
            _adapter.SetupSequence(x => x.SendReplyAsync(It.IsAny<ChatbotMessage>(), It.IsAny<ChatbotResponse>()))
                .ThrowsAsync(new InvalidOperationException("provider down")).Returns(Task.CompletedTask);
            Assert.ThrowsAsync<InvalidOperationException>(() => _processor.ProcessAsync(_item));
            await _processor.ProcessAsync(_item);
            await _processor.ProcessAsync(_item);
            _ingress.Verify(x => x.ProcessMessageAsync(It.IsAny<ChatbotMessage>()), Times.Once);
            _adapter.Verify(x => x.SendReplyAsync(It.IsAny<ChatbotMessage>(), It.IsAny<ChatbotResponse>()), Times.Exactly(2));
        }

        [TestCase("unlink")]
        [TestCase("relink")]
        [TestCase("switch")]
        [TestCase("protect")]
        [TestCase("lock")]
        public async Task Saved_content_is_withheld_after_access_changes(string change)
        {
            _adapter.SetupSequence(x => x.SendReplyAsync(It.IsAny<ChatbotMessage>(), It.IsAny<ChatbotResponse>()))
                .ThrowsAsync(new InvalidOperationException()).Returns(Task.CompletedTask);
            Assert.ThrowsAsync<InvalidOperationException>(() => _processor.ProcessAsync(_item));
            if (change == "unlink") _identity = null;
            if (change == "relink") _identity.Id = "new-link";
            if (change == "switch") _departments.Setup(x => x.GetAllDepartmentsForUserAsync("user-1"))
                .ReturnsAsync(new List<DepartmentMember> { new() { DepartmentId = 8, IsActive = true } });
            if (change == "protect") _protection.Setup(x => x.IsChannelSanitizedAsync(7, ProtectedDataEgressChannel.ChatPlatform)).ReturnsAsync(true);
            if (change == "lock") _locks.Setup(x => x.IsDepartmentLockedAsync(7)).ReturnsAsync(true);
            await _processor.ProcessAsync(_item);
            _adapter.Verify(x => x.SendReplyAsync(It.IsAny<ChatbotMessage>(), It.Is<ChatbotResponse>(r => r.Text == "Please sign in to Resgrid to continue.")), Times.Once);
            _ingress.Verify(x => x.ProcessMessageAsync(It.IsAny<ChatbotMessage>()), Times.Once);
        }

        [Test]
        public async Task Protected_department_never_runs_external_command()
        {
            _protection.Setup(x => x.IsChannelSanitizedAsync(7, ProtectedDataEgressChannel.ChatPlatform)).ReturnsAsync(true);
            await _processor.ProcessAsync(_item);
            _ingress.Verify(x => x.ProcessMessageAsync(It.IsAny<ChatbotMessage>()), Times.Never);
        }

        [Test]
        public void Cache_outage_does_not_execute_command()
        {
            _cache.Setup(x => x.IsConnected()).Returns(false);
            Assert.ThrowsAsync<InvalidOperationException>(() => _processor.ProcessAsync(_item));
            _ingress.Verify(x => x.ProcessMessageAsync(It.IsAny<ChatbotMessage>()), Times.Never);
        }

        [Test]
        public void Stale_message_does_not_execute_command()
        {
            _item.ReceivedAtUtc = DateTime.UtcNow.AddMinutes(-16);
            Assert.ThrowsAsync<InvalidOperationException>(() => _processor.ProcessAsync(_item));
            _ingress.Verify(x => x.ProcessMessageAsync(It.IsAny<ChatbotMessage>()), Times.Never);
        }

        [Test]
        public async Task Locked_department_never_runs_external_command()
        {
            _locks.Setup(x => x.IsDepartmentLockedAsync(7)).ReturnsAsync(true);
            await _processor.ProcessAsync(_item);
            _ingress.Verify(x => x.ProcessMessageAsync(It.IsAny<ChatbotMessage>()), Times.Never);
        }

        [Test]
        public async Task Checkpointed_reply_remains_deliverable_after_command_expiry()
        {
            _adapter.SetupSequence(x => x.SendReplyAsync(It.IsAny<ChatbotMessage>(), It.IsAny<ChatbotResponse>()))
                .ThrowsAsync(new InvalidOperationException()).Returns(Task.CompletedTask);
            Assert.ThrowsAsync<InvalidOperationException>(() => _processor.ProcessAsync(_item));
            _item.ReceivedAtUtc = DateTime.UtcNow.AddMinutes(-16);
            await _processor.ProcessAsync(_item);
            _ingress.Verify(x => x.ProcessMessageAsync(It.IsAny<ChatbotMessage>()), Times.Once);
            _adapter.Verify(x => x.SendReplyAsync(It.IsAny<ChatbotMessage>(), It.Is<ChatbotResponse>(r => r.Text == "Private call details")), Times.Exactly(2));
        }

        [Test]
        public void Uncertain_execution_is_not_repeated()
        {
            _ingress.Setup(x => x.ProcessMessageAsync(It.IsAny<ChatbotMessage>())).ThrowsAsync(new InvalidOperationException());
            Assert.ThrowsAsync<InvalidOperationException>(() => _processor.ProcessAsync(_item));
            Assert.ThrowsAsync<InvalidOperationException>(() => _processor.ProcessAsync(_item));
            _ingress.Verify(x => x.ProcessMessageAsync(It.IsAny<ChatbotMessage>()), Times.Once);
            _adapter.Verify(x => x.SendReplyAsync(It.IsAny<ChatbotMessage>(), It.IsAny<ChatbotResponse>()), Times.Never);
        }

        [Test]
        public async Task Successful_switch_acknowledges_change_without_new_department_content()
        {
            _ingress.Setup(x => x.ProcessMessageAsync(It.IsAny<ChatbotMessage>())).ReturnsAsync(() =>
            {
                _departments.Setup(x => x.GetAllDepartmentsForUserAsync("user-1"))
                    .ReturnsAsync(new List<DepartmentMember> { new() { DepartmentId = 8, IsActive = true } });
                _protection.Setup(x => x.IsChannelSanitizedAsync(8, ProtectedDataEgressChannel.ChatPlatform)).ReturnsAsync(true);
                return new ChatbotResponse { Text = "Switched to confidential department", Processed = true, DepartmentChanged = true };
            });
            await _processor.ProcessAsync(_item);
            _adapter.Verify(x => x.SendReplyAsync(It.IsAny<ChatbotMessage>(), It.Is<ChatbotResponse>(r => r.Text.StartsWith("Your active department changed.") && !r.Text.Contains("confidential"))), Times.Once);
        }

        [Test]
        public async Task Stop_unlinks_even_when_department_protected()
        {
            _item.Body = "STOP";
            _protection.Setup(x => x.IsChannelSanitizedAsync(7, ProtectedDataEgressChannel.ChatPlatform)).ReturnsAsync(true);
            await _processor.ProcessAsync(_item);
            _identities.Verify(x => x.UnlinkUserAsync("link-1"), Times.Once);
            _ingress.Verify(x => x.ProcessMessageAsync(It.IsAny<ChatbotMessage>()), Times.Never);
        }
    }

    [TestFixture, NonParallelizable]
    public class NativeChatbotWebhookTests
    {
        private string _secret;
        [SetUp] public void Setup() { _secret = ChatbotConfig.TelegramWebhookSecretToken; ChatbotConfig.TelegramWebhookSecretToken = "test-secret"; }
        [TearDown] public void Cleanup() { ChatbotConfig.TelegramWebhookSecretToken = _secret; }

        private static (ChatbotPlatformsController Controller, Mock<IQueueService> Queue) Controller(string body, string secret = "test-secret")
        {
            var queue = new Mock<IQueueService>();
            queue.Setup(x => x.EnqueueChatbotMessageAsync(It.IsAny<ChatbotMessageQueueItem>(), It.IsAny<System.Threading.CancellationToken>())).ReturnsAsync(true);
            var adapter = new Mock<IExternalChatbotAdapter>(); adapter.SetupGet(x => x.IsConfigured).Returns(true); adapter.SetupGet(x => x.IsInboundConfigured).Returns(true);
            var registry = new Mock<IChatbotAdapterRegistry>(); registry.Setup(x => x.GetAdapter(ChatbotPlatform.Telegram)).Returns(adapter.Object);
            var http = new DefaultHttpContext(); http.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
            http.Request.Headers["X-Telegram-Bot-Api-Secret-Token"] = secret;
            return (new ChatbotPlatformsController(queue.Object, registry.Object, new ChatbotJwtValidator())
                { ControllerContext = new ControllerContext { HttpContext = http } }, queue);
        }
        private static string Update(string type = "private", long? date = null) => Newtonsoft.Json.JsonConvert.SerializeObject(new
        { update_id = 7, message = new { from = new { id = 1234, is_bot = false }, chat = new { id = 1234, type }, text = "LINK ABC123", date = date ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds() } });

        [Test]
        public async Task Valid_private_message_is_queued_with_native_identity()
        {
            var (controller, queue) = Controller(Update());
            Assert.That(await controller.Receive("Telegram"), Is.TypeOf<OkResult>());
            queue.Verify(x => x.EnqueueChatbotMessageAsync(It.Is<ChatbotMessageQueueItem>(m => m.From == "1234" && m.Platform == 5 && m.MessageId == "7"), It.IsAny<System.Threading.CancellationToken>()), Times.Once);
        }
        [TestCase("bad-secret", "private")]
        [TestCase("test-secret", "group")]
        public async Task Forged_or_shared_messages_never_reach_queue(string secret, string type)
        {
            var (controller, queue) = Controller(Update(type), secret);
            await controller.Receive("Telegram");
            queue.Verify(x => x.EnqueueChatbotMessageAsync(It.IsAny<ChatbotMessageQueueItem>(), It.IsAny<System.Threading.CancellationToken>()), Times.Never);
        }
        [Test]
        public async Task Old_provider_event_is_not_freshened_at_receipt()
        {
            var (controller, queue) = Controller(Update(date: DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeSeconds()));
            Assert.That(await controller.Receive("Telegram"), Is.TypeOf<OkResult>());
            queue.Verify(x => x.EnqueueChatbotMessageAsync(It.IsAny<ChatbotMessageQueueItem>(), It.IsAny<System.Threading.CancellationToken>()), Times.Never);
        }
        [Test]
        public async Task Failed_publish_returns_retryable_status()
        {
            var (controller, queue) = Controller(Update());
            queue.Setup(x => x.EnqueueChatbotMessageAsync(It.IsAny<ChatbotMessageQueueItem>(), It.IsAny<System.Threading.CancellationToken>())).ThrowsAsync(new InvalidOperationException());
            Assert.That((await controller.Receive("Telegram") as StatusCodeResult)?.StatusCode, Is.EqualTo(503));
        }
    }
}
