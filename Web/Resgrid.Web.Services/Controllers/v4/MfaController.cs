using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Security;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.Mfa;
using Resgrid.Web.Services.Models.v4.Passkeys;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// API step-up for sensitive operations (passkey plan sections 7.6 and 11): the v4 replacement for per-feature
	/// verification. A verified second factor becomes server-side evidence on the caller's validated session, with its
	/// method and time; it returns no token and authorizes nothing by itself. The guarded commands check the evidence.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	// Authentication and session flows stay available during a department operation lock (ADP plan section 20.2): a locked
	// shared session must still unlock or end its shift, and Responder must still approve or deny.
	[Resgrid.Web.Services.Filters.AllowDuringDepartmentLock]
	public class MfaController : V4AuthenticatedApiControllerbase
	{
		private const int MaxAttempts = 5;
		private static readonly TimeSpan AttemptWindow = TimeSpan.FromMinutes(5);

		private readonly UserManager<Model.Identity.IdentityUser> _userManager;
		private readonly IMfaPolicyService _mfaPolicy;
		private readonly IMfaEvidenceService _mfaEvidence;
		private readonly ICacheProvider _cacheProvider;
		private readonly ISystemAuditsService _systemAudits;
		private readonly IPasskeyService _passkeys;
		private readonly IDepartmentSsoService _departmentSso;
		private readonly ISsoBrokerService _ssoBroker;
		private readonly IMfaApprovalService _approvals;
		private readonly IMfaActivityService _activity;

		public MfaController(UserManager<Model.Identity.IdentityUser> userManager, IMfaPolicyService mfaPolicy,
			IMfaEvidenceService mfaEvidence, ICacheProvider cacheProvider, ISystemAuditsService systemAudits, IPasskeyService passkeys,
			IDepartmentSsoService departmentSso, ISsoBrokerService ssoBroker, IMfaApprovalService approvals, IMfaActivityService activity)
		{
			_activity = activity;
			_approvals = approvals;
			_departmentSso = departmentSso;
			_ssoBroker = ssoBroker;
			_userManager = userManager;
			_mfaPolicy = mfaPolicy;
			_mfaEvidence = mfaEvidence;
			_cacheProvider = cacheProvider;
			_systemAudits = systemAudits;
			_passkeys = passkeys;
		}

		/// <summary>The methods the caller can use for <paramref name="operation"/>, and which to show first.</summary>
		[HttpGet("StepUpOptions")]
		[Authorize]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<StepUpOptionsResult>> StepUpOptions([FromQuery] string operation, CancellationToken cancellationToken)
		{
			if (!MfaStepUpOperations.IsKnown(operation))
				return Problem(type: "invalid_request", title: "Unknown step-up operation.", statusCode: StatusCodes.Status400BadRequest);

			var user = await _userManager.FindByIdAsync(UserId);
			if (user == null)
				return Problem(type: "session_revoked", title: "User not found.", statusCode: StatusCodes.Status401Unauthorized);

			// A passkey counts as enrolled only when one is bound to the app asking (plan section 3 item 14).
			var caller = ApiPasskeys.Caller(HttpContext, user.Id, user.UserName, DepartmentId);
			var passkeyEnrolled = caller != null && await _passkeys.HasActiveForClientAsync(user.Id, caller.ClientApplication, cancellationToken);
			// Provider step-up runs through Sso/Begin with purpose step_up; never for account factors (plan section 7.8).
			var federatedEnrolled = operation != MfaStepUpOperations.AccountSecurity &&
				await _departmentSso.IsFederatedMfaAvailableAsync(DepartmentId, user.Id, cancellationToken);
			// Responder approval from the user's own phone, for another app's session (plan section 7.9).
			var approvalEnrolled = caller != null && await _approvals.IsAvailableAsync(user.Id, caller.ClientApplication, cancellationToken);
			var choice = await _mfaPolicy.GetMethodChoiceAsync(UserId, await _userManager.GetTwoFactorEnabledAsync(user), DepartmentId,
				MfaStepUpOperations.ScopeFor(operation), passkeyEnrolled, federatedEnrolled, approvalEnrolled, cancellationToken);
			var methods = choice.AllowedMethods.Where(choice.EnrolledMethods.Contains).ToList();

			PasskeyCeremonyResultData passkey = null;
			if (methods.Contains(MfaMethodNames.Passkey))
			{
				// Advisory like the rest of this response: when the ceremony cannot start, the other methods still work.
				var start = await _passkeys.BeginAssertionAsync(caller, AuthenticationChallengePurpose.SensitiveOperation, cancellationToken);
				if (start.Succeeded)
					passkey = new PasskeyCeremonyResultData { RequestId = start.RequestId, Options = ApiPasskeys.Options(start.OptionsJson) };
			}

			var result = new StepUpOptionsResult
			{
				Data = new StepUpOptionsResultData
				{
					Methods = methods,
					EnrolledMethods = choice.EnrolledMethods.ToList(),
					AllowedMethods = choice.AllowedMethods.ToList(),
					Preferred = choice.Preferred,
					EnrollmentRequired = !choice.CanVerify,
					WindowMinutes = (int)MfaStepUpOperations.WindowFor(operation).TotalMinutes,
					Passkey = passkey
				},
				PageSize = 1,
				Status = ResponseHelper.Success
			};
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>
		/// Verifies a second factor for <c>operation</c> and records it as evidence on this session. A TOTP failure counts
		/// against the account lockout shared with sign-in (plan section 7.5 rule 6); a passkey is limited by its single-use
		/// request instead, since a signature cannot be guessed.
		/// </summary>
		[HttpPost("VerifyStepUp")]
		[Authorize]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<VerifyStepUpResult>> VerifyStepUp([FromBody] VerifyStepUpInput input, CancellationToken cancellationToken)
		{
			if (input == null || !MfaStepUpOperations.IsKnown(input.Operation))
				return Problem(type: "invalid_request", title: "Unknown step-up operation.", statusCode: StatusCodes.Status400BadRequest);

			var method = input.Method ?? MfaMethodNames.Totp;
			var isPasskey = string.Equals(method, MfaMethodNames.Passkey, StringComparison.Ordinal);
			var isFederated = string.Equals(method, MfaMethodNames.Federated, StringComparison.Ordinal);
			var isApproval = string.Equals(method, MfaMethodNames.PasskeyApproval, StringComparison.Ordinal);
			var isTotp = string.Equals(method, MfaMethodNames.Totp, StringComparison.Ordinal);
			if (!isPasskey && !isFederated && !isApproval && !isTotp)
				return Problem(type: "mfa_method_not_allowed", title: "That verification method is not available.",
					statusCode: StatusCodes.Status400BadRequest);
			if (isTotp && string.IsNullOrWhiteSpace(input.Code))
				return Problem(type: "invalid_totp", title: "A verification code is required.", statusCode: StatusCodes.Status400BadRequest);

			// Evidence belongs to a tracked session; a token without one cannot hold it, so the client signs in again.
			var sessionKey = ApiStepUpEvidence.SessionKey(HttpContext);
			if (sessionKey == null)
				return Problem(type: "session_required", title: "Sign in again to verify for this operation.",
					statusCode: StatusCodes.Status409Conflict);

			// Brute-force limiter on top of the account lockout. It fails open on cache faults; the lockout does not.
			if (await _cacheProvider.IncrementAsync($"MfaStepUpAttempts_{UserId}", AttemptWindow) > MaxAttempts)
				return Problem(type: "too_many_attempts", title: "Too many verification attempts. Wait a few minutes and try again.",
					statusCode: StatusCodes.Status429TooManyRequests);

			var user = await _userManager.FindByIdAsync(UserId);
			if (user == null)
				return Problem(type: "session_revoked", title: "User not found.", statusCode: StatusCodes.Status401Unauthorized);

			// Provider step-up proves the provider account, so an SSO user needs no Resgrid factor for it; TOTP, passkeys and
			// approval (whose Responder passkey needed TOTP to register) do.
			if (!isFederated && !await _userManager.GetTwoFactorEnabledAsync(user))
				return Problem(type: "mfa_enrollment_required",
					title: "Set up an authenticator app in Resgrid on the web before this operation.",
					statusCode: StatusCodes.Status409Conflict);

			if (await _userManager.IsLockedOutAsync(user))
				return Problem(type: "too_many_attempts", title: "Too many failed attempts. Wait a few minutes and try again.",
					statusCode: StatusCodes.Status429TooManyRequests);

			DateTime verifiedAt;
			MfaEvidenceMethod evidenceMethod;
			string factorReference = null;
			if (isApproval)
			{
				var approved = await VerifyApprovalAsync(input, user, cancellationToken);
				if (approved.Problem != null)
				{
					await AuditAsync(user, false, input.Operation, method, cancellationToken);
					return approved.Problem;
				}

				verifiedAt = approved.VerifiedAt;
				evidenceMethod = MfaEvidenceMethod.PasskeyApproval;
				factorReference = approved.FactorReference;
			}
			else if (isFederated)
			{
				var federated = await VerifyFederatedAsync(input, cancellationToken);
				if (federated.Problem != null)
				{
					await AuditAsync(user, false, input.Operation, method, cancellationToken);
					return federated.Problem;
				}

				verifiedAt = federated.VerifiedAt;
				evidenceMethod = MfaEvidenceMethod.Federated;
				factorReference = federated.FactorReference;
			}
			else if (isPasskey)
			{
				// The department (or, for account changes, the deployment) must accept a passkey for this operation now.
				if (!await _mfaPolicy.IsMethodAcceptedAsync(DepartmentId, MfaStepUpOperations.ScopeFor(input.Operation), MfaEvidenceMethod.Passkey,
						cancellationToken))
					return Problem(type: "mfa_method_not_allowed", title: "That verification method is not available.",
						statusCode: StatusCodes.Status400BadRequest);

				var assertion = await _passkeys.CompleteAssertionAsync(ApiPasskeys.Caller(HttpContext, user.Id, user.UserName, DepartmentId),
					AuthenticationChallengePurpose.SensitiveOperation, input.RequestId, ApiPasskeys.CredentialJson(input.Credential), cancellationToken);
				if (!assertion.Succeeded)
				{
					if (assertion.Outcome is PasskeyOutcome.VerificationFailed or PasskeyOutcome.NotRegisteredForClient)
						await AuditAsync(user, false, input.Operation, method, cancellationToken);
					return Problem(type: PasskeyOutcomes.ErrorCode(assertion.Outcome), title: ApiPasskeys.TitleFor(assertion.Outcome),
						statusCode: ApiPasskeys.StatusFor(assertion.Outcome));
				}

				verifiedAt = assertion.VerifiedOnUtc;
				evidenceMethod = MfaEvidenceMethod.Passkey;
				factorReference = UserPasskey.FactorReferenceFor(assertion.Passkey.UserPasskeyId);
			}
			else
			{
				// One-time: ResgridAuthenticatorTokenProvider accepts each time step once per user, on every surface.
				if (!await _userManager.VerifyTwoFactorTokenAsync(user, _userManager.Options.Tokens.AuthenticatorTokenProvider,
						input.Code.Replace(" ", string.Empty).Replace("-", string.Empty)))
				{
					await _userManager.AccessFailedAsync(user);
					await AuditAsync(user, false, input.Operation, method, cancellationToken);
					return Problem(type: "invalid_totp", title: "The verification code is invalid or has expired.",
						statusCode: StatusCodes.Status401Unauthorized);
				}

				await _userManager.ResetAccessFailedCountAsync(user);
				verifiedAt = DateTime.UtcNow;
				evidenceMethod = MfaEvidenceMethod.Totp;
			}

			var client = HttpProtectedGrantContext.SessionOf(HttpContext)?.ClientApplication ?? (int)UserSessionClientApplication.Api;
			try
			{
				await _mfaEvidence.RecordAsync(user.Id, sessionKey, (UserSessionClientApplication)client, MfaEvidenceKind.SecondFactor,
					evidenceMethod, MfaEvidencePurpose.StepUp, verifiedAt, user.AuthenticationGeneration, DepartmentId,
					factorReference, cancellationToken);
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				// The evidence is the whole product of this call: without it the operation would refuse anyway.
				Framework.Logging.LogException(ex, "API step-up evidence could not be recorded.");
				return Problem(type: "service_unavailable", title: "The verification could not be recorded. Try again.",
					statusCode: StatusCodes.Status503ServiceUnavailable);
			}

			await AuditAsync(user, true, input.Operation, method, cancellationToken);

			var result = new VerifyStepUpResult
			{
				Data = new VerifyStepUpResultData
				{
					VerifiedAt = verifiedAt.ToString("O"),
					ExpiresAt = verifiedAt.Add(MfaStepUpOperations.WindowFor(input.Operation)).ToString("O")
				},
				PageSize = 1,
				Status = ResponseHelper.Success
			};
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>
		/// Uses a Responder approval requested by this session for this operation (plan section 7.9 step 6). The department
		/// must accept approval here (never security changes or account factors), and the approving passkey and Responder
		/// session must still count; the evidence names them and carries the approval time.
		/// </summary>
		private async Task<(ObjectResult Problem, DateTime VerifiedAt, string FactorReference)> VerifyApprovalAsync(VerifyStepUpInput input,
			Model.Identity.IdentityUser user, CancellationToken cancellationToken)
		{
			if (!await _mfaPolicy.IsMethodAcceptedAsync(DepartmentId, MfaStepUpOperations.ScopeFor(input.Operation), MfaEvidenceMethod.PasskeyApproval,
					cancellationToken))
				return (Problem(type: "mfa_method_not_allowed", title: "That verification method is not available.", statusCode: StatusCodes.Status400BadRequest),
					default, null);

			var session = HttpProtectedGrantContext.SessionOf(HttpContext);
			var found = await _approvals.GetForRequesterAsync(input.ApprovalRequestId, MfaApprovalRequesterKind.Session, session.SessionId, cancellationToken);
			// A shared session's request is bound to the lock version it was made at; a lock since then voids it.
			if (found.Succeeded && (found.Request.RequestPurpose != MfaApprovalPurpose.StepUp ||
					!string.Equals(found.Request.Operation, input.Operation, StringComparison.Ordinal) ||
					found.Request.LockVersion != session.SessionLockVersion))
				return (Problem(type: "approval_expired", title: "That approval was for another operation. Request it again.",
					statusCode: StatusCodes.Status400BadRequest), default, null);

			var consumed = await _approvals.ConsumeAsync(input.ApprovalRequestId, MfaApprovalRequesterKind.Session, session.SessionId, user.Id,
				session.AuthenticationGeneration, cancellationToken);
			if (!consumed.Succeeded)
				return (Problem(type: MfaApprovalOutcomes.ErrorCode(consumed.Outcome) ?? "approval_unavailable",
					title: consumed.Outcome == MfaApprovalOutcome.Pending ? "The request has not been approved yet." : "The approval could not be used. Request it again.",
					statusCode: consumed.Outcome switch
					{
						MfaApprovalOutcome.Pending => StatusCodes.Status409Conflict,
						MfaApprovalOutcome.Denied => StatusCodes.Status403Forbidden,
						MfaApprovalOutcome.ServiceUnavailable => StatusCodes.Status503ServiceUnavailable,
						_ => StatusCodes.Status400BadRequest
					}), default, null);

			var approval = consumed.Request;
			return (null, approval.DecidedOnUtc ?? DateTime.UtcNow,
				MfaApprovalRequest.FactorReferenceFor(approval.ApproverPasskeyId, approval.ApproverSessionId));
		}

		/// <summary>
		/// Redeems a provider step-up begun through <c>Sso/Begin</c> (purpose <c>step_up</c>) for this session and operation.
		/// Evidence carries the provider's own authentication time and names the SSO configuration and mapping version, so a
		/// mapping change retires it (plan section 7.8).
		/// </summary>
		private async Task<(ObjectResult Problem, DateTime VerifiedAt, string FactorReference)> VerifyFederatedAsync(VerifyStepUpInput input,
			CancellationToken cancellationToken)
		{
			if (input.Operation == MfaStepUpOperations.AccountSecurity ||
				!await _mfaPolicy.IsMethodAcceptedAsync(DepartmentId, MfaStepUpOperations.ScopeFor(input.Operation), MfaEvidenceMethod.Federated, cancellationToken))
				return (Problem(type: "mfa_method_not_allowed", title: "That verification method is not available.", statusCode: StatusCodes.Status400BadRequest),
					default, null);

			var session = HttpProtectedGrantContext.SessionOf(HttpContext);
			var redeemed = await _ssoBroker.RedeemAsync(input.SsoTransactionId, input.SsoCode, input.CodeVerifier,
				(UserSessionClientApplication)session.ClientApplication, cancellationToken, SsoTransactionPurpose.StepUp);
			var transaction = redeemed.Transaction;
			var config = redeemed.Succeeded ? await _departmentSso.GetTestedFederatedMfaConfigAsync(DepartmentId, cancellationToken) : null;
			if (!redeemed.Succeeded || transaction.DepartmentId != DepartmentId || !FederatedMfaMapping.Satisfies(transaction, config) ||
				!string.Equals(transaction.Operation, input.Operation, StringComparison.Ordinal) ||
				!string.Equals(transaction.SessionId, session.SessionId, StringComparison.Ordinal) ||
				// Begun before this shared session's last lock: nothing from before a lock counts after it (plan section 12.5.3).
				(session.SessionLockedOnUtc != null && transaction.CreatedOnUtc <= session.SessionLockedOnUtc.Value) ||
				!string.Equals(transaction.ExpectedUserId, UserId, StringComparison.OrdinalIgnoreCase) ||
				!string.Equals(transaction.UserId, UserId, StringComparison.OrdinalIgnoreCase) ||
				transaction.AuthenticationGeneration != session.AuthenticationGeneration)
				return (Problem(type: redeemed.Succeeded ? "federated_mfa_not_satisfied" : SsoBrokerOutcomes.ErrorCode(redeemed.Outcome) ?? "sso_failed",
					title: "The provider step-up could not be verified for this operation. Start it again.", statusCode: StatusCodes.Status401Unauthorized),
					default, null);

			return (null, transaction.AuthenticatedOnUtc ?? DateTime.UtcNow,
				FederatedMfaMapping.FactorReferenceFor(config.DepartmentSsoConfigId, config.FederatedMfaMappingVersion));
		}

		private async Task AuditAsync(Model.Identity.IdentityUser user, bool successful, string operation, string method, CancellationToken cancellationToken)
		{
			await _systemAudits.SaveSystemAuditAsync(new SystemAudit
			{
				System = (int)SystemAuditSystems.Api,
				Type = (int)SystemAuditTypes.TwoFactorStepUpVerified,
				UserId = user.Id,
				Username = user.UserName,
				Successful = successful,
				IpAddress = IpAddressHelper.GetRequestIP(Request, true),
				ServerName = Environment.MachineName,
				Data = $"API step-up for {operation} with {method}: {(successful ? "verified" : "failed")}."
			}, cancellationToken);

			// A success is recorded with its evidence; a failure has none, so it is recorded here (plan section 6.5).
			if (!successful)
			{
				var session = HttpProtectedGrantContext.SessionOf(HttpContext);
				await _activity.RecordAsync(new MfaActivityEntry
				{
					UserId = user.Id, Method = MfaMethodNames.Parse(method), Purpose = MfaEvidencePurpose.StepUp, Successful = false,
					ClientApplication = (UserSessionClientApplication)(session?.ClientApplication ?? (int)UserSessionClientApplication.Api),
					SharedMode = session?.SharedMode == true, DepartmentId = DepartmentId, SessionId = session?.SessionId
				}, cancellationToken);
			}
		}
	}
}
