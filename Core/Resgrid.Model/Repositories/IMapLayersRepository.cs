using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	public interface IMapLayersDocRepository
	{
		Task<List<MapLayer>> GetAllMapLayersByDepartmentIdAsync(int departmentId, MapLayerTypes type);
		Task<MapLayer> GetByIdAsync(string id);
		Task<MapLayer> GetByOldIdAsync(string id);
		Task<MapLayer> InsertAsync(MapLayer mapLayer);
		Task<MapLayer> UpdateAsync(MapLayer mapLayer);
	}
}
