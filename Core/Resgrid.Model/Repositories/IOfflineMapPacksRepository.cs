using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>Offline map packs (OfflineMapPacks, M0309).</summary>
	public interface IOfflineMapPacksRepository : IRepository<OfflineMapPack>
	{
		Task<OfflineMapPack> GetByPackKeyAsync(string packKey);

		Task<List<OfflineMapPack>> GetByPackKeysAsync(IReadOnlyCollection<string> packKeys);

		/// <summary>The department's own packs (not the shared base-map cells).</summary>
		Task<List<OfflineMapPack>> GetForDepartmentAsync(int departmentId);

		/// <summary>Queued packs, oldest first.</summary>
		Task<List<OfflineMapPack>> GetQueuedAsync(int max);

		/// <summary>Claims a queued pack for building; false when another worker took it first.</summary>
		Task<bool> TryClaimAsync(string offlineMapPackId, CancellationToken cancellationToken = default);

		/// <summary>Packs building since before <paramref name="olderThan"/> (their worker stopped).</summary>
		Task<List<OfflineMapPack>> GetStuckBuildingAsync(DateTime olderThan);

		/// <summary>Packs last requested before <paramref name="cutoff"/> (retention).</summary>
		Task<List<OfflineMapPack>> GetNotRequestedSinceAsync(DateTime cutoff, int max);

		/// <summary>Marks the packs as requested now (retention clock), without loading them.</summary>
		Task<int> TouchAsync(IReadOnlyCollection<string> offlineMapPackIds, CancellationToken cancellationToken = default);

		Task<int> DeleteForDepartmentAsync(int departmentId, CancellationToken cancellationToken = default);
	}
}
