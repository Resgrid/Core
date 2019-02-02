using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	public interface IUserProfileService
	{
		UserProfile GetProfileByUserId(string userId, bool bypassCache = false);
		UserProfile SaveProfile(int DepartmentId, UserProfile profile);
		void DeletProfileForUser(string userId);
		UserProfile FindProfileByMobileNumber(string number);
		//List<UserProfile> GetAllUserProfilesForDepartment(int departmentId);
		Dictionary<string, UserProfile> GetAllProfilesForDepartment(int departmentId, bool bypassCache = false);
		List<UserProfile> GetSelectedUserProfiles(List<string> userIds);
		void ClearUserProfileFromCache(string userId);
		void ClearAllUserProfilesFromCache(int departmentId);
		void DisableTextMessagesForUser(string userId);
		UserProfile FindProfileByHomeNumber(string number);
		UserProfile GetUserProfileForEditing(string userId);
		Task<UserProfile> GetProfileByUserIdAsync(string userId, bool bypassCache = false);
		Task<List<UserProfile>> GetSelectedUserProfilesAsync(List<string> userIds);
	}
}
