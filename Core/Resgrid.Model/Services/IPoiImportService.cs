using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// POI uploads (KML, KMZ or CSV). The upload request reads the file and queues it; the POI import worker (worker 79)
	/// geocodes the rows that carry an address but no position and saves the POIs.
	/// </summary>
	public interface IPoiImportService
	{
		/// <summary>Reads the POIs in an upload (the extension of <paramref name="fileName"/> decides the format). Nothing is geocoded or saved.</summary>
		PoiImportFile ReadFile(Stream input, string fileName);

		/// <summary>Queues the rows of a file read by <see cref="ReadFile"/> for the worker to import into the POI type.</summary>
		Task<PoiImport> QueueImportAsync(int departmentId, int poiTypeId, string poiTypeName, string userId, string fileName, PoiImportFile file, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>The department's most recent imports, newest first; only <paramref name="userId"/>'s uploads when given.</summary>
		Task<List<PoiImport>> GetRecentImportsAsync(int departmentId, int max = 10, string userId = null);

		/// <summary>The unlocated rows an import's report names (at most the first hundred).</summary>
		List<PoiImportRow> GetUnlocatedRows(PoiImport import);

		/// <summary>Worker: claims and runs the next runnable import. False when there was nothing to run.</summary>
		Task<bool> ProcessNextAsync(CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>Worker: deletes finished imports older than the retention period. Returns how many were deleted.</summary>
		Task<int> CleanupAsync(CancellationToken cancellationToken = default(CancellationToken));
	}
}
