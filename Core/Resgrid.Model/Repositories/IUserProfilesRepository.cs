using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	public interface IUserProfilesRepository : IRepository<UserProfile>
	{
		List<UserProfile> GetAllUserProfilesForDepartment(int departmentId);
		List<UserProfile> GetSelectedUserProfiles(List<string> userIds);
		UserProfile GetProfileByUserId(string userId);
		Task<UserProfile> GetProfileByUserIdAsync(string userId);
		Task<List<UserProfile>> GetSelectedUserProfilesAsync(List<string> userIds);
		List<UserProfile> GetAllUserProfilesForDepartmentIncDisabledDeleted(int departmentId);
	}
}
