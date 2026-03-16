using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	public interface ICustomMapTilesRepository : IRepository<CustomMapTile>
	{
		Task<CustomMapTile> GetTileAsync(string layerId, int zoomLevel, int tileX, int tileY);
		Task<IEnumerable<CustomMapTile>> GetTilesForLayerAsync(string layerId);
		Task<bool> DeleteTilesForLayerAsync(string layerId, CancellationToken cancellationToken = default(CancellationToken));
	}
}
