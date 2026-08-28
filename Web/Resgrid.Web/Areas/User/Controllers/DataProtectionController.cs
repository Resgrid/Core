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

		private readonly IDepartmentDataProtectionService _dataProtectionService;
		private readonly IDepartmentLockService _departmentLockService;
		private readonly IAdpSizingService _sizingService;
		private readonly IProtectedDataBrokerClient _brokerClient;
		private readonly IDepartmentsService _departmentsService;
		private readonly UserManager<IdentityUser> _userManager;

		public DataProtectionController(IDepartmentDataProtectionService dataProtectionService,
			IDepartmentLockService departmentLockService, IAdpSizingService sizingService,
			IProtectedDataBrokerClient brokerClient, IDepartmentsService departmentsService,
			UserManager<IdentityUser> userManager)
		{
			_dataProtectionService = dataProtectionService;
			_departmentLockService = departmentLockService;
			_sizingService = sizingService;
			_brokerClient = brokerClient;
			_departmentsService = departmentsService;
			_userManager = userManager;
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
