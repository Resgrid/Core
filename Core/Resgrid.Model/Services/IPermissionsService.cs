using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	public interface IPermissionsService
	{
		/// <summary>
		/// Gets all permissions for department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;List&lt;Permission&gt;&gt;.</returns>
		Task<List<Permission>> GetAllPermissionsForDepartmentAsync(int departmentId);

		/// <summary>
		/// Sets the permission for department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="userId">The user identifier.</param>
		/// <param name="type">The type.</param>
		/// <param name="action">The action.</param>
		/// <param name="data">The data.</param>
		/// <param name="lockToGroup">if set to <c>true</c> [lock to group].</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;Permission&gt;.</returns>
		Task<Permission> SetPermissionForDepartmentAsync(int departmentId, string userId, PermissionTypes type,
			PermissionActions action, string data, bool lockToGroup, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Gets the permission by department type asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="type">The type.</param>
		/// <returns>Task&lt;Permission&gt;.</returns>
		Task<Permission> GetPermissionByDepartmentTypeAsync(int departmentId, PermissionTypes type);

		/// <summary>
		/// Determines whether [is user allowed] [the specified permission].
		/// </summary>
		/// <param name="permission">The permission.</param>
		/// <param name="isUserDepartmentAdmin">if set to <c>true</c> [is user department admin].</param>
		/// <param name="isUserGroupAdmin">if set to <c>true</c> [is user group admin].</param>
		/// <param name="roles">The roles.</param>
		/// <returns><c>true</c> if [is user allowed] [the specified permission]; otherwise, <c>false</c>.</returns>
		bool IsUserAllowed(Permission permission, bool isUserDepartmentAdmin, bool isUserGroupAdmin, List<PersonnelRole> roles);

		/// <summary>
		/// Gets the allowed users.
		/// </summary>
		/// <param name="permission">The permission.</param>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="sourceGroupId">The source group identifier.</param>
		/// <param name="isUserDepartmentAdmin">if set to <c>true</c> [is user department admin].</param>
		/// <param name="isUserGroupAdmin">if set to <c>true</c> [is user group admin].</param>
		/// <param name="roles">The roles.</param>
		/// <returns>List&lt;System.String&gt;.</returns>
		Task<List<string>> GetAllowedUsersAsync(Permission permission, int departmentId, int? sourceGroupId, bool isUserDepartmentAdmin, bool isUserGroupAdmin, List<PersonnelRole> roles);

		bool IsUserAllowed(Permission permission, int departmentId, int? sourceGroupId, int? userGroupId,
			bool isUserDepartmentAdmin, bool isUserGroupAdmin, List<PersonnelRole> roles);
	}
}
