using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	public interface IRouteSchedulesRepository : IRepository<RouteSchedule>
	{
		Task<IEnumerable<RouteSchedule>> GetSchedulesByRoutePlanIdAsync(string routePlanId);
		Task<IEnumerable<RouteSchedule>> GetActiveSchedulesDueAsync(DateTime asOfDate);
	}
}
