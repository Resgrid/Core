using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface IDepartmentGroupsRepository
	/// Implements the <see cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.DepartmentGroup}" />
	/// </summary>
	/// <seealso cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.DepartmentGroup}" />
	public interface IDepartmentGroupsRepository: IRepository<DepartmentGroup>
	{
		/// <summary>
		/// Gets all station groups for department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;List&lt;DepartmentGroup&gt;&gt;.</returns>
		Task<List<DepartmentGroup>> GetAllStationGroupsForDepartmentAsync(int departmentId);

		/// <summary>
		/// Gets all groups by department identifier asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;DepartmentGroup&gt;&gt;.</returns>
		Task<IEnumerable<DepartmentGroup>> GetAllGroupsByDepartmentIdAsync(int departmentId);

		/// <summary>
		/// Gets all groups by parent group identifier asynchronous.
		/// </summary>
		/// <param name="parentGroupId">The parent group identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;DepartmentGroup&gt;&gt;.</returns>
		Task<IEnumerable<DepartmentGroup>> GetAllGroupsByParentGroupIdAsync(int parentGroupId);

		/// <summary>
		/// Gets the group by dispatch code asynchronous.
		/// </summary>
		/// <param name="dispatchCode">The dispatch code.</param>
		/// <returns>Task&lt;DepartmentGroup&gt;.</returns>
		Task<DepartmentGroup> GetGroupByDispatchCodeAsync(string dispatchCode);

		/// <summary>
		/// Gets the group by message code asynchronous.
		/// </summary>
		/// <param name="messageCode">The message code.</param>
		/// <returns>Task&lt;DepartmentGroup&gt;.</returns>
		Task<DepartmentGroup> GetGroupByMessageCodeAsync(string messageCode);

		/// <summary>
		/// Gets the group by group identifier asynchronous.
		/// </summary>
		/// <param name="departmentGroupId">The department group identifier.</param>
		/// <returns>Task&lt;DepartmentGroup&gt;.</returns>
		Task<DepartmentGroup> GetGroupByGroupIdAsync(int departmentGroupId);
	}
}
