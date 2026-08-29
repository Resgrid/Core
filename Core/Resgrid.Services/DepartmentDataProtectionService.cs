using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Advanced Data Protection policy/state orchestration. See
	/// <see cref="IDepartmentDataProtectionService"/> for the contract. Enforces the enrollment gates
	/// server-side (managing member, paid plan, active ADP addon, fresh authoritative global-gate
	/// evaluation) and owns the queue/cancel/offboarding-schedule edges of the durable state machine;
	/// every transition beyond those is made by the ADP migration worker. No cryptography here.
	/// </summary>
	public class DepartmentDataProtectionService : IDepartmentDataProtectionService
	{
		private const string PolicyCacheKey = "AdpPolicy_{0}";
		private const string EgressCacheKey = "AdpEgress_{0}";
		private static readonly TimeSpan CacheLength = TimeSpan.FromMinutes(5);

		private readonly IDepartmentDataProtectionPolicyRepository _policyRepository;
		private readonly IDepartmentProtectedDataEgressPolicyRepository _egressPolicyRepository;
		private readonly IDepartmentsService _departmentsService;
		private readonly IFeatureToggleService _featureToggleService;
		private readonly ISubscriptionsService _subscriptionsService;
		private readonly ICacheProvider _cacheProvider;
		private readonly IProtectedFieldCatalog _fieldCatalog;

		public DepartmentDataProtectionService(IDepartmentDataProtectionPolicyRepository policyRepository,
			IDepartmentProtectedDataEgressPolicyRepository egressPolicyRepository, IDepartmentsService departmentsService,
			IFeatureToggleService featureToggleService, ISubscriptionsService subscriptionsService,
			ICacheProvider cacheProvider, IProtectedFieldCatalog fieldCatalog)
		{
			_policyRepository = policyRepository;
			_egressPolicyRepository = egressPolicyRepository;
			_departmentsService = departmentsService;
			_featureToggleService = featureToggleService;
			_subscriptionsService = subscriptionsService;
			_cacheProvider = cacheProvider;
			_fieldCatalog = fieldCatalog;
		}

		public async Task<int> GetPinnedCatalogVersionAsync(int departmentId)
		{
			var policy = await GetPolicyByDepartmentIdAsync(departmentId);
			return policy?.CatalogVersion ?? 0;
		}

		public async Task<bool> IsCatalogUpgradePendingAsync(int departmentId)
		{
			if (!await ShouldEncryptNewWritesAsync(departmentId))
				return false;

			var pinned = await GetPinnedCatalogVersionAsync(departmentId);

			// A pinned version of zero on an encrypting department means the policy row lost its
			// catalog stamp — treat it as owed an upgrade rather than silently current.
			return pinned < _fieldCatalog.Version;
		}

		public async Task<DepartmentDataProtectionPolicy> GetPolicyByDepartmentIdAsync(int departmentId, bool bypassCache = false)
		{
			async Task<DepartmentDataProtectionPolicy> getPolicy()
			{
				return await _policyRepository.GetByDepartmentIdAsync(departmentId);
			}

			if (!bypassCache && Config.SystemBehaviorConfig.CacheEnabled)
			{
				var cached = await _cacheProvider.RetrieveAsync<DepartmentDataProtectionPolicy>(
					string.Format(PolicyCacheKey, departmentId), getPolicy, CacheLength);

				// Guard against blank-entity cache poisoning; an empty payload must read as "no policy".
				if (cached == null || cached.DepartmentDataProtectionPolicyId <= 0)
					return null;

				return cached;
			}

			return await getPolicy();
		}

		public async Task<DepartmentDataProtectionState> GetStateAsync(int departmentId, bool bypassCache = false)
		{
			var policy = await GetPolicyByDepartmentIdAsync(departmentId, bypassCache);
			return policy == null ? DepartmentDataProtectionState.Disabled : (DepartmentDataProtectionState)policy.State;
		}

		public async Task<bool> ShouldEncryptNewWritesAsync(int departmentId)
		{
			var policy = await GetPolicyByDepartmentIdAsync(departmentId);
			if (policy == null)
				return false;

			switch ((DepartmentDataProtectionState)policy.State)
			{
				case DepartmentDataProtectionState.Encrypting:
				case DepartmentDataProtectionState.Enabled:
				case DepartmentDataProtectionState.Rotating:
				case DepartmentDataProtectionState.OffboardingScheduled:
					return true;

				case DepartmentDataProtectionState.Verifying:
					// Enrollment/rotation verification still encrypts; offboarding verification is past
					// the decrypt pass and new writes stay plaintext.
					return policy.ActiveMigrationKind != (int)DepartmentDataProtectionMigrationKind.Offboarding;

				case DepartmentDataProtectionState.Failed:
					// A failed run resumes from its cursor. Failures on the enrollment/rotation side keep
					// encrypting so the migrated portion never regresses; failures while offboarding keep
					// writing plaintext so the decrypt backlog only shrinks.
					return policy.ActiveMigrationKind != (int)DepartmentDataProtectionMigrationKind.Offboarding;

				default:
					return false;
			}
		}

		public async Task<bool> IsProtectionEnforcedAsync(int departmentId)
		{
			var state = await GetStateAsync(departmentId);
			if (state == DepartmentDataProtectionState.Enabled
				|| state == DepartmentDataProtectionState.Rotating
				|| state == DepartmentDataProtectionState.OffboardingScheduled)
				return true;

			// A CATALOG UPGRADE runs through the Encrypting state on a department that is ALREADY
			// protected: its corpus is fully enveloped and only the newly cataloged fields are being
			// swept. Enforcement must stay on for the duration — dropping it would hand rgdp
			// ciphertext straight to clients through the unenforced read path. (Enrollment's
			// Encrypting is different: nothing is encrypted yet, so there is nothing to enforce.)
			if (state == DepartmentDataProtectionState.Encrypting || state == DepartmentDataProtectionState.Verifying)
			{
				var policy = await GetPolicyByDepartmentIdAsync(departmentId);
				return policy?.ActiveMigrationKind == (int)DepartmentDataProtectionMigrationKind.CatalogUpgrade;
			}

			// A FAILED run always enforces, whatever it was doing when it stopped. Every failure
			// leaves envelopes at rest: enrollment and catalog upgrade fail part-way through a
			// sweep, rotation fails over an already-enveloped corpus, and offboarding fails with
			// the decrypt pass incomplete. ShouldEncryptNewWritesAsync already keeps encrypting in
			// this state for everything but offboarding, so the two would otherwise disagree —
			// writes producing envelopes that reads hand straight to clients.
			//
			// The cost of being wrong in the other direction is a department seeing REDACTED for
			// values that happen to still be plaintext, which is recoverable; serving ciphertext,
			// or worse the plaintext behind it, is not.
			if (state == DepartmentDataProtectionState.Failed)
				return true;

			return false;
		}

		public async Task<DepartmentDataProtectionEnrollmentResult> QueueEnrollmentAsync(int departmentId, string requestingUserId,
			string acknowledgementsJson, string windowStartLocal, string windowEndLocal, string windowTimeZone,
			CancellationToken cancellationToken = default)
		{
			try
			{
				var managingCheck = await VerifyManagingMemberAsync(departmentId, requestingUserId);
				if (managingCheck != null)
					return managingCheck.Value;

				// Paid plan required; the Billing API can return null (empty payload) — treat as free.
				var plan = await _subscriptionsService.GetCurrentPlanForDepartmentAsync(departmentId, byPassCache: true);
				if (plan == null || plan.Cost <= 0)
					return DepartmentDataProtectionEnrollmentResult.PlanRequired;

				if (!await HasActiveAdpAddonAsync(departmentId))
					return DepartmentDataProtectionEnrollmentResult.AddonRequired;

				// Fresh authoritative global-gate evaluation, performed immediately before commit.
				// Amended 2026-08-26: the flag is a global admission switch — no targeting, no
				// percentage rollout, no department overrides. Any error fails closed.
				FeatureFlag gate;
				try
				{
					gate = await _featureToggleService.GetFlagByKeyAsync(FeatureFlagKeys.DepartmentProtectedDataEnrollment, bypassCache: true);
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"ADP enrollment gate evaluation failed for department {departmentId}; denying enrollment (fail closed)");
					return DepartmentDataProtectionEnrollmentResult.FeatureNotAvailable;
				}

				if (gate == null || gate.IsArchived || !gate.IsEnabledGlobally)
					return DepartmentDataProtectionEnrollmentResult.FeatureNotAvailable;

				var policy = await _policyRepository.GetByDepartmentIdAsync(departmentId);
				if (policy != null && (DepartmentDataProtectionState)policy.State != DepartmentDataProtectionState.Disabled)
					return DepartmentDataProtectionEnrollmentResult.InvalidState;

				// A queued migration whose window time zone never resolves would wait forever (the
				// worker reads an unresolvable zone as closed). Resolve NOW and persist the
				// canonical id: the explicit wizard selection first, then the department's own time
				// zone; neither resolvable = reject rather than queue a permanent stall.
				string resolvedWindowTimeZone;
				if (!string.IsNullOrWhiteSpace(windowTimeZone))
				{
					if (!TryResolveWindowTimeZone(windowTimeZone, out resolvedWindowTimeZone))
						return DepartmentDataProtectionEnrollmentResult.InvalidWindow;
				}
				else
				{
					var department = await _departmentsService.GetDepartmentByIdAsync(departmentId);
					if (!TryResolveWindowTimeZone(department?.TimeZone, out resolvedWindowTimeZone))
						return DepartmentDataProtectionEnrollmentResult.InvalidWindow;
				}

				var utcNow = DateTime.UtcNow;
				var evaluationRecord = JsonConvert.SerializeObject(new
				{
					flagKey = FeatureFlagKeys.DepartmentProtectedDataEnrollment,
					isEnabledGlobally = gate.IsEnabledGlobally,
					evaluationSource = "GetFlagByKeyAsync(bypassCache)",
					evaluatedOnUtc = utcNow,
					requestingUserId,
					correlationId = Guid.NewGuid().ToString("N")
				});

				if (policy == null)
				{
					policy = new DepartmentDataProtectionPolicy
					{
						DepartmentId = departmentId,
						State = (int)DepartmentDataProtectionState.EnrollmentQueued,
						ActiveMigrationKind = (int)DepartmentDataProtectionMigrationKind.Enrollment,
						StepUpWindowMinutes = Config.DataProtectionConfig.StepUpWindowDefaultMinutes,
						AcknowledgementsJson = acknowledgementsJson,
						AcknowledgedByUserId = requestingUserId,
						AcknowledgedOn = utcNow,
						EnrollmentFlagEvaluationJson = evaluationRecord,
						MigrationWindowStartLocal = string.IsNullOrWhiteSpace(windowStartLocal) ? Config.DataProtectionConfig.MigrationWindowDefaultStartLocal : windowStartLocal,
						MigrationWindowEndLocal = string.IsNullOrWhiteSpace(windowEndLocal) ? Config.DataProtectionConfig.MigrationWindowDefaultEndLocal : windowEndLocal,
						MigrationWindowTimeZone = resolvedWindowTimeZone,
						CreatedOn = utcNow,
						CreatedByUserId = requestingUserId
					};

					// The unique DepartmentId index turns a concurrent double-enroll into a DbException
					// on one side; that caller re-reads a non-Disabled row and reports InvalidState.
					try
					{
						await _policyRepository.InsertAsync(policy, cancellationToken);
					}
					catch (Exception ex)
					{
						// Logged so operators can tell a lost enroll race from a real fault
						// (connectivity, mapping) that also lands here.
						Logging.LogException(ex, $"ADP enrollment insert failed for department {departmentId}; reporting InvalidState");
						await InvalidateProtectionCacheAsync(departmentId);
						return DepartmentDataProtectionEnrollmentResult.InvalidState;
					}
				}
				else
				{
					// State already verified Disabled above; the CAS transition still closes the race
					// against a concurrent enroll command that committed since that read.
					var rows = await _policyRepository.TryTransitionStateAsync(departmentId,
						DepartmentDataProtectionState.Disabled, DepartmentDataProtectionState.EnrollmentQueued,
						(int)DepartmentDataProtectionMigrationKind.Enrollment, requestingUserId, cancellationToken);
					if (rows == 0)
						return DepartmentDataProtectionEnrollmentResult.InvalidState;

					policy.State = (int)DepartmentDataProtectionState.EnrollmentQueued;
					policy.ActiveMigrationKind = (int)DepartmentDataProtectionMigrationKind.Enrollment;
					policy.AcknowledgementsJson = acknowledgementsJson;
					policy.AcknowledgedByUserId = requestingUserId;
					policy.AcknowledgedOn = utcNow;
					policy.EnrollmentFlagEvaluationJson = evaluationRecord;
					policy.MigrationWindowStartLocal = string.IsNullOrWhiteSpace(windowStartLocal) ? Config.DataProtectionConfig.MigrationWindowDefaultStartLocal : windowStartLocal;
					policy.MigrationWindowEndLocal = string.IsNullOrWhiteSpace(windowEndLocal) ? Config.DataProtectionConfig.MigrationWindowDefaultEndLocal : windowEndLocal;
					policy.MigrationWindowTimeZone = resolvedWindowTimeZone;
					policy.UpdatedOn = utcNow;
					policy.UpdatedByUserId = requestingUserId;
					await _policyRepository.SaveOrUpdateAsync(policy, cancellationToken);
				}

				await InvalidateProtectionCacheAsync(departmentId);
				return DepartmentDataProtectionEnrollmentResult.Queued;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"ADP QueueEnrollmentAsync failed for department {departmentId}");
				return DepartmentDataProtectionEnrollmentResult.Failed;
			}
		}

		public async Task<DepartmentDataProtectionEnrollmentResult> CancelQueuedEnrollmentAsync(int departmentId,
			string requestingUserId, CancellationToken cancellationToken = default)
		{
			var managingCheck = await VerifyManagingMemberAsync(departmentId, requestingUserId);
			if (managingCheck != null)
				return managingCheck.Value;

			var rows = await _policyRepository.TryTransitionStateAsync(departmentId,
				DepartmentDataProtectionState.EnrollmentQueued, DepartmentDataProtectionState.Disabled,
				null, requestingUserId, cancellationToken);

			await InvalidateProtectionCacheAsync(departmentId);
			return rows > 0 ? DepartmentDataProtectionEnrollmentResult.Queued : DepartmentDataProtectionEnrollmentResult.InvalidState;
		}

		public async Task<DepartmentDataProtectionEnrollmentResult> ScheduleOffboardingAsync(int departmentId,
			DepartmentDataProtectionOffboardingSource source, DateTime effectiveOnUtc,
			CancellationToken cancellationToken = default)
		{
			try
			{
				var policy = await _policyRepository.GetByDepartmentIdAsync(departmentId);
				if (policy == null)
					return DepartmentDataProtectionEnrollmentResult.InvalidState;

				// A cancellation while still queued simply dequeues at no data cost.
				if ((DepartmentDataProtectionState)policy.State == DepartmentDataProtectionState.EnrollmentQueued)
				{
					var dequeued = await _policyRepository.TryTransitionStateAsync(departmentId,
						DepartmentDataProtectionState.EnrollmentQueued, DepartmentDataProtectionState.Disabled,
						null, "system:billing", cancellationToken);
					await InvalidateProtectionCacheAsync(departmentId);
					return dequeued > 0 ? DepartmentDataProtectionEnrollmentResult.Queued : DepartmentDataProtectionEnrollmentResult.InvalidState;
				}

				// Mid-enrollment cancellations are NOT scheduled here: the enrollment completes to
				// Enabled first (plan section 21.3) and the worker then re-applies the pending billing
				// state. Only a plain Enabled department schedules offboarding.
				var rows = await _policyRepository.TryTransitionStateAsync(departmentId,
					DepartmentDataProtectionState.Enabled, DepartmentDataProtectionState.OffboardingScheduled,
					null, "system:billing", cancellationToken);
				if (rows == 0)
					return DepartmentDataProtectionEnrollmentResult.InvalidState;

				var updated = await _policyRepository.GetByDepartmentIdAsync(departmentId);
				if (updated != null)
				{
					updated.OffboardingEffectiveOn = effectiveOnUtc;
					updated.OffboardingSource = (int)source;
					updated.UpdatedOn = DateTime.UtcNow;
					updated.UpdatedByUserId = "system:billing";
					await _policyRepository.SaveOrUpdateAsync(updated, cancellationToken);
				}

				await InvalidateProtectionCacheAsync(departmentId);
				return DepartmentDataProtectionEnrollmentResult.Queued;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"ADP ScheduleOffboardingAsync failed for department {departmentId}");
				return DepartmentDataProtectionEnrollmentResult.Failed;
			}
		}

		public async Task<DepartmentDataProtectionEnrollmentResult> RevokeOffboardingAsync(int departmentId,
			string requestingUserId, CancellationToken cancellationToken = default)
		{
			var managingCheck = await VerifyManagingMemberAsync(departmentId, requestingUserId);
			if (managingCheck != null)
				return managingCheck.Value;

			var rows = await _policyRepository.TryTransitionStateAsync(departmentId,
				DepartmentDataProtectionState.OffboardingScheduled, DepartmentDataProtectionState.Enabled,
				null, requestingUserId, cancellationToken);
			if (rows == 0)
			{
				await InvalidateProtectionCacheAsync(departmentId);
				return DepartmentDataProtectionEnrollmentResult.InvalidState;
			}

			var policy = await _policyRepository.GetByDepartmentIdAsync(departmentId);
			if (policy != null)
			{
				policy.OffboardingEffectiveOn = null;
				policy.OffboardingSource = null;
				policy.UpdatedOn = DateTime.UtcNow;
				policy.UpdatedByUserId = requestingUserId;
				await _policyRepository.SaveOrUpdateAsync(policy, cancellationToken);
			}

			await InvalidateProtectionCacheAsync(departmentId);
			return DepartmentDataProtectionEnrollmentResult.Queued;
		}

		public async Task<DepartmentProtectedDataEgressPolicy> GetEgressPolicyByDepartmentIdAsync(int departmentId, bool bypassCache = false)
		{
			async Task<DepartmentProtectedDataEgressPolicy> getEgressPolicy()
			{
				return await _egressPolicyRepository.GetByDepartmentIdAsync(departmentId);
			}

			DepartmentProtectedDataEgressPolicy policy;
			if (!bypassCache && Config.SystemBehaviorConfig.CacheEnabled)
			{
				policy = await _cacheProvider.RetrieveAsync<DepartmentProtectedDataEgressPolicy>(
					string.Format(EgressCacheKey, departmentId), getEgressPolicy, CacheLength);

				if (policy != null && policy.DepartmentProtectedDataEgressPolicyId <= 0)
					policy = null;
			}
			else
			{
				policy = await getEgressPolicy();
			}

			// No row = the fail-safe defaults: every channel GenericOnly.
			return policy ?? new DepartmentProtectedDataEgressPolicy
			{
				DepartmentId = departmentId,
				PushMode = (int)ProtectedDataEgressMode.GenericOnly,
				EmailMode = (int)ProtectedDataEgressMode.GenericOnly,
				SmsMode = (int)ProtectedDataEgressMode.GenericOnly,
				VoiceMode = (int)ProtectedDataEgressMode.GenericOnly,
				PinChallengeExpiryMinutes = 5,
				PinMaxAttempts = 3,
				PinLockoutMinutes = 15
			};
		}

		public async Task<DepartmentProtectedDataEgressPolicy> SaveEgressPolicyAsync(DepartmentProtectedDataEgressPolicy policy,
			string updatedByUserId, CancellationToken cancellationToken = default)
		{
			if (policy == null)
				throw new ArgumentNullException(nameof(policy));

			// ProtectedAfterPin is a two-step SMS/voice release; push and email have no PIN interaction.
			if (policy.PushMode == (int)ProtectedDataEgressMode.ProtectedAfterPin ||
				policy.EmailMode == (int)ProtectedDataEgressMode.ProtectedAfterPin)
				throw new ArgumentException("ProtectedAfterPin is only valid for the SMS and voice channels.", nameof(policy));

			// Any mode that can emit protected content off-app (plan sections 9.1 and 12) requires
			// the versioned administrator acknowledgement to be recorded on the policy — otherwise
			// an unacknowledged save silently enables protected egress.
			var emitsProtectedContent =
				policy.PushMode == (int)ProtectedDataEgressMode.AllowProtectedContent ||
				policy.EmailMode == (int)ProtectedDataEgressMode.AllowProtectedContent ||
				policy.SmsMode == (int)ProtectedDataEgressMode.AllowProtectedContent ||
				policy.VoiceMode == (int)ProtectedDataEgressMode.AllowProtectedContent ||
				policy.SmsMode == (int)ProtectedDataEgressMode.ProtectedAfterPin ||
				policy.VoiceMode == (int)ProtectedDataEgressMode.ProtectedAfterPin;
			if (emitsProtectedContent &&
				(string.IsNullOrWhiteSpace(policy.AcknowledgementVersion) || string.IsNullOrWhiteSpace(policy.AcknowledgedByUserId)))
				throw new ArgumentException(
					"Egress modes that emit protected content require a recorded, versioned administrator acknowledgement.",
					nameof(policy));

			var utcNow = DateTime.UtcNow;
			if (policy.DepartmentProtectedDataEgressPolicyId <= 0)
				policy.CreatedOn = utcNow;
			policy.UpdatedOn = utcNow;
			policy.UpdatedByUserId = updatedByUserId;

			var saved = await _egressPolicyRepository.SaveOrUpdateAsync(policy, cancellationToken);

			// Egress changes revoke outstanding grants and force send-time re-evaluation.
			await IncrementPolicyEpochAsync(policy.DepartmentId, updatedByUserId, cancellationToken);
			await InvalidateProtectionCacheAsync(policy.DepartmentId);

			return saved;
		}

		public async Task<AdpEnrollmentPreflight> GetEnrollmentPreflightAsync(int departmentId, string requestingUserId,
			CancellationToken cancellationToken = default)
		{
			var preflight = new AdpEnrollmentPreflight();

			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId);
			preflight.IsManagingMember = department != null && !string.IsNullOrWhiteSpace(requestingUserId) &&
				string.Equals(department.ManagingUserId, requestingUserId, StringComparison.OrdinalIgnoreCase);

			try
			{
				var plan = await _subscriptionsService.GetCurrentPlanForDepartmentAsync(departmentId, byPassCache: true);
				preflight.HasPaidPlan = plan != null && plan.Cost > 0;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"ADP preflight plan lookup failed for department {departmentId}; reporting no paid plan");
			}

			preflight.HasActiveAddon = await HasActiveAdpAddonAsync(departmentId);

			try
			{
				var gate = await _featureToggleService.GetFlagByKeyAsync(FeatureFlagKeys.DepartmentProtectedDataEnrollment, bypassCache: true);
				preflight.GateOpen = gate != null && !gate.IsArchived && gate.IsEnabledGlobally;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"ADP preflight gate evaluation failed for department {departmentId}; reporting closed");
			}

			preflight.StateAllowsEnrollment = await GetStateAsync(departmentId, bypassCache: true) == DepartmentDataProtectionState.Disabled;

			return preflight;
		}

		public async Task<long> IncrementPolicyEpochAsync(int departmentId, string updatedByUserId, CancellationToken cancellationToken = default)
		{
			var epoch = await _policyRepository.IncrementPolicyEpochAsync(departmentId, updatedByUserId, cancellationToken);
			await InvalidateProtectionCacheAsync(departmentId);
			return epoch;
		}

		public async Task InvalidateProtectionCacheAsync(int departmentId)
		{
			await _cacheProvider.RemoveAsync(string.Format(PolicyCacheKey, departmentId));
			await _cacheProvider.RemoveAsync(string.Format(EgressCacheKey, departmentId));
		}

		/// <summary>
		/// Resolves a wizard-supplied or department time zone to its canonical system id. The worker
		/// evaluates windows with TimeZoneInfo.FindSystemTimeZoneById, so only ids that resolve
		/// there may ever be persisted on the policy row.
		/// </summary>
		private static bool TryResolveWindowTimeZone(string timeZoneId, out string resolvedId)
		{
			resolvedId = null;
			if (string.IsNullOrWhiteSpace(timeZoneId))
				return false;

			try
			{
				resolvedId = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId.Trim()).Id;
				return true;
			}
			catch (TimeZoneNotFoundException)
			{
				return false;
			}
			catch (InvalidTimeZoneException)
			{
				return false;
			}
		}

		private async Task<DepartmentDataProtectionEnrollmentResult?> VerifyManagingMemberAsync(int departmentId, string requestingUserId)
		{
			if (string.IsNullOrWhiteSpace(requestingUserId))
				return DepartmentDataProtectionEnrollmentResult.NotManagingMember;

			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId);
			if (department == null)
				return DepartmentDataProtectionEnrollmentResult.Failed;

			// Only the managing member — holders of ManageDepartmentDataProtection are deliberately
			// NOT sufficient for enrollment/offboarding/billing commands (plan decision 15).
			if (!string.Equals(department.ManagingUserId, requestingUserId, StringComparison.OrdinalIgnoreCase))
				return DepartmentDataProtectionEnrollmentResult.NotManagingMember;

			return null;
		}

		private async Task<bool> HasActiveAdpAddonAsync(int departmentId)
		{
			// Provider-level addon resolution (Stripe today; the Billing API DepartmentBillingSummary
			// ADP block replaces this in workstream A1). Null-safe: a missing/errored billing response
			// reads as "no addon" — fail closed for enrollment.
			try
			{
				var addons = await _subscriptionsService.GetCurrentPlanAddonsForDepartmentFromStripeAsync(departmentId);
				if (addons == null)
					return false;

				return addons.Any(a => a != null && a.AddonType == (int)PlanAddonTypes.ADP && !a.IsCancelled);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"ADP addon lookup failed for department {departmentId}; treating as no active addon");
				return false;
			}
		}
	}
}
