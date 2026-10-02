using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Security;
using Resgrid.Repositories.DataRepository.Stores;
using Resgrid.Web.Helpers;
using Resgrid.Web.Models.AccountViewModels;
using IdentityUser = Resgrid.Model.Identity.IdentityUser;

namespace Resgrid.Web.Controllers
{
	/// <summary>
	/// Web sign-in for an account that must have MFA and has none yet (the restricted setup transaction, passkey plan section 6.2),
	/// and "I lost my authenticator" (the restricted recovery transaction, sections 5.4 and 6.3). Neither grants any ordinary access:
	/// setup finishes the sign-in only once a code from the new authenticator verifies; recovery replaces the lost factors and ends
	/// every session, and the user then signs in normally.
	/// </summary>
	public partial class AccountController
	{
		private const string RecoveryCookie = ".Resgrid.FactorRecovery";

		// ── Setup: a required-MFA account with no authenticator yet ───────────────────────────────────────────────────

		/// <summary>
		/// Whether this sign-in may continue only by setting up an authenticator (plan section 7.5 rule 9): its department requires MFA
		/// and the account has none. The Web gate decides whether that is the setup transaction or, as before, the enrollment page.
		/// </summary>
		private async Task<bool> MustSetUpMfaAsync(IdentityUser user, int? departmentId, bool alwaysEnforced, CancellationToken cancellationToken) =>
			departmentId is int department && !await _userManager.GetTwoFactorEnabledAsync(user) &&
			(alwaysEnforced
				? await _mfaPolicy.DepartmentRequiresMfaAsync(department, cancellationToken)
				: await _mfaPolicy.IsRequireMfaEnforcedAsync(department, cancellationToken));

		/// <summary>Starts the restricted setup transaction: it permits setting up an authenticator for this sign-in and nothing else.</summary>
		private async Task<IActionResult> BeginSetupTransactionAsync(MfaLoginTransactionRequest request, string returnUrl, CancellationToken cancellationToken)
		{
			var start = await _loginTransactions.BeginAsync(request, cancellationToken);
			SetLoginTransactionCookie(start);
			return RedirectToAction(nameof(LoginMfaSetup), new { returnUrl = SafeReturnUrl(returnUrl) });
		}

		//
		// GET: /Account/LoginMfaSetup
		[HttpGet]
		[AllowAnonymous]
		public async Task<IActionResult> LoginMfaSetup(string returnUrl = null, CancellationToken cancellationToken = default)
		{
			var (transaction, user, outcome) = await OpenLoginTransactionAsync(cancellationToken);
			if (transaction == null)
				return RestartSignIn(outcome, returnUrl);
			if (await _userManager.GetTwoFactorEnabledAsync(user))
				return RedirectToAction(nameof(LoginMfa), new { returnUrl = SafeReturnUrl(returnUrl) });

			SetNoStoreHeaders();
			return View(await SetupModelAsync(user, new LoginMfaSetupViewModel { ReturnUrl = SafeReturnUrl(returnUrl) }));
		}

		//
		// POST: /Account/LoginMfaSetup
		[HttpPost]
		[AllowAnonymous]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> LoginMfaSetup(LoginMfaSetupViewModel model, CancellationToken cancellationToken)
		{
			model ??= new LoginMfaSetupViewModel();
			model.ReturnUrl = SafeReturnUrl(model.ReturnUrl);
			var (transaction, user, outcome) = await OpenLoginTransactionAsync(cancellationToken);
			if (transaction == null)
				return RestartSignIn(outcome, model.ReturnUrl);
			if (await _userManager.GetTwoFactorEnabledAsync(user))
				return RedirectToAction(nameof(LoginMfa), new { returnUrl = model.ReturnUrl });
			if (await _userManager.IsLockedOutAsync(user))
				return RestartSignIn(MfaLoginTransactionOutcome.TooManyAttempts, model.ReturnUrl);

			SetNoStoreHeaders();
			var stagedKey = await AuthenticatorSetup.GetStagedKeyAsync(_userManager, user);
			if (stagedKey == null)
			{
				// The staged key's short lifetime ran out: a new one is shown, and the old one can never be activated.
				ModelState.AddModelError(nameof(model.Code), _twoFactorLocalizer["SetupExpired"]);
				return View(await SetupModelAsync(user, model));
			}

			if (!await AuthenticatorSetup.VerifyStagedCodeAsync(_mfaState, user, stagedKey, model.Code, cancellationToken))
			{
				var ended = await LoginFactorFailedAsync(transaction, user, MfaEvidenceMethod.Totp, cancellationToken);
				if (ended != null)
					return RestartSignIn(ended.Value, model.ReturnUrl);

				ModelState.AddModelError(nameof(model.Code), _twoFactorLocalizer["InvalidCodeLogin"]);
				return View(await SetupModelAsync(user, model, stagedKey));
			}

			await AuthenticatorSetup.PromoteAsync(_userManager, _userStore, _mfaState, user, stagedKey,
				new TotpEnrollmentContext(false, (int)UserSessionClientApplication.Web), cancellationToken);
			var codes = await AuthenticatorSetup.NewRecoveryCodesAsync(_userManager, user);
			await _systemAuditsService.SaveSystemAuditAsync(new SystemAudit
			{
				System = (int)SystemAuditSystems.Website,
				Type = (int)SystemAuditTypes.TwoFactorEnabled,
				UserId = user.Id,
				Username = user.UserName,
				Successful = true,
				IpAddress = IpAddressHelper.GetRequestIP(Request, true),
				ServerName = Environment.MachineName,
				Data = "Authenticator set up during Web sign-in (setup transaction)."
			}, cancellationToken);
			await _securityNotices.QueueAsync(new SecurityNoticeRequest
			{
				UserId = user.Id, Kind = SecurityNoticeKind.TotpEnabled, ClientApplication = UserSessionClientApplication.Web
			}, cancellationToken);

			var finish = await FinishLoginTransactionAsync(transaction, user, MfaEvidenceMethod.Totp, null, DateTime.UtcNow, model.ReturnUrl, false,
				cancellationToken);
			if (finish.Redirect == null)
				return FinishedRedirect(finish, model.ReturnUrl);

			// The new recovery codes are shown once, on the signed-in page that explains them.
			TempData["RecoveryCodes"] = codes;
			return RedirectToAction("ShowRecoveryCodes", "TwoFactor", new { area = "User" });
		}

		private async Task<LoginMfaSetupViewModel> SetupModelAsync(IdentityUser user, LoginMfaSetupViewModel model, string stagedKey = null)
		{
			model.Code = null;
			var (sharedKey, uri) = stagedKey == null
				? await AuthenticatorSetup.StageAsync(_userManager, user)
				: (AuthenticatorSetup.FormatKey(stagedKey), await AuthenticatorSetup.UriAsync(_userManager, user, stagedKey));
			model.SharedKey = sharedKey;
			model.AuthenticatorUri = uri;
			model.QrCodeDataUrl = WebQrCode.DataUrl(uri);
			return model;
		}

		/// <summary>Secret and recovery pages are never cached, stored in history, or embedded elsewhere (plan section 6.2).</summary>
		private void SetNoStoreHeaders()
		{
			Response.Headers["Cache-Control"] = "no-store, no-cache, max-age=0";
			Response.Headers["Pragma"] = "no-cache";
			Response.Headers["X-Frame-Options"] = "DENY";
			Response.Headers["Referrer-Policy"] = "no-referrer";
		}

		// ── "I lost my authenticator" ─────────────────────────────────────────────────────────────────────────────────

		//
		// GET: /Account/LostFactor
		/// <summary>
		/// The lost-authenticator page (plan section 6.3): with a recovery code, a restricted recovery replaces the lost factors; without
		/// one, the page names who can help. There is no automatic reset, and nothing here waives MFA or required SSO.
		/// </summary>
		[HttpGet]
		[AllowAnonymous]
		public async Task<IActionResult> LostFactor(string returnUrl = null, CancellationToken cancellationToken = default)
		{
			var (transaction, _, _) = await OpenLoginTransactionAsync(cancellationToken);
			return View(new LostFactorViewModel { ReturnUrl = SafeReturnUrl(returnUrl), CanRecover = transaction != null && _recoveries.IsEnabled });
		}

		//
		// POST: /Account/LostFactor
		/// <summary>
		/// Opens a restricted recovery from this sign-in's verified first factor and a recovery code (plan section 5.4). The code is spent
		/// and the sign-in ends; a wrong code counts against the sign-in and the account lockout.
		/// </summary>
		[HttpPost]
		[AllowAnonymous]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> LostFactor(LostFactorViewModel model, CancellationToken cancellationToken)
		{
			model ??= new LostFactorViewModel();
			model.ReturnUrl = SafeReturnUrl(model.ReturnUrl);
			var (login, user, outcome) = await OpenLoginTransactionAsync(cancellationToken);
			if (login == null)
				return RestartSignIn(outcome, model.ReturnUrl);

			model.CanRecover = _recoveries.IsEnabled;
			if (!model.CanRecover)
				return View(model);
			if (await _userManager.IsLockedOutAsync(user))
				return RestartSignIn(MfaLoginTransactionOutcome.TooManyAttempts, model.ReturnUrl);
			if (!await _userManager.GetTwoFactorEnabledAsync(user) || string.IsNullOrWhiteSpace(model.Code))
			{
				ModelState.AddModelError(nameof(model.Code), _twoFactorLocalizer["InvalidRecoveryCode"]);
				return View(model);
			}

			// The code is spent here, once (plan section 5.4); if the recovery then cannot open, the user uses another code.
			if (!(await _userManager.RedeemTwoFactorRecoveryCodeAsync(user, model.Code.Replace(" ", string.Empty).Trim())).Succeeded)
			{
				await _userManager.AccessFailedAsync(user);
				await _loginTransactions.RecordFailedAttemptAsync(login, cancellationToken);
				await AuditRecoveryAsync(user, SystemAuditTypes.FactorRecoveryStarted, false, "Web factor recovery refused: wrong recovery code.", cancellationToken);
				if (await _userManager.IsLockedOutAsync(user))
					return RestartSignIn(MfaLoginTransactionOutcome.TooManyAttempts, model.ReturnUrl);

				ModelState.AddModelError(nameof(model.Code), _twoFactorLocalizer["InvalidRecoveryCode"]);
				model.Code = null;
				return View(model);
			}

			await _userManager.ResetAccessFailedCountAsync(user);
			await _loginTransactions.AbandonAsync(login, cancellationToken);
			EndLoginTransactionCookie();
			FactorRecoveryStart start;
			try
			{
				start = await _recoveries.BeginAsync(login, cancellationToken);
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				Logging.LogException(ex, "A Web factor recovery could not be opened after its recovery code was spent.");
				TempData["LoginMfaMessage"] = _twoFactorLocalizer["RecoveryUnavailable"].Value;
				return RedirectToAction(nameof(LogOn), new { returnUrl = model.ReturnUrl });
			}

			await AuditRecoveryAsync(user, SystemAuditTypes.FactorRecoveryStarted, true, "Web factor recovery started with a recovery code.", cancellationToken);
			await _securityNotices.QueueAsync(new SecurityNoticeRequest
			{
				UserId = user.Id, Kind = SecurityNoticeKind.RecoveryCodeUsed, ClientApplication = UserSessionClientApplication.Web
			}, cancellationToken);

			Response.Cookies.Append(RecoveryCookie, start.Secret, new CookieOptions
			{
				HttpOnly = true,
				Secure = true,
				SameSite = SameSiteMode.Strict,
				IsEssential = true,
				Path = MfaLoginCookiePath,
				Expires = start.Transaction.ExpiresOnUtc
			});
			return RedirectToAction(nameof(Recovery));
		}

		private async Task<(FactorRecoveryTransaction Recovery, IdentityUser User, FactorRecoveryOutcome Outcome)> OpenRecoveryAsync(CancellationToken cancellationToken)
		{
			var secret = Request.Cookies[RecoveryCookie];
			if (string.IsNullOrWhiteSpace(secret))
				return (null, null, FactorRecoveryOutcome.Invalid);

			var opened = await _recoveries.OpenAsync(secret, UserSessionClientApplication.Web, cancellationToken);
			if (!opened.IsUsable)
				return (null, null, opened.Outcome);

			var user = await _userManager.FindByIdAsync(opened.Transaction.UserId);
			return user == null ? (null, null, FactorRecoveryOutcome.Invalid) : (opened.Transaction, user, FactorRecoveryOutcome.Usable);
		}

		private IActionResult RecoveryEnded()
		{
			Response.Cookies.Delete(RecoveryCookie, new CookieOptions { Path = MfaLoginCookiePath, Secure = true, SameSite = SameSiteMode.Strict });
			TempData["LoginMfaMessage"] = _twoFactorLocalizer["RecoveryEnded"].Value;
			return RedirectToAction(nameof(LogOn));
		}

		//
		// GET: /Account/Recovery
		/// <summary>The open recovery: a new authenticator to set up, and the passkeys the user may remove as lost.</summary>
		[HttpGet]
		[AllowAnonymous]
		public async Task<IActionResult> Recovery(CancellationToken cancellationToken)
		{
			var (recovery, user, _) = await OpenRecoveryAsync(cancellationToken);
			if (recovery == null)
				return RecoveryEnded();

			SetNoStoreHeaders();
			return View(await RecoveryModelAsync(user, new FactorRecoveryViewModel(), null, cancellationToken));
		}

		//
		// POST: /Account/Recovery
		/// <summary>
		/// Completes the recovery with a code from the new authenticator (plan section 6.3): it becomes the authenticator, the recovery
		/// codes are replaced (shown once), the chosen passkeys are removed, and every session and piece of evidence ends. A wrong code
		/// counts against the recovery, and nothing changes until the code verifies.
		/// </summary>
		[HttpPost]
		[AllowAnonymous]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Recovery(FactorRecoveryViewModel model, CancellationToken cancellationToken)
		{
			model ??= new FactorRecoveryViewModel();
			var (recovery, user, _) = await OpenRecoveryAsync(cancellationToken);
			if (recovery == null)
				return RecoveryEnded();

			SetNoStoreHeaders();
			var stagedKey = await AuthenticatorSetup.GetStagedKeyAsync(_userManager, user);
			if (stagedKey == null)
			{
				ModelState.AddModelError(nameof(model.Code), _twoFactorLocalizer["SetupExpired"]);
				return View(await RecoveryModelAsync(user, model, null, cancellationToken));
			}

			// Only the user's own active passkeys can be chosen for removal.
			var active = await _passkeyRows.GetActiveForUserAsync(user.Id, cancellationToken);
			var remove = (model.RemovePasskeyIds ?? new List<string>()).Distinct(StringComparer.Ordinal).ToList();
			if (remove.Any(id => active.All(p => p.UserPasskeyId != id)))
			{
				ModelState.AddModelError(string.Empty, _twoFactorLocalizer["RecoveryPasskeyNotFound"]);
				return View(await RecoveryModelAsync(user, model, stagedKey, cancellationToken));
			}

			if (!await AuthenticatorSetup.VerifyStagedCodeAsync(_mfaState, user, stagedKey, model.Code, cancellationToken))
			{
				await _recoveries.RecordFailedAttemptAsync(recovery, cancellationToken);
				ModelState.AddModelError(nameof(model.Code), _twoFactorLocalizer["InvalidCodeLogin"]);
				return View(await RecoveryModelAsync(user, model, stagedKey, cancellationToken));
			}

			// One completion per recovery: a concurrent request, an expiry or exhaustion leaves the working factor untouched.
			if (!await _recoveries.TryCompleteAsync(recovery, cancellationToken))
				return RecoveryEnded();

			var now = DateTime.UtcNow;
			await AuthenticatorSetup.PromoteAsync(_userManager, _userStore, _mfaState, user, stagedKey,
				new TotpEnrollmentContext(false, (int)UserSessionClientApplication.Web), cancellationToken);
			var codes = await AuthenticatorSetup.RetireOldAuthorityAsync(_userManager, _userSessionService, _mfaEvidenceService, user, cancellationToken);
			foreach (var id in remove)
				await _passkeyRows.TryRevokeAsync(id, user.Id, PasskeyRevocationReason.FactorRecovery, user.Id, now, cancellationToken);
			await _challenges.CancelPendingForUserAsync(user.Id, cancellationToken);
			await _approvalRows.CancelPendingForUserAsync(user.Id, MfaApprovalEndReason.ApproverRevoked, now, cancellationToken);

			await AuditRecoveryAsync(user, SystemAuditTypes.FactorRecoveryCompleted, true,
				$"Web factor recovery completed: authenticator replaced, recovery codes rotated, {remove.Count} passkey(s) removed, all sessions ended.",
				cancellationToken);
			await _securityNotices.QueueAsync(new SecurityNoticeRequest
			{
				UserId = user.Id, Kind = SecurityNoticeKind.FactorRecoveryCompleted, ClientApplication = UserSessionClientApplication.Web
			}, cancellationToken);

			Response.Cookies.Delete(RecoveryCookie, new CookieOptions { Path = MfaLoginCookiePath, Secure = true, SameSite = SameSiteMode.Strict });
			return View("RecoveryComplete", new RecoveryCompleteViewModel { RecoveryCodes = codes });
		}

		//
		// POST: /Account/RecoveryCancel
		/// <summary>Ends the recovery; the recovery code that opened it stays spent, and nothing about the account changes.</summary>
		[HttpPost]
		[AllowAnonymous]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> RecoveryCancel(CancellationToken cancellationToken)
		{
			var (recovery, user, _) = await OpenRecoveryAsync(cancellationToken);
			if (recovery != null && await _recoveries.CancelAsync(Request.Cookies[RecoveryCookie], UserSessionClientApplication.Web, cancellationToken) ==
					FactorRecoveryOutcome.Usable)
				await AuditRecoveryAsync(user, SystemAuditTypes.FactorRecoveryCanceled, true, "Web factor recovery canceled.", cancellationToken);

			Response.Cookies.Delete(RecoveryCookie, new CookieOptions { Path = MfaLoginCookiePath, Secure = true, SameSite = SameSiteMode.Strict });
			return RedirectToAction(nameof(LogOn));
		}

		private async Task<FactorRecoveryViewModel> RecoveryModelAsync(IdentityUser user, FactorRecoveryViewModel model, string stagedKey,
			CancellationToken cancellationToken)
		{
			model.Code = null;
			var (sharedKey, uri) = stagedKey == null
				? await AuthenticatorSetup.StageAsync(_userManager, user)
				: (AuthenticatorSetup.FormatKey(stagedKey), await AuthenticatorSetup.UriAsync(_userManager, user, stagedKey));
			model.SharedKey = sharedKey;
			model.AuthenticatorUri = uri;
			model.QrCodeDataUrl = WebQrCode.DataUrl(uri);
			model.Passkeys = (await _passkeyRows.GetActiveForUserAsync(user.Id, cancellationToken))
				.Select(p => new RecoveryPasskeyView { Id = p.UserPasskeyId, Name = p.DisplayName, ClientApplication = p.ClientApplication, CreatedOnUtc = p.CreatedOnUtc })
				.ToList();
			return model;
		}

		private Task AuditRecoveryAsync(IdentityUser user, SystemAuditTypes type, bool successful, string data, CancellationToken cancellationToken) =>
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
