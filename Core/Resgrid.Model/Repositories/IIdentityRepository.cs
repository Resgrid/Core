using Resgrid.Model.Identity;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	public interface IIdentityRepository
	{
		List<IdentityUser> GetAll();
		IdentityUser GetUserById(string userId);
		IdentityUser GetUserByUserName(string userName);
		IdentityUser GetUserByEmail(string email);
		IdentityUser Update(IdentityUser user);
		void UpdateUsername(string oldUsername, string newUsername);
		void AddUserToRole(string userId, string roleId);
		IdentityUserRole GetRoleForUserRole(string userId, string roleId);
		List<IdentityUser> GetAllMembershipsForDepartment(int departmentId);
		List<IdentityUser> GetAllUsersForDepartment(int departmentId);
		List<IdentityUser> GetAllUsersForDepartmentWithinLimits(int departmentId, bool retrieveHidden);
		List<IdentityUser> GetAllUsersCreatedAfterTimestamp(DateTime timestamp);
		void InitUserExtInfo(string userId);
		List<IdentityUser> GetAllUsersForGroup(int groupId);
		List<UserGroupRole> GetAllUsersGroupsAndRoles(int departmentId, bool retrieveHidden, bool retrieveDisabled, bool retrieveDeleted);
		void UpdateEmail(string userId, string newEmail);
		Task<IdentityUser> GetUserByUserNameAsync(string userName);
		Task<List<IdentityUser>> GetAllUsersForDepartmentWithinLimitsAsync(int departmentId, bool retrieveHidden);
	}
}
