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
		private readonly IDepartmentDataProtectionMigrationRepository _migrationRepository;
		private readonly IDepartmentLockService _departmentLockService;
		private readonly IDepartmentKeyService _keyService;

		public DepartmentDataProtectionService(IDepartmentDataProtectionPolicyRepository policyRepository,
			IDepartmentProtectedDataEgressPolicyRepository egressPolicyRepository, IDepartmentsService departmentsService,
			IFeatureToggleService featureToggleService, ISubscriptionsService subscriptionsService,
			ICacheProvider cacheProvider, IProtectedFieldCatalog fieldCatalog,
			IDepartmentDataProtectionMigrationRepository migrationRepository,
			IDepartmentLockService departmentLockService, IDepartmentKeyService keyService)
		{
			_departmentLockService = departmentLockService;
			_keyService = keyService;
			_policyRepository = policyRepository;
			_egressPolicyRepository = egressPolicyRepository;
			_departmentsService = departmentsService;
			_featureToggleService = featureToggleService;
			_subscriptionsService = subscriptionsService;
			_cacheProvider = cacheProvider;
			_fieldCatalog = fieldCatalog;
			_migrationRepository = migrationRepository;
		}

		public async Task<AdpMigrationProgress> GetMigrationProgressAsync(int departmentId,
			CancellationToken cancellationToken = default)
		{
			var progress = new AdpMigrationProgress();

			// bypassCache: the panel is watched while a run is moving, and a stale kind would point
			// the query at the wrong set of cursor rows.
			var policy = await GetPolicyByDepartmentIdAsync(departmentId, bypassCache: true);
			if (policy?.ActiveMigrationKind == null)
				return progress;

			progress.Kind = (DepartmentDataProtectionMigrationKind)policy.ActiveMigrationKind.Value;

			var rows = await _migrationRepository.GetActiveByDepartmentIdAsync(departmentId, progress.Kind);
			if (rows == null || rows.Count == 0)
				return progress;

			progress.IsRunning = true;
			progress.TablesStarted = rows.Count;
			progress.RowsTotal = rows.Sum(r => r.RowsTotal);
			progress.RowsCompleted = rows.Sum(r => r.RowsProcessed + r.RowsAlreadyProtected);
			progress.RowsAnomalous = rows.Sum(r => r.RowsAnomalous);

			// Same formula as DepartmentDataMigrationEngine.ComputePercentCompleteAsync, so the two
			// never report different numbers for the same run.
			if (progress.RowsTotal > 0)
				progress.PercentComplete = (int)Math.Min(100, progress.RowsCompleted * 100 / progress.RowsTotal);

			progress.CurrentTable = rows
				.OrderBy(r => r.RowsTotal <= 0 ? 1d : (double)(r.RowsProcessed + r.RowsAlreadyProtected) / r.RowsTotal)
				.ThenBy(r => r.TargetTable)
				.Select(r => r.TargetTable)
				.FirstOrDefault();

			return progress;
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

		public async Task<DepartmentDataProtectionEnrollmentResult> ApplyAddonBillingEventAsync(
			AdpAddonBillingEvent billingEvent, CancellationToken cancellationToken = default)
		{
			if (billingEvent == null || billingEvent.DepartmentId <= 0)
				return DepartmentDataProtectionEnrollmentResult.InvalidState;

			try
			{
				var policy = await GetPolicyByDepartmentIdAsync(billingEvent.DepartmentId, bypassCache: true);

				// A department that has never touched ADP has no policy row. Activation and renewal
				// are simply recorded as "may enroll" by the addon existing at all, so there is
				// nothing to write; a cancellation for a department with no policy is a no-op.
				if (policy == null)
					return billingEvent.Kind == AdpAddonBillingEventKind.Cancelled
						? DepartmentDataProtectionEnrollmentResult.InvalidState
						: DepartmentDataProtectionEnrollmentResult.Queued;

				// Idempotency, first pass. Providers retry and duplicate webhooks; an event already
				// applied is acknowledged rather than re-run, so an immediate redelivery of Cancelled
				// cannot re-schedule an offboarding a member has since revoked.
				if (!string.IsNullOrWhiteSpace(billingEvent.ProviderEventId) &&
					string.Equals(policy.LastBillingEventId, billingEvent.ProviderEventId, StringComparison.OrdinalIgnoreCase))
					return DepartmentDataProtectionEnrollmentResult.Queued;

				// Idempotency, second pass. The id above remembers exactly one event, so it stops
				// recognising a redelivery once any other event has overwritten it: Cancelled applies,
				// Renewed withdraws the offboarding AND takes over the id slot, then the provider
				// redelivers the Cancelled - which would pass the check above and re-schedule the
				// offboarding the renewal just withdrew. The provider's own timestamp orders them, so
				// anything older than what has already been applied is a stale redelivery and a no-op.
				// Equal timestamps still apply: two distinct events can share a second.
				if (billingEvent.OccurredOnUtc != default && policy.LastBillingEventOccurredOn.HasValue &&
					billingEvent.OccurredOnUtc < policy.LastBillingEventOccurredOn.Value)
				{
					Logging.LogInfo($"ADP billing event {billingEvent.Kind} for department {billingEvent.DepartmentId} " +
						$"ignored as a stale redelivery (occurred {billingEvent.OccurredOnUtc:o}, last applied " +
						$"{policy.LastBillingEventOccurredOn.Value:o}).");
					return DepartmentDataProtectionEnrollmentResult.Queued;
				}

				var result = DepartmentDataProtectionEnrollmentResult.Queued;

				switch (billingEvent.Kind)
				{
					case AdpAddonBillingEventKind.Activated:
					case AdpAddonBillingEventKind.Renewed:
						// No crypto change (plan 17.3). A payment landing ENDS any lapse: the grace
						// floor and the dunning marker are cleared, and a scheduled offboarding that
						// has not started yet is withdrawn.
						//
						// This is the whole recovery path for a late payer. An invoiced department
						// whose NET45 cheque clears on day fifty arrives here as a Renewed, and the
						// offboarding scheduled at their grace floor is withdrawn before its date -
						// nothing was ever decrypted, and nobody had to phone support.
						policy.AddonDunningStartedOn = null;
						policy.AddonGraceEndsOn = null;

						if ((DepartmentDataProtectionState)policy.State == DepartmentDataProtectionState.OffboardingScheduled)
							result = await RevokeScheduledOffboardingForBillingAsync(billingEvent.DepartmentId, cancellationToken);
						break;

					case AdpAddonBillingEventKind.PaymentFailed:
						// Protection continues untouched (plan 17.3). What this DOES do is fix the
						// floor beneath which offboarding can later be scheduled, once, at the start
						// of the lapse - see BeginLapseIfNewAsync for why once and not per event.
						BeginLapseIfNew(policy, billingEvent);

						Logging.LogInfo($"ADP addon payment failed for department {billingEvent.DepartmentId} " +
							$"(provider {billingEvent.ProviderName}, dunning {billingEvent.DunningState}); protection " +
							$"continues to at least {policy.AddonGraceEndsOn:o}.");

						// An exhausted dunning cycle is a cancellation in everything but name, and
						// some providers report it that way rather than sending a separate cancel.
						if (!billingEvent.IsDunningExhausted)
							break;

						goto case AdpAddonBillingEventKind.Cancelled;

					case AdpAddonBillingEventKind.Cancelled:
						var source = billingEvent.IsChargeback
							? DepartmentDataProtectionOffboardingSource.Chargeback
							: billingEvent.IsDunningExhausted
								? DepartmentDataProtectionOffboardingSource.DunningExhausted
								: DepartmentDataProtectionOffboardingSource.UserCancelled;

						var effectiveOn = ResolveOffboardingEffectiveOn(policy, billingEvent, source);

						// Already scheduled: the provider is repeating itself. Re-scheduling would
						// move a date a member may have been told, so it is left alone.
						if ((DepartmentDataProtectionState)policy.State == DepartmentDataProtectionState.OffboardingScheduled)
							break;

						result = await ScheduleOffboardingAsync(billingEvent.DepartmentId, source, effectiveOn, cancellationToken);

						// Mid-enrollment cancellations return InvalidState by design: the enrollment
						// finishes to Enabled first (plan 21.3) and the reconciliation re-runs then.
						// That is not a failure, so it is recorded rather than surfaced as one.
						if (result == DepartmentDataProtectionEnrollmentResult.InvalidState)
							Logging.LogInfo($"ADP cancellation for department {billingEvent.DepartmentId} deferred: " +
								$"state {(DepartmentDataProtectionState)policy.State} completes first (plan 21.3).");
						break;
				}

				await RecordBillingEventAsync(billingEvent, policy, cancellationToken);
				return result;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"ADP ApplyAddonBillingEventAsync failed for department {billingEvent.DepartmentId}");
				return DepartmentDataProtectionEnrollmentResult.Failed;
			}
		}

		/// <summary>
		/// The grace a lapse gets, in days: the department's own override if support set one, else the
		/// configured default for how it pays. Clamped so a mistyped override cannot hand out
		/// protection indefinitely, and floored at zero so a negative one cannot backdate the floor
		/// into the past.
		/// </summary>
		private static int ResolveGraceDays(DepartmentDataProtectionPolicy policy)
		{
			var configured = (AdpAddonBillingMode?)policy.AddonBillingMode == AdpAddonBillingMode.Invoiced
				? Config.DataProtectionConfig.AddonInvoicedBillingGraceDays
				: Config.DataProtectionConfig.AddonAutomaticBillingGraceDays;

			var days = policy.AddonGraceDaysOverride ?? configured;
			var ceiling = Math.Max(0, Config.DataProtectionConfig.AddonMaxGraceDays);

			return Math.Min(Math.Max(0, days), ceiling);
		}

		/// <summary>
		/// Opens a lapse and fixes its grace floor — ONCE. A failing card produces a payment-failure
		/// webhook on every retry, and recomputing the floor on each one would push it forward
		/// indefinitely: a department whose card never works again would keep protection forever
		/// while paying nothing. So the floor is set on the first failure of an episode and left
		/// alone until a payment lands and clears it.
		///
		/// The anchor is the paid-through date, not "now": what the department bought runs out when
		/// it runs out, and the grace is added to that. Only when we have no paid-through date at all
		/// does the failure's own timestamp stand in.
		/// </summary>
		private static void BeginLapseIfNew(DepartmentDataProtectionPolicy policy, AdpAddonBillingEvent billingEvent)
		{
			if (policy.AddonGraceEndsOn.HasValue && policy.AddonDunningStartedOn.HasValue)
				return;

			var occurredOn = billingEvent.OccurredOnUtc != default ? billingEvent.OccurredOnUtc : DateTime.UtcNow;

			policy.AddonDunningStartedOn = occurredOn;
			policy.AddonGraceEndsOn = (policy.AddonPaidThroughOn ?? occurredOn).AddDays(ResolveGraceDays(policy));
		}

		/// <summary>
		/// When protection actually ends for a cancellation.
		///
		/// A member who cancels gets exactly what they paid for and not a day more — they asked to
		/// stop, so the provider's end-of-cycle date stands. A chargeback ends it now; that is a
		/// dispute, not a slow payment. Between those two sits the case this exists for: a department
		/// that simply has not paid yet, where the provider's end date is only a statement about
		/// billing, and using it directly would decrypt a customer whose invoice is still inside its
		/// terms. There, the grace floor wins.
		/// </summary>
		private static DateTime ResolveOffboardingEffectiveOn(DepartmentDataProtectionPolicy policy,
			AdpAddonBillingEvent billingEvent, DepartmentDataProtectionOffboardingSource source)
		{
			var providerEnd = billingEvent.EffectiveEndUtc ?? DateTime.UtcNow;

			if (source != DepartmentDataProtectionOffboardingSource.DunningExhausted)
				return providerEnd;

			// The floor may not have been set if the provider never sent a payment failure before
			// giving up, so compute it here rather than trusting it to exist.
			if (!policy.AddonGraceEndsOn.HasValue)
				BeginLapseIfNew(policy, billingEvent);

			var floor = policy.AddonGraceEndsOn ?? providerEnd;
			return floor > providerEnd ? floor : providerEnd;
		}

		/// <summary>
		/// Withdraws an offboarding that billing has superseded. Deliberately NOT RevokeOffboardingAsync:
		/// that one is the member-facing command and enforces managing-member authorization, which a
		/// billing event does not have and should not need.
		/// </summary>
		private async Task<DepartmentDataProtectionEnrollmentResult> RevokeScheduledOffboardingForBillingAsync(
			int departmentId, CancellationToken cancellationToken)
		{
			var rows = await _policyRepository.TryTransitionStateAsync(departmentId,
				DepartmentDataProtectionState.OffboardingScheduled, DepartmentDataProtectionState.Enabled,
				null, "system:billing", cancellationToken);

			if (rows == 0)
				return DepartmentDataProtectionEnrollmentResult.InvalidState;

			var policy = await _policyRepository.GetByDepartmentIdAsync(departmentId);
			if (policy != null)
			{
				policy.OffboardingEffectiveOn = null;
				policy.OffboardingSource = null;
				policy.UpdatedOn = DateTime.UtcNow;
				policy.UpdatedByUserId = "system:billing";
				await _policyRepository.SaveOrUpdateAsync(policy, cancellationToken);
			}

			await InvalidateProtectionCacheAsync(departmentId);
			return DepartmentDataProtectionEnrollmentResult.Queued;
		}

		/// <summary>
		/// Stamps the subscription reference and the applied event id. The id is what makes a repeat
		/// of the same webhook a no-op above.
		/// </summary>
		private async Task RecordBillingEventAsync(AdpAddonBillingEvent billingEvent,
			DepartmentDataProtectionPolicy applied, CancellationToken cancellationToken)
		{
			var policy = await _policyRepository.GetByDepartmentIdAsync(billingEvent.DepartmentId);
			if (policy == null)
				return;

			// The lapse fields were decided against the row the rules ran on; carry them across
			// rather than recomputing, so a state transition in between cannot change the answer.
			policy.AddonDunningStartedOn = applied?.AddonDunningStartedOn;
			policy.AddonGraceEndsOn = applied?.AddonGraceEndsOn;

			if (billingEvent.BillingMode.HasValue)
				policy.AddonBillingMode = (int)billingEvent.BillingMode.Value;

			// Only ever moves forward. A late-arriving event from an older cycle must not shorten
			// what the department has already been told it is paid up to.
			if (billingEvent.PaidThroughUtc.HasValue &&
				(!policy.AddonPaidThroughOn.HasValue || billingEvent.PaidThroughUtc.Value > policy.AddonPaidThroughOn.Value))
				policy.AddonPaidThroughOn = billingEvent.PaidThroughUtc.Value;

			if (!string.IsNullOrWhiteSpace(billingEvent.ExternalSubscriptionRef))
				policy.AddonBillingReference = billingEvent.ExternalSubscriptionRef;

			policy.LastBillingEventId = billingEvent.ProviderEventId;

			// Never moves backwards. An out-of-order event that was recent enough to apply must not
			// lower the watermark, or the redelivery it just overtook would become applicable again.
			if (billingEvent.OccurredOnUtc != default &&
				(!policy.LastBillingEventOccurredOn.HasValue ||
					billingEvent.OccurredOnUtc > policy.LastBillingEventOccurredOn.Value))
				policy.LastBillingEventOccurredOn = billingEvent.OccurredOnUtc;
			policy.UpdatedOn = DateTime.UtcNow;
			policy.UpdatedByUserId = "system:billing";

			await _policyRepository.SaveOrUpdateAsync(policy, cancellationToken);
			await InvalidateProtectionCacheAsync(billingEvent.DepartmentId);
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

		public async Task<AdpStepUpExemptClients> GetStepUpExemptClientsAsync(int departmentId, bool bypassCache = false)
		{
			try
			{
				var policy = await GetPolicyByDepartmentIdAsync(departmentId, bypassCache);
				return ((AdpStepUpExemptClients)(policy?.StepUpExemptClients ?? 0)).Sanitize();
			}
			catch (Exception ex)
			{
				// Fail closed: an unknown setting must read as "nothing is exempt", which prompts.
				Logging.LogException(ex, $"ADP step-up exemption lookup failed for department {departmentId}; reporting none exempt.");
				return AdpStepUpExemptClients.None;
			}
		}

		public async Task<bool> IsStepUpRequiredForClientAsync(int departmentId, UserSessionClientApplication client,
			bool bypassCache = false)
		{
			var decision = await GetStepUpDecisionForClientAsync(departmentId, client, bypassCache);
			return decision.StepUpRequired;
		}

		public async Task<AdpStepUpDecision> GetStepUpDecisionForClientAsync(int departmentId,
			UserSessionClientApplication client, bool bypassCache = false)
		{
			try
			{
				// ONE read backs the whole decision. The epoch a grant is stamped with has to come
				// from the same snapshot that said the client was exempt, or a revocation arriving
				// between two reads would mint a grant carrying the epoch its own revocation bumped.
				var policy = await GetPolicyByDepartmentIdAsync(departmentId, bypassCache);
				var exemptions = ((AdpStepUpExemptClients)(policy?.StepUpExemptClients ?? 0)).Sanitize();

				return new AdpStepUpDecision
				{
					StepUpRequired = policy == null || !exemptions.IsExempt(client),
					PolicyEpoch = policy?.PolicyEpoch ?? 0,
					StepUpWindowMinutes = policy?.StepUpWindowMinutes ?? 0
				};
			}
			catch (Exception ex)
			{
				// Fail closed: an unknown setting must read as "nothing is exempt", which prompts.
				Logging.LogException(ex, $"ADP step-up decision lookup failed for department {departmentId}; requiring step up.");
				return new AdpStepUpDecision { StepUpRequired = true };
			}
		}

		public async Task<DepartmentDataProtectionEnrollmentResult> SetStepUpExemptClientsAsync(int departmentId,
			AdpStepUpExemptClients exemptions, string requestingUserId, CancellationToken cancellationToken = default)
		{
			try
			{
				// Weakening a protection control is a managing-member decision, the same identity that
				// bought the addon and enrolled the department - not any administrator.
				var managingCheck = await VerifyManagingMemberAsync(departmentId, requestingUserId);
				if (managingCheck != null)
					return managingCheck.Value;

				var policy = await _policyRepository.GetByDepartmentIdAsync(departmentId);
				if (policy == null)
					return DepartmentDataProtectionEnrollmentResult.InvalidState;

				var sanitized = exemptions.Sanitize();
				if ((AdpStepUpExemptClients)policy.StepUpExemptClients == sanitized)
					return DepartmentDataProtectionEnrollmentResult.Queued;

				policy.StepUpExemptClients = (int)sanitized;
				policy.UpdatedOn = DateTime.UtcNow;
				policy.UpdatedByUserId = requestingUserId;
				await _policyRepository.SaveOrUpdateAsync(policy, cancellationToken);

				await InvalidateProtectionCacheAsync(departmentId);

				// The epoch bump is what makes a TIGHTENING take effect now rather than whenever the
				// last loosely-issued grant happened to expire. It is applied in both directions so
				// the rule stays simple and there is never a window where the two disagree.
				await IncrementPolicyEpochAsync(departmentId, requestingUserId, cancellationToken);

				Logging.LogInfo($"ADP step-up exemptions for department {departmentId} set to {sanitized} by {requestingUserId}.");

				return DepartmentDataProtectionEnrollmentResult.Queued;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"ADP SetStepUpExemptClientsAsync failed for department {departmentId}");
				return DepartmentDataProtectionEnrollmentResult.Failed;
			}
		}

		public async Task<DepartmentDataProtectionEnrollmentResult> QueueKeyRotationAsync(int departmentId,
			string requestingUserId, CancellationToken cancellationToken = default)
		{
			try
			{
				var policy = await GetPolicyByDepartmentIdAsync(departmentId, bypassCache: true);
				if (policy == null)
					return DepartmentDataProtectionEnrollmentResult.InvalidState;

				// Enabled only. A department mid-enrollment, mid-upgrade or mid-offboarding already
				// has a cursor in flight, and a second sweep over the same rows under a different key
				// would race the first.
				if ((DepartmentDataProtectionState)policy.State != DepartmentDataProtectionState.Enabled)
					return DepartmentDataProtectionEnrollmentResult.InvalidState;

				// The key is provisioned BEFORE the state moves. Provisioning is the step that can
				// fail on a KMS outage, and failing it here leaves the department Enabled with no new
				// version; failing it after the transition would park a department in Rotating with
				// no version to rotate to.
				var newKey = await _keyService.ProvisionNextKeyVersionAsync(departmentId, cancellationToken);
				if (newKey == null)
					return DepartmentDataProtectionEnrollmentResult.Failed;

				var rows = await _policyRepository.TryTransitionStateAsync(departmentId,
					DepartmentDataProtectionState.Enabled, DepartmentDataProtectionState.Rotating,
					(int)DepartmentDataProtectionMigrationKind.Rotation, requestingUserId, cancellationToken);

				if (rows == 0)
				{
					// Losing this race is NOT a no-op: provisioning already activated the new version
					// and moved the previous one to Retiring, so the department stays Enabled holding
					// a key version no sweep is queued to apply. New writes take the new version while
					// older envelopes still name the Retiring one — readable, and cleared by the next
					// rotation, but an operator has to be able to see that it happened.
					Logging.LogError($"ADP key rotation for department {departmentId} lost the Enabled->Rotating transition after key v{newKey.Version} was activated; the department holds an unswept key version.");
					return DepartmentDataProtectionEnrollmentResult.InvalidState;
				}

				await InvalidateProtectionCacheAsync(departmentId);

				// Value-free: department, key version and who asked. Never key material.
				Logging.LogInfo($"ADP key rotation queued for department {departmentId} to key v{newKey.Version} by {requestingUserId}.");

				return DepartmentDataProtectionEnrollmentResult.Queued;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"ADP QueueKeyRotationAsync failed for department {departmentId}");
				return DepartmentDataProtectionEnrollmentResult.Failed;
			}
		}

		public async Task<DepartmentDataProtectionEnrollmentResult> RetryFailedMigrationAsync(int departmentId,
			string requestingUserId, CancellationToken cancellationToken = default)
		{
			try
			{
				var policy = await GetPolicyByDepartmentIdAsync(departmentId, bypassCache: true);
				if (policy == null)
					return DepartmentDataProtectionEnrollmentResult.InvalidState;

				if ((DepartmentDataProtectionState)policy.State != DepartmentDataProtectionState.Failed)
					return DepartmentDataProtectionEnrollmentResult.InvalidState;

				// A run that failed without recording what it was doing cannot be resumed safely:
				// encrypting and decrypting from the same cursor are opposite operations.
				if (!policy.ActiveMigrationKind.HasValue)
					return DepartmentDataProtectionEnrollmentResult.InvalidState;

				// Each kind resumes into the state its own sweep runs from. An offboarding that came
				// back as EnrollmentQueued would re-encrypt a department on its way out, and a
				// rotation that did would re-run enrollment over an already-protected corpus.
				//
				// CatalogUpgrade resumes into Encrypting, the state the nightly sweep queues it into.
				// Sending it to EnrollmentQueued would be worse than a wasted pass: the worker's
				// enrollment path rewrites ActiveMigrationKind to Enrollment on its first transition,
				// which both loses the upgrade's narrower field scope and drops enforcement — an
				// Encrypting department only enforces while its kind still reads CatalogUpgrade, so
				// the unenforced read path would start handing out rgdp ciphertext.
				var resumeState = (DepartmentDataProtectionMigrationKind)policy.ActiveMigrationKind.Value switch
				{
					DepartmentDataProtectionMigrationKind.Offboarding => DepartmentDataProtectionState.DisableRequested,
					DepartmentDataProtectionMigrationKind.Rotation => DepartmentDataProtectionState.Rotating,
					DepartmentDataProtectionMigrationKind.CatalogUpgrade => DepartmentDataProtectionState.Encrypting,
					_ => DepartmentDataProtectionState.EnrollmentQueued
				};

				var rows = await _policyRepository.TryTransitionStateAsync(departmentId,
					DepartmentDataProtectionState.Failed, resumeState, policy.ActiveMigrationKind,
					requestingUserId, cancellationToken);

				if (rows == 0)
					return DepartmentDataProtectionEnrollmentResult.InvalidState;

				await InvalidateProtectionCacheAsync(departmentId);
				Logging.LogInfo($"ADP migration for department {departmentId} re-queued as {resumeState} by {requestingUserId}; resumes from its cursor.");

				return DepartmentDataProtectionEnrollmentResult.Queued;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"ADP RetryFailedMigrationAsync failed for department {departmentId}");
				return DepartmentDataProtectionEnrollmentResult.Failed;
			}
		}

		public async Task<bool> AbortActiveMigrationAsync(int departmentId, string requestingUserId,
			CancellationToken cancellationToken = default)
		{
			try
			{
				var policy = await GetPolicyByDepartmentIdAsync(departmentId, bypassCache: true);
				if (policy == null)
					return false;

				var state = (DepartmentDataProtectionState)policy.State;

				// Only a state the worker is actually running can be aborted. Enabled, Disabled and
				// the scheduled states have no window to stop.
				if (state != DepartmentDataProtectionState.ProvisioningKey &&
					state != DepartmentDataProtectionState.Encrypting &&
					state != DepartmentDataProtectionState.Verifying &&
					state != DepartmentDataProtectionState.Decrypting)
					return false;

				// The lock goes first. The worker checks it on every heartbeat, so releasing it is
				// what actually makes the run stop; flipping the state first would leave a worker
				// writing into a department the state says is idle.
				var activeLock = await _departmentLockService.GetActiveLockAsync(departmentId, bypassCache: true);
				if (activeLock != null)
					await _departmentLockService.ReleaseLockAsync(activeLock.DepartmentOperationLockId,
						DepartmentOperationLockReleaseKind.Aborted, requestingUserId, cancellationToken);

				var rows = await _policyRepository.TryTransitionStateAsync(departmentId, state,
					DepartmentDataProtectionState.Failed, policy.ActiveMigrationKind, requestingUserId,
					cancellationToken);

				await InvalidateProtectionCacheAsync(departmentId);

				if (rows > 0)
					Logging.LogInfo($"ADP migration for department {departmentId} aborted from {state} by {requestingUserId}; resumable from its cursor.");

				return rows > 0;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"ADP AbortActiveMigrationAsync failed for department {departmentId}");
				return false;
			}
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
