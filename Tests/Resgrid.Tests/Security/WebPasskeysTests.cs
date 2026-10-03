using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Localization;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Security;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Models.TwoFactor;
using WebClaims = Resgrid.Web.Helpers.ClaimsAuthorizationHelper;
using IdentityUser = Resgrid.Model.Identity.IdentityUser;
using PasskeysController = Resgrid.Web.Areas.User.Controllers.PasskeysController;
using TwoFactorController = Resgrid.Web.Areas.User.Controllers.TwoFactorController;
using WebDataProtectionController = Resgrid.Web.Areas.User.Controllers.DataProtectionController;

namespace Resgrid.Tests.Security
{
	/// <summary>
	/// Passkey workbook section 12, slice 19 (Phase 2): passkeys on Core Web. The account page adds a Web passkey and
	/// renames or removes any of the user's passkeys through the passkey service, following it back to the password
	/// confirmation or step-up it asks for; Verify2FA accepts a Web passkey and records it as this session's evidence; and
	/// the ADP reveal dialog can use a passkey or Responder approval through the one grant issuer.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class WebPasskeysTests
	{
		private const string UserId = "user-1";
		private const int DepartmentId = 42;
		private const string SessionId = "web-session";

		private IHttpContextAccessor _previous;
		private IdentityUser _user;
		private Mock<IPasskeyService> _passkeys;

		[SetUp]
		public void SetUp()
		{
			_previous = WebClaims._httpContextAccessor;
			_user = new IdentityUser { Id = UserId, UserName = "user1", AuthenticationGeneration = 4 };
			_passkeys = new Mock<IPasskeyService>();
		}

		[TearDown]
		public void TearDown() => WebClaims._httpContextAccessor = _previous;

		private DefaultHttpContext Http(bool withSession = true)
		{
			var http = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, UserId), new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString()), new Claim(ClaimTypes.Name, "user1"),
					new Claim(SessionClaimTypes.SessionId, SessionId)
				}, "test"))
			};
			http.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
			if (withSession)
				http.Items[ProtectedGrantSessionContext.HttpItemKey] = new ProtectedGrantSessionContext
				{
					SessionId = SessionId, ClientApplication = (int)UserSessionClientApplication.Web, AuthenticationGeneration = 4
				};
			WebClaims._httpContextAccessor = new HttpContextAccessor { HttpContext = http };
			return http;
		}

		private Mock<UserManager<IdentityUser>> Users(int codesLeft = 8, bool enrolled = true)
		{
			var users = new Mock<UserManager<IdentityUser>>(Mock.Of<IUserStore<IdentityUser>>(), null, null, null, null, null, null, null, null);
			users.Setup(m => m.GetUserAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync(_user);
			users.Setup(m => m.FindByIdAsync(UserId)).ReturnsAsync(_user);
			users.Setup(m => m.GetTwoFactorEnabledAsync(_user)).ReturnsAsync(enrolled);
			users.Setup(m => m.CountRecoveryCodesAsync(_user)).ReturnsAsync(codesLeft);
			users.Setup(m => m.GetAuthenticatorKeyAsync(_user)).ReturnsAsync("KEY");
			return users;
		}

		private static IUrlHelper Urls()
		{
			var urls = new Mock<IUrlHelper>();
			urls.Setup(u => u.Action(It.IsAny<UrlActionContext>())).Returns((UrlActionContext c) =>
				"/" + (c.Values is null ? "" : (new RouteValueReader(c.Values).Area ?? "")) + "/" + c.Controller + "/" + c.Action);
			urls.Setup(u => u.IsLocalUrl(It.IsAny<string>())).Returns((string url) => url != null && url.StartsWith("/") && !url.StartsWith("//"));
			return urls.Object;
		}

		private sealed class RouteValueReader
		{
			public RouteValueReader(object values) => Area = values?.GetType().GetProperty("area")?.GetValue(values) as string;
			public string Area { get; }
		}

		private static JObject Body(IActionResult result) => JObject.FromObject(((JsonResult)result).Value);

		private PasskeysController Passkeys(bool withSession = true) => new(_passkeys.Object, Users().Object)
		{
			ControllerContext = new ControllerContext { HttpContext = Http(withSession) },
			Url = Urls()
		};

		// ---- The account page's passkey commands --------------------------------------------------------------------

		[Test]
		public async Task Adding_a_web_passkey_starts_a_ceremony_for_this_session_and_the_web_client()
		{
			PasskeyCaller caller = null;
			_passkeys.Setup(p => p.BeginRegistrationAsync(It.IsAny<PasskeyCaller>(), true, 8, It.IsAny<CancellationToken>()))
				.Callback((PasskeyCaller c, bool _, int _, CancellationToken _) => caller = c)
				.ReturnsAsync(new PasskeyCeremonyStart { Outcome = PasskeyOutcome.Succeeded, RequestId = "req-1", OptionsJson = "{\"challenge\":\"abc\"}" });

			var started = Body(await Passkeys().RegistrationOptions(CancellationToken.None));

			started.Value<bool>("success").Should().BeTrue();
			started.Value<string>("requestId").Should().Be("req-1");
			started.Value<string>("options").Should().Be("{\"challenge\":\"abc\"}", "the page hands the server's options to the browser unchanged");
			caller.SessionId.Should().Be(SessionId, "the validated session, never a claim");
			caller.ClientApplication.Should().Be(UserSessionClientApplication.Web);
			caller.AuditSystem.Should().Be(SystemAuditSystems.Website);
			caller.DepartmentId.Should().Be(DepartmentId);

			_passkeys.Setup(p => p.CompleteRegistrationAsync(It.Is<PasskeyCaller>(c => c.SessionId == SessionId), "req-1", "{\"id\":\"x\"}", "Desk key",
				It.IsAny<CancellationToken>())).ReturnsAsync(new PasskeyRegistrationResult { Outcome = PasskeyOutcome.Succeeded });
			Body(await Passkeys().CompleteRegistration("req-1", "{\"id\":\"x\"}", "Desk key", CancellationToken.None)).Value<bool>("success").Should().BeTrue();
			Body(await Passkeys().CompleteRegistration("req-1", null, "Desk key", CancellationToken.None)).Value<string>("error").Should().Be("invalid_request");
		}

		[TestCase(PasskeyOutcome.ReauthenticationRequired, "reauthentication_required", "/User/AccountSecurity/Reauthenticate")]
		[TestCase(PasskeyOutcome.StepUpRequired, "step_up_required", "/User/TwoFactor/Verify2FA")]
		[TestCase(PasskeyOutcome.LimitReached, "passkey_limit_reached", null)]
		[TestCase(PasskeyOutcome.Unavailable, "passkeys_unavailable", null)]
		public async Task A_refusal_names_what_to_do_first(PasskeyOutcome outcome, string code, string redirect)
		{
			_passkeys.Setup(p => p.BeginRegistrationAsync(It.IsAny<PasskeyCaller>(), It.IsAny<bool>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(PasskeyCeremonyStart.Of(outcome));

			var refused = Body(await Passkeys().RegistrationOptions(CancellationToken.None));

			refused.Value<bool>("success").Should().BeFalse();
			refused.Value<string>("error").Should().Be(code);
			refused.Value<string>("redirect").Should().Be(redirect);
		}

		[Test]
		public async Task Any_passkey_can_be_renamed_or_removed_and_removing_this_sessions_passkey_signs_out()
		{
			_passkeys.Setup(p => p.RenameAsync(It.Is<PasskeyCaller>(c => c.SessionId == SessionId), "pk-unit", "Engine tablet", It.IsAny<CancellationToken>()))
				.ReturnsAsync(PasskeyOutcome.Succeeded);
			Body(await Passkeys().Rename("pk-unit", "Engine tablet", CancellationToken.None)).Value<bool>("success").Should().BeTrue();

			_passkeys.Setup(p => p.RevokeAsync(It.Is<PasskeyCaller>(c => c.SessionId == SessionId), "pk-web", It.IsAny<CancellationToken>()))
				.ReturnsAsync(new PasskeyRevocationResult { Outcome = PasskeyOutcome.Succeeded, Revoked = 1, CurrentSessionEnded = true });
			var removed = Body(await Passkeys().Remove("pk-web", CancellationToken.None));
			removed.Value<bool>("success").Should().BeTrue();
			removed.Value<bool>("signedOut").Should().BeTrue();

			_passkeys.Setup(p => p.RevokeAsync(It.IsAny<PasskeyCaller>(), "pk-web", It.IsAny<CancellationToken>()))
				.ReturnsAsync(PasskeyRevocationResult.Of(PasskeyOutcome.StepUpRequired));
			Body(await Passkeys().Remove("pk-web", CancellationToken.None)).Value<string>("redirect").Should().Be("/User/TwoFactor/Verify2FA");

			_passkeys.Setup(p => p.RevokeAsync(null, "pk-web", It.IsAny<CancellationToken>())).ReturnsAsync(PasskeyRevocationResult.Of(PasskeyOutcome.SessionRequired));
			Body(await Passkeys(withSession: false).Remove("pk-web", CancellationToken.None)).Value<string>("error").Should().Be("session_required",
				"an untracked session has no evidence to act on");
		}

		// ---- The account page and Verify2FA -------------------------------------------------------------------------

		private TwoFactorController TwoFactor(Mock<IMfaEvidenceService> evidence = null, Mock<IMfaActivityService> activity = null, IMfaPolicyService policy = null)
		{
			var localizer = new Mock<IStringLocalizer<Resgrid.Localization.Areas.User.TwoFactor.TwoFactor>>();
			localizer.Setup(l => l[It.IsAny<string>()]).Returns((string key) => new LocalizedString(key, key));
			return new TwoFactorController(Users().Object, null, Mock.Of<ISystemAuditsService>(), UrlEncoder.Default, localizer.Object,
				Mock.Of<IUserStore<IdentityUser>>(), new InMemoryUserMfaStateRepository(), Mock.Of<IUserSessionService>(), (evidence ?? new Mock<IMfaEvidenceService>()).Object,
				policy ?? Mock.Of<IMfaPolicyService>(), new InMemoryUserPasskeyRepository(), Mock.Of<ISecurityNoticeService>(),
				(activity ?? new Mock<IMfaActivityService>()).Object, _passkeys.Object, Mock.Of<IMfaApprovalService>(), Mock.Of<ISsoBrokerService>(),
				Mock.Of<ISsoReturnTargetRegistry>(), Mock.Of<IDepartmentSsoService>(), Mock.Of<IDepartmentsService>(), Mock.Of<Resgrid.Model.Providers.ICacheProvider>(), new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider())
			{
				ControllerContext = new ControllerContext { HttpContext = Http() },
				Url = Urls()
			};
		}

		[Test]
		public async Task The_account_page_lists_passkeys_from_every_app_and_says_whether_a_web_one_can_be_added()
		{
			_passkeys.Setup(p => p.IsRegistrationAvailable(UserSessionClientApplication.Web)).Returns(true);
			_passkeys.Setup(p => p.GetActiveForUserAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<UserPasskey>
			{
				new() { UserPasskeyId = "pk-unit", UserId = UserId, ClientApplication = (int)UserSessionClientApplication.Unit, DisplayName = "Engine tablet",
					CreatedOnUtc = new DateTime(2026, 9, 1), RegisteredInSharedMode = true },
				new() { UserPasskeyId = "pk-web", UserId = UserId, ClientApplication = (int)UserSessionClientApplication.Web, DisplayName = "Laptop",
					CreatedOnUtc = new DateTime(2026, 9, 2), LastUsedOnUtc = new DateTime(2026, 9, 3) }
			});

			var model = (TwoFactorIndexViewModel)((ViewResult)await TwoFactor().Index("added", CancellationToken.None)).Model;

			model.WebPasskeyRegistrationAvailable.Should().BeTrue();
			model.PasskeyStatus.Should().Be("added");
			model.Passkeys.Should().HaveCount(2);
			model.Passkeys[0].Id.Should().Be("pk-web", "grouped by app, the web first");
			model.Passkeys[0].AppLabelKey.Should().Be("PasskeyAppWeb");
			model.Passkeys[0].LastUsedOn.Should().Be(new DateTime(2026, 9, 3));
			model.Passkeys[1].AppLabelKey.Should().Be("PasskeyAppUnit");
			model.Passkeys[1].CreatedOnSharedInstallation.Should().BeTrue();

			((TwoFactorIndexViewModel)((ViewResult)await TwoFactor().Index("<script>", CancellationToken.None)).Model).PasskeyStatus
				.Should().BeNull("only known statuses are shown");
		}

		[Test]
		public async Task Verify2FA_offers_a_passkey_only_with_a_web_passkey_and_where_the_scope_accepts_one()
		{
			var policy = new Mock<IMfaPolicyService>();
			policy.Setup(p => p.IsMethodAcceptedAsync(DepartmentId, MfaMethodScope.Adp, MfaEvidenceMethod.Passkey, It.IsAny<CancellationToken>())).ReturnsAsync(true);
			policy.Setup(p => p.IsMethodAcceptedAsync(DepartmentId, MfaMethodScope.Login, MfaEvidenceMethod.Passkey, It.IsAny<CancellationToken>())).ReturnsAsync(false);
			async Task<bool> Offered(string scope) =>
				((StepUpVerifyViewModel)((ViewResult)await TwoFactor(policy: policy.Object).Verify2FA("/User/Records", scope, CancellationToken.None)).Model).PasskeyAvailable;

			(await Offered("Adp")).Should().BeFalse("the user has no web passkey");

			_passkeys.Setup(p => p.HasActiveForClientAsync(UserId, UserSessionClientApplication.Web, It.IsAny<CancellationToken>())).ReturnsAsync(true);
			(await Offered("Adp")).Should().BeTrue();
			(await Offered("Login")).Should().BeFalse("the department does not accept a passkey there");
			(await Offered(null)).Should().BeFalse("no scope reads as sign-in's");
			(await Offered("bogus")).Should().BeFalse();

			var untracked = TwoFactor(policy: policy.Object);
			untracked.ControllerContext.HttpContext.Items.Remove(ProtectedGrantSessionContext.HttpItemKey);
			((StepUpVerifyViewModel)((ViewResult)await untracked.Verify2FA("/User/Records", "Adp", CancellationToken.None)).Model).PasskeyAvailable
				.Should().BeFalse("a passkey needs a tracked session to hold its evidence");
		}

		[Test]
		public async Task A_web_passkey_at_step_up_is_this_sessions_evidence_and_returns_only_to_a_local_page()
		{
			var passkey = new UserPasskey { UserPasskeyId = "pk-web", UserId = UserId, ClientApplication = (int)UserSessionClientApplication.Web };
			var verifiedOn = DateTime.UtcNow.AddSeconds(-1);
			_passkeys.Setup(p => p.CompleteAssertionAsync(It.Is<PasskeyCaller>(c => c.SessionId == SessionId && c.ClientApplication == UserSessionClientApplication.Web),
					AuthenticationChallengePurpose.SensitiveOperation, "req-1", "{}", It.IsAny<CancellationToken>()))
				.ReturnsAsync(new PasskeyAssertionResult { Outcome = PasskeyOutcome.Succeeded, Passkey = passkey, VerifiedOnUtc = verifiedOn });
			var evidence = new Mock<IMfaEvidenceService>();

			var done = Body(await TwoFactor(evidence).Verify2FAPasskey("req-1", "{}", "/User/Records/Edit/7", CancellationToken.None));

			done.Value<bool>("success").Should().BeTrue();
			done.Value<string>("redirect").Should().Be("/User/Records/Edit/7");
			evidence.Verify(e => e.RecordAsync(UserId, "sid:" + SessionId, UserSessionClientApplication.Web, MfaEvidenceKind.SecondFactor, MfaEvidenceMethod.Passkey,
				MfaEvidencePurpose.StepUp, verifiedOn, 4, null, "passkey:pk-web", It.IsAny<CancellationToken>()), Times.Once);

			Body(await TwoFactor(evidence).Verify2FAPasskey("req-1", "{}", "https://evil.example/", CancellationToken.None)).Value<string>("redirect")
				.Should().Be("/User/Home/Dashboard", "never an outside address");
		}

		[Test]
		public async Task A_failed_web_passkey_at_step_up_is_denied_activity_and_no_evidence()
		{
			_passkeys.Setup(p => p.CompleteAssertionAsync(It.IsAny<PasskeyCaller>(), AuthenticationChallengePurpose.SensitiveOperation, It.IsAny<string>(),
				It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(PasskeyAssertionResult.Of(PasskeyOutcome.VerificationFailed));
			var evidence = new Mock<IMfaEvidenceService>();
			var activity = new Mock<IMfaActivityService>();

			var refused = Body(await TwoFactor(evidence, activity).Verify2FAPasskey("req-1", "{}", "/", CancellationToken.None));

			refused.Value<string>("error").Should().Be("passkey_verification_failed");
			evidence.Verify(e => e.RecordAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<UserSessionClientApplication>(), It.IsAny<MfaEvidenceKind>(),
				It.IsAny<MfaEvidenceMethod>(), It.IsAny<MfaEvidencePurpose>(), It.IsAny<DateTime>(), It.IsAny<long>(), It.IsAny<int?>(), It.IsAny<string>(),
				It.IsAny<CancellationToken>()), Times.Never);
			activity.Verify(a => a.RecordAsync(It.Is<MfaActivityEntry>(e => !e.Successful && e.Method == MfaEvidenceMethod.Passkey && e.Purpose == MfaEvidencePurpose.StepUp &&
				e.ClientApplication == UserSessionClientApplication.Web && e.SessionId == SessionId), It.IsAny<CancellationToken>()), Times.Once);

			_passkeys.Setup(p => p.BeginAssertionAsync(It.Is<PasskeyCaller>(c => c.SessionId == SessionId), AuthenticationChallengePurpose.SensitiveOperation,
				It.IsAny<CancellationToken>())).ReturnsAsync(new PasskeyCeremonyStart { Outcome = PasskeyOutcome.Succeeded, RequestId = "req-2", OptionsJson = "{}" });
			Body(await TwoFactor().Verify2FAPasskeyOptions(CancellationToken.None)).Value<string>("requestId").Should().Be("req-2");
		}

		// ---- The ADP reveal dialog on Web ------------------------------------------------------------------------------

		[Test]
		public async Task The_reveal_dialog_can_use_a_passkey_or_responder_through_the_one_issuer()
		{
			var adp = new Mock<IAdpStepUpService>();
			var approvals = new Mock<IMfaApprovalService>();
			var controller = new WebDataProtectionController(Mock.Of<IDepartmentDataProtectionService>(), Mock.Of<IDepartmentLockService>(),
				Mock.Of<IAdpSizingService>(), Mock.Of<IProtectedDataBrokerClient>(), Mock.Of<IDepartmentsService>(), Users().Object,
				Mock.Of<IProtectedDataGrantService>(), Mock.Of<IAdpReleaseService>(), Mock.Of<IAdpAccessStore>(), Mock.Of<IAdpAuditRepository>(), Mock.Of<ICacheProvider>(),
				Mock.Of<IEventAggregator>(), Mock.Of<IProtectedWorkflowService>(), Mock.Of<Resgrid.Chatbot.Interfaces.IChatbotDepartmentConfigService>(),
				Mock.Of<IMfaEvidenceService>(), Mock.Of<IMfaActivityService>(), adp.Object, Mock.Of<IMfaCredentialStateService>(), approvals.Object)
			{
				ControllerContext = new ControllerContext { HttpContext = Http() }
			};
			var grant = new AdpGrantIssue { Outcome = AdpGrantOutcome.Issued, GrantId = "g", Token = "T", ExpiresOnUtc = new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc) };

			adp.Setup(a => a.GetMethodChoiceAsync(It.IsAny<AdpStepUpCaller>(), true, It.IsAny<CancellationToken>())).ReturnsAsync(new MfaMethodChoice
			{
				EnrolledMethods = new[] { MfaMethodNames.Totp, MfaMethodNames.Passkey },
				AllowedMethods = new[] { MfaMethodNames.Totp, MfaMethodNames.Passkey, MfaMethodNames.PasskeyApproval },
				Preferred = MfaMethodNames.Passkey
			});
			var methods = Body(await controller.StepUpMethods(CancellationToken.None));
			methods["methods"].ToObject<string[]>().Should().Equal(new[] { MfaMethodNames.Totp, MfaMethodNames.Passkey },
				"only what the user has and the department accepts");
			methods.Value<string>("preferred").Should().Be(MfaMethodNames.Passkey);

			adp.Setup(a => a.BeginPasskeyAsync(It.Is<AdpStepUpCaller>(c => c.Session.SessionId == SessionId && c.AuditSystem == SystemAuditSystems.Website),
				It.IsAny<CancellationToken>())).ReturnsAsync(new PasskeyCeremonyStart { Outcome = PasskeyOutcome.Succeeded, RequestId = "req-1", OptionsJson = "{}" });
			Body(await controller.PasskeyOptions(CancellationToken.None)).Value<string>("requestId").Should().Be("req-1");
			adp.Setup(a => a.CompletePasskeyAsync(It.IsAny<AdpStepUpCaller>(), "req-1", "{}", It.IsAny<CancellationToken>())).ReturnsAsync(grant);
			Body(await controller.VerifyPasskey("req-1", "{}", CancellationToken.None)).Value<string>("grantToken").Should().Be("T");
			Body(await controller.VerifyPasskey("req-1", null, CancellationToken.None)).Value<string>("error").Should().Be("invalid_request");

			adp.Setup(a => a.RequestApprovalAsync(It.IsAny<AdpStepUpCaller>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new MfaApprovalStart { Outcome = MfaApprovalOutcome.Succeeded, ApprovalRequestId = "a1", MatchNumber = "42", ExpiresInSeconds = 120 });
			var asked = Body(await controller.RequestApproval(CancellationToken.None));
			asked.Value<string>("matchNumber").Should().Be("42");

			approvals.Setup(a => a.GetForRequesterAsync("a1", MfaApprovalRequesterKind.Session, SessionId, It.IsAny<CancellationToken>()))
				.ReturnsAsync(MfaApprovalResult.Of(MfaApprovalOutcome.Succeeded, new MfaApprovalRequest
				{
					MfaApprovalRequestId = "a1", State = (int)MfaApprovalRequestState.Pending, ExpiresOnUtc = DateTime.UtcNow.AddMinutes(1)
				}));
			Body(await controller.ApprovalStatus("a1", CancellationToken.None)).Value<string>("state").Should().Be("pending");
			approvals.Setup(a => a.GetForRequesterAsync("a1", MfaApprovalRequesterKind.Session, SessionId, It.IsAny<CancellationToken>()))
				.ReturnsAsync(MfaApprovalResult.Of(MfaApprovalOutcome.Succeeded, new MfaApprovalRequest
				{
					MfaApprovalRequestId = "a1", State = (int)MfaApprovalRequestState.Pending, ExpiresOnUtc = DateTime.UtcNow.AddMinutes(-1)
				}));
			Body(await controller.ApprovalStatus("a1", CancellationToken.None)).Value<string>("state").Should().Be("expired", "a pending request past its time");

			adp.Setup(a => a.CompleteApprovalAsync(It.IsAny<AdpStepUpCaller>(), "a1", It.IsAny<CancellationToken>())).ReturnsAsync(grant);
			Body(await controller.CompleteApproval("a1", CancellationToken.None)).Value<string>("grantToken").Should().Be("T");
			adp.Setup(a => a.CompleteApprovalAsync(It.IsAny<AdpStepUpCaller>(), "a1", It.IsAny<CancellationToken>()))
				.ReturnsAsync(AdpGrantIssue.Of(AdpGrantOutcome.ApprovalPending));
			Body(await controller.CompleteApproval("a1", CancellationToken.None)).Value<string>("error").Should().Be("approval_pending");
		}
	}
}
