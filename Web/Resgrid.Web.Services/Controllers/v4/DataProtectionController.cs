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
	/// denied), active paid ADP addon, and a fresh authoritative global-gate evaluation performed by
	/// the service immediately before commit. MFA-recency enforcement joins these commands when the
	/// Protected Data Grant service ships (Phase 2); until then the wizard flow gates purchase in
	/// Core Web.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class DataProtectionController : V4AuthenticatedApiControllerbase
	{
		// TOTP step-up brute-force limiter: attempts per user inside the window before 429.
		private const int StepUpMaxAttempts = 5;
		private static readonly TimeSpan StepUpAttemptWindow = TimeSpan.FromMinutes(5);

		private readonly IDepartmentDataProtectionService _dataProtectionService;
		private readonly IDepartmentLockService _departmentLockService;
		private readonly IProtectedFieldCatalog _protectedFieldCatalog;
		private readonly IDepartmentsService _departmentsService;
		private readonly IFeatureToggleService _featureToggleService;
		private readonly UserManager<Model.Identity.IdentityUser> _userManager;
		private readonly ICacheProvider _cacheProvider;

		public DataProtectionController(IDepartmentDataProtectionService dataProtectionService,
			IDepartmentLockService departmentLockService, IProtectedFieldCatalog protectedFieldCatalog,
			IDepartmentsService departmentsService, IFeatureToggleService featureToggleService,
			UserManager<Model.Identity.IdentityUser> userManager, ICacheProvider cacheProvider)
		{
			_dataProtectionService = dataProtectionService;
			_departmentLockService = departmentLockService;
			_protectedFieldCatalog = protectedFieldCatalog;
			_departmentsService = departmentsService;
			_featureToggleService = featureToggleService;
			_userManager = userManager;
			_cacheProvider = cacheProvider;
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

			var result = new StepUpResult
			{
				GrantId = null,
				StepUpExpiresOnUtc = DateTime.UtcNow.AddMinutes(windowMinutes).ToString("O"),
				StepUpWindowMinutes = windowMinutes
			};
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
			var outcome = await _dataProtectionService.RevokeOffboardingAsync(DepartmentId, UserId);
			return await MapCommandOutcomeAsync(outcome);
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

				default:
					return Problem(type: "command_failed",
						title: "The command could not be completed; it may be retried.",
						statusCode: StatusCodes.Status500InternalServerError);
			}
		}
	}
}
