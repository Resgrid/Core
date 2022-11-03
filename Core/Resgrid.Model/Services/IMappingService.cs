using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	public interface IMappingService
	{
		/// <summary>
		/// Saves the poi type asynchronous.
		/// </summary>
		/// <param name="type">The type.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;PoiType&gt;.</returns>
		Task<PoiType> SavePOITypeAsync(PoiType type, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Saves the poi asynchronous.
		/// </summary>
		/// <param name="poi">The poi.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;Poi&gt;.</returns>
		Task<Poi> SavePOIAsync(Poi poi, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Gets the poi types for department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;List&lt;PoiType&gt;&gt;.</returns>
		Task<List<PoiType>> GetPOITypesForDepartmentAsync(int departmentId);

		/// <summary>
		/// Gets the type by identifier asynchronous.
		/// </summary>
		/// <param name="poiTypeId">The poi type identifier.</param>
		/// <returns>Task&lt;PoiType&gt;.</returns>
		Task<PoiType> GetTypeByIdAsync(int poiTypeId);

		/// <summary>
		/// Deletes the poi type asynchronous.
		/// </summary>
		/// <param name="poiTypeId">The poi type identifier.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> DeletePOITypeAsync(int poiTypeId, CancellationToken cancellationToken = default(CancellationToken));

		Task<MapLayer> SaveMapLayerAsync(MapLayer mapLayer);

		Task<List<MapLayer>> GetMapLayersForTypeDepartmentAsync(int departmentId, MapLayerTypes type);

		Task<MapLayer> GetMapLayersByIdAsync(string id);
	}
}
