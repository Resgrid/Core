using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Chatbot.Handlers;
using Resgrid.Chatbot.Interfaces;
using Resgrid.Chatbot.Models;
using Resgrid.Chatbot.Services;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using LinkingCodeEntity = Resgrid.Model.ChatbotLinkingCode;

namespace Resgrid.Tests.Chatbot
{
	/// <summary>
	/// Tests for the chatbot security hardening: anti-IDOR department-ownership checks,
	/// per-handler authorization, single-use linking codes, and OAuth CSRF state binding.
	/// </summary>
	[TestFixture]
	public class ChatbotSecurityTests
	{
		private static ChatbotSession Session(string userId = "user-1", int departmentId = 1) =>
			new ChatbotSession { SessionId = "s1", UserId = userId, DepartmentId = departmentId, Platform = ChatbotPlatform.SmsTwilio };

		private static ChatbotIntent CallDetailIntent(string callId)
		{
			var intent = new ChatbotIntent { Type = ChatbotIntentType.GetCallDetail };
			intent.Parameters["callId"] = callId;
			return intent;
		}

		// ---- S1: anti-IDOR on CallDetailActionHandler -------------------------------------------

		[Test]
		public async Task CallDetail_CallInDifferentDepartment_ReturnsNotFound_AndDoesNotCheckPermission()
		{
			var calls = new Mock<ICallsService>();
			calls.Setup(s => s.GetCallByIdAsync(It.IsAny<int>(), It.IsAny<bool>()))
				.ReturnsAsync(new Resgrid.Model.Call { CallId = 5, Name = "Secret", DepartmentId = 2 });
			var depts = new Mock<IDepartmentsService>();
			var authz = new Mock<IAuthorizationService>();

			var handler = new CallDetailActionHandler(calls.Object, depts.Object, authz.Object);

			var response = await handler.HandleAsync(new ChatbotMessage { Text = "C5" }, CallDetailIntent("5"), Session(departmentId: 1));

			// Cross-department call resolves to the same no-match reply as a nonexistent one (anti-IDOR).
			response.Text.Should().Contain("No active call found matching");
			response.Text.Should().NotContain("Secret");
			// Cross-department access must short-circuit before any permission check.
			authz.Verify(a => a.CanUserViewCallAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
		}

		[Test]
		public async Task CallDetail_CallInDepartment_WithoutViewPermission_IsDenied()
		{
			var calls = new Mock<ICallsService>();
			calls.Setup(s => s.GetCallByIdAsync(It.IsAny<int>(), It.IsAny<bool>()))
				.ReturnsAsync(new Resgrid.Model.Call { CallId = 5, Name = "Structure Fire", DepartmentId = 1 });
			var depts = new Mock<IDepartmentsService>();
			var authz = new Mock<IAuthorizationService>();
			authz.Setup(a => a.CanUserViewCallAsync(It.IsAny<string>(), It.IsAny<int>())).ReturnsAsync(false);

			var handler = new CallDetailActionHandler(calls.Object, depts.Object, authz.Object);

			var response = await handler.HandleAsync(new ChatbotMessage { Text = "C5" }, CallDetailIntent("5"), Session(departmentId: 1));

			response.Processed.Should().BeFalse();
			response.Text.Should().Contain("permission");
		}

		// ---- S2: authorization on PersonnelActionHandler ----------------------------------------

		[Test]
		public async Task Personnel_WithoutViewAllPeoplePermission_IsDenied()
		{
			var authz = new Mock<IAuthorizationService>();
			authz.Setup(a => a.CanUserViewAllPeopleAsync(It.IsAny<string>(), It.IsAny<int>())).ReturnsAsync(false);

			var handler = new PersonnelActionHandler(
				Mock.Of<IDepartmentsService>(),
				Mock.Of<IUsersService>(),
				Mock.Of<IActionLogsService>(),
				Mock.Of<IUserStateService>(),
				Mock.Of<ICustomStateService>(),
				authz.Object);

			var response = await handler.HandleAsync(new ChatbotMessage { Text = "personnel" },
				new ChatbotIntent { Type = ChatbotIntentType.PersonnelLookup }, Session());

			response.Processed.Should().BeFalse();
			response.Text.Should().Contain("permission");
		}

		// ---- S4: single-use, validated linking codes --------------------------------------------

		[Test]
		public async Task ProcessCode_UnknownCode_Fails()
		{
			var identity = new Mock<IChatbotUserIdentityService>();
			var repo = new Mock<IChatbotLinkingCodeRepository>();
			repo.Setup(r => r.GetByCodeAsync(It.IsAny<string>())).ReturnsAsync((LinkingCodeEntity)null);

			var service = new CodeLinkingService(identity.Object, repo.Object);

			var result = await service.ProcessCodeAsync("ABC123", ChatbotPlatform.Telegram, "tg-1", "Test");

			result.Success.Should().BeFalse();
			identity.Verify(i => i.LinkUserAsync(It.IsAny<string>(), It.IsAny<ChatbotPlatform>(), It.IsAny<string>(),
				It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
		}

		[Test]
		public async Task ProcessCode_AlreadyUsedCode_Fails()
		{
			var identity = new Mock<IChatbotUserIdentityService>();
			var repo = new Mock<IChatbotLinkingCodeRepository>();
			repo.Setup(r => r.GetByCodeAsync(It.IsAny<string>())).ReturnsAsync(new LinkingCodeEntity
			{
				Code = "ABC123", UserId = "user-9", IsUsed = true, ExpiresAt = DateTime.UtcNow.AddMinutes(5)
			});

			var service = new CodeLinkingService(identity.Object, repo.Object);

			var result = await service.ProcessCodeAsync("ABC123", ChatbotPlatform.Telegram, "tg-1", "Test");

			result.Success.Should().BeFalse();
			result.Message.Should().Contain("already been used");
		}

		[Test]
		public async Task ProcessCode_ExpiredCode_Fails()
		{
			var identity = new Mock<IChatbotUserIdentityService>();
			var repo = new Mock<IChatbotLinkingCodeRepository>();
			repo.Setup(r => r.GetByCodeAsync(It.IsAny<string>())).ReturnsAsync(new LinkingCodeEntity
			{
				Code = "ABC123", UserId = "user-9", IsUsed = false, ExpiresAt = DateTime.UtcNow.AddMinutes(-1)
			});

			var service = new CodeLinkingService(identity.Object, repo.Object);

			var result = await service.ProcessCodeAsync("ABC123", ChatbotPlatform.Telegram, "tg-1", "Test");

			result.Success.Should().BeFalse();
			result.Message.Should().Contain("expired");
		}

		[Test]
		public async Task ProcessCode_ValidCode_LinksStoredUser_NotTheCodeString_AndConsumesCode()
		{
			var identity = new Mock<IChatbotUserIdentityService>();
			identity.Setup(i => i.LinkUserAsync(It.IsAny<string>(), It.IsAny<ChatbotPlatform>(), It.IsAny<string>(),
					It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
				.ReturnsAsync(new ChatbotUserIdentity());

			var repo = new Mock<IChatbotLinkingCodeRepository>();
			repo.Setup(r => r.GetByCodeAsync(It.IsAny<string>())).ReturnsAsync(new LinkingCodeEntity
			{
				Code = "ABC123", UserId = "user-9", IsUsed = false, ExpiresAt = DateTime.UtcNow.AddMinutes(5)
			});
			repo.Setup(r => r.UpdateAsync(It.IsAny<LinkingCodeEntity>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((LinkingCodeEntity e, CancellationToken _, bool __) => e);

			var service = new CodeLinkingService(identity.Object, repo.Object);

			var result = await service.ProcessCodeAsync("ABC123", ChatbotPlatform.Telegram, "tg-1", "Test");

			result.Success.Should().BeTrue();
			// The link must use the stored Resgrid user id, NOT the code string.
			identity.Verify(i => i.LinkUserAsync("user-9", ChatbotPlatform.Telegram, "tg-1",
				It.IsAny<string>(), "code", It.IsAny<string>()), Times.Once);
			// The code must be consumed (single use).
			repo.Verify(r => r.UpdateAsync(It.Is<LinkingCodeEntity>(e => e.IsUsed), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);
		}

		// ---- S5: OAuth CSRF state binding -------------------------------------------------------

		[Test]
		public async Task OAuthComplete_UnknownState_Fails()
		{
			var service = new OAuthLinkingService(Mock.Of<IChatbotUserIdentityService>(), Mock.Of<ICacheProvider>());

			var result = await service.ExchangeAndLinkAsync("user-1", ChatbotPlatform.Discord, "code", "state-that-was-never-issued");

			result.Success.Should().BeFalse();
		}

		[Test]
		public async Task OAuthComplete_StateIssuedToDifferentUser_Fails()
		{
			ChatbotConfig.DiscordClientId = "test-client";
			ChatbotConfig.OAuthRedirectUri = "https://example.test/callback";

			var service = new OAuthLinkingService(Mock.Of<IChatbotUserIdentityService>(), Mock.Of<ICacheProvider>());

			var start = await service.StartLinkAsync("user-A", ChatbotPlatform.Discord);
			start.Success.Should().BeTrue();

			// A different authenticated user attempts to complete the link with user-A's state.
			var result = await service.ExchangeAndLinkAsync("user-B", ChatbotPlatform.Discord, "code", start.State);

			result.Success.Should().BeFalse();
		}
	}
}
