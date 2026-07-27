using System.Threading.Tasks;
using System;
using System.Threading;

namespace Resgrid.Model.Repositories
{
	public interface IUnitLocationsMongoRepository
	{
		Task EnsureIndexesAsync();
		Task<UnitLocationWriteResult> InsertAsync(UnitsLocation location);
		Task<UnitLocationWriteResult> UpdateAsync(UnitsLocation location);
		Task<int> DeleteHardwareLocationsBeforeAsync(
			int departmentId,
			DateTime cutoffUtc,
			int batchSize,
			CancellationToken cancellationToken = default);
	}
}
