using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>Background POI imports (PoiImports, M0313).</summary>
	public interface IPoiImportsRepository : IRepository<PoiImport>
	{
		/// <summary>The department's most recent imports, newest first.</summary>
		Task<List<PoiImport>> GetRecentForDepartmentAsync(int departmentId, int max);

		/// <summary>
		/// Imports a worker can take, oldest first: queued ones, and running ones whose heartbeat is older than
		/// <paramref name="staleBefore"/> (or was released) because their worker stopped.
		/// </summary>
		Task<List<PoiImport>> GetRunnableAsync(DateTime staleBefore, int max);

		/// <summary>
		/// Claims an import that is still in <paramref name="expectedStatus"/> (and, when running, still stale), moving it to
		/// <paramref name="newStatus"/>; false when another worker took it first.
		/// </summary>
		Task<bool> TryClaimAsync(string poiImportId, int expectedStatus, int newStatus, DateTime staleBefore, CancellationToken cancellationToken = default);

		/// <summary>Marks a running import as alive.</summary>
		Task HeartbeatAsync(string poiImportId, CancellationToken cancellationToken = default);

		/// <summary>Clears a running import's heartbeat so the next worker tick resumes it.</summary>
		Task ReleaseAsync(string poiImportId, CancellationToken cancellationToken = default);

		/// <summary>How many POIs the import has saved so far.</summary>
		Task<int> CountSavedPoisAsync(string poiImportId, CancellationToken cancellationToken = default);

		/// <summary>Deletes finished imports completed (or added) before <paramref name="cutoff"/>.</summary>
		Task<int> DeleteFinishedBeforeAsync(DateTime cutoff, CancellationToken cancellationToken = default);
	}
}
