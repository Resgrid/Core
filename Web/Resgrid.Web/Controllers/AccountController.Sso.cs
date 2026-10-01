using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Security;
using Resgrid.Web.Helpers;
using Resgrid.Web.Models.AccountViewModels;
using IdentityUser = Resgrid.Model.Identity.IdentityUser;

namespace Resgrid.Web.Controllers
{
	/// <summary>
	/// Core Web single sign-on (passkey plan sections 7.7.2 and 7.7.3) and the Web's other round trips through the department's
	/// identity provider: reauthentication (section 6.2), provider step-up at sign-in, for a guarded action and for protected
	/// data, and the managing member's mapping test (section 7.8). Every one returns to <c>Account/SsoReturn</c>, which redeems
	/// the broker's one-time code once with this browser's PKCE verifier; no IdP token or assertion ever reaches the browser.
	/// </summary>
	public partial class AccountController
	{
		/// <summary>Web SSO sign-in needs the broker, the Web's registered return address, and Web sign-in on the login transaction.</summary>
		private bool WebSsoAvailable => UseLoginTransaction && WebSsoRoundTrip.IsAvailable(_ssoBroker, _ssoReturnTargets);

		//
		// GET: /Account/SsoLogOn
		[HttpGet]
		[AllowAnonymous]
		public IActionResult SsoLogOn(string returnUrl = null, string departmentCode = null)
		{
			if (!WebSsoAvailable)
				return RedirectToAction(nameof(LogOn), new { returnUrl = SafeReturnUrl(returnUrl) });

			// A department that requires its own SSO sends the user here with its code already filled in (plan section 7.6 row 5).
			return View(new SsoLogOnViewModel { ReturnUrl = SafeReturnUrl(returnUrl), DepartmentCode = departmentCode?.Trim() });
		}

		//
		// POST: /Account/SsoLogOn
		[HttpPost]
		[AllowAnonymous]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> SsoLogOn(SsoLogOnViewModel model, CancellationToken cancellationToken)
		{
			model ??= new SsoLogOnViewModel();
			model.ReturnUrl = SafeReturnUrl(model.ReturnUrl);
			if (!WebSsoAvailable)
				return RedirectToAction(nameof(LogOn), new { returnUrl = model.ReturnUrl });

			var username = model.Username?.Trim();
			var departmentCode = model.DepartmentCode?.Trim();
			if (string.IsNullOrWhiteSpace(username) && string.IsNullOrWhiteSpace(departmentCode))
			{
				ModelState.AddModelError(string.Empty, _twoFactorLocalizer["SsoLogOnNeedsEntry"]);
				return View(model);
			}

			// An unknown account or department reads the same as one without single sign-on: nothing about either is revealed.
			var department = await _ssoBroker.ResolveDepartmentAsync(null, string.IsNullOrWhiteSpace(username) ? departmentCode : null,
				string.IsNullOrWhiteSpace(username) ? null : username, cancellationToken);
			var authorizeUrl = department == null
				? null
				: await BeginRoundTripAsync(department, SsoTransactionPurpose.Login, WebSsoPurpose.Login, model.ReturnUrl, cancellationToken);
			if (authorizeUrl == null)
			{
				ModelState.AddModelError(string.Empty, _twoFactorLocalizer["SsoUnavailable"]);
				return View(model);
			}

			return Redirect(authorizeUrl);
		}

		/// <summary>
		/// Starts a round trip through the department's identity provider for this browser: the broker keeps the IdP state and nonce,
		/// and this browser keeps the transaction id, the PKCE verifier and the state. Null when the broker refused.
		/// </summary>
		private async Task<string> BeginRoundTripAsync(Department department, SsoTransactionPurpose brokerPurpose, WebSsoPurpose purpose, string returnUrl,
			CancellationToken cancellationToken, string sessionId = null, string userId = null, long? generation = null, string operation = null,
			string loginTransactionId = null, string scope = null)
		{
			var (verifier, challenge, state) = WebSsoRoundTrip.NewSecrets();
			SsoBeginResult begun;
			try
			{
				begun = await _ssoBroker.BeginAsync(new SsoBeginRequest
				{
					DepartmentId = department.DepartmentId,
					DepartmentCode = department.Code,
					Purpose = brokerPurpose,
					ClientApplication = UserSessionClientApplication.Web,
					Platform = "web",
					ReturnTarget = WebSsoRoundTrip.ReturnTarget,
					ClientState = state,
					CodeChallenge = challenge,
					CodeChallengeMethod = "S256",
					SessionId = sessionId,
					UserId = userId,
					AuthenticationGeneration = generation,
					Operation = operation,
					LoginTransactionId = loginTransactionId,
					// A shared workstation asks the provider to let the operator choose the account (plan section 12.5.2).
					SharedInstallation = WebSharedSession.IsAvailable && WebSharedSession.IsWorkstation(Request)
				}, cancellationToken);
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				Logging.LogException(ex, "A Web SSO round trip could not be started.");
				return null;
			}

			if (!begun.Succeeded)
				return null;

			WebSsoRoundTrip.Store(HttpContext, _dataProtection, new WebSsoRoundTripState
			{
				TransactionId = begun.TransactionId,
				Verifier = verifier,
				State = state,
				Purpose = purpose,
				ReturnUrl = SafeReturnUrl(returnUrl),
				Scope = scope,
				CreatedOnUtc = DateTime.UtcNow
			}, begun.ExpiresInSeconds);
			return begun.AuthorizeUrl;
		}

		//
		// GET: /Account/SsoReturn
		/// <summary>
		/// Where the broker sends the browser back: a one-time code and this browser's state. The browser arrives from the identity
		/// provider, a cross-site navigation, so it sends none of this site's cookies: the Web's cookie policy makes every cookie
		/// <c>SameSite=Strict</c>, including the round trip, the sign-in, recovery and session cookies. This page therefore reads and
		/// changes nothing. It hands the code and state to <see cref="SsoReturnContinue"/> in a form that this site's own page posts:
		/// a same-site request, which carries them.
		/// </summary>
		[HttpGet]
		[AllowAnonymous]
		public IActionResult SsoReturn([FromQuery(Name = "sso_code")] string code, [FromQuery(Name = "state")] string state,
			[FromQuery(Name = "error")] string error)
		{
			SetNoStoreHeaders();
			Response.Headers["Content-Security-Policy"] = "default-src 'none'; script-src 'self'; base-uri 'none'; frame-ancestors 'none'";
			return View("SsoReturnContinue", new SsoReturnContinueViewModel { Code = code, State = state, Error = error });
		}

		//
		// POST: /Account/SsoReturn
		/// <summary>
		/// The return, posted on by this site's own page with the browser's cookies. The code is redeemed once, only for what this
		/// browser began, and only with its PKCE verifier; a return that is not this browser's own redeems nothing. The state bound to
		/// this browser's round-trip cookie is what protects this post, as it protects any OAuth return, so it takes no antiforgery
		/// token: a token issued on the cookieless arrival page would be for the wrong user and the wrong antiforgery cookie.
		/// </summary>
		[HttpPost]
		[AllowAnonymous]
		[IgnoreAntiforgeryToken]
		[ActionName(nameof(SsoReturn))]
		public async Task<IActionResult> SsoReturnContinue([FromForm(Name = "sso_code")] string code, [FromForm(Name = "state")] string state,
			[FromForm(Name = "error")] string error, CancellationToken cancellationToken)
		{
			var trip = WebSsoRoundTrip.Take(HttpContext, _dataProtection);
			if (trip == null || !WebSsoRoundTrip.StateMatches(trip, state))
				return SsoFailed(null, "SsoRestart");
			// A locked shared session finishes only its own unlock here; anything else waits for the unlock (plan section 12.5.3).
			if (WebSharedSession.IsLocked(HttpContext) && trip.Purpose != WebSsoPurpose.SharedUnlock)
				return LocalRedirect(WebSharedSession.LockedUrl(trip.ReturnUrl));
			if (!string.IsNullOrWhiteSpace(error) || string.IsNullOrWhiteSpace(code))
				return SsoFailed(trip, error == "access_denied" ? "SsoDeniedByProvider" : "SsoFailed");

			return trip.Purpose switch
			{
				WebSsoPurpose.Login => await SsoLoginReturnAsync(trip, code, cancellationToken),
				WebSsoPurpose.LoginStepUp => await SsoLoginStepUpReturnAsync(trip, code, cancellationToken),
				WebSsoPurpose.Reauthentication => await SsoReauthenticationReturnAsync(trip, code, cancellationToken),
				WebSsoPurpose.StepUp => await SsoStepUpReturnAsync(trip, code, cancellationToken),
				WebSsoPurpose.AdpStepUp => await SsoAdpReturnAsync(trip, code, cancellationToken),
				WebSsoPurpose.MappingTest => await SsoMappingTestReturnAsync(trip, code, cancellationToken),
				WebSsoPurpose.SharedUnlock => await SsoSharedUnlockReturnAsync(trip, code, cancellationToken),
				_ => SsoFailed(trip, "SsoFailed")
			};
		}

		/// <summary>A round trip that did not finish: sign-in starts again; anything else returns to where it began, saying why.</summary>
		private IActionResult SsoFailed(WebSsoRoundTripState trip, string messageKey)
		{
			var message = _twoFactorLocalizer[messageKey].Value;
			switch (trip?.Purpose)
			{
				case null:
				case WebSsoPurpose.Login:
				case WebSsoPurpose.LoginStepUp:
					if (trip?.Purpose == WebSsoPurpose.LoginStepUp && HoldsLoginTransaction)
					{
						TempData["LoginMfaChoiceMessage"] = message;
						return RedirectToAction(nameof(LoginMfa), new { returnUrl = trip.ReturnUrl });
					}

					TempData["LoginMfaMessage"] = message;
					return RedirectToAction(nameof(LogOn), new { returnUrl = trip?.ReturnUrl });
				case WebSsoPurpose.AdpStepUp:
					return View("SsoReturnGrant", new SsoReturnGrantViewModel { Error = "mfa_verification_failed" });
				case WebSsoPurpose.Reauthentication:
					TempData["StepUpMessage"] = message;
					return RedirectToAction("Reauthenticate", "AccountSecurity", new { area = "User", returnUrl = trip.ReturnUrl });
				case WebSsoPurpose.MappingTest:
					TempData["FederatedMfaError"] = message;
					return RedirectToAction("FederatedMfa", "Security", new { area = "User" });
				case WebSsoPurpose.SharedUnlock:
					TempData["SharedUnlockMessage"] = message;
					return LocalRedirect(WebSharedSession.LockedUrl(trip.ReturnUrl));
				default:
					TempData["StepUpMessage"] = message;
					return RedirectToAction("Verify2FA", "TwoFactor", new { area = "User", returnUrl = trip.ReturnUrl, scope = trip.Scope });
			}
		}

		// ── Sign-in ──────────────────────────────────────────────────────────────────────────────────────────────────

		/// <summary>
		/// A single sign-on first factor continues exactly like a password (plan section 7.6 row 2): the provider's own mapped MFA
		/// finishes it where the department accepts that; an account with an authenticator app chooses a second factor on the
		/// login transaction; an account without MFA signs in, unless its department requires MFA.
		/// </summary>
		private async Task<IActionResult> SsoLoginReturnAsync(WebSsoRoundTripState trip, string code, CancellationToken cancellationToken)
		{
			var redeemed = await _ssoBroker.RedeemAsync(trip.TransactionId, code, trip.Verifier, UserSessionClientApplication.Web, cancellationToken,
				SsoTransactionPurpose.Login);
			if (!redeemed.Succeeded)
				return SsoFailed(trip, SsoMessageFor(redeemed.Outcome));

			var sso = redeemed.Transaction;
			var user = await _userManager.FindByIdAsync(sso.UserId);
			if (user == null)
				return SsoFailed(trip, "SsoAccessDenied");
			if (await _userManager.IsLockedOutAsync(user))
				return SsoFailed(trip, "LoginMfaTooManyAttempts");

			var membership = await _departmentsService.GetDepartmentMemberAsync(user.Id, sso.DepartmentId, bypassCache: true);
			if (membership == null || membership.IsDeleted || membership.IsDisabled == true)
				return SsoFailed(trip, "SsoAccessDenied");

			var totp = await _userManager.GetTwoFactorEnabledAsync(user);
			var request = new MfaLoginTransactionRequest
			{
				UserId = user.Id,
				DepartmentId = sso.DepartmentId,
				ClientApplication = UserSessionClientApplication.Web,
				FirstFactorMethod = MfaEvidenceMethod.Sso,
				FirstFactorVerifiedOnUtc = sso.AuthenticatedOnUtc ?? DateTime.UtcNow,
				DepartmentSsoConfigId = sso.DepartmentSsoConfigId,
				AuthenticationGeneration = user.AuthenticationGeneration,
				TotpEnrolled = totp,
				SharedModeRequested = WebInstallation().Shared,
				InstallationLabel = WebInstallation().Label
			};

			try
			{
				var federated = await ProviderMfaSatisfiedAsync(sso, cancellationToken);
				if (federated != null)
				{
					// The sign-in's own round trip carried the department's mapped provider MFA (plan section 7.8 flow 1).
					await AuditSsoAsync(user, SystemAuditTypes.SsoLogin, true, "Web SSO sign-in; provider MFA satisfied the second factor.", cancellationToken);
					return await SignInCompletedAsync(await _loginTransactions.BeginCompletedAsync(request, MfaEvidenceMethod.Federated,
						FederatedMfaMapping.FactorReferenceFor(federated.DepartmentSsoConfigId, federated.FederatedMfaMappingVersion),
						request.FirstFactorVerifiedOnUtc, cancellationToken), user, trip.ReturnUrl, cancellationToken);
				}

				if (totp)
				{
					var start = await _loginTransactions.BeginAsync(request, cancellationToken);
					await AuditSsoAsync(user, SystemAuditTypes.SsoLogin, true, "Web SSO sign-in; a second factor is required.", cancellationToken);
					SetLoginTransactionCookie(start);
					return RedirectToAction(nameof(LoginMfa), new { returnUrl = trip.ReturnUrl });
				}

				// RequireMfa has always applied to single sign-on (plan section 7.6 row 4): no session without enrolled MFA. The sign-in
				// continues only as the restricted setup transaction (plan section 6.2).
				if (await MustSetUpMfaAsync(user, sso.DepartmentId, true, cancellationToken))
				{
					await AuditSsoAsync(user, SystemAuditTypes.SsoLogin, true, "Web SSO sign-in; an authenticator must be set up first.", cancellationToken);
					return await BeginSetupTransactionAsync(request, trip.ReturnUrl, cancellationToken);
				}

				await AuditSsoAsync(user, SystemAuditTypes.SsoLogin, true, "Web SSO sign-in; no second factor required.", cancellationToken);
				return await SignInCompletedAsync(await _loginTransactions.BeginCompletedAsync(request, cancellationToken), user, trip.ReturnUrl,
					cancellationToken);
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				Logging.LogException(ex, "A Web SSO sign-in could not continue after the provider.");
				return SsoFailed(trip, "LoginMfaUnavailable");
			}
		}

		/// <summary>An already-complete login transaction (no second factor, or provider MFA) is redeemed once and signs in.</summary>
		private async Task<IActionResult> SignInCompletedAsync(MfaLoginCompletion completion, IdentityUser user, string returnUrl,
			CancellationToken cancellationToken)
		{
			if (!completion.Succeeded)
				return RestartSignIn(completion.Outcome, returnUrl);

			var redeemed = await _loginTransactions.RedeemAsync(completion.Transaction, completion.CompletionCode, UserSessionClientApplication.Web,
				cancellationToken);
			if (!redeemed.IsUsable)
				return RestartSignIn(redeemed.Outcome, returnUrl);

			var done = redeemed.Transaction;
			var method = done.CompletionMethod == null ? (MfaEvidenceMethod?)null : (MfaEvidenceMethod)done.CompletionMethod.Value;
			var finish = await SignInRedeemedAsync(done, user, method, done.CompletionFactorReference, done.CompletionVerifiedOnUtc ?? DateTime.UtcNow,
				returnUrl, false, cancellationToken);
			return FinishedRedirect(finish, returnUrl);
		}

		/// <summary>
		/// Provider step-up that finishes a password sign-in (plan section 7.6 row 1): the round trip must be this sign-in's own,
		/// for this account and generation, under the department's unchanged mapping.
		/// </summary>
		private async Task<IActionResult> SsoLoginStepUpReturnAsync(WebSsoRoundTripState trip, string code, CancellationToken cancellationToken)
		{
			var (transaction, user, outcome) = await OpenLoginTransactionAsync(cancellationToken);
			if (transaction == null)
				return RestartSignIn(outcome, trip.ReturnUrl);

			var refusal = await RefuseLoginMethodAsync(transaction, user, MfaEvidenceMethod.Federated, cancellationToken);
			if (refusal?.Restart == true)
				return RestartSignIn(refusal.Value.Outcome, trip.ReturnUrl);
			if (refusal != null)
				return SsoFailed(trip, "LoginMfaMethodUnavailable");

			var redeemed = await _ssoBroker.RedeemAsync(trip.TransactionId, code, trip.Verifier, UserSessionClientApplication.Web, cancellationToken,
				SsoTransactionPurpose.StepUp);
			if (!redeemed.Succeeded)
				return SsoFailed(trip, SsoMessageFor(redeemed.Outcome));

			var step = redeemed.Transaction;
			var config = transaction.DepartmentId is int departmentId
				? await _departmentSsoService.GetTestedFederatedMfaConfigAsync(departmentId, cancellationToken)
				: null;
			if (!FederatedMfaMapping.Satisfies(step, config) || step.DepartmentId != transaction.DepartmentId ||
				!string.Equals(step.LoginTransactionId, transaction.MfaLoginTransactionId, StringComparison.Ordinal) ||
				!string.Equals(step.Operation, SsoLoginTransaction.LoginOperation, StringComparison.Ordinal) ||
				!string.Equals(step.ExpectedUserId, transaction.UserId, StringComparison.OrdinalIgnoreCase) ||
				!string.Equals(step.UserId, transaction.UserId, StringComparison.OrdinalIgnoreCase) ||
				step.AuthenticationGeneration != transaction.AuthenticationGeneration)
			{
				var ended = await LoginFactorFailedAsync(transaction, user, MfaEvidenceMethod.Federated, cancellationToken);
				return ended != null ? RestartSignIn(ended.Value, trip.ReturnUrl) : SsoFailed(trip, "SsoProviderMfaNotConfirmed");
			}

			var finish = await FinishLoginTransactionAsync(transaction, user, MfaEvidenceMethod.Federated,
				FederatedMfaMapping.FactorReferenceFor(config.DepartmentSsoConfigId, config.FederatedMfaMappingVersion), step.AuthenticatedOnUtc ?? DateTime.UtcNow,
				trip.ReturnUrl, false, cancellationToken);
			return FinishedRedirect(finish, trip.ReturnUrl);
		}

		private IActionResult FinishedRedirect(LoginFinish finish, string returnUrl)
		{
			if (finish.Redirect != null)
				return LocalRedirect(finish.Redirect);
			if (finish.Restart is MfaLoginTransactionOutcome restart)
				return RestartSignIn(restart, returnUrl);

			TempData["LoginMfaMessage"] = _twoFactorLocalizer["LoginMfaMaximumSessions"].Value;
			return RedirectToAction(nameof(LogOn), new { returnUrl = SafeReturnUrl(returnUrl) });
		}

		/// <summary>
		/// Starts provider step-up to finish this password sign-in, where the department accepts its provider's MFA for sign-in and
		/// the account signs in through that provider.
		/// </summary>
		[HttpPost]
		[AllowAnonymous]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> LoginMfaBeginFederated(string returnUrl, CancellationToken cancellationToken)
		{
			var (transaction, user, outcome) = await OpenLoginTransactionAsync(cancellationToken);
			if (transaction == null)
				return RestartSignIn(outcome, returnUrl);

			var refusal = await RefuseLoginMethodAsync(transaction, user, MfaEvidenceMethod.Federated, cancellationToken);
			if (refusal?.Restart == true)
				return RestartSignIn(refusal.Value.Outcome, returnUrl);

			var department = refusal == null && transaction.DepartmentId is int departmentId && WebSsoAvailable &&
				await _departmentSsoService.IsFederatedMfaAvailableAsync(departmentId, transaction.UserId, cancellationToken)
					? await _departmentsService.GetDepartmentByIdAsync(departmentId)
					: null;
			var authorizeUrl = department == null
				? null
				: await BeginRoundTripAsync(department, SsoTransactionPurpose.StepUp, WebSsoPurpose.LoginStepUp, returnUrl, cancellationToken,
					userId: transaction.UserId, generation: transaction.AuthenticationGeneration, operation: SsoLoginTransaction.LoginOperation,
					loginTransactionId: transaction.MfaLoginTransactionId);
			if (authorizeUrl == null)
			{
				TempData["LoginMfaChoiceMessage"] = _twoFactorLocalizer["LoginMfaMethodUnavailable"].Value;
				return RedirectToAction(nameof(LoginMfa), new { returnUrl = SafeReturnUrl(returnUrl) });
			}

			return Redirect(authorizeUrl);
		}

		// ── Round trips for the signed-in session ───────────────────────────────────────────────────────────────────────

		/// <summary>
		/// Starts a round trip for the signed-in session: reauthentication, provider step-up for a guarded action (by its scope) or
		/// for protected data, or the managing member's mapping test. Every one is bound to this session, account and generation,
		/// in the session's own department; the broker refuses a step-up the department's switches or tested mapping do not allow.
		/// </summary>
		[HttpPost]
		[Authorize]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> SsoSessionBegin(string purpose, string scope, string returnUrl, CancellationToken cancellationToken)
		{
			var webPurpose = purpose switch
			{
				"reauthenticate" => WebSsoPurpose.Reauthentication,
				"step_up" => WebSsoPurpose.StepUp,
				"adp" => WebSsoPurpose.AdpStepUp,
				"mapping_test" => WebSsoPurpose.MappingTest,
				_ => (WebSsoPurpose?)null
			};
			var session = HttpProtectedGrantContext.SessionOf(HttpContext);
			var userId = User.FindFirst(ClaimTypes.PrimarySid)?.Value;
			if (webPurpose == null || session == null || string.IsNullOrWhiteSpace(userId) || !WebSsoRoundTrip.IsAvailable(_ssoBroker, _ssoReturnTargets) ||
				!int.TryParse(User.FindFirst(ClaimTypes.PrimaryGroupSid)?.Value, out var departmentId))
				return SsoFailed(new WebSsoRoundTripState { Purpose = webPurpose ?? WebSsoPurpose.StepUp, ReturnUrl = SafeReturnUrl(returnUrl) }, "SsoUnavailable");

			var methodScope = Enum.TryParse<MfaMethodScope>(scope, true, out var parsed) && Enum.IsDefined(parsed) ? parsed : MfaMethodScope.Login;
			var trip = new WebSsoRoundTripState { Purpose = webPurpose.Value, ReturnUrl = SafeReturnUrl(returnUrl) };
			if (webPurpose == WebSsoPurpose.MappingTest && !await MayTestMappingAsync(userId, departmentId, cancellationToken))
				return SsoFailed(trip, "FederatedMfaNeedsResgridMfaShort");

			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId);
			var authorizeUrl = department == null
				? null
				: await BeginRoundTripAsync(department, webPurpose switch
					{
						WebSsoPurpose.Reauthentication => SsoTransactionPurpose.Reauthentication,
						WebSsoPurpose.AdpStepUp => SsoTransactionPurpose.AdpStepUp,
						WebSsoPurpose.MappingTest => SsoTransactionPurpose.MappingTest,
						_ => SsoTransactionPurpose.StepUp
					}, webPurpose.Value, returnUrl, cancellationToken, session.SessionId, userId, session.AuthenticationGeneration,
					webPurpose == WebSsoPurpose.StepUp ? MfaStepUpOperations.ForScope(methodScope) : null, scope: methodScope.ToString());
			return authorizeUrl == null ? SsoFailed(trip, "SsoUnavailable") : Redirect(authorizeUrl);
		}

		/// <summary>
		/// Starts unlocking this locked shared session through the department's identity provider (plan sections 7.8 and 12.5.3):
		/// the same operator, at the lock version the lock screen showed, where the department accepts its provider's MFA for
		/// sign-in and has a tested mapping. The round trip is bound to this session and counts only for the lock it began in.
		/// </summary>
		[HttpPost]
		[AllowAnonymous]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> SsoUnlockBegin(long lockVersion, string returnUrl, CancellationToken cancellationToken)
		{
			var trip = new WebSsoRoundTripState { Purpose = WebSsoPurpose.SharedUnlock, ReturnUrl = SafeReturnUrl(returnUrl) };
			var session = WebSharedSession.SessionOf(HttpContext);
			if (session is not { SharedMode: true, IsLocked: true })
				return session == null ? RedirectToAction(nameof(LogOn)) : LocalRedirect(SafeReturnUrl(returnUrl) ?? "/User/Home/Dashboard");
			if (session.LockVersion != lockVersion)
				return SsoFailed(trip, "SharedUnlockLockChanged");
			if (await _sharedSessions.CanUnlockAsync(session, cancellationToken) != SharedSessionOutcome.Succeeded ||
				session.DepartmentId is not int departmentId || !WebSsoRoundTrip.IsAvailable(_ssoBroker, _ssoReturnTargets) ||
				!await _mfaPolicy.IsMethodAcceptedAsync(departmentId, MfaMethodScope.Login, MfaEvidenceMethod.Federated, cancellationToken) ||
				!await _departmentSsoService.IsFederatedMfaAvailableAsync(departmentId, session.UserId, cancellationToken))
				return SsoFailed(trip, "SsoUnavailable");

			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId);
			var authorizeUrl = department == null
				? null
				: await BeginRoundTripAsync(department, SsoTransactionPurpose.StepUp, WebSsoPurpose.SharedUnlock, returnUrl, cancellationToken,
					session.UserSessionId, session.UserId, session.AuthenticationGeneration, SsoLoginTransaction.SharedUnlockOperation,
					scope: session.LockVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
			return authorizeUrl == null ? SsoFailed(trip, "SsoUnavailable") : Redirect(authorizeUrl);
		}

		/// <summary>
		/// Finishes a provider unlock: the round trip must be this locked session's own shared unlock, for its operator and
		/// generation, begun after its last lock, under the department's unchanged tested mapping, at the lock version it began
		/// at. The same session resumes; the first-factor time and the shift end do not change.
		/// </summary>
		private async Task<IActionResult> SsoSharedUnlockReturnAsync(WebSsoRoundTripState trip, string code, CancellationToken cancellationToken)
		{
			var session = WebSharedSession.SessionOf(HttpContext);
			if (session is not { SharedMode: true, IsLocked: true } || session.DepartmentId is not int departmentId)
				return session == null ? RedirectToAction(nameof(LogOn)) : LocalRedirect(trip.ReturnUrl ?? "/User/Home/Dashboard");
			if (!long.TryParse(trip.Scope, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var lockVersion) ||
				lockVersion != session.LockVersion)
				return SsoFailed(trip, "SharedUnlockLockChanged");
			if (await _sharedSessions.CanUnlockAsync(session, cancellationToken) != SharedSessionOutcome.Succeeded ||
				!await _mfaPolicy.IsMethodAcceptedAsync(departmentId, MfaMethodScope.Login, MfaEvidenceMethod.Federated, cancellationToken))
				return SsoFailed(trip, "SsoUnavailable");

			var redeemed = await _ssoBroker.RedeemAsync(trip.TransactionId, code, trip.Verifier, UserSessionClientApplication.Web, cancellationToken,
				SsoTransactionPurpose.StepUp);
			if (!redeemed.Succeeded)
				return SsoFailed(trip, SsoMessageFor(redeemed.Outcome));

			var info = new SharedSessionRequestInfo
			{
				UserName = User.Identity?.Name, IpAddress = IpAddressHelper.GetRequestIP(Request, true), CorrelationId = HttpContext.TraceIdentifier,
				AuditSystem = SystemAuditSystems.Website
			};
			var config = await _departmentSsoService.GetTestedFederatedMfaConfigAsync(departmentId, cancellationToken);
			if (!SharedSessionRules.FederatedUnlockMatches(redeemed.Transaction, session, session.UserId, departmentId, config))
			{
				await _sharedSessions.RecordFailedUnlockAsync(session, MfaMethodNames.Federated, info, cancellationToken);
				return SsoFailed(trip, "SsoProviderMfaNotConfirmed");
			}

			var unlocked = await _sharedSessions.UnlockAsync(session, lockVersion, MfaEvidenceMethod.Federated,
				FederatedMfaMapping.FactorReferenceFor(config.DepartmentSsoConfigId, config.FederatedMfaMappingVersion),
				redeemed.Transaction.AuthenticatedOnUtc ?? DateTime.UtcNow, info, cancellationToken);
			if (!unlocked.Succeeded)
				return SsoFailed(trip, unlocked.Outcome == SharedSessionOutcome.LockChanged ? "SharedUnlockLockChanged" : "SsoFailed");

			return LocalRedirect(trip.ReturnUrl ?? "/User/Home/Dashboard");
		}

		/// <summary>The mapping test is the managing member's, after a Resgrid factor within 5 minutes (never provider step-up itself).</summary>
		private async Task<bool> MayTestMappingAsync(string userId, int departmentId, CancellationToken cancellationToken)
		{
			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId);
			if (department == null || department.ManagingUserId != userId)
				return false;

			var user = await _userManager.FindByIdAsync(userId);
			var verifiedAt = await StepUpEvidence.GetLatestSecondFactorUtcAsync(_mfaEvidenceService, user, HttpContext, _mfaPolicy, departmentId,
				MfaMethodScope.SecurityChange, cancellationToken, excludeFederated: true);
			return verifiedAt != null && DateTime.UtcNow - verifiedAt.Value <= TimeSpan.FromMinutes(5);
		}

		/// <summary>The session the round trip was begun for, still this browser's: same session, account and generation.</summary>
		private (ProtectedGrantSessionContext Session, string UserId, int DepartmentId)? SessionFor(SsoLoginTransaction transaction)
		{
			var session = HttpProtectedGrantContext.SessionOf(HttpContext);
			var userId = User.FindFirst(ClaimTypes.PrimarySid)?.Value;
			if (transaction == null || session == null || string.IsNullOrWhiteSpace(userId) ||
				!int.TryParse(User.FindFirst(ClaimTypes.PrimaryGroupSid)?.Value, out var departmentId) ||
				!string.Equals(transaction.SessionId, session.SessionId, StringComparison.Ordinal) ||
				!string.Equals(transaction.ExpectedUserId, userId, StringComparison.OrdinalIgnoreCase) ||
				!string.Equals(transaction.UserId, userId, StringComparison.OrdinalIgnoreCase) ||
				transaction.AuthenticationGeneration != session.AuthenticationGeneration ||
				transaction.DepartmentId != departmentId ||
				// Begun before this shared session's last lock: nothing from before a lock counts after it (plan section 12.5.3).
				(session.SessionLockedOnUtc != null && transaction.CreatedOnUtc <= session.SessionLockedOnUtc.Value))
				return null;

			return (session, userId, departmentId);
		}

		/// <summary>Fresh SSO first-factor proof for this session (plan section 6.2), at the provider's own authentication time.</summary>
		private async Task<IActionResult> SsoReauthenticationReturnAsync(WebSsoRoundTripState trip, string code, CancellationToken cancellationToken)
		{
			var redeemed = await _ssoBroker.RedeemAsync(trip.TransactionId, code, trip.Verifier, UserSessionClientApplication.Web, cancellationToken,
				SsoTransactionPurpose.Reauthentication);
			var bound = redeemed.Succeeded ? SessionFor(redeemed.Transaction) : null;
			if (bound == null)
				return SsoFailed(trip, redeemed.Succeeded ? "SsoAccountMismatch" : SsoMessageFor(redeemed.Outcome));

			var sso = redeemed.Transaction;
			var user = await _userManager.FindByIdAsync(bound.Value.UserId);
			await _mfaEvidenceService.RecordAsync(bound.Value.UserId, MfaEvidence.TrackedSessionKey(bound.Value.Session.SessionId), UserSessionClientApplication.Web,
				MfaEvidenceKind.FirstFactor, MfaEvidenceMethod.Sso, MfaEvidencePurpose.Reauthentication, sso.AuthenticatedOnUtc ?? DateTime.UtcNow,
				bound.Value.Session.AuthenticationGeneration, sso.DepartmentId, cancellationToken: cancellationToken);
			await AuditSsoAsync(user, SystemAuditTypes.AccountReauthenticated, true, "Reauthenticated on the web through the department's SSO provider.",
				cancellationToken);
			return LocalRedirect(trip.ReturnUrl ?? Url.Action("Index", "TwoFactor", new { Area = "User" }));
		}

		/// <summary>
		/// Provider step-up for a guarded action (plan sections 7.6 row 7 and 7.8): the session's own round trip for that action's
		/// operation, carrying a value the department's tested mapping counts as MFA, where the department accepts it there.
		/// </summary>
		private async Task<IActionResult> SsoStepUpReturnAsync(WebSsoRoundTripState trip, string code, CancellationToken cancellationToken)
		{
			var scope = Enum.TryParse<MfaMethodScope>(trip.Scope, true, out var parsed) && Enum.IsDefined(parsed) ? parsed : MfaMethodScope.Login;
			var redeemed = await _ssoBroker.RedeemAsync(trip.TransactionId, code, trip.Verifier, UserSessionClientApplication.Web, cancellationToken,
				SsoTransactionPurpose.StepUp);
			var bound = redeemed.Succeeded ? SessionFor(redeemed.Transaction) : null;
			if (bound == null)
				return SsoFailed(trip, redeemed.Succeeded ? "SsoAccountMismatch" : SsoMessageFor(redeemed.Outcome));

			var step = redeemed.Transaction;
			var config = await _departmentSsoService.GetTestedFederatedMfaConfigAsync(bound.Value.DepartmentId, cancellationToken);
			if (!FederatedMfaMapping.Satisfies(step, config) || !string.Equals(step.Operation, MfaStepUpOperations.ForScope(scope), StringComparison.Ordinal) ||
				!await _mfaPolicy.IsMethodAcceptedAsync(bound.Value.DepartmentId, scope, MfaEvidenceMethod.Federated, cancellationToken))
				return SsoFailed(trip, "SsoProviderMfaNotConfirmed");

			var user = await _userManager.FindByIdAsync(bound.Value.UserId);
			await _mfaEvidenceService.RecordAsync(bound.Value.UserId, MfaEvidence.TrackedSessionKey(bound.Value.Session.SessionId), UserSessionClientApplication.Web,
				MfaEvidenceKind.SecondFactor, MfaEvidenceMethod.Federated, MfaEvidencePurpose.StepUp, step.AuthenticatedOnUtc ?? DateTime.UtcNow,
				bound.Value.Session.AuthenticationGeneration, bound.Value.DepartmentId,
				FederatedMfaMapping.FactorReferenceFor(config.DepartmentSsoConfigId, config.FederatedMfaMappingVersion), cancellationToken);
			await _systemAuditsService.SaveSystemAuditAsync(new SystemAudit
			{
				System = (int)SystemAuditSystems.Website,
				Type = (int)SystemAuditTypes.TwoFactorStepUpVerified,
				UserId = user?.Id,
				Username = user?.UserName,
				Successful = true,
				IpAddress = IpAddressHelper.GetRequestIP(Request, true),
				ServerName = Environment.MachineName,
				Data = "Step-up verified with the identity provider's MFA."
			}, cancellationToken);
			return LocalRedirect(trip.ReturnUrl ?? Url.Action("Dashboard", "Home", new { Area = "User" }));
		}

		/// <summary>
		/// Provider step-up for protected data (plan section 7.6 row 9), finished in the popup the reveal dialog opened: the one
		/// grant issuer redeems the round trip and issues a grant, which the popup hands to its opener on this site only.
		/// </summary>
		private async Task<IActionResult> SsoAdpReturnAsync(WebSsoRoundTripState trip, string code, CancellationToken cancellationToken)
		{
			var session = HttpProtectedGrantContext.SessionOf(HttpContext);
			var userId = User.FindFirst(ClaimTypes.PrimarySid)?.Value;
			if (session == null || string.IsNullOrWhiteSpace(userId) || !int.TryParse(User.FindFirst(ClaimTypes.PrimaryGroupSid)?.Value, out var departmentId))
				return SsoFailed(trip, "SsoAccountMismatch");

			var user = await _userManager.FindByIdAsync(userId);
			var issued = await _adpStepUp.CompleteFederatedAsync(new AdpStepUpCaller
			{
				UserId = userId,
				UserName = user?.UserName,
				DepartmentId = departmentId,
				Session = session,
				LegacySessionId = User.FindFirst(SessionClaimTypes.SessionId)?.Value,
				ClientApplication = UserSessionClientApplication.Web,
				AccountAuthenticationGeneration = user?.AuthenticationGeneration ?? 0,
				EvidenceSessionKey = MfaEvidenceSession.KeyFor(User, HttpContext),
				IpAddress = IpAddressHelper.GetRequestIP(Request, true),
				AuditSystem = SystemAuditSystems.Website
			}, trip.TransactionId, code, trip.Verifier, cancellationToken);

			return View("SsoReturnGrant", issued.Succeeded
				? new SsoReturnGrantViewModel
				{
					GrantToken = issued.Token, GrantId = issued.GrantId, ExpiresOnUtc = issued.ExpiresOnUtc.ToString("O"), WindowMinutes = issued.WindowMinutes
				}
				: new SsoReturnGrantViewModel { Error = issued.ErrorCode ?? "mfa_verification_failed" });
		}

		/// <summary>
		/// The managing member's mapping test (plan section 7.8): a fresh provider sign-in of their own session's account that
		/// carried a value the saved mapping counts as MFA makes that exact version effective. A mapping changed meanwhile is not.
		/// </summary>
		private async Task<IActionResult> SsoMappingTestReturnAsync(WebSsoRoundTripState trip, string code, CancellationToken cancellationToken)
		{
			var redeemed = await _ssoBroker.RedeemAsync(trip.TransactionId, code, trip.Verifier, UserSessionClientApplication.Web, cancellationToken,
				SsoTransactionPurpose.MappingTest);
			var bound = redeemed.Succeeded ? SessionFor(redeemed.Transaction) : null;
			var test = redeemed.Transaction;
			trip.ReturnUrl = Url.Action("FederatedMfa", "Security", new { Area = "User" });
			if (bound == null || string.IsNullOrWhiteSpace(test?.FederatedMfaValue) || test.FederatedMappingVersion == null)
				return SsoFailed(trip, bound == null && redeemed.Succeeded ? "SsoAccountMismatch" : "FederatedMfaTestFailed");

			var department = await _departmentsService.GetDepartmentByIdAsync(bound.Value.DepartmentId);
			if (department?.ManagingUserId != bound.Value.UserId)
				return SsoFailed(trip, "FederatedMfaTestFailed");
			if (!await _departmentSsoService.RecordFederatedMfaTestAsync(test.DepartmentSsoConfigId, test.FederatedMappingVersion.Value, bound.Value.UserId,
					cancellationToken))
				return SsoFailed(trip, "FederatedMfaTestChanged");

			var user = await _userManager.FindByIdAsync(bound.Value.UserId);
			await _systemAuditsService.SaveSystemAuditAsync(new SystemAudit
			{
				System = (int)SystemAuditSystems.Website,
				Type = (int)SystemAuditTypes.FederatedMfaMappingTested,
				UserId = bound.Value.UserId,
				Username = user?.UserName,
				Successful = true,
				IpAddress = IpAddressHelper.GetRequestIP(Request, true),
				ServerName = Environment.MachineName,
				Data = $"Provider step-up mapping version {test.FederatedMappingVersion} for SSO configuration {test.DepartmentSsoConfigId} passed its test ({test.FederatedMfaValue})."
			}, cancellationToken);

			TempData["FederatedMfaSuccess"] = _twoFactorLocalizer["FederatedMfaTestPassed"].Value;
			return LocalRedirect(trip.ReturnUrl);
		}

		// ── Helpers ────────────────────────────────────────────────────────────────────────────────────────────────────

		/// <summary>The tested configuration when the sign-in's own round trip carried provider MFA the department accepts for sign-in.</summary>
		private async Task<DepartmentSsoConfig> ProviderMfaSatisfiedAsync(SsoLoginTransaction transaction, CancellationToken cancellationToken)
		{
			if (string.IsNullOrWhiteSpace(transaction.FederatedMfaValue))
				return null;

			var config = await _departmentSsoService.GetTestedFederatedMfaConfigAsync(transaction.DepartmentId, cancellationToken);
			return FederatedMfaMapping.Satisfies(transaction, config) &&
				await _mfaPolicy.IsMethodAcceptedAsync(transaction.DepartmentId, MfaMethodScope.Login, MfaEvidenceMethod.Federated, cancellationToken)
					? config
					: null;
		}

		private static string SsoMessageFor(SsoBrokerOutcome outcome) => outcome switch
		{
			SsoBrokerOutcome.Expired => "SsoExpired",
			SsoBrokerOutcome.IdentityMismatch => "SsoAccountMismatch",
			SsoBrokerOutcome.AccessDenied => "SsoAccessDenied",
			SsoBrokerOutcome.ServiceUnavailable => "LoginMfaUnavailable",
			SsoBrokerOutcome.FederatedNotSatisfied => "SsoProviderMfaNotConfirmed",
			SsoBrokerOutcome.ReauthenticationNotFresh => "SsoNotFresh",
			_ => "SsoFailed"
		};

		private Task AuditSsoAsync(IdentityUser user, SystemAuditTypes type, bool successful, string data, CancellationToken cancellationToken) =>
			_systemAuditsService.SaveSystemAuditAsync(new SystemAudit
			{
				System = (int)SystemAuditSystems.Website,
				Type = (int)type,
				UserId = user?.Id,
				Username = user?.UserName,
				Successful = successful,
				IpAddress = IpAddressHelper.GetRequestIP(Request, true),
				ServerName = Environment.MachineName,
				Data = data
			}, cancellationToken);
	}
}
