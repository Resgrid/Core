using System;
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

		public DataProtectionController(IDepartmentDataProtectionService dataProtectionService,
			IDepartmentLockService departmentLockService, IProtectedFieldCatalog protectedFieldCatalog,
			IDepartmentsService departmentsService, IFeatureToggleService featureToggleService,
			UserManager<Model.Identity.IdentityUser> userManager, ICacheProvider cacheProvider,
			IProtectedDataGrantService grantService)
		{
			_dataProtectionService = dataProtectionService;
			_departmentLockService = departmentLockService;
			_protectedFieldCatalog = protectedFieldCatalog;
			_departmentsService = departmentsService;
			_featureToggleService = featureToggleService;
			_userManager = userManager;
			_cacheProvider = cacheProvider;
			_grantService = grantService;
		}

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
					LockProjectedEndUtc = isLocked ? activeLock?.ProjectedEndUtc?.ToString("O") : null
				}
			};

			result.PageSize = 1;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>
		/// Verifies the caller's authenticator (TOTP) code for the ADP step-up (plan section 3).
		/// Success returns the absolute expiry of the step-up window — clients hold it in memory
		/// only, conceal protected values at expiry, and prompt again on the next reveal/edit.
		/// Refreshing an access token never refreshes this window. Allowed during a department lock:
		/// step-up is a read-side control and reads continue while locked. Attempts are rate limited
		/// per user; the code is never logged.
		/// </summary>
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
			var clientApp = int.TryParse(User.FindFirst(Model.Security.SessionClaimTypes.ClientApp)?.Value, out var parsed)
				? (UserSessionClientApplication)parsed
				: UserSessionClientApplication.Api;

			if (await _dataProtectionService.IsStepUpRequiredForClientAsync(DepartmentId, clientApp))
				return Problem(type: "step_up_required",
					title: "This department requires second-factor verification before protected values are shown.",
					statusCode: StatusCodes.Status401Unauthorized);

			if (!_grantService.CanIssueGrants)
				return Problem(type: "grants_not_configured", title: "Protected data grants are not configured.",
					statusCode: StatusCodes.Status503ServiceUnavailable);

			var policy = await _dataProtectionService.GetPolicyByDepartmentIdAsync(DepartmentId);
			var windowMinutes = policy?.StepUpWindowMinutes > 0
				? policy.StepUpWindowMinutes
				: Config.DataProtectionConfig.StepUpWindowDefaultMinutes;
			windowMinutes = Math.Min(Math.Max(1, windowMinutes), Math.Max(1, Config.DataProtectionConfig.StepUpMaximumMinutes));

			var issued = _grantService.IssueGrant(new ProtectedDataGrantIssueRequest
			{
				UserId = UserId,
				DepartmentId = DepartmentId,
				SessionId = User.FindFirst(Model.Security.SessionClaimTypes.SessionId)?.Value,
				ClientApp = (int)clientApp,
				PolicyEpoch = policy?.PolicyEpoch ?? 0,
				WindowMinutes = windowMinutes,
				Scopes = new[] { ProtectedDataGrantScopes.Read, ProtectedDataGrantScopes.Write },
				MfaAtUtc = DateTime.UtcNow,
				StepUpExempt = true
			});

			var exemptResult = new StepUpResult
			{
				GrantId = issued.GrantId,
				GrantToken = issued.Token,
				StepUpExpiresOnUtc = issued.ExpiresOnUtc.ToString("O"),
				StepUpWindowMinutes = windowMinutes,
				PageSize = 1,
				Status = ResponseHelper.Success
			};

			ResponseHelper.PopulateV4ResponseData(exemptResult);
			return exemptResult;
		}

		[HttpPost("VerifyStepUp")]
		[AllowDuringDepartmentLock]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize]
		public async Task<ActionResult<StepUpResult>> VerifyStepUp([FromBody] VerifyStepUpInput input)
		{
			if (string.IsNullOrWhiteSpace(input?.Code))
				return Problem(type: "invalid_totp", title: "A verification code is required.",
					statusCode: StatusCodes.Status400BadRequest);

			// Brute-force limiter (fail open on cache faults: lockout also guards below via TOTP
			// time-step; a cache outage must not disable step-up entirely).
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

			var valid = await _userManager.VerifyTwoFactorTokenAsync(user,
				_userManager.Options.Tokens.AuthenticatorTokenProvider, input.Code.Trim());
			if (!valid)
				return Problem(type: "invalid_totp",
					title: "The verification code is invalid or has expired.",
					statusCode: StatusCodes.Status401Unauthorized);

			var policy = await _dataProtectionService.GetPolicyByDepartmentIdAsync(DepartmentId);
			var windowMinutes = policy?.StepUpWindowMinutes > 0
				? policy.StepUpWindowMinutes
				: Config.DataProtectionConfig.StepUpWindowDefaultMinutes;

			// Same clamp IssueGrant applies: the advertised window must never exceed the grant's
			// actual lifetime, or clients would keep protected values visible past expiry.
			windowMinutes = Math.Min(Math.Max(1, windowMinutes), Math.Max(1, Config.DataProtectionConfig.StepUpMaximumMinutes));

			var result = new StepUpResult
			{
				GrantId = null,
				StepUpExpiresOnUtc = DateTime.UtcNow.AddMinutes(windowMinutes).ToString("O"),
				StepUpWindowMinutes = windowMinutes
			};

			// When signing key material is configured (identity tier), the verification mints a real
			// Protected Data Grant bound to user, department, session, client app, policy epoch and
			// this moment's MFA. Without it, the response keeps the pre-broker shape (null grant).
			if (_grantService.CanIssueGrants)
			{
				var issued = _grantService.IssueGrant(new ProtectedDataGrantIssueRequest
				{
					UserId = UserId,
					DepartmentId = DepartmentId,
					SessionId = User.FindFirst(Model.Security.SessionClaimTypes.SessionId)?.Value,
					ClientApp = int.TryParse(User.FindFirst(Model.Security.SessionClaimTypes.ClientApp)?.Value, out var clientApp)
						? clientApp
						: (int)UserSessionClientApplication.Api,
					PolicyEpoch = policy?.PolicyEpoch ?? 0,
					WindowMinutes = windowMinutes,
					Scopes = new[] { ProtectedDataGrantScopes.Read, ProtectedDataGrantScopes.Write },
					MfaAtUtc = DateTime.UtcNow
				});

				result.GrantId = issued.GrantId;
				result.GrantToken = issued.Token;
				result.StepUpExpiresOnUtc = issued.ExpiresOnUtc.ToString("O");
			}
			result.PageSize = 1;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>
		/// Queues enrollment (Disabled -> EnrollmentQueued). Managing member only; requires an active
		/// paid ADP addon and an open global admission gate, both re-verified server-side.
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
		/// MFA-recency gate for enrollment/offboarding commands (plan sections 3.5 and 18): the
		/// caller must present a currently-valid Protected Data Grant — minted by VerifyStepUp after
		/// fresh TOTP, absolute lifetime = the department step-up window — in the
		/// X-Resgrid-Protected-Grant header, bound to THIS user and department at the CURRENT policy
		/// epoch. On deployments without grant key material (CanValidateGrants false) the gate is
		/// inactive and the pre-Phase-2 gates (managing member, addon, global flag) stand alone.
		/// Returns null when the command may proceed.
		/// </summary>
		private async Task<ActionResult> RequireRecentMfaAsync()
		{
			if (!_grantService.CanValidateGrants)
				return null;

			var token = Request.Headers[GrantHeader].ToString();
			var policy = await _dataProtectionService.GetPolicyByDepartmentIdAsync(DepartmentId);
			var outcome = _grantService.ValidateGrant(token, DepartmentId, policy?.PolicyEpoch ?? 0,
				requiredScope: null, out var grant);

			if (outcome != ProtectedDataGrantValidationOutcome.Valid ||
				!string.Equals(grant.UserId, UserId, StringComparison.OrdinalIgnoreCase))
				return Problem(type: "step_up_required",
					title: "Recent multi-factor verification is required for this command. Verify your authenticator code and retry with the issued grant.",
					statusCode: StatusCodes.Status403Forbidden);

			return null;
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

				default:
					return Problem(type: "command_failed",
						title: "The command could not be completed; it may be retried.",
						statusCode: StatusCodes.Status500InternalServerError);
			}
		}
	}
}
