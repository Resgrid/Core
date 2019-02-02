using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	public interface IDepartmentMembersRepository : IRepository<DepartmentMember>
	{
		List<DepartmentMember> GetAllDepartmentMembersWithinLimits(int deparmentId);
		List<UserProfileMaintenance> GetAllMissingUserProfiles();
		List<UserProfileMaintenance> GetAllUserProfilesWithEmptyNames();
		List<DepartmentMember> GetAllDepartmentMembersUnlimited(int departmentId);
	}
}