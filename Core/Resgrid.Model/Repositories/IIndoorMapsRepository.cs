using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	public interface IIndoorMapsRepository : IRepository<IndoorMap>
	{
		Task<IEnumerable<IndoorMap>> GetIndoorMapsByDepartmentIdAsync(int departmentId);
	}
}
