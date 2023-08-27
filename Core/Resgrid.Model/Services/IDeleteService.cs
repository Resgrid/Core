using System.Threading;
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
		Task<DeleteGroupResults> DeleteGroupAsync(int departmentGroupId, int departmentId, string currentUserId, CancellationToken cancellationToken = default(CancellationToken));

		Task<DeleteUserResults> DeleteUserAccountAsync(int departmentId, string authorizingUserId, string userIdToDelete, string ipAddress, string userAgent, CancellationToken cancellationToken = default(CancellationToken));

		Task<DeleteDepartmentResults> DeleteDepartment(int departmentId, string authorizingUserId, string ipAddress, string userAgent,
			CancellationToken cancellationToken = default(CancellationToken));

		Task<DeleteDepartmentResults> HandlePendingDepartmentDeletionRequestAsync(QueueItem item, CancellationToken cancellationToken = default(CancellationToken));
	}
}
