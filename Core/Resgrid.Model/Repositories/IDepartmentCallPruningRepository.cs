using System.Collections.Generic;

namespace Resgrid.Model.Repositories
{
	public interface IDepartmentCallPruningRepository : IRepository<DepartmentCallPruning>
	{
		List<DepartmentCallPruning> GetAllDepartmentCallPrunings();
		DepartmentCallPruning GetDepartmentCallPruningSettings(int departmentId);
	}
}
