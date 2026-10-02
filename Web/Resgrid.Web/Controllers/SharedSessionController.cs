using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Security;
using Resgrid.Model.Services;
using Resgrid.Web.Helpers;
using Resgrid.Web.Models.AccountViewModels;
using IdentityUser = Resgrid.Model.Identity.IdentityUser;

namespace Resgrid.Web.Controllers
{
	/// <summary>
	/// Core Web on a shared workstation (passkey plan sections 10.5 and 12.5): the station's own setting, the lock screen, Lock,
	/// End shift and Switch operator, and the same operator's unlock with a TOTP code, a Web passkey or Responder approval (the
	/// identity provider's unlock is <c>Account/SsoUnlockBegin</c>). A locked session reaches only these routes. Unlock resumes
	/// the same session: the first-factor time and the shift end do not change, and nothing from before the lock works again.
	/// </summary>
	// Authentication and session flows stay available during a department operation lock (ADP plan section 20.2): signing in
	// and out, locking and unlocking a shared session, and verifying a second factor touch no department data.
	[Resgrid.Web.Filters.AllowDuringDepartmentLock]
	public class SharedSessionController : Controller
	{
		private static readonly TimeSpan UnlockAttemptWindow = TimeSpan.FromMinutes(5);

		private static readonly string[] UnlockMethods =
			{ MfaMethodNames.Totp, MfaMethodNames.Passkey, MfaMethodNames.PasskeyApproval, MfaMethodNames.Federated };

		private readonly ISharedSessionService _sharedSessions;
		private readonly UserManager<IdentityUser> _userManager;
		private readonly SignInManager<IdentityUser> _signInManager;
		private readonly IMfaPolicyService _mfaPolicy;
		private readonly IPasskeyService _passkeys;
		private readonly IMfaApprovalService _approvals;
		private readonly ICacheProvider _cacheProvider;
		private readonly IDepartmentSsoService _departmentSso;
		private readonly ISsoBrokerService _ssoBroker;
		private readonly ISsoReturnTargetRegistry _ssoReturnTargets;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.TwoFactor.TwoFactor> _localizer;
		private readonly TimeProvider _time;

		public SharedSessionController(ISharedSessionService sharedSessions, UserManager<IdentityUser> userManager, SignInManager<IdentityUser> signInManager,
			IMfaPolicyService mfaPolicy, IPasskeyService passkeys, IMfaApprovalService approvals, ICacheProvider cacheProvider,
			IDepartmentSsoService departmentSso, ISsoBrokerService ssoBroker, ISsoReturnTargetRegistry ssoReturnTargets,
			IStringLocalizer<Resgrid.Localization.Areas.User.TwoFactor.TwoFactor> localizer, TimeProvider time)
		{
			_time = time;
			_sharedSessions = sharedSessions;
			_userManager = userManager;
			_signInManager = signInManager;
			_mfaPolicy = mfaPolicy;
			_passkeys = passkeys;
			_approvals = approvals;
			_cacheProvider = cacheProvider;
			_departmentSso = departmentSso;
			_ssoBroker = ssoBroker;
			_ssoReturnTargets = ssoReturnTargets;
			_localizer = localizer;
		}

		// ── This browser's setting ──────────────────────────────────────────────────────────────────────────────────────

		//
		// GET: /SharedSession/Workstation
		/// <summary>Whether this browser is a shared workstation. It can only make later sign-ins stricter.</summary>
		[HttpGet]
		[AllowAnonymous]
		public IActionResult Workstation()
		{
			var label = WebSharedSession.WorkstationLabel(Request);
			return View(new SharedWorkstationViewModel
			{
				Available = WebSharedSession.IsAvailable,
				Shared = label != null,
				Label = label,
				Status = TempData["SharedWorkstationStatus"] as string
			});
		}

		//
		// POST: /SharedSession/Workstation
		/// <summary>
		/// Sets this browser up as a shared workstation, or stops. It applies from the next sign-in; a session is shared from its
		/// sign-in and stays shared, so turning this off never relaxes a session already running.
		/// </summary>
		[HttpPost]
		[AllowAnonymous]
		[ValidateAntiForgeryToken]
		public IActionResult Workstation(SharedWorkstationViewModel model)
		{
			if (!WebSharedSession.IsAvailable)
				return RedirectToAction(nameof(Workstation));

			if (model?.Shared == true)
				WebSharedSession.SetWorkstation(Response, model.Label);
			else
				WebSharedSession.ClearWorkstation(Response);

			TempData["SharedWorkstationStatus"] = _localizer[model?.Shared == true ? "SharedWorkstationSaved" : "SharedWorkstationRemoved"].Value;
			return RedirectToAction(nameof(Workstation));
		}

		// ── Status, Lock, End shift ─────────────────────────────────────────────────────────────────────────────────────

		//
		// GET: /SharedSession/Status
		/// <summary>
		/// This session's shared state for the page's timers, in seconds from now so the browser's clock does not matter. Asking
		/// is not operator activity unless the page marks it as such after real input.
		/// </summary>
		[HttpGet]
		[AllowAnonymous]
		public async Task<IActionResult> Status(CancellationToken cancellationToken)
		{
			NoStore();
			var session = WebSharedSession.SessionOf(HttpContext);
			if (session == null)
				return StatusCode(401, new { error = "session_required" });

			var status = await _sharedSessions.GetStatusAsync(session, cancellationToken);
			if (status == null || !status.Shared)
				return Json(new { shared = false });

			// The service's clock decided the deadlines, so the same clock counts down to them.
			var now = _time.GetUtcNow().UtcDateTime;
			return Json(new
			{
				shared = true,
				locked = status.Locked,
				lockVersion = status.LockVersion,
				idleLockMinutes = status.IdleLockMinutes,
				idleLocksInSeconds = status.IdleLocksOnUtc is DateTime idle ? Math.Max(0, (int)(idle - now).TotalSeconds) : (int?)null,
				shiftEndsInSeconds = status.ShiftEndsOnUtc is DateTime shift ? Math.Max(0, (int)(shift - now).TotalSeconds) : (int?)null,
				lockedUrl = status.Locked ? WebSharedSession.LockedPath : null
			});
		}

		//
		// POST: /SharedSession/Lock
		/// <summary>
		/// Locks this shared session now. Everything from before it, including Protected Data Grants and pending verifications,
		/// stops working; the lock screen asks the same operator to verify again. Locking a locked session is fine.
		/// </summary>
		[HttpPost]
		[AllowAnonymous]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Lock(string returnUrl, CancellationToken cancellationToken)
		{
			var session = WebSharedSession.SessionOf(HttpContext);
			if (session == null)
				return RedirectToAction("LogOn", "Account");

			var locked = await _sharedSessions.LockAsync(session, RequestInfo(), cancellationToken);
			if (!locked.Succeeded)
				return RedirectToLocal(returnUrl);

			return Redirect(WebSharedSession.LockedUrl(SafeReturnUrl(returnUrl)));
		}

		//
		// POST: /SharedSession/EndShift
		/// <summary>
		/// End shift or Switch operator (plan section 12.5.3): ends this shared session, signs the browser out, and asks it to
		/// clear this site's caches and storage, so nothing of the outgoing operator is left for the next one. Available while
		/// locked. The workstation setting stays.
		/// </summary>
		[HttpPost]
		[AllowAnonymous]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> EndShift(bool switchOperator, CancellationToken cancellationToken)
		{
			var session = WebSharedSession.SessionOf(HttpContext);
			if (session is not { SharedMode: true })
				return session == null ? RedirectToAction("LogOn", "Account") : RedirectToLocal(null);

			var ended = await _sharedSessions.EndShiftAsync(session, switchOperator, RequestInfo(), cancellationToken);
			if (!ended.Succeeded)
			{
				TempData["SharedUnlockMessage"] = _localizer["SharedSessionUpdateFailed"].Value;
				return Redirect(session.IsLocked ? WebSharedSession.LockedPath : "/User/Home/Dashboard");
			}

			await _signInManager.SignOutAsync();
			await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
			try
			{
				HttpContext.Session?.Clear();
			}
			catch (InvalidOperationException)
			{
				// No server session on this request; nothing to clear.
			}

			Response.Headers["Clear-Site-Data"] = "\"cache\", \"storage\"";
			TempData["LoginMfaMessage"] = _localizer[switchOperator ? "SharedSwitchOperatorDone" : "SharedShiftEndedByOperator"].Value;
			return RedirectToAction("LogOn", "Account");
		}

		// ── The lock screen and unlock ──────────────────────────────────────────────────────────────────────────────────

		//
		// GET: /SharedSession/Locked
		/// <summary>The lock screen: the operator's name, and the ways that same operator can unlock it.</summary>
		[HttpGet]
		[AllowAnonymous]
		public async Task<IActionResult> Locked(string returnUrl, CancellationToken cancellationToken)
		{
			var session = WebSharedSession.SessionOf(HttpContext);
			if (session == null)
				return RedirectToAction("LogOn", "Account", new { returnUrl = SafeReturnUrl(returnUrl) });
			if (session is not { SharedMode: true, IsLocked: true })
				return RedirectToLocal(returnUrl);

			NoStore();
			return View(await LockedModelAsync(session, returnUrl, TempData["SharedUnlockMessage"] as string, cancellationToken));
		}

		//
		// POST: /SharedSession/Unlock
		/// <summary>Unlocks with a code from the operator's authenticator app. Each time step is accepted once, so a code seen on this screen cannot be replayed.</summary>
		[HttpPost]
		[AllowAnonymous]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Unlock(string code, long lockVersion, string returnUrl, CancellationToken cancellationToken)
		{
			var (session, user, refusal) = await OpenUnlockAsync(lockVersion, returnUrl, cancellationToken);
			if (refusal?.Restart != null)
			{
				if (refusal.Error == "SharedUnlockLockChanged")
					TempData["SharedUnlockMessage"] = _localizer[refusal.Error].Value;
				return LocalRedirect(refusal.Restart);
			}
			if (refusal != null)
				return await LockedAgainAsync(session, returnUrl, refusal.Error, cancellationToken);

			var denied = await RefuseAttemptAsync(session, user, MfaEvidenceMethod.Totp, cancellationToken);
			if (denied != null)
				return await LockedAgainAsync(session, returnUrl, denied, cancellationToken);
			if (string.IsNullOrWhiteSpace(code))
				return await LockedAgainAsync(session, returnUrl, "InvalidCodeLogin", cancellationToken);

			if (!await _userManager.VerifyTwoFactorTokenAsync(user, _userManager.Options.Tokens.AuthenticatorTokenProvider,
					code.Replace(" ", string.Empty).Replace("-", string.Empty)))
			{
				await _userManager.AccessFailedAsync(user);
				await _sharedSessions.RecordFailedUnlockAsync(session, MfaMethodNames.Totp, RequestInfo(user), cancellationToken);
				return await LockedAgainAsync(session, returnUrl, "InvalidCodeLogin", cancellationToken);
			}

			await _userManager.ResetAccessFailedCountAsync(user);
			var unlocked = await _sharedSessions.UnlockAsync(session, lockVersion, MfaEvidenceMethod.Totp, null, DateTime.UtcNow, RequestInfo(user),
				cancellationToken);
			if (!unlocked.Succeeded)
				return await LockedAgainAsync(session, returnUrl, UnlockRefusalKey(unlocked.Outcome), cancellationToken);

			return RedirectToLocal(returnUrl);
		}

		/// <summary>Assertion options for the operator's Web passkeys, bound to this session's current lock version.</summary>
		[HttpPost]
		[AllowAnonymous]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> UnlockPasskeyOptions(long lockVersion, string returnUrl, CancellationToken cancellationToken)
		{
			var (session, user, refusal) = await OpenUnlockAsync(lockVersion, returnUrl, cancellationToken);
			if (refusal != null)
				return RefusalJson(refusal);
			if (!await MayUseAsync(session, user, MfaEvidenceMethod.Passkey, cancellationToken))
				return Json(new { success = false, error = "mfa_method_not_allowed" });

			var start = await _passkeys.BeginAssertionAsync(UnlockCaller(session, user), AuthenticationChallengePurpose.SharedDeviceUnlock, cancellationToken);
			return start.Succeeded
				? Json(new { success = true, requestId = start.RequestId, options = start.OptionsJson })
				: Json(new { success = false, error = PasskeyOutcomes.ErrorCode(start.Outcome) });
		}

		/// <summary>Unlocks with a Web passkey; a signature that does not verify counts as a failed unlock.</summary>
		[HttpPost]
		[AllowAnonymous]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> UnlockPasskey(string requestId, string credential, long lockVersion, string returnUrl, CancellationToken cancellationToken)
		{
			var (session, user, refusal) = await OpenUnlockAsync(lockVersion, returnUrl, cancellationToken);
			if (refusal != null)
				return RefusalJson(refusal);
			var denied = await RefuseAttemptAsync(session, user, MfaEvidenceMethod.Passkey, cancellationToken);
			if (denied != null)
				return Json(new { success = false, error = denied == "SharedUnlockTooManyAttempts" ? "too_many_attempts" : "mfa_method_not_allowed" });

			var assertion = await _passkeys.CompleteAssertionAsync(UnlockCaller(session, user), AuthenticationChallengePurpose.SharedDeviceUnlock, requestId,
				credential, cancellationToken);
			if (!assertion.Succeeded)
			{
				if (assertion.Outcome is PasskeyOutcome.VerificationFailed or PasskeyOutcome.NotRegisteredForClient)
					await _sharedSessions.RecordFailedUnlockAsync(session, MfaMethodNames.Passkey, RequestInfo(user), cancellationToken);
				return Json(new { success = false, error = PasskeyOutcomes.ErrorCode(assertion.Outcome) });
			}

			return await UnlockedJsonAsync(session, user, lockVersion, MfaEvidenceMethod.Passkey, UserPasskey.FactorReferenceFor(assertion.Passkey.UserPasskeyId),
				assertion.VerifiedOnUtc, returnUrl, cancellationToken);
		}

		/// <summary>Asks the operator's own Responder to approve this unlock; the number is shown on this screen only (plan section 7.9).</summary>
		[HttpPost]
		[AllowAnonymous]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> UnlockApproval(long lockVersion, string returnUrl, CancellationToken cancellationToken)
		{
			var (session, user, refusal) = await OpenUnlockAsync(lockVersion, returnUrl, cancellationToken);
			if (refusal != null)
				return RefusalJson(refusal);
			if (!await MayUseAsync(session, user, MfaEvidenceMethod.PasskeyApproval, cancellationToken))
				return Json(new { success = false, error = "approval_unavailable" });

			var start = await _approvals.RequestAsync(new MfaApprovalRequester
			{
				UserId = user.Id,
				Kind = MfaApprovalRequesterKind.Session,
				RequesterId = session.UserSessionId,
				ClientApplication = UserSessionClientApplication.Web,
				AuthenticationGeneration = session.AuthenticationGeneration,
				DepartmentId = session.DepartmentId,
				Purpose = MfaApprovalPurpose.Unlock,
				SharedMode = true,
				LockVersion = session.LockVersion,
				IpAddress = IpAddressHelper.GetRequestIP(Request, true),
				UserName = user.UserName,
				AuditSystem = SystemAuditSystems.Website
			}, cancellationToken);
			return start.Succeeded
				? Json(new { success = true, approvalRequestId = start.ApprovalRequestId, matchNumber = start.MatchNumber, expiresIn = start.ExpiresInSeconds })
				: Json(new { success = false, error = MfaApprovalOutcomes.ErrorCode(start.Outcome) ?? "approval_unavailable" });
		}

		/// <summary>The state of this session's unlock approval. A lock since the request voids it, whatever Responder does with it.</summary>
		[HttpPost]
		[AllowAnonymous]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> UnlockApprovalStatus(string approvalRequestId, CancellationToken cancellationToken)
		{
			var session = WebSharedSession.SessionOf(HttpContext);
			if (session is not { SharedMode: true, IsLocked: true })
				return Json(new { success = false, error = "shared_session_not_locked" });

			var found = await _approvals.GetForRequesterAsync(approvalRequestId, MfaApprovalRequesterKind.Session, session.UserSessionId, cancellationToken);
			if (!found.Succeeded || found.Request.RequestPurpose != MfaApprovalPurpose.Unlock)
				return Json(new { success = false, error = MfaApprovalOutcomes.ErrorCode(found.Succeeded ? MfaApprovalOutcome.NotFound : found.Outcome) ?? "approval_unavailable" });

			return Json(new
			{
				success = true,
				state = MfaApprovalOutcomes.StateName(found.Request.LockVersion == session.LockVersion
					? found.Request.EffectiveState(DateTime.UtcNow)
					: MfaApprovalRequestState.Canceled)
			});
		}

		/// <summary>Uses this session's approved unlock request once, at the lock version it was made for.</summary>
		[HttpPost]
		[AllowAnonymous]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> UnlockApprovalComplete(string approvalRequestId, long lockVersion, string returnUrl, CancellationToken cancellationToken)
		{
			var (session, user, refusal) = await OpenUnlockAsync(lockVersion, returnUrl, cancellationToken);
			if (refusal != null)
				return RefusalJson(refusal);
			var denied = await RefuseAttemptAsync(session, user, MfaEvidenceMethod.PasskeyApproval, cancellationToken);
			if (denied != null)
				return Json(new { success = false, error = denied == "SharedUnlockTooManyAttempts" ? "too_many_attempts" : "approval_unavailable" });

			// Only an unlock request this session made at this lock version unlocks it.
			var found = await _approvals.GetForRequesterAsync(approvalRequestId, MfaApprovalRequesterKind.Session, session.UserSessionId, cancellationToken);
			if (found.Succeeded && (found.Request.RequestPurpose != MfaApprovalPurpose.Unlock || found.Request.LockVersion != session.LockVersion))
				return Json(new { success = false, error = "approval_expired" });

			var consumed = await _approvals.ConsumeAsync(approvalRequestId, MfaApprovalRequesterKind.Session, session.UserSessionId, user.Id,
				session.AuthenticationGeneration, cancellationToken);
			if (!consumed.Succeeded)
			{
				if (consumed.Outcome == MfaApprovalOutcome.Denied)
					await _sharedSessions.RecordFailedUnlockAsync(session, MfaMethodNames.PasskeyApproval, RequestInfo(user), cancellationToken);
				return Json(new { success = false, error = MfaApprovalOutcomes.ErrorCode(consumed.Outcome) ?? "approval_unavailable" });
			}

			var approval = consumed.Request;
			return await UnlockedJsonAsync(session, user, lockVersion, MfaEvidenceMethod.PasskeyApproval,
				MfaApprovalRequest.FactorReferenceFor(approval.ApproverPasskeyId, approval.ApproverSessionId), approval.DecidedOnUtc ?? DateTime.UtcNow, returnUrl,
				cancellationToken);
		}

		// ── Helpers ─────────────────────────────────────────────────────────────────────────────────────────────────────

		private sealed record Refusal(string Error, string Restart);

		/// <summary>
		/// The locked session this request is about, its operator, and whether they may try to unlock it now: shared, locked, at the
		/// lock version the screen showed, and not needing a normal sign-in instead.
		/// </summary>
		private async Task<(UserSession Session, IdentityUser User, Refusal Refusal)> OpenUnlockAsync(long lockVersion, string returnUrl,
			CancellationToken cancellationToken)
		{
			var session = WebSharedSession.SessionOf(HttpContext);
			if (session == null)
				return (null, null, new Refusal("session_revoked", Url.Action("LogOn", "Account")));
			if (session is not { SharedMode: true, IsLocked: true })
				return (null, null, new Refusal("shared_session_not_locked", SafeReturnUrl(returnUrl) ?? "/User/Home/Dashboard"));

			var outcome = await _sharedSessions.CanUnlockAsync(session, cancellationToken);
			if (outcome != SharedSessionOutcome.Succeeded)
				return (session, null, new Refusal(UnlockRefusalKey(outcome), null));
			if (lockVersion != session.LockVersion)
				return (session, null, new Refusal("SharedUnlockLockChanged", WebSharedSession.LockedUrl(SafeReturnUrl(returnUrl))));

			var user = await _userManager.FindByIdAsync(session.UserId);
			return user == null
				? (session, null, new Refusal("session_revoked", Url.Action("LogOn", "Account")))
				: (session, user, null);
		}

		/// <summary>
		/// Refuses an unlock attempt before any factor is checked: too many attempts on this session (shared with the API's limit),
		/// a locked-out account, no authenticator app behind a Resgrid factor, or a method the department does not accept.
		/// </summary>
		private async Task<string> RefuseAttemptAsync(UserSession session, IdentityUser user, MfaEvidenceMethod method, CancellationToken cancellationToken)
		{
			// Per session, on top of the account lockout; it fails open on cache faults, the lockout does not.
			if (await _cacheProvider.IncrementAsync($"SharedUnlockAttempts_{session.UserSessionId}", UnlockAttemptWindow) >
				Math.Max(1, PasskeyConfig.SharedUnlockMaxAttempts))
				return "SharedUnlockTooManyAttempts";
			if (await _userManager.IsLockedOutAsync(user))
				return "SharedUnlockTooManyAttempts";
			if (!await _userManager.GetTwoFactorEnabledAsync(user))
				return "SharedUnlockNeedsAuthenticator";
			if (!await _mfaPolicy.IsMethodAcceptedAsync(session.DepartmentId, MfaMethodScope.Login, method, cancellationToken))
				return "LoginMfaMethodUnavailable";
			return null;
		}

		private async Task<bool> MayUseAsync(UserSession session, IdentityUser user, MfaEvidenceMethod method, CancellationToken cancellationToken) =>
			await _userManager.GetTwoFactorEnabledAsync(user) && !await _userManager.IsLockedOutAsync(user) &&
			await _mfaPolicy.IsMethodAcceptedAsync(session.DepartmentId, MfaMethodScope.Login, method, cancellationToken);

		private async Task<IActionResult> UnlockedJsonAsync(UserSession session, IdentityUser user, long lockVersion, MfaEvidenceMethod method,
			string factorReference, DateTime verifiedOnUtc, string returnUrl, CancellationToken cancellationToken)
		{
			var unlocked = await _sharedSessions.UnlockAsync(session, lockVersion, method, factorReference, verifiedOnUtc, RequestInfo(user), cancellationToken);
			if (!unlocked.Succeeded)
				return Json(new
				{
					success = false,
					error = SharedSessionOutcomes.ErrorCode(unlocked.Outcome),
					restart = unlocked.Outcome == SharedSessionOutcome.LockChanged ? WebSharedSession.LockedUrl(SafeReturnUrl(returnUrl)) : null
				});

			return Json(new { success = true, redirect = SafeReturnUrl(returnUrl) ?? "/User/Home/Dashboard" });
		}

		private IActionResult RefusalJson(Refusal refusal) =>
			Json(new { success = false, error = refusal.Error, restart = refusal.Restart });

		/// <summary>The lock screen again, with why the last attempt did not unlock it.</summary>
		private async Task<IActionResult> LockedAgainAsync(UserSession session, string returnUrl, string errorKey, CancellationToken cancellationToken)
		{
			NoStore();
			return View(nameof(Locked), await LockedModelAsync(session, returnUrl, _localizer[errorKey].Value, cancellationToken));
		}

		/// <summary>
		/// The lock screen for this session: the operator, and the unlock methods they have that the department accepts for sign-in
		/// (as the API's <c>unlock-options</c>). None means quick unlock is unavailable: end the shift and sign in normally.
		/// </summary>
		private async Task<SharedSessionLockedViewModel> LockedModelAsync(UserSession session, string returnUrl, string error, CancellationToken cancellationToken)
		{
			var model = new SharedSessionLockedViewModel
			{
				InstallationLabel = session.DeviceName,
				LockedWhenIdle = session.LockReason == (int)SharedSessionLockReason.Idle,
				LockVersion = session.LockVersion,
				ReturnUrl = SafeReturnUrl(returnUrl),
				Error = error
			};

			var user = await _userManager.FindByIdAsync(session.UserId);
			model.Operator = user?.UserName;
			var outcome = await _sharedSessions.CanUnlockAsync(session, cancellationToken);
			if (user == null || outcome != SharedSessionOutcome.Succeeded)
			{
				model.Unavailable = _localizer[UnlockRefusalKey(user == null ? SharedSessionOutcome.SessionEnded : outcome)].Value;
				return model;
			}

			// Passkeys and approval needed the authenticator app to register, so they count only with it. The identity provider's
			// MFA proves the provider account instead, so an SSO operator without one can still use it.
			var totpEnrolled = await _userManager.GetTwoFactorEnabledAsync(user);
			var passkeyEnrolled = totpEnrolled && await _passkeys.HasActiveForClientAsync(user.Id, UserSessionClientApplication.Web, cancellationToken);
			var approvalEnrolled = totpEnrolled && await _approvals.IsAvailableAsync(user.Id, UserSessionClientApplication.Web, cancellationToken);
			var federatedEnrolled = session.DepartmentId is int departmentId && WebSsoRoundTrip.IsAvailable(_ssoBroker, _ssoReturnTargets) &&
				await _departmentSso.IsFederatedMfaAvailableAsync(departmentId, user.Id, cancellationToken);
			var choice = await _mfaPolicy.GetMethodChoiceAsync(user.Id, totpEnrolled, session.DepartmentId, MfaMethodScope.Login, passkeyEnrolled,
				federatedEnrolled, approvalEnrolled, cancellationToken);
			model.Methods = choice.AllowedMethods.Where(choice.EnrolledMethods.Contains).Where(UnlockMethods.Contains).ToList();
			if (model.Methods.Count == 0)
				model.Unavailable = _localizer["SharedUnlockNeedsAuthenticator"].Value;
			return model;
		}

		private static string UnlockRefusalKey(SharedSessionOutcome outcome) => outcome switch
		{
			SharedSessionOutcome.SsoReauthenticationRequired => "SharedUnlockNeedsSso",
			SharedSessionOutcome.LockChanged => "SharedUnlockLockChanged",
			SharedSessionOutcome.NotLocked => "SharedUnlockLockChanged",
			SharedSessionOutcome.SessionEnded => "SharedUnlockSessionEnded",
			_ => "SharedSessionUpdateFailed"
		};

		private PasskeyCaller UnlockCaller(UserSession session, IdentityUser user) => new()
		{
			UserId = user.Id,
			UserName = user.UserName,
			SessionId = session.UserSessionId,
			ClientApplication = UserSessionClientApplication.Web,
			AuthenticationGeneration = session.AuthenticationGeneration,
			SessionLockVersion = session.LockVersion,
			DepartmentId = session.DepartmentId,
			SharedMode = true,
			AuditSystem = SystemAuditSystems.Website,
			IpAddress = IpAddressHelper.GetRequestIP(Request, true)
		};

		private SharedSessionRequestInfo RequestInfo(IdentityUser user = null) => new()
		{
			UserName = user?.UserName ?? User?.Identity?.Name,
			IpAddress = IpAddressHelper.GetRequestIP(Request, true),
			CorrelationId = HttpContext.TraceIdentifier,
			AuditSystem = SystemAuditSystems.Website
		};

		private void NoStore()
		{
			Response.Headers["Cache-Control"] = "no-store, no-cache, max-age=0";
			Response.Headers["Pragma"] = "no-cache";
			Response.Headers["X-Frame-Options"] = "DENY";
			Response.Headers["Referrer-Policy"] = "no-referrer";
		}

		private string SafeReturnUrl(string returnUrl) =>
			!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl) && !returnUrl.StartsWith("/SharedSession/", StringComparison.OrdinalIgnoreCase)
				? returnUrl
				: null;

		private IActionResult RedirectToLocal(string returnUrl) => LocalRedirect(SafeReturnUrl(returnUrl) ?? "/User/Home/Dashboard");
	}
}
