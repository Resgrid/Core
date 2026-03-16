using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	public interface ICustomMapService
	{
		// Maps CRUD
		Task<IndoorMap> SaveCustomMapAsync(IndoorMap map, CancellationToken cancellationToken = default(CancellationToken));
		Task<IndoorMap> GetCustomMapByIdAsync(string mapId);
		Task<List<IndoorMap>> GetCustomMapsForDepartmentAsync(int departmentId, CustomMapType? filterType = null);
		Task<bool> DeleteCustomMapAsync(string mapId, CancellationToken cancellationToken = default(CancellationToken));

		// Layers CRUD
		Task<IndoorMapFloor> SaveLayerAsync(IndoorMapFloor layer, CancellationToken cancellationToken = default(CancellationToken));
		Task<IndoorMapFloor> GetLayerByIdAsync(string layerId);
		Task<List<IndoorMapFloor>> GetLayersForMapAsync(string mapId);
		Task<bool> DeleteLayerAsync(string layerId, CancellationToken cancellationToken = default(CancellationToken));

		// Regions CRUD
		Task<IndoorMapZone> SaveRegionAsync(IndoorMapZone region, CancellationToken cancellationToken = default(CancellationToken));
		Task<IndoorMapZone> GetRegionByIdAsync(string regionId);
		Task<List<IndoorMapZone>> GetRegionsForLayerAsync(string layerId);
		Task<bool> DeleteRegionAsync(string regionId, CancellationToken cancellationToken = default(CancellationToken));

		// Dispatch integration
		Task<List<IndoorMapZone>> SearchRegionsAsync(int departmentId, string searchTerm);
		Task<string> GetRegionDisplayNameAsync(string regionId);

		// Tile processing
		Task ProcessAndStoreTilesAsync(string layerId, byte[] imageData, CancellationToken cancellationToken = default(CancellationToken));
		Task DeleteTilesForLayerAsync(string layerId, CancellationToken cancellationToken = default(CancellationToken));
		Task<CustomMapTile> GetTileAsync(string layerId, int z, int x, int y);

		// Geo imports
		Task<CustomMapImport> ImportGeoJsonAsync(string mapId, string layerId, string geoJsonString, string userId, CancellationToken cancellationToken = default(CancellationToken));
		Task<CustomMapImport> ImportKmlAsync(string mapId, string layerId, Stream kmlStream, bool isKmz, string userId, CancellationToken cancellationToken = default(CancellationToken));
		Task<List<CustomMapImport>> GetImportsForMapAsync(string mapId);
	}
}
