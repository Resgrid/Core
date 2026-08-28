using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Department operation lock control plane (ADP plan section 20). While a department lock is
	/// active, department-scoped mutations are refused (423 department_locked) and reads continue.
	/// Enforcement callers use <see cref="IsDepartmentLockedAsync"/> on the hot path (short-TTL cache
	/// with immediate invalidation on change); the ADP migration worker owns apply/heartbeat/release.
	/// A lock whose heartbeat has gone stale past ExpiresUtc no longer enforces — dispatch
	/// availability beats migration progress.
	/// </summary>
	public interface IDepartmentLockService
	{
		/// <summary>
		/// True when the department has an active, unexpired lock. Served from a short-TTL cache; an
		/// expired lock reports false immediately even before the sweep releases it durably.
		/// </summary>
		Task<bool> IsDepartmentLockedAsync(int departmentId);

		/// <summary>The department's active lock row, or null. Expired locks are still returned (callers see ExpiresUtc).</summary>
		Task<DepartmentOperationLock> GetActiveLockAsync(int departmentId, bool bypassCache = false);

		/// <summary>Every active lock across departments (BackOffice Locks view).</summary>
		Task<IReadOnlyList<DepartmentOperationLock>> GetAllActiveLocksAsync();

		/// <summary>
		/// Acquires the department's single active lock. Returns the created lock, or null when
		/// another active lock already exists (the invariant is enforced by the database).
		/// </summary>
		Task<DepartmentOperationLock> ApplyLockAsync(int departmentId, DepartmentOperationLockType lockType,
			string reason, string correlationId, string appliedByIdentity, DateTime expiresUtc,
			DateTime? projectedEndUtc, CancellationToken cancellationToken = default);

		/// <summary>Advances the worker heartbeat; optionally extends the safety valve. False when the lock is gone.</summary>
		Task<bool> HeartbeatAsync(int departmentOperationLockId, DateTime? newExpiresUtc = null,
			CancellationToken cancellationToken = default);

		/// <summary>Releases an active lock (Completed/Checkpoint/Aborted). False when it was already released.</summary>
		Task<bool> ReleaseLockAsync(int departmentOperationLockId, DepartmentOperationLockReleaseKind kind,
			string releasedBy, CancellationToken cancellationToken = default);

		/// <summary>
		/// Liveness sweep: durably releases (as Expired) every active lock whose ExpiresUtc has passed
		/// with a stale heartbeat. Returns the released locks so the caller can mark their migrations
		/// Failed and page operators.
		/// </summary>
		Task<IReadOnlyList<DepartmentOperationLock>> ReleaseExpiredLocksAsync(CancellationToken cancellationToken = default);

		/// <summary>Drops the department's cached lock state immediately (called on every apply/release).</summary>
		Task InvalidateLockCacheAsync(int departmentId);
	}
}
