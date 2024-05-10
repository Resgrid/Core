using Resgrid.Model.Identity;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface IIdentityRepository
	/// </summary>
	public interface IIdentityRepository
	{
		/// <summary>
		/// Gets all.
		/// </summary>
		/// <returns>List&lt;IdentityUser&gt;.</returns>
		List<IdentityUser> GetAll();

		/// <summary>
		/// Gets the user by identifier.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <returns>IdentityUser.</returns>
		IdentityUser GetUserById(string userId);

		/// <summary>
		/// Gets the user by email.
		/// </summary>
		/// <param name="email">The email.</param>
		/// <returns>IdentityUser.</returns>
		IdentityUser GetUserByEmail(string email);

		/// <summary>
		/// Updates the specified user.
		/// </summary>
		/// <param name="user">The user.</param>
		/// <returns>IdentityUser.</returns>
		IdentityUser Update(IdentityUser user);

		/// <summary>
		/// Updates the username.
		/// </summary>
		/// <param name="oldUsername">The old username.</param>
		/// <param name="newUsername">The new username.</param>
		void UpdateUsername(string oldUsername, string newUsername);

		/// <summary>
		/// Adds the user to role.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <param name="roleId">The role identifier.</param>
		void AddUserToRole(string userId, string roleId);

		/// <summary>
		/// Gets the role for user role.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <param name="roleId">The role identifier.</param>
		/// <returns>IdentityUserRole.</returns>
		IdentityUserRole GetRoleForUserRole(string userId, string roleId);

		/// <summary>
		/// Gets all memberships for department.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>List&lt;IdentityUser&gt;.</returns>
		List<IdentityUser> GetAllMembershipsForDepartment(int departmentId);

		/// <summary>
		/// Gets all users for department.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>List&lt;IdentityUser&gt;.</returns>
		List<IdentityUser> GetAllUsersForDepartment(int departmentId);

		/// <summary>
		/// Gets all users for department within limits.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="retrieveHidden">if set to <c>true</c> [retrieve hidden].</param>
		/// <returns>List&lt;IdentityUser&gt;.</returns>
		List<IdentityUser> GetAllUsersForDepartmentWithinLimits(int departmentId, bool retrieveHidden);

		/// <summary>
		/// Gets all users created after timestamp.
		/// </summary>
		/// <param name="timestamp">The timestamp.</param>
		/// <returns>List&lt;IdentityUser&gt;.</returns>
		List<IdentityUser> GetAllUsersCreatedAfterTimestamp(DateTime timestamp);

		/// <summary>
		/// Initializes the user ext information.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		void InitUserExtInfo(string userId);

		/// <summary>
		/// Gets all users for group.
		/// </summary>
		/// <param name="groupId">The group identifier.</param>
		/// <returns>List&lt;IdentityUser&gt;.</returns>
		List<IdentityUser> GetAllUsersForGroup(int groupId);

		/// <summary>
		/// Gets all users groups and roles.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="retrieveHidden">if set to <c>true</c> [retrieve hidden].</param>
		/// <param name="retrieveDisabled">if set to <c>true</c> [retrieve disabled].</param>
		/// <param name="retrieveDeleted">if set to <c>true</c> [retrieve deleted].</param>
		/// <returns>List&lt;UserGroupRole&gt;.</returns>
		Task<List<UserGroupRole>> GetAllUsersGroupsAndRolesAsync(int departmentId, bool retrieveHidden, bool retrieveDisabled, bool retrieveDeleted);

		/// <summary>
		/// Updates the email.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <param name="newEmail">The new email.</param>
		void UpdateEmail(string userId, string newEmail);

		/// <summary>
		/// Gets the user by user name asynchronous.
		/// </summary>
		/// <param name="userName">Name of the user.</param>
		/// <returns>Task&lt;IdentityUser&gt;.</returns>
		Task<IdentityUser> GetUserByUserNameAsync(string userName);

		/// <summary>
		/// Gets all users for department within limits asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="retrieveHidden">if set to <c>true</c> [retrieve hidden].</param>
		/// <returns>Task&lt;List&lt;IdentityUser&gt;&gt;.</returns>
		Task<List<IdentityUser>> GetAllUsersForDepartmentWithinLimitsAsync(int departmentId, bool retrieveHidden);

		Task<bool> CleanUpOIDCTokensAsync(DateTime timestamp);

		Task<bool> ClearOutUserLoginAsync(string userId);

		Task<bool> CleanUpOIDCTokensByUserAsync(string userId);
	}
}
