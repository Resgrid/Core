using System.Threading.Tasks;
using System;
using System.Threading;

namespace Resgrid.Model.Repositories
{
	public interface IUnitLocationsMongoRepository
	{
		Task EnsureIndexesAsync();
		Task<UnitLocationWriteResult> InsertAsync(UnitsLocation location, CancellationToken cancellationToken = default);
		Task<UnitLocationWriteResult> UpdateAsync(UnitsLocation location, CancellationToken cancellationToken = default);
		Task<int> DeleteHardwareLocationsBeforeAsync(
			int departmentId,
			DateTime cutoffUtc,
			int batchSize,
			CancellationToken cancellationToken = default);
	}
}
