using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	public interface IDepartmentGroupsRepository : IRepository<DepartmentGroup>
	{
		Task<List<DepartmentGroup>> GetAllGroupsForDepartmentUnlimitedAsync(int departmentId);
		List<DepartmentGroup> GetAllStationGroupsForDepartment(int departmentId);
	}
}
