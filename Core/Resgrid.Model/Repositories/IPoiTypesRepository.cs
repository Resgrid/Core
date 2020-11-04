using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface IPoiTypesRepository
	/// Implements the <see cref="PoiType" />
	/// </summary>
	/// <seealso cref="PoiType" />
	public interface IPoiTypesRepository: IRepository<PoiType>
	{
		/// <summary>
		/// Gets the poi types by department identifier asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;PoiType&gt;&gt;.</returns>
		Task<IEnumerable<PoiType>> GetPoiTypesByDepartmentIdAsync(int departmentId);

		/// <summary>
		/// Gets the poi type by type identifier asynchronous.
		/// </summary>
		/// <param name="poiTypeId">The poi type identifier.</param>
		/// <returns>Task&lt;PoiType&gt;.</returns>
		Task<PoiType> GetPoiTypeByTypeIdAsync(int poiTypeId);
	}
}
