using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface ICustomMapFloorsRepository
	/// Implements the <see cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.CustomMapFloor}" />
	/// </summary>
	public interface ICustomMapFloorsRepository : IRepository<CustomMapFloor>
	{
		/// <summary>
		/// Gets all floors for a given custom map, ordered by SortOrder.
		/// </summary>
		Task<IEnumerable<CustomMapFloor>> GetFloorsByMapIdAsync(string customMapId);

		/// <summary>
		/// Gets a single floor by id, including its zones.
		/// </summary>
		Task<CustomMapFloor> GetFloorByIdWithZonesAsync(string customMapFloorId);
	}
}
