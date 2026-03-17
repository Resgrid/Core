using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	public interface IRouteInstancesRepository : IRepository<RouteInstance>
	{
		Task<IEnumerable<RouteInstance>> GetInstancesByDepartmentIdAsync(int departmentId);
		Task<IEnumerable<RouteInstance>> GetActiveInstancesByUnitIdAsync(int unitId);
		Task<IEnumerable<RouteInstance>> GetInstancesByRoutePlanIdAsync(string routePlanId);
		Task<IEnumerable<RouteInstance>> GetInstancesByDateRangeAsync(int departmentId, DateTime startDate, DateTime endDate);
	}
}
