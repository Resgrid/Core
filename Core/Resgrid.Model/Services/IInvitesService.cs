using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Interface IInvitesService
	/// </summary>
	public interface IInvitesService
	{
		/// <summary>
		/// Gets all invites for department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;List&lt;Invite&gt;&gt;.</returns>
		Task<List<Invite>> GetAllInvitesForDepartmentAsync(int departmentId);

		/// <summary>
		/// Creates the invites asynchronous.
		/// </summary>
		/// <param name="department">The department.</param>
		/// <param name="addingUserId">The adding user identifier.</param>
		/// <param name="emailAddresses">The email addresses.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> CreateInvitesAsync(Department department, string addingUserId, List<string> emailAddresses, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Gets the invite by code asynchronous.
		/// </summary>
		/// <param name="inviteCode">The invite code.</param>
		/// <returns>Task&lt;Invite&gt;.</returns>
		Task<Invite> GetInviteByCodeAsync(Guid inviteCode);

		/// <summary>
		/// Completes the invite asynchronous.
		/// </summary>
		/// <param name="inviteCode">The invite code.</param>
		/// <param name="userId">The user identifier.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;Invite&gt;.</returns>
		Task<Invite> CompleteInviteAsync(Guid inviteCode, string userId, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Resends the invite asynchronous.
		/// </summary>
		/// <param name="inviteId">The invite identifier.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;Invite&gt;.</returns>
		Task<Invite> ResendInviteAsync(int inviteId, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Deletes the invite asynchronous.
		/// </summary>
		/// <param name="inviteId">The invite identifier.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> DeleteInviteAsync(int inviteId, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Gets the invite by identifier asynchronous.
		/// </summary>
		/// <param name="inviteId">The invite identifier.</param>
		/// <returns>Task&lt;Invite&gt;.</returns>
		Task<Invite> GetInviteByIdAsync(int inviteId);

		/// <summary>
		/// Gets the invite by email asynchronous.
		/// </summary>
		/// <param name="emailAddress">The email address.</param>
		/// <returns>Task&lt;Invite&gt;.</returns>
		Task<Invite> GetInviteByEmailAsync(string emailAddress);
	}
}
