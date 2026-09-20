using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Chatbot.Interfaces;
using Resgrid.Chatbot.Models;
using Resgrid.Model;
using Resgrid.Providers.Chatbot.Adapters;
using Resgrid.Providers.Chatbot.Interfaces;
using Resgrid.Providers.Chatbot.Services;

namespace Resgrid.Tests.Chatbot
{
	/// <summary>
	/// Tests for the Phase 3 outbound delivery channel: the adapter registry's proactive-initiation
	/// rules and the outbound service's department/platform gating + per-platform isolation.
	/// </summary>
	[TestFixture]
	public class ChatbotOutboundTests
	{
		private static ChatbotUserIdentity Identity(ChatbotPlatform platform, string platformUserId = "ext-1", bool isActive = true) =>
			new ChatbotUserIdentity { UserId = "user-1", Platform = platform, PlatformUserId = platformUserId, IsActive = isActive };

		private static ChatbotOutboundMessage DispatchMsg() =>
			new ChatbotOutboundMessage { Type = ChatbotOutboundType.Dispatch, Title = "Call Structure Fire", Body = "123 Main St", ReferenceId = "5" };

		// ---- ChatbotAdapterRegistry ----

		[Test]
		public void Registry_UnregisteredPlatforms_CannotInitiateProactively()
		{
			var registry = new ChatbotAdapterRegistry(new Lazy<IEnumerable<IChatbotPlatformAdapter>>(() => new List<IChatbotPlatformAdapter>()));

			foreach (var platform in Enum.GetValues<ChatbotPlatform>())
				registry.CanInitiateProactively(platform).Should().BeFalse($"{platform} has no registered adapter");
		}

		[TestCase(true)]
		[TestCase(false)]
		public void Registry_RegisteredExternalAdapter_HonorsProactiveCapability(bool canInitiate)
		{
			var slack = new Mock<IExternalChatbotAdapter>();
			slack.SetupGet(a => a.Platform).Returns(ChatbotPlatform.Slack);
			slack.SetupGet(a => a.IsConfigured).Returns(canInitiate);
			slack.SetupGet(a => a.CanInitiateProactively).Returns(canInitiate);
			var registry = new ChatbotAdapterRegistry(new Lazy<IEnumerable<IChatbotPlatformAdapter>>(() =>
				new[] { slack.Object }));

			registry.CanInitiateProactively(ChatbotPlatform.Slack).Should().Be(canInitiate);
			registry.CanInitiateProactively(ChatbotPlatform.Signal).Should().BeFalse();
		}

		[Test]
		public void Registry_RegisteredWebChat_CanInitiateProactively()
		{
			var webChat = new WebChatAdapter(Mock.Of<IChatbotWebChatNotifier>());
			var registry = new ChatbotAdapterRegistry(new Lazy<IEnumerable<IChatbotPlatformAdapter>>(() =>
				new[] { webChat }));

			registry.CanInitiateProactively(ChatbotPlatform.WebChat).Should().BeTrue();
		}

		[Test]
		public void Registry_GetAdapter_ResolvesByPlatform()
		{
			var slack = new SlackBotAdapter();
			var registry = new ChatbotAdapterRegistry(new Lazy<IEnumerable<IChatbotPlatformAdapter>>(() => new List<IChatbotPlatformAdapter> { slack }));

			registry.GetAdapter(ChatbotPlatform.Slack).Should().BeSameAs(slack);
			registry.GetAdapter(ChatbotPlatform.Discord).Should().BeNull();
		}

		[Test]
		public void Registry_DoesNotResolveAdaptersUntilFirstGetAdapter()
		{
			// Guards the circular-dependency fix: constructing the registry (which happens inside the
			// CommunicationService -> ChatbotOutboundService activation chain) must not materialize the
			// adapters, because WebChatAdapter's dependency graph loops back through CallsService.
			var resolved = false;
			var registry = new ChatbotAdapterRegistry(new Lazy<IEnumerable<IChatbotPlatformAdapter>>(() =>
			{
				resolved = true;
				return new List<IChatbotPlatformAdapter>();
			}));

			resolved.Should().BeFalse();
			registry.GetAdapter(ChatbotPlatform.Slack);
			resolved.Should().BeTrue();
		}

		// ---- ChatbotOutboundService ----

		private static (Mock<IChatbotUserIdentityService> identities, Mock<IChatbotDepartmentConfigService> config, Mock<IChatbotAdapterRegistry> registry)
			Mocks(List<ChatbotUserIdentity> identities, ChatbotDepartmentConfig config)
		{
			var idMock = new Mock<IChatbotUserIdentityService>();
			idMock.Setup(i => i.GetUserIdentitiesAsync(It.IsAny<string>())).ReturnsAsync(identities);

			var cfgMock = new Mock<IChatbotDepartmentConfigService>();
			cfgMock.Setup(c => c.GetConfigAsync(It.IsAny<int>(), It.IsAny<bool>())).ReturnsAsync(config);

			var regMock = new Mock<IChatbotAdapterRegistry>();
			return (idMock, cfgMock, regMock);
		}

		[Test]
		public async Task Outbound_ProactiveDisabled_DoesNotSend()
		{
			var (id, cfg, reg) = Mocks(
				new List<ChatbotUserIdentity> { Identity(ChatbotPlatform.Slack) },
				new ChatbotDepartmentConfig { IsEnabled = true, ProactiveNotificationsEnabled = false, AllowedPlatforms = "*" });

			var service = new ChatbotOutboundService(id.Object, reg.Object, cfg.Object);
			var result = await service.SendToUserAsync("user-1", 1, DispatchMsg());

			result.AnyDelivered.Should().BeFalse();
			reg.Verify(r => r.GetAdapter(It.IsAny<ChatbotPlatform>()), Times.Never);
		}

		[Test]
		public async Task Outbound_DeferredPlatform_NotDelivered()
		{
			var (id, cfg, reg) = Mocks(
				new List<ChatbotUserIdentity> { Identity(ChatbotPlatform.Telegram) },
				new ChatbotDepartmentConfig { IsEnabled = true, ProactiveNotificationsEnabled = true, AllowedPlatforms = "*" });
			reg.Setup(r => r.CanInitiateProactively(ChatbotPlatform.Telegram)).Returns(false);

			var service = new ChatbotOutboundService(id.Object, reg.Object, cfg.Object);
			var result = await service.SendToUserAsync("user-1", 1, DispatchMsg());

			result.AnyDelivered.Should().BeFalse();
			reg.Verify(r => r.GetAdapter(It.IsAny<ChatbotPlatform>()), Times.Never);
		}

		[Test]
		public async Task Outbound_SmsIdentity_Skipped()
		{
			var (id, cfg, reg) = Mocks(
				new List<ChatbotUserIdentity> { Identity(ChatbotPlatform.SmsTwilio) },
				new ChatbotDepartmentConfig { IsEnabled = true, ProactiveNotificationsEnabled = true, AllowedPlatforms = "*" });

			var service = new ChatbotOutboundService(id.Object, reg.Object, cfg.Object);
			var result = await service.SendToUserAsync("user-1", 1, DispatchMsg());

			result.AnyDelivered.Should().BeFalse();
			// SMS is handled by the dedicated SMS channel; the chat outbound must not touch it.
			reg.Verify(r => r.CanInitiateProactively(It.IsAny<ChatbotPlatform>()), Times.Never);
		}

		[Test]
		public async Task Outbound_InactiveIdentity_Skipped()
		{
			var (id, cfg, reg) = Mocks(
				new List<ChatbotUserIdentity> { Identity(ChatbotPlatform.Slack, isActive: false) },
				new ChatbotDepartmentConfig { IsEnabled = true, ProactiveNotificationsEnabled = true, AllowedPlatforms = "*" });

			var service = new ChatbotOutboundService(id.Object, reg.Object, cfg.Object);
			var result = await service.SendToUserAsync("user-1", 1, DispatchMsg());

			result.AnyDelivered.Should().BeFalse();
		}

		[Test]
		public async Task Outbound_PlatformNotInAllowedList_Skipped()
		{
			var (id, cfg, reg) = Mocks(
				new List<ChatbotUserIdentity> { Identity(ChatbotPlatform.Slack) },
				new ChatbotDepartmentConfig { IsEnabled = true, ProactiveNotificationsEnabled = true, AllowedPlatforms = "Discord,Telegram" });
			reg.Setup(r => r.CanInitiateProactively(ChatbotPlatform.Slack)).Returns(true);

			var service = new ChatbotOutboundService(id.Object, reg.Object, cfg.Object);
			var result = await service.SendToUserAsync("user-1", 1, DispatchMsg());

			result.AnyDelivered.Should().BeFalse();
		}

		[Test]
		public async Task Outbound_AllowedInitiablePlatform_Delivers()
		{
			var (id, cfg, reg) = Mocks(
				new List<ChatbotUserIdentity> { Identity(ChatbotPlatform.Slack, platformUserId: "U123") },
				new ChatbotDepartmentConfig { IsEnabled = true, ProactiveNotificationsEnabled = true, AllowedPlatforms = "*" });
			reg.Setup(r => r.CanInitiateProactively(ChatbotPlatform.Slack)).Returns(true);

			var adapter = new Mock<IChatbotPlatformAdapter>();
			adapter.Setup(a => a.SendRichResponseAsync("U123", It.IsAny<ChatbotResponse>())).Returns(Task.CompletedTask);
			reg.Setup(r => r.GetAdapter(ChatbotPlatform.Slack)).Returns(adapter.Object);

			var service = new ChatbotOutboundService(id.Object, reg.Object, cfg.Object);
			var result = await service.SendToUserAsync("user-1", 1, DispatchMsg());

			result.AnyDelivered.Should().BeTrue();
			result.DeliveredPlatforms.Should().Contain("Slack");
			adapter.Verify(a => a.SendRichResponseAsync("U123", It.IsAny<ChatbotResponse>()), Times.Once);
		}

		[Test]
		public async Task Outbound_NoConfig_PreservesWebChatDelivery()
		{
			// A department with no config row: defaults apply, chatbot stays enabled (backward compatible).
			var (id, cfg, reg) = Mocks(
				new List<ChatbotUserIdentity> { Identity(ChatbotPlatform.WebChat, platformUserId: "conn-1") },
				config: null);
			reg.Setup(r => r.CanInitiateProactively(ChatbotPlatform.WebChat)).Returns(true);

			var adapter = new Mock<IChatbotPlatformAdapter>();
			adapter.Setup(a => a.SendRichResponseAsync("conn-1", It.IsAny<ChatbotResponse>())).Returns(Task.CompletedTask);
			reg.Setup(r => r.GetAdapter(ChatbotPlatform.WebChat)).Returns(adapter.Object);

			var service = new ChatbotOutboundService(id.Object, reg.Object, cfg.Object);
			var result = await service.SendToUserAsync("user-1", 1, DispatchMsg());

			result.DeliveredPlatforms.Should().Contain("WebChat");
		}

		[Test]
		public async Task Outbound_DisabledDepartment_DoesNotSendEvenWhenProactiveIsEnabled()
		{
			var (id, cfg, reg) = Mocks(
				new List<ChatbotUserIdentity> { Identity(ChatbotPlatform.Slack, "U123") },
				new ChatbotDepartmentConfig { IsEnabled = false, ProactiveNotificationsEnabled = true, AllowedPlatforms = "*" });
			var adapter = new Mock<IChatbotPlatformAdapter>();
			reg.Setup(r => r.CanInitiateProactively(ChatbotPlatform.Slack)).Returns(true);
			reg.Setup(r => r.GetAdapter(ChatbotPlatform.Slack)).Returns(adapter.Object);

			var service = new ChatbotOutboundService(id.Object, reg.Object, cfg.Object);
			var result = await service.SendToUserAsync("user-1", 1, DispatchMsg());

			result.AnyDelivered.Should().BeFalse();
			adapter.Verify(a => a.SendRichResponseAsync(It.IsAny<string>(), It.IsAny<ChatbotResponse>()), Times.Never);
		}

		[Test]
		public async Task Outbound_NoConfig_DoesNotSendExternally()
		{
			var (id, cfg, reg) = Mocks(
				new List<ChatbotUserIdentity> { Identity(ChatbotPlatform.Slack, "U123") }, config: null);
			var adapter = new Mock<IChatbotPlatformAdapter>();
			reg.Setup(r => r.CanInitiateProactively(ChatbotPlatform.Slack)).Returns(true);
			reg.Setup(r => r.GetAdapter(ChatbotPlatform.Slack)).Returns(adapter.Object);

			var service = new ChatbotOutboundService(id.Object, reg.Object, cfg.Object);
			var result = await service.SendToUserAsync("user-1", 1, DispatchMsg());

			result.AnyDelivered.Should().BeFalse();
			adapter.Verify(a => a.SendRichResponseAsync(It.IsAny<string>(), It.IsAny<ChatbotResponse>()), Times.Never);
		}
	}
}
