using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	public interface IRoutePlansRepository : IRepository<RoutePlan>
	{
		Task<IEnumerable<RoutePlan>> GetRoutePlansByDepartmentIdAsync(int departmentId);
		Task<IEnumerable<RoutePlan>> GetRoutePlansByUnitIdAsync(int unitId);
		Task<IEnumerable<RoutePlan>> GetActiveRoutePlansByDepartmentIdAsync(int departmentId);
	}
}
