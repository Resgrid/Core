using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface IUserStatesRepository
	/// Implements the <see cref="UserState" />
	/// </summary>
	/// <seealso cref="UserState" />
	public interface IUserStatesRepository: IRepository<UserState>
	{
		/// <summary>
		/// Gets the latest user states by department identifier asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;UserState&gt;&gt;.</returns>
		Task<IEnumerable<UserState>> GetLatestUserStatesByDepartmentIdAsync(int departmentId);

		/// <summary>
		/// Gets the user states by user identifier asynchronous.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;UserState&gt;&gt;.</returns>
		Task<IEnumerable<UserState>> GetUserStatesByUserIdAsync(string userId);

		/// <summary>
		/// Gets the last user state by user identifier asynchronous.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <returns>Task&lt;UserState&gt;.</returns>
		Task<UserState> GetLastUserStateByUserIdAsync(string userId);

		/// <summary>
		/// Gets the previous user state by user identifier asynchronous.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <param name="userStateId">The user state identifier.</param>
		/// <returns>Task&lt;UserState&gt;.</returns>
		Task<UserState> GetPreviousUserStateByUserIdAsync(string userId, int userStateId);

		/// <summary>
		/// Gets all user states by department identifier in range asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="startDate">The start date.</param>
		/// <param name="endDate">The end date.</param>
		/// <returns>Task&lt;IEnumerable&lt;UserState&gt;&gt;.</returns>
		Task<IEnumerable<UserState>> GetAllUserStatesByDepartmentIdInRangeAsync(int departmentId, DateTime startDate,
			DateTime endDate);
	}
}
