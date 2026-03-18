using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	public interface IRouteStopsRepository : IRepository<RouteStop>
	{
		Task<IEnumerable<RouteStop>> GetStopsByRoutePlanIdAsync(string routePlanId);
		Task<IEnumerable<RouteStop>> GetStopsByCallIdAsync(int callId);
		Task<IEnumerable<RouteStop>> GetStopsByContactIdAsync(string contactId);
	}
}
