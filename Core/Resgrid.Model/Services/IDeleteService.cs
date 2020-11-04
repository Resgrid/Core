using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Interface IDeleteService
	/// </summary>
	public interface IDeleteService
	{
		/// <summary>
		/// Deletes the user asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="authorizingUserId">The authorizing user identifier.</param>
		/// <param name="userIdToDelete">The user identifier to delete.</param>
		/// <returns>Task&lt;DeleteUserResults&gt;.</returns>
		Task<DeleteUserResults> DeleteUserAsync(int departmentId, string authorizingUserId, string userIdToDelete);

		/// <summary>
		/// Deletes the group asynchronous.
		/// </summary>
		/// <param name="departmentGroupId">The department group identifier.</param>
		/// <param name="currentUserId">The current user identifier.</param>
		/// <returns>Task&lt;DeleteGroupResults&gt;.</returns>
		Task<DeleteGroupResults> DeleteGroupAsync(int departmentGroupId, string currentUserId);
	}
}
