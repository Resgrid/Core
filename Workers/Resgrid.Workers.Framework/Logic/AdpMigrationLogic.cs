using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Workers.Framework.Logic
{
	/// <summary>
	/// ADP migration coordinator (plan sections 19, 20, 21). Each sweep: releases expired locks and
	/// fails their migrations at the cursor; flips due OffboardingScheduled departments to
	/// DisableRequested; then picks up to MigrationNightlyConcurrency departments (FIFO) whose
	/// department-local overnight window is open and drives one night of the durable state machine —
	/// lock, provision, engine run, checkpoint/verify, notify. All bulk data movement lives behind
	/// IDepartmentDataMigrationEngine, and nights run only where IDepartmentDataMigrationEngine
	/// reports available (a host with a real KMS adapter — the Protected Data Broker). Elsewhere the
	/// sweep does liveness/offboarding work and leaves queued departments queued.
	///
	/// Deliberately NOT here: gate/addon re-checks. A committed enrollment must never be aborted by
	/// flag removal (plan section 3.5), and addon lapses reach the state machine through billing
	/// events, not the worker.
	/// </summary>
	public sealed class AdpMigrationLogic
	{
		private const string WorkerIdentity = "worker:adp-migration";

		private readonly IDepartmentLockService _lockService;
		private readonly IDepartmentDataProtectionPolicyRepository _policyRepository;
		private readonly IDepartmentDataProtectionService _protectionService;
		private readonly IDepartmentKeyService _keyService;
		private readonly IDepartmentDataMigrationEngine _engine;
		private readonly IProtectedFieldCatalog _catalog;
		private readonly IDepartmentsService _departmentsService;
		private readonly IEmailService _emailService;
		private readonly IMemberProfileRelocationService _relocationService;

		public AdpMigrationLogic()
			: this(
				Bootstrapper.GetKernel().Resolve<IDepartmentLockService>(),
				Bootstrapper.GetKernel().Resolve<IDepartmentDataProtectionPolicyRepository>(),
				Bootstrapper.GetKernel().Resolve<IDepartmentDataProtectionService>(),
				Bootstrapper.GetKernel().Resolve<IDepartmentKeyService>(),
				Bootstrapper.GetKernel().Resolve<IDepartmentDataMigrationEngine>(),
				Bootstrapper.GetKernel().Resolve<IProtectedFieldCatalog>(),
				Bootstrapper.GetKernel().Resolve<IDepartmentsService>(),
				Bootstrapper.GetKernel().Resolve<IEmailService>(),
				Bootstrapper.GetKernel().Resolve<IMemberProfileRelocationService>())
		{
		}

		public AdpMigrationLogic(IDepartmentLockService lockService,
			IDepartmentDataProtectionPolicyRepository policyRepository,
			IDepartmentDataProtectionService protectionService, IDepartmentKeyService keyService,
			IDepartmentDataMigrationEngine engine, IProtectedFieldCatalog catalog,
			IDepartmentsService departmentsService, IEmailService emailService,
			IMemberProfileRelocationService relocationService)
		{
			_lockService = lockService ?? throw new ArgumentNullException(nameof(lockService));
			_policyRepository = policyRepository ?? throw new ArgumentNullException(nameof(policyRepository));
			_protectionService = protectionService ?? throw new ArgumentNullException(nameof(protectionService));
			_keyService = keyService ?? throw new ArgumentNullException(nameof(keyService));
			_engine = engine ?? throw new ArgumentNullException(nameof(engine));
			_catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
			_departmentsService = departmentsService ?? throw new ArgumentNullException(nameof(departmentsService));
			_emailService = emailService ?? throw new ArgumentNullException(nameof(emailService));
			_relocationService = relocationService ?? throw new ArgumentNullException(nameof(relocationService));
		}

		public async Task<Tuple<bool, string>> Process(CancellationToken cancellationToken)
		{
			try
			{
				var utcNow = DateTime.UtcNow;
				var summary = new List<string>();

				// 1) Liveness: durably release expired locks and fail their migrations at the cursor.
				var expired = await _lockService.ReleaseExpiredLocksAsync(cancellationToken);
				foreach (var expiredLock in expired.Where(l => l.LockType == (int)DepartmentOperationLockType.AdpMigration))
				{
					await FailInFlightMigrationAsync(expiredLock.DepartmentId, "lock_heartbeat_expired", cancellationToken);
					await NotifyAdminsAsync(expiredLock.DepartmentId,
						"Advanced Data Protection: the overnight migration stopped unexpectedly and your department has returned to full service. Work resumes from its last checkpoint; no action is needed. Support has been alerted.");
					summary.Add($"expired lock released for department {expiredLock.DepartmentId}");
				}

				// 2) Offboarding due: end of paid cycle reached — begin the decrypt path.
				var policies = (await _policyRepository.GetAllAsync())?.ToList() ?? new List<DepartmentDataProtectionPolicy>();
				foreach (var policy in policies.Where(p =>
							 p.State == (int)DepartmentDataProtectionState.OffboardingScheduled &&
							 p.OffboardingEffectiveOn.HasValue && p.OffboardingEffectiveOn.Value <= utcNow))
				{
					var rows = await _policyRepository.TryTransitionStateAsync(policy.DepartmentId,
						DepartmentDataProtectionState.OffboardingScheduled, DepartmentDataProtectionState.DisableRequested,
						(int)DepartmentDataProtectionMigrationKind.Offboarding, WorkerIdentity, cancellationToken);
					if (rows > 0)
					{
						policy.State = (int)DepartmentDataProtectionState.DisableRequested;
						policy.ActiveMigrationKind = (int)DepartmentDataProtectionMigrationKind.Offboarding;
						await _protectionService.InvalidateProtectionCacheAsync(policy.DepartmentId);
						summary.Add($"offboarding due for department {policy.DepartmentId}");
					}
				}

				// 2b) Catalog upgrades due: the code's catalog has advanced past what this
				// department was migrated to, so the newly cataloged fields are still landing in
				// plaintext. Sweep only those fields; existing envelopes are untouched (the catalog
				// version is not an AAD component). The department's OLD CatalogVersion stays on the
				// policy row until the upgrade verifies — that is what makes the run resumable
				// across nights, since FromCatalogVersion is read straight off the policy.
				foreach (var policy in policies.Where(p =>
							 p.State == (int)DepartmentDataProtectionState.Enabled &&
							 p.CatalogVersion < _catalog.Version))
				{
					var rows = await _policyRepository.TryTransitionStateAsync(policy.DepartmentId,
						DepartmentDataProtectionState.Enabled, DepartmentDataProtectionState.Encrypting,
						(int)DepartmentDataProtectionMigrationKind.CatalogUpgrade, WorkerIdentity, cancellationToken);
					if (rows > 0)
					{
						policy.State = (int)DepartmentDataProtectionState.Encrypting;
						policy.ActiveMigrationKind = (int)DepartmentDataProtectionMigrationKind.CatalogUpgrade;
						await _protectionService.InvalidateProtectionCacheAsync(policy.DepartmentId);
						summary.Add($"catalog upgrade v{policy.CatalogVersion}->v{_catalog.Version} queued for department {policy.DepartmentId}");
					}
				}

				// 3) Pick work, unless the operator has paused the queue.
				if (DataProtectionConfig.MigrationQueuePaused)
				{
					summary.Add("queue paused");
					return new Tuple<bool, string>(true, Summarize(summary));
				}

				var workable = policies
					.Where(p => IsWorkableState((DepartmentDataProtectionState)p.State))
					.OrderBy(p => p.AcknowledgedOn ?? p.CreatedOn)
					.ToList();

				// Nights run only where the engine can actually move data (a real KMS adapter — the
				// Protected Data Broker host). On app/worker hosts the sweep still does the liveness
				// and offboarding work above, but queued departments are left QUEUED for the broker
				// instead of being marked Failed by a host that can never succeed.
				if (workable.Count > 0 && !_engine.IsAvailable)
				{
					summary.Add($"{workable.Count} department(s) queued; engine unavailable on this host, nights skipped");
					return new Tuple<bool, string>(true, Summarize(summary));
				}

				var concurrency = Math.Max(1, DataProtectionConfig.MigrationNightlyConcurrency);
				var executed = 0;

				foreach (var policy in workable)
				{
					cancellationToken.ThrowIfCancellationRequested();

					if (executed >= concurrency)
						break;

					if (!TryGetOpenWindow(policy, utcNow, out var windowEndUtc))
						continue;

					executed++;
					var nightSummary = await ExecuteNightAsync(policy, windowEndUtc, cancellationToken);
					summary.Add(nightSummary);
				}

				return new Tuple<bool, string>(true, Summarize(summary));
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return new Tuple<bool, string>(false, ex.ToString());
			}
		}

		private static bool IsWorkableState(DepartmentDataProtectionState state)
		{
			switch (state)
			{
				case DepartmentDataProtectionState.EnrollmentQueued:
				case DepartmentDataProtectionState.ProvisioningKey:
				case DepartmentDataProtectionState.Encrypting:
				case DepartmentDataProtectionState.Rotating:
				case DepartmentDataProtectionState.Verifying:
				case DepartmentDataProtectionState.DisableRequested:
				case DepartmentDataProtectionState.Decrypting:
					return true;

				// Failed is deliberately NOT auto-resumed: an operator (or the BackOffice retry
				// control) moves it back into the queue after the cause is cleared.
				default:
					return false;
			}
		}

		/// <summary>
		/// True when the department's overnight window is open at <paramref name="utcNow"/>. Windows
		/// are department-local and may span midnight (the 22:00-06:00 default does). A missing or
		/// unresolvable time zone reads as CLOSED — a migration must never run at an unintended local
		/// time.
		/// </summary>
		public static bool TryGetOpenWindow(DepartmentDataProtectionPolicy policy, DateTime utcNow, out DateTime windowEndUtc)
		{
			windowEndUtc = default;

			try
			{
				if (!TimeSpan.TryParse(string.IsNullOrWhiteSpace(policy.MigrationWindowStartLocal)
						? DataProtectionConfig.MigrationWindowDefaultStartLocal
						: policy.MigrationWindowStartLocal, out var start))
					return false;
				if (!TimeSpan.TryParse(string.IsNullOrWhiteSpace(policy.MigrationWindowEndLocal)
						? DataProtectionConfig.MigrationWindowDefaultEndLocal
						: policy.MigrationWindowEndLocal, out var end))
					return false;
				if (string.IsNullOrWhiteSpace(policy.MigrationWindowTimeZone))
					return false;

				var timeZone = TimeZoneInfo.FindSystemTimeZoneById(policy.MigrationWindowTimeZone);
				var local = TimeZoneInfo.ConvertTimeFromUtc(utcNow, timeZone);
				var time = local.TimeOfDay;

				bool open;
				DateTime localEnd;
				if (start <= end)
				{
					open = time >= start && time < end;
					localEnd = local.Date.Add(end);
				}
				else
				{
					// Window spans midnight (e.g. 22:00 -> 06:00).
					open = time >= start || time < end;
					localEnd = time >= start ? local.Date.AddDays(1).Add(end) : local.Date.Add(end);
				}

				if (!open)
					return false;

				var unspecifiedEnd = DateTime.SpecifyKind(localEnd, DateTimeKind.Unspecified);
				if (timeZone.IsInvalidTime(unspecifiedEnd))
				{
					// Spring-forward gap: the configured window end does not exist as a local time
					// today. Treat the window as closed rather than letting ConvertTimeToUtc throw
					// and abort the whole sweep (liveness releases included).
					Logging.LogError($"ADP migration: department {policy.DepartmentId} window end falls in a DST gap for '{policy.MigrationWindowTimeZone}' today; treating window as closed.");
					return false;
				}

				windowEndUtc = TimeZoneInfo.ConvertTimeToUtc(unspecifiedEnd, timeZone);
				return true;
			}
			catch (ArgumentException ex)
			{
				Logging.LogException(ex, $"ADP migration: department {policy.DepartmentId} window end is not a valid local time in '{policy.MigrationWindowTimeZone}'; treating window as closed.");
				return false;
			}
			catch (TimeZoneNotFoundException)
			{
				Logging.LogError($"ADP migration: department {policy.DepartmentId} has unresolvable window time zone '{policy.MigrationWindowTimeZone}'; treating window as closed.");
				return false;
			}
			catch (InvalidTimeZoneException)
			{
				Logging.LogError($"ADP migration: department {policy.DepartmentId} has invalid window time zone '{policy.MigrationWindowTimeZone}'; treating window as closed.");
				return false;
			}
		}

		private async Task<string> ExecuteNightAsync(DepartmentDataProtectionPolicy policy, DateTime windowEndUtc,
			CancellationToken cancellationToken)
		{
			var departmentId = policy.DepartmentId;
			var correlationId = Guid.NewGuid().ToString("N");
			var kind = policy.ActiveMigrationKind.HasValue
				? (DepartmentDataProtectionMigrationKind)policy.ActiveMigrationKind.Value
				: DepartmentDataProtectionMigrationKind.Enrollment;

			var departmentLock = await _lockService.ApplyLockAsync(departmentId, DepartmentOperationLockType.AdpMigration,
				"Advanced Data Protection migration in progress — data entry paused", correlationId, WorkerIdentity,
				DateTime.UtcNow.AddSeconds(DataProtectionConfig.LockExpirySeconds), windowEndUtc, cancellationToken);

			if (departmentLock == null)
				return $"department {departmentId}: lock unavailable, skipped";

			var releaseKind = DepartmentOperationLockReleaseKind.Checkpoint;
			try
			{
				await NotifyAdminsAsync(departmentId,
					"Advanced Data Protection: tonight's migration window has started. Data entry is paused until the window closes; viewing is unaffected.");

				var context = new AdpMigrationNightContext
				{
					DepartmentId = departmentId,
					Kind = kind,
					CatalogVersion = _catalog.Version,

					// Still the department's OLD version until the run verifies, so a resumed
					// upgrade keeps sweeping the same field range.
					FromCatalogVersion = policy.CatalogVersion,
					WindowEndUtc = windowEndUtc,
					DepartmentOperationLockId = departmentLock.DepartmentOperationLockId,
					CorrelationId = correlationId,
					HeartbeatAsync = () => _lockService.HeartbeatAsync(departmentLock.DepartmentOperationLockId,
						DateTime.UtcNow.AddSeconds(DataProtectionConfig.LockExpirySeconds), cancellationToken)
				};

				var state = (DepartmentDataProtectionState)policy.State;

				// --- Enrollment path -----------------------------------------------------------
				if (state == DepartmentDataProtectionState.EnrollmentQueued)
				{
					if (await _policyRepository.TryTransitionStateAsync(departmentId, DepartmentDataProtectionState.EnrollmentQueued,
							DepartmentDataProtectionState.ProvisioningKey, (int)DepartmentDataProtectionMigrationKind.Enrollment,
							WorkerIdentity, cancellationToken) == 0)
						return $"department {departmentId}: lost enrollment start race, skipped";

					state = DepartmentDataProtectionState.ProvisioningKey;
				}

				if (state == DepartmentDataProtectionState.ProvisioningKey)
				{
					var key = await _keyService.ProvisionNextKeyVersionAsync(departmentId, cancellationToken);
					context.TargetKeyVersion = key.Version;

					if (await _policyRepository.TryTransitionStateAsync(departmentId, DepartmentDataProtectionState.ProvisioningKey,
							DepartmentDataProtectionState.Encrypting, (int)DepartmentDataProtectionMigrationKind.Enrollment,
							WorkerIdentity, cancellationToken) == 0)
						return $"department {departmentId}: provisioning transition race, skipped";

					await _protectionService.InvalidateProtectionCacheAsync(departmentId);
					state = DepartmentDataProtectionState.Encrypting;
				}
				else if (kind != DepartmentDataProtectionMigrationKind.Offboarding)
				{
					var activeKey = await _keyService.GetActiveKeyAsync(departmentId);
					context.TargetKeyVersion = activeKey?.Version;
				}

				// A rotation re-encrypts an already-protected corpus under the new key version. It
				// runs the same night as an enrollment because the engine's encrypt path IS the
				// re-key path - it decrypts each envelope to validate it anyway, so a rotation is
				// that decrypt followed by an encrypt under the new version.
				if (state == DepartmentDataProtectionState.Rotating)
				{
					var rotationNight = await _engine.RunEncryptionNightAsync(context, cancellationToken);
					if (rotationNight.Outcome == AdpMigrationNightOutcome.WindowClosed)
					{
						await NotifyAdminsAsync(departmentId,
							$"Advanced Data Protection: tonight's key rotation checkpoint is complete ({rotationNight.PercentComplete?.ToString() ?? "?"}% done). Your department is back in full service; work resumes the next scheduled night.");
						return $"department {departmentId}: rotation checkpointed";
					}

					if (rotationNight.Outcome == AdpMigrationNightOutcome.Failed)
					{
						releaseKind = DepartmentOperationLockReleaseKind.Aborted;
						await FailInFlightMigrationAsync(departmentId, rotationNight.ErrorCode, cancellationToken);
						await NotifyFailureAsync(departmentId);
						return $"department {departmentId}: rotation failed ({rotationNight.ErrorCode})";
					}

					if (await _policyRepository.TryTransitionStateAsync(departmentId, DepartmentDataProtectionState.Rotating,
							DepartmentDataProtectionState.Verifying, (int)DepartmentDataProtectionMigrationKind.Rotation,
							WorkerIdentity, cancellationToken) == 0)
						return $"department {departmentId}: verify transition race";

					state = DepartmentDataProtectionState.Verifying;
				}

				if (state == DepartmentDataProtectionState.Encrypting)
				{
					// Before the sweep: move any member data still sitting in the legacy global
					// location into this department's own rows (plan 5.1), so the night encrypts a
					// complete corpus instead of leaving identification numbers and addresses behind
					// in plaintext. Safe at any point in a resumed run — relocated values go through
					// the normal write path, which envelopes them because the department is already
					// encrypting new writes, so it does not matter where the sweep cursor sits.
					var relocation = await _relocationService.RelocateDepartmentAsync(departmentId, cancellationToken);
					if (relocation.Failures > 0)
						Logging.LogError($"ADP migration: {relocation.Failures} member profile relocation(s) failed for department {departmentId}; they retry on the next pass.");

					var night = await _engine.RunEncryptionNightAsync(context, cancellationToken);
					if (night.Outcome == AdpMigrationNightOutcome.WindowClosed)
					{
						await NotifyAdminsAsync(departmentId,
							$"Advanced Data Protection: tonight's migration checkpoint is complete ({night.PercentComplete?.ToString() ?? "?"}% done). Your department is back in full service; work resumes the next scheduled night.");
						return $"department {departmentId}: encryption checkpointed";
					}

					if (night.Outcome == AdpMigrationNightOutcome.Failed)
					{
						releaseKind = DepartmentOperationLockReleaseKind.Aborted;
						await FailInFlightMigrationAsync(departmentId, night.ErrorCode, cancellationToken);
						await NotifyFailureAsync(departmentId);
						return $"department {departmentId}: encryption failed ({night.ErrorCode})";
					}

					if (await _policyRepository.TryTransitionStateAsync(departmentId, DepartmentDataProtectionState.Encrypting,
							DepartmentDataProtectionState.Verifying, (int)kind,
							WorkerIdentity, cancellationToken) == 0)
						return $"department {departmentId}: verify transition race";

					state = DepartmentDataProtectionState.Verifying;
				}

				// --- Offboarding path ----------------------------------------------------------
				if (state == DepartmentDataProtectionState.DisableRequested)
				{
					if (await _policyRepository.TryTransitionStateAsync(departmentId, DepartmentDataProtectionState.DisableRequested,
							DepartmentDataProtectionState.Decrypting, (int)DepartmentDataProtectionMigrationKind.Offboarding,
							WorkerIdentity, cancellationToken) == 0)
						return $"department {departmentId}: offboarding start race, skipped";

					await NotifyAdminsAsync(departmentId,
						"Advanced Data Protection: offboarding has started. Protection remains in effect until your data is fully restored to standard storage.");
					await _protectionService.InvalidateProtectionCacheAsync(departmentId);
					state = DepartmentDataProtectionState.Decrypting;
				}

				if (state == DepartmentDataProtectionState.Decrypting)
				{
					var night = await _engine.RunDecryptionNightAsync(context, cancellationToken);
					if (night.Outcome == AdpMigrationNightOutcome.WindowClosed)
					{
						await NotifyAdminsAsync(departmentId,
							$"Advanced Data Protection: tonight's offboarding checkpoint is complete ({night.PercentComplete?.ToString() ?? "?"}% done). Your department is back in full service; work resumes the next scheduled night.");
						return $"department {departmentId}: decryption checkpointed";
					}

					if (night.Outcome == AdpMigrationNightOutcome.Failed)
					{
						releaseKind = DepartmentOperationLockReleaseKind.Aborted;
						await FailInFlightMigrationAsync(departmentId, night.ErrorCode, cancellationToken);
						await NotifyFailureAsync(departmentId);
						return $"department {departmentId}: decryption failed ({night.ErrorCode})";
					}

					if (await _policyRepository.TryTransitionStateAsync(departmentId, DepartmentDataProtectionState.Decrypting,
							DepartmentDataProtectionState.Verifying, (int)DepartmentDataProtectionMigrationKind.Offboarding,
							WorkerIdentity, cancellationToken) == 0)
						return $"department {departmentId}: verify transition race";

					state = DepartmentDataProtectionState.Verifying;
				}

				// --- Verification (shared; direction from the migration kind) ------------------
				if (state == DepartmentDataProtectionState.Verifying)
				{
					var verified = await _engine.VerifyAsync(context, cancellationToken);
					if (!verified)
					{
						releaseKind = DepartmentOperationLockReleaseKind.Aborted;
						await FailInFlightMigrationAsync(departmentId, "verification_failed", cancellationToken);
						await NotifyFailureAsync(departmentId);
						return $"department {departmentId}: verification failed";
					}

					if (kind == DepartmentDataProtectionMigrationKind.Offboarding)
					{
						if (await _policyRepository.TryTransitionStateAsync(departmentId, DepartmentDataProtectionState.Verifying,
								DepartmentDataProtectionState.Disabled, null, WorkerIdentity, cancellationToken) == 0)
							return $"department {departmentId}: disable transition race";

						await _protectionService.IncrementPolicyEpochAsync(departmentId, WorkerIdentity, cancellationToken);
						await NotifyAdminsAsync(departmentId,
							"Advanced Data Protection: offboarding is complete. Your department has returned to standard storage. Re-enabling requires purchasing the addon again and completing a new enrollment.");
						releaseKind = DepartmentOperationLockReleaseKind.Completed;
						return $"department {departmentId}: offboarding complete";
					}

					if (await _policyRepository.TryTransitionStateAsync(departmentId, DepartmentDataProtectionState.Verifying,
							DepartmentDataProtectionState.Enabled, null, WorkerIdentity, cancellationToken) == 0)
						return $"department {departmentId}: enable transition race";

					var enabledPolicy = await _policyRepository.GetByDepartmentIdAsync(departmentId);
					if (enabledPolicy != null)
					{
						enabledPolicy.CatalogVersion = context.CatalogVersion;
						enabledPolicy.UpdatedOn = DateTime.UtcNow;
						enabledPolicy.UpdatedByUserId = WorkerIdentity;
						await _policyRepository.SaveOrUpdateAsync(enabledPolicy, cancellationToken);
					}

					// The epoch bump invalidates outstanding grants: after an upgrade a client's cached
					// view of which fields are protected is stale, so everyone re-steps-up.
					await _protectionService.IncrementPolicyEpochAsync(departmentId, WorkerIdentity, cancellationToken);

					if (kind == DepartmentDataProtectionMigrationKind.CatalogUpgrade)
					{
						await NotifyAdminsAsync(departmentId,
							"Advanced Data Protection: additional fields are now protected for your department and verification passed. No action is needed.");
						releaseKind = DepartmentOperationLockReleaseKind.Completed;
						return $"department {departmentId}: catalog upgrade complete at v{context.CatalogVersion}";
					}

					if (kind == DepartmentDataProtectionMigrationKind.Rotation)
					{
						// Retirement happens ONLY here, after verification proved no envelope still
						// references a superseded version (plan 11.3: "retires old versions only after
						// all copies and restore tests pass"). Retiring earlier would make any row the
						// sweep had not reached yet unreadable. The rows themselves are never deleted -
						// cryptographic erasure is a separate dual-controlled operation.
						var retired = await RetireSupersededKeyVersionsAsync(departmentId, context.TargetKeyVersion ?? 0, cancellationToken);

						await NotifyAdminsAsync(departmentId,
							"Advanced Data Protection: your department's encryption key has been rotated and verification passed. No action is needed.");
						releaseKind = DepartmentOperationLockReleaseKind.Completed;
						return $"department {departmentId}: rotation complete at key v{context.TargetKeyVersion}, {retired} version(s) retired";
					}

					await NotifyAdminsAsync(departmentId,
						"Advanced Data Protection: verification passed and protection is now ACTIVE for your department.");
					releaseKind = DepartmentOperationLockReleaseKind.Completed;
					return $"department {departmentId}: enrollment complete, protection active";
				}

				return $"department {departmentId}: no work for state {(DepartmentDataProtectionState)policy.State}";
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				// Worker shutdown/redeploy is NOT a migration failure: leave the durable state
				// untouched (the next sweep resumes from the cursor) and release the lock as a
				// checkpoint, mirroring how Process propagates cancellation.
				throw;
			}
			catch (Exception ex)
			{
				releaseKind = DepartmentOperationLockReleaseKind.Aborted;
				Logging.LogException(ex, $"ADP migration night failed for department {departmentId}");
				await FailInFlightMigrationAsync(departmentId, "night_execution_error", cancellationToken);
				await NotifyFailureAsync(departmentId);
				return $"department {departmentId}: night execution error";
			}
			finally
			{
				await _lockService.ReleaseLockAsync(departmentLock.DepartmentOperationLockId, releaseKind, WorkerIdentity, CancellationToken.None);
			}
		}

		/// <summary>
		/// Retires every Retiring version below the rotation target. Called only after verification,
		/// so by this point nothing references them. Failures here are logged rather than failing the
		/// run: the data is fully re-keyed and readable, and a version left Retiring is a metadata
		/// tidy-up an operator can repeat, not a reason to unwind a successful rotation.
		/// </summary>
		private async Task<int> RetireSupersededKeyVersionsAsync(int departmentId, int targetVersion, CancellationToken cancellationToken)
		{
			if (targetVersion <= 0)
				return 0;

			var retired = 0;

			try
			{
				var versions = await _keyService.GetAllVersionsAsync(departmentId);

				foreach (var key in (versions ?? Array.Empty<DepartmentDataProtectionKey>())
					.Where(k => k.Version < targetVersion &&
						(DepartmentDataProtectionKeyStatus)k.Status == DepartmentDataProtectionKeyStatus.Retiring))
				{
					if (await _keyService.RetireKeyVersionAsync(departmentId, key.Version, cancellationToken))
						retired++;
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"ADP rotation for department {departmentId} completed but retiring superseded key versions failed.");
			}

			return retired;
		}

		/// <summary>Moves an in-flight (transitional) state to Failed, preserving the migration kind for resume.</summary>
		private async Task FailInFlightMigrationAsync(int departmentId, string errorCode, CancellationToken cancellationToken)
		{
			var policy = await _policyRepository.GetByDepartmentIdAsync(departmentId);
			if (policy == null)
				return;

			var state = (DepartmentDataProtectionState)policy.State;
			if (state != DepartmentDataProtectionState.ProvisioningKey &&
				state != DepartmentDataProtectionState.Encrypting &&
				state != DepartmentDataProtectionState.Verifying &&
				state != DepartmentDataProtectionState.Decrypting &&
				state != DepartmentDataProtectionState.Rotating)
				return;

			await _policyRepository.TryTransitionStateAsync(departmentId, state, DepartmentDataProtectionState.Failed,
				policy.ActiveMigrationKind, WorkerIdentity, cancellationToken);
			await _protectionService.InvalidateProtectionCacheAsync(departmentId);
			Logging.LogError($"ADP migration for department {departmentId} marked Failed ({errorCode}); resumable from its cursor.");
		}

		private async Task NotifyFailureAsync(int departmentId)
		{
			await NotifyAdminsAsync(departmentId,
				"Advanced Data Protection: tonight's migration run could not complete and will resume after review. Your department is in full service and your data remains safe. Support has been alerted.");
		}

		/// <summary>
		/// Emails every department admin (managing member included) with value-free content only —
		/// counts and states, never protected values (plan section 19.5). Notification failure never
		/// fails the migration.
		/// </summary>
		private async Task NotifyAdminsAsync(int departmentId, string message)
		{
			try
			{
				var admins = await _departmentsService.GetAllAdminsForDepartmentAsync(departmentId);
				if (admins == null)
					return;

				foreach (var admin in admins)
				{
					try
					{
						await _emailService.SendNotificationAsync(admin.UserId, message, departmentId);
					}
					catch (Exception ex)
					{
						Logging.LogException(ex, $"ADP migration notification to user {admin.UserId} failed for department {departmentId}");
					}
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"ADP migration notification fan-out failed for department {departmentId}");
			}
		}

		private static string Summarize(List<string> parts) =>
			parts.Count == 0 ? "ADP migration sweep: no work" : "ADP migration sweep: " + string.Join("; ", parts);
	}
}
