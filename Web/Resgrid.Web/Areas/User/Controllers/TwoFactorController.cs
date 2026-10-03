using System;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using QRCoder;
using Resgrid.Config;
using Resgrid.Framework;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Security;
using Resgrid.Model.Services;
using Resgrid.Repositories.DataRepository.Stores;
using Resgrid.Web.Areas.User.Models.TwoFactor;
using Resgrid.Web.Attributes;
using Resgrid.Web.Helpers;
using IdentityUser = Resgrid.Model.Identity.IdentityUser;

namespace Resgrid.Web.Areas.User.Controllers
{
	[Area("User")]
	[Authorize]
	// Authentication and session flows stay available during a department operation lock (ADP plan section 20.2): signing in
	// and out, locking and unlocking a shared session, and verifying a second factor touch no department data.
	[Resgrid.Web.Filters.AllowDuringDepartmentLock]
	public class TwoFactorController : SecureBaseController
	{
		private readonly UserManager<IdentityUser> _userManager;
		private readonly SignInManager<IdentityUser> _signInManager;
		private readonly ISystemAuditsService _systemAuditsService;
		private readonly UrlEncoder _urlEncoder;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.TwoFactor.TwoFactor> _localizer;
		private readonly IUserStore<IdentityUser> _userStore;
		private readonly IUserMfaStateRepository _mfaStateRepository;
		private readonly IUserSessionService _userSessionService;
		private readonly IMfaEvidenceService _mfaEvidenceService;

		// A new authenticator key is held here until a code from it verifies; the active key is never redisplayed
		// (passkey plan section 6.2).
		private const string StagedKeyLoginProvider = StagedAuthenticatorKey.LoginProvider;
		private const string StagedKeyTokenName = StagedAuthenticatorKey.TokenName;

		// Replacement authority: a TOTP step-up this recent, or a recovery-code sign-in this recent.
		private const int ReplaceStepUpWindowMinutes = 5;
		private const int ReplaceRecoverySessionWindowMinutes = 15;

		public TwoFactorController(
			UserManager<IdentityUser> userManager,
			SignInManager<IdentityUser> signInManager,
			ISystemAuditsService systemAuditsService,
			UrlEncoder urlEncoder,
			IStringLocalizer<Resgrid.Localization.Areas.User.TwoFactor.TwoFactor> localizer,
			IUserStore<IdentityUser> userStore,
			IUserMfaStateRepository mfaStateRepository,
			IUserSessionService userSessionService,
			IMfaEvidenceService mfaEvidenceService,
			IMfaPolicyService mfaPolicyService,
			IUserPasskeyRepository passkeyRepository,
			ISecurityNoticeService securityNotices,
			IMfaActivityService mfaActivity,
			IPasskeyService passkeys,
			IMfaApprovalService approvals,
			ISsoBrokerService ssoBroker,
			ISsoReturnTargetRegistry ssoReturnTargets,
			IDepartmentSsoService departmentSso,
			IDepartmentsService departments,
			ICacheProvider cacheProvider,
			IDataProtectionProvider dataProtection)
		{
			_departments = departments;
			_cacheProvider = cacheProvider;
			_dataProtection = dataProtection;
			_passkeys = passkeys;
			_approvals = approvals;
			_ssoBroker = ssoBroker;
			_ssoReturnTargets = ssoReturnTargets;
			_departmentSso = departmentSso;
			_mfaActivity = mfaActivity;
			_securityNotices = securityNotices;
			_mfaPolicyService = mfaPolicyService;
			_passkeyRepository = passkeyRepository;
			_userManager = userManager;
			_signInManager = signInManager;
			_systemAuditsService = systemAuditsService;
			_urlEncoder = urlEncoder;
			_localizer = localizer;
			_userStore = userStore;
			_mfaStateRepository = mfaStateRepository;
			_userSessionService = userSessionService;
			_mfaEvidenceService = mfaEvidenceService;
		}

		private readonly IMfaPolicyService _mfaPolicyService;
		private readonly IUserPasskeyRepository _passkeyRepository;
		private readonly ISecurityNoticeService _securityNotices;
		private readonly IMfaActivityService _mfaActivity;
		private readonly IPasskeyService _passkeys;
		private readonly IMfaApprovalService _approvals;
		private readonly ISsoBrokerService _ssoBroker;
		private readonly ISsoReturnTargetRegistry _ssoReturnTargets;
		private readonly IDepartmentSsoService _departmentSso;
		private readonly IDepartmentsService _departments;
		private readonly ICacheProvider _cacheProvider;
		private readonly IDataProtectionProvider _dataProtection;

		// ── Index ─────────────────────────────────────────────────────────────────

		[HttpGet]
		public async Task<IActionResult> Index(string passkeyStatus = null, CancellationToken cancellationToken = default)
		{
			var user = await _userManager.GetUserAsync(User);
			if (user == null) return NotFound();

			var codesLeft = await _userManager.CountRecoveryCodesAsync(user);

			var model = new TwoFactorIndexViewModel
			{
				HasAuthenticator = await _userManager.GetAuthenticatorKeyAsync(user) != null,
				Is2FAEnabled = await _userManager.GetTwoFactorEnabledAsync(user),
				RecoveryCodesLeft = codesLeft,
				RecoveryCodeWarning = codesLeft <= TwoFactorConfig.RecoveryCodeWarningThreshold,
				WebPasskeyRegistrationAvailable = _passkeys.IsRegistrationAvailable(UserSessionClientApplication.Web),
				PasskeyStatus = passkeyStatus is "added" or "renamed" or "removed" ? passkeyStatus : null,
				Passkeys = (await _passkeys.GetActiveForUserAsync(user.Id, cancellationToken) ?? Array.Empty<UserPasskey>())
					.OrderBy(p => p.ClientApplication).ThenBy(p => p.CreatedOnUtc)
					.Select(p => new PasskeyRowView
					{
						Id = p.UserPasskeyId,
						DisplayName = p.DisplayName,
						AppLabelKey = AppLabelKey((UserSessionClientApplication)p.ClientApplication),
						CreatedOn = DateTime.SpecifyKind(p.CreatedOnUtc, DateTimeKind.Utc),
						LastUsedOn = p.LastUsedOnUtc == null ? null : DateTime.SpecifyKind(p.LastUsedOnUtc.Value, DateTimeKind.Utc),
						CreatedOnSharedInstallation = p.RegisteredInSharedMode
					}).ToList()
			};

			return View(model);
		}

		private static string AppLabelKey(UserSessionClientApplication client) => client switch
		{
			UserSessionClientApplication.Responder => "PasskeyAppResponder",
			UserSessionClientApplication.Unit => "PasskeyAppUnit",
			UserSessionClientApplication.Dispatch => "PasskeyAppDispatch",
			UserSessionClientApplication.Command => "PasskeyAppCommand",
			_ => "PasskeyAppWeb"
		};

		// ── Enable 2FA ────────────────────────────────────────────────────────────

		[HttpGet]
		public async Task<IActionResult> Enable2FA()
		{
			var user = await _userManager.GetUserAsync(User);
			if (user == null) return NotFound();

			// An enrolled authenticator's secret is never shown again; moving to a new app is ReplaceAuthenticator.
			if (await _userManager.GetTwoFactorEnabledAsync(user))
				return RedirectToAction(nameof(Index));

			// Setting up a factor needs a recent password (or SSO) verification for this session (plan section 6.2).
			if (!await HasFreshFirstFactorAsync(user, TwoFactorConfig.FirstFactorReauthWindowMinutes))
				return await RedirectToReauthenticateAsync();

			return View(await BuildAuthenticatorModelAsync(user, await StageNewAuthenticatorKeyAsync(user), isReplacement: false));
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Enable2FA(EnableAuthenticatorViewModel model, CancellationToken cancellationToken)
		{
			var user = await _userManager.GetUserAsync(User);
			if (user == null) return NotFound();

			if (await _userManager.GetTwoFactorEnabledAsync(user))
				return RedirectToAction(nameof(Index));

			// The change started inside the reauthentication window; it must also finish inside the operation window.
			if (!await HasFreshFirstFactorAsync(user, TwoFactorConfig.FirstFactorOperationWindowMinutes))
				return await RedirectToReauthenticateAsync(Url.Action(nameof(Enable2FA)));

			var stagedKey = await GetStagedAuthenticatorKeyAsync(user);
			if (string.IsNullOrEmpty(stagedKey))
				return RedirectToAction(nameof(Enable2FA));

			if (!ModelState.IsValid)
				return View(await BuildAuthenticatorModelAsync(user, stagedKey, isReplacement: false));

			// The staged key is verified directly and its time step consumed, so the same code cannot be replayed.
			if (!await ResgridAuthenticatorTokenProvider.ValidateAndConsumeAsync(_mfaStateRepository, user.Id, stagedKey,
					model.Code, DateTime.UtcNow, cancellationToken))
			{
				ModelState.AddModelError(nameof(model.Code), _localizer["InvalidCodeError"]);
				return View(await BuildAuthenticatorModelAsync(user, stagedKey, isReplacement: false));
			}

			await PromoteStagedAuthenticatorKeyAsync(user, stagedKey, cancellationToken);
			await _userManager.SetTwoFactorEnabledAsync(user, true);

			await _systemAuditsService.SaveSystemAuditAsync(new SystemAudit
			{
				System = (int)SystemAuditSystems.Website,
				Type = (int)SystemAuditTypes.TwoFactorEnabled,
				UserId = user.Id,
				Username = user.UserName,
				Successful = true,
				IpAddress = IpAddressHelper.GetRequestIP(Request, true),
				ServerName = Environment.MachineName,
				Data = $"2FA enabled via web. {Request.Headers["User-Agent"]}"
			}, cancellationToken);
			await NoticeAsync(user, SecurityNoticeKind.TotpEnabled, cancellationToken);

			var recoveryCodes = await _userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, TwoFactorConfig.DefaultRecoveryCodeCount);

			TempData["RecoveryCodes"] = recoveryCodes.ToArray();
			TempData["StatusMessage"] = "Your authenticator app has been verified. Save your recovery codes below.";

			return RedirectToAction(nameof(ShowRecoveryCodes));
		}

		// ── Replace Authenticator ─────────────────────────────────────────────────
		// Moves an enrolled account to a new authenticator (passkey plan sections 6.2, 7.5 rule 7). Allowed after a fresh
		// TOTP step-up, or shortly after a recovery-code sign-in (the lost-authenticator journey). The new key is staged
		// and verified before it replaces the old one; success rotates recovery codes, advances the authentication
		// generation and signs every session out.

		[HttpGet]
		public async Task<IActionResult> ReplaceAuthenticator(CancellationToken cancellationToken)
		{
			var user = await _userManager.GetUserAsync(User);
			if (user == null) return NotFound();

			if (!await _userManager.GetTwoFactorEnabledAsync(user))
				return RedirectToAction(nameof(Enable2FA));

			// Replacement needs a fresh first factor AND an existing factor (or recovery authority).
			if (!await HasFreshFirstFactorAsync(user, TwoFactorConfig.FirstFactorReauthWindowMinutes))
				return await RedirectToReauthenticateAsync();

			if (!await HasReplacementAuthorityAsync(user, cancellationToken))
				return RedirectToStepUp();

			return View(nameof(Enable2FA), await BuildAuthenticatorModelAsync(user, await StageNewAuthenticatorKeyAsync(user), isReplacement: true));
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> ReplaceAuthenticator(EnableAuthenticatorViewModel model, CancellationToken cancellationToken)
		{
			var user = await _userManager.GetUserAsync(User);
			if (user == null) return NotFound();

			if (!await _userManager.GetTwoFactorEnabledAsync(user))
				return RedirectToAction(nameof(Enable2FA));

			if (!await HasFreshFirstFactorAsync(user, TwoFactorConfig.FirstFactorOperationWindowMinutes))
				return await RedirectToReauthenticateAsync(Url.Action(nameof(ReplaceAuthenticator)));

			if (!await HasReplacementAuthorityAsync(user, cancellationToken))
				return RedirectToStepUp();

			var stagedKey = await GetStagedAuthenticatorKeyAsync(user);
			if (string.IsNullOrEmpty(stagedKey))
				return RedirectToAction(nameof(ReplaceAuthenticator));

			if (!ModelState.IsValid)
				return View(nameof(Enable2FA), await BuildAuthenticatorModelAsync(user, stagedKey, isReplacement: true));

			var now = DateTime.UtcNow;
			if (!await ResgridAuthenticatorTokenProvider.ValidateAndConsumeAsync(_mfaStateRepository, user.Id, stagedKey,
					model.Code, now, cancellationToken))
			{
				ModelState.AddModelError(nameof(model.Code), _localizer["InvalidCodeError"]);
				return View(nameof(Enable2FA), await BuildAuthenticatorModelAsync(user, stagedKey, isReplacement: true));
			}

			await PromoteStagedAuthenticatorKeyAsync(user, stagedKey, cancellationToken);

			// The old seed, remembered browsers (security stamp) and every session derived from the old factor end here.
			user.AuthenticationGeneration++;
			user.CredentialsValidAfterUtc = now;
			user.AuthenticationStateChangedOn = now;
			await _userManager.UpdateSecurityStampAsync(user);
			var recoveryCodes = (await _userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, TwoFactorConfig.DefaultRecoveryCodeCount)).ToArray();
			await _userSessionService.RevokeAllAfterCredentialChangeAsync(user.Id, user.Id,
				UserSessionRevocationReason.MfaChanged, now, cancellationToken);
			await _mfaEvidenceService.RevokeForUserAsync(user.Id, cancellationToken);

			await _systemAuditsService.SaveSystemAuditAsync(new SystemAudit
			{
				System = (int)SystemAuditSystems.Website,
				Type = (int)SystemAuditTypes.TwoFactorAuthenticatorReplaced,
				UserId = user.Id,
				Username = user.UserName,
				Successful = true,
				IpAddress = IpAddressHelper.GetRequestIP(Request, true),
				ServerName = Environment.MachineName,
				Data = $"Authenticator replaced via web; all sessions revoked. {Request.Headers["User-Agent"]}"
			}, cancellationToken);
			await NoticeAsync(user, SecurityNoticeKind.TotpReplaced, cancellationToken);

			// This request is still authenticated, so the new codes render once before the cookie is gone.
			await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
			return View(nameof(ShowRecoveryCodes), new ShowRecoveryCodesViewModel { RecoveryCodes = recoveryCodes, SignInAgainRequired = true });
		}

		// ── Recovery Codes ────────────────────────────────────────────────────────

		[HttpGet]
		public IActionResult ShowRecoveryCodes()
		{
			var codes = TempData["RecoveryCodes"] as string[];
			if (codes == null || codes.Length == 0)
				return RedirectToAction(nameof(Index));

			return View(new ShowRecoveryCodesViewModel { RecoveryCodes = codes });
		}

		// Recovery codes are account factor management (passkey plan section 7.6 row 14): TOTP or this app's passkey only,
		// never Responder approval or provider step-up, whatever the department accepts for sign-in.
		[HttpGet]
		[RequiresRecentTwoFactor(MethodScope = MfaMethodScope.Account)]
		public async Task<IActionResult> ViewRecoveryCodes()
		{
			var user = await _userManager.GetUserAsync(User);
			if (user == null) return NotFound();

			if (!await _userManager.GetTwoFactorEnabledAsync(user))
				return RedirectToAction(nameof(Index));

			// Recovery codes are hashed; we can only show count — regenerate to view
			return RedirectToAction(nameof(RegenerateRecoveryCodes));
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[RequiresRecentTwoFactor(MethodScope = MfaMethodScope.Account)]
		public async Task<IActionResult> RegenerateRecoveryCodes()
		{
			var user = await _userManager.GetUserAsync(User);
			if (user == null) return NotFound();

			if (!await _userManager.GetTwoFactorEnabledAsync(user))
				return RedirectToAction(nameof(Index));

			var recoveryCodes = await _userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, TwoFactorConfig.DefaultRecoveryCodeCount);
			await NoticeAsync(user, SecurityNoticeKind.RecoveryCodesRegenerated, HttpContext.RequestAborted);
			TempData["RecoveryCodes"] = recoveryCodes.ToArray();
			TempData["StatusMessage"] = "Your recovery codes have been regenerated.";

			return RedirectToAction(nameof(ShowRecoveryCodes));
		}

		// ── Disable 2FA ───────────────────────────────────────────────────────────

		[HttpGet]
		public async Task<IActionResult> Disable2FA()
		{
			var user = await _userManager.GetUserAsync(User);
			if (user == null) return NotFound();

			if (!await HasFreshFirstFactorAsync(user, TwoFactorConfig.FirstFactorReauthWindowMinutes))
				return await RedirectToReauthenticateAsync();

			return View(new Disable2FAViewModel { BlockedByPasskeys = await _passkeyRepository.CountActiveForUserAsync(user.Id) > 0 });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Disable2FA(Disable2FAViewModel model, CancellationToken cancellationToken)
		{
			var user = await _userManager.GetUserAsync(User);
			if (user == null) return NotFound();

			if (!await HasFreshFirstFactorAsync(user, TwoFactorConfig.FirstFactorOperationWindowMinutes))
				return await RedirectToReauthenticateAsync(Url.Action(nameof(Disable2FA)));

			// TOTP is the fallback every passkey relies on in this release, so it cannot be turned off while any passkey
			// exists (plan section 7.5 rule 7). Remove the passkeys first.
			model.BlockedByPasskeys = await _passkeyRepository.CountActiveForUserAsync(user.Id, cancellationToken) > 0;
			if (model.BlockedByPasskeys)
			{
				ModelState.AddModelError(string.Empty, _localizer["DisableBlockedByPasskeys"]);
				return View(model);
			}

			if (!ModelState.IsValid) return View(model);

			var verificationCode = model.Code.Replace(" ", string.Empty).Replace("-", string.Empty);
			var isValid = await _userManager.VerifyTwoFactorTokenAsync(user,
				_userManager.Options.Tokens.AuthenticatorTokenProvider, verificationCode);

			if (!isValid)
			{
				ModelState.AddModelError(nameof(model.Code), _localizer["InvalidCodeError"]);
				return View(model);
			}

			// Turning MFA off advances the authentication generation, discards the seed and recovery codes, forgets
			// remembered browsers and ends every session established under the old factor (plan section 7.5 rule 7).
			var now = DateTime.UtcNow;
			user.AuthenticationGeneration++;
			user.CredentialsValidAfterUtc = now;
			user.AuthenticationStateChangedOn = now;
			await _userManager.SetTwoFactorEnabledAsync(user, false);
			await _userManager.ResetAuthenticatorKeyAsync(user);
			await _userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 0);
			await _userManager.RemoveAuthenticationTokenAsync(user, StagedKeyLoginProvider, StagedKeyTokenName);
			await _mfaEvidenceService.RevokeForUserAsync(user.Id, cancellationToken);

			await _systemAuditsService.SaveSystemAuditAsync(new SystemAudit
			{
				System = (int)SystemAuditSystems.Website,
				Type = (int)SystemAuditTypes.TwoFactorDisabled,
				UserId = user.Id,
				Username = user.UserName,
				Successful = true,
				IpAddress = IpAddressHelper.GetRequestIP(Request, true),
				ServerName = Environment.MachineName,
				Data = $"2FA disabled via web; all sessions revoked. {Request.Headers["User-Agent"]}"
			}, cancellationToken);
			await NoticeAsync(user, SecurityNoticeKind.TotpDisabled, cancellationToken);

			await _signInManager.ForgetTwoFactorClientAsync();
			await _userSessionService.RevokeAllAfterCredentialChangeAsync(user.Id, user.Id,
				UserSessionRevocationReason.MfaChanged, now, cancellationToken);
			await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
			return RedirectToAction("LogOn", "Account", new { area = "", reason = "mfa-changed" });
		}

		// ── Step-Up Verification ──────────────────────────────────────────────────

		[HttpGet]
		[AllowAnonymous]
		public async Task<IActionResult> Verify2FA(string returnUrl = null, string scope = null, CancellationToken cancellationToken = default,
			int? entry = null, bool resubmit = false)
		{
			var methodScope = ParseScope(scope);
			var entering = await EntryDepartmentAsync(entry);
			return View(new StepUpVerifyViewModel
			{
				ReturnUrl = returnUrl,
				Scope = methodScope.ToString(),
				EntryDepartmentId = entering,
				SubmissionNotHeld = resubmit,
				PasskeyAvailable = await PasskeyStepUpAvailableAsync(scope, cancellationToken, entering),
				ApprovalAvailable = await ApprovalStepUpAvailableAsync(methodScope, cancellationToken, entering),
				FederatedAvailable = entering == null && await FederatedStepUpAvailableAsync(methodScope, cancellationToken)
			});
		}

		/// <summary>
		/// The department being entered, whose switches then decide the methods (plan section 7.6 row 5: verify with a method the
		/// target accepts before switching), when the user is a member of it; null for the active department. A display decision
		/// that fails safe to the active department.
		/// </summary>
		private async Task<int?> EntryDepartmentAsync(int? entry)
		{
			if (entry is not int target || target <= 0)
				return null;

			try
			{
				return target != DepartmentId && await _departments.IsMemberOfDepartmentAsync(target, UserId) ? target : null;
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				Logging.LogException(ex, "The department being entered could not be checked; the active department's methods apply.");
				return null;
			}
		}

		/// <summary>
		/// Whether to offer provider step-up (plan section 7.8): Web SSO is set up, the session is tracked, the account signs in through
		/// the department's provider under a tested mapping, and the scope accepts the provider's MFA here (never account factors).
		/// </summary>
		private async Task<bool> FederatedStepUpAvailableAsync(MfaMethodScope scope, CancellationToken cancellationToken)
		{
			try
			{
				var user = await _userManager.GetUserAsync(User);
				if (user == null || scope == MfaMethodScope.Account || HttpProtectedGrantContext.SessionOf(HttpContext) == null ||
					!WebSsoRoundTrip.IsAvailable(_ssoBroker, _ssoReturnTargets))
					return false;

				return await _mfaPolicyService.IsMethodAcceptedAsync(DepartmentId, scope, MfaEvidenceMethod.Federated, cancellationToken) &&
					await _departmentSso.IsFederatedMfaAvailableAsync(DepartmentId, user.Id, cancellationToken);
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				Logging.LogException(ex, "Provider step-up availability could not be read; the other methods are shown.");
				return false;
			}
		}

		/// <summary>Verify2FA again after a refused code, with the same choices: they come from the server, never the form.</summary>
		private async Task<IActionResult> StepUpViewAsync(StepUpVerifyViewModel model, CancellationToken cancellationToken)
		{
			var methodScope = ParseScope(model.Scope);
			var entering = await EntryDepartmentAsync(model.EntryDepartmentId);
			model.Scope = methodScope.ToString();
			model.EntryDepartmentId = entering;
			model.PasskeyAvailable = await PasskeyStepUpAvailableAsync(model.Scope, cancellationToken, entering);
			model.ApprovalAvailable = await ApprovalStepUpAvailableAsync(methodScope, cancellationToken, entering);
			model.FederatedAvailable = entering == null && await FederatedStepUpAvailableAsync(methodScope, cancellationToken);
			return View(nameof(Verify2FA), model);
		}

		private static MfaMethodScope ParseScope(string scope) =>
			Enum.TryParse<MfaMethodScope>(scope, true, out var parsed) && Enum.IsDefined(parsed) ? parsed : MfaMethodScope.Login;

		/// <summary>
		/// Whether to offer Responder approval at step-up (plan section 7.9): a tracked session, an eligible Responder of the user's,
		/// and a scope that accepts approval in the active department (never security changes or account factors). Display only.
		/// </summary>
		private async Task<bool> ApprovalStepUpAvailableAsync(MfaMethodScope scope, CancellationToken cancellationToken, int? department = null)
		{
			try
			{
				var user = await _userManager.GetUserAsync(User);
				if (user == null || HttpProtectedGrantContext.SessionOf(HttpContext) == null)
					return false;

				return await _approvals.IsAvailableAsync(user.Id, UserSessionClientApplication.Web, cancellationToken) &&
					await _mfaPolicyService.IsMethodAcceptedAsync(department ?? DepartmentId, scope, MfaEvidenceMethod.PasskeyApproval, cancellationToken);
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				Logging.LogException(ex, "Approval step-up availability could not be read; the other methods are shown.");
				return false;
			}
		}

		/// <summary>Asks the user's Responder to approve this step-up; the number is shown on this page only.</summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Verify2FARequestApproval([FromForm] string scope, CancellationToken cancellationToken, [FromForm] int? entry = null)
		{
			var user = await _userManager.GetUserAsync(User);
			var session = HttpProtectedGrantContext.SessionOf(HttpContext);
			if (user == null || session == null)
				return Json(new { success = false, error = MfaApprovalOutcomes.ErrorCode(MfaApprovalOutcome.SessionRequired) });

			var start = await _approvals.RequestAsync(new MfaApprovalRequester
			{
				UserId = user.Id,
				Kind = MfaApprovalRequesterKind.Session,
				RequesterId = session.SessionId,
				ClientApplication = UserSessionClientApplication.Web,
				AuthenticationGeneration = session.AuthenticationGeneration,
				DepartmentId = await EntryDepartmentAsync(entry) ?? DepartmentId,
				Purpose = MfaApprovalPurpose.StepUp,
				Operation = MfaStepUpOperations.ForScope(ParseScope(scope)),
				SharedMode = session.SharedMode,
				LockVersion = session.SessionLockVersion,
				IpAddress = IpAddressHelper.GetRequestIP(Request, true),
				UserName = user.UserName,
				AuditSystem = SystemAuditSystems.Website
			}, cancellationToken);
			return start.Succeeded
				? Json(new { success = true, approvalRequestId = start.ApprovalRequestId, matchNumber = start.MatchNumber, expiresIn = start.ExpiresInSeconds })
				: Json(new { success = false, error = MfaApprovalOutcomes.ErrorCode(start.Outcome) ?? "approval_unavailable" });
		}

		/// <summary>The state of this session's own step-up approval request.</summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Verify2FAApprovalStatus([FromForm] string approvalRequestId, CancellationToken cancellationToken)
		{
			var session = HttpProtectedGrantContext.SessionOf(HttpContext);
			if (session == null)
				return Json(new { success = false, error = MfaApprovalOutcomes.ErrorCode(MfaApprovalOutcome.SessionRequired) });

			var found = await _approvals.GetForRequesterAsync(approvalRequestId, MfaApprovalRequesterKind.Session, session.SessionId, cancellationToken);
			return found.Succeeded
				? Json(new { success = true, state = MfaApprovalOutcomes.StateName(found.Request.EffectiveState(DateTime.UtcNow)) })
				: Json(new { success = false, error = MfaApprovalOutcomes.ErrorCode(found.Outcome) ?? "approval_unavailable" });
		}

		/// <summary>
		/// Uses this session's approved step-up request once (plan section 7.9 step 6): it must be for this scope's operation, at the
		/// session's lock version, where the department still accepts approval. The evidence names the approving passkey and
		/// Responder session and carries the approval time; the user returns to the local page that asked.
		/// </summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Verify2FACompleteApproval([FromForm] string approvalRequestId, [FromForm] string scope, [FromForm] string returnUrl,
			CancellationToken cancellationToken, [FromForm] int? entry = null)
		{
			var user = await _userManager.GetUserAsync(User);
			var session = HttpProtectedGrantContext.SessionOf(HttpContext);
			if (user == null || session == null)
				return Json(new { success = false, error = MfaApprovalOutcomes.ErrorCode(MfaApprovalOutcome.SessionRequired) });

			var methodScope = ParseScope(scope);
			if (!await _mfaPolicyService.IsMethodAcceptedAsync(await EntryDepartmentAsync(entry) ?? DepartmentId, methodScope, MfaEvidenceMethod.PasskeyApproval,
					cancellationToken))
				return Json(new { success = false, error = "mfa_method_not_allowed" });

			var found = await _approvals.GetForRequesterAsync(approvalRequestId, MfaApprovalRequesterKind.Session, session.SessionId, cancellationToken);
			if (found.Succeeded && (found.Request.RequestPurpose != MfaApprovalPurpose.StepUp ||
					!string.Equals(found.Request.Operation, MfaStepUpOperations.ForScope(methodScope), StringComparison.Ordinal) ||
					found.Request.LockVersion != session.SessionLockVersion))
				return Json(new { success = false, error = "approval_expired" });

			var consumed = await _approvals.ConsumeAsync(approvalRequestId, MfaApprovalRequesterKind.Session, session.SessionId, user.Id,
				session.AuthenticationGeneration, cancellationToken);
			if (!consumed.Succeeded)
				return Json(new { success = false, error = MfaApprovalOutcomes.ErrorCode(consumed.Outcome) ?? "approval_unavailable" });

			var approval = consumed.Request;
			if (!await RecordEvidenceAsync(user, MfaEvidenceKind.SecondFactor, MfaEvidenceMethod.PasskeyApproval, MfaEvidencePurpose.StepUp,
					approval.DecidedOnUtc ?? DateTime.UtcNow, cancellationToken, MfaApprovalRequest.FactorReferenceFor(approval.ApproverPasskeyId, approval.ApproverSessionId)))
				return Json(new { success = false, error = "service_unavailable" });

			await _systemAuditsService.SaveSystemAuditAsync(new SystemAudit
			{
				System = (int)SystemAuditSystems.Website,
				Type = (int)SystemAuditTypes.TwoFactorStepUpVerified,
				UserId = user.Id,
				Username = user.UserName,
				Successful = true,
				IpAddress = IpAddressHelper.GetRequestIP(Request, true),
				ServerName = Environment.MachineName,
				Data = "Step-up verified with a Responder approval."
			}, cancellationToken);

			return Json(new
			{
				success = true,
				redirect = !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : Url.Action("Dashboard", "Home", new { area = "User" })
			});
		}

		/// <summary>
		/// Whether to offer a passkey at step-up: the user has one for the web, and the scope of the action that sent them here
		/// accepts a passkey in the active department (passkey plan section 7.6 rows 7 and 13-15). A display decision only;
		/// the guarded action checks the evidence it gets.
		/// </summary>
		private async Task<bool> PasskeyStepUpAvailableAsync(string scope, CancellationToken cancellationToken, int? department = null)
		{
			try
			{
				var user = await _userManager.GetUserAsync(User);
				if (user == null || HttpProtectedGrantContext.SessionOf(HttpContext) == null)
					return false;

				var methodScope = ParseScope(scope);
				return await _passkeys.HasActiveForClientAsync(user.Id, UserSessionClientApplication.Web, cancellationToken) &&
					await _mfaPolicyService.IsMethodAcceptedAsync(department ?? DepartmentId, methodScope, MfaEvidenceMethod.Passkey, cancellationToken);
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				Logging.LogException(ex, "Passkey step-up availability could not be read; the code prompt is shown alone.");
				return false;
			}
		}

		/// <summary>Assertion options for a Web passkey, bound to this session, for a sensitive operation (step-up).</summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Verify2FAPasskeyOptions(CancellationToken cancellationToken)
		{
			var user = await _userManager.GetUserAsync(User);
			if (user == null) return Json(new { success = false, error = PasskeyOutcomes.ErrorCode(PasskeyOutcome.SessionRequired) });

			var start = await _passkeys.BeginAssertionAsync(PasskeyCaller(user), AuthenticationChallengePurpose.SensitiveOperation, cancellationToken);
			return start.Succeeded
				? Json(new { success = true, requestId = start.RequestId, options = start.OptionsJson })
				: Json(new { success = false, error = PasskeyOutcomes.ErrorCode(start.Outcome) });
		}

		/// <summary>
		/// Verifies a Web passkey for step-up and records it as this session's second-factor evidence, then sends the user
		/// back to the local page that asked (passkey plan section 7.5 rule 2: Verify2FA accepts either method).
		/// </summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Verify2FAPasskey([FromForm] string requestId, [FromForm] string credential, [FromForm] string returnUrl,
			CancellationToken cancellationToken)
		{
			var user = await _userManager.GetUserAsync(User);
			if (user == null) return Json(new { success = false, error = PasskeyOutcomes.ErrorCode(PasskeyOutcome.SessionRequired) });
			if (string.IsNullOrWhiteSpace(requestId) || string.IsNullOrWhiteSpace(credential))
				return Json(new { success = false, error = PasskeyOutcomes.ErrorCode(PasskeyOutcome.InvalidRequest) });

			var assertion = await _passkeys.CompleteAssertionAsync(PasskeyCaller(user), AuthenticationChallengePurpose.SensitiveOperation, requestId, credential,
				cancellationToken);
			if (!assertion.Succeeded)
			{
				if (assertion.Outcome is PasskeyOutcome.VerificationFailed or PasskeyOutcome.NotRegisteredForClient)
					await _mfaActivity.RecordAsync(new MfaActivityEntry
					{
						UserId = user.Id, Method = MfaEvidenceMethod.Passkey, Purpose = MfaEvidencePurpose.StepUp, Successful = false,
						ClientApplication = UserSessionClientApplication.Web, SessionId = MfaEvidence.TrackedSessionId(MfaEvidenceSession.KeyFor(User, HttpContext))
					}, cancellationToken);
				return Json(new { success = false, error = PasskeyOutcomes.ErrorCode(assertion.Outcome) });
			}

			if (!await RecordEvidenceAsync(user, MfaEvidenceKind.SecondFactor, MfaEvidenceMethod.Passkey, MfaEvidencePurpose.StepUp, assertion.VerifiedOnUtc,
					cancellationToken, UserPasskey.FactorReferenceFor(assertion.Passkey.UserPasskeyId)))
				return Json(new { success = false, error = "service_unavailable" });

			await _systemAuditsService.SaveSystemAuditAsync(new SystemAudit
			{
				System = (int)SystemAuditSystems.Website,
				Type = (int)SystemAuditTypes.TwoFactorStepUpVerified,
				UserId = user.Id,
				Username = user.UserName,
				Successful = true,
				IpAddress = IpAddressHelper.GetRequestIP(Request, true),
				ServerName = Environment.MachineName,
				Data = "Step-up verified with a passkey."
			}, cancellationToken);

			return Json(new
			{
				success = true,
				redirect = !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : Url.Action("Dashboard", "Home", new { area = "User" })
			});
		}

		private PasskeyCaller PasskeyCaller(IdentityUser user) =>
			Resgrid.Model.Security.PasskeyCaller.From(HttpProtectedGrantContext.SessionOf(HttpContext), user.Id, user.UserName, DepartmentId,
				SystemAuditSystems.Website, IpAddressHelper.GetRequestIP(Request, true));

		[HttpPost]
		[ValidateAntiForgeryToken]
		[AllowAnonymous]
		public async Task<IActionResult> Verify2FA(StepUpVerifyViewModel model, CancellationToken cancellationToken)
		{
			if (!ModelState.IsValid) return await StepUpViewAsync(model, cancellationToken);

			var user = await _userManager.GetUserAsync(User);
			if (user == null) return Challenge();

			// Step-up codes share the account lockout with sign-in (passkey plan section 7.5 rule 6): a session that
			// already holds the password does not get unlimited guesses at the second factor.
			if (await _userManager.IsLockedOutAsync(user))
			{
				ModelState.AddModelError(nameof(model.Code), "Too many failed attempts. Wait a few minutes and try again.");
				return await StepUpViewAsync(model, cancellationToken);
			}

			var verificationCode = model.Code.Replace(" ", string.Empty).Replace("-", string.Empty);
			var isValid = await _userManager.VerifyTwoFactorTokenAsync(user,
				_userManager.Options.Tokens.AuthenticatorTokenProvider, verificationCode);

			if (!isValid)
			{
				await _userManager.AccessFailedAsync(user);
				await _mfaActivity.RecordAsync(new MfaActivityEntry
				{
					UserId = user.Id, Method = MfaEvidenceMethod.Totp, Purpose = MfaEvidencePurpose.StepUp, Successful = false,
					ClientApplication = UserSessionClientApplication.Web, SessionId = MfaEvidence.TrackedSessionId(MfaEvidenceSession.KeyFor(User, HttpContext))
				}, cancellationToken);
				ModelState.AddModelError(nameof(model.Code), "Verification code is invalid.");
				return await StepUpViewAsync(model, cancellationToken);
			}

			await _userManager.ResetAccessFailedCountAsync(user);

			// The proof is server-side evidence for this session and generation; RequiresRecentTwoFactor reads it. Without
			// it the user would be sent straight back here, so say so instead of looping.
			var verifiedAt = DateTime.UtcNow;
			if (!await RecordEvidenceAsync(user, MfaEvidenceKind.SecondFactor, MfaEvidenceMethod.Totp, MfaEvidencePurpose.StepUp, verifiedAt, cancellationToken))
			{
				ModelState.AddModelError(nameof(model.Code), "Your verification could not be recorded. Wait for the next code and try again.");
				return await StepUpViewAsync(model, cancellationToken);
			}

			await _systemAuditsService.SaveSystemAuditAsync(new SystemAudit
			{
				System = (int)SystemAuditSystems.Website,
				Type = (int)SystemAuditTypes.TwoFactorStepUpVerified,
				UserId = user.Id,
				Username = user.UserName,
				Successful = true,
				IpAddress = IpAddressHelper.GetRequestIP(Request, true),
				ServerName = Environment.MachineName,
				Data = $"Step-up 2FA verified. {Request.Headers["User-Agent"]}"
			}, cancellationToken);

			if (!string.IsNullOrWhiteSpace(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
				return Redirect(model.ReturnUrl);

			return RedirectToAction("Dashboard", "Home", new { area = "User" });
		}

		// ── Helpers ───────────────────────────────────────────────────────────────

		/// <summary>A security notice to the account holder (passkey plan section 6.4); it never fails the change it reports.</summary>
		private Task NoticeAsync(IdentityUser user, SecurityNoticeKind kind, CancellationToken cancellationToken) =>
			_securityNotices.QueueAsync(new SecurityNoticeRequest { UserId = user.Id, Kind = kind, ClientApplication = UserSessionClientApplication.Web },
				cancellationToken);

		private async Task<string> StageNewAuthenticatorKeyAsync(IdentityUser user)
		{
			var key = _userManager.GenerateNewAuthenticatorKey();
			await _userManager.SetAuthenticationTokenAsync(user, StagedKeyLoginProvider, StagedKeyTokenName,
				StagedAuthenticatorKey.Serialize(key, DateTime.UtcNow));
			return key;
		}

		// A staged key is usable only for its short lifetime: it was authorized by the fresh first factor that staged it.
		private async Task<string> GetStagedAuthenticatorKeyAsync(IdentityUser user)
			=> StagedAuthenticatorKey.ReadUsableKey(
				await _userManager.GetAuthenticationTokenAsync(user, StagedKeyLoginProvider, StagedKeyTokenName),
				DateTime.UtcNow, TimeSpan.FromMinutes(Math.Max(1, TwoFactorConfig.StagedAuthenticatorLifetimeMinutes)));

		private async Task PromoteStagedAuthenticatorKeyAsync(IdentityUser user, string stagedKey, CancellationToken cancellationToken)
		{
			if (_userStore is not IUserAuthenticatorKeyStore<IdentityUser> keyStore)
				throw new InvalidOperationException("The user store does not support authenticator keys.");

			await keyStore.SetAuthenticatorKeyAsync(user, stagedKey, cancellationToken);
			await _userManager.RemoveAuthenticationTokenAsync(user, StagedKeyLoginProvider, StagedKeyTokenName);
			await _mfaStateRepository.RecordTotpEnrollmentAsync(user.Id, DateTime.UtcNow, new TotpEnrollmentContext(false, (int)UserSessionClientApplication.Web), cancellationToken);
		}

		private async Task<EnableAuthenticatorViewModel> BuildAuthenticatorModelAsync(IdentityUser user, string key, bool isReplacement)
		{
			var uri = GenerateQrCodeUri(await _userManager.GetEmailAsync(user), key);
			return new EnableAuthenticatorViewModel
			{
				SharedKey = FormatKey(key),
				AuthenticatorUri = uri,
				QrCodeDataUrl = GenerateQrCodeDataUrl(uri),
				IsReplacement = isReplacement
			};
		}

		private async Task<bool> HasReplacementAuthorityAsync(IdentityUser user, CancellationToken cancellationToken)
		{
			var stepUp = await RequiresRecentTwoFactorAttribute.GetStepUpVerifiedAtUtcAsync(HttpContext, user, _mfaEvidenceService,
				_mfaPolicyService, null, MfaMethodScope.Account);
			if (stepUp.HasValue && stepUp.Value <= DateTime.UtcNow && DateTime.UtcNow - stepUp.Value <= TimeSpan.FromMinutes(ReplaceStepUpWindowMinutes))
				return true;

			// Lost-authenticator journey: a recent recovery-code sign-in may replace the factor, and nothing else sensitive.
			var sessionId = User.FindFirst(SessionClaimTypes.SessionId)?.Value;
			if (string.IsNullOrWhiteSpace(sessionId))
				return false;

			var session = (await _userSessionService.GetActiveForUserAsync(user.Id, cancellationToken))
				.FirstOrDefault(s => s.UserSessionId == sessionId);
			return session != null
				&& session.AuthenticationMethod == UserSessionAuthenticationMethod.Recovery
				&& DateTime.UtcNow - session.CreatedOn <= TimeSpan.FromMinutes(ReplaceRecoverySessionWindowMinutes);
		}

		private Task<bool> HasFreshFirstFactorAsync(IdentityUser user, int maxAgeMinutes)
			=> _mfaEvidenceService.HasFreshFirstFactorAsync(user.Id, MfaEvidenceSession.KeyFor(User, HttpContext),
				user.AuthenticationGeneration, TimeSpan.FromMinutes(Math.Max(1, maxAgeMinutes)), DateTime.UtcNow);

		/// <summary>
		/// Sends the user to confirm their password, then back to <paramref name="returnUrl"/> (default: this page). A submission
		/// is held and finished after confirming (<see cref="StepUpFormReplay"/>) rather than discarded.
		/// </summary>
		private async Task<IActionResult> RedirectToReauthenticateAsync(string returnUrl = null)
		{
			var (returnTo, submissionLost) = await StepUpFormReplay.HoldForVerificationAsync(HttpContext, _cacheProvider, _dataProtection,
				_userManager.GetUserId(User), returnUrl ?? $"{Request.Path}{Request.QueryString}");

			return RedirectToAction("Reauthenticate", "AccountSecurity",
				new { area = "User", returnUrl = returnTo, resubmit = submissionLost ? "1" : null });
		}

		/// <summary>Records server-side evidence for this session; false when it could not be recorded.</summary>
		private async Task<bool> RecordEvidenceAsync(IdentityUser user, MfaEvidenceKind kind, MfaEvidenceMethod method,
			MfaEvidencePurpose purpose, DateTime verifiedOnUtc, CancellationToken cancellationToken, string factorReference = null)
		{
			var sessionKey = MfaEvidenceSession.KeyFor(User, HttpContext);
			if (sessionKey == null)
				return false;

			try
			{
				await _mfaEvidenceService.RecordAsync(user.Id, sessionKey, UserSessionClientApplication.Web, kind, method, purpose,
					verifiedOnUtc, user.AuthenticationGeneration, factorReference: factorReference, cancellationToken: cancellationToken);
				return true;
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				Logging.LogException(ex, "Failed to record MFA evidence.");
				return false;
			}
		}

		private IActionResult RedirectToStepUp()
			=> RedirectToAction(nameof(Verify2FA), new { returnUrl = Url.Action(nameof(ReplaceAuthenticator)) });

		private static string FormatKey(string unformattedKey)
		{
			var result = new StringBuilder();
			int currentPosition = 0;
			while (currentPosition + 4 < unformattedKey.Length)
			{
				result.Append(unformattedKey.Substring(currentPosition, 4)).Append(' ');
				currentPosition += 4;
			}
			if (currentPosition < unformattedKey.Length)
				result.Append(unformattedKey.Substring(currentPosition));
			return result.ToString().ToLowerInvariant();
		}

		private string GenerateQrCodeUri(string email, string unformattedKey)
		{
			return $"otpauth://totp/{_urlEncoder.Encode(TwoFactorConfig.TotpIssuerName)}:{_urlEncoder.Encode(email)}?secret={unformattedKey}&issuer={_urlEncoder.Encode(TwoFactorConfig.TotpIssuerName)}&digits=6";
		}

		private static string GenerateQrCodeDataUrl(string uri)
		{
			using var qrGenerator = new QRCodeGenerator();
			using var qrCodeData = qrGenerator.CreateQrCode(uri, QRCodeGenerator.ECCLevel.Q);
			using var qrCode = new PngByteQRCode(qrCodeData);
			var bytes = qrCode.GetGraphic(5);
			return $"data:image/png;base64,{Convert.ToBase64String(bytes)}";
		}
	}
}




