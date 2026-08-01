using System.Collections.Generic;
using System.Threading.Tasks;
using System;
using System.Threading;

namespace Resgrid.Model.Repositories
{
	public interface IUnitLocationsDocRepository
	{
		Task<List<UnitsLocation>> GetAllLocationsByUnitIdAsync(int unitId);
		Task<UnitsLocation> GetLatestLocationsByUnitIdAsync(int unitId);
		Task<List<UnitsLocation>> GetLatestLocationsByDepartmentIdAsync(int departmentId);
		Task<UnitsLocation> GetByIdAsync(string id);
		Task<UnitsLocation> GetByOldIdAsync(string id);
		Task<UnitLocationWriteResult> InsertAsync(UnitsLocation location, CancellationToken cancellationToken = default);
		Task<UnitLocationWriteResult> UpdateAsync(UnitsLocation location, CancellationToken cancellationToken = default);
		Task<int> DeleteHardwareLocationsBeforeAsync(
			int departmentId,
			DateTime cutoffUtc,
			int batchSize,
			CancellationToken cancellationToken = default);
	}
}
