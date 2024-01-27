using System.Collections.Generic;
using System.Threading.Tasks;
using Resgrid.Model.Identity;

namespace Resgrid.Model.Services
{
	public interface IUsersService
	{
		string UserRoleId { get; }
		string AdminRoleId { get; }
		string AffiliateRoleId { get; }
		List<IdentityUser> GetAll();
		void AddUserToUserRole(string userId);
		bool IsUserInRole(string userId, string roleId);
		IdentityUser GetUserById(string userId, bool bypassCache = true);
		Dictionary<string, int> GetNewUsersCountForLast5Days();
		int GetUsersCount();
		Task<IdentityUser> UpdateUsername(string oldUsername, string newUsername);
		IdentityUser GetUserByEmail(string emailAddress);
		IdentityUser GetMembershipByUserId(string userId);
		void AddUserToAffiliteRole(string userId);
		List<IdentityUser> GetAllMembershipsForDepartment(int departmentId);
		IdentityUser SaveUser(IdentityUser user);
		void InitUserExtInfo(string userId);
		Task<List<UserGroupRole>> GetUserGroupAndRolesByDepartmentIdAsync(int deparmentId, bool retrieveHidden, bool retrieveDisabled, bool retrieveDeleted);
		IdentityUser UpdateEmail(string userId, string newEmail);
		Task<bool> DoesUserHaveAnyActiveDepartments(string userName);
		void ClearCacheForDepartment(int departmentId);
		Task<IdentityUser> GetUserByNameAsync(string userName);
		Task<PersonnelLocation> SavePersonnelLocationAsync(PersonnelLocation personnelLocation);
		Task<List<PersonnelLocation>> GetLatestLocationsForDepartmentPersonnelAsync(int departmentId);
		Task<PersonnelLocation> GetPersonnelLocationByIdAsync(string id);
		Task<bool> ClearOutUserLoginAsync(string userId);
		Task<List<UserGroupRole>> GetUserGroupAndRolesByDepartmentIdInLimitAsync(int deparmentId, bool retrieveHidden, bool retrieveDisabled, bool retrieveDeleted);
	}
}
