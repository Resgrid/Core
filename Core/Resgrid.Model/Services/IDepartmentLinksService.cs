using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	public interface IDepartmentLinksService
	{
		/// <summary>
		/// Gets all links for department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;List&lt;DepartmentLink&gt;&gt;.</returns>
		Task<List<DepartmentLink>> GetAllLinksForDepartmentAsync(int departmentId);

		/// <summary>
		/// Gets the link by identifier asynchronous.
		/// </summary>
		/// <param name="linkId">The link identifier.</param>
		/// <returns>Task&lt;DepartmentLink&gt;.</returns>
		Task<DepartmentLink> GetLinkByIdAsync(int linkId);

		/// <summary>
		/// Saves a link. When the save gives either party less than the stored row did (disabled, parties changed or a
		/// share flag turned off), both stored parties' link-share access epochs are rotated after the write, so realtime
		/// link subscriptions made before the change stop receiving events.
		/// </summary>
		/// <param name="link">The link.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;DepartmentLink&gt;.</returns>
		Task<DepartmentLink> SaveAsync(DepartmentLink link, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// The department's current link-share access epoch, part of every SignalR share-group name for its data
		/// (<see cref="Helpers.DepartmentLinkSharing.GetShareGroupName"/>). One is minted on first use. Null when the cache
		/// is unavailable: callers must then neither join nor publish to a share group.
		/// </summary>
		/// <param name="departmentId">The department whose data the share groups carry.</param>
		Task<string> GetShareAccessVersionAsync(int departmentId);

		/// <summary>
		/// The SignalR share groups a connection of <paramref name="viewerDepartmentId"/> joins to subscribe through link
		/// <paramref name="linkId"/>: one per evented kind the other side shares toward the viewer over an enabled link, in
		/// the other side's current access epoch. The epoch is read before the link is re-read, so a revocation that lands
		/// in between is either seen (refused) or leaves the caller in a group nobody sends to. Empty when the viewer may not
		/// subscribe; null when the cache is unavailable.
		/// </summary>
		Task<List<string>> GetShareGroupsForSubscriberAsync(int linkId, int viewerDepartmentId);

		/// <summary>
		/// The share group a publisher sends <paramref name="departmentId"/>'s <paramref name="shareType"/> events to, in the
		/// department's current access epoch. Never mints an epoch: null when there is none (so nobody can be subscribed)
		/// or the cache is unavailable, and the publisher then sends nothing to link subscribers.
		/// </summary>
		Task<string> GetShareGroupNameAsync(int departmentId, DepartmentLinkShareTypes shareType);

		/// <summary>
		/// Rotates the department's link-share access epoch. Every connection subscribed to its share groups is left in a
		/// group publishers no longer send to, and has to subscribe again (which re-checks the link).
		/// </summary>
		/// <param name="departmentId">The department whose share groups are revoked.</param>
		/// <returns>False when the new epoch could not be stored.</returns>
		Task<bool> RevokeShareAccessAsync(int departmentId);

		/// <summary>
		/// Deletes a department link and revokes both parties' realtime link subscriptions. Callers check that the
		/// requesting department is a party to the link (its creator or the linked department) before calling this.
		/// </summary>
		/// <param name="link">The link to delete.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;System.Boolean&gt;, true when the row was deleted.</returns>
		Task<bool> DeleteLinkAsync(DepartmentLink link, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Doeses the link exist asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="departmentLinkId">The department link identifier.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> DoesLinkExistAsync(int departmentId, int departmentLinkId);

		/// <summary>
		/// Gets the department by link code asynchronous.
		/// </summary>
		/// <param name="code">The code.</param>
		/// <returns>Task&lt;Department&gt;.</returns>
		Task<Department> GetDepartmentByLinkCodeAsync(string code);
	}
}
