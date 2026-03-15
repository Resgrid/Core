using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface ICustomMapsRepository
	/// Implements the <see cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.CustomMap}" />
	/// </summary>
	public interface ICustomMapsRepository : IRepository<CustomMap>
	{
		/// <summary>
		/// Gets all custom maps for a department, including their floors.
		/// </summary>
		Task<IEnumerable<CustomMap>> GetCustomMapsByDepartmentIdAsync(int departmentId);

		/// <summary>
		/// Gets a single custom map by id, including floors and zones.
		/// </summary>
		Task<CustomMap> GetCustomMapByIdWithFloorsAsync(string customMapId);
	}
}
