using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Newtonsoft.Json;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Models.DataProtection;
using Resgrid.Web.Attributes;
using Resgrid.Web.Filters;
using Resgrid.Web.Helpers;
using IdentityUser = Resgrid.Model.Identity.IdentityUser;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// Advanced Data Protection status page and Enrollment Wizard (plan sections 3.5, 12, 18).
	/// The page renders per the section 3.5 state table (wizard only for a Disabled department with
	/// an open gate and active addon; queue/progress/offboarding controls otherwise). Every command
	/// is server-enforced regardless of what the page showed: managing member only, active paid
	/// addon, fresh global-gate evaluation (inside QueueEnrollmentAsync), per-operation MFA step-up
	/// (RequiresRecentTwoFactor), and antiforgery. Acknowledgements are versioned and validated
	/// server-side against the full section 12 item list — a client that omits one cannot enroll.
	/// </summary>
	[Area("User")]
	[Authorize]
	public class DataProtectionController : SecureBaseController
	{
		/// <summary>Version stamp recorded with every acknowledgement set; bump when section 12 text changes.</summary>
		public const string AcknowledgementVersion = "ADP-ACK-1";

		/// <summary>
		/// The section 12 disclosure items. Item KEYS are stable identifiers recorded in the policy's
		/// acknowledgement JSON; the wizard renders matching text and the queue action refuses any
		/// submission that does not acknowledge every key.
		/// </summary>
		public static readonly IReadOnlyList<string> AckItems = new[]
		{
			"catalog_scope",
			"plaintext_metadata",
			"authorized_server_access",
			"step_up_window",
			"bigboard_reduction",
			"workflow_redaction",
			"default_egress",
			"search_report_limitations",
			"migration_disable",
			"key_loss_support",
			"not_hipaa_compliance"
		};

		private const int StepUpMaxAttempts = 5;
		private static readonly TimeSpan StepUpAttemptWindow = TimeSpan.FromMinutes(5);

		private readonly IDepartmentDataProtectionService _dataProtectionService;
		private readonly IDepartmentLockService _departmentLockService;
		private readonly IAdpSizingService _sizingService;
		private readonly IProtectedDataBrokerClient _brokerClient;
		private readonly IDepartmentsService _departmentsService;
		private readonly UserManager<IdentityUser> _userManager;
		private readonly IProtectedDataGrantService _grantService;
		private readonly ICacheProvider _cacheProvider;
		private readonly IEventAggregator _eventAggregator;

		public DataProtectionController(IDepartmentDataProtectionService dataProtectionService,
			IDepartmentLockService departmentLockService, IAdpSizingService sizingService,
			IProtectedDataBrokerClient brokerClient, IDepartmentsService departmentsService,
			UserManager<IdentityUser> userManager, IProtectedDataGrantService grantService,
			ICacheProvider cacheProvider, IEventAggregator eventAggregator)
		{
			_eventAggregator = eventAggregator;
			_dataProtectionService = dataProtectionService;
			_departmentLockService = departmentLockService;
			_sizingService = sizingService;
			_brokerClient = brokerClient;
			_departmentsService = departmentsService;
			_userManager = userManager;
			_grantService = grantService;
			_cacheProvider = cacheProvider;
		}

		public async Task<IActionResult> Index()
		{
			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
				return Unauthorized();

			var model = new DataProtectionIndexView();

			var policy = await _dataProtectionService.GetPolicyByDepartmentIdAsync(DepartmentId, bypassCache: true);
			model.State = policy == null ? DepartmentDataProtectionState.Disabled : (DepartmentDataProtectionState)policy.State;
			model.MigrationWindowStartLocal = policy?.MigrationWindowStartLocal;
			model.MigrationWindowEndLocal = policy?.MigrationWindowEndLocal;
			model.MigrationWindowTimeZone = policy?.MigrationWindowTimeZone;
			model.OffboardingEffectiveOn = policy?.OffboardingEffectiveOn?.ToString("f");

			model.Preflight = await _dataProtectionService.GetEnrollmentPreflightAsync(DepartmentId, UserId);
			model.IsManagingMember = model.Preflight.IsManagingMember;

			model.IsDepartmentLocked = await _departmentLockService.IsDepartmentLockedAsync(DepartmentId);
			if (model.IsDepartmentLocked)
				model.LockReason = (await _departmentLockService.GetActiveLockAsync(DepartmentId))?.Reason;

			model.BrokerHealthy = await _brokerClient.IsHealthyAsync();

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			if (department != null && !string.IsNullOrWhiteSpace(department.ManagingUserId))
			{
				var managingUser = await _userManager.FindByIdAsync(department.ManagingUserId);
				model.ManagingMemberHasMfa = managingUser != null && await _userManager.GetTwoFactorEnabledAsync(managingUser);
			}

			model.StepUpExemptClients = ((AdpStepUpExemptClients)(policy?.StepUpExemptClients ?? 0)).Sanitize();

			model.DefaultWindowStart = Config.DataProtectionConfig.MigrationWindowDefaultStartLocal;
			model.DefaultWindowEnd = Config.DataProtectionConfig.MigrationWindowDefaultEndLocal;
			model.TimeZones = TimeZoneInfo.GetSystemTimeZones()
				.Select(tz => new SelectListItem
				{
					Value = tz.Id,
					Text = tz.DisplayName,
					Selected = string.Equals(tz.Id, department?.TimeZone, StringComparison.OrdinalIgnoreCase)
				})
				.ToList();

			return View(model);
		}

		/// <summary>Wizard step 5: read-only sizing scan and the P50–P90 estimate (plan 18.2).</summary>
		[HttpGet]
		public async Task<IActionResult> SizingScan(int windowMinutes, CancellationToken cancellationToken)
		{
			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
				return Unauthorized();

			var minutes = windowMinutes is > 0 and <= 24 * 60 ? windowMinutes : 8 * 60;
			var result = await _sizingService.RunSizingScanAsync(DepartmentId, minutes, cancellationToken);
			return Json(result);
		}

		/// <summary>
		/// Row-count progress for the status panel while a migration is in flight. Value-free: row
		/// counts and table names, never a value out of any row. Read-only, so it stays a GET and
		/// is polled by the status panel.
		/// </summary>
		[HttpGet]
		public async Task<IActionResult> MigrationProgress(CancellationToken cancellationToken)
		{
			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
				return Unauthorized();

			return Json(await _dataProtectionService.GetMigrationProgressAsync(DepartmentId, cancellationToken));
		}

		/// <summary>
		/// Wizard step 8: final confirmation and queueing. The acknowledgement record persisted on
		/// the policy embeds the version, every acknowledged item, the lock consent, and a FRESH
		/// server-side sizing scan (the client-shown estimate is advisory; the record's numbers are
		/// authoritative). QueueEnrollmentAsync re-verifies managing member, paid plan, active
		/// addon, the global gate and the window time zone at commit.
		/// </summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		[RequiresRecentTwoFactor(RequireForOperation = true)]
		public async Task<IActionResult> QueueEnrollment([FromForm] QueueEnrollmentInputModel input,
			CancellationToken cancellationToken)
		{
			if (input == null)
				return BadRequest();

			var missing = AckItems.Where(item => input.AcknowledgedItems == null ||
				!input.AcknowledgedItems.Contains(item, StringComparer.Ordinal)).ToList();
			if (missing.Count > 0)
				return Json(new { success = false, error = "acknowledgements_incomplete" });

			if (!input.LockConsent)
				return Json(new { success = false, error = "lock_consent_required" });

			AdpSizingResult sizing = null;
			try
			{
				sizing = await _sizingService.RunSizingScanAsync(DepartmentId, 8 * 60, cancellationToken);
			}
			catch (Exception ex)
			{
				// The record survives without an estimate; the worker re-runs sizing on execution night.
				Framework.Logging.LogException(ex, $"ADP wizard sizing scan failed for department {DepartmentId} at queue time");
			}

			var acknowledgementsJson = JsonConvert.SerializeObject(new
			{
				version = AcknowledgementVersion,
				acknowledgedItems = AckItems,
				lockConsent = true,
				acknowledgedOnUtc = DateTime.UtcNow,
				sizing
			});

			var outcome = await _dataProtectionService.QueueEnrollmentAsync(DepartmentId, UserId,
				acknowledgementsJson, input.WindowStartLocal, input.WindowEndLocal, input.WindowTimeZone,
				cancellationToken);

			return MapOutcome(outcome);
		}

		/// <summary>Dequeues a not-yet-started enrollment at no data cost. Managing member only (service-enforced).</summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		[AllowDuringDepartmentLock]
		[RequiresRecentTwoFactor(RequireForOperation = true)]
		public async Task<IActionResult> CancelQueuedEnrollment(CancellationToken cancellationToken)
		{
			var outcome = await _dataProtectionService.CancelQueuedEnrollmentAsync(DepartmentId, UserId, cancellationToken);
			return MapOutcome(outcome);
		}

		/// <summary>
		/// Revokes a scheduled offboarding before the first offboarding window opens. Allowed during
		/// a department lock — the revoke window must not be blocked by an unrelated migration night.
		/// </summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		[AllowDuringDepartmentLock]
		[RequiresRecentTwoFactor(RequireForOperation = true)]
		public async Task<IActionResult> RevokeOffboarding(CancellationToken cancellationToken)
		{
			var outcome = await _dataProtectionService.RevokeOffboardingAsync(DepartmentId, UserId, cancellationToken);
			return MapOutcome(outcome);
		}

		/// <summary>
		/// Verifies the caller's authenticator (TOTP) code for the ADP step-up (plan section 3) and,
		/// with signing key material configured, mints a Protected Data Grant. Mirrors the v4 endpoint:
		/// the web client holds the token in JS MEMORY ONLY (never a cookie, localStorage, or the URL),
		/// conceals values at expiry, and prompts again on the next reveal. Rate limited per user; the
		/// code is never logged. Allowed during a department lock — step-up is a read-side control.
		/// </summary>
		/// <summary>
		/// Replaces the department's per-app step-up exemptions (plan 3.3).
		///
		/// Requires a fresh second factor to change — you have to prove one to switch one off. That is
		/// not ceremony: without it, anyone who walked up to a signed-in session could quietly remove
		/// the control that would have stopped them, and the first sign would be plaintext on screen.
		///
		/// Audited with the before and after mask. The service enforces managing-member only and bumps
		/// the policy epoch so outstanding grants issued under the previous setting stop working.
		/// </summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		[RequiresRecentTwoFactor(RequireForOperation = true)]
		public async Task<IActionResult> SaveStepUpExemptions([FromForm] int exemptions, CancellationToken cancellationToken)
		{
			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
				return Unauthorized();

			var before = await _dataProtectionService.GetStepUpExemptClientsAsync(DepartmentId, bypassCache: true);
			var requested = ((AdpStepUpExemptClients)exemptions).Sanitize();

			var outcome = await _dataProtectionService.SetStepUpExemptClientsAsync(DepartmentId, requested,
				UserId, cancellationToken);

			// Audited whatever the outcome: a REFUSED attempt to weaken this is at least as
			// interesting as a successful one.
			var auditEvent = new AuditEvent
			{
				DepartmentId = DepartmentId,
				UserId = UserId,
				Type = AuditLogTypes.DataProtectionStepUpExemptionsChanged,
				Before = before.ToString(),
				After = requested.ToString(),
				Successful = outcome == DepartmentDataProtectionEnrollmentResult.Queued,
				IpAddress = IpAddressHelper.GetRequestIP(Request, true),
				ServerName = Environment.MachineName,
				UserAgent = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}"
			};
			_eventAggregator.SendMessage<AuditEvent>(auditEvent);

			return MapOutcome(outcome);
		}

		/// <summary>
		/// Issues a grant WITHOUT a second factor, but only for a client the department has explicitly
		/// exempted (plan 3.3). The client calls this first and falls back to the step-up modal when
		/// it is refused, so the prompt appears exactly where the department left it switched on.
		///
		/// This never weakens VerifyStepUp and never bypasses anything else: the caller is still an
		/// authenticated member of the department, the grant is still tenant-bound, epoch-bound and
		/// short-lived, and every read it authorizes is still audited. What is skipped is only the
		/// second factor — and the grant records that it was skipped.
		/// </summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		[AllowDuringDepartmentLock]
		public async Task<IActionResult> RequestGrant()
		{
			if (await _dataProtectionService.IsStepUpRequiredForClientAsync(DepartmentId, UserSessionClientApplication.Web))
				return Json(new { success = false, error = "step_up_required" });

			if (!_grantService.CanIssueGrants)
				return Json(new { success = false, error = "grants_not_configured" });

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
				ClientApp = (int)UserSessionClientApplication.Web,
				PolicyEpoch = policy?.PolicyEpoch ?? 0,
				WindowMinutes = windowMinutes,
				Scopes = new[] { ProtectedDataGrantScopes.Read, ProtectedDataGrantScopes.Write },
				MfaAtUtc = DateTime.UtcNow,
				StepUpExempt = true
			});

			return Json(new
			{
				success = true,
				grantToken = issued.Token,
				grantId = issued.GrantId,
				expiresOnUtc = issued.ExpiresOnUtc.ToString("O"),
				windowMinutes
			});
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[AllowDuringDepartmentLock]
		public async Task<IActionResult> VerifyStepUp([FromForm] string code)
		{
			if (string.IsNullOrWhiteSpace(code))
				return Json(new { success = false, error = "invalid_totp" });

			var attempts = await _cacheProvider.IncrementAsync($"AdpStepUpAttempts_{UserId}", StepUpAttemptWindow);
			if (attempts > StepUpMaxAttempts)
				return Json(new { success = false, error = "too_many_attempts" });

			var user = await _userManager.FindByIdAsync(UserId);
			if (user == null)
				return Json(new { success = false, error = "protected_access_denied" });

			if (!await _userManager.GetTwoFactorEnabledAsync(user))
				return Json(new { success = false, error = "mfa_not_enrolled" });

			var valid = await _userManager.VerifyTwoFactorTokenAsync(user,
				_userManager.Options.Tokens.AuthenticatorTokenProvider, code.Trim());
			if (!valid)
				return Json(new { success = false, error = "invalid_totp" });

			var policy = await _dataProtectionService.GetPolicyByDepartmentIdAsync(DepartmentId);
			var windowMinutes = policy?.StepUpWindowMinutes > 0
				? policy.StepUpWindowMinutes
				: Config.DataProtectionConfig.StepUpWindowDefaultMinutes;
			windowMinutes = Math.Min(Math.Max(1, windowMinutes), Math.Max(1, Config.DataProtectionConfig.StepUpMaximumMinutes));

			if (!_grantService.CanIssueGrants)
				return Json(new { success = false, error = "grants_not_configured" });

			var issued = _grantService.IssueGrant(new ProtectedDataGrantIssueRequest
			{
				UserId = UserId,
				DepartmentId = DepartmentId,
				SessionId = User.FindFirst(Model.Security.SessionClaimTypes.SessionId)?.Value,
				ClientApp = (int)UserSessionClientApplication.Web,
				PolicyEpoch = policy?.PolicyEpoch ?? 0,
				WindowMinutes = windowMinutes,
				Scopes = new[] { ProtectedDataGrantScopes.Read, ProtectedDataGrantScopes.Write },
				MfaAtUtc = DateTime.UtcNow
			});

			return Json(new
			{
				success = true,
				grantToken = issued.Token,
				grantId = issued.GrantId,
				expiresOnUtc = issued.ExpiresOnUtc.ToString("O"),
				windowMinutes
			});
		}

		private IActionResult MapOutcome(DepartmentDataProtectionEnrollmentResult outcome)
		{
			if (outcome == DepartmentDataProtectionEnrollmentResult.Queued)
				return Json(new { success = true });

			// Value-free codes matching the v4 API's problem types; the wizard maps them to text.
			var error = outcome switch
			{
				DepartmentDataProtectionEnrollmentResult.NotManagingMember => "protected_access_denied",
				DepartmentDataProtectionEnrollmentResult.AddonRequired => "addon_required",
				DepartmentDataProtectionEnrollmentResult.PlanRequired => "plan_required",
				DepartmentDataProtectionEnrollmentResult.FeatureNotAvailable => "feature_not_available",
				DepartmentDataProtectionEnrollmentResult.InvalidState => "invalid_state",
				DepartmentDataProtectionEnrollmentResult.InvalidWindow => "invalid_window",
				_ => "command_failed"
			};

			return Json(new { success = false, error });
		}
	}
}
