using System;
using System.Collections.Generic;
using System.Threading;
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

		/// <summary>
		/// Serializes route starts for one unit: takes a lock on the unit, held until the unit of work's transaction ends, so two
		/// starts cannot both find the unit free and both insert a run. Needs the open <c>IUnitOfWork</c> transaction; throws
		/// <see cref="TimeoutException"/> when another start keeps the lock past the wait.
		/// </summary>
		Task LockUnitForRouteStartAsync(int unitId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Records when the run reached its start, only if it has not been recorded yet. Writes that one column, never the rest of
		/// the row (a status the run moved to meanwhile is left alone). True when this call recorded it.
		/// </summary>
		Task<bool> SetStartReachedOnAsync(string routeInstanceId, DateTime reachedOn, CancellationToken cancellationToken = default);
	}
}
