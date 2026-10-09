using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Security;
using Resgrid.Model.Services;
using Resgrid.Web.Helpers;
using Resgrid.Web.Models.AccountViewModels;
using IdentityUser = Resgrid.Model.Identity.IdentityUser;

namespace Resgrid.Tests.Security
{
	/// <summary>
	/// Web sign-in on a shared workstation, and the same operator's unlock through the identity provider (passkey plan sections
	/// 10.5 and 12.5; workbook section 12, slice 25).
	/// </summary>
	public partial class WebLoginMfaTransactionTests
	{
		// ---- Signing in on a shared workstation ---------------------------------------------------------------------

		[Test]
		public async Task A_shared_workstation_signs_in_to_a_shared_session_that_never_outlives_its_shift_or_remembers_the_browser()
		{
			_grantShared = true;
			var (controller, http) = Browser(workstation: "Desk 2");
			await controller.LogOn(new LoginViewModel { Username = "user1", Password = "pw" }, CancellationToken.None, "/User/Calls");
			_signIn.Verify(s => s.ForgetTwoFactorClientAsync(), Times.Once, "a browser remembered before it became a workstation is forgotten");

			var done = Browser(SecretFrom(http), workstation: "Desk 2");
			(await done.Controller.LoginMfa(new LoginMfaViewModel { Code = "123456", RememberBrowser = true, ReturnUrl = "/User/Calls" }, CancellationToken.None))
				.Should().BeOfType<LocalRedirectResult>().Which.Url.Should().Be("/User/Calls");

			_issued.Single().SharedModeRequested.Should().BeTrue();
			_issued.Single().DeviceName.Should().Be("Desk 2", "the station's own label names the installation");
			_signIn.Verify(s => s.RememberTwoFactorClientAsync(It.IsAny<IdentityUser>()), Times.Never, "a shared workstation is never remembered");
			_lastAuthentication.Verify(a => a.SignInAsync(It.IsAny<HttpContext>(), CookieAuthenticationDefaults.AuthenticationScheme, It.IsAny<System.Security.Claims.ClaimsPrincipal>(),
				It.Is<AuthenticationProperties>(p => !p.IsPersistent && p.ExpiresUtc!.Value <= DateTimeOffset.UtcNow.AddHours(2).AddSeconds(5) &&
					p.ExpiresUtc!.Value >= DateTimeOffset.UtcNow.AddHours(2).AddSeconds(-5))), Times.Once, "the cookie never outlives the shift");
			_audits.Verify(a => a.SaveSystemAuditAsync(It.Is<SystemAudit>(x => x.Type == (int)SystemAuditTypes.SharedSessionStarted &&
				x.System == (int)SystemAuditSystems.Website && x.Data.Contains("Desk 2")), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task A_shared_workstations_sign_in_is_shown_to_responder_as_shared_and_named_by_the_station()
		{
			_gates.SetupGet(g => g.SharedDeviceModeEnabled).Returns(true);
			var (controller, http) = Browser(workstation: "Desk 2");
			await controller.LogOn(new LoginViewModel { Username = "user1", Password = "pw" }, CancellationToken.None, "/User/Calls");
			Row.SharedMode.Should().BeTrue();
			Row.InstallationLabel.Should().Be("Desk 2");

			MfaApprovalRequester requester = null;
			_approvals.Setup(a => a.RequestAsync(It.IsAny<MfaApprovalRequester>(), It.IsAny<CancellationToken>()))
				.Callback((MfaApprovalRequester r, CancellationToken _) => requester = r)
				.ReturnsAsync(new MfaApprovalStart { Outcome = MfaApprovalOutcome.Succeeded, ApprovalRequestId = "ap-1", MatchNumber = "47", ExpiresInSeconds = 120 });
			await Browser(SecretFrom(http), workstation: "Desk 2").Controller.LoginMfaRequestApproval(null, CancellationToken.None);
			requester.SharedMode.Should().BeTrue();
			requester.InstallationLabel.Should().Be("Desk 2");
		}

		[Test]
		public async Task An_ordinary_browser_asks_for_no_shared_session_and_may_be_remembered()
		{
			var secret = await SignInWithPassword();
			await Browser(secret).Controller.LoginMfa(new LoginMfaViewModel { Code = "123456", RememberBrowser = true, ReturnUrl = "/User/Calls" }, CancellationToken.None);

			_issued.Single().SharedModeRequested.Should().BeFalse();
			_signIn.Verify(s => s.RememberTwoFactorClientAsync(It.IsAny<IdentityUser>()), Times.Once);
			_signIn.Verify(s => s.ForgetTwoFactorClientAsync(), Times.Never);
			_lastAuthentication.Verify(a => a.SignInAsync(It.IsAny<HttpContext>(), CookieAuthenticationDefaults.AuthenticationScheme, It.IsAny<System.Security.Claims.ClaimsPrincipal>(),
				It.Is<AuthenticationProperties>(p => p.ExpiresUtc!.Value >= DateTimeOffset.UtcNow.AddHours(8).AddSeconds(-5))), Times.Once);
			_audits.Verify(a => a.SaveSystemAuditAsync(It.Is<SystemAudit>(x => x.Type == (int)SystemAuditTypes.SharedSessionStarted), It.IsAny<CancellationToken>()),
				Times.Never);
		}

		[Test]
		public async Task A_workstation_request_the_server_does_not_grant_is_an_ordinary_session()
		{
			// The gate is off at the server: the session is personal, and the cookie keeps its normal lifetime.
			var (controller, http) = Browser(workstation: "Desk 2");
			await controller.LogOn(new LoginViewModel { Username = "user1", Password = "pw" }, CancellationToken.None, "/User/Calls");
			await Browser(SecretFrom(http), workstation: "Desk 2").Controller.LoginMfa(new LoginMfaViewModel { Code = "123456", ReturnUrl = "/User/Calls" },
				CancellationToken.None);

			_issued.Single().SharedModeRequested.Should().BeTrue();
			_lastAuthentication.Verify(a => a.SignInAsync(It.IsAny<HttpContext>(), CookieAuthenticationDefaults.AuthenticationScheme, It.IsAny<System.Security.Claims.ClaimsPrincipal>(),
				It.Is<AuthenticationProperties>(p => p.ExpiresUtc!.Value >= DateTimeOffset.UtcNow.AddHours(8).AddSeconds(-5))), Times.Once);
			_audits.Verify(a => a.SaveSystemAuditAsync(It.Is<SystemAudit>(x => x.Type == (int)SystemAuditTypes.SharedSessionStarted), It.IsAny<CancellationToken>()),
				Times.Never);
		}

		[Test]
		public async Task The_older_second_factor_page_never_remembers_a_shared_workstation()
		{
			_signIn.Setup(s => s.GetTwoFactorAuthenticationUserAsync()).ReturnsAsync(_user);
			_signIn.Setup(s => s.TwoFactorAuthenticatorSignInAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>()))
				.ReturnsAsync(Microsoft.AspNetCore.Identity.SignInResult.Failed);

			await Browser(workstation: "Desk 2").Controller.LoginWith2fa(new VerifyCodeViewModel { Code = "123456", RememberBrowser = true }, CancellationToken.None);
			_signIn.Verify(s => s.TwoFactorAuthenticatorSignInAsync("123456", It.IsAny<bool>(), false), Times.Once);

			await Browser().Controller.LoginWith2fa(new VerifyCodeViewModel { Code = "123456", RememberBrowser = true }, CancellationToken.None);
			_signIn.Verify(s => s.TwoFactorAuthenticatorSignInAsync("123456", It.IsAny<bool>(), true), Times.Once);
		}

		[Test]
		public async Task The_older_second_factor_page_restarts_sign_in_when_the_partial_sign_in_expired()
		{
			_signIn.Setup(s => s.GetTwoFactorAuthenticationUserAsync()).ReturnsAsync((IdentityUser)null);

			var expired = Browser();
			var result = await expired.Controller.LoginWith2fa(new VerifyCodeViewModel { Code = "123456" }, CancellationToken.None);

			result.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("LogOn");
			expired.Controller.TempData["LoginMfaMessage"].Should().Be("LoginMfaExpired");
			_signIn.Verify(s => s.TwoFactorAuthenticatorSignInAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never);
		}

		[Test]
		public async Task The_older_recovery_code_page_restarts_sign_in_when_the_partial_sign_in_expired()
		{
			_signIn.Setup(s => s.GetTwoFactorAuthenticationUserAsync()).ReturnsAsync((IdentityUser)null);

			var expired = Browser();
			var result = await expired.Controller.LoginWithRecoveryCode(new VerifyCodeViewModel { Code = "ABCD-1234" }, CancellationToken.None);

			result.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("LogOn");
			expired.Controller.TempData["LoginMfaMessage"].Should().Be("LoginMfaExpired");
			_signIn.Verify(s => s.TwoFactorRecoveryCodeSignInAsync(It.IsAny<string>()), Times.Never);
		}

		[Test]
		public async Task The_sign_in_page_names_the_workstation_only_where_shared_mode_is_on_and_says_when_a_shift_ended()
		{
			PasskeyConfig.SharedDeviceModeEnabled = true;
			var (named, _) = Browser(workstation: "Desk 2");
			await named.LogOn((string)null);
			named.ViewData["SharedWorkstation"].Should().Be("Desk 2");
			named.ViewData["SharedWorkstationAvailable"].Should().Be(true);

			var (ordinary, _) = Browser();
			await ordinary.LogOn((string)null);
			ordinary.ViewData["SharedWorkstation"].Should().BeNull();

			var (ended, endedHttp) = Browser();
			endedHttp.Request.QueryString = new QueryString("?reason=shift_ended");
			await ended.LogOn((string)null);
			ended.ViewData["LoginMfaMessage"].Should().Be("SharedShiftEnded");

			PasskeyConfig.SharedDeviceModeEnabled = false;
			var (off, _) = Browser(workstation: "Desk 2");
			await off.LogOn((string)null);
			off.ViewData["SharedWorkstation"].Should().BeNull("the deployment has shared mode off");
			off.ViewData["SharedWorkstationAvailable"].Should().Be(false);
		}

		[Test]
		public async Task A_shared_workstation_asks_the_provider_to_let_the_operator_choose_the_account()
		{
			SsoReady();
			PasskeyConfig.SharedDeviceModeEnabled = true;
			await Browser(workstation: "Desk 2").Controller.SsoLogOn(new SsoLogOnViewModel { Username = "user1" }, CancellationToken.None);
			_begun.SharedInstallation.Should().BeTrue("an existing provider session must not silently sign in the previous operator");

			await Browser().Controller.SsoLogOn(new SsoLogOnViewModel { Username = "user1" }, CancellationToken.None);
			_begun.SharedInstallation.Should().BeFalse();
		}

		// ---- Unlocking through the identity provider -------------------------------------------------------------------

		private Mock<ISharedSessionService> SharedSessions()
		{
			var shared = new Mock<ISharedSessionService>();
			shared.Setup(s => s.CanUnlockAsync(It.IsAny<UserSession>(), It.IsAny<CancellationToken>())).ReturnsAsync(SharedSessionOutcome.Succeeded);
			shared.Setup(s => s.UnlockAsync(It.IsAny<UserSession>(), It.IsAny<long>(), It.IsAny<MfaEvidenceMethod>(), It.IsAny<string>(), It.IsAny<DateTime>(),
					It.IsAny<SharedSessionRequestInfo>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((UserSession s, long version, MfaEvidenceMethod _, string _, DateTime _, SharedSessionRequestInfo _, CancellationToken _) =>
					SharedSessionTransition.Of(version == s.LockVersion ? SharedSessionOutcome.Succeeded : SharedSessionOutcome.LockChanged, s.LockVersion));
			_sharedSessions = shared.Object;
			return shared;
		}

		private void ProviderUnlockAllowed()
		{
			_gates.SetupGet(g => g.ProviderStepUpEnabled).Returns(true);
			_policy.AllowFederatedMfaForLoginMfa = true;
			_sso.Setup(s => s.IsFederatedMfaAvailableAsync(DepartmentId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
		}

		private static UserSession LockedWorkstation(long version = 3) => new()
		{
			UserSessionId = WebSession, UserId = UserId, DepartmentId = DepartmentId, AuthenticationGeneration = 4, State = (int)UserSessionState.Active,
			ClientApplication = (int)UserSessionClientApplication.Web, SharedMode = true, IsLocked = true, LockVersion = version,
			LockedOnUtc = DateTime.UtcNow.AddMinutes(-1), LockReason = (int)SharedSessionLockReason.Idle, DeviceName = "Desk 2"
		};

		/// <summary>A request from the locked workstation: session validation handed over the locked row and nothing else.</summary>
		private (Resgrid.Web.Controllers.AccountController Controller, DefaultHttpContext Http) LockedBrowser(UserSession locked, string ssoTrip = null)
		{
			var browser = Browser(ssoTrip: ssoTrip, signedIn: true);
			browser.Http.Items.Remove(ProtectedGrantSessionContext.HttpItemKey);
			browser.Http.Items[WebSharedSession.SessionItemKey] = locked;
			return browser;
		}

		private SsoLoginTransaction UnlockAtProvider(DateTime createdOnUtc) => new()
		{
			SsoLoginTransactionId = "sso-1", Purpose = (int)SsoTransactionPurpose.StepUp, DepartmentId = DepartmentId, DepartmentSsoConfigId = "cfg",
			Operation = SsoLoginTransaction.SharedUnlockOperation, SessionId = WebSession, ExpectedUserId = UserId, UserId = UserId, AuthenticationGeneration = 4,
			FederatedMfaValue = "amr:mfa", FederatedMappingVersion = 3, AuthenticatedOnUtc = ProviderSignedInAt, CreatedOnUtc = createdOnUtc
		};

		[Test]
		public async Task A_locked_workstation_unlocks_through_the_provider_only_with_its_own_round_trip_begun_after_the_lock()
		{
			SsoReady();
			ProviderUnlockAllowed();
			var shared = SharedSessions();
			var locked = LockedWorkstation();

			var begin = LockedBrowser(locked);
			(await begin.Controller.SsoUnlockBegin(3, "/User/Calls", CancellationToken.None))
				.Should().BeOfType<RedirectResult>().Which.Url.Should().Be("https://idp.example/authorize");
			_begun.Purpose.Should().Be(SsoTransactionPurpose.StepUp);
			_begun.Operation.Should().Be(SsoLoginTransaction.SharedUnlockOperation);
			_begun.SessionId.Should().Be(WebSession);
			_begun.UserId.Should().Be(UserId);
			_begun.AuthenticationGeneration.Should().Be(4);
			var trip = TripFrom(begin.Http);

			// A round trip from before the lock never unlocks it, and counts as a failed unlock.
			var step = UnlockAtProvider(locked.LockedOnUtc!.Value.AddMinutes(-5));
			Redeems(step);
			var early = LockedBrowser(locked, trip);
			(await early.Controller.SsoReturnContinue("code-1", _begun.ClientState, null, CancellationToken.None))
				.Should().BeOfType<LocalRedirectResult>().Which.Url.Should().Be(WebSharedSession.LockedUrl("/User/Calls"));
			early.Controller.TempData["SharedUnlockMessage"].Should().Be("SsoProviderMfaNotConfirmed");
			shared.Verify(s => s.UnlockAsync(It.IsAny<UserSession>(), It.IsAny<long>(), It.IsAny<MfaEvidenceMethod>(), It.IsAny<string>(), It.IsAny<DateTime>(),
				It.IsAny<SharedSessionRequestInfo>(), It.IsAny<CancellationToken>()), Times.Never);
			shared.Verify(s => s.RecordFailedUnlockAsync(locked, MfaMethodNames.Federated, It.Is<SharedSessionRequestInfo>(i => i.AuditSystem == SystemAuditSystems.Website),
				It.IsAny<CancellationToken>()), Times.Once);

			// Begun after it, for this session and operator: the same session resumes at the provider's own time.
			var again = LockedBrowser(locked);
			await again.Controller.SsoUnlockBegin(3, "/User/Calls", CancellationToken.None);
			step.CreatedOnUtc = DateTime.UtcNow;
			var back = LockedBrowser(locked, TripFrom(again.Http));
			(await back.Controller.SsoReturnContinue("code-1", _begun.ClientState, null, CancellationToken.None))
				.Should().BeOfType<LocalRedirectResult>().Which.Url.Should().Be("/User/Calls");
			_redeemedFor.Should().Equal(SsoTransactionPurpose.StepUp);
			shared.Verify(s => s.UnlockAsync(locked, 3, MfaEvidenceMethod.Federated, FederatedMfaMapping.FactorReferenceFor("cfg", 3), ProviderSignedInAt,
				It.Is<SharedSessionRequestInfo>(i => i.AuditSystem == SystemAuditSystems.Website), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task Provider_unlock_is_refused_for_a_changed_lock_or_where_the_department_does_not_accept_it()
		{
			SsoReady();
			ProviderUnlockAllowed();
			var shared = SharedSessions();
			var locked = LockedWorkstation();

			var stale = LockedBrowser(locked);
			(await stale.Controller.SsoUnlockBegin(2, "/User/Calls", CancellationToken.None))
				.Should().BeOfType<LocalRedirectResult>().Which.Url.Should().Be(WebSharedSession.LockedUrl("/User/Calls"));
			stale.Controller.TempData["SharedUnlockMessage"].Should().Be("SharedUnlockLockChanged");
			_begun.Should().BeNull("nothing starts for a lock the screen no longer shows");

			_policy.AllowFederatedMfaForLoginMfa = false;
			var refused = LockedBrowser(locked);
			await refused.Controller.SsoUnlockBegin(3, "/User/Calls", CancellationToken.None);
			refused.Controller.TempData["SharedUnlockMessage"].Should().Be("SsoUnavailable");
			_begun.Should().BeNull();
			_policy.AllowFederatedMfaForLoginMfa = true;

			shared.Setup(s => s.CanUnlockAsync(It.IsAny<UserSession>(), It.IsAny<CancellationToken>())).ReturnsAsync(SharedSessionOutcome.SsoReauthenticationRequired);
			var needsSignIn = LockedBrowser(locked);
			await needsSignIn.Controller.SsoUnlockBegin(3, "/User/Calls", CancellationToken.None);
			needsSignIn.Controller.TempData["SharedUnlockMessage"].Should().Be("SsoUnavailable");
			_begun.Should().BeNull();
			shared.Setup(s => s.CanUnlockAsync(It.IsAny<UserSession>(), It.IsAny<CancellationToken>())).ReturnsAsync(SharedSessionOutcome.Succeeded);

			// The session locked again while the operator was at the provider: that round trip is for the old lock.
			var begin = LockedBrowser(locked);
			await begin.Controller.SsoUnlockBegin(3, "/User/Calls", CancellationToken.None);
			Redeems(UnlockAtProvider(DateTime.UtcNow));
			var relocked = LockedWorkstation(4);
			var back = LockedBrowser(relocked, TripFrom(begin.Http));
			(await back.Controller.SsoReturnContinue("code-1", _begun.ClientState, null, CancellationToken.None))
				.Should().BeOfType<LocalRedirectResult>().Which.Url.Should().Be(WebSharedSession.LockedUrl("/User/Calls"));
			back.Controller.TempData["SharedUnlockMessage"].Should().Be("SharedUnlockLockChanged");
			_redeemedFor.Should().BeNull("the code is not redeemed for a lock it was not begun in");
			shared.Verify(s => s.UnlockAsync(It.IsAny<UserSession>(), It.IsAny<long>(), It.IsAny<MfaEvidenceMethod>(), It.IsAny<string>(), It.IsAny<DateTime>(),
				It.IsAny<SharedSessionRequestInfo>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task While_locked_the_providers_return_finishes_nothing_but_its_own_unlock()
		{
			SsoReady();
			SharedSessions();
			Redeems(LoginAtProvider("amr:mfa"));

			// A step-up begun before the lock, returning after it.
			var protection = new DefaultHttpContext();
			WebSsoRoundTrip.Store(protection, _dataProtection, new WebSsoRoundTripState
			{
				TransactionId = "sso-1", Verifier = "verifier", State = "state-1", Purpose = WebSsoPurpose.StepUp, ReturnUrl = "/User/Calls",
				CreatedOnUtc = DateTime.UtcNow
			}, 600);
			var trip = TripFrom(protection);

			var back = LockedBrowser(LockedWorkstation(), trip);
			(await back.Controller.SsoReturnContinue("code-1", "state-1", null, CancellationToken.None))
				.Should().BeOfType<LocalRedirectResult>().Which.Url.Should().Be(WebSharedSession.LockedUrl("/User/Calls"));
			_redeemedFor.Should().BeNull("a locked session redeems nothing else");
		}

		[Test]
		public async Task An_unlocked_session_has_nothing_to_unlock_through_the_provider()
		{
			SsoReady();
			ProviderUnlockAllowed();
			SharedSessions();
			var active = LockedWorkstation();
			active.IsLocked = false;

			(await LockedBrowser(active).Controller.SsoUnlockBegin(3, "/User/Calls", CancellationToken.None))
				.Should().BeOfType<LocalRedirectResult>().Which.Url.Should().Be("/User/Calls");
			(await Browser().Controller.SsoUnlockBegin(3, "/User/Calls", CancellationToken.None))
				.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("LogOn");
			_begun.Should().BeNull();
		}
	}
}
