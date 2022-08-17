using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface IDepartmentMembersRepository
	/// Implements the <see cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.DepartmentMember}" />
	/// </summary>
	/// <seealso cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.DepartmentMember}" />
	public interface IDepartmentMembersRepository: IRepository<DepartmentMember>
	{
		/// <summary>
		/// Gets all department members within limits asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;DepartmentMember&gt;&gt;.</returns>
		Task<IEnumerable<DepartmentMember>> GetAllDepartmentMembersWithinLimitsAsync(int departmentId);
		/// <summary>
		/// Gets all department members unlimited asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;DepartmentMember&gt;&gt;.</returns>
		Task<IEnumerable<DepartmentMember>> GetAllDepartmentMembersUnlimitedAsync(int departmentId);
		/// <summary>
		/// Gets the department members u by department identifier and user identifier asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="userId">The user identifier.</param>
		/// <returns>Task&lt;DepartmentMember&gt;.</returns>
		Task<DepartmentMember> GetDepartmentMemberByDepartmentIdAndUserIdAsync(int departmentId, string userId);

		/// <summary>
		/// Gets all department member by user identifier asynchronous.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;DepartmentMember&gt;&gt;.</returns>
		Task<IEnumerable<DepartmentMember>> GetAllDepartmentMemberByUserIdAsync(string userId);
		//List<UserProfileMaintenance> GetAllMissingUserProfiles();
		//List<UserProfileMaintenance> GetAllUserProfilesWithEmptyNames();

		Task<IEnumerable<DepartmentMember>> GetAllDepartmentMembersUnlimitedIncDelAsync(int departmentId);
	}
}
