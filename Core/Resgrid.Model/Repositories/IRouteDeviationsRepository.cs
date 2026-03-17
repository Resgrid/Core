using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	public interface IRouteDeviationsRepository : IRepository<RouteDeviation>
	{
		Task<IEnumerable<RouteDeviation>> GetDeviationsByRouteInstanceIdAsync(string routeInstanceId);
		Task<IEnumerable<RouteDeviation>> GetUnacknowledgedDeviationsByDepartmentAsync(int departmentId);
	}
}
