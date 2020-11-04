using System.Collections.Generic;
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
	}
}
