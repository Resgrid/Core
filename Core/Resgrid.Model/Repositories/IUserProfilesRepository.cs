using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface IUserProfilesRepository
	/// Implements the <see cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.UserProfile}" />
	/// </summary>
	/// <seealso cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.UserProfile}" />
	public interface IUserProfilesRepository: IRepository<UserProfile>
	{
		/// <summary>
		/// Gets the profile by user identifier asynchronous.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <returns>Task&lt;UserProfile&gt;.</returns>
		Task<UserProfile> GetProfileByUserIdAsync(string userId);

		/// <summary>
		/// Gets the profile by mobile number asynchronous.
		/// </summary>
		/// <param name="mobileNumber">The mobile number.</param>
		/// <returns>Task&lt;UserProfile&gt;.</returns>
		Task<UserProfile> GetProfileByMobileNumberAsync(string mobileNumber);

		/// <summary>
		/// Gets the profile by home number asynchronous.
		/// </summary>
		/// <param name="homeNumber">The home number.</param>
		/// <returns>Task&lt;UserProfile&gt;.</returns>
		Task<UserProfile> GetProfileByHomeNumberAsync(string homeNumber);

		/// <summary>
		/// Gets the selected user profiles asynchronous.
		/// </summary>
		/// <param name="userIds">The user ids.</param>
		/// <returns>Task&lt;IEnumerable&lt;UserProfile&gt;&gt;.</returns>
		Task<IEnumerable<UserProfile>> GetSelectedUserProfilesAsync(List<string> userIds);

		/// <summary>
		/// Gets all user profiles for department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;UserProfile&gt;&gt;.</returns>
		Task<IEnumerable<UserProfile>> GetAllUserProfilesForDepartmentAsync(int departmentId);
		
		/// <summary>
		/// Gets all user profiles for department inc disabled deleted asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;UserProfile&gt;&gt;.</returns>
		Task<IEnumerable<UserProfile>> GetAllUserProfilesForDepartmentIncDisabledDeletedAsync(int departmentId);
	}
}
