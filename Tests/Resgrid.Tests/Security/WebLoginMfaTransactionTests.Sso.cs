using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Security;
using Resgrid.Web.Areas.User.Models.TwoFactor;
using Resgrid.Web.Helpers;
using Resgrid.Web.Models.AccountViewModels;

namespace Resgrid.Tests.Security
{
	/// <summary>
	/// Passkey workbook section 12, slice 22 (Phase 3): Core Web single sign-on and the Web's other round trips through the department's
	/// identity provider (plan sections 6.2, 7.7.2, 7.7.3 and 7.8). The broker is stubbed; the round trip's browser half (the protected
	/// cookie with the transaction id, PKCE verifier and state) and the login transaction are real.
	/// </summary>
	public partial class WebLoginMfaTransactionTests
	{
		private static readonly DateTime ProviderSignedInAt = new(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);
		private DepartmentSsoConfig _ssoConfig;
		private SsoBeginRequest _begun;
		private SsoTransactionPurpose[] _redeemedFor;

		private void SsoReady(bool registered = true)
		{
			_ssoConfig = new DepartmentSsoConfig
			{
				DepartmentSsoConfigId = "cfg", DepartmentId = DepartmentId, SsoProviderType = (int)SsoProviderType.Oidc, IsEnabled = true,
				FederatedMfaMappingJson = "{\"acceptAmr\":[\"mfa\"]}", FederatedMfaMappingVersion = 3, FederatedMfaTestedVersion = 3
			};
			_broker.SetupGet(b => b.IsEnabled).Returns(true);
			_broker.Setup(b => b.SupportsBrokered(It.IsAny<DepartmentSsoConfig>())).Returns(true);
			_returnTargets.Setup(r => r.IsAllowed(UserSessionClientApplication.Web, WebSsoRoundTrip.ReturnTarget)).Returns(registered);
			_sso.Setup(s => s.GetSsoConfigsForDepartmentAsync(DepartmentId, It.IsAny<CancellationToken>()))
				.ReturnsAsync(() => new List<DepartmentSsoConfig> { _ssoConfig });
			_sso.Setup(s => s.GetTestedFederatedMfaConfigAsync(DepartmentId, It.IsAny<CancellationToken>()))
				.ReturnsAsync(() => FederatedMfaMapping.IsTested(_ssoConfig) ? _ssoConfig : null);
			_broker.Setup(b => b.ResolveDepartmentAsync(null, null, "user1", It.IsAny<CancellationToken>()))
				.ReturnsAsync(new Department { DepartmentId = DepartmentId, Code = "DEPT" });
			_broker.Setup(b => b.BeginAsync(It.IsAny<SsoBeginRequest>(), It.IsAny<CancellationToken>()))
				.Callback((SsoBeginRequest r, CancellationToken _) => _begun = r)
				.ReturnsAsync(new SsoBeginResult { Outcome = SsoBrokerOutcome.Succeeded, AuthorizeUrl = "https://idp.example/authorize", TransactionId = "sso-1",
					ExpiresInSeconds = 600 });
		}

		/// <summary>The broker hands back this transaction for the round trip this browser began, once, with the right verifier.</summary>
		private void Redeems(SsoLoginTransaction transaction)
		{
			_broker.Setup(b => b.RedeemAsync("sso-1", "code-1", It.IsAny<string>(), UserSessionClientApplication.Web, It.IsAny<CancellationToken>(),
					It.IsAny<SsoTransactionPurpose[]>()))
				.Callback((string _, string _, string verifier, UserSessionClientApplication _, CancellationToken _, SsoTransactionPurpose[] purposes) =>
				{
					_redeemedFor = purposes;
					Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))).Should().Be(_begun.CodeChallenge, "only this browser's verifier matches");
				})
				.ReturnsAsync(() => new SsoRedemptionResult { Outcome = SsoBrokerOutcome.Succeeded, Transaction = transaction });
		}

		private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

		private static string TripFrom(HttpContext http)
		{
			var header = http.Response.Headers.SetCookie.Single(c => c.StartsWith(WebSsoRoundTrip.CookieName + "=", StringComparison.Ordinal));
			return header.Substring(WebSsoRoundTrip.CookieName.Length + 1).Split(';')[0];
		}

		private SsoLoginTransaction LoginAtProvider(string federatedValue = null, DateTime? signedInAt = null) => new()
		{
			SsoLoginTransactionId = "sso-1", Purpose = (int)SsoTransactionPurpose.Login, DepartmentId = DepartmentId, DepartmentSsoConfigId = "cfg",
			UserId = UserId, AuthenticatedOnUtc = signedInAt ?? ProviderSignedInAt, FederatedMfaValue = federatedValue,
			FederatedMappingVersion = federatedValue == null ? null : 3
		};

		/// <summary>Sign in with single sign-on up to the provider: returns this browser's round-trip cookie.</summary>
		private async Task<string> BeginSsoSignIn(string returnUrl = "/User/Calls")
		{
			var (controller, http) = Browser();
			(await controller.SsoLogOn(new SsoLogOnViewModel { Username = "user1", ReturnUrl = returnUrl }, CancellationToken.None))
				.Should().BeOfType<RedirectResult>().Which.Url.Should().Be("https://idp.example/authorize");
			return TripFrom(http);
		}

		// ---- Offering and starting ---------------------------------------------------------------------------------

		[Test]
		public async Task Sso_sign_in_is_offered_only_with_the_broker_the_webs_return_address_and_web_sign_in_on_the_transaction()
		{
			SsoReady();
			var (offered, _) = Browser();
			await offered.LogOn((string)null);
			offered.ViewData["SsoSignInAvailable"].Should().Be(true);

			SsoReady(registered: false);
			var (unregistered, _) = Browser();
			await unregistered.LogOn((string)null);
			unregistered.ViewData["SsoSignInAvailable"].Should().Be(false, "the deployment has not registered the Web's return address");
			unregistered.SsoLogOn((string)null).Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("LogOn");

			SsoReady();
			TwoFactorConfig_WebGate(false);
			var (gated, _) = Browser();
			await gated.LogOn((string)null);
			gated.ViewData["SsoSignInAvailable"].Should().Be(false, "a single sign-on continues on the login transaction");
		}

		private static void TwoFactorConfig_WebGate(bool on) => Resgrid.Config.TwoFactorConfig.WebLoginMfaTransactionEnabled = on;

		[Test]
		public async Task Starting_binds_the_round_trip_to_this_browser_and_reveals_nothing_about_unknown_accounts()
		{
			SsoReady();
			var (controller, http) = Browser();

			var result = await controller.SsoLogOn(new SsoLogOnViewModel { Username = " user1 ", ReturnUrl = "https://evil.example/" }, CancellationToken.None);

			result.Should().BeOfType<RedirectResult>().Which.Url.Should().Be("https://idp.example/authorize");
			_begun.Purpose.Should().Be(SsoTransactionPurpose.Login);
			_begun.ClientApplication.Should().Be(UserSessionClientApplication.Web);
			_begun.ReturnTarget.Should().Be(WebSsoRoundTrip.ReturnTarget);
			_begun.CodeChallengeMethod.Should().Be("S256");
			_begun.DepartmentId.Should().Be(DepartmentId);
			var cookie = http.Response.Headers.SetCookie.Single(c => c.StartsWith(WebSsoRoundTrip.CookieName + "=", StringComparison.Ordinal)).ToLowerInvariant();
			cookie.Should().Contain("httponly").And.Contain("secure").And.Contain("samesite=strict").And.Contain("path=/account/ssoreturn");

			var read = new DefaultHttpContext();
			read.Request.Headers["Cookie"] = WebSsoRoundTrip.CookieName + "=" + TripFrom(http);
			var trip = WebSsoRoundTrip.Take(read, _dataProtection);
			trip.TransactionId.Should().Be("sso-1");
			trip.State.Should().Be(_begun.ClientState);
			Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(trip.Verifier))).Should().Be(_begun.CodeChallenge, "the broker sees only the challenge");
			trip.Purpose.Should().Be(WebSsoPurpose.Login);
			trip.ReturnUrl.Should().BeNull("only a local return address is kept");

			var unknown = Browser().Controller;
			(await unknown.SsoLogOn(new SsoLogOnViewModel { Username = "nobody" }, CancellationToken.None)).Should().BeOfType<ViewResult>();
			unknown.ModelState[string.Empty]!.Errors.Single().ErrorMessage.Should().Be("SsoUnavailable");
			_broker.Verify(b => b.BeginAsync(It.IsAny<SsoBeginRequest>(), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task The_providers_cross_site_return_reads_and_changes_nothing_and_this_sites_own_page_posts_it_on()
		{
			SsoReady();
			Redeems(LoginAtProvider());
			var trip = await BeginSsoSignIn();

			// The arrival from the provider, which a browser sends without this site's SameSite=Strict cookies: nothing is read,
			// redeemed or removed, and the page cannot be cached, framed or leak its address.
			var arrival = Browser(ssoTrip: trip);
			var page = arrival.Controller.SsoReturn("code-1", _begun.ClientState, null).Should().BeOfType<ViewResult>().Subject;
			page.ViewName.Should().Be("SsoReturnContinue");
			var model = (SsoReturnContinueViewModel)page.Model;
			model.Code.Should().Be("code-1");
			model.State.Should().Be(_begun.ClientState);
			model.Error.Should().BeNull();
			arrival.Http.Response.Headers.SetCookie.Should().BeEmpty("the arrival removes nothing, not even the round trip");
			arrival.Http.Response.Headers.CacheControl.ToString().Should().Contain("no-store");
			arrival.Http.Response.Headers["Referrer-Policy"].ToString().Should().Be("no-referrer");
			arrival.Http.Response.Headers["Content-Security-Policy"].ToString().Should().Contain("default-src 'none'").And.Contain("script-src 'self'")
				.And.Contain("frame-ancestors 'none'");
			_broker.Verify(b => b.RedeemAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<UserSessionClientApplication>(),
				It.IsAny<CancellationToken>(), It.IsAny<SsoTransactionPurpose[]>()), Times.Never);

			// A post without the round-trip cookie (a cross-site post, for one) ends nothing this browser has under way.
			var crossSite = Browser();
			await crossSite.Controller.SsoReturnContinue("code-1", _begun.ClientState, null, CancellationToken.None);
			crossSite.Http.Response.Headers.SetCookie.Should().NotContain(c => c.StartsWith(WebSsoRoundTrip.CookieName + "=", StringComparison.Ordinal));

			// The post from this site's own page carries the round trip, and continues.
			var continued = Browser(ssoTrip: trip);
			(await continued.Controller.SsoReturnContinue(model.Code, model.State, model.Error, CancellationToken.None))
				.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("LoginMfa");
			SetCookie(continued.Http).Should().Contain(WebSsoRoundTrip.CookieName + "=;", "a round trip is read once");
		}

		[Test]
		public async Task A_return_that_is_not_this_browsers_own_redeems_nothing()
		{
			SsoReady();
			var trip = await BeginSsoSignIn();

			var noCookie = Browser();
			(await noCookie.Controller.SsoReturnContinue("code-1", _begun.ClientState, null, CancellationToken.None))
				.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("LogOn");
			noCookie.Controller.TempData["LoginMfaMessage"].Should().Be("SsoRestart");

			await Browser(ssoTrip: trip).Controller.SsoReturnContinue("code-1", "another-state", null, CancellationToken.None);
			await Browser(ssoTrip: "tampered").Controller.SsoReturnContinue("code-1", _begun.ClientState, null, CancellationToken.None);

			var refused = Browser(ssoTrip: trip);
			await refused.Controller.SsoReturnContinue(null, _begun.ClientState, "access_denied", CancellationToken.None);
			refused.Controller.TempData["LoginMfaMessage"].Should().Be("SsoDeniedByProvider");

			var errorWithCode = Browser(ssoTrip: await BeginSsoSignIn());
			await errorWithCode.Controller.SsoReturnContinue("code-1", _begun.ClientState, "sso_failed", CancellationToken.None);
			errorWithCode.Controller.TempData["LoginMfaMessage"].Should().Be("SsoFailed", "an error is never redeemed, whatever else came back");

			_broker.Verify(b => b.RedeemAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<UserSessionClientApplication>(),
				It.IsAny<CancellationToken>(), It.IsAny<SsoTransactionPurpose[]>()), Times.Never);
			SetCookie(refused.Http).Should().Contain(WebSsoRoundTrip.CookieName + "=;", "a round trip is read once");
		}

		// ---- Signing in ------------------------------------------------------------------------------------------------

		[Test]
		public async Task A_single_sign_on_with_an_authenticator_continues_on_the_login_transaction_and_signs_in_as_that_provider()
		{
			SsoReady();
			Redeems(LoginAtProvider());
			var trip = await BeginSsoSignIn();

			var back = Browser(ssoTrip: trip);
			(await back.Controller.SsoReturnContinue("code-1", _begun.ClientState, null, CancellationToken.None))
				.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("LoginMfa");
			_redeemedFor.Should().Equal(SsoTransactionPurpose.Login);
			Row.FirstFactorMethod.Should().Be((int)MfaEvidenceMethod.Sso);
			Row.FirstFactorVerifiedOnUtc.Should().Be(ProviderSignedInAt, "the provider's own time, not when the browser came back");
			Row.DepartmentSsoConfigId.Should().Be("cfg");
			_issued.Should().BeEmpty();

			var result = await Browser(SecretFrom(back.Http)).Controller.LoginMfa(new LoginMfaViewModel { Code = "123456", ReturnUrl = "/User/Calls" },
				CancellationToken.None);

			result.Should().BeOfType<LocalRedirectResult>().Which.Url.Should().Be("/User/Calls");
			_issued.Single().AuthenticationMethod.Should().Be(UserSessionAuthenticationMethod.OidcSso);
			_issued.Single().DepartmentSsoConfigId.Should().Be("cfg");
			_issued.Single().DepartmentId.Should().Be(DepartmentId);
			_recorded[0].Should().Match<(MfaEvidenceKind Kind, MfaEvidenceMethod Method, DateTime At, string Reference, string SessionKey)>(e =>
				e.Kind == MfaEvidenceKind.FirstFactor && e.Method == MfaEvidenceMethod.Sso && e.At == ProviderSignedInAt);
			_recorded[1].Method.Should().Be(MfaEvidenceMethod.Totp);
		}

		[Test]
		public async Task A_shared_workstations_single_sign_on_records_the_station_for_the_responder_it_may_ask()
		{
			_gates.SetupGet(g => g.SharedDeviceModeEnabled).Returns(true);
			SsoReady();
			Redeems(LoginAtProvider());
			var trip = await BeginSsoSignIn();

			await Browser(ssoTrip: trip, workstation: "Desk 2").Controller.SsoReturnContinue("code-1", _begun.ClientState, null, CancellationToken.None);

			Row.FirstFactorMethod.Should().Be((int)MfaEvidenceMethod.Sso);
			Row.SharedMode.Should().BeTrue();
			Row.InstallationLabel.Should().Be("Desk 2");
		}

		[Test]
		public async Task A_single_sign_on_without_mfa_signs_in_directly_unless_the_department_requires_mfa_then_it_sets_one_up()
		{
			SsoReady();
			_users.Setup(m => m.GetTwoFactorEnabledAsync(It.IsAny<Resgrid.Model.Identity.IdentityUser>())).ReturnsAsync(false);
			Redeems(LoginAtProvider());

			var result = await Browser(ssoTrip: await BeginSsoSignIn()).Controller.SsoReturnContinue("code-1", _begun.ClientState, null, CancellationToken.None);

			result.Should().BeOfType<LocalRedirectResult>().Which.Url.Should().Be("/User/Calls");
			_issued.Single().LoginMfaMethod.Should().BeNull();
			_issued.Single().AuthenticationMethod.Should().Be(UserSessionAuthenticationMethod.OidcSso);
			_recorded.Should().ContainSingle().Which.Kind.Should().Be(MfaEvidenceKind.FirstFactor, "no second factor is claimed");

			_policy.RequireMfa = true;
			_issued.Clear();
			_rows.Rows.Clear();
			var setup = Browser(ssoTrip: await BeginSsoSignIn());
			(await setup.Controller.SsoReturnContinue("code-1", _begun.ClientState, null, CancellationToken.None))
				.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("LoginMfaSetup");
			_issued.Should().BeEmpty("RequireMfa has always applied to single sign-on: no session before an authenticator is set up");
			Row.FirstFactorMethod.Should().Be((int)MfaEvidenceMethod.Sso);
			SecretFrom(setup.Http).Should().NotBeNullOrEmpty("the setup continues on the restricted transaction");
		}

		[Test]
		public async Task Provider_mfa_on_the_sign_in_itself_finishes_it_where_the_department_accepts_it()
		{
			SsoReady();
			_gates.SetupGet(g => g.ProviderStepUpEnabled).Returns(true);
			_policy.AllowFederatedMfaForLoginMfa = true;
			Redeems(LoginAtProvider("amr:mfa"));

			(await Browser(ssoTrip: await BeginSsoSignIn()).Controller.SsoReturnContinue("code-1", _begun.ClientState, null, CancellationToken.None))
				.Should().BeOfType<LocalRedirectResult>();

			_issued.Single().LoginMfaMethod.Should().Be(MfaEvidenceMethod.Federated);
			_issued.Single().LoginMfaFactorReference.Should().Be(FederatedMfaMapping.FactorReferenceFor("cfg", 3));
			_recorded.Last().Should().Be((MfaEvidenceKind.SecondFactor, MfaEvidenceMethod.Federated, ProviderSignedInAt, FederatedMfaMapping.FactorReferenceFor("cfg", 3),
				"sid:new-session-1"));

			// Where the department does not accept it, the provider's MFA is only a first factor and the second is still asked for.
			_policy.AllowFederatedMfaForLoginMfa = false;
			_issued.Clear();
			(await Browser(ssoTrip: await BeginSsoSignIn()).Controller.SsoReturnContinue("code-1", _begun.ClientState, null, CancellationToken.None))
				.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("LoginMfa");
			_issued.Should().BeEmpty();
		}

		[Test]
		public async Task A_provider_sign_in_to_another_department_makes_it_the_active_one()
		{
			SsoReady();
			_users.Setup(m => m.GetTwoFactorEnabledAsync(It.IsAny<Resgrid.Model.Identity.IdentityUser>())).ReturnsAsync(false);
			_activeDepartment = 7;
			Redeems(LoginAtProvider());

			await Browser(ssoTrip: await BeginSsoSignIn()).Controller.SsoReturnContinue("code-1", _begun.ClientState, null, CancellationToken.None);

			_departments.Verify(d => d.SetActiveDepartmentForUserAsync(UserId, DepartmentId, _user, It.IsAny<CancellationToken>()), Times.Once);
			_issued.Single().DepartmentId.Should().Be(DepartmentId);
		}

		[Test]
		public async Task A_lost_membership_or_a_disabled_configuration_stops_the_sign_in_before_the_session()
		{
			SsoReady();
			Redeems(LoginAtProvider());
			_membership = null;
			var noMember = Browser(ssoTrip: await BeginSsoSignIn());
			await noMember.Controller.SsoReturnContinue("code-1", _begun.ClientState, null, CancellationToken.None);
			noMember.Controller.TempData["LoginMfaMessage"].Should().Be("SsoAccessDenied");
			_rows.Rows.Should().BeEmpty();

			_membership = new DepartmentMember { UserId = UserId, DepartmentId = DepartmentId };
			var back = Browser(ssoTrip: await BeginSsoSignIn());
			await back.Controller.SsoReturnContinue("code-1", _begun.ClientState, null, CancellationToken.None);
			_ssoConfig.IsEnabled = false;
			var finish = Browser(SecretFrom(back.Http));
			(await finish.Controller.LoginMfa(new LoginMfaViewModel { Code = "123456" }, CancellationToken.None))
				.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("LogOn");
			finish.Controller.TempData["LoginMfaMessage"].Should().Be("LoginMfaSignInChanged");
			_issued.Should().BeEmpty();
		}

		// ---- Provider step-up that finishes a password sign-in -------------------------------------------------------------

		[Test]
		public async Task Provider_step_up_finishes_a_password_sign_in_only_with_its_own_round_trip()
		{
			SsoReady();
			_gates.SetupGet(g => g.ProviderStepUpEnabled).Returns(true);
			_policy.AllowFederatedMfaForLoginMfa = true;
			_sso.Setup(s => s.IsFederatedMfaAvailableAsync(DepartmentId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
			var secret = await SignInWithPassword();
			((LoginMfaViewModel)((ViewResult)await Browser(secret).Controller.LoginMfa((string)null, CancellationToken.None)).Model).Methods
				.Should().Contain(MfaMethodNames.Federated);
			_returnTargets.Setup(r => r.IsAllowed(UserSessionClientApplication.Web, WebSsoRoundTrip.ReturnTarget)).Returns(false);
			((LoginMfaViewModel)((ViewResult)await Browser(secret).Controller.LoginMfa((string)null, CancellationToken.None)).Model).Methods
				.Should().NotContain(MfaMethodNames.Federated, "the Web cannot come back from the provider");
			_returnTargets.Setup(r => r.IsAllowed(UserSessionClientApplication.Web, WebSsoRoundTrip.ReturnTarget)).Returns(true);

			var begin = Browser(secret);
			(await begin.Controller.LoginMfaBeginFederated("/User/Calls", CancellationToken.None)).Should().BeOfType<RedirectResult>();
			_begun.Purpose.Should().Be(SsoTransactionPurpose.StepUp);
			_begun.LoginTransactionId.Should().Be(Row.MfaLoginTransactionId);
			_begun.UserId.Should().Be(UserId);
			_begun.AuthenticationGeneration.Should().Be(4);
			_begun.Operation.Should().Be(SsoLoginTransaction.LoginOperation);
			var trip = TripFrom(begin.Http);

			var step = new SsoLoginTransaction
			{
				SsoLoginTransactionId = "sso-1", Purpose = (int)SsoTransactionPurpose.StepUp, DepartmentId = DepartmentId, DepartmentSsoConfigId = "cfg",
				LoginTransactionId = "someone-elses-sign-in", Operation = SsoLoginTransaction.LoginOperation, ExpectedUserId = UserId, UserId = UserId,
				AuthenticationGeneration = 4, FederatedMfaValue = "amr:mfa", FederatedMappingVersion = 3, AuthenticatedOnUtc = ProviderSignedInAt
			};
			Redeems(step);
			await Browser(secret, trip).Controller.SsoReturnContinue("code-1", _begun.ClientState, null, CancellationToken.None);
			Row.Attempts.Should().Be(1, "a round trip for another sign-in is a failed attempt");
			_issued.Should().BeEmpty();

			await Browser(secret).Controller.LoginMfaBeginFederated("/User/Calls", CancellationToken.None);
			trip = TripFrom(_lastHttp);
			step.LoginTransactionId = Row.MfaLoginTransactionId;
			var result = await Browser(secret, trip).Controller.SsoReturnContinue("code-1", _begun.ClientState, null, CancellationToken.None);

			result.Should().BeOfType<LocalRedirectResult>().Which.Url.Should().Be("/User/Calls");
			_redeemedFor.Should().Equal(SsoTransactionPurpose.StepUp);
			_issued.Single().LoginMfaMethod.Should().Be(MfaEvidenceMethod.Federated);
			_issued.Single().AuthenticationMethod.Should().Be(UserSessionAuthenticationMethod.LocalPassword, "the password was the first factor");
			_recorded.Last().At.Should().Be(ProviderSignedInAt);
		}

		// ---- Round trips for the signed-in session --------------------------------------------------------------------------

		private SsoLoginTransaction ForSession(SsoTransactionPurpose purpose, string sessionId = WebSession, string operation = null) => new()
		{
			SsoLoginTransactionId = "sso-1", Purpose = (int)purpose, DepartmentId = DepartmentId, DepartmentSsoConfigId = "cfg", SessionId = sessionId,
			ExpectedUserId = UserId, UserId = UserId, AuthenticationGeneration = 4, Operation = operation, AuthenticatedOnUtc = ProviderSignedInAt,
			FederatedMfaValue = "amr:mfa", FederatedMappingVersion = 3, CreatedOnUtc = ProviderSignedInAt.AddMinutes(-1)
		};

		private async Task<string> BeginForSession(string purpose, string scope = null, string returnUrl = "/User/Protected")
		{
			var (controller, http) = Browser(signedIn: true);
			(await controller.SsoSessionBegin(purpose, scope, returnUrl, CancellationToken.None)).Should().BeOfType<RedirectResult>();
			return TripFrom(http);
		}

		[Test]
		public async Task Reauthentication_records_fresh_provider_evidence_for_this_session_only()
		{
			SsoReady();
			var trip = await BeginForSession("reauthenticate", returnUrl: "/User/TwoFactor");
			_begun.Purpose.Should().Be(SsoTransactionPurpose.Reauthentication);
			_begun.SessionId.Should().Be(WebSession);
			_begun.UserId.Should().Be(UserId);

			Redeems(ForSession(SsoTransactionPurpose.Reauthentication, sessionId: "another-session"));
			var mismatch = Browser(ssoTrip: trip, signedIn: true);
			(await mismatch.Controller.SsoReturnContinue("code-1", _begun.ClientState, null, CancellationToken.None))
				.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("Reauthenticate");
			mismatch.Controller.TempData["StepUpMessage"].Should().Be("SsoAccountMismatch");
			_recorded.Should().BeEmpty();

			trip = await BeginForSession("reauthenticate", returnUrl: "/User/TwoFactor");
			Redeems(ForSession(SsoTransactionPurpose.Reauthentication));
			(await Browser(ssoTrip: trip, signedIn: true).Controller.SsoReturnContinue("code-1", _begun.ClientState, null, CancellationToken.None))
				.Should().BeOfType<LocalRedirectResult>().Which.Url.Should().Be("/User/TwoFactor");
			_recorded.Should().ContainSingle().Which.Should().Be((MfaEvidenceKind.FirstFactor, MfaEvidenceMethod.Sso, ProviderSignedInAt, null, "sid:" + WebSession));
			_evidence.Verify(e => e.RecordAsync(UserId, "sid:" + WebSession, UserSessionClientApplication.Web, MfaEvidenceKind.FirstFactor, MfaEvidenceMethod.Sso,
				MfaEvidencePurpose.Reauthentication, ProviderSignedInAt, 4, DepartmentId, null, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task Provider_step_up_for_a_guarded_action_becomes_evidence_for_that_actions_operation()
		{
			SsoReady();
			_gates.SetupGet(g => g.ProviderStepUpEnabled).Returns(true);
			_gates.SetupGet(g => g.AdpAcceptanceEnabled).Returns(true);
			_policy.AllowFederatedMfaForAdp = true;
			var trip = await BeginForSession("step_up", scope: "Adp");
			_begun.Purpose.Should().Be(SsoTransactionPurpose.StepUp);
			_begun.Operation.Should().Be(MfaStepUpOperations.AdpManagement);

			Redeems(ForSession(SsoTransactionPurpose.StepUp, operation: MfaStepUpOperations.SensitiveOperation));
			var wrongOperation = Browser(ssoTrip: trip, signedIn: true);
			(await wrongOperation.Controller.SsoReturnContinue("code-1", _begun.ClientState, null, CancellationToken.None))
				.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("Verify2FA");
			wrongOperation.Controller.TempData["StepUpMessage"].Should().Be("SsoProviderMfaNotConfirmed");
			_recorded.Should().BeEmpty();

			trip = await BeginForSession("step_up", scope: "Adp");
			Redeems(ForSession(SsoTransactionPurpose.StepUp, operation: MfaStepUpOperations.AdpManagement));
			(await Browser(ssoTrip: trip, signedIn: true).Controller.SsoReturnContinue("code-1", _begun.ClientState, null, CancellationToken.None))
				.Should().BeOfType<LocalRedirectResult>().Which.Url.Should().Be("/User/Protected");
			_recorded.Should().ContainSingle().Which.Should().Be((MfaEvidenceKind.SecondFactor, MfaEvidenceMethod.Federated, ProviderSignedInAt,
				FederatedMfaMapping.FactorReferenceFor("cfg", 3), "sid:" + WebSession));

			// Where the department does not accept the provider's MFA for that action, nothing is recorded.
			_policy.AllowFederatedMfaForAdp = false;
			trip = await BeginForSession("step_up", scope: "Adp");
			await Browser(ssoTrip: trip, signedIn: true).Controller.SsoReturnContinue("code-1", _begun.ClientState, null, CancellationToken.None);
			_recorded.Should().ContainSingle();
		}

		[Test]
		public async Task The_mapping_test_is_the_managing_members_after_a_resgrid_factor_and_makes_that_exact_version_effective()
		{
			SsoReady();
			// Provider step-up would otherwise count for security changes here: only the rules below refuse.
			_gates.SetupGet(g => g.ProviderStepUpEnabled).Returns(true);
			_policy.AllowFederatedMfaForLoginMfa = true;
			_evidence.Setup(e => e.GetLatestSecondFactorAsync(UserId, "sid:" + WebSession, 4, It.IsAny<CancellationToken>()))
				.ReturnsAsync(new MfaEvidence { Method = (int)MfaEvidenceMethod.Totp, VerifiedOnUtc = DateTime.UtcNow });
			var notManager = Browser(signedIn: true);
			(await notManager.Controller.SsoSessionBegin("mapping_test", null, null, CancellationToken.None))
				.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("FederatedMfa");
			notManager.Controller.TempData["FederatedMfaError"].Should().Be("FederatedMfaNeedsResgridMfaShort", "only the managing member tests it");

			_managingUserId = UserId;
			_evidence.Setup(e => e.GetLatestSecondFactorAsync(UserId, "sid:" + WebSession, 4, It.IsAny<CancellationToken>()))
				.ReturnsAsync(new MfaEvidence { Method = (int)MfaEvidenceMethod.Federated, VerifiedOnUtc = DateTime.UtcNow });
			await Browser(signedIn: true).Controller.SsoSessionBegin("mapping_test", null, null, CancellationToken.None);
			_broker.Verify(b => b.BeginAsync(It.IsAny<SsoBeginRequest>(), It.IsAny<CancellationToken>()), Times.Never,
				"provider step-up cannot approve testing itself");

			_evidence.Setup(e => e.GetLatestSecondFactorAsync(UserId, "sid:" + WebSession, 4, It.IsAny<CancellationToken>()))
				.ReturnsAsync(new MfaEvidence { Method = (int)MfaEvidenceMethod.Totp, VerifiedOnUtc = DateTime.UtcNow });
			var trip = await BeginForSession("mapping_test");
			_begun.Purpose.Should().Be(SsoTransactionPurpose.MappingTest);

			Redeems(ForSession(SsoTransactionPurpose.MappingTest));
			_sso.Setup(s => s.RecordFederatedMfaTestAsync("cfg", 3, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
			var passed = Browser(ssoTrip: trip, signedIn: true);
			(await passed.Controller.SsoReturnContinue("code-1", _begun.ClientState, null, CancellationToken.None))
				.Should().BeOfType<LocalRedirectResult>().Which.Url.Should().Be("/User/Security/FederatedMfa");
			passed.Controller.TempData["FederatedMfaSuccess"].Should().Be("FederatedMfaTestPassed");
			_audits.Verify(a => a.SaveSystemAuditAsync(It.Is<SystemAudit>(x => x.Type == (int)SystemAuditTypes.FederatedMfaMappingTested &&
				x.System == (int)SystemAuditSystems.Website && x.UserId == UserId), It.IsAny<CancellationToken>()), Times.Once);

			_sso.Setup(s => s.RecordFederatedMfaTestAsync("cfg", 3, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(false);
			trip = await BeginForSession("mapping_test");
			var changed = Browser(ssoTrip: trip, signedIn: true);
			await changed.Controller.SsoReturnContinue("code-1", _begun.ClientState, null, CancellationToken.None);
			changed.Controller.TempData["FederatedMfaError"].Should().Be("FederatedMfaTestChanged");
		}

		[Test]
		public async Task Protected_data_step_up_hands_the_grant_only_to_the_page_that_opened_the_popup()
		{
			SsoReady();
			var trip = await BeginForSession("adp");
			_begun.Purpose.Should().Be(SsoTransactionPurpose.AdpStepUp);
			AdpStepUpCaller caller = null;
			_adpStepUp.Setup(a => a.CompleteFederatedAsync(It.IsAny<AdpStepUpCaller>(), "sso-1", "code-1", It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.Callback((AdpStepUpCaller c, string _, string _, string _, CancellationToken _) => caller = c)
				.ReturnsAsync(new AdpGrantIssue { Outcome = AdpGrantOutcome.Issued, Token = "G1", GrantId = "g-1", ExpiresOnUtc = ProviderSignedInAt.AddMinutes(15),
					WindowMinutes = 15 });

			var view = (await Browser(ssoTrip: trip, signedIn: true).Controller.SsoReturnContinue("code-1", _begun.ClientState, null, CancellationToken.None))
				.Should().BeOfType<ViewResult>().Subject;

			view.ViewName.Should().Be("SsoReturnGrant");
			var grant = view.Model.Should().BeOfType<SsoReturnGrantViewModel>().Subject;
			grant.GrantToken.Should().Be("G1");
			grant.Error.Should().BeNull();
			caller.Session.SessionId.Should().Be(WebSession);
			caller.DepartmentId.Should().Be(DepartmentId);
			caller.ClientApplication.Should().Be(UserSessionClientApplication.Web);

			_adpStepUp.Setup(a => a.CompleteFederatedAsync(It.IsAny<AdpStepUpCaller>(), "sso-1", "code-1", It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(AdpGrantIssue.Of(AdpGrantOutcome.VerificationFailed));
			trip = await BeginForSession("adp");
			var failed = (SsoReturnGrantViewModel)((ViewResult)await Browser(ssoTrip: trip, signedIn: true).Controller
				.SsoReturnContinue("code-1", _begun.ClientState, null, CancellationToken.None)).Model;
			failed.GrantToken.Should().BeNull();
			failed.Error.Should().Be("mfa_verification_failed");
		}

		// ---- Entering another department (slice 23; plan section 7.6 row 5) ------------------------------------------------

		[Test]
		public async Task Verifying_to_enter_a_department_offers_and_uses_that_departments_methods()
		{
			const int Target = 7;
			var targetPolicy = new DepartmentSecurityPolicy { DepartmentId = Target, AllowPasskeysForLoginMfa = false, AllowResponderApproval = false };
			_sso.Setup(s => s.GetSecurityPolicyForDepartmentAsync(Target, It.IsAny<CancellationToken>())).ReturnsAsync(targetPolicy);
			_departments.Setup(d => d.IsMemberOfDepartmentAsync(Target, UserId)).ReturnsAsync(true);
			_passkeys.Setup(p => p.HasActiveForClientAsync(UserId, UserSessionClientApplication.Web, It.IsAny<CancellationToken>())).ReturnsAsync(true);
			_approvals.Setup(a => a.IsAvailableAsync(UserId, UserSessionClientApplication.Web, It.IsAny<CancellationToken>())).ReturnsAsync(true);

			var here = (StepUpVerifyViewModel)((ViewResult)await StepUp().Verify2FA("/User/Profile/YourDepartments", null, CancellationToken.None)).Model;
			here.PasskeyAvailable.Should().BeTrue();
			here.EntryDepartmentId.Should().BeNull();

			var entering = (StepUpVerifyViewModel)((ViewResult)await StepUp().Verify2FA("/User/Profile/YourDepartments", null, CancellationToken.None, Target)).Model;
			entering.EntryDepartmentId.Should().Be(Target);
			entering.PasskeyAvailable.Should().BeFalse("the department being entered does not accept passkeys");
			entering.ApprovalAvailable.Should().BeFalse();

			MfaApprovalRequester requester = null;
			_approvals.Setup(a => a.RequestAsync(It.IsAny<MfaApprovalRequester>(), It.IsAny<CancellationToken>()))
				.Callback((MfaApprovalRequester r, CancellationToken _) => requester = r)
				.ReturnsAsync(new MfaApprovalStart { Outcome = MfaApprovalOutcome.Succeeded, ApprovalRequestId = "ap-1", MatchNumber = "12" });
			await StepUp().Verify2FARequestApproval(null, CancellationToken.None, Target);
			requester.DepartmentId.Should().Be(Target, "the approval is judged by the department being entered");

			// A department the user does not belong to is not a lever: the active department's rules apply.
			_departments.Setup(d => d.IsMemberOfDepartmentAsync(Target, UserId)).ReturnsAsync(false);
			var outsider = (StepUpVerifyViewModel)((ViewResult)await StepUp().Verify2FA(null, null, CancellationToken.None, Target)).Model;
			outsider.EntryDepartmentId.Should().BeNull();
			outsider.PasskeyAvailable.Should().BeTrue();
			await StepUp().Verify2FARequestApproval(null, CancellationToken.None, Target);
			requester.DepartmentId.Should().Be(DepartmentId);
		}

		[Test]
		public void The_sso_page_arrives_with_the_department_to_enter_already_filled_in()
		{
			SsoReady();
			var model = (SsoLogOnViewModel)((ViewResult)Browser().Controller.SsoLogOn("/User/Home/Dashboard", " TGT ")).Model;
			model.DepartmentCode.Should().Be("TGT");
			model.ReturnUrl.Should().Be("/User/Home/Dashboard");
		}

		// ---- Verify2FA -----------------------------------------------------------------------------------------------

		[Test]
		public async Task Verify2FA_offers_the_provider_where_the_account_uses_it_and_the_guarded_action_accepts_it()
		{
			SsoReady();
			_gates.SetupGet(g => g.ProviderStepUpEnabled).Returns(true);
			_policy.AllowFederatedMfaForLoginMfa = true;
			_sso.Setup(s => s.IsFederatedMfaAvailableAsync(DepartmentId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(true);

			((StepUpVerifyViewModel)((ViewResult)await StepUp().Verify2FA("/User/Calls", null, CancellationToken.None)).Model).FederatedAvailable.Should().BeTrue();
			((StepUpVerifyViewModel)((ViewResult)await StepUp().Verify2FA(null, "Account", CancellationToken.None)).Model).FederatedAvailable
				.Should().BeFalse("account factors need a Resgrid factor");

			_sso.Setup(s => s.IsFederatedMfaAvailableAsync(DepartmentId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(false);
			((StepUpVerifyViewModel)((ViewResult)await StepUp().Verify2FA(null, null, CancellationToken.None)).Model).FederatedAvailable
				.Should().BeFalse("the account does not sign in through the provider");

			SsoReady(registered: false);
			_sso.Setup(s => s.IsFederatedMfaAvailableAsync(DepartmentId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
			((StepUpVerifyViewModel)((ViewResult)await StepUp().Verify2FA(null, null, CancellationToken.None)).Model).FederatedAvailable
				.Should().BeFalse("the Web has no registered return address");
		}
	}
}
