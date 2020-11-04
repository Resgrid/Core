using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	public interface IPersonnelRolesService
	{
		/// <summary>
		/// Gets the roles for department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;List&lt;PersonnelRole&gt;&gt;.</returns>
		Task<List<PersonnelRole>> GetRolesForDepartmentAsync(int departmentId);
		/// <summary>
		/// Gets the roles for department unlimited asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;List&lt;PersonnelRole&gt;&gt;.</returns>
		Task<List<PersonnelRole>> GetRolesForDepartmentUnlimitedAsync(int departmentId);

		/// <summary>
		/// Gets the role by identifier asynchronous.
		/// </summary>
		/// <param name="roleId">The role identifier.</param>
		/// <returns>Task&lt;PersonnelRole&gt;.</returns>
		Task<PersonnelRole> GetRoleByIdAsync(int roleId);

		/// <summary>
		/// Gets all roles for department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;List&lt;PersonnelRole&gt;&gt;.</returns>
		Task<List<PersonnelRole>> GetAllRolesForDepartmentAsync(int departmentId);


		/// <summary>
		/// Saves the role asynchronous.
		/// </summary>
		/// <param name="role">The role.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;PersonnelRole&gt;.</returns>
		Task<PersonnelRole> SaveRoleAsync(PersonnelRole role, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Gets the role by department and name asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="name">The name.</param>
		/// <returns>Task&lt;PersonnelRole&gt;.</returns>
		Task<PersonnelRole> GetRoleByDepartmentAndNameAsync(int departmentId, string name);

		/// <summary>
		/// Deletes the role by identifier asynchronous.
		/// </summary>
		/// <param name="roleId">The role identifier.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> DeleteRoleByIdAsync(int roleId, CancellationToken cancellationToken = default(CancellationToken));


		/// <summary>
		/// Deletes the role users asynchronous.
		/// </summary>
		/// <param name="users">The users.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> DeleteRoleUsersAsync(List<PersonnelRoleUser> users, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Gets the roles for user asynchronous.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;List&lt;PersonnelRole&gt;&gt;.</returns>
		Task<List<PersonnelRole>> GetRolesForUserAsync(string userId, int departmentId);

		/// <summary>
		/// Gets all roles for users in department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;Dictionary&lt;System.String, List&lt;PersonnelRole&gt;&gt;&gt;.</returns>
		Task<Dictionary<string, List<PersonnelRole>>> GetAllRolesForUsersInDepartmentAsync(int departmentId);


		/// <summary>
		/// Removes the user from all roles asynchronous.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> RemoveUserFromAllRolesAsync(string userId, int departmentId, CancellationToken cancellationToken = default(CancellationToken));


		/// <summary>
		/// Sets the roles for user asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="userId">The user identifier.</param>
		/// <param name="roleIds">The role ids.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> SetRolesForUserAsync(int departmentId, string userId, string[] roleIds, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Gets all members of role asynchronous.
		/// </summary>
		/// <param name="roleId">The role identifier.</param>
		/// <returns>Task&lt;List&lt;PersonnelRoleUser&gt;&gt;.</returns>
		Task<List<PersonnelRoleUser>> GetAllMembersOfRoleAsync(int roleId);
	}
}
