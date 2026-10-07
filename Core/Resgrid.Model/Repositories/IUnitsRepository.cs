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
		/// Gets the department's non-deleted unit with this name (unit names are unique among non-deleted units only).
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="name">The name.</param>
		/// <returns>Task&lt;Unit&gt;.</returns>
		Task<Unit> GetUnitByNameDepartmentIdAsync(int departmentId, string name);

		/// <summary>
		/// Gets the non-deleted units stationed in a group.
		/// </summary>
		/// <param name="groupId">The group identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;Unit&gt;&gt;.</returns>
		Task<IEnumerable<Unit>> GetAllUnitsByGroupIdAsync(int groupId);

		/// <summary>
		/// Gets every unit stationed in a group, soft-deleted ones included (they still reference the group).
		/// </summary>
		/// <param name="groupId">The group identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;Unit&gt;&gt;.</returns>
		Task<IEnumerable<Unit>> GetAllUnitsByGroupIdIncludingDeletedAsync(int groupId);

		/// <summary>
		/// Gets the department's non-deleted units of a type.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="type">The type.</param>
		/// <returns>Task&lt;IEnumerable&lt;Unit&gt;&gt;.</returns>
		Task<IEnumerable<Unit>> GetAllUnitsForTypeAsync(int departmentId, string type);

		/// <summary>
		/// Gets the department's non-deleted units (with their roles).
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;Unit&gt;&gt;.</returns>
		Task<IEnumerable<Unit>> GetAllUnitsByDepartmentIdAsync(int departmentId);

		/// <summary>
		/// Gets every unit the department has had, soft-deleted ones included, for resolving point-in-time data.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;Unit&gt;&gt;.</returns>
		Task<IEnumerable<Unit>> GetAllUnitsByDepartmentIdIncludingDeletedAsync(int departmentId);
	}
}
