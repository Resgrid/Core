using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Interface IUserStateService
	/// </summary>
	public interface IUserStateService
	{
		/// <summary>
		/// Gets the user state by identifier asynchronous.
		/// </summary>
		/// <param name="userStateId">The user state identifier.</param>
		/// <returns>Task&lt;UserState&gt;.</returns>
		Task<UserState> GetUserStateByIdAsync(int userStateId);

		/// <summary>
		/// Gets the last user state by user identifier asynchronous.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <returns>Task&lt;UserState&gt;.</returns>
		Task<UserState> GetLastUserStateByUserIdAsync(string userId);

		/// <summary>
		/// Gets the previous user state asynchronous.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <param name="userStateId">The user state identifier.</param>
		/// <returns>Task&lt;UserState&gt;.</returns>
		Task<UserState> GetPreviousUserStateAsync(string userId, int userStateId);

		/// <summary>
		/// Creates the state of the user.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="userStateType">Type of the user state.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;UserState&gt;.</returns>
		Task<UserState> CreateUserState(string userId, int departmentId, int userStateType,
			CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Creates the state of the user.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="userStateType">Type of the user state.</param>
		/// <param name="note">The note.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;UserState&gt;.</returns>
		Task<UserState> CreateUserState(string userId, int departmentId, int userStateType, string note,
			CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Creates the user state asynchronous.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="userStateType">Type of the user state.</param>
		/// <param name="note">The note.</param>
		/// <param name="timeStamp">The time stamp.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;UserState&gt;.</returns>
		Task<UserState> CreateUserStateAsync(string userId, int departmentId, int userStateType, string note,
			DateTime timeStamp, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Gets the states for department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;List&lt;UserState&gt;&gt;.</returns>
		Task<List<UserState>> GetStatesForDepartmentAsync(int departmentId);

		/// <summary>
		/// Gets all states for department in date range asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="startDate">The start date.</param>
		/// <param name="endDate">The end date.</param>
		/// <returns>Task&lt;List&lt;UserState&gt;&gt;.</returns>
		Task<List<UserState>> GetAllStatesForDepartmentInDateRangeAsync(int departmentId, DateTime startDate,
			DateTime endDate);

		/// <summary>
		/// Deletes the states for user asynchronous.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> DeleteStatesForUserAsync(string userId,
			CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Invalidates the latest states for department cache.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		void InvalidateLatestStatesForDepartmentCache(int departmentId);

		/// <summary>
		/// Gets the latest states for department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="bypassCache">if set to <c>true</c> [bypass cache].</param>
		/// <returns>Task&lt;List&lt;UserState&gt;&gt;.</returns>
		Task<List<UserState>> GetLatestStatesForDepartmentAsync(int departmentId, bool bypassCache = false);
	}
}
