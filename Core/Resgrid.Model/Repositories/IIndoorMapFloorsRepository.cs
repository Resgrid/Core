using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	public interface IIndoorMapFloorsRepository : IRepository<IndoorMapFloor>
	{
		Task<IEnumerable<IndoorMapFloor>> GetFloorsByIndoorMapIdAsync(string indoorMapId);
	}
}
