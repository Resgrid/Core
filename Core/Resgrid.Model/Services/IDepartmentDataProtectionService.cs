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

		/// <summary>Atomically bumps the department policy epoch (grant revocation); returns the new epoch.</summary>
		Task<long> IncrementPolicyEpochAsync(int departmentId, string updatedByUserId, CancellationToken cancellationToken = default);

		/// <summary>Drops the department's cached policy/egress state (called after every mutation).</summary>
		Task InvalidateProtectionCacheAsync(int departmentId);
	}
}
