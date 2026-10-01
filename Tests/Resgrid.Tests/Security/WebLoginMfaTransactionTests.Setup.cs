using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Security;
using Resgrid.Repositories.DataRepository.Stores;
using Resgrid.Web.Models.AccountViewModels;
using IdentityUser = Resgrid.Model.Identity.IdentityUser;

namespace Resgrid.Tests.Security
{
	/// <summary>
	/// Passkey workbook section 12, slice 24 (Phase 3): the restricted setup transaction for a required-MFA account with no MFA yet
	/// (plan section 6.2), and "I lost my authenticator" through the restricted recovery transaction (sections 5.4 and 6.3). The
	/// real login and recovery transaction services run over in-memory stores; the authenticator key is staged, verified with a real
	/// TOTP code, and only then made active.
	/// </summary>
	public partial class WebLoginMfaTransactionTests
	{
		private static string CodeFor(string key, int stepOffset = 0) =>
			TotpCalculator.ComputeCode(TotpCalculator.Base32Decode(key), TotpCalculator.CurrentTimeStep(DateTime.UtcNow) + stepOffset).ToString("D6");

		private void RequiresMfaWithoutOne()
		{
			_users.Setup(m => m.GetTwoFactorEnabledAsync(It.IsAny<IdentityUser>())).ReturnsAsync(() => _totpTurnedOn);
			_signIn.Setup(s => s.PasswordSignInAsync("user1", "pw", true, true)).ReturnsAsync(Microsoft.AspNetCore.Identity.SignInResult.Success);
			_usersEnforced = TwoFactorConfig.RequireMfaEnforcementEnabled;
			TwoFactorConfig.RequireMfaEnforcementEnabled = true;
			_policy.RequireMfa = true;
		}

		private bool? _usersEnforced;

		[TearDown]
		public void RestoreEnforcement()
		{
			if (_usersEnforced is bool enforced)
				TwoFactorConfig.RequireMfaEnforcementEnabled = enforced;
			_usersEnforced = null;
		}

		// ---- Setup ------------------------------------------------------------------------------------------------------

		[Test]
		public async Task A_shared_workstations_required_setup_is_recorded_as_the_station()
		{
			_gates.SetupGet(g => g.SharedDeviceModeEnabled).Returns(true);
			RequiresMfaWithoutOne();

			(await Browser(workstation: "Desk 2").Controller.LogOn(new LoginViewModel { Username = "user1", Password = "pw" }, CancellationToken.None, "/User/Calls"))
				.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("LoginMfaSetup");
			Row.SharedMode.Should().BeTrue();
			Row.InstallationLabel.Should().Be("Desk 2");
		}

		[Test]
		public async Task A_required_mfa_account_without_mfa_sets_up_an_authenticator_before_any_session()
		{
			RequiresMfaWithoutOne();
			var (logOn, http) = Browser();

			(await logOn.LogOn(new LoginViewModel { Username = "user1", Password = "pw" }, CancellationToken.None, "/User/Calls"))
				.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("LoginMfaSetup");
			_issued.Should().BeEmpty("no ordinary access before required MFA exists");
			_signIn.Verify(s => s.SignOutAsync(), Times.AtLeast(2), "the password sign-in's own cookie is dropped");
			var secret = SecretFrom(http);
			Row.FirstFactorMethod.Should().Be((int)MfaEvidenceMethod.Password);

			(await Browser(secret).Controller.LoginMfa("/User/Calls", CancellationToken.None))
				.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("LoginMfaSetup", "a sign-in with nothing to choose sets one up");

			var page = Browser(secret);
			var model = (LoginMfaSetupViewModel)((ViewResult)await page.Controller.LoginMfaSetup("/User/Calls", CancellationToken.None)).Model;
			model.SharedKey.Should().Be(AuthenticatorSetup.FormatKey(StagedKey));
			model.AuthenticatorUri.Should().Contain("secret=" + StagedKey);
			model.QrCodeDataUrl.Should().StartWith("data:image/png;base64,");
			page.Http.Response.Headers.CacheControl.ToString().Should().Contain("no-store", "the secret page is never cached");
			_activeKey.Should().BeNull("a staged key is not the authenticator until a code from it verifies");

			var wrong = Browser(secret);
			(await wrong.Controller.LoginMfaSetup(new LoginMfaSetupViewModel { Code = "000000" }, CancellationToken.None)).Should().BeOfType<ViewResult>();
			Row.Attempts.Should().Be(1);
			_activeKey.Should().BeNull();

			var done = Browser(secret);
			var result = await done.Controller.LoginMfaSetup(new LoginMfaSetupViewModel { Code = CodeFor(StagedKey), ReturnUrl = "/User/Calls" }, CancellationToken.None);

			result.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("ShowRecoveryCodes");
			_activeKey.Should().Be(StagedKey);
			_totpTurnedOn.Should().BeTrue();
			done.Controller.TempData["RecoveryCodes"].Should().BeEquivalentTo(new[] { "new-1", "new-2" }, "shown once, on the signed-in page");
			_issued.Single().LoginMfaMethod.Should().Be(MfaEvidenceMethod.Totp);
			_recorded.Last().Should().Match<(MfaEvidenceKind Kind, MfaEvidenceMethod Method, DateTime At, string Reference, string SessionKey)>(e =>
				e.Kind == MfaEvidenceKind.SecondFactor && e.Method == MfaEvidenceMethod.Totp);
			_notices.Verify(n => n.QueueAsync(It.Is<SecurityNoticeRequest>(r => r.Kind == SecurityNoticeKind.TotpEnabled), It.IsAny<CancellationToken>()), Times.Once);
			_audits.Verify(a => a.SaveSystemAuditAsync(It.Is<SystemAudit>(x => x.Type == (int)SystemAuditTypes.TwoFactorEnabled && x.Successful),
				It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task Setup_is_only_for_an_account_without_an_authenticator_and_a_stale_key_is_never_activated()
		{
			RequiresMfaWithoutOne();
			var (logOn, http) = Browser();
			await logOn.LogOn(new LoginViewModel { Username = "user1", Password = "pw" }, CancellationToken.None, null);
			var secret = SecretFrom(http);
			await Browser(secret).Controller.LoginMfaSetup((string)null, CancellationToken.None);

			// The staged key outlived its lifetime: a new one is shown, and the old code cannot turn anything on.
			_tokens[StagedAuthenticatorKey.LoginProvider + "/" + StagedAuthenticatorKey.TokenName] =
				StagedAuthenticatorKey.Serialize(StagedKey, DateTime.UtcNow.AddHours(-2));
			var stale = Browser(secret);
			(await stale.Controller.LoginMfaSetup(new LoginMfaSetupViewModel { Code = CodeFor(StagedKey) }, CancellationToken.None)).Should().BeOfType<ViewResult>();
			stale.Controller.ModelState["Code"]!.Errors.Single().ErrorMessage.Should().Be("SetupExpired");
			_activeKey.Should().BeNull();

			_totpTurnedOn = true;
			(await Browser(secret).Controller.LoginMfaSetup((string)null, CancellationToken.None))
				.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("LoginMfa", "an account with an authenticator uses it");
		}

		[Test]
		public async Task Without_the_web_gate_the_password_sign_in_is_unchanged()
		{
			RequiresMfaWithoutOne();
			TwoFactorConfig.WebLoginMfaTransactionEnabled = false;

			var result = await Browser().Controller.LogOn(new LoginViewModel { Username = "user1", Password = "pw" }, CancellationToken.None, null);

			result.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("Dashboard", "the enrollment middleware confines it, as before");
			_rows.Rows.Should().BeEmpty();
		}

		// ---- I lost my authenticator -----------------------------------------------------------------------------------------

		[Test]
		public async Task A_recovery_code_opens_a_recovery_that_replaces_the_authenticator_and_signs_out_everywhere()
		{
			var secret = await SignInWithPassword();
			var lost = (LostFactorViewModel)((ViewResult)await Browser(secret).Controller.LostFactor("/User/Calls", CancellationToken.None)).Model;
			lost.CanRecover.Should().BeTrue();

			var wrong = Browser(secret);
			(await wrong.Controller.LostFactor(new LostFactorViewModel { Code = "nope" }, CancellationToken.None)).Should().BeOfType<ViewResult>();
			Row.Attempts.Should().Be(1, "a wrong code counts against the sign-in");
			_failures.Should().Be(1);
			_recoveryRows.Rows.Should().BeEmpty();

			var opened = Browser(secret);
			(await opened.Controller.LostFactor(new LostFactorViewModel { Code = "RC-1111" }, CancellationToken.None))
				.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("Recovery");
			Row.TransactionState.Should().Be(MfaLoginTransactionState.Exhausted, "the sign-in is spent; recovery takes over");
			SetCookie(opened.Http).ToLowerInvariant().Should().Contain(".resgrid.mfalogin=;");
			var recoveryCookie = opened.Http.Response.Headers.SetCookie.Single(c => c.StartsWith(".Resgrid.FactorRecovery=", StringComparison.Ordinal)).ToLowerInvariant();
			recoveryCookie.Should().Contain("httponly").And.Contain("secure").And.Contain("samesite=strict").And.Contain("path=/account");
			_issued.Should().BeEmpty("recovery never signs in");
			_notices.Verify(n => n.QueueAsync(It.Is<SecurityNoticeRequest>(r => r.Kind == SecurityNoticeKind.RecoveryCodeUsed), It.IsAny<CancellationToken>()), Times.Once);
			var recoverySecret = RecoverySecretFrom(opened.Http);

			var lostKey = new UserPasskey { UserPasskeyId = "pk-lost", UserId = UserId, ClientApplication = (int)UserSessionClientApplication.Web, DisplayName = "Old laptop",
				CreatedOnUtc = DateTime.UtcNow.AddDays(-9), StateVersion = 1 };
			_passkeyRows.Rows.Add(lostKey);
			_passkeyRows.Rows.Add(new UserPasskey { UserPasskeyId = "pk-kept", UserId = UserId, ClientApplication = (int)UserSessionClientApplication.Responder,
				DisplayName = "Phone", CreatedOnUtc = DateTime.UtcNow.AddDays(-3), StateVersion = 1 });

			var page = RecoveryBrowser(recoverySecret);
			var model = (FactorRecoveryViewModel)((ViewResult)await page.Controller.Recovery(CancellationToken.None)).Model;
			model.SharedKey.Should().Be(AuthenticatorSetup.FormatKey(StagedKey));
			model.Passkeys.Select(p => p.Id).Should().BeEquivalentTo("pk-lost", "pk-kept");

			(await RecoveryBrowser(recoverySecret).Controller.Recovery(new FactorRecoveryViewModel { Code = "000000", RemovePasskeyIds = { "pk-lost" } },
				CancellationToken.None)).Should().BeOfType<ViewResult>();
			_recoveryRows.Rows.Single().Attempts.Should().Be(1);
			_activeKey.Should().BeNull("nothing changes until the new authenticator's code verifies");
			lostKey.RevokedOnUtc.Should().BeNull();

			var generation = _user.AuthenticationGeneration;
			var complete = RecoveryBrowser(recoverySecret);
			var view = (await complete.Controller.Recovery(new FactorRecoveryViewModel { Code = CodeFor(StagedKey), RemovePasskeyIds = { "pk-lost" } },
				CancellationToken.None)).Should().BeOfType<ViewResult>().Subject;

			view.ViewName.Should().Be("RecoveryComplete");
			((RecoveryCompleteViewModel)view.Model).RecoveryCodes.Should().Equal("new-1", "new-2");
			_activeKey.Should().Be(StagedKey);
			_user.AuthenticationGeneration.Should().Be(generation + 1, "everything the old authenticator authorized ends");
			_sessions.Verify(s => s.RevokeAllAfterCredentialChangeAsync(UserId, UserId, UserSessionRevocationReason.MfaChanged, It.IsAny<DateTime>(),
				It.IsAny<CancellationToken>()), Times.Once);
			_evidence.Verify(e => e.RevokeForUserAsync(UserId, It.IsAny<CancellationToken>()), Times.Once);
			lostKey.RevokedOnUtc.Should().NotBeNull();
			_passkeyRows.Rows.Single(p => p.UserPasskeyId == "pk-kept").RevokedOnUtc.Should().BeNull("only the passkeys chosen as lost are removed");
			_challenges.Verify(c => c.CancelPendingForUserAsync(UserId, It.IsAny<CancellationToken>()), Times.Once);
			_approvalRows.Verify(a => a.CancelPendingForUserAsync(UserId, MfaApprovalEndReason.ApproverRevoked, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
				Times.Once);
			_notices.Verify(n => n.QueueAsync(It.Is<SecurityNoticeRequest>(r => r.Kind == SecurityNoticeKind.FactorRecoveryCompleted), It.IsAny<CancellationToken>()),
				Times.Once);
			_issued.Should().BeEmpty("the user signs in again normally");
			SetCookie(complete.Http).ToLowerInvariant().Should().Contain(".resgrid.factorrecovery=;");

			var again = RecoveryBrowser(recoverySecret);
			(await again.Controller.Recovery(new FactorRecoveryViewModel { Code = CodeFor(StagedKey, 1) }, CancellationToken.None))
				.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("LogOn", "a finished recovery is spent");
			again.Controller.TempData["LoginMfaMessage"].Should().Be("RecoveryEnded");
		}

		[Test]
		public async Task Two_completions_racing_for_one_recovery_change_the_authenticator_once()
		{
			var secret = await SignInWithPassword();
			var opened = Browser(secret);
			await opened.Controller.LostFactor(new LostFactorViewModel { Code = "RC-1111" }, CancellationToken.None);
			var recoverySecret = RecoverySecretFrom(opened.Http);
			await RecoveryBrowser(recoverySecret).Controller.Recovery(CancellationToken.None);
			var generation = _user.AuthenticationGeneration;

			// Another request with the same recovery completes it first, after this one opened it.
			_recoveryRows.BeforeTryComplete = () =>
			{
				_recoveryRows.BeforeTryComplete = null;
				_recoveryRows.Rows.Single().State = (int)FactorRecoveryState.Completed;
			};
			var late = RecoveryBrowser(recoverySecret);
			(await late.Controller.Recovery(new FactorRecoveryViewModel { Code = CodeFor(StagedKey) }, CancellationToken.None))
				.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("LogOn");
			late.Controller.TempData["LoginMfaMessage"].Should().Be("RecoveryEnded");
			_activeKey.Should().BeNull("only the request that completed the recovery changes the authenticator");
			_user.AuthenticationGeneration.Should().Be(generation);
			_sessions.Verify(s => s.RevokeAllAfterCredentialChangeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<UserSessionRevocationReason>(),
				It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
			_notices.Verify(n => n.QueueAsync(It.Is<SecurityNoticeRequest>(r => r.Kind == SecurityNoticeKind.FactorRecoveryCompleted), It.IsAny<CancellationToken>()),
				Times.Never);
		}

		[Test]
		public async Task A_recovery_cannot_remove_someone_elses_passkey_and_can_be_canceled()
		{
			var secret = await SignInWithPassword();
			var opened = Browser(secret);
			await opened.Controller.LostFactor(new LostFactorViewModel { Code = "RC-1111" }, CancellationToken.None);
			var recoverySecret = RecoverySecretFrom(opened.Http);
			_passkeyRows.Rows.Add(new UserPasskey { UserPasskeyId = "pk-other", UserId = "someone-else", ClientApplication = (int)UserSessionClientApplication.Web,
				CreatedOnUtc = DateTime.UtcNow, StateVersion = 1 });
			await RecoveryBrowser(recoverySecret).Controller.Recovery(CancellationToken.None);

			var refused = RecoveryBrowser(recoverySecret);
			(await refused.Controller.Recovery(new FactorRecoveryViewModel { Code = CodeFor(StagedKey), RemovePasskeyIds = { "pk-other" } }, CancellationToken.None))
				.Should().BeOfType<ViewResult>();
			refused.Controller.ModelState[string.Empty]!.Errors.Single().ErrorMessage.Should().Be("RecoveryPasskeyNotFound");
			_activeKey.Should().BeNull();

			(await RecoveryBrowser(recoverySecret).Controller.RecoveryCancel(CancellationToken.None))
				.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("LogOn");
			_recoveryRows.Rows.Single().RecoveryState.Should().Be(FactorRecoveryState.Canceled);
			_audits.Verify(a => a.SaveSystemAuditAsync(It.Is<SystemAudit>(x => x.Type == (int)SystemAuditTypes.FactorRecoveryCanceled), It.IsAny<CancellationToken>()),
				Times.Once);
		}

		[Test]
		public async Task Without_a_sign_in_the_lost_authenticator_page_only_says_who_can_help()
		{
			var page = (LostFactorViewModel)((ViewResult)await Browser().Controller.LostFactor((string)null, CancellationToken.None)).Model;
			page.CanRecover.Should().BeFalse("a recovery code works only after the password or single sign-on");

			(await Browser().Controller.LostFactor(new LostFactorViewModel { Code = "RC-1111" }, CancellationToken.None))
				.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("LogOn");
			_users.Verify(m => m.RedeemTwoFactorRecoveryCodeAsync(It.IsAny<IdentityUser>(), It.IsAny<string>()), Times.Never, "no code is spent without a sign-in");
		}

		private static string RecoverySecretFrom(Microsoft.AspNetCore.Http.HttpContext http)
		{
			var header = http.Response.Headers.SetCookie.Single(c => c.StartsWith(".Resgrid.FactorRecovery=", StringComparison.Ordinal));
			return Uri.UnescapeDataString(header.Substring(".Resgrid.FactorRecovery=".Length).Split(';')[0]);
		}

		private (Resgrid.Web.Controllers.AccountController Controller, Microsoft.AspNetCore.Http.DefaultHttpContext Http) RecoveryBrowser(string recoverySecret)
		{
			var browser = Browser();
			browser.Http.Request.Headers["Cookie"] = ".Resgrid.FactorRecovery=" + recoverySecret;
			return browser;
		}
	}
}
