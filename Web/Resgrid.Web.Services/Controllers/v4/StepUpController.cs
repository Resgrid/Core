using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Security;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.Passkeys;
using Resgrid.Web.Services.Models.v4;
using Resgrid.Web.Services.Models.v4.StepUp;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Department step-up for every Omni app, in the app (owner decision 2026-11-26). A protected action refuses with the one
	/// <c>step_up_required</c> shape (<see cref="ApiStepUp"/>); the app asks <c>Options</c> what the member can use, verifies here with an
	/// authenticator code, a passkey, a Responder approval, the provider's MFA, the password or a fresh provider sign-in, and retries the
	/// same request. For signatures, acknowledgments and approvals the grant is server-side evidence on this tracked session (bound to the
	/// account, its authentication generation, this session and this department; nothing to send). For protected data the answer is a
	/// Protected Data Grant, sent as <c>X-Resgrid-Protected-Grant</c>.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	// Verifying touches no department data; a locked department's members must still be able to verify (as Mfa/VerifyStepUp).
	[Resgrid.Web.Services.Filters.AllowDuringDepartmentLock]
	public class StepUpController : V4AuthenticatedApiControllerbase
	{
		/// <summary>Attempts per user per window across every method, on top of the account lockout the code and password share with sign-in.</summary>
		public const int MaxAttempts = 5;
		public static readonly TimeSpan AttemptWindow = TimeSpan.FromMinutes(5);

		private readonly UserManager<Model.Identity.IdentityUser> _userManager;
		private readonly IStepUpPolicyService _policy;
		private readonly IStepUpCapabilityService _capabilities;
		private readonly IMfaEvidenceService _evidence;
		private readonly IMfaPolicyService _mfaPolicy;
		private readonly IPasskeyService _passkeys;
		private readonly IMfaApprovalService _approvals;
		private readonly ISsoBrokerService _broker;
		private readonly IDepartmentSsoService _departmentSso;
		private readonly IExternalIdentityLinkService _identityLinks;
		private readonly IAdpStepUpService _adpStepUp;
		private readonly ICacheProvider _cache;
		private readonly ISystemAuditsService _systemAudits;
		private readonly IMfaActivityService _activity;
		private readonly IEventAggregator _events;

		public StepUpController(UserManager<Model.Identity.IdentityUser> userManager, IStepUpPolicyService policy, IStepUpCapabilityService capabilities,
			IMfaEvidenceService evidence, IMfaPolicyService mfaPolicy, IPasskeyService passkeys, IMfaApprovalService approvals, ISsoBrokerService broker,
			IDepartmentSsoService departmentSso, IExternalIdentityLinkService identityLinks, IAdpStepUpService adpStepUp, ICacheProvider cache,
			ISystemAuditsService systemAudits, IMfaActivityService activity, IEventAggregator events)
		{
			_userManager = userManager;
			_policy = policy;
			_capabilities = capabilities;
			_evidence = evidence;
			_mfaPolicy = mfaPolicy;
			_passkeys = passkeys;
			_approvals = approvals;
			_broker = broker;
			_departmentSso = departmentSso;
			_identityLinks = identityLinks;
			_adpStepUp = adpStepUp;
			_cache = cache;
			_systemAudits = systemAudits;
			_activity = activity;
			_events = events;
		}

		// ── Options ───────────────────────────────────────────────────────────────────

		/// <summary>The department's rule for <paramref name="area"/> and what this member can use for it (advisory; Verify rechecks).</summary>
		[HttpGet("Options")]
		[Authorize]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<StepUpOptionsV4Result>> Options([FromQuery] string area, CancellationToken cancellationToken)
		{
			Response.Headers["Cache-Control"] = "no-store";
			if (!StepUpAreas.TryParse(area, out var stepUpArea))
				return Problem(type: "invalid_request", title: "Unknown step-up area.", statusCode: StatusCodes.Status400BadRequest);

			var user = await _userManager.FindByIdAsync(UserId);
			if (user == null)
				return Problem(type: "session_revoked", title: "User not found.", statusCode: StatusCodes.Status401Unauthorized);

			var requirement = await _capabilities.DescribeAsync(DepartmentId, user.Id, ApiStepUp.ClientOf(HttpContext), stepUpArea, cancellationToken);
			var data = new StepUpOptionsV4ResultData
			{
				Area = StepUpAreas.Name(stepUpArea),
				Required = requirement.Required,
				AllowedMethods = requirement.AllowedMethods,
				UsableMethods = requirement.UsableMethods,
				MfaMethods = requirement.MfaMethods,
				WindowMinutes = requirement.WindowMinutes,
				CanComplete = requirement.CanComplete
			};

			var sessionKey = ApiStepUpEvidence.SessionKey(HttpContext);
			if (stepUpArea != StepUpArea.ProtectedData && sessionKey != null)
			{
				var evaluation = await _policy.EvaluateAsync(Subject(user, sessionKey), stepUpArea, cancellationToken);
				data.Satisfied = evaluation.Satisfied;
				data.SatisfiedUntil = evaluation.ExpiresOnUtc?.ToString("O");
			}

			if (requirement.MfaMethods.Contains(MfaMethodNames.Passkey))
			{
				// Advisory like the rest: when the ceremony cannot start, the other methods still work.
				var start = stepUpArea == StepUpArea.ProtectedData
					? await _adpStepUp.BeginPasskeyAsync(AdpCaller(user), cancellationToken)
					: await _passkeys.BeginAssertionAsync(ApiPasskeys.Caller(HttpContext, user.Id, user.UserName, DepartmentId),
						AuthenticationChallengePurpose.SensitiveOperation, cancellationToken);
				if (start.Succeeded)
					data.Passkey = new PasskeyCeremonyResultData { RequestId = start.RequestId, Options = ApiPasskeys.Options(start.OptionsJson) };
			}

			return Wrap(new StepUpOptionsV4Result { Data = data });
		}

		// ── Verify ────────────────────────────────────────────────────────────────────

		/// <summary>
		/// Verifies one method for one area and records it for this session. Each method must be one the department allows for the area
		/// (checked before any secret is looked at), and every attempt counts against a per-user limit; a wrong code or password also counts
		/// against the account lockout shared with sign-in. Successes and failures are audited (method and area only).
		/// </summary>
		[HttpPost("Verify")]
		[Authorize]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<StepUpVerifyResult>> Verify([FromBody] StepUpVerifyInput input, CancellationToken cancellationToken)
		{
			Response.Headers["Cache-Control"] = "no-store";
			if (input == null || !StepUpAreas.TryParse(input.Area, out var area))
				return Problem(type: "invalid_request", title: "Unknown step-up area.", statusCode: StatusCodes.Status400BadRequest);

			var method = ParseMethod(input.Method);
			if (method == null)
				return Problem(type: "step_up_method_not_allowed", title: "That verification method is not available.", statusCode: StatusCodes.Status400BadRequest);
			var methodClass = StepUpMethodNames.ClassOf(method.Value);

			// Evidence belongs to a tracked session; a token without one cannot hold it, so the app signs in again.
			var session = HttpProtectedGrantContext.SessionOf(HttpContext);
			var sessionKey = ApiStepUpEvidence.SessionKey(HttpContext);
			if (session == null || sessionKey == null)
				return Problem(type: "session_required", title: "Sign in again to verify.", statusCode: StatusCodes.Status409Conflict);

			var rule = await _policy.GetRuleAsync(DepartmentId, area, cancellationToken);
			if (!rule.Allows(methodClass))
				return Problem(type: "step_up_method_not_allowed", title: "Your organization does not allow that kind of verification here.",
					statusCode: StatusCodes.Status400BadRequest);

			var missing = MissingInput(method.Value, input);
			if (missing != null)
				return Problem(type: missing, title: "Something the verification needs is missing.", statusCode: StatusCodes.Status400BadRequest);

			// Brute-force limiter on top of the account lockout. It fails open on cache faults; the lockout does not.
			if (await _cache.IncrementAsync($"StepUpAttempts_{UserId}", AttemptWindow) > MaxAttempts)
				return Problem(type: "too_many_attempts", title: "Too many verification attempts. Wait a few minutes and try again.",
					statusCode: StatusCodes.Status429TooManyRequests);

			var user = await _userManager.FindByIdAsync(UserId);
			if (user == null)
				return Problem(type: "session_revoked", title: "User not found.", statusCode: StatusCodes.Status401Unauthorized);
			if (await _userManager.IsLockedOutAsync(user))
				return Problem(type: "too_many_attempts", title: "Too many failed attempts. Wait a few minutes and try again.",
					statusCode: StatusCodes.Status429TooManyRequests);

			var verified = method.Value switch
			{
				MfaEvidenceMethod.Totp => await VerifyTotpAsync(user, input, cancellationToken),
				MfaEvidenceMethod.Password => await VerifyPasswordAsync(user, input, cancellationToken),
				MfaEvidenceMethod.Sso => await VerifySsoAsync(user, input, session, rule, cancellationToken),
				MfaEvidenceMethod.Passkey when area != StepUpArea.ProtectedData => await VerifyPasskeyAsync(user, input, cancellationToken),
				MfaEvidenceMethod.PasskeyApproval when area != StepUpArea.ProtectedData => await VerifyApprovalAsync(user, input, session, cancellationToken),
				MfaEvidenceMethod.Federated when area != StepUpArea.ProtectedData => await VerifyFederatedAsync(user, input, session, cancellationToken),
				_ => Verification.Delegated
			};

			// Protected data: the one grant issuer verifies the passkey, approval and provider MFA itself and answers the grant.
			if (area == StepUpArea.ProtectedData)
				return await ProtectedDataAsync(user, input, method.Value, verified, cancellationToken);

			if (verified.Problem != null)
			{
				await AuditAsync(user, area, input.Method, false, cancellationToken);
				return verified.Problem;
			}

			var purpose = methodClass == StepUpMethodClasses.Mfa ? MfaEvidencePurpose.StepUp : MfaEvidencePurpose.Reauthentication;
			var kind = methodClass == StepUpMethodClasses.Mfa ? MfaEvidenceKind.SecondFactor : MfaEvidenceKind.FirstFactor;
			try
			{
				await _evidence.RecordAsync(user.Id, sessionKey, (UserSessionClientApplication)session.ClientApplication, kind, method.Value, purpose,
					verified.VerifiedAt, session.AuthenticationGeneration, DepartmentId, verified.FactorReference, cancellationToken);
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				// The evidence is the whole product of this call: without it the retried action would refuse anyway.
				Framework.Logging.LogException(ex, "Step-up evidence could not be recorded.");
				return Problem(type: "service_unavailable", title: "The verification could not be recorded. Try again.",
					statusCode: StatusCodes.Status503ServiceUnavailable);
			}

			await AuditAsync(user, area, input.Method, true, cancellationToken);
			return Wrap(new StepUpVerifyResult
			{
				Data = new StepUpVerifyResultData
				{
					Area = StepUpAreas.Name(area),
					Method = input.Method,
					MethodClass = StepUpMethodNames.Of(methodClass)[0],
					VerifiedAt = verified.VerifiedAt.ToString("O"),
					ExpiresAt = verified.VerifiedAt.AddMinutes(rule.WindowMinutes).ToString("O")
				}
			});
		}

		/// <summary>The method names Verify takes; null for anything else (a recovery code is never a step-up).</summary>
		public static MfaEvidenceMethod? ParseMethod(string method) => method switch
		{
			MfaMethodNames.Totp => MfaEvidenceMethod.Totp,
			MfaMethodNames.Passkey => MfaEvidenceMethod.Passkey,
			MfaMethodNames.PasskeyApproval => MfaEvidenceMethod.PasskeyApproval,
			MfaMethodNames.Federated => MfaEvidenceMethod.Federated,
			StepUpMethodNames.Password => MfaEvidenceMethod.Password,
			StepUpMethodNames.Sso => MfaEvidenceMethod.Sso,
			_ => null
		};

		private static string MissingInput(MfaEvidenceMethod method, StepUpVerifyInput input) => method switch
		{
			MfaEvidenceMethod.Totp when string.IsNullOrWhiteSpace(input.Code) => "invalid_totp",
			MfaEvidenceMethod.Password when string.IsNullOrEmpty(input.Password) => "invalid_password",
			MfaEvidenceMethod.Passkey when string.IsNullOrWhiteSpace(input.RequestId) || input.Credential == null => "invalid_request",
			MfaEvidenceMethod.PasskeyApproval when string.IsNullOrWhiteSpace(input.ApprovalRequestId) => "invalid_request",
			MfaEvidenceMethod.Sso or MfaEvidenceMethod.Federated when string.IsNullOrWhiteSpace(input.SsoTransactionId) || string.IsNullOrWhiteSpace(input.SsoCode) ||
				string.IsNullOrWhiteSpace(input.CodeVerifier) => "invalid_request",
			_ => null
		};

		private sealed class Verification
		{
			public ObjectResult Problem { get; init; }
			public DateTime VerifiedAt { get; init; }
			public string FactorReference { get; init; }

			/// <summary>Not verified here: the protected-data grant issuer verifies this method itself.</summary>
			public static Verification Delegated { get; } = new();

			public static Verification Ok(DateTime verifiedAt, string factorReference = null) => new() { VerifiedAt = DateTime.SpecifyKind(verifiedAt, DateTimeKind.Utc), FactorReference = factorReference };
		}

		private Verification Fail(string type, string title, int status) => new() { Problem = Problem(type: type, title: title, statusCode: status) };

		private async Task<Verification> VerifyTotpAsync(Model.Identity.IdentityUser user, StepUpVerifyInput input, CancellationToken cancellationToken)
		{
			if (!await _userManager.GetTwoFactorEnabledAsync(user))
				return Fail("mfa_enrollment_required", "Set up an authenticator app first.", StatusCodes.Status409Conflict);

			// One-time: ResgridAuthenticatorTokenProvider accepts each time step once per user, on every surface.
			if (!await _userManager.VerifyTwoFactorTokenAsync(user, _userManager.Options.Tokens.AuthenticatorTokenProvider,
					input.Code.Replace(" ", string.Empty).Replace("-", string.Empty)))
			{
				await _userManager.AccessFailedAsync(user);
				await RecordFailedActivityAsync(user, MfaEvidenceMethod.Totp, cancellationToken);
				return Fail("invalid_totp", "The verification code is invalid or has expired.", StatusCodes.Status401Unauthorized);
			}

			await _userManager.ResetAccessFailedCountAsync(user);
			return Verification.Ok(DateTime.UtcNow);
		}

		private async Task<Verification> VerifyPasswordAsync(Model.Identity.IdentityUser user, StepUpVerifyInput input, CancellationToken cancellationToken)
		{
			// A member who signs in only through the provider has no password to offer here (as AccountSecurity/Reauthenticate).
			if (!await _userManager.HasPasswordAsync(user) || !await _identityLinks.IsLocalLoginAllowedAsync(user.Id, DepartmentId, cancellationToken))
				return Fail("password_not_available", "Confirm who you are with your organization's sign-in instead.", StatusCodes.Status409Conflict);

			if (!await _userManager.CheckPasswordAsync(user, input.Password))
			{
				await _userManager.AccessFailedAsync(user);
				return Fail("invalid_password", "The password is incorrect.", StatusCodes.Status401Unauthorized);
			}

			await _userManager.ResetAccessFailedCountAsync(user);
			return Verification.Ok(DateTime.UtcNow);
		}

		/// <summary>
		/// A fresh sign-in at the department's provider, begun by <c>Sso/Begin</c> with purpose <c>reauth</c> for this session (the broker sent
		/// <c>prompt=login</c> and <c>max_age</c>, and at the callback checked the id_token's issuer, audience, authorized party, nonce,
		/// signature and <c>auth_time</c>, and that the id_token was never used before). Here: the one-time code, this session, account and
		/// generation, this department, and the provider's own authentication time inside the area's window and after the round trip began.
		/// </summary>
		private async Task<Verification> VerifySsoAsync(Model.Identity.IdentityUser user, StepUpVerifyInput input, ProtectedGrantSessionContext session,
			StepUpRule rule, CancellationToken cancellationToken)
		{
			var redeemed = await _broker.RedeemAsync(input.SsoTransactionId, input.SsoCode, input.CodeVerifier, (UserSessionClientApplication)session.ClientApplication,
				cancellationToken, SsoTransactionPurpose.Reauthentication);
			if (!redeemed.Succeeded)
				return Fail(SsoBrokerOutcomes.ErrorCode(redeemed.Outcome) ?? "sso_failed", "The sign-in could not be confirmed. Start it again.",
					redeemed.Outcome == SsoBrokerOutcome.ServiceUnavailable ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status401Unauthorized);

			var transaction = redeemed.Transaction;
			var authenticatedAt = transaction.AuthenticatedOnUtc.HasValue ? DateTime.SpecifyKind(transaction.AuthenticatedOnUtc.Value, DateTimeKind.Utc) : (DateTime?)null;
			var now = DateTime.UtcNow;
			if (transaction.DepartmentId != DepartmentId || !string.Equals(transaction.SessionId, session.SessionId, StringComparison.Ordinal) ||
				(session.SessionLockedOnUtc != null && transaction.CreatedOnUtc <= session.SessionLockedOnUtc.Value) ||
				!string.Equals(transaction.ExpectedUserId, user.Id, StringComparison.OrdinalIgnoreCase) ||
				!string.Equals(transaction.UserId, user.Id, StringComparison.OrdinalIgnoreCase) ||
				transaction.AuthenticationGeneration != session.AuthenticationGeneration ||
				authenticatedAt == null || authenticatedAt.Value > now.AddSeconds(30) || now - authenticatedAt.Value > TimeSpan.FromMinutes(rule.WindowMinutes) ||
				// A sign-in from before this step-up began is an existing provider session, not a fresh authentication (prompt=login ignored).
				authenticatedAt.Value < transaction.CreatedOnUtc.AddSeconds(-30))
				return Fail("sso_reauthentication_not_satisfied", "The sign-in was for another session, account or organization, or is too old. Start it again.",
					StatusCodes.Status401Unauthorized);

			return Verification.Ok(authenticatedAt.Value, "sso:" + transaction.DepartmentSsoConfigId);
		}

		private async Task<Verification> VerifyPasskeyAsync(Model.Identity.IdentityUser user, StepUpVerifyInput input, CancellationToken cancellationToken)
		{
			if (!await _mfaPolicy.IsMethodAcceptedAsync(DepartmentId, MfaMethodScope.Login, MfaEvidenceMethod.Passkey, cancellationToken))
				return Fail("mfa_method_not_allowed", "That verification method is not available.", StatusCodes.Status400BadRequest);

			var assertion = await _passkeys.CompleteAssertionAsync(ApiPasskeys.Caller(HttpContext, user.Id, user.UserName, DepartmentId),
				AuthenticationChallengePurpose.SensitiveOperation, input.RequestId, ApiPasskeys.CredentialJson(input.Credential), cancellationToken);
			if (!assertion.Succeeded)
				return Fail(PasskeyOutcomes.ErrorCode(assertion.Outcome), ApiPasskeys.TitleFor(assertion.Outcome), ApiPasskeys.StatusFor(assertion.Outcome));

			return Verification.Ok(assertion.VerifiedOnUtc, UserPasskey.FactorReferenceFor(assertion.Passkey.UserPasskeyId));
		}

		/// <summary>A Responder approval this session requested (purpose <c>step_up</c>, operation <c>sensitive_operation</c>), used once.</summary>
		private async Task<Verification> VerifyApprovalAsync(Model.Identity.IdentityUser user, StepUpVerifyInput input, ProtectedGrantSessionContext session,
			CancellationToken cancellationToken)
		{
			if (!await _mfaPolicy.IsMethodAcceptedAsync(DepartmentId, MfaMethodScope.Login, MfaEvidenceMethod.PasskeyApproval, cancellationToken))
				return Fail("mfa_method_not_allowed", "That verification method is not available.", StatusCodes.Status400BadRequest);

			var found = await _approvals.GetForRequesterAsync(input.ApprovalRequestId, MfaApprovalRequesterKind.Session, session.SessionId, cancellationToken);
			if (found.Succeeded && (found.Request.RequestPurpose != MfaApprovalPurpose.StepUp ||
					!string.Equals(found.Request.Operation, MfaStepUpOperations.SensitiveOperation, StringComparison.Ordinal) ||
					found.Request.LockVersion != session.SessionLockVersion))
				return Fail("approval_expired", "That approval was for something else. Request it again.", StatusCodes.Status400BadRequest);

			var consumed = await _approvals.ConsumeAsync(input.ApprovalRequestId, MfaApprovalRequesterKind.Session, session.SessionId, user.Id,
				session.AuthenticationGeneration, cancellationToken);
			if (!consumed.Succeeded)
				return Fail(MfaApprovalOutcomes.ErrorCode(consumed.Outcome) ?? "approval_unavailable",
					consumed.Outcome == MfaApprovalOutcome.Pending ? "The request has not been approved yet." : "The approval could not be used. Request it again.",
					consumed.Outcome switch
					{
						MfaApprovalOutcome.Pending => StatusCodes.Status409Conflict,
						MfaApprovalOutcome.Denied => StatusCodes.Status403Forbidden,
						MfaApprovalOutcome.ServiceUnavailable => StatusCodes.Status503ServiceUnavailable,
						_ => StatusCodes.Status400BadRequest
					});

			var approval = consumed.Request;
			return Verification.Ok(approval.DecidedOnUtc ?? DateTime.UtcNow, MfaApprovalRequest.FactorReferenceFor(approval.ApproverPasskeyId, approval.ApproverSessionId));
		}

		/// <summary>The provider's MFA, begun by <c>Sso/Begin</c> with purpose <c>step_up</c> and operation <c>sensitive_operation</c> for this session.</summary>
		private async Task<Verification> VerifyFederatedAsync(Model.Identity.IdentityUser user, StepUpVerifyInput input, ProtectedGrantSessionContext session,
			CancellationToken cancellationToken)
		{
			if (!await _mfaPolicy.IsMethodAcceptedAsync(DepartmentId, MfaMethodScope.Login, MfaEvidenceMethod.Federated, cancellationToken))
				return Fail("mfa_method_not_allowed", "That verification method is not available.", StatusCodes.Status400BadRequest);

			var redeemed = await _broker.RedeemAsync(input.SsoTransactionId, input.SsoCode, input.CodeVerifier, (UserSessionClientApplication)session.ClientApplication,
				cancellationToken, SsoTransactionPurpose.StepUp);
			var transaction = redeemed.Transaction;
			var config = redeemed.Succeeded ? await _departmentSso.GetTestedFederatedMfaConfigAsync(DepartmentId, cancellationToken) : null;
			if (!redeemed.Succeeded || transaction.DepartmentId != DepartmentId || !FederatedMfaMapping.Satisfies(transaction, config) ||
				!string.Equals(transaction.Operation, MfaStepUpOperations.SensitiveOperation, StringComparison.Ordinal) ||
				!string.Equals(transaction.SessionId, session.SessionId, StringComparison.Ordinal) ||
				(session.SessionLockedOnUtc != null && transaction.CreatedOnUtc <= session.SessionLockedOnUtc.Value) ||
				!string.Equals(transaction.ExpectedUserId, user.Id, StringComparison.OrdinalIgnoreCase) ||
				!string.Equals(transaction.UserId, user.Id, StringComparison.OrdinalIgnoreCase) ||
				transaction.AuthenticationGeneration != session.AuthenticationGeneration)
			{
				if (redeemed.Succeeded)
					await RecordFailedActivityAsync(user, MfaEvidenceMethod.Federated, cancellationToken);
				return Fail(redeemed.Succeeded ? "federated_mfa_not_satisfied" : SsoBrokerOutcomes.ErrorCode(redeemed.Outcome) ?? "sso_failed",
					"The provider step-up could not be verified. Start it again.",
					redeemed.Outcome == SsoBrokerOutcome.ServiceUnavailable ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status401Unauthorized);
			}

			return Verification.Ok(transaction.AuthenticatedOnUtc ?? DateTime.UtcNow,
				FederatedMfaMapping.FactorReferenceFor(config.DepartmentSsoConfigId, config.FederatedMfaMappingVersion));
		}

		// ── Protected data: the ADP grant ─────────────────────────────────────────────

		private async Task<ActionResult<StepUpVerifyResult>> ProtectedDataAsync(Model.Identity.IdentityUser user, StepUpVerifyInput input, MfaEvidenceMethod method,
			Verification verified, CancellationToken cancellationToken)
		{
			if (verified.Problem != null)
			{
				await AuditAsync(user, StepUpArea.ProtectedData, input.Method, false, cancellationToken);
				return verified.Problem;
			}

			var caller = AdpCaller(user);
			AdpGrantIssue issued;
			switch (method)
			{
				case MfaEvidenceMethod.Totp:
					issued = await _adpStepUp.IssueForTotpAsync(caller, verified.VerifiedAt, cancellationToken);
					break;
				case MfaEvidenceMethod.Passkey:
					issued = await _adpStepUp.CompletePasskeyAsync(caller, input.RequestId, ApiPasskeys.CredentialJson(input.Credential), cancellationToken);
					break;
				case MfaEvidenceMethod.PasskeyApproval:
					issued = await _adpStepUp.CompleteApprovalAsync(caller, input.ApprovalRequestId, cancellationToken);
					break;
				case MfaEvidenceMethod.Federated:
					issued = await _adpStepUp.CompleteFederatedAsync(caller, input.SsoTransactionId, input.SsoCode, input.CodeVerifier, cancellationToken);
					break;
				default:
					// The password or provider sign-in was verified above; it is recorded as such, then the non-MFA grant is issued from it.
					try
					{
						await _evidence.RecordAsync(user.Id, caller.EvidenceKey, caller.Client, MfaEvidenceKind.FirstFactor, method, MfaEvidencePurpose.Reauthentication,
							verified.VerifiedAt, caller.Session.AuthenticationGeneration, DepartmentId, verified.FactorReference, cancellationToken);
					}
					catch (Exception ex) when (!(ex is OperationCanceledException))
					{
						Framework.Logging.LogException(ex, "Reauthentication evidence for protected data could not be recorded.");
					}

					issued = await _adpStepUp.IssueForReauthenticationAsync(caller, method, verified.VerifiedAt, cancellationToken);
					break;
			}

			await AuditAsync(user, StepUpArea.ProtectedData, input.Method, issued.Succeeded, cancellationToken);
			if (!issued.Succeeded)
				return Problem(type: issued.ErrorCode, title: "Protected data could not be unlocked with that verification.",
					statusCode: AdpGrantOutcomes.StatusFor(issued.Outcome));

			return Wrap(new StepUpVerifyResult
			{
				Data = new StepUpVerifyResultData
				{
					Area = StepUpAreas.ProtectedDataName,
					Method = input.Method,
					MethodClass = StepUpMethodNames.Of(StepUpMethodNames.ClassOf(method))[0],
					VerifiedAt = (method is MfaEvidenceMethod.Totp or MfaEvidenceMethod.Password or MfaEvidenceMethod.Sso ? verified.VerifiedAt : DateTime.UtcNow).ToString("O"),
					ExpiresAt = issued.ExpiresOnUtc.ToString("O"),
					GrantId = issued.GrantId,
					GrantToken = issued.Token,
					GrantExpiresAt = issued.ExpiresOnUtc.ToString("O")
				}
			});
		}

		/// <summary>The caller as the grant issuer sees it: this user, department and validated session (never client-supplied).</summary>
		private AdpStepUpCaller AdpCaller(Model.Identity.IdentityUser user) => new()
		{
			UserId = UserId,
			UserName = user?.UserName,
			DepartmentId = DepartmentId,
			Session = HttpProtectedGrantContext.SessionOf(HttpContext),
			LegacySessionId = User.FindFirst(SessionClaimTypes.SessionId)?.Value,
			ClientApplication = ApiStepUp.ClientOf(HttpContext),
			AccountAuthenticationGeneration = user?.AuthenticationGeneration ?? 0,
			EvidenceSessionKey = ApiStepUpEvidence.SessionKey(HttpContext),
			IpAddress = IpAddressHelper.GetRequestIP(Request, true),
			AuditSystem = SystemAuditSystems.Api
		};

		private StepUpSubject Subject(Model.Identity.IdentityUser user, string sessionKey) => new()
		{
			UserId = user.Id,
			DepartmentId = DepartmentId,
			SessionKey = sessionKey,
			AuthenticationGeneration = HttpProtectedGrantContext.SessionOf(HttpContext)?.AuthenticationGeneration ?? user.AuthenticationGeneration
		};

		// ── Audit ─────────────────────────────────────────────────────────────────────

		/// <summary>The system audit and the department audit log, success or failure: the area and method only, never a code or password.</summary>
		private async Task AuditAsync(Model.Identity.IdentityUser user, StepUpArea area, string method, bool successful, CancellationToken cancellationToken)
		{
			var mfa = StepUpMethodNames.ClassOf(ParseMethod(method) ?? MfaEvidenceMethod.Totp) == StepUpMethodClasses.Mfa;
			var areaName = StepUpAreas.Name(area);
			try
			{
				await _systemAudits.SaveSystemAuditAsync(new SystemAudit
				{
					System = (int)SystemAuditSystems.Api,
					Type = (int)(mfa ? SystemAuditTypes.TwoFactorStepUpVerified : SystemAuditTypes.AccountReauthenticated),
					UserId = user.Id,
					Username = user.UserName,
					Successful = successful,
					IpAddress = IpAddressHelper.GetRequestIP(Request, true),
					ServerName = Environment.MachineName,
					Data = $"Step-up for {areaName} with {method}: {(successful ? "verified" : "failed")}."
				}, cancellationToken);
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				Framework.Logging.LogException(ex, "A step-up audit could not be written.");
			}

			_events?.SendMessage(new AuditEvent
			{
				DepartmentId = DepartmentId,
				UserId = user.Id,
				Type = successful ? AuditLogTypes.StepUpCompleted : AuditLogTypes.StepUpFailed,
				After = JsonConvert.SerializeObject(new { Area = areaName, Method = method, Client = ApiPasskeys.ClientName(ApiStepUp.ClientOf(HttpContext)) }),
				Successful = successful,
				IpAddress = IpAddressHelper.GetRequestIP(Request, true),
				ServerName = Environment.MachineName,
				UserAgent = Request.Headers["User-Agent"].ToString()
			});
		}

		private Task RecordFailedActivityAsync(Model.Identity.IdentityUser user, MfaEvidenceMethod method, CancellationToken cancellationToken)
		{
			var session = HttpProtectedGrantContext.SessionOf(HttpContext);
			return _activity.RecordAsync(new MfaActivityEntry
			{
				UserId = user.Id, Method = method, Purpose = MfaEvidencePurpose.StepUp, Successful = false, ClientApplication = ApiStepUp.ClientOf(HttpContext),
				SharedMode = session?.SharedMode == true, DepartmentId = DepartmentId, SessionId = session?.SessionId
			}, cancellationToken);
		}

		private static T Wrap<T>(T result) where T : StandardApiResponseV4Base
		{
			result.PageSize = 1;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}
	}
}
