using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	public interface IPersonnelLocationsDocRepository
	{
		Task<List<PersonnelLocation>> GetAllLocationsByUnitIdAsync(string userId);
		Task<PersonnelLocation> GetLatestLocationsByUnitIdAsync(string userId);
		Task<List<PersonnelLocation>> GetLatestLocationsByDepartmentIdAsync(int departmentId);
		Task<PersonnelLocation> GetByIdAsync(string id);
		Task<PersonnelLocation> GetByOldIdAsync(string id);
		Task<PersonnelLocation> InsertAsync(PersonnelLocation location);
	}
}
