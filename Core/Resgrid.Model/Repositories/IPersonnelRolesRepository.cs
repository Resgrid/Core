using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface IPersonnelRolesRepository
	/// Implements the <see cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.PersonnelRole}" />
	/// </summary>
	/// <seealso cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.PersonnelRole}" />
	public interface IPersonnelRolesRepository: IRepository<PersonnelRole>
	{
		/// <summary>
		/// Gets the role by department and name asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="name">The name.</param>
		/// <returns>Task&lt;PersonnelRole&gt;.</returns>
		Task<PersonnelRole> GetRoleByDepartmentAndNameAsync(int departmentId, string name);

		/// <summary>
		/// Gets the roles for user asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="userId">The user identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;PersonnelRole&gt;&gt;.</returns>
		Task<IEnumerable<PersonnelRole>> GetRolesForUserAsync(int departmentId, string userId);

		/// <summary>
		/// Gets the personnel roles by department identifier asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;PersonnelRole&gt;&gt;.</returns>
		Task<IEnumerable<PersonnelRole>> GetPersonnelRolesByDepartmentIdAsync(int departmentId);

		/// <summary>
		/// Gets the role by role identifier asynchronous.
		/// </summary>
		/// <param name="personnelRoleId">The personnel role identifier.</param>
		/// <returns>Task&lt;PersonnelRole&gt;.</returns>
		Task<PersonnelRole> GetRoleByRoleIdAsync(int personnelRoleId);

		/// <summary>
		/// Removes every row in other tables that points at a personnel role, so the role row itself can
		/// be deleted without tripping a foreign key (CallDispatchRoles has a non-cascading FK on RoleId).
		/// Rows that merely reference the role as an optional qualification (UnitRoles) are nulled out
		/// instead of deleted.
		/// </summary>
		/// <param name="personnelRoleId">The personnel role identifier.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <returns>Task&lt;bool&gt;.</returns>
		Task<bool> DeleteRoleDependenciesAsync(int personnelRoleId, CancellationToken cancellationToken = default(CancellationToken));
	}
}
