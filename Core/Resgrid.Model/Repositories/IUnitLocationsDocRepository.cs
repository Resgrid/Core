using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	public interface IUnitLocationsDocRepository
	{
		Task<List<UnitsLocation>> GetAllLocationsByUnitIdAsync(int unitId);
		Task<UnitsLocation> GetLatestLocationsByUnitIdAsync(int unitId);
		Task<List<UnitsLocation>> GetLatestLocationsByDepartmentIdAsync(int departmentId);
		Task<UnitsLocation> GetByIdAsync(string id);
		Task<UnitsLocation> GetByOldIdAsync(string id);
		Task<UnitsLocation> InsertAsync(UnitsLocation location);
	}
}
