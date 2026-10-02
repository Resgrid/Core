using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Filters;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.DataProtection;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Advanced Data Protection capability and enrollment API (ADP plan sections 7.1, 12, 18).
	/// The capability report is value-free and advisory; every command is re-validated server-side —
	/// managing member only (ordinary admins, including ManageDepartmentDataProtection holders, are
	/// denied), active paid ADP addon, a fresh authoritative global-gate evaluation performed by the
	/// service immediately before commit, and (where grant key material is deployed) a
	/// currently-valid Protected Data Grant in X-Resgrid-Protected-Grant proving recent MFA
	/// (RequireRecentMfaAsync).
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class DataProtectionController : V4AuthenticatedApiControllerbase
	{
		// TOTP step-up brute-force limiter: attempts per user inside the window before 429.
		private const int StepUpMaxAttempts = 5;
		private static readonly TimeSpan StepUpAttemptWindow = TimeSpan.FromMinutes(5);

		[HttpPost("EnrollPin")]
		[Authorize]
		public async Task<IActionResult> EnrollPin([FromBody] PinEnrollmentInput input)
		{
			Response.Headers["Cache-Control"] = "no-store";
			return Ok(new { success = await _adpRelease.EnrollPinAsync(DepartmentId, UserId,
				Request.Headers[GrantHeader].ToString(), input?.Pin) });
		}

		public sealed class PinEnrollmentInput { public string Pin { get; set; } }

		/// <summary>Header carrying the caller's Protected Data Grant on MFA-gated commands.</summary>
		public const string GrantHeader = "X-Resgrid-Protected-Grant";

		private readonly IDepartmentDataProtectionService _dataProtectionService;
		private readonly IDepartmentLockService _departmentLockService;
		private readonly IProtectedFieldCatalog _protectedFieldCatalog;
		private readonly IDepartmentsService _departmentsService;
		private readonly IFeatureToggleService _featureToggleService;
		private readonly UserManager<Model.Identity.IdentityUser> _userManager;
		private readonly ICacheProvider _cacheProvider;
		private readonly IProtectedDataGrantService _grantService;
		private readonly IAdpReleaseService _adpRelease;
		private readonly Resgrid.Model.Repositories.IAdpAuditRepository _adpAudit;

		public DataProtectionController(IDepartmentDataProtectionService dataProtectionService,
			IDepartmentLockService departmentLockService, IProtectedFieldCatalog protectedFieldCatalog,
			IDepartmentsService departmentsService, IFeatureToggleService featureToggleService,
			UserManager<Model.Identity.IdentityUser> userManager, ICacheProvider cacheProvider,
			IProtectedDataGrantService grantService, IAdpReleaseService adpRelease, Resgrid.Model.Repositories.IAdpAuditRepository adpAudit,
			IMfaEvidenceService mfaEvidence, IMfaPolicyService mfaPolicy, IAdpStepUpService adpStepUp, IMfaCredentialStateService credentialStates)
		{
			_adpStepUp = adpStepUp;
			_credentialStates = credentialStates;
			_mfaPolicy = mfaPolicy;
			_dataProtectionService = dataProtectionService;
			_departmentLockService = departmentLockService;
			_protectedFieldCatalog = protectedFieldCatalog;
			_departmentsService = departmentsService;
			_featureToggleService = featureToggleService;
			_userManager = userManager;
			_cacheProvider = cacheProvider;
			_grantService = grantService;
			_adpRelease = adpRelease;
			_adpAudit = adpAudit;
			_mfaEvidence = mfaEvidence;
		}

		private readonly IMfaEvidenceService _mfaEvidence;
		private readonly IMfaPolicyService _mfaPolicy;
		private readonly IAdpStepUpService _adpStepUp;
		private readonly IMfaCredentialStateService _credentialStates;

		/// <summary>
		/// Value-free ADP capability report for the caller's department: durable state, catalog and
		/// policy versions, step-up window, egress summary, and lock state. Never returns protected
		/// values, ciphertext, or key material.
		/// </summary>
		[HttpGet("Capabilities")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize]
		public async Task<ActionResult<DataProtectionCapabilitiesResult>> Capabilities()
		{
			var policy = await _dataProtectionService.GetPolicyByDepartmentIdAsync(DepartmentId);
			var state = policy == null ? DepartmentDataProtectionState.Disabled : (DepartmentDataProtectionState)policy.State;
			var egress = await _dataProtectionService.GetEgressPolicyByDepartmentIdAsync(DepartmentId);
			var activeLock = await _departmentLockService.GetActiveLockAsync(DepartmentId);
			var isLocked = await _departmentLockService.IsDepartmentLockedAsync(DepartmentId);

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			var isManagingMember = department != null &&
				string.Equals(department.ManagingUserId, UserId, StringComparison.OrdinalIgnoreCase);

			// Advisory gate read (ordinary cached path is fine here; commands re-evaluate fresh).
			var gateOpen = false;
			try
			{
				var gate = await _featureToggleService.GetFlagByKeyAsync(FeatureFlagKeys.DepartmentProtectedDataEnrollment);
				gateOpen = gate != null && !gate.IsArchived && gate.IsEnabledGlobally;
			}
			catch
			{
				// Advisory only — a flag-store fault reads as "gate closed".
			}

			var result = new DataProtectionCapabilitiesResult
			{
				Data = new DataProtectionCapabilitiesData
				{
					State = (int)state,
					StateName = state.ToString(),
					IsProtectionEnabled = await _dataProtectionService.IsProtectionEnforcedAsync(DepartmentId),
					IsEnrollmentAvailable = gateOpen && state == DepartmentDataProtectionState.Disabled,
					CanEnable = isManagingMember && state == DepartmentDataProtectionState.Disabled,
					CanDisable = isManagingMember &&
						(state == DepartmentDataProtectionState.Enabled ||
						 state == DepartmentDataProtectionState.EnrollmentQueued ||
						 state == DepartmentDataProtectionState.OffboardingScheduled),
					ReenableRequiresFeatureFlag = true,
					CatalogVersion = policy?.CatalogVersion ?? 0,
					CurrentCatalogVersion = _protectedFieldCatalog.Version,
					PolicyEpoch = policy?.PolicyEpoch ?? 0,
					StepUpWindowMinutes = policy?.StepUpWindowMinutes ?? Config.DataProtectionConfig.StepUpWindowDefaultMinutes,
					OffboardingEffectiveOn = policy?.OffboardingEffectiveOn?.ToString("O"),
					PushEgressMode = egress.PushMode,
					EmailEgressMode = egress.EmailMode,
					SmsEgressMode = egress.SmsMode,
					VoiceEgressMode = egress.VoiceMode,
					IsDepartmentLocked = isLocked,
					LockReason = isLocked ? activeLock?.Reason : null,
					LockProjectedEndUtc = isLocked ? activeLock?.ProjectedEndUtc?.ToString("O") : null,
					AcknowledgementVersion = AdpEnrollmentAcknowledgements.Version,
					AcknowledgementItems = AdpEnrollmentAcknowledgements.Items.ToList()
				}
			};

			result.PageSize = 1;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>
		/// Issues a grant without a second factor for a client the department has exempted from the
		/// step-up prompt (plan section 3.3). Refused with <c>step_up_required</c> for every other
		/// client, which is what the app treats as "show the code prompt".
		///
		/// The exemption is per client application and is off for every app until a department's
		/// managing member turns it off deliberately. It removes the PROMPT, not the grant: the
		/// caller is still authenticated, the grant is still bound to this department and policy
		/// epoch, still expires, and still authorizes an audited read.
		/// </summary>
		[HttpPost("RequestGrant")]
		[AllowDuringDepartmentLock]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize]
		public async Task<ActionResult<StepUpResult>> RequestGrant()
		{
			var issued = await _adpStepUp.IssueExemptAsync(Caller());
			if (issued.Outcome == Model.Security.AdpGrantOutcome.StepUpRequired)
			{
				// This session's own recent sign-in or unlock MFA, where the department accepts reusing it (plan section 9.1).
				var reused = await _adpStepUp.IssueFromRecentEvidenceAsync(Caller());
				if (!reused.Succeeded)
					return Problem(type: "step_up_required",
						title: "This department requires second-factor verification before protected values are shown.",
						statusCode: StatusCodes.Status401Unauthorized);

				issued = reused;
			}

			return Grant(issued);
		}

		/// <summary>
		/// A grant from this session's recent sign-in or shared-unlock MFA, with no new prompt (plan sections 7.6 row 9 and 9.1): only
		/// where the department accepts reusing that method for protected data, and only until the original verification's window
		/// ends. <c>step_up_required</c> when nothing qualifies; the client then verifies with <c>VerifyStepUp</c> or another method.
		/// </summary>
		[HttpPost("RequestGrantFromRecentMfa")]
		[AllowDuringDepartmentLock]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize]
		public async Task<ActionResult<StepUpResult>> RequestGrantFromRecentMfa() =>
			Grant(await _adpStepUp.IssueFromRecentEvidenceAsync(Caller()));

		[HttpPost("VerifyStepUp")]
		[AllowDuringDepartmentLock]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize]
		public async Task<ActionResult<StepUpResult>> VerifyStepUp([FromBody] VerifyStepUpInput input)
		{
			if (string.IsNullOrWhiteSpace(input?.Code))
				return Problem(type: "invalid_totp", title: "A verification code is required.",
					statusCode: StatusCodes.Status400BadRequest);

			// Brute-force limiter. It fails open on cache faults so an outage does not disable step-up; replay is
			// still blocked because ResgridAuthenticatorTokenProvider accepts each TOTP time step once, in the database.
			var attempts = await _cacheProvider.IncrementAsync($"AdpStepUpAttempts_{UserId}", StepUpAttemptWindow);
			if (attempts > StepUpMaxAttempts)
				return Problem(type: "too_many_attempts",
					title: "Too many verification attempts. Wait a few minutes and try again.",
					statusCode: StatusCodes.Status429TooManyRequests);

			var user = await _userManager.FindByIdAsync(UserId);
			if (user == null)
				return Problem(type: "protected_access_denied", title: "User not found.",
					statusCode: StatusCodes.Status401Unauthorized);

			if (!await _userManager.GetTwoFactorEnabledAsync(user))
				return Problem(type: "mfa_not_enrolled",
					title: "Two-factor authentication is not enrolled for this account. Enroll an authenticator app in account security settings first.",
					statusCode: StatusCodes.Status409Conflict);

			// ADP step-up shares the account lockout with sign-in and every other TOTP surface (passkey plan section 7.5 rule 6).
			if (await _userManager.IsLockedOutAsync(user))
				return Problem(type: "too_many_attempts",
					title: "Too many failed attempts. Wait a few minutes and try again.",
					statusCode: StatusCodes.Status429TooManyRequests);

			var valid = await _userManager.VerifyTwoFactorTokenAsync(user,
				_userManager.Options.Tokens.AuthenticatorTokenProvider, input.Code.Trim());
			await _adpAudit.AppendAsync(new AdpAuditEvent { DepartmentId = DepartmentId, Layer = "identity",
				Operation = "mfa-verify", Outcome = valid ? "verified" : "denied", ActorId = UserId });
			if (!valid)
			{
				await _userManager.AccessFailedAsync(user);
				return Problem(type: "invalid_totp",
					title: "The verification code is invalid or has expired.",
					statusCode: StatusCodes.Status401Unauthorized);
			}

			await _userManager.ResetAccessFailedCountAsync(user);

			// The verified code becomes AdpStepUp evidence and, through the one issuer every method shares, a grant whose
			// expiry runs from this verification (passkey plan sections 8.1 and 9.2).
			return Grant(await _adpStepUp.IssueForTotpAsync(Caller(user), DateTime.UtcNow));
		}

		/// <summary>
		/// How the caller can verify for this department's protected data now: the methods it has that the department
		/// accepts, and which to show first (passkey plan section 7.5 rule 5). Advisory; every verification rechecks.
		/// </summary>
		[HttpGet("StepUpMethods")]
		[AllowDuringDepartmentLock]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize]
		public async Task<ActionResult<AdpStepUpMethodsResult>> StepUpMethods(System.Threading.CancellationToken cancellationToken)
		{
			var user = await _userManager.FindByIdAsync(UserId);
			if (user == null)
				return Problem(type: "protected_access_denied", title: "User not found.", statusCode: StatusCodes.Status401Unauthorized);

			var choice = await _adpStepUp.GetMethodChoiceAsync(Caller(user), await _userManager.GetTwoFactorEnabledAsync(user), cancellationToken);
			var result = new AdpStepUpMethodsResult
			{
				Data = new AdpStepUpMethodsResultData
				{
					Methods = choice.AllowedMethods.Where(choice.EnrolledMethods.Contains).ToList(),
					Preferred = choice.Preferred,
					EnrolledMethods = choice.EnrolledMethods.ToList(),
					AllowedMethods = choice.AllowedMethods.ToList()
				},
				PageSize = 1,
				Status = ResponseHelper.Success
			};
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>
		/// Assertion options for a passkey bound to this app, for this department's protected data (passkey plan section 8.1).
		/// Needs a tracked session; the department and deployment must accept passkeys for protected data.
		/// </summary>
		[HttpPost("PasskeyOptions")]
		[AllowDuringDepartmentLock]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize]
		public async Task<ActionResult<Models.v4.Passkeys.PasskeyCeremonyResult>> PasskeyOptions(System.Threading.CancellationToken cancellationToken)
		{
			var start = await _adpStepUp.BeginPasskeyAsync(Caller(await _userManager.FindByIdAsync(UserId)), cancellationToken);
			if (!start.Succeeded)
				return Problem(type: Model.Security.PasskeyOutcomes.ErrorCode(start.Outcome), title: ApiPasskeys.TitleFor(start.Outcome),
					statusCode: ApiPasskeys.StatusFor(start.Outcome));

			var result = new Models.v4.Passkeys.PasskeyCeremonyResult
			{
				Data = new Models.v4.Passkeys.PasskeyCeremonyResultData { RequestId = start.RequestId, Options = ApiPasskeys.Options(start.OptionsJson) },
				PageSize = 1,
				Status = ResponseHelper.Success
			};
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>Verifies the passkey assertion and returns a <c>passkey</c> grant (the same <see cref="StepUpResult"/> as a code).</summary>
		[HttpPost("VerifyPasskey")]
		[AllowDuringDepartmentLock]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize]
		public async Task<ActionResult<StepUpResult>> VerifyPasskey([FromBody] AdpPasskeyStepUpInput input, System.Threading.CancellationToken cancellationToken)
		{
			if (string.IsNullOrWhiteSpace(input?.RequestId) || input.Credential == null)
				return Problem(type: "invalid_request", title: "A passkey response is required.", statusCode: StatusCodes.Status400BadRequest);

			var user = await _userManager.FindByIdAsync(UserId);
			if (user == null)
				return Problem(type: "protected_access_denied", title: "User not found.", statusCode: StatusCodes.Status401Unauthorized);
			if (await _userManager.IsLockedOutAsync(user))
				return Problem(type: "too_many_attempts", title: "Too many failed attempts. Wait a few minutes and try again.",
					statusCode: StatusCodes.Status429TooManyRequests);

			// A passkey is not a guessable secret: its challenge carries its own attempt limit, as at Mfa/VerifyStepUp.
			return Grant(await _adpStepUp.CompletePasskeyAsync(Caller(user), input.RequestId, ApiPasskeys.CredentialJson(input.Credential), cancellationToken));
		}

		/// <summary>
		/// Uses an approved Responder request (<c>MfaApproval/Request</c>, purpose <c>adp</c>, from this session and department)
		/// once, and returns a <c>passkey_approval</c> grant. Answers 409 <c>approval_pending</c> until it is decided.
		/// </summary>
		[HttpPost("CompleteApproval")]
		[AllowDuringDepartmentLock]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize]
		public async Task<ActionResult<StepUpResult>> CompleteApproval([FromBody] AdpApprovalStepUpInput input, System.Threading.CancellationToken cancellationToken)
		{
			if (string.IsNullOrWhiteSpace(input?.ApprovalRequestId))
				return Problem(type: "invalid_request", title: "An approval request is required.", statusCode: StatusCodes.Status400BadRequest);

			return Grant(await _adpStepUp.CompleteApprovalAsync(Caller(await _userManager.FindByIdAsync(UserId)), input.ApprovalRequestId, cancellationToken));
		}

		/// <summary>
		/// Redeems a brokered provider step-up (<c>Sso/Begin</c>, purpose <c>adp_step_up</c>) once and returns a <c>federated</c>
		/// grant, where the department accepts its provider's MFA for protected data (passkey plan section 7.8).
		/// </summary>
		[HttpPost("CompleteFederated")]
		[AllowDuringDepartmentLock]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize]
		public async Task<ActionResult<StepUpResult>> CompleteFederated([FromBody] AdpFederatedStepUpInput input, System.Threading.CancellationToken cancellationToken)
		{
			if (string.IsNullOrWhiteSpace(input?.SsoTransactionId) || string.IsNullOrWhiteSpace(input.SsoCode))
				return Problem(type: "invalid_request", title: "A provider step-up is required.", statusCode: StatusCodes.Status400BadRequest);

			return Grant(await _adpStepUp.CompleteFederatedAsync(Caller(await _userManager.FindByIdAsync(UserId)), input.SsoTransactionId, input.SsoCode,
				input.CodeVerifier, cancellationToken));
		}

		/// <summary>The caller as the grant issuer sees it: this user, department and validated session (never client-supplied).</summary>
		private Model.Security.AdpStepUpCaller Caller(Model.Identity.IdentityUser user = null) => new()
		{
			UserId = UserId,
			UserName = user?.UserName,
			DepartmentId = DepartmentId,
			Session = HttpProtectedGrantContext.SessionOf(HttpContext),
			LegacySessionId = User.FindFirst(Model.Security.SessionClaimTypes.SessionId)?.Value,
			ClientApplication = int.TryParse(User.FindFirst(Model.Security.SessionClaimTypes.ClientApp)?.Value, out var client)
				? (UserSessionClientApplication)client
				: UserSessionClientApplication.Api,
			AccountAuthenticationGeneration = user?.AuthenticationGeneration ?? 0,
			EvidenceSessionKey = ApiStepUpEvidence.SessionKey(HttpContext),
			IpAddress = IpAddressHelper.GetRequestIP(Request, true),
			AuditSystem = SystemAuditSystems.Api
		};

		private ActionResult<StepUpResult> Grant(Model.Security.AdpGrantIssue issued)
		{
			if (!issued.Succeeded)
				return Problem(type: issued.ErrorCode, title: issued.Outcome switch
				{
					Model.Security.AdpGrantOutcome.NotConfigured => "Protected data grants are not configured.",
					Model.Security.AdpGrantOutcome.SessionRequired => "Sign in again to verify for protected data.",
					Model.Security.AdpGrantOutcome.MethodNotAllowed => "That verification method is not available for protected data here.",
					Model.Security.AdpGrantOutcome.StepUpRequired => "Verify again to view protected data.",
					Model.Security.AdpGrantOutcome.ApprovalPending => "The request has not been approved yet.",
					Model.Security.AdpGrantOutcome.ApprovalUnavailable => "The approval could not be used. Request it again.",
					Model.Security.AdpGrantOutcome.CredentialRevoked => "The credential used was removed or changed. Verify again.",
					Model.Security.AdpGrantOutcome.VerificationFailed => "The verification failed.",
					Model.Security.AdpGrantOutcome.InvalidRequest => "The request is not valid.",
					_ => "Protected data is temporarily unavailable. Try again."
				}, statusCode: Model.Security.AdpGrantOutcomes.StatusFor(issued.Outcome));

			var result = new StepUpResult
			{
				GrantId = issued.GrantId,
				GrantToken = issued.Token,
				StepUpExpiresOnUtc = issued.ExpiresOnUtc.ToString("O"),
				StepUpWindowMinutes = issued.WindowMinutes,
				PageSize = 1,
				Status = ResponseHelper.Success
			};
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>
		/// Queues enrollment (Disabled -> EnrollmentQueued). Managing member only; requires an active
		/// paid ADP addon and an open global admission gate, both re-verified server-side, and an
		/// acknowledgement record for the current version: <c>{"version": "ADP-ACK-2",
		/// "acknowledgedItems": [every key], "lockConsent": true}</c>, with the version and keys from
		/// Capabilities. Anything less is refused with <c>acknowledgements_incomplete</c>.
		/// </summary>
		[HttpPost("QueueEnrollment")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize]
		public async Task<ActionResult<EnrollmentCommandResult>> QueueEnrollment([FromBody] QueueEnrollmentInput input)
		{
			var mfaProblem = await RequireRecentMfaAsync();
			if (mfaProblem != null)
				return mfaProblem;

			var outcome = await _dataProtectionService.QueueEnrollmentAsync(DepartmentId, UserId,
				input?.AcknowledgementsJson, input?.WindowStartLocal, input?.WindowEndLocal, input?.WindowTimeZone);
			return await MapCommandOutcomeAsync(outcome);
		}

		/// <summary>Dequeues a not-yet-started enrollment (EnrollmentQueued -> Disabled). Managing member only.</summary>
		[HttpPost("CancelQueuedEnrollment")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize]
		public async Task<ActionResult<EnrollmentCommandResult>> CancelQueuedEnrollment()
		{
			var mfaProblem = await RequireRecentMfaAsync();
			if (mfaProblem != null)
				return mfaProblem;

			var outcome = await _dataProtectionService.CancelQueuedEnrollmentAsync(DepartmentId, UserId);
			return await MapCommandOutcomeAsync(outcome);
		}

		/// <summary>
		/// Revokes a scheduled offboarding (OffboardingScheduled -> Enabled) before the first
		/// offboarding window opens. Managing member only. Allowed during a department lock: the
		/// revoke window closes when offboarding execution starts, and this command must not be
		/// blocked by an unrelated migration window.
		/// </summary>
		[HttpPost("RevokeOffboarding")]
		[AllowDuringDepartmentLock]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize]
		public async Task<ActionResult<EnrollmentCommandResult>> RevokeOffboarding()
		{
			var mfaProblem = await RequireRecentMfaAsync();
			if (mfaProblem != null)
				return mfaProblem;

			var outcome = await _dataProtectionService.RevokeOffboardingAsync(DepartmentId, UserId);
			return await MapCommandOutcomeAsync(outcome);
		}

		/// <summary>
		/// MFA-recency gate for enrollment, offboarding and security commands (ADP plan sections 3.5 and 18, passkey plan
		/// section 7.6 row 10 and section 8.4): actual MFA within the sensitive-operation window (5 minutes), shown either
		/// by Mfa/VerifyStepUp evidence on this session (operation <c>adp_management</c>) or by a Protected Data Grant in
		/// the X-Resgrid-Protected-Grant header whose own verification is that recent, bound to THIS user, session and
		/// department at the CURRENT policy epoch. The department's longer data-access window never stretches this: an
		/// eight-hour-old grant cannot change the protection lifecycle. Missing grant key material is not proof either;
		/// the evidence path still works without it. Returns null when the command may proceed.
		///
		/// A step-up-EXEMPT grant is refused here. Those are minted by RequestGrant without any second
		/// factor, for a client the department exempted from the reveal prompt (plan 3.3) — that
		/// exemption covers reading protected values, not running enrollment or offboarding. Accepting
		/// one would let an exempt client change the department's protection lifecycle with a password
		/// alone, which is exactly what this gate exists to stop.
		/// </summary>
		private async Task<ActionResult> RequireRecentMfaAsync()
		{
			var window = Model.Security.MfaStepUpOperations.WindowFor(Model.Security.MfaStepUpOperations.AdpManagement);
			if (await ApiStepUpEvidence.HasRecentSecondFactorAsync(_mfaEvidence, _mfaPolicy, UserId, HttpContext, DepartmentId,
					Model.Security.MfaMethodScope.Adp, window))
				return null;

			if (_grantService.CanValidateGrants)
			{
				var token = Request.Headers[GrantHeader].ToString();
				var policy = await _dataProtectionService.GetPolicyByDepartmentIdAsync(DepartmentId);
				var outcome = _grantService.ValidateGrant(token, DepartmentId, policy?.PolicyEpoch ?? 0,
					requiredScope: null, out var grant);

				var now = DateTime.UtcNow;
				if (outcome == ProtectedDataGrantValidationOutcome.Valid &&
					!grant.StepUpExempt &&
					await Resgrid.Services.ProtectedGrantBinding.CheckAsync(grant, UserId, HttpProtectedGrantContext.SessionOf(HttpContext), policy?.StepUpWindowMinutes,
						_credentialStates) == Resgrid.Model.Security.ProtectedGrantBindingOutcome.Bound &&
					grant.MfaAtUtc <= now.AddSeconds(30) && now - grant.MfaAtUtc <= window)
					return null;
			}

			return Problem(type: "step_up_required",
				title: "Recent multi-factor verification is required for this command. Verify a second factor (Mfa/VerifyStepUp, operation adp_management) and retry.",
				statusCode: StatusCodes.Status403Forbidden);
		}

		private async Task<ActionResult<EnrollmentCommandResult>> MapCommandOutcomeAsync(DepartmentDataProtectionEnrollmentResult outcome)
		{
			switch (outcome)
			{
				case DepartmentDataProtectionEnrollmentResult.Queued:
					var state = await _dataProtectionService.GetStateAsync(DepartmentId, bypassCache: true);
					var result = new EnrollmentCommandResult
					{
						Outcome = outcome.ToString(),
						State = (int)state
					};
					result.PageSize = 1;
					result.Status = ResponseHelper.Success;
					ResponseHelper.PopulateV4ResponseData(result);
					return result;

				case DepartmentDataProtectionEnrollmentResult.NotManagingMember:
					return Problem(type: "protected_access_denied",
						title: "Only the department's managing member may run this command.",
						statusCode: StatusCodes.Status403Forbidden);

				case DepartmentDataProtectionEnrollmentResult.AddonRequired:
					return Problem(type: "addon_required",
						title: "An active Advanced Data Protection addon is required.",
						statusCode: StatusCodes.Status409Conflict);

				case DepartmentDataProtectionEnrollmentResult.PlanRequired:
					return Problem(type: "plan_required",
						title: "Advanced Data Protection requires a paid plan.",
						statusCode: StatusCodes.Status409Conflict);

				case DepartmentDataProtectionEnrollmentResult.FeatureNotAvailable:
					return Problem(type: "feature_not_available",
						title: "Advanced Data Protection enrollment is temporarily unavailable.",
						statusCode: StatusCodes.Status409Conflict);

				case DepartmentDataProtectionEnrollmentResult.InvalidState:
					return Problem(type: "invalid_state",
						title: "The department's protection state does not permit this command.",
						statusCode: StatusCodes.Status409Conflict);

				case DepartmentDataProtectionEnrollmentResult.InvalidWindow:
					return Problem(type: "invalid_window",
						title: "A valid migration window time zone is required (select one in the wizard, or set the department time zone).",
						statusCode: StatusCodes.Status400BadRequest);

				case DepartmentDataProtectionEnrollmentResult.AcknowledgementsIncomplete:
					return Problem(type: "acknowledgements_incomplete",
						title: "Every Advanced Data Protection disclosure item in the current acknowledgement version must be acknowledged, with lock consent (see Capabilities).",
						statusCode: StatusCodes.Status400BadRequest);

				default:
					return Problem(type: "command_failed",
						title: "The command could not be completed; it may be retried.",
						statusCode: StatusCodes.Status500InternalServerError);
			}
		}
	}
}
