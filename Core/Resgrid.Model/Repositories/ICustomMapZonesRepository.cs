using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface ICustomMapZonesRepository
	/// Implements the <see cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.CustomMapZone}" />
	/// </summary>
	public interface ICustomMapZonesRepository : IRepository<CustomMapZone>
	{
		/// <summary>
		/// Gets all active zones for a given floor.
		/// </summary>
		Task<IEnumerable<CustomMapZone>> GetZonesByFloorIdAsync(string customMapFloorId);

		/// <summary>
		/// Gets all searchable zones for an entire custom map (across all floors).
		/// </summary>
		Task<IEnumerable<CustomMapZone>> GetSearchableZonesByMapIdAsync(string customMapId);
	}
}

