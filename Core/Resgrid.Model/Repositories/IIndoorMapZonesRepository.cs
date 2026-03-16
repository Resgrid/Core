using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	public interface IIndoorMapZonesRepository : IRepository<IndoorMapZone>
	{
		Task<IEnumerable<IndoorMapZone>> GetZonesByFloorIdAsync(string indoorMapFloorId);
		Task<IEnumerable<IndoorMapZone>> SearchZonesAsync(int departmentId, string searchTerm);
	}
}
