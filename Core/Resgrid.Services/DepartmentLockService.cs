using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Department operation lock control plane. See <see cref="IDepartmentLockService"/> for the
	/// contract. The hot path (IsDepartmentLockedAsync) is a short-TTL cache read; apply/release
	/// invalidate immediately so enforcement follows lock changes within one cache miss. Expired
	/// locks stop enforcing the moment ExpiresUtc passes, before any durable sweep runs.
	/// </summary>
	public class DepartmentLockService : IDepartmentLockService
	{
		private const string ActiveLockCacheKey = "DeptOpLock_{0}";
		private static readonly TimeSpan CacheLength = TimeSpan.FromSeconds(30);

		private readonly IDepartmentOperationLockRepository _departmentOperationLockRepository;
		private readonly ICacheProvider _cacheProvider;

		public DepartmentLockService(IDepartmentOperationLockRepository departmentOperationLockRepository,
			ICacheProvider cacheProvider)
		{
			_departmentOperationLockRepository = departmentOperationLockRepository;
			_cacheProvider = cacheProvider;
		}

		public async Task<bool> IsDepartmentLockedAsync(int departmentId)
		{
			try
			{
				var activeLock = await GetActiveLockAsync(departmentId);

				// A blank cache-poisoned entity (Id 0) or an expired safety valve never enforces.
				if (activeLock == null || activeLock.DepartmentOperationLockId <= 0)
					return false;

				return activeLock.ExpiresUtc > DateTime.UtcNow;
			}
			catch (Exception ex)
			{
				// Fail open by design: the lock exists to protect a migration, but a lock-store outage
				// must never take dispatch down — dispatch availability beats migration progress. The
				// migration worker separately refuses to proceed when it cannot verify its own lock.
				Logging.LogException(ex, $"DepartmentLockService.IsDepartmentLockedAsync failed for department {departmentId}; failing open (unlocked)");
				return false;
			}
		}

		public async Task<DepartmentOperationLock> GetActiveLockAsync(int departmentId, bool bypassCache = false)
		{
			async Task<DepartmentOperationLock> getActiveLock()
			{
				return await _departmentOperationLockRepository.GetActiveByDepartmentIdAsync(departmentId);
			}

			if (!bypassCache && Config.SystemBehaviorConfig.CacheEnabled)
			{
				var cached = await _cacheProvider.RetrieveAsync<DepartmentOperationLock>(
					string.Format(ActiveLockCacheKey, departmentId), getActiveLock, CacheLength);

				// Guard against blank-entity cache poisoning: an empty payload deserializes to a
				// non-null entity with default values, which must read as "no lock".
				if (cached == null || cached.DepartmentOperationLockId <= 0)
					return null;

				return cached;
			}

			return await getActiveLock();
		}

		public Task<IReadOnlyList<DepartmentOperationLock>> GetAllActiveLocksAsync()
		{
			return _departmentOperationLockRepository.GetAllActiveAsync();
		}

		public async Task<DepartmentOperationLock> ApplyLockAsync(int departmentId, DepartmentOperationLockType lockType,
			string reason, string correlationId, string appliedByIdentity, DateTime expiresUtc,
			DateTime? projectedEndUtc, CancellationToken cancellationToken = default)
		{
			var utcNow = DateTime.UtcNow;
			var departmentLock = new DepartmentOperationLock
			{
				DepartmentId = departmentId,
				LockType = (int)lockType,
				Reason = reason,
				CorrelationId = correlationId,
				AppliedUtc = utcNow,
				AppliedByIdentity = appliedByIdentity,
				HeartbeatUtc = utcNow,
				ExpiresUtc = expiresUtc,
				ProjectedEndUtc = projectedEndUtc
			};

			var acquired = await _departmentOperationLockRepository.TryAcquireAsync(departmentLock, cancellationToken);

			await InvalidateLockCacheAsync(departmentId);

			return acquired ? departmentLock : null;
		}

		public async Task<bool> HeartbeatAsync(int departmentOperationLockId, DateTime? newExpiresUtc = null,
			CancellationToken cancellationToken = default)
		{
			var rows = await _departmentOperationLockRepository.HeartbeatAsync(departmentOperationLockId,
				DateTime.UtcNow, newExpiresUtc, cancellationToken);
			return rows > 0;
		}

		public async Task<bool> ReleaseLockAsync(int departmentOperationLockId, DepartmentOperationLockReleaseKind kind,
			string releasedBy, CancellationToken cancellationToken = default)
		{
			// The row is fetched first so the department's cache entry can be invalidated after release.
			var lockRow = await _departmentOperationLockRepository.GetByIdAsync(departmentOperationLockId);

			var rows = await _departmentOperationLockRepository.ReleaseAsync(departmentOperationLockId, kind,
				releasedBy, DateTime.UtcNow, cancellationToken);

			if (lockRow != null)
				await InvalidateLockCacheAsync(lockRow.DepartmentId);

			return rows > 0;
		}

		public async Task<IReadOnlyList<DepartmentOperationLock>> ReleaseExpiredLocksAsync(CancellationToken cancellationToken = default)
		{
			var utcNow = DateTime.UtcNow;
			var released = new List<DepartmentOperationLock>();

			foreach (var activeLock in await _departmentOperationLockRepository.GetAllActiveAsync())
			{
				if (activeLock.ExpiresUtc > utcNow)
					continue;

				var rows = await _departmentOperationLockRepository.ReleaseAsync(activeLock.DepartmentOperationLockId,
					DepartmentOperationLockReleaseKind.Expired, "system:lock-expiry-sweep", utcNow, cancellationToken);

				if (rows > 0)
				{
					released.Add(activeLock);
					await InvalidateLockCacheAsync(activeLock.DepartmentId);
					Logging.LogError($"Department operation lock {activeLock.DepartmentOperationLockId} for department {activeLock.DepartmentId} expired with a stale heartbeat and was force-released; the owning migration must be marked Failed at its cursor.");
				}
			}

			return released;
		}

		public async Task InvalidateLockCacheAsync(int departmentId)
		{
			await _cacheProvider.RemoveAsync(string.Format(ActiveLockCacheKey, departmentId));
		}
	}
}
