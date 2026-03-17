using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	public interface IRouteInstanceStopsRepository : IRepository<RouteInstanceStop>
	{
		Task<IEnumerable<RouteInstanceStop>> GetStopsByRouteInstanceIdAsync(string routeInstanceId);
	}
}
