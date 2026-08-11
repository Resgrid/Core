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
		/// Removes a user from a department (admin initiated). If the user belongs to other
		/// departments only this department's access, roles, groups, lists and automations are
		/// revoked and the account stays usable; if this is their only department the whole
		/// account is deactivated using the same flow as the self-service account delete.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="authorizingUserId">The authorizing user identifier.</param>
		/// <param name="userIdToDelete">The user identifier to delete.</param>
		/// <returns>Task&lt;DeleteUserResults&gt;.</returns>
		Task<DeleteUserResults> DeleteUserAsync(int departmentId, string authorizingUserId, string userIdToDelete, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Revokes a user's access to a single department without touching their account, login
		/// or PII: removes roles, group memberships, distribution list subscriptions and scheduled
		/// automations for that department, then soft-deletes the membership. No authorization
		/// check is performed; callers are responsible for authorizing the operation.
		/// </summary>
		Task<bool> RevokeDepartmentAccessAsync(string userId, int departmentId, string revokingUserId, CancellationToken cancellationToken = default(CancellationToken));

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
