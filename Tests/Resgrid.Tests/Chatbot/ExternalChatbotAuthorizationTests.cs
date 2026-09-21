using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Chatbot.Handlers;
using Resgrid.Chatbot.Interfaces;
using Resgrid.Chatbot.Models;
using Resgrid.Chatbot.Services;
using Resgrid.Model;
using Resgrid.Model.Reporting;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Tests.Chatbot
{
	[TestFixture]
	public class ExternalChatbotAuthorizationTests
	{
		private const string UserId = "channel-user";
		private const string Sender = "15551234567";
		private const int DepartmentId = 42;

		[Test]
		public async Task CallsList_FiltersDeniedAndForeignCallsBeforeApplyingTenCallLimit()
		{
			var candidates = Enumerable.Range(1, 12)
				.Select(id => new Call { CallId = id, DepartmentId = DepartmentId, Name = "HiddenCall" + id }).ToList();
			candidates.Add(new Call { CallId = 900, DepartmentId = 99, Name = "ForeignCall" });
			candidates.AddRange(Enumerable.Range(100, 11)
				.Select(id => new Call { CallId = id, DepartmentId = DepartmentId, Name = "VisibleCall" + id }));
			var calls = new Mock<ICallsService>();
			calls.Setup(c => c.GetActiveCallsByDepartmentAsync(DepartmentId)).ReturnsAsync(candidates);
			var authorization = new Mock<IAuthorizationService>();
			authorization.Setup(a => a.CanUserViewCallAsync(UserId, It.IsAny<int>()))
				.ReturnsAsync((string userId, int callId) => callId >= 100);
			var handler = new CallsActionHandler(calls.Object, Mock.Of<IDepartmentsService>(),
				Mock.Of<ICustomStateService>(), Mock.Of<IUserProfileService>(), authorization.Object);

			var response = await handler.HandleAsync(Message(), new ChatbotIntent { Type = ChatbotIntentType.ListCalls }, Session());

			response.Processed.Should().BeTrue();
			response.Text.Should().NotContain("HiddenCall").And.NotContain("ForeignCall").And.NotContain("VisibleCall110");
			foreach (var id in Enumerable.Range(100, 10)) response.Text.Should().Contain("VisibleCall" + id);
			authorization.Verify(a => a.CanUserViewCallAsync(It.IsAny<string>(), 900), Times.Never);
		}

		[Test]
		public async Task UnitsList_FiltersDeniedAndForeignUnitsBeforeApplyingFifteenUnitLimit()
		{
			var candidates = UnitCandidates();
			var units = new Mock<IUnitsService>();
			units.Setup(u => u.GetAllLatestStatusForUnitsByDepartmentIdAsync(DepartmentId)).ReturnsAsync(candidates);
			var authorization = UnitAuthorization();
			var states = new Mock<ICustomStateService>();
			states.Setup(s => s.GetCustomUnitStateAsync(It.IsAny<UnitState>()))
				.ReturnsAsync(new CustomStateDetail { ButtonText = "Available" });
			var handler = new UnitsActionHandler(units.Object, states.Object, Mock.Of<IDepartmentsService>(), authorization.Object);

			var response = await handler.HandleAsync(Message(), new ChatbotIntent { Type = ChatbotIntentType.ListUnits }, Session());

			response.Processed.Should().BeTrue();
			response.Text.Should().NotContain("HiddenUnit").And.NotContain("ForeignUnit").And.NotContain("VisibleUnit115");
			foreach (var id in Enumerable.Range(100, 15)) response.Text.Should().Contain("VisibleUnit" + id);
			states.Verify(s => s.GetCustomUnitStateAsync(It.Is<UnitState>(u => u.UnitId < 100 || u.UnitId == 900)), Times.Never);
			authorization.Verify(a => a.CanUserViewUnitAsync(It.IsAny<string>(), 900), Times.Never);
		}

		[Test]
		public async Task AvailableUnits_CountAndOverflowIncludeOnlyAuthorizedDepartmentUnits()
		{
			var units = new Mock<IUnitsService>();
			units.Setup(u => u.GetAllLatestStatusForUnitsByDepartmentIdAsync(DepartmentId)).ReturnsAsync(UnitCandidates());
			var authorization = UnitAuthorization();
			var states = new Mock<ICustomStateService>();
			states.Setup(s => s.GetCustomUnitStateAsync(It.IsAny<UnitState>()))
				.ReturnsAsync(new CustomStateDetail { ButtonText = "Available" });
			var reporting = new Mock<IPlatformReportingService>();
			reporting.Setup(r => r.ClassifyUnitAvailabilityAsync(DepartmentId, It.IsAny<int>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(AvailabilityClass.Available);
			var handler = new UnitsAvailableActionHandler(units.Object, states.Object, reporting.Object, authorization.Object);

			var response = await handler.HandleAsync(Message(), new ChatbotIntent { Type = ChatbotIntentType.UnitsAvailable }, Session());

			response.Processed.Should().BeTrue();
			response.Text.Should().Contain("Available Units (16):").And.Contain("...and 1 more.")
				.And.NotContain("HiddenUnit").And.NotContain("ForeignUnit").And.NotContain("VisibleUnit115");
			foreach (var id in Enumerable.Range(100, 15)) response.Text.Should().Contain("VisibleUnit" + id);
			// The fixture uses distinct state ids so even availability classification must not touch denied units.
			reporting.Verify(r => r.ClassifyUnitAvailabilityAsync(DepartmentId, It.Is<int>(state => state < 100 || state == 900), It.IsAny<CancellationToken>()), Times.Never);
			reporting.Verify(r => r.ClassifyUnitAvailabilityAsync(DepartmentId, It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Exactly(16));
			authorization.Verify(a => a.CanUserViewUnitAsync(It.IsAny<string>(), 900), Times.Never);
		}

		[TestCase(ChatbotPlatform.Telegram)]
		[TestCase(ChatbotPlatform.Discord)]
		[TestCase(ChatbotPlatform.WhatsApp)]
		public async Task ExternalNumericSender_CannotAuthenticateThroughSmsPhoneIdentity(ChatbotPlatform platform)
		{
			var harness = new IngressHarness(platform);
			harness.Identities.Setup(i => i.GetIdentityAsync(platform, Sender)).ReturnsAsync((ChatbotUserIdentity)null);
			harness.Identities.Setup(i => i.GetIdentityByPhoneAsync(Sender)).ReturnsAsync(new ChatbotUserIdentity
			{
				UserId = "sms-owner", Platform = ChatbotPlatform.SmsTwilio, PlatformUserId = Sender, IsActive = true
			});

			var response = await harness.Service.ProcessMessageAsync(Message(platform));

			response.Text.Should().Contain("couldn't identify your account");
			harness.Identities.Verify(i => i.GetIdentityByPhoneAsync(It.IsAny<string>()), Times.Never);
			harness.VerifyNoIdentityTouch();
			harness.Departments.Verify(d => d.GetAllDepartmentsForUserAsync(It.IsAny<string>()), Times.Never);
		}

		[TestCase(ChatbotPlatform.Telegram)]
		[TestCase(ChatbotPlatform.SmsTwilio)]
		[TestCase(ChatbotPlatform.WebChat)]
		public async Task InactiveIdentity_IsRejectedWithoutReactivation(ChatbotPlatform platform)
		{
			var harness = new IngressHarness(platform);
			harness.Identity.IsActive = false;

			var response = await harness.Service.ProcessMessageAsync(Message(platform));

			response.Text.Should().Contain("no longer linked");
			harness.VerifyNoIdentityTouch();
			harness.Identities.Verify(i => i.GetIdentityByPhoneAsync(It.IsAny<string>()), Times.Never);
			harness.Profiles.Verify(p => p.GetProfileByMobileNumberAsync(It.IsAny<string>()), Times.Never);
		}

		[TestCase(ChatbotPlatform.Telegram)]
		[TestCase(ChatbotPlatform.Slack)]
		[TestCase(ChatbotPlatform.MicrosoftTeams)]
		public async Task ExternalChannel_UsesActiveMembershipWithoutSmsProvisioningEligibility(ChatbotPlatform platform)
		{
			var harness = new IngressHarness(platform);
			// Stop at the rate limiter: reaching it proves membership and platform checks passed.
			harness.RateLimiter.Setup(r => r.TryAcquireAsync(UserId, DepartmentId, It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(false);

			var response = await harness.Service.ProcessMessageAsync(Message(platform));

			response.Text.Should().Contain("too quickly");
			harness.Authorization.Verify(a => a.IsUserValidWithinLimitsAsync(UserId, DepartmentId), Times.Once);
			harness.RateLimiter.Verify(r => r.TryAcquireAsync(UserId, DepartmentId, It.IsAny<int>(), It.IsAny<int>()), Times.Once);
			harness.Limits.Verify(l => l.CanDepartmentProvisionNumberAsync(It.IsAny<int>()), Times.Never);
			harness.Departments.Verify(d => d.GetActiveSmsDepartmentForUserAsync(It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
			harness.Departments.Verify(d => d.GetSmsSupportedMembershipsForUserAsync(It.IsAny<string>()), Times.Never);
			harness.Departments.Verify(d => d.GetDepartmentByIdAsync(99, It.IsAny<bool>()), Times.Never);
		}

		[Test]
		public async Task ExternalChannel_StillRequiresCurrentMembershipAuthorization()
		{
			var harness = new IngressHarness(ChatbotPlatform.Telegram);
			harness.Authorization.Setup(a => a.IsUserValidWithinLimitsAsync(UserId, DepartmentId)).ReturnsAsync(false);

			var response = await harness.Service.ProcessMessageAsync(Message());

			response.Text.Should().Contain("isn't active in this department");
			harness.RateLimiter.Verify(r => r.TryAcquireAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
		}

		[TestCase(false, "*")]
		[TestCase(true, "Discord")]
		public async Task ExternalChannel_StillRequiresEnabledDepartmentAndAllowedPlatform(bool enabled, string allowedPlatforms)
		{
			var harness = new IngressHarness(ChatbotPlatform.Telegram);
			harness.Configuration.IsEnabled = enabled;
			harness.Configuration.AllowedPlatforms = allowedPlatforms;

			var response = await harness.Service.ProcessMessageAsync(Message());

			response.Text.Should().Contain(enabled ? "This channel isn't enabled" : "not enabled for your department");
			harness.RateLimiter.Verify(r => r.TryAcquireAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
		}

		private static ChatbotMessage Message(ChatbotPlatform platform = ChatbotPlatform.Telegram) => new()
		{ From = Sender, Text = "units", Platform = platform };
		private static ChatbotSession Session() => new()
		{ UserId = UserId, DepartmentId = DepartmentId, Culture = "en", Platform = ChatbotPlatform.Telegram };

		private static List<UnitState> UnitCandidates()
		{
			var units = Enumerable.Range(1, 16).Select(id => State(id, DepartmentId, "HiddenUnit" + id)).ToList();
			units.Add(State(900, 99, "ForeignUnit"));
			units.AddRange(Enumerable.Range(100, 16).Select(id => State(id, DepartmentId, "VisibleUnit" + id)));
			return units;
		}
		private static UnitState State(int id, int departmentId, string name) => new()
		{ UnitId = id, State = id, Unit = new Unit { UnitId = id, DepartmentId = departmentId, Name = name } };
		private static Mock<IAuthorizationService> UnitAuthorization()
		{
			var authorization = new Mock<IAuthorizationService>();
			authorization.Setup(a => a.CanUserViewUnitAsync(UserId, It.IsAny<int>()))
				.ReturnsAsync((string userId, int unitId) => unitId >= 100);
			return authorization;
		}

		private sealed class IngressHarness
		{
			public readonly Mock<IChatbotUserIdentityService> Identities = new();
			public readonly Mock<IDepartmentsService> Departments = new();
			public readonly Mock<IUserProfileService> Profiles = new();
			public readonly Mock<ILimitsService> Limits = new();
			public readonly Mock<IAuthorizationService> Authorization = new();
			public readonly Mock<IChatbotRateLimiter> RateLimiter = new();
			public readonly ChatbotUserIdentity Identity;
			public readonly ChatbotDepartmentConfig Configuration = new()
			{ DepartmentId = DepartmentId, IsEnabled = true, AllowedPlatforms = "*" };
			public readonly ChatbotIngressService Service;

			public IngressHarness(ChatbotPlatform platform)
			{
				Identity = new ChatbotUserIdentity
				{ Id = "identity-1", UserId = UserId, Platform = platform, PlatformUserId = Sender, IsActive = true, LinkingMethod = "code" };
				Identities.Setup(i => i.GetIdentityAsync(platform, Sender)).ReturnsAsync(Identity);
				Identities.Setup(i => i.LinkUserAsync(UserId, platform, Sender, It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(Identity);
				Departments.Setup(d => d.GetAllDepartmentsForUserAsync(UserId)).ReturnsAsync(new List<DepartmentMember>
				{
					new DepartmentMember { DepartmentId = 99, IsDefault = true },
					new DepartmentMember { DepartmentId = DepartmentId, IsActive = true }
				});
				Departments.Setup(d => d.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>()))
					.ReturnsAsync(new Department { DepartmentId = DepartmentId });
				Authorization.Setup(a => a.IsUserValidWithinLimitsAsync(UserId, DepartmentId)).ReturnsAsync(true);
				var config = new Mock<IChatbotDepartmentConfigService>();
				config.Setup(c => c.GetConfigAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(Configuration);
				var templates = new Mock<IChatbotTemplateRenderer>();
				templates.Setup(t => t.RenderResponseAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<ChatbotPlatform>(), It.IsAny<ChatbotIntent>()))
					.ReturnsAsync((string name, object model, ChatbotPlatform source, ChatbotIntent intent) =>
						new ChatbotResponse { Text = (model as ErrorModel)?.Message ?? "Template response", Processed = true });
				Service = new ChatbotIngressService(Identities.Object, Mock.Of<IChatbotSessionManager>(),
					Mock.Of<IChatbotIntentRouter>(), Array.Empty<IChatbotActionHandler>(), Mock.Of<IConversationEngine>(),
					templates.Object, Profiles.Object, Departments.Object, Mock.Of<IDepartmentSettingsService>(),
					Limits.Object, Authorization.Object, config.Object, RateLimiter.Object, Mock.Of<ISecurityPinService>(),
					Mock.Of<ITextResponseResolver>(), Mock.Of<IChatbotConversationalFallback>(), Mock.Of<IChatbotMessageLogRepository>());
			}

			public void VerifyNoIdentityTouch()
			{
				Identities.Verify(i => i.LinkUserAsync(It.IsAny<string>(), It.IsAny<ChatbotPlatform>(), It.IsAny<string>(),
					It.IsAny<string>(), It.IsAny<string>()), Times.Never);
				Identities.Verify(i => i.LinkUserAsync(It.IsAny<string>(), It.IsAny<ChatbotPlatform>(), It.IsAny<string>(),
					It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
			}
		}
	}
}
