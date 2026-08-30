using System;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Advanced Data Protection policy/state orchestration for departments. Owns the durable
	/// DepartmentDataProtectionPolicies state machine (the single data-safety truth), the per-channel
	/// egress policy, and the server-enforced enrollment/offboarding command gates: managing member
	/// only, active paid ADP addon, and a fresh authoritative global-gate evaluation — never trusting
	/// client/UI state. Bulk state transitions beyond queueing are made only by the ADP migration
	/// worker. This service performs no cryptography.
	/// </summary>
	public interface IDepartmentDataProtectionService
	{
		/// <summary>The department's policy row, or null when the department has never touched ADP.</summary>
		Task<DepartmentDataProtectionPolicy> GetPolicyByDepartmentIdAsync(int departmentId, bool bypassCache = false);

		/// <summary>Durable protection state; Disabled when no policy row exists.</summary>
		Task<DepartmentDataProtectionState> GetStateAsync(int departmentId, bool bypassCache = false);

		/// <summary>
		/// True when writers must envelope-encrypt newly changed cataloged fields: the department has a
		/// provisioned key and is Encrypting/Verifying(enrollment or rotation)/Enabled/Rotating/
		/// OffboardingScheduled. False for Disabled, queued/provisioning, and the offboarding decrypt
		/// path (DisableRequested/Decrypting/offboarding-Verifying), where new writes stay plaintext so
		/// the decrypt backlog only shrinks.
		/// </summary>
		Task<bool> ShouldEncryptNewWritesAsync(int departmentId);

		/// <summary>
		/// True when protected-data enforcement (grants, shields, redacted projections) applies to
		/// reads: state is Enabled, Rotating or OffboardingScheduled — protection stays fully active
		/// while offboarding is merely scheduled.
		/// </summary>
		Task<bool> IsProtectionEnforcedAsync(int departmentId);

		/// <summary>
		/// The catalog version this department is PINNED at — the version its envelopes were written
		/// under and the one its AAD is computed from. Zero when the department has no policy row.
		/// Never assume the code's current catalog version: a department that enrolled earlier owns
		/// only the fields that existed then, until a catalog upgrade sweeps it.
		/// </summary>
		Task<int> GetPinnedCatalogVersionAsync(int departmentId);

		/// <summary>
		/// True when the code's catalog has advanced past what this protected department was migrated
		/// to, so newly cataloged fields are still landing in plaintext and an upgrade sweep is owed.
		/// False for unprotected departments (nothing to upgrade) and for departments already current.
		/// </summary>
		Task<bool> IsCatalogUpgradePendingAsync(int departmentId);

		/// <summary>
		/// Queues enrollment (Disabled -> EnrollmentQueued) after enforcing, server-side: caller is
		/// Department.ManagingUserId; department is on a paid plan with an active paid ADP addon; a
		/// fresh authoritative (bypass-cache) evaluation of the global admission gate is true; and the
		/// durable state is Disabled. Persists the acknowledgement record, selected overnight window,
		/// and the value-free flag-evaluation audit reference on the policy row.
		/// </summary>
		Task<DepartmentDataProtectionEnrollmentResult> QueueEnrollmentAsync(int departmentId, string requestingUserId,
			string acknowledgementsJson, string windowStartLocal, string windowEndLocal, string windowTimeZone,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Dequeues a not-yet-started enrollment (EnrollmentQueued -> Disabled) at no data cost.
		/// Managing member only.
		/// </summary>
		Task<DepartmentDataProtectionEnrollmentResult> CancelQueuedEnrollmentAsync(int departmentId,
			string requestingUserId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Schedules offboarding (Enabled -> OffboardingScheduled) at the end of the paid cycle.
		/// Protection, grants and egress remain fully active until the offboarding migration runs.
		/// Called from the billing event path (cancellation, exhausted dunning, chargeback).
		/// </summary>
		Task<DepartmentDataProtectionEnrollmentResult> ScheduleOffboardingAsync(int departmentId,
			DepartmentDataProtectionOffboardingSource source, DateTime effectiveOnUtc,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Revokes a scheduled offboarding (OffboardingScheduled -> Enabled) before the first
		/// offboarding window opens. Managing member only; not offered once decryption has begun.
		/// </summary>
		Task<DepartmentDataProtectionEnrollmentResult> RevokeOffboardingAsync(int departmentId,
			string requestingUserId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Per-channel egress policy; when the department has no row, returns an unsaved default with
		/// every channel GenericOnly (the fail-safe posture).
		/// </summary>
		Task<DepartmentProtectedDataEgressPolicy> GetEgressPolicyByDepartmentIdAsync(int departmentId, bool bypassCache = false);

		/// <summary>
		/// Saves the egress policy and increments the department policy epoch, revoking outstanding
		/// grants and forcing send-time re-evaluation of pending protected deliveries.
		/// </summary>
		Task<DepartmentProtectedDataEgressPolicy> SaveEgressPolicyAsync(DepartmentProtectedDataEgressPolicy policy,
			string updatedByUserId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Advisory readiness report for the Enrollment Wizard preflight (plan section 18.1 step 4):
		/// managing member, paid plan, active addon, fresh global-gate evaluation, and Disabled
		/// state. Every check is re-verified inside QueueEnrollmentAsync at commit — this never
		/// substitutes for the command gates.
		/// </summary>
		Task<AdpEnrollmentPreflight> GetEnrollmentPreflightAsync(int departmentId, string requestingUserId,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Row-count progress for the department's in-flight migration, for the wizard status panel
		/// (plan 18). Reads the SAME cursor rows the engine writes, so the panel cannot disagree
		/// with the worker. Returns a not-running report when nothing is in flight.
		/// </summary>
		Task<AdpMigrationProgress> GetMigrationProgressAsync(int departmentId,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Applies an ADP addon billing event to the department's durable protection state
		/// (plan 17.3). Idempotent: providers retry and duplicate webhooks, and an out-of-order
		/// Cancelled-then-Renewed pair must settle on the provider's current truth.
		///
		/// This can only move lifecycle state. It never disables decryption, suppresses grants or
		/// downgrades clients — only the completed offboarding migration changes ciphertext.
		/// </summary>
		Task<DepartmentDataProtectionEnrollmentResult> ApplyAddonBillingEventAsync(AdpAddonBillingEvent billingEvent,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// True when the calling client must complete a second factor before a grant is issued
		/// (plan 3.3). Departments may exempt named apps; everything else prompts.
		///
		/// Fails CLOSED: a lookup that throws, a department with no policy, and any client that is
		/// not exemptable all read as "step up required". The safe answer to "I am not sure" is the
		/// prompt.
		/// </summary>
		Task<bool> IsStepUpRequiredForClientAsync(int departmentId, UserSessionClientApplication client);

		/// <summary>The department's current per-app step-up exemptions (plan 3.3).</summary>
		Task<AdpStepUpExemptClients> GetStepUpExemptClientsAsync(int departmentId, bool bypassCache = false);

		/// <summary>
		/// Replaces the department's step-up exemptions. Managing member only, and every change bumps
		/// the policy epoch: tightening the setting has to invalidate the grants the looser one
		/// already issued, or the change would not bite until they expired on their own.
		///
		/// The caller is responsible for writing the audit record — it needs the request's IP and
		/// user agent, which this layer does not have.
		/// </summary>
		Task<DepartmentDataProtectionEnrollmentResult> SetStepUpExemptClientsAsync(int departmentId,
			AdpStepUpExemptClients exemptions, string requestingUserId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Queues a department key rotation (plan 11.3): provisions the next key version, moves the
		/// current one to Retiring, and sets the department Rotating so the overnight worker re-keys
		/// every envelope. The superseded version is only retired after verification proves nothing
		/// still references it.
		///
		/// Rotation is a RESGRID operation, not a customer one - it runs under the CryptoOps identity
		/// and is triggered from BackOffice. Protection, grants and reads continue throughout: the
		/// department sees a locked overnight window, exactly as it saw at enrollment.
		///
		/// Only an Enabled department can rotate. Anything mid-migration already owns the cursor.
		/// </summary>
		Task<DepartmentDataProtectionEnrollmentResult> QueueKeyRotationAsync(int departmentId,
			string requestingUserId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Puts a Failed migration back in the queue, resuming from its stored cursor (plan 7.4).
		///
		/// A Failed run is deliberately never auto-resumed: whatever stopped it is usually still
		/// there, and a worker that retries on its own turns one bad night into a loop. An operator
		/// clears the cause and calls this. The migration kind on the policy decides which state it
		/// returns to, so an offboarding resumes decrypting and an enrollment resumes encrypting.
		/// </summary>
		Task<DepartmentDataProtectionEnrollmentResult> RetryFailedMigrationAsync(int departmentId,
			string requestingUserId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Stops an in-flight migration window (plan 7.4). Releases the department operation lock as
		/// Aborted, so the worker stops at its next checkpoint, and marks the run Failed with an
		/// operator error code — which is the state that can be retried from the cursor.
		///
		/// Nothing already written is undone. Every row the run has processed stays exactly as it
		/// is; that is what makes stopping safe at any point, and it is why this is an abort rather
		/// than a rollback.
		/// </summary>
		Task<bool> AbortActiveMigrationAsync(int departmentId, string requestingUserId,
			CancellationToken cancellationToken = default);

		/// <summary>Atomically bumps the department policy epoch (grant revocation); returns the new epoch.</summary>
		Task<long> IncrementPolicyEpochAsync(int departmentId, string updatedByUserId, CancellationToken cancellationToken = default);

		/// <summary>Drops the department's cached policy/egress state (called after every mutation).</summary>
		Task InvalidateProtectionCacheAsync(int departmentId);
	}
}
