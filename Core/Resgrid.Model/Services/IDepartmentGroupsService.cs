using Resgrid.Model.Identity;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Interface IDepartmentGroupsService
	/// </summary>
	public interface IDepartmentGroupsService
	{
		/// <summary>
		/// Gets all asynchronous.
		/// </summary>
		/// <returns>Task&lt;List&lt;DepartmentGroup&gt;&gt;.</returns>
		Task<List<DepartmentGroup>> GetAllAsync();

		/// <summary>
		/// Saves the asynchronous.
		/// </summary>
		/// <param name="departmentGroup">The department group.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;DepartmentGroup&gt;.</returns>
		Task<DepartmentGroup> SaveAsync(DepartmentGroup departmentGroup,
			CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Gets all groups for department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;List&lt;DepartmentGroup&gt;&gt;.</returns>
		Task<List<DepartmentGroup>> GetAllGroupsForDepartmentAsync(int departmentId);

		/// <summary>
		/// Invalidates the group in cache.
		/// </summary>
		/// <param name="groupId">The group identifier.</param>
		Task InvalidateGroupInCache(int groupId);

		/// <summary>
		/// Gets all groups for department unlimited asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;List&lt;DepartmentGroup&gt;&gt;.</returns>
		Task<List<DepartmentGroup>> GetAllGroupsForDepartmentUnlimitedAsync(int departmentId);

		/// <summary>
		/// Gets the group by identifier asynchronous.
		/// </summary>
		/// <param name="departmentGroupId">The department group identifier.</param>
		/// <param name="bypassCache">if set to <c>true</c> [bypass cache].</param>
		/// <returns>Task&lt;DepartmentGroup&gt;.</returns>
		Task<DepartmentGroup> GetGroupByIdAsync(int departmentGroupId, bool bypassCache = true);

		/// <summary>
		/// Deletes the group by identifier asynchronous.
		/// </summary>
		/// <param name="groupId">The group identifier.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> DeleteGroupByIdAsync(int groupId, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Updates the asynchronous.
		/// </summary>
		/// <param name="departmentGroup">The department group.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;DepartmentGroup&gt;.</returns>
		Task<DepartmentGroup> UpdateAsync(DepartmentGroup departmentGroup,
			CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Determines whether [is user in a group asynchronous] [the specified user identifier].
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> IsUserInAGroupAsync(string userId, int departmentId);

		/// <summary>
		/// Determines whether [is user a group admin asynchronous] [the specified user identifier].
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> IsUserAGroupAdminAsync(string userId, int departmentId);

		/// <summary>
		/// Determines whether [is user in a group asynchronous] [the specified user identifier].
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <param name="excludedGroupId">The excluded group identifier.</param>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> IsUserInAGroupAsync(string userId, int excludedGroupId, int departmentId);

		/// <summary>
		/// Deletes the user from groups asynchronous.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> DeleteUserFromGroupsAsync(string userId, int departmentId,
			CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Gets all child department groups asynchronous.
		/// </summary>
		/// <param name="parentDepartmentGroupId">The parent department group identifier.</param>
		/// <returns>Task&lt;List&lt;DepartmentGroup&gt;&gt;.</returns>
		Task<List<DepartmentGroup>> GetAllChildDepartmentGroupsAsync(int parentDepartmentGroupId);

		/// <summary>
		/// Gets all station groups for department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;List&lt;DepartmentGroup&gt;&gt;.</returns>
		Task<List<DepartmentGroup>> GetAllStationGroupsForDepartmentAsync(int departmentId);

		/// <summary>
		/// Gets the group for user asynchronous.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;DepartmentGroup&gt;.</returns>
		Task<DepartmentGroup> GetGroupForUserAsync(string userId, int departmentId);

		/// <summary>
		/// Gets the group member for user asynchronous.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;DepartmentGroupMember&gt;.</returns>
		Task<DepartmentGroupMember> GetGroupMemberForUserAsync(string userId, int departmentId);

		/// <summary>
		/// Saves the group member.
		/// </summary>
		/// <param name="depMember">The dep member.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>DepartmentGroupMember.</returns>
		Task<DepartmentGroupMember> SaveGroupMember(DepartmentGroupMember depMember,
			CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Gets all department groups for department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;Dictionary&lt;System.String, DepartmentGroup&gt;&gt;.</returns>
		Task<Dictionary<string, DepartmentGroup>> GetAllDepartmentGroupsForDepartmentAsync(int departmentId);

		/// <summary>
		/// Alls the grouped user ids for department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;List&lt;System.String&gt;&gt;.</returns>
		Task<List<string>> AllGroupedUserIdsForDepartmentAsync(int departmentId);

		/// <summary>
		/// Gets the map center coordinates for group asynchronous.
		/// </summary>
		/// <param name="departmentGroupId">The department group identifier.</param>
		/// <returns>Task&lt;Coordinates&gt;.</returns>
		Task<Coordinates> GetMapCenterCoordinatesForGroupAsync(int departmentGroupId);

		/// <summary>
		/// Moves the user into group asynchronous.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <param name="groupId">The group identifier.</param>
		/// <param name="isAdmin">if set to <c>true</c> [is admin].</param>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;DepartmentGroupMember&gt;.</returns>
		Task<DepartmentGroupMember> MoveUserIntoGroupAsync(string userId, int groupId, bool isAdmin, int departmentId,
			CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Gets all members for group asynchronous.
		/// </summary>
		/// <param name="groupId">The group identifier.</param>
		/// <returns>Task&lt;List&lt;DepartmentGroupMember&gt;&gt;.</returns>
		Task<List<DepartmentGroupMember>> GetAllMembersForGroupAsync(int groupId);

		/// <summary>
		/// Gets all members for group and child groups.
		/// </summary>
		/// <param name="group">The group.</param>
		/// <returns>List&lt;DepartmentGroupMember&gt;.</returns>
		List<DepartmentGroupMember> GetAllMembersForGroupAndChildGroups(DepartmentGroup group);

		/// <summary>
		/// Gets all admins for group asynchronous.
		/// </summary>
		/// <param name="groupId">The group identifier.</param>
		/// <returns>Task&lt;List&lt;DepartmentGroupMember&gt;&gt;.</returns>
		Task<List<DepartmentGroupMember>> GetAllAdminsForGroupAsync(int groupId);

		/// <summary>
		/// Gets the group by dispatch email code asynchronous.
		/// </summary>
		/// <param name="code">The code.</param>
		/// <returns>Task&lt;DepartmentGroup&gt;.</returns>
		Task<DepartmentGroup> GetGroupByDispatchEmailCodeAsync(string code);

		/// <summary>
		/// Gets the group by message email code asynchronous.
		/// </summary>
		/// <param name="code">The code.</param>
		/// <returns>Task&lt;DepartmentGroup&gt;.</returns>
		Task<DepartmentGroup> GetGroupByMessageEmailCodeAsync(string code);

		/// <summary>
		/// Gets all users for group.
		/// </summary>
		/// <param name="groupId">The group identifier.</param>
		/// <returns>List&lt;IdentityUser&gt;.</returns>
		List<IdentityUser> GetAllUsersForGroup(int groupId);

		Task<bool> DeleteGroupMembersByGroupIdAsync(int groupId, int departmentId, CancellationToken cancellationToken = default(CancellationToken));

		Task<List<DepartmentGroup>> GetAllGroupsForDepartmentUnlimitedThinAsync(int departmentId);

		Task<bool> DeleteAllMembersFromGroupAsync(DepartmentGroup departmentGroup, CancellationToken cancellationToken = default(CancellationToken));

		Task<List<DepartmentGroupMember>> GetAllGroupAdminsByDepartmentIdAsync(int departmentId);
	}
}
