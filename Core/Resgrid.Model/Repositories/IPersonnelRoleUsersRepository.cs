using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface IPersonnelRoleUsersRepository
	/// Implements the <see cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.PersonnelRoleUser}" />
	/// </summary>
	/// <seealso cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.PersonnelRoleUser}" />
	public interface IPersonnelRoleUsersRepository: IRepository<PersonnelRoleUser>
	{
		/// <summary>
		/// Gets all role users for user asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="userId">The user identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;PersonnelRoleUser&gt;&gt;.</returns>
		Task<IEnumerable<PersonnelRoleUser>> GetAllRoleUsersForUserAsync(int departmentId, string userId);
		/// <summary>
		/// Gets all members of role asynchronous.
		/// </summary>
		/// <param name="roleId">The role identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;PersonnelRoleUser&gt;&gt;.</returns>
		Task<IEnumerable<PersonnelRoleUser>> GetAllMembersOfRoleAsync(int roleId);

		/// <summary>
		/// Gets all role users for department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;PersonnelRoleUser&gt;&gt;.</returns>
		Task<IEnumerable<PersonnelRoleUser>> GetAllRoleUsersForDepartmentAsync(int departmentId);
	}
}
