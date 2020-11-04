using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	public interface IUserProfileService
	{
		/// <summary>
		/// Gets the profile by user identifier asynchronous.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <param name="bypassCache">if set to <c>true</c> [bypass cache].</param>
		/// <returns>Task&lt;UserProfile&gt;.</returns>
		Task<UserProfile> GetProfileByUserIdAsync(string userId, bool bypassCache = false);

		/// <summary>
		/// Gets all profiles for department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="bypassCache">if set to <c>true</c> [bypass cache].</param>
		/// <returns>Task&lt;Dictionary&lt;System.String, UserProfile&gt;&gt;.</returns>
		Task<Dictionary<string, UserProfile>> GetAllProfilesForDepartmentAsync(int departmentId, bool bypassCache = false);

		/// <summary>
		/// Gets all profiles for department inc disabled deleted asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;Dictionary&lt;System.String, UserProfile&gt;&gt;.</returns>
		Task<Dictionary<string, UserProfile>> GetAllProfilesForDepartmentIncDisabledDeletedAsync(int departmentId);

		/// <summary>
		/// Saves the profile asynchronous.
		/// </summary>
		/// <param name="DepartmentId">The department identifier.</param>
		/// <param name="profile">The profile.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;UserProfile&gt;.</returns>
		Task<UserProfile> SaveProfileAsync(int DepartmentId, UserProfile profile, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Clears the user profile from cache.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		void ClearUserProfileFromCache(string userId);

		/// <summary>
		/// Clears all user profiles from cache.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		void ClearAllUserProfilesFromCache(int departmentId);

		/// <summary>
		/// Disables the text messages for user asynchronous.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;UserProfile&gt;.</returns>
		Task<UserProfile> DisableTextMessagesForUserAsync(string userId, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Gets the selected user profiles asynchronous.
		/// </summary>
		/// <param name="userIds">The user ids.</param>
		/// <returns>Task&lt;List&lt;UserProfile&gt;&gt;.</returns>
		Task<List<UserProfile>> GetSelectedUserProfilesAsync(List<string> userIds);

		/// <summary>
		/// Gets the profile by mobile number asynchronous.
		/// </summary>
		/// <param name="number">The number.</param>
		/// <returns>Task&lt;UserProfile&gt;.</returns>
		Task<UserProfile> GetProfileByMobileNumberAsync(string number);

		/// <summary>
		/// Gets the profile by home number asynchronous.
		/// </summary>
		/// <param name="number">The number.</param>
		/// <returns>Task&lt;UserProfile&gt;.</returns>
		Task<UserProfile> GetProfileByHomeNumberAsync(string number);
	}
}
