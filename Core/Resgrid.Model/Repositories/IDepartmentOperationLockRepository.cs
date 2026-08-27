using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	public interface IDepartmentOperationLockRepository : IRepository<DepartmentOperationLock>
	{
		/// <summary>The department's active (unreleased) lock, or null.</summary>
		Task<DepartmentOperationLock> GetActiveByDepartmentIdAsync(int departmentId);

		/// <summary>Every active lock across all departments (BackOffice Locks view).</summary>
		Task<IReadOnlyList<DepartmentOperationLock>> GetAllActiveAsync();

		/// <summary>
		/// Inserts the lock as the department's single active lock. The one-active-lock-per-department
		/// invariant is enforced by a filtered/partial unique index, so a concurrent second acquire
		/// fails at the database rather than racing; returns false in that case without throwing.
		/// </summary>
		Task<bool> TryAcquireAsync(DepartmentOperationLock departmentLock, CancellationToken cancellationToken);

		/// <summary>Advances HeartbeatUtc (and optionally ExpiresUtc) on an active lock; 0 rows = lock gone.</summary>
		Task<int> HeartbeatAsync(int departmentOperationLockId, DateTime heartbeatUtc, DateTime? newExpiresUtc,
			CancellationToken cancellationToken);

		/// <summary>
		/// Releases an active lock with the given kind. Only releases when still active, so a release
		/// racing an expiry cannot double-write; returns rows affected.
		/// </summary>
		Task<int> ReleaseAsync(int departmentOperationLockId, DepartmentOperationLockReleaseKind kind,
			string releasedBy, DateTime releasedUtc, CancellationToken cancellationToken);
	}
}
