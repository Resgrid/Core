using System.Threading.Tasks;
using System;
using System.Threading;

namespace Resgrid.Model.Repositories
{
	public interface IUnitLocationsMongoRepository
	{
		Task EnsureIndexesAsync();

		/// <summary>
		/// Indexes only reads use (map visualization history). Built by the document database update, never on the write
		/// path: building one on a large collection would hold every process's first location write until it finished.
		/// </summary>
		Task EnsureReadIndexesAsync();
		Task<UnitLocationWriteResult> InsertAsync(UnitsLocation location, CancellationToken cancellationToken = default);
		Task<UnitLocationWriteResult> UpdateAsync(UnitsLocation location, CancellationToken cancellationToken = default);
		Task<int> DeleteHardwareLocationsBeforeAsync(
			int departmentId,
			DateTime cutoffUtc,
			int batchSize,
			CancellationToken cancellationToken = default);
	}
}
