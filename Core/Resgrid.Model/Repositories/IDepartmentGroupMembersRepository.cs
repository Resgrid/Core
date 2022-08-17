using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface IDepartmentGroupMembersRepository
	/// Implements the <see cref="DepartmentGroupMember" />
	/// </summary>
	/// <seealso cref="DepartmentGroupMember" />
	public interface IDepartmentGroupMembersRepository: IRepository<DepartmentGroupMember>
	{
		/// <summary>
		/// Gets all group members by group identifier asynchronous.
		/// </summary>
		/// <param name="groupId">The group identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;DepartmentGroupMember&gt;&gt;.</returns>
		Task<IEnumerable<DepartmentGroupMember>> GetAllGroupMembersByGroupIdAsync(int groupId);

		/// <summary>
		/// Gets all group members by user and department asynchronous.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;DepartmentGroupMember&gt;&gt;.</returns>
		Task<IEnumerable<DepartmentGroupMember>> GetAllGroupMembersByUserAndDepartmentAsync(string userId,
			int departmentId);

		Task<bool> DeleteGroupMembersByGroupIdAsync(int groupId, int departmentId, CancellationToken cancellationToken = default(CancellationToken));
	}
}
