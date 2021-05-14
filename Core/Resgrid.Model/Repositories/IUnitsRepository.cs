using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface IUnitsRepository
	/// Implements the <see cref="Unit" />
	/// </summary>
	/// <seealso cref="Unit" />
	public interface IUnitsRepository: IRepository<Unit>
	{
		/// <summary>
		/// Gets the unit by name department identifier asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="name">The name.</param>
		/// <returns>Task&lt;Unit&gt;.</returns>
		Task<Unit> GetUnitByNameDepartmentIdAsync(int departmentId, string name);

		/// <summary>
		/// Gets all units by group identifier asynchronous.
		/// </summary>
		/// <param name="groupId">The group identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;Unit&gt;&gt;.</returns>
		Task<IEnumerable<Unit>> GetAllUnitsByGroupIdAsync(int groupId);

		/// <summary>
		/// Gets all units for type asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="type">The type.</param>
		/// <returns>Task&lt;IEnumerable&lt;Unit&gt;&gt;.</returns>
		Task<IEnumerable<Unit>> GetAllUnitsForTypeAsync(int departmentId, string type);

		/// <summary>
		/// Gets all units by group identifier asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;Unit&gt;&gt;.</returns>
		Task<IEnumerable<Unit>> GetAllUnitsByDepartmentIdAsync(int departmentId);
	}
}
