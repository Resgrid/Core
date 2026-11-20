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

		/// <summary>The department's in-progress and paused instances, without route geometry or notes (summary read).</summary>
		Task<IEnumerable<RouteInstance>> GetActiveInstancesByDepartmentIdAsync(int departmentId);

		/// <summary>Every run started for the call (M0322 <see cref="RouteInstance.CallId"/>), newest first.</summary>
		Task<IEnumerable<RouteInstance>> GetInstancesByCallIdAsync(int departmentId, int callId);
	}
}
