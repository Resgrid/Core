using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Security;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.MfaApproval;
using Resgrid.Web.Services.Models.v4.Passkeys;
using Resgrid.Web.Services.Models.v4.Sessions;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// User authentication session inventory and revocation, and the shared vehicle and workstation session lifecycle (passkey
	/// plan sections 11 and 12.5): status, lock, unlock and end shift. Those are the only endpoints a locked shared session can
	/// reach; session validation refuses it everywhere else.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/sessions")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	// Authentication and session flows stay available during a department operation lock (ADP plan section 20.2): a locked
	// shared session must still unlock or end its shift, and Responder must still approve or deny.
	[Resgrid.Web.Services.Filters.AllowDuringDepartmentLock]
	public class SessionsController : V4AuthenticatedApiControllerbase
	{
		private static readonly TimeSpan UnlockAttemptWindow = TimeSpan.FromMinutes(5);

		private readonly IUserSessionService _userSessionService;
		private readonly ISystemAuditsService _systemAuditsService;
		private readonly ISharedSessionService _sharedSessions;
		private readonly UserManager<Model.Identity.IdentityUser> _userManager;
		private readonly IMfaPolicyService _mfaPolicy;
		private readonly IPasskeyService _passkeys;
		private readonly IMfaApprovalService _approvals;
		private readonly ICacheProvider _cacheProvider;
		private readonly ISsoBrokerService _ssoBroker;
		private readonly IDepartmentSsoService _departmentSso;
		private readonly IDepartmentsService _departments;

		public SessionsController(IUserSessionService userSessionService, ISystemAuditsService systemAuditsService, ISharedSessionService sharedSessions,
			UserManager<Model.Identity.IdentityUser> userManager, IMfaPolicyService mfaPolicy, IPasskeyService passkeys, IMfaApprovalService approvals,
			ICacheProvider cacheProvider, ISsoBrokerService ssoBroker, IDepartmentSsoService departmentSso, IDepartmentsService departments)
		{
			_ssoBroker = ssoBroker;
			_departmentSso = departmentSso;
			_departments = departments;
			_userSessionService = userSessionService;
			_systemAuditsService = systemAuditsService;
			_sharedSessions = sharedSessions;
			_userManager = userManager;
			_mfaPolicy = mfaPolicy;
			_passkeys = passkeys;
			_approvals = approvals;
			_cacheProvider = cacheProvider;
		}

		[HttpGet]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<IReadOnlyList<UserSessionSummary>>> Get(CancellationToken cancellationToken)
		{
			var currentSessionId = User.FindFirstValue(SessionClaimTypes.SessionId);
			var sessions = await _userSessionService.GetActiveForUserAsync(UserId, cancellationToken);
			foreach (var session in sessions)
				session.IsCurrent = string.Equals(session.UserSessionId, currentSessionId, StringComparison.Ordinal);
			return Ok(sessions);
		}

		[HttpDelete("{sessionId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<IActionResult> Revoke(string sessionId, CancellationToken cancellationToken)
		{
			var result = await _userSessionService.RevokeSessionAsync(UserId, UserId, sessionId,
				UserSessionRevocationReason.UserRevoked, cancellationToken);
			await AuditAsync(SystemAuditTypes.SessionRevoked, sessionId, result.RevokedSessionCount > 0, cancellationToken);
			return Ok(new { revoked = result.RevokedSessionCount });
		}

		[HttpPost("revoke-others")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status409Conflict)]
		public async Task<IActionResult> RevokeOthers(CancellationToken cancellationToken)
		{
			var currentSessionId = User.FindFirstValue(SessionClaimTypes.SessionId);
			if (string.IsNullOrWhiteSpace(currentSessionId))
				return Conflict(new { error = "legacy_session", message = "Refresh this session before revoking all others." });

			var result = await _userSessionService.RevokeOtherSessionsAsync(UserId, currentSessionId,
				UserSessionRevocationReason.OtherSessionsRevoked, cancellationToken);
			await AuditAsync(SystemAuditTypes.OtherSessionsRevoked, null, true, cancellationToken);
			return Ok(new { revoked = result.RevokedSessionCount });
		}

		[HttpPost("revoke-all")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<IActionResult> RevokeAll(CancellationToken cancellationToken)
		{
			var result = await _userSessionService.RevokeAllAsync(UserId, UserId,
				UserSessionRevocationReason.AccountCompromised, DateTime.UtcNow, cancellationToken);
			await AuditAsync(SystemAuditTypes.AllSessionsRevoked, null, true, cancellationToken);
			return Ok(new { revoked = result.RevokedSessionCount, reauthenticationRequired = true });
		}

		// ── Shared vehicle and workstation sessions (plan section 12.5.3) ──────────────

		/// <summary>This session's operator, lock state and deadlines. Available while locked.</summary>
		[HttpGet("current")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<CurrentSessionResult>> Current(CancellationToken cancellationToken)
		{
			var session = OwnSession();
			if (session == null)
				return SessionRequired();

			return Wrap(new CurrentSessionResult { Data = ToData(await _sharedSessions.GetStatusAsync(session, cancellationToken), session) });
		}

		/// <summary>
		/// Locks this shared session now (Lock, an OS lock, or the app going to the background). Everything issued before it,
		/// including Protected Data Grants and pending verifications, stops working. Locking a locked session is fine.
		/// </summary>
		[HttpPost("lock")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status409Conflict)]
		public async Task<ActionResult<SessionLockResult>> Lock(CancellationToken cancellationToken)
		{
			var session = OwnSession();
			if (session == null)
				return SessionRequired();

			var locked = await _sharedSessions.LockAsync(session, RequestInfo(), cancellationToken);
			if (!locked.Succeeded)
				return Refuse(locked.Outcome);

			return Wrap(new SessionLockResult { Data = new SessionLockResultData { Locked = true, LockVersion = locked.LockVersion } });
		}

		/// <summary>
		/// End shift or Switch operator: ends this shared session. The client then clears the operator's tokens, caches and
		/// connections and signs the next operator in normally. Available while locked.
		/// </summary>
		[HttpPost("end-shift")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status409Conflict)]
		public async Task<ActionResult<EndShiftResult>> EndShift([FromBody] EndShiftInput input, CancellationToken cancellationToken)
		{
			var session = OwnSession();
			if (session == null)
				return SessionRequired();

			var ended = await _sharedSessions.EndShiftAsync(session, input?.SwitchOperator == true, RequestInfo(), cancellationToken);
			if (!ended.Succeeded)
				return Refuse(ended.Outcome);

			return Wrap(new EndShiftResult { Data = new EndShiftResultData { Ended = true } });
		}

		/// <summary>
		/// How the locked session's own operator can unlock it, bound to its current lock version. Empty methods mean quick
		/// unlock is unavailable (no factor, or the department's rules exclude them): end the shift and sign in normally.
		/// </summary>
		[HttpPost("unlock-options")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status409Conflict)]
		public async Task<ActionResult<UnlockOptionsResult>> UnlockOptions([FromBody] UnlockOptionsInput input, CancellationToken cancellationToken)
		{
			var (session, user, refusal) = await OpenUnlockAsync(input?.LockVersion, cancellationToken);
			if (refusal != null)
				return refusal;

			PasskeyCeremonyResultData passkey = null;
			var client = (UserSessionClientApplication)session.ClientApplication;

			// Passkeys and approval needed the authenticator app to register, so they count only with it. The identity
			// provider's MFA proves the provider account instead, so an SSO operator without one can still use it.
			var totpEnrolled = await _userManager.GetTwoFactorEnabledAsync(user);
			var passkeyEnrolled = totpEnrolled && await _passkeys.HasActiveForClientAsync(user.Id, client, cancellationToken);
			var approvalEnrolled = totpEnrolled && await _approvals.IsAvailableAsync(user.Id, client, cancellationToken);
			var federatedEnrolled = session.DepartmentId is int departmentId && await _departmentSso.IsFederatedMfaAvailableAsync(departmentId, user.Id, cancellationToken);
			var choice = await _mfaPolicy.GetMethodChoiceAsync(user.Id, totpEnrolled, session.DepartmentId, MfaMethodScope.Login, passkeyEnrolled,
				federatedEnrolled, approvalEnrolled, cancellationToken);
			var methods = choice.AllowedMethods.Where(choice.EnrolledMethods.Contains).Where(UnlockMethods.Contains).ToList();
			// The client shows this first but never starts the passkey prompt on its own on a shared installation.
			var preferred = methods.Contains(choice.Preferred) ? choice.Preferred : methods.FirstOrDefault();

			if (methods.Contains(MfaMethodNames.Passkey))
			{
				// Advisory, like the rest of this response: when the ceremony cannot start, the other methods still work.
				var start = await _passkeys.BeginAssertionAsync(UnlockCaller(session, user), AuthenticationChallengePurpose.SharedDeviceUnlock,
					cancellationToken);
				if (start.Succeeded)
					passkey = new PasskeyCeremonyResultData { RequestId = start.RequestId, Options = ApiPasskeys.Options(start.OptionsJson) };
			}

			return Wrap(new UnlockOptionsResult
			{
				Data = new UnlockOptionsResultData
				{
					Operator = user.UserName,
					LockVersion = session.LockVersion,
					Methods = methods,
					Preferred = preferred,
					Passkey = passkey
				}
			});
		}

		/// <summary>
		/// Asks the operator's own Responder to approve this unlock (plan section 7.9). The request is bound to this lock
		/// version; show the number on this screen only, then poll <c>unlock-approval/{id}</c>.
		/// </summary>
		[HttpPost("unlock-approval")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status409Conflict)]
		public async Task<ActionResult<MfaApprovalRequestResult>> RequestUnlockApproval([FromBody] UnlockApprovalInput input, CancellationToken cancellationToken)
		{
			var (session, user, refusal) = await OpenUnlockAsync(input?.LockVersion, cancellationToken);
			if (refusal != null)
				return refusal;
			if (!await _userManager.GetTwoFactorEnabledAsync(user))
				return RefuseApproval(MfaApprovalOutcome.Unavailable);

			var start = await _approvals.RequestAsync(new MfaApprovalRequester
			{
				UserId = user.Id,
				Kind = MfaApprovalRequesterKind.Session,
				RequesterId = session.UserSessionId,
				ClientApplication = (UserSessionClientApplication)session.ClientApplication,
				AuthenticationGeneration = session.AuthenticationGeneration,
				DepartmentId = session.DepartmentId,
				Purpose = MfaApprovalPurpose.Unlock,
				SharedMode = true,
				LockVersion = session.LockVersion,
				IpAddress = IpAddressHelper.GetRequestIP(Request, true),
				UserName = user.UserName,
				AuditSystem = SystemAuditSystems.Api
			}, cancellationToken);
			if (!start.Succeeded)
				return RefuseApproval(start.Outcome);

			return Wrap(new MfaApprovalRequestResult
			{
				Data = new MfaApprovalRequestResultData { ApprovalRequestId = start.ApprovalRequestId, MatchNumber = start.MatchNumber, ExpiresIn = start.ExpiresInSeconds }
			});
		}

		/// <summary>
		/// Begins an unlock through the department's identity provider, which must perform MFA the department's tested mapping
		/// accepts (plan section 7.8). The provider is asked to let the operator choose the account. The transaction is bound
		/// to this session and counts only for the lock it began in; finish with <c>complete-unlock</c> method <c>federated</c>.
		/// </summary>
		[HttpPost("unlock-sso")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		public async Task<ActionResult<Models.v4.Sso.SsoBeginResult>> BeginUnlockSso([FromBody] UnlockSsoInput input, CancellationToken cancellationToken)
		{
			var (session, user, refusal) = await OpenUnlockAsync(input?.LockVersion, cancellationToken);
			if (refusal != null)
				return refusal;
			if (session.DepartmentId is not int departmentId ||
				!await _mfaPolicy.IsMethodAcceptedAsync(departmentId, MfaMethodScope.Login, MfaEvidenceMethod.Federated, cancellationToken) ||
				!await _departmentSso.IsFederatedMfaAvailableAsync(departmentId, user.Id, cancellationToken))
				return Problem(type: "mfa_method_not_allowed", title: "That verification method is not available.", statusCode: StatusCodes.Status400BadRequest);

			var department = await _departments.GetDepartmentByIdAsync(departmentId);
			var begun = await _ssoBroker.BeginAsync(new SsoBeginRequest
			{
				DepartmentId = departmentId,
				DepartmentCode = department?.Code,
				Purpose = SsoTransactionPurpose.StepUp,
				ClientApplication = (UserSessionClientApplication)session.ClientApplication,
				Platform = input.Platform,
				ReturnTarget = input.ReturnTarget,
				ClientState = input.State,
				CodeChallenge = input.CodeChallenge,
				CodeChallengeMethod = input.CodeChallengeMethod,
				SessionId = session.UserSessionId,
				UserId = user.Id,
				AuthenticationGeneration = session.AuthenticationGeneration,
				Operation = SsoLoginTransaction.SharedUnlockOperation,
				SharedInstallation = true
			}, cancellationToken);
			if (!begun.Succeeded)
				return Problem(type: SsoBrokerOutcomes.ErrorCode(begun.Outcome) ?? "sso_failed", title: "The sign-in with your identity provider could not start.",
					statusCode: begun.Outcome == SsoBrokerOutcome.ServiceUnavailable ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status400BadRequest);

			return Wrap(new Models.v4.Sso.SsoBeginResult
			{
				Data = new Models.v4.Sso.SsoBeginResultData { AuthorizeUrl = begun.AuthorizeUrl, SsoTransactionId = begun.TransactionId, ExpiresIn = begun.ExpiresInSeconds }
			});
		}

		/// <summary>The unlock approval's state, for this session only. Poll every 2 seconds.</summary>
		[HttpGet("unlock-approval/{approvalRequestId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<MfaApprovalStatusResult>> UnlockApprovalStatus(string approvalRequestId, CancellationToken cancellationToken)
		{
			var session = OwnSession();
			if (session == null)
				return SessionRequired();

			var found = await _approvals.GetForRequesterAsync(approvalRequestId, MfaApprovalRequesterKind.Session, session.UserSessionId, cancellationToken);
			if (!found.Succeeded || found.Request.RequestPurpose != MfaApprovalPurpose.Unlock)
				return RefuseApproval(found.Succeeded ? MfaApprovalOutcome.NotFound : found.Outcome);

			return Wrap(new MfaApprovalStatusResult
			{
				Data = new MfaApprovalStatusResultData
				{
					// A lock since the request voids it, whatever Responder does with it.
					State = MfaApprovalOutcomes.StateName(found.Request.LockVersion == session.LockVersion
						? found.Request.EffectiveState(DateTime.UtcNow)
						: MfaApprovalRequestState.Canceled),
					ExpiresAt = found.Request.ExpiresOnUtc.ToString("O")
				}
			});
		}

		/// <summary>Cancels this session's unlock approval request.</summary>
		[HttpDelete("unlock-approval/{approvalRequestId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<MfaApprovalStatusResult>> CancelUnlockApproval(string approvalRequestId, CancellationToken cancellationToken)
		{
			var session = OwnSession();
			if (session == null)
				return SessionRequired();

			var outcome = await _approvals.CancelAsync(approvalRequestId, MfaApprovalRequesterKind.Session, session.UserSessionId, cancellationToken);
			if (outcome != MfaApprovalOutcome.Succeeded)
				return RefuseApproval(outcome);

			return Wrap(new MfaApprovalStatusResult { Data = new MfaApprovalStatusResultData { State = MfaApprovalOutcomes.StateName(MfaApprovalRequestState.Canceled) } });
		}

		/// <summary>
		/// Unlocks this locked shared session for its own operator with a TOTP code, a passkey for this app, or an approved
		/// Responder request, all at the lock version from <c>unlock-options</c>. The same session resumes: the first-factor
		/// time and the shift end do not change, and nothing from before the lock works again.
		/// </summary>
		[HttpPost("complete-unlock")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[ProducesResponseType(StatusCodes.Status409Conflict)]
		[ProducesResponseType(StatusCodes.Status429TooManyRequests)]
		public async Task<ActionResult<CompleteUnlockResult>> CompleteUnlock([FromBody] CompleteUnlockInput input, CancellationToken cancellationToken)
		{
			var method = input?.Method ?? MfaMethodNames.Totp;
			if (input == null || !UnlockMethods.Contains(method))
				return Problem(type: "mfa_method_not_allowed", title: "That verification method is not available.", statusCode: StatusCodes.Status400BadRequest);
			if (method == MfaMethodNames.Totp && string.IsNullOrWhiteSpace(input.Code))
				return Problem(type: "invalid_totp", title: "A verification code is required.", statusCode: StatusCodes.Status400BadRequest);

			var (session, user, refusal) = await OpenUnlockAsync(input.LockVersion, cancellationToken);
			if (refusal != null)
				return refusal;

			// Per session, on top of the account lockout; it fails open on cache faults, the lockout does not.
			if (await _cacheProvider.IncrementAsync($"SharedUnlockAttempts_{session.UserSessionId}", UnlockAttemptWindow) >
				Math.Max(1, PasskeyConfig.SharedUnlockMaxAttempts))
				return Problem(type: "too_many_attempts", title: "Too many unlock attempts. Wait a few minutes or end the shift and sign in again.",
					statusCode: StatusCodes.Status429TooManyRequests);

			// The identity provider's MFA proves the provider account, so it needs no Resgrid factor; everything else does.
			if (method != MfaMethodNames.Federated && !await _userManager.GetTwoFactorEnabledAsync(user))
				return Problem(type: "mfa_enrollment_required", title: "Quick unlock needs an authenticator app. End the shift and sign in again.",
					statusCode: StatusCodes.Status409Conflict);
			if (await _userManager.IsLockedOutAsync(user))
				return Problem(type: "too_many_attempts", title: "Too many failed attempts. Wait a few minutes and try again.",
					statusCode: StatusCodes.Status429TooManyRequests);

			DateTime verifiedAt;
			MfaEvidenceMethod evidenceMethod;
			string factorReference = null;
			if (method == MfaMethodNames.Federated)
			{
				var federated = await VerifyFederatedUnlockAsync(session, user, input, cancellationToken);
				if (federated.Problem != null)
					return federated.Problem;

				verifiedAt = federated.VerifiedAt;
				evidenceMethod = MfaEvidenceMethod.Federated;
				factorReference = federated.FactorReference;
			}
			else if (method == MfaMethodNames.Passkey)
			{
				if (!await _mfaPolicy.IsMethodAcceptedAsync(session.DepartmentId, MfaMethodScope.Login, MfaEvidenceMethod.Passkey, cancellationToken))
					return Problem(type: "mfa_method_not_allowed", title: "That verification method is not available.", statusCode: StatusCodes.Status400BadRequest);

				var assertion = await _passkeys.CompleteAssertionAsync(UnlockCaller(session, user), AuthenticationChallengePurpose.SharedDeviceUnlock,
					input.RequestId, ApiPasskeys.CredentialJson(input.Credential), cancellationToken);
				if (!assertion.Succeeded)
				{
					if (assertion.Outcome is PasskeyOutcome.VerificationFailed or PasskeyOutcome.NotRegisteredForClient)
						await _sharedSessions.RecordFailedUnlockAsync(session, method, RequestInfo(user), cancellationToken);
					return Problem(type: PasskeyOutcomes.ErrorCode(assertion.Outcome), title: ApiPasskeys.TitleFor(assertion.Outcome),
						statusCode: ApiPasskeys.StatusFor(assertion.Outcome));
				}

				verifiedAt = assertion.VerifiedOnUtc;
				evidenceMethod = MfaEvidenceMethod.Passkey;
				factorReference = UserPasskey.FactorReferenceFor(assertion.Passkey.UserPasskeyId);
			}
			else if (method == MfaMethodNames.PasskeyApproval)
			{
				if (!await _mfaPolicy.IsMethodAcceptedAsync(session.DepartmentId, MfaMethodScope.Login, MfaEvidenceMethod.PasskeyApproval, cancellationToken))
					return Problem(type: "mfa_method_not_allowed", title: "That verification method is not available.", statusCode: StatusCodes.Status400BadRequest);

				// Only an unlock request made by this session at this lock version unlocks it.
				var found = await _approvals.GetForRequesterAsync(input.ApprovalRequestId, MfaApprovalRequesterKind.Session, session.UserSessionId,
					cancellationToken);
				if (found.Succeeded && (found.Request.RequestPurpose != MfaApprovalPurpose.Unlock || found.Request.LockVersion != session.LockVersion))
					return Problem(type: "approval_expired", title: "That approval was for an earlier lock. Request it again.",
						statusCode: StatusCodes.Status400BadRequest);

				var consumed = await _approvals.ConsumeAsync(input.ApprovalRequestId, MfaApprovalRequesterKind.Session, session.UserSessionId, user.Id,
					session.AuthenticationGeneration, cancellationToken);
				if (!consumed.Succeeded)
				{
					if (consumed.Outcome == MfaApprovalOutcome.Denied)
						await _sharedSessions.RecordFailedUnlockAsync(session, method, RequestInfo(user), cancellationToken);
					return RefuseApproval(consumed.Outcome);
				}

				verifiedAt = consumed.Request.DecidedOnUtc ?? DateTime.UtcNow;
				evidenceMethod = MfaEvidenceMethod.PasskeyApproval;
				factorReference = MfaApprovalRequest.FactorReferenceFor(consumed.Request.ApproverPasskeyId, consumed.Request.ApproverSessionId);
			}
			else
			{
				// One-time: each time step is accepted once per user, so a code seen on this screen cannot be replayed.
				if (!await _userManager.VerifyTwoFactorTokenAsync(user, _userManager.Options.Tokens.AuthenticatorTokenProvider,
						input.Code.Replace(" ", string.Empty).Replace("-", string.Empty)))
				{
					await _userManager.AccessFailedAsync(user);
					await _sharedSessions.RecordFailedUnlockAsync(session, method, RequestInfo(user), cancellationToken);
					return Problem(type: "invalid_totp", title: "The verification code is invalid or has expired.", statusCode: StatusCodes.Status401Unauthorized);
				}

				await _userManager.ResetAccessFailedCountAsync(user);
				verifiedAt = DateTime.UtcNow;
				evidenceMethod = MfaEvidenceMethod.Totp;
			}

			var unlocked = await _sharedSessions.UnlockAsync(session, input.LockVersion, evidenceMethod, factorReference, verifiedAt, RequestInfo(user),
				cancellationToken);
			if (!unlocked.Succeeded)
				return Refuse(unlocked.Outcome);

			return Wrap(new CompleteUnlockResult { Data = ToData(await _sharedSessions.GetStatusAsync(session, cancellationToken), session, user.UserName) });
		}

		private static readonly string[] UnlockMethods =
			{ MfaMethodNames.Totp, MfaMethodNames.Passkey, MfaMethodNames.PasskeyApproval, MfaMethodNames.Federated };

		/// <summary>
		/// Redeems a provider step-up begun with <c>unlock-sso</c> for this session and lock: the same department and tested
		/// mapping, this session and operator, the current generation, and begun after the session's last lock, so a round
		/// trip from before a lock or for anything else never unlocks it. Evidence names the SSO configuration and mapping
		/// version, so a mapping change retires it.
		/// </summary>
		private async Task<(ObjectResult Problem, DateTime VerifiedAt, string FactorReference)> VerifyFederatedUnlockAsync(UserSession session,
			Model.Identity.IdentityUser user, CompleteUnlockInput input, CancellationToken cancellationToken)
		{
			if (session.DepartmentId is not int departmentId ||
				!await _mfaPolicy.IsMethodAcceptedAsync(departmentId, MfaMethodScope.Login, MfaEvidenceMethod.Federated, cancellationToken))
				return (Problem(type: "mfa_method_not_allowed", title: "That verification method is not available.", statusCode: StatusCodes.Status400BadRequest),
					default, null);

			var redeemed = await _ssoBroker.RedeemAsync(input.SsoTransactionId, input.SsoCode, input.CodeVerifier,
				(UserSessionClientApplication)session.ClientApplication, cancellationToken, SsoTransactionPurpose.StepUp);
			var transaction = redeemed.Transaction;
			var config = redeemed.Succeeded ? await _departmentSso.GetTestedFederatedMfaConfigAsync(departmentId, cancellationToken) : null;
			if (!redeemed.Succeeded || !SharedSessionRules.FederatedUnlockMatches(transaction, session, user.Id, departmentId, config))
			{
				if (redeemed.Succeeded)
					await _sharedSessions.RecordFailedUnlockAsync(session, MfaMethodNames.Federated, RequestInfo(user), cancellationToken);
				return (Problem(type: redeemed.Succeeded ? "federated_mfa_not_satisfied" : SsoBrokerOutcomes.ErrorCode(redeemed.Outcome) ?? "sso_failed",
					title: "The identity provider sign-in could not unlock this session. Start it again.", statusCode: StatusCodes.Status401Unauthorized),
					default, null);
			}

			return (null, transaction.AuthenticatedOnUtc ?? DateTime.UtcNow,
				FederatedMfaMapping.FactorReferenceFor(config.DepartmentSsoConfigId, config.FederatedMfaMappingVersion));
		}

		/// <summary>
		/// The locked session this request is about, its operator, and whether they may try to unlock it now: shared, locked,
		/// still at the lock version the client saw, and not waiting on an SSO sign-in.
		/// </summary>
		private async Task<(UserSession Session, Model.Identity.IdentityUser User, ObjectResult Refusal)> OpenUnlockAsync(long? expectedLockVersion,
			CancellationToken cancellationToken)
		{
			var session = OwnSession();
			if (session == null)
				return (null, null, SessionRequired());

			var outcome = await _sharedSessions.CanUnlockAsync(session, cancellationToken);
			if (outcome != SharedSessionOutcome.Succeeded)
				return (null, null, Refuse(outcome));
			if (expectedLockVersion != session.LockVersion)
				return (null, null, Refuse(SharedSessionOutcome.LockChanged));

			var user = await _userManager.FindByIdAsync(session.UserId);
			return user == null ? (null, null, Refuse(SharedSessionOutcome.SessionEnded)) : (session, user, null);
		}

		/// <summary>The session request validation accepted for this caller; for a locked session, only on these endpoints.</summary>
		private UserSession OwnSession()
		{
			var session = SharedSessionEndpoints.SessionOf(HttpContext);
			return session != null && string.Equals(session.UserId, UserId, StringComparison.OrdinalIgnoreCase) ? session : null;
		}

		private PasskeyCaller UnlockCaller(UserSession session, Model.Identity.IdentityUser user) => new()
		{
			UserId = user.Id,
			UserName = user.UserName,
			SessionId = session.UserSessionId,
			ClientApplication = (UserSessionClientApplication)session.ClientApplication,
			AuthenticationGeneration = session.AuthenticationGeneration,
			SessionLockVersion = session.LockVersion,
			DepartmentId = session.DepartmentId,
			SharedMode = true,
			AuditSystem = SystemAuditSystems.Api,
			IpAddress = IpAddressHelper.GetRequestIP(Request, true)
		};

		private SharedSessionRequestInfo RequestInfo(Model.Identity.IdentityUser user = null) => new()
		{
			UserName = user?.UserName ?? UserName,
			IpAddress = IpAddressHelper.GetRequestIP(Request, true),
			CorrelationId = HttpContext.TraceIdentifier
		};

		private static CurrentSessionResultData ToData(SharedSessionStatus status, UserSession session, string operatorName = null) => new()
		{
			Operator = operatorName,
			Client = ApiPasskeys.ClientName((UserSessionClientApplication)session.ClientApplication),
			Shared = status.Shared,
			Locked = status.Locked,
			LockVersion = status.LockVersion,
			LockReason = status.LockReason switch
			{
				SharedSessionLockReason.Explicit => "explicit",
				SharedSessionLockReason.Idle => "idle",
				_ => null
			},
			IdleLockMinutes = status.IdleLockMinutes,
			IdleLocksAt = ApiPasskeys.Iso(status.IdleLocksOnUtc),
			ShiftEndsAt = ApiPasskeys.Iso(status.ShiftEndsOnUtc),
			InstallationLabel = status.InstallationLabel
		};

		private ObjectResult SessionRequired() =>
			Problem(type: "session_required", title: "Sign in again to continue.", statusCode: StatusCodes.Status409Conflict);

		private ObjectResult Refuse(SharedSessionOutcome outcome) => Problem(
			type: SharedSessionOutcomes.ErrorCode(outcome),
			title: outcome switch
			{
				SharedSessionOutcome.NotShared => "This is not a shared session.",
				SharedSessionOutcome.NotLocked => "This session is not locked.",
				SharedSessionOutcome.LockChanged => "The session locked again. Start the unlock again.",
				SharedSessionOutcome.SsoReauthenticationRequired => "Your department requires single sign-on. End the shift and sign in with SSO.",
				SharedSessionOutcome.SessionEnded => "This session has ended. Sign in again.",
				_ => "The session could not be updated. Try again."
			},
			statusCode: outcome switch
			{
				SharedSessionOutcome.SessionEnded or SharedSessionOutcome.SsoReauthenticationRequired => StatusCodes.Status401Unauthorized,
				SharedSessionOutcome.ServiceUnavailable => StatusCodes.Status503ServiceUnavailable,
				_ => StatusCodes.Status409Conflict
			});

		private ObjectResult RefuseApproval(MfaApprovalOutcome outcome) => Problem(
			type: MfaApprovalOutcomes.ErrorCode(outcome) ?? "approval_unavailable",
			title: outcome switch
			{
				MfaApprovalOutcome.Pending => "The request has not been approved yet.",
				MfaApprovalOutcome.Denied => "The request was denied in Responder.",
				MfaApprovalOutcome.Suspended => "Approval requests are paused for a few minutes. Use another verification method.",
				MfaApprovalOutcome.TooManyRequests => "Too many approval requests. Wait a few minutes or use another verification method.",
				MfaApprovalOutcome.Unavailable => "Approve with Responder is not available here.",
				_ => "The request is no longer valid. Start again."
			},
			statusCode: outcome switch
			{
				MfaApprovalOutcome.Pending => StatusCodes.Status409Conflict,
				MfaApprovalOutcome.Denied => StatusCodes.Status403Forbidden,
				MfaApprovalOutcome.TooManyRequests or MfaApprovalOutcome.Suspended => StatusCodes.Status429TooManyRequests,
				MfaApprovalOutcome.ServiceUnavailable => StatusCodes.Status503ServiceUnavailable,
				_ => StatusCodes.Status400BadRequest
			});

		private static T Wrap<T>(T result) where T : Models.v4.StandardApiResponseV4Base
		{
			result.PageSize = 1;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		private Task AuditAsync(SystemAuditTypes type, string sessionId, bool successful, CancellationToken cancellationToken)
		{
			return _systemAuditsService.SaveSystemAuditAsync(new SystemAudit
			{
				System = (int)SystemAuditSystems.Api,
				Type = (int)type,
				UserId = UserId,
				TargetUserId = UserId,
				SessionId = SessionSupportSuffix(sessionId),
				Successful = successful,
				IpAddress = IpAddressHelper.GetRequestIP(Request, true),
				ServerName = Environment.MachineName,
				CorrelationId = HttpContext.TraceIdentifier,
				Data = $"API session operation. Agent={BoundAuditValue(Request.Headers.UserAgent.ToString(), 256)}",
				LoggedOn = DateTime.UtcNow
			}, cancellationToken);
		}

		private static string BoundAuditValue(string value, int maximumLength)
		{
			if (string.IsNullOrWhiteSpace(value)) return "Unknown";
			var sanitized = value.Replace("\r", " ").Replace("\n", " ").Trim();
			return sanitized.Length <= maximumLength ? sanitized : sanitized.Substring(0, maximumLength);
		}

		private static string SessionSupportSuffix(string sessionId) =>
			string.IsNullOrWhiteSpace(sessionId) || sessionId.Length <= 8
				? sessionId
				: sessionId.Substring(sessionId.Length - 8);
	}
}
