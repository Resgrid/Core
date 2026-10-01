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
using Resgrid.Chatbot.Interfaces;
using Resgrid.Chatbot.NLU;
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
		/// <summary>Version stamp recorded with every acknowledgement set (AdpEnrollmentAcknowledgements.Version).</summary>
		public const string AcknowledgementVersion = AdpEnrollmentAcknowledgements.Version;

		/// <summary>
		/// The section 12 disclosure items (AdpEnrollmentAcknowledgements.Items, shared with the v4 API and the command
		/// gate). The wizard renders one checkbox per key and the queue action refuses any submission that omits one.
		/// </summary>
		public static IReadOnlyList<string> AckItems => AdpEnrollmentAcknowledgements.Items;

		private const int StepUpMaxAttempts = 5;
		private static readonly TimeSpan StepUpAttemptWindow = TimeSpan.FromMinutes(5);

		private readonly IDepartmentDataProtectionService _dataProtectionService;
		private readonly IDepartmentLockService _departmentLockService;
		private readonly IAdpSizingService _sizingService;
		private readonly IProtectedDataBrokerClient _brokerClient;
		private readonly IDepartmentsService _departmentsService;
		private readonly UserManager<IdentityUser> _userManager;
		private readonly IProtectedDataGrantService _grantService;
		private readonly IAdpReleaseService _adpRelease;
		private readonly Resgrid.Model.Repositories.IAdpAccessStore _adpAccess;
		private readonly Resgrid.Model.Repositories.IAdpAuditRepository _adpAudit;
		private readonly ICacheProvider _cacheProvider;
		private readonly IEventAggregator _eventAggregator;
		private readonly IProtectedWorkflowService _protectedWorkflows;
		private readonly IChatbotDepartmentConfigService _chatbotConfig;

		public DataProtectionController(IDepartmentDataProtectionService dataProtectionService,
			IDepartmentLockService departmentLockService, IAdpSizingService sizingService,
			IProtectedDataBrokerClient brokerClient, IDepartmentsService departmentsService,
			UserManager<IdentityUser> userManager, IProtectedDataGrantService grantService, IAdpReleaseService adpRelease, Resgrid.Model.Repositories.IAdpAccessStore adpAccess, Resgrid.Model.Repositories.IAdpAuditRepository adpAudit,
			ICacheProvider cacheProvider, IEventAggregator eventAggregator, IProtectedWorkflowService protectedWorkflows,
			IChatbotDepartmentConfigService chatbotConfig, IMfaEvidenceService mfaEvidence, IMfaActivityService mfaActivity,
			IAdpStepUpService adpStepUp, IMfaCredentialStateService credentialStates, IMfaApprovalService approvals)
		{
			_approvals = approvals;
			_adpStepUp = adpStepUp;
			_credentialStates = credentialStates;
			_mfaActivity = mfaActivity;
			_chatbotConfig = chatbotConfig;
			_mfaEvidence = mfaEvidence;
			_protectedWorkflows = protectedWorkflows;
			_eventAggregator = eventAggregator;
			_dataProtectionService = dataProtectionService;
			_departmentLockService = departmentLockService;
			_sizingService = sizingService;
			_brokerClient = brokerClient;
			_departmentsService = departmentsService;
			_userManager = userManager;
			_grantService = grantService;
			_adpRelease = adpRelease;
			_adpAccess = adpAccess;
			_adpAudit = adpAudit;
			_cacheProvider = cacheProvider;
		}

		private readonly IMfaEvidenceService _mfaEvidence;
		private readonly IMfaActivityService _mfaActivity;
		private readonly IAdpStepUpService _adpStepUp;
		private readonly IMfaApprovalService _approvals;
		private readonly IMfaCredentialStateService _credentialStates;

		[HttpGet]
		public async Task<IActionResult> ReleaseSettings()
		{
			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin()) return Unauthorized();
			var egress = await _dataProtectionService.GetEgressPolicyByDepartmentIdAsync(DepartmentId, bypassCache: true);
			var consent = await _adpAccess.GetAsync(AdpSupportConsent.Key(DepartmentId));
			return View(new AdpReleaseSettingsView { SmsMode = egress.SmsMode, VoiceMode = egress.VoiceMode,
				SupportEnabled = consent != null && Newtonsoft.Json.JsonConvert.DeserializeObject<AdpSupportConsent>(consent.Json).Enabled });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> SaveReleaseSettings(int smsMode, int voiceMode, bool supportEnabled, bool acknowledged, string grantToken)
		{
			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin() || !await _protectedWorkflows.CanAdministerAsync(DepartmentId, UserId) || smsMode is < 0 or > 2 || voiceMode is < 0 or > 2 ||
				!acknowledged && (smsMode != 0 || voiceMode != 0 || supportEnabled)) return Unauthorized();
			var policy = await _dataProtectionService.GetPolicyByDepartmentIdAsync(DepartmentId, bypassCache: true);
			if (policy == null || _grantService.ValidateGrant(grantToken, DepartmentId, policy.PolicyEpoch, ProtectedDataGrantScopes.Read,
				out var grant) != ProtectedDataGrantValidationOutcome.Valid || grant.UserId != UserId ||
				await Resgrid.Services.ProtectedGrantBinding.CheckAsync(grant, UserId, HttpProtectedGrantContext.SessionOf(HttpContext), policy.StepUpWindowMinutes,
					_credentialStates) != Resgrid.Model.Security.ProtectedGrantBindingOutcome.Bound || grant.StepUpExempt ||
				grant.MfaAtUtc < DateTime.UtcNow.AddMinutes(-5) || grant.MfaAtUtc > DateTime.UtcNow.AddSeconds(30)) return Unauthorized();
			await _adpAudit.AppendAsync(new AdpAuditEvent { DepartmentId = DepartmentId, Layer = "application", Operation = "release-settings",
				Outcome = "requested", ActorId = UserId, PolicyEpoch = policy.PolicyEpoch });
			var previous = await _adpAccess.GetAsync(AdpSupportConsent.Key(DepartmentId));
			if (!await _adpAccess.SaveAsync(AdpSupportConsent.Key(DepartmentId), Newtonsoft.Json.JsonConvert.SerializeObject(
				new AdpSupportConsent { Enabled = supportEnabled, UserId = UserId, UpdatedUtc = DateTime.UtcNow }), previous?.Version ?? 0))
				return Conflict();
			var egress = await _dataProtectionService.GetEgressPolicyByDepartmentIdAsync(DepartmentId, bypassCache: true);
			egress.SmsMode = smsMode;
			egress.VoiceMode = voiceMode;
			if (acknowledged) { egress.AcknowledgementVersion = "pin-release-v1"; egress.AcknowledgedByUserId = UserId; egress.AcknowledgedOn = DateTime.UtcNow; }
			await _dataProtectionService.SaveEgressPolicyAsync(egress, UserId);
			return Json(new { success = true });
		}

		/// <summary>
		/// One verified page of the department's ADP audit chain. Page forward by passing the previous page's
		/// tailSequence and tailHash as afterSequence and afterHash; each page is verified against that anchor.
		/// </summary>
		[HttpGet]
		public async Task<IActionResult> AuditChain(long afterSequence = 0, string afterHash = null, int take = 500)
		{
			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin()) return Unauthorized();
			Response.Headers["Cache-Control"] = "no-store";
			if (afterSequence < 0 || afterSequence > 0 && string.IsNullOrWhiteSpace(afterHash)) return BadRequest();
			var anchorHash = afterSequence == 0 ? AdpAuditChain.Genesis : afterHash;
			take = Math.Clamp(take, 1, 1000);
			var read = await _adpAudit.ReadAsync(DepartmentId, afterSequence, take + 1);
			var rows = read.Take(take).ToList();
			var tail = rows.LastOrDefault();
			return Json(new { rows, afterSequence, afterHash = anchorHash, tailSequence = tail?.Sequence ?? afterSequence,
				tailHash = tail?.Hash ?? anchorHash, hasMore = read.Count > take, valid = AdpAuditChain.VerifySegment(rows, afterSequence, anchorHash) });
		}

		[HttpGet]
		public IActionResult Pin()
		{
			Response.Headers["Cache-Control"] = "no-store";
			return View();
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> SavePin([FromForm] string pin, [FromForm] string grantToken)
		{
			Response.Headers["Cache-Control"] = "no-store";
			return Json(new { success = await _adpRelease.EnrollPinAsync(DepartmentId, UserId, grantToken, pin) });
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

			// Enrollment turns off the department's own AI provider (EnhancedAiAccessService.GetOwnLlmProviderStatusAsync);
			// name a saved one so the wizard can say it stops being used. Only for a department that can still enroll.
			if (model.State == DepartmentDataProtectionState.Disabled)
			{
				try
				{
					var chatbot = await _chatbotConfig.GetConfigAsync(DepartmentId, bypassCache: true);
					if (!string.IsNullOrWhiteSpace(chatbot?.LlmApiEndpoint) && !string.IsNullOrWhiteSpace(chatbot.LlmApiKey))
					{
						var provider = LlmProviderCatalog.Infer(chatbot.LlmApiEndpoint);
						model.OwnAiProviderName = provider.Id != LlmProviderCatalog.CustomId ? provider.Name
							: Uri.TryCreate(chatbot.LlmApiEndpoint, UriKind.Absolute, out var uri) ? uri.Host : null;
					}
				}
				catch (Exception ex)
				{
					// The acknowledgement still covers it; a failed lookup only drops the named warning.
					Framework.Logging.LogException(ex);
				}
			}

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			if (department != null && !string.IsNullOrWhiteSpace(department.ManagingUserId))
			{
				var managingUser = await _userManager.FindByIdAsync(department.ManagingUserId);
				model.ManagingMemberHasMfa = managingUser != null && await _userManager.GetTwoFactorEnabledAsync(managingUser);
			}

			model.StepUpExemptClients = ((AdpStepUpExemptClients)(policy?.StepUpExemptClients ?? 0)).Sanitize();

			// ADP > Egress: Protected Workflows (the service enforces every rule again on save).
			model.ProtectedWorkflows = await _protectedWorkflows.GetDepartmentSettingsAsync(DepartmentId, bypassCache: true);
			model.CanAdministerProtectedWorkflows = await _protectedWorkflows.CanAdministerAsync(DepartmentId, UserId);

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
		[RequiresRecentTwoFactor(RequireForOperation = true, VerificationWindowMinutes = 5, MethodScope = Resgrid.Model.Security.MfaMethodScope.Adp)]
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
		[RequiresRecentTwoFactor(RequireForOperation = true, VerificationWindowMinutes = 5, MethodScope = Resgrid.Model.Security.MfaMethodScope.Adp)]
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
		[RequiresRecentTwoFactor(RequireForOperation = true, VerificationWindowMinutes = 5, MethodScope = Resgrid.Model.Security.MfaMethodScope.Adp)]
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
		[RequiresRecentTwoFactor(RequireForOperation = true, VerificationWindowMinutes = 5, MethodScope = Resgrid.Model.Security.MfaMethodScope.Adp)]
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
			// The exemption answer and the epoch stamped on the grant come from one policy snapshot inside the issuer, so a
			// revocation between two reads cannot leave a step-up-exempt grant alive after it.
			var caller = StepUpCaller(await _userManager.FindByIdAsync(UserId));
			var issued = await _adpStepUp.IssueExemptAsync(caller);
			if (issued.Outcome == Model.Security.AdpGrantOutcome.StepUpRequired)
			{
				// This session's own recent sign-in or unlock MFA, where the department accepts reusing it (plan section 9.1).
				var reused = await _adpStepUp.IssueFromRecentEvidenceAsync(caller);
				if (reused.Succeeded)
					issued = reused;
			}

			return GrantJson(issued);
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

			// ADP step-up shares the account lockout with sign-in and every other TOTP surface (passkey plan section 7.5 rule 6).
			if (await _userManager.IsLockedOutAsync(user))
				return Json(new { success = false, error = "too_many_attempts" });

			var valid = await _userManager.VerifyTwoFactorTokenAsync(user,
				_userManager.Options.Tokens.AuthenticatorTokenProvider, code.Trim());
			await _adpAudit.AppendAsync(new AdpAuditEvent { DepartmentId = DepartmentId, Layer = "identity",
				Operation = "mfa-verify", Outcome = valid ? "verified" : "denied", ActorId = UserId });
			if (!valid)
			{
				await _userManager.AccessFailedAsync(user);
				await _mfaActivity.RecordAsync(new Model.Security.MfaActivityEntry
				{
					UserId = user.Id, Method = Model.Security.MfaEvidenceMethod.Totp, Purpose = Model.Security.MfaEvidencePurpose.AdpStepUp, Successful = false,
					ClientApplication = UserSessionClientApplication.Web, DepartmentId = DepartmentId,
					SessionId = Model.Security.MfaEvidence.TrackedSessionId(MfaEvidenceSession.KeyFor(User, HttpContext))
				});
				return Json(new { success = false, error = "invalid_totp" });
			}

			await _userManager.ResetAccessFailedCountAsync(user);

			// The verified code becomes AdpStepUp evidence and, through the one issuer every method shares, a grant whose
			// expiry runs from this verification (passkey plan sections 8.1 and 9.2).
			return GrantJson(await _adpStepUp.IssueForTotpAsync(StepUpCaller(user), DateTime.UtcNow));
		}

		/// <summary>
		/// The ways this user can verify for this department's protected data now (passkey plan section 7.5 rule 5), for the
		/// reveal dialog: the methods it has that the department accepts, and which to show first. Advisory only.
		/// </summary>
		[HttpGet]
		[AllowDuringDepartmentLock]
		public async Task<IActionResult> StepUpMethods(CancellationToken cancellationToken)
		{
			var user = await _userManager.FindByIdAsync(UserId);
			if (user == null)
				return Json(new { success = false, error = "protected_access_denied" });

			var choice = await _adpStepUp.GetMethodChoiceAsync(StepUpCaller(user), await _userManager.GetTwoFactorEnabledAsync(user), cancellationToken);
			return Json(new
			{
				success = true,
				methods = choice.AllowedMethods.Where(choice.EnrolledMethods.Contains).ToList(),
				preferred = choice.Preferred
			});
		}

		/// <summary>Assertion options for a Web passkey, bound to this session and department's protected data.</summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		[AllowDuringDepartmentLock]
		public async Task<IActionResult> PasskeyOptions(CancellationToken cancellationToken)
		{
			var start = await _adpStepUp.BeginPasskeyAsync(StepUpCaller(await _userManager.FindByIdAsync(UserId)), cancellationToken);
			return start.Succeeded
				? Json(new { success = true, requestId = start.RequestId, options = start.OptionsJson })
				: Json(new { success = false, error = Model.Security.PasskeyOutcomes.ErrorCode(start.Outcome) });
		}

		/// <summary>Verifies the passkey and returns a <c>passkey</c> grant, held in the page's memory only.</summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		[AllowDuringDepartmentLock]
		public async Task<IActionResult> VerifyPasskey([FromForm] string requestId, [FromForm] string credential, CancellationToken cancellationToken)
		{
			if (string.IsNullOrWhiteSpace(requestId) || string.IsNullOrWhiteSpace(credential))
				return Json(new { success = false, error = "invalid_request" });

			return GrantJson(await _adpStepUp.CompletePasskeyAsync(StepUpCaller(await _userManager.FindByIdAsync(UserId)), requestId, credential,
				cancellationToken));
		}

		/// <summary>Asks the user's Responder to approve access to this department's protected data; returns the number to show.</summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		[AllowDuringDepartmentLock]
		public async Task<IActionResult> RequestApproval(CancellationToken cancellationToken)
		{
			var start = await _adpStepUp.RequestApprovalAsync(StepUpCaller(await _userManager.FindByIdAsync(UserId)), cancellationToken);
			return start.Succeeded
				? Json(new { success = true, approvalRequestId = start.ApprovalRequestId, matchNumber = start.MatchNumber, expiresIn = start.ExpiresInSeconds })
				: Json(new { success = false, error = Model.Security.MfaApprovalOutcomes.ErrorCode(start.Outcome) ?? "approval_unavailable" });
		}

		/// <summary>The state of this session's own approval request: pending, approved, denied, expired or canceled.</summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		[AllowDuringDepartmentLock]
		public async Task<IActionResult> ApprovalStatus([FromForm] string approvalRequestId, CancellationToken cancellationToken)
		{
			var session = HttpProtectedGrantContext.SessionOf(HttpContext);
			if (session == null)
				return Json(new { success = false, error = Model.Security.MfaApprovalOutcomes.ErrorCode(Model.Security.MfaApprovalOutcome.SessionRequired) });

			var found = await _approvals.GetForRequesterAsync(approvalRequestId, Model.Security.MfaApprovalRequesterKind.Session, session.SessionId,
				cancellationToken);
			return found.Succeeded
				? Json(new { success = true, state = Model.Security.MfaApprovalOutcomes.StateName(found.Request.EffectiveState(DateTime.UtcNow)) })
				: Json(new { success = false, error = Model.Security.MfaApprovalOutcomes.ErrorCode(found.Outcome) ?? "approval_unavailable" });
		}

		/// <summary>Uses the approved request once and returns a <c>passkey_approval</c> grant.</summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		[AllowDuringDepartmentLock]
		public async Task<IActionResult> CompleteApproval([FromForm] string approvalRequestId, CancellationToken cancellationToken)
		{
			if (string.IsNullOrWhiteSpace(approvalRequestId))
				return Json(new { success = false, error = "invalid_request" });

			return GrantJson(await _adpStepUp.CompleteApprovalAsync(StepUpCaller(await _userManager.FindByIdAsync(UserId)), approvalRequestId,
				cancellationToken));
		}

		/// <summary>The caller as the grant issuer sees it: this user, department and validated Web session (never client-supplied).</summary>
		private Model.Security.AdpStepUpCaller StepUpCaller(IdentityUser user = null) => new()
		{
			UserId = UserId,
			UserName = user?.UserName,
			DepartmentId = DepartmentId,
			Session = HttpProtectedGrantContext.SessionOf(HttpContext),
			LegacySessionId = User.FindFirst(Model.Security.SessionClaimTypes.SessionId)?.Value,
			ClientApplication = UserSessionClientApplication.Web,
			AccountAuthenticationGeneration = user?.AuthenticationGeneration ?? 0,
			EvidenceSessionKey = MfaEvidenceSession.KeyFor(User, HttpContext),
			IpAddress = IpAddressHelper.GetRequestIP(Request, true),
			AuditSystem = SystemAuditSystems.Website
		};

		/// <summary>The reveal module's JSON shape: a grant held in page memory only, or a value-free error code.</summary>
		private IActionResult GrantJson(Model.Security.AdpGrantIssue issued) =>
			issued.Succeeded
				? Json(new
				{
					success = true,
					grantToken = issued.Token,
					grantId = issued.GrantId,
					expiresOnUtc = issued.ExpiresOnUtc.ToString("O"),
					windowMinutes = issued.WindowMinutes
				})
				: Json(new { success = false, error = issued.ErrorCode });

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
				DepartmentDataProtectionEnrollmentResult.AcknowledgementsIncomplete => "acknowledgements_incomplete",
				_ => "command_failed"
			};

			return Json(new { success = false, error });
		}
	}
}
