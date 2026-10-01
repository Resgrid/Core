using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Security;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.AccountSecurity;
using Resgrid.Repositories.DataRepository.Stores;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// The account "Sign-in methods" view (passkey plan section 6.5): the signed-in user's own authenticator app and
	/// passkeys, grouped by the app each passkey works in. Viewing needs only a signed-in session; every change goes
	/// through a command that checks fresh MFA.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	// Authentication and session flows stay available during a department operation lock (ADP plan section 20.2): a locked
	// shared session must still unlock or end its shift, and Responder must still approve or deny.
	[Resgrid.Web.Services.Filters.AllowDuringDepartmentLock]
	public class AccountSecurityController : V4AuthenticatedApiControllerbase
	{
		private readonly UserManager<Model.Identity.IdentityUser> _userManager;
		private readonly IPasskeyService _passkeys;
		private readonly IUserMfaStateRepository _mfaState;
		private readonly IUserStore<Model.Identity.IdentityUser> _userStore;
		private readonly IMfaEvidenceService _evidence;
		private readonly IMfaPolicyService _policy;
		private readonly IUserSessionService _sessions;
		private readonly IExternalIdentityLinkService _identityLinks;
		private readonly ISecurityNoticeService _notices;
		private readonly ISystemAuditsService _audits;
		private readonly IUserSessionsRepository _sessionRows;
		private readonly IMfaActivityService _activity;
		private readonly IDepartmentsService _departments;
		private readonly IDepartmentSsoService _departmentSso;

		public AccountSecurityController(UserManager<Model.Identity.IdentityUser> userManager, IPasskeyService passkeys,
			IUserMfaStateRepository mfaState, IUserStore<Model.Identity.IdentityUser> userStore, IMfaEvidenceService evidence, IMfaPolicyService policy,
			IUserSessionService sessions, IExternalIdentityLinkService identityLinks, ISecurityNoticeService notices, ISystemAuditsService audits,
			IUserSessionsRepository sessionRows, IMfaActivityService activity, IDepartmentsService departments, IDepartmentSsoService departmentSso)
		{
			_sessionRows = sessionRows;
			_activity = activity;
			_departments = departments;
			_departmentSso = departmentSso;
			_userManager = userManager;
			_passkeys = passkeys;
			_mfaState = mfaState;
			_userStore = userStore;
			_evidence = evidence;
			_policy = policy;
			_sessions = sessions;
			_identityLinks = identityLinks;
			_notices = notices;
			_audits = audits;
		}

		[HttpGet("Methods")]
		[Authorize]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<AccountMethodsResult>> Methods(CancellationToken cancellationToken)
		{
			var user = await _userManager.FindByIdAsync(UserId);
			if (user == null)
				return Problem(type: "session_revoked", title: "User not found.", statusCode: StatusCodes.Status401Unauthorized);

			var totpEnrolled = await _userManager.GetTwoFactorEnabledAsync(user);
			var codesLeft = totpEnrolled ? await _userManager.CountRecoveryCodesAsync(user) : 0;
			var totpState = totpEnrolled ? await _mfaState.GetTotpStateAsync(user.Id, cancellationToken) : null;
			var passkeys = await _passkeys.GetActiveForUserAsync(user.Id, cancellationToken);
			var currentSession = HttpProtectedGrantContext.SessionOf(HttpContext);
			var currentClient = currentSession?.ClientApplication;
			var activity = await _activity.GetRecentAsync(user.Id, cancellationToken) ?? Array.Empty<MfaActivity>();
			var lastTotp = activity.FirstOrDefault(a => a.Successful && a.Method == (int)MfaEvidenceMethod.Totp);

			var result = new AccountMethodsResult
			{
				Data = new AccountMethodsResultData
				{
					CurrentClient = currentClient == null ? null : ApiPasskeys.ClientName((UserSessionClientApplication)currentClient.Value),
					Totp = new TotpMethodData
					{
						Enrolled = totpEnrolled,
						EnrolledOn = ApiPasskeys.Iso(totpState?.EnrolledOnUtc),
						LastUsedOn = totpState == null || totpState.LastAcceptedTimeStep <= 0 ? null : ApiPasskeys.Iso(totpState.LastAcceptedOnUtc),
						RecoveryCodesRemaining = codesLeft,
						RecoveryCodeWarning = totpEnrolled && codesLeft <= TwoFactorConfig.RecoveryCodeWarningThreshold,
						SetUpOnSharedInstallation = totpState?.EnrolledInSharedMode == true,
						EnrolledClient = totpState?.EnrolledClientApplication is int enrolledClient
							? ApiPasskeys.ClientName((UserSessionClientApplication)enrolledClient)
							: null,
						EnrolledInstallation = totpState?.EnrolledInstallation,
						LastUsedClient = lastTotp == null ? null : ApiPasskeys.ClientName((UserSessionClientApplication)lastTotp.ClientApplication),
						LastUsedInstallation = lastTotp?.InstallationLabel,
						CanTurnOff = totpEnrolled && passkeys.Count == 0
					},
					PasskeyGroups = ApiPasskeys.PasskeyClients.Select(client => new PasskeyClientGroupData
					{
						Client = ApiPasskeys.ClientName(client),
						RegistrationAvailable = _passkeys.IsRegistrationAvailable(client),
						Passkeys = passkeys.Where(p => p.ClientApplication == (int)client).Select(PasskeysController.ToData).ToList()
					}).ToList(),
					ApprovalInstallations = await ApprovalInstallationsAsync(user, passkeys, activity, currentSession?.SessionId),
					LinkedIdentities = await LinkedIdentitiesAsync(user.Id, activity, cancellationToken),
					RecentActivity = activity.Select(a => ToData(a, currentSession?.SessionId)).ToList()
				},
				PageSize = 1,
				Status = ResponseHelper.Success
			};
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>
		/// "This wasn't me" (plan section 6.5): marks a verification as not the account holder's, ends the session it opened or
		/// served (never the one reporting it) and sends a security notice. Its effect is ending a session, which any session of
		/// the account may already do, so it needs no fresh MFA: a user whose factor was taken can still report.
		/// </summary>
		[HttpPost("ReportActivity")]
		[Authorize]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<ReportActivityResult>> ReportActivity([FromBody] ReportActivityInput input, CancellationToken cancellationToken)
		{
			var session = HttpProtectedGrantContext.SessionOf(HttpContext);
			var user = await _userManager.FindByIdAsync(UserId);
			if (session == null || user == null)
				return Problem(type: "session_required", title: "Sign in again to continue.", statusCode: StatusCodes.Status409Conflict);
			if (string.IsNullOrWhiteSpace(input?.ActivityId))
				return Problem(type: "invalid_request", title: "Choose the activity to report.", statusCode: StatusCodes.Status400BadRequest);

			var report = await _activity.ReportAsync(user.Id, input.ActivityId.Trim(), session.SessionId,
				new SharedSessionRequestInfo
				{
					UserName = user.UserName, IpAddress = IpAddressHelper.GetRequestIP(Request, true), CorrelationId = HttpContext.TraceIdentifier
				}, cancellationToken);
			return report.Outcome switch
			{
				MfaActivityReportOutcome.NotFound => Problem(type: "activity_not_found", title: "That activity is not on this account or is too old to report.",
					statusCode: StatusCodes.Status404NotFound),
				MfaActivityReportOutcome.AlreadyReported => Problem(type: "activity_already_reported", title: "That activity was already reported.",
					statusCode: StatusCodes.Status409Conflict),
				_ => Wrap(new ReportActivityResult
				{
					Data = new ReportActivityResultData
					{
						SessionEnded = report.SessionEnded,
						NextSteps = new List<string> { "change_password", "review_methods" }
					}
				})
			};
		}

		/// <summary>
		/// The user's Responder installations that can receive approval requests (plan section 6.5): active, personal (never
		/// shared) Responder sessions. Push delivery is per account, not per installation, so no push state is shown.
		/// </summary>
		private async Task<List<ApprovalInstallationData>> ApprovalInstallationsAsync(Model.Identity.IdentityUser user, IReadOnlyList<UserPasskey> passkeys,
			IReadOnlyList<MfaActivity> activity, string currentSessionId)
		{
			var now = DateTime.UtcNow;
			var canApprove = passkeys.Any(p => Resgrid.Services.ApprovalApprovers.IsApprovingPasskey(p, user.Id));
			return (await _sessionRows.GetActiveByUserAsync(user.Id, now) ?? Array.Empty<UserSession>())
				.Where(s => s.ClientApplication == (int)UserSessionClientApplication.Responder && !s.SharedMode && s.State == (int)UserSessionState.Active &&
					s.RevokedOn == null && s.ExpiresOn > now && s.AuthenticationGeneration == user.AuthenticationGeneration)
				.OrderByDescending(s => s.LastActiveOn)
				.Select(s =>
				{
					var decision = activity.FirstOrDefault(a => a.Method == (int)MfaEvidenceMethod.PasskeyApproval &&
						string.Equals(a.ApproverSessionId, s.UserSessionId, StringComparison.Ordinal));
					return new ApprovalInstallationData
					{
						InstallationId = s.UserSessionId,
						Label = s.DeviceName,
						Platform = s.OperatingSystem,
						IsCurrent = string.Equals(s.UserSessionId, currentSessionId, StringComparison.Ordinal),
						ApprovalsOn = canApprove && s.ApprovalsDisabledOnUtc == null,
						StoppedOn = ApiPasskeys.Iso(s.ApprovalsDisabledOnUtc),
						LastDecisionOn = ApiPasskeys.Iso(decision?.OccurredOnUtc),
						LastDecision = decision == null ? null : decision.Successful ? "approved" : "denied"
					};
				})
				.ToList();
		}

		/// <summary>The user's department identity-provider links; informational, managed by the department and SCIM.</summary>
		private async Task<List<LinkedIdentityData>> LinkedIdentitiesAsync(string userId, IReadOnlyList<MfaActivity> activity, CancellationToken cancellationToken)
		{
			var result = new List<LinkedIdentityData>();
			foreach (var link in await _identityLinks.GetActiveLinksAsync(userId, cancellationToken) ?? Array.Empty<UserExternalIdentityLink>())
			{
				var department = await _departments.GetDepartmentByIdAsync(link.DepartmentId, false);
				var lastStepUp = activity.FirstOrDefault(a => a.Successful && a.Method == (int)MfaEvidenceMethod.Federated && a.DepartmentId == link.DepartmentId);
				result.Add(new LinkedIdentityData
				{
					DepartmentId = link.DepartmentId,
					DepartmentName = department?.Name,
					ProviderType = (SsoProviderType)link.ProviderType switch
					{
						SsoProviderType.Saml2 => "saml2",
						SsoProviderType.Oidc => "oidc",
						_ => null
					},
					LinkedOn = ApiPasskeys.Iso(link.LinkedOn),
					AcceptsProviderStepUp = await _policy.IsMethodAcceptedAsync(link.DepartmentId, MfaMethodScope.Login, MfaEvidenceMethod.Federated, cancellationToken) &&
						await _departmentSso.IsFederatedMfaAvailableAsync(link.DepartmentId, userId, cancellationToken),
					LastProviderStepUpOn = ApiPasskeys.Iso(lastStepUp?.OccurredOnUtc)
				});
			}

			return result;
		}

		private static MfaActivityData ToData(MfaActivity activity, string currentSessionId) => new()
		{
			ActivityId = activity.MfaActivityId,
			OccurredOn = ApiPasskeys.Iso(activity.OccurredOnUtc),
			Method = (MfaEvidenceMethod)activity.Method == MfaEvidenceMethod.RecoveryCode ? "recovery_code" : MfaMethodNames.From((MfaEvidenceMethod)activity.Method),
			Purpose = (MfaEvidencePurpose)activity.Purpose switch
			{
				MfaEvidencePurpose.Login => "login",
				MfaEvidencePurpose.Reauthentication => "reauthentication",
				MfaEvidencePurpose.StepUp => "step_up",
				MfaEvidencePurpose.AdpStepUp => "adp_step_up",
				MfaEvidencePurpose.SharedUnlock => "shared_unlock",
				_ => null
			},
			Successful = activity.Successful,
			Client = ApiPasskeys.ClientName((UserSessionClientApplication)activity.ClientApplication),
			Installation = activity.InstallationLabel,
			SharedInstallation = activity.SharedMode,
			IsCurrentSession = activity.SessionId != null && string.Equals(activity.SessionId, currentSessionId, StringComparison.Ordinal),
			ReportedOn = ApiPasskeys.Iso(activity.ReportedOnUtc)
		};

		// ── Reauthentication (plan section 6.2) ───────────────────────────────────────

		/// <summary>
		/// Confirms the password for this session: fresh first-factor evidence for operations that need it (adding a passkey,
		/// replacing the authenticator). Failures count toward the account lockout. SSO accounts reauthenticate through
		/// <c>Sso/Begin</c> with purpose <c>reauth</c>.
		/// </summary>
		[HttpPost("Reauthenticate")]
		[Authorize]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<ReauthenticateResult>> Reauthenticate([FromBody] ReauthenticateInput input, CancellationToken cancellationToken)
		{
			var session = HttpProtectedGrantContext.SessionOf(HttpContext);
			var sessionKey = ApiStepUpEvidence.SessionKey(HttpContext);
			var user = await _userManager.FindByIdAsync(UserId);
			if (session == null || sessionKey == null || user == null)
				return Problem(type: "session_required", title: "Sign in again to continue.", statusCode: StatusCodes.Status409Conflict);
			if (string.IsNullOrWhiteSpace(input?.Password))
				return Problem(type: "invalid_request", title: "Enter your password.", statusCode: StatusCodes.Status400BadRequest);
			if (!await _userManager.HasPasswordAsync(user) || !await _identityLinks.IsLocalLoginAllowedAsync(user.Id, DepartmentId, cancellationToken))
				return Problem(type: "sso_reauthentication_required", title: "Confirm who you are with your organization's sign-in instead.",
					statusCode: StatusCodes.Status409Conflict);
			if (await _userManager.IsLockedOutAsync(user))
				return Problem(type: "too_many_attempts", title: "Too many failed attempts. Wait a few minutes and try again.",
					statusCode: StatusCodes.Status429TooManyRequests);

			if (!await _userManager.CheckPasswordAsync(user, input.Password))
			{
				await _userManager.AccessFailedAsync(user);
				await AuditAsync(user, SystemAuditTypes.AccountReauthenticated, false, "Password reauthentication failed.", cancellationToken);
				return Problem(type: "invalid_grant", title: "The password is incorrect.", statusCode: StatusCodes.Status401Unauthorized);
			}

			await _userManager.ResetAccessFailedCountAsync(user);
			var now = DateTime.UtcNow;
			await _evidence.RecordAsync(user.Id, sessionKey, (UserSessionClientApplication)session.ClientApplication, MfaEvidenceKind.FirstFactor,
				MfaEvidenceMethod.Password, MfaEvidencePurpose.Reauthentication, now, session.AuthenticationGeneration, DepartmentId,
				cancellationToken: cancellationToken);
			await AuditAsync(user, SystemAuditTypes.AccountReauthenticated, true, "Password confirmed for this session.", cancellationToken);
			return Wrap(new ReauthenticateResult { Data = new ReauthenticateResultData { VerifiedAt = now.ToString("O") } });
		}

		// ── Replacing the authenticator (plan sections 6.2 and 7.5 rule 7) ────────────────

		/// <summary>
		/// Stages a new authenticator key. Needs the password (or SSO) confirmed within five minutes and the current
		/// authenticator or a passkey for this app verified within five minutes (plan section 7.6 row 14).
		/// </summary>
		[HttpPost("ReplaceTotpOptions")]
		[Authorize]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<ReplaceTotpOptionsResult>> ReplaceTotpOptions(CancellationToken cancellationToken)
		{
			var (user, refusal) = await ReplacementAuthorityAsync(TwoFactorConfig.FirstFactorReauthWindowMinutes, cancellationToken);
			if (refusal != null)
				return refusal;

			var (sharedKey, uri) = await AuthenticatorSetup.StageAsync(_userManager, user);
			return Wrap(new ReplaceTotpOptionsResult
			{
				Data = new ReplaceTotpOptionsResultData
				{
					SharedKey = sharedKey,
					AuthenticatorUri = uri,
					ExpiresIn = (int)TimeSpan.FromMinutes(Math.Max(1, TwoFactorConfig.StagedAuthenticatorLifetimeMinutes)).TotalSeconds
				}
			});
		}

		/// <summary>
		/// Makes the staged key the authenticator once its code verifies. Everything the old one authorized ends: every
		/// session (this one too), all evidence, and the old recovery codes. The new codes are returned once.
		/// </summary>
		[HttpPost("ReplaceTotp")]
		[Authorize]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<ReplaceTotpResult>> ReplaceTotp([FromBody] ReplaceTotpInput input, CancellationToken cancellationToken)
		{
			var (user, refusal) = await ReplacementAuthorityAsync(TwoFactorConfig.FirstFactorOperationWindowMinutes, cancellationToken);
			if (refusal != null)
				return refusal;

			var stagedKey = await AuthenticatorSetup.GetStagedKeyAsync(_userManager, user);
			if (stagedKey == null)
				return Problem(type: "setup_expired", title: "Start replacing the authenticator again.", statusCode: StatusCodes.Status400BadRequest);
			if (!await AuthenticatorSetup.VerifyStagedCodeAsync(_mfaState, user, stagedKey, input?.Code, cancellationToken))
			{
				await _userManager.AccessFailedAsync(user);
				return Problem(type: "invalid_totp", title: "The code from the new authenticator is invalid or has expired.",
					statusCode: StatusCodes.Status401Unauthorized);
			}

			var session = HttpProtectedGrantContext.SessionOf(HttpContext);
			var shared = session?.SharedMode == true;
			var sessionRow = session == null ? null : await _sessionRows.GetByIdAsync(session.SessionId);
			await AuthenticatorSetup.PromoteAsync(_userManager, _userStore, _mfaState, user, stagedKey,
				new TotpEnrollmentContext(shared, session?.ClientApplication, sessionRow?.DeviceName), cancellationToken);
			var codes = await AuthenticatorSetup.RetireOldAuthorityAsync(_userManager, _sessions, _evidence, user, cancellationToken);
			await AuditAsync(user, SystemAuditTypes.TwoFactorAuthenticatorReplaced, true, "Authenticator replaced via the API; all sessions revoked.",
				cancellationToken);
			await _notices.QueueAsync(new SecurityNoticeRequest
			{
				UserId = user.Id, Kind = SecurityNoticeKind.TotpReplaced, ClientApplication = (UserSessionClientApplication?)session?.ClientApplication
			}, cancellationToken);
			// Its setup key was on a shared screen (plan section 6.5): say so, so the user can replace it from a personal device.
			if (shared)
				await _notices.QueueAsync(new SecurityNoticeRequest
				{
					UserId = user.Id, Kind = SecurityNoticeKind.SharedInstallationFactor, ClientApplication = (UserSessionClientApplication?)session.ClientApplication
				}, cancellationToken);

			return Wrap(new ReplaceTotpResult { Data = new ReplaceTotpResultData { RecoveryCodes = codes.ToList(), SignInAgain = true } });
		}

		/// <summary>
		/// The authority to replace the authenticator: this session's first factor within <paramref name="firstFactorMinutes"/>,
		/// and TOTP or a passkey for this app within five minutes. Never approval or provider step-up (plan section 7.6 row 14).
		/// </summary>
		private async Task<(Model.Identity.IdentityUser User, ObjectResult Refusal)> ReplacementAuthorityAsync(int firstFactorMinutes,
			CancellationToken cancellationToken)
		{
			var session = HttpProtectedGrantContext.SessionOf(HttpContext);
			var sessionKey = ApiStepUpEvidence.SessionKey(HttpContext);
			var user = await _userManager.FindByIdAsync(UserId);
			if (session == null || sessionKey == null || user == null)
				return (null, Problem(type: "session_required", title: "Sign in again to continue.", statusCode: StatusCodes.Status409Conflict));
			if (!await _userManager.GetTwoFactorEnabledAsync(user))
				return (null, Problem(type: "mfa_enrollment_required", title: "There is no authenticator to replace.", statusCode: StatusCodes.Status409Conflict));
			if (await _userManager.IsLockedOutAsync(user))
				return (null, Problem(type: "too_many_attempts", title: "Too many failed attempts. Wait a few minutes and try again.",
					statusCode: StatusCodes.Status429TooManyRequests));

			if (!await _evidence.HasFreshFirstFactorAsync(user.Id, sessionKey, session.AuthenticationGeneration,
					TimeSpan.FromMinutes(Math.Max(1, firstFactorMinutes)), DateTime.UtcNow, cancellationToken))
				return (null, Problem(type: "reauthentication_required", title: "Confirm your password (or sign in with SSO) again first.",
					statusCode: StatusCodes.Status409Conflict));

			if (!await ApiStepUpEvidence.HasRecentSecondFactorAsync(_evidence, _policy, user.Id, HttpContext, DepartmentId, MfaMethodScope.Account,
					MfaStepUpOperations.WindowFor(MfaStepUpOperations.AccountSecurity), cancellationToken))
				return (null, Problem(type: "step_up_required",
					title: "Verify with your current authenticator or a passkey for this app first (Mfa/VerifyStepUp, operation account_security).",
					statusCode: StatusCodes.Status403Forbidden));

			return (user, null);
		}

		private T Wrap<T>(T result) where T : Models.v4.StandardApiResponseV4Base
		{
			result.PageSize = 1;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		private Task AuditAsync(Model.Identity.IdentityUser user, SystemAuditTypes type, bool successful, string data, CancellationToken cancellationToken) =>
			_audits.SaveSystemAuditAsync(new SystemAudit
			{
				System = (int)SystemAuditSystems.Api,
				Type = (int)type,
				UserId = user.Id,
				Username = user.UserName,
				Successful = successful,
				IpAddress = IpAddressHelper.GetRequestIP(Request, true),
				ServerName = Environment.MachineName,
				Data = data
			}, cancellationToken);
	}
}
