using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	public interface IDistributionListsService
	{
		/// <summary>
		/// Gets all asynchronous.
		/// </summary>
		/// <returns>Task&lt;List&lt;DistributionList&gt;&gt;.</returns>
		Task<List<DistributionList>> GetAllAsync();
		/// <summary>
		/// Gets the distribution list by identifier asynchronous.
		/// </summary>
		/// <param name="distributionListId">The distribution list identifier.</param>
		/// <returns>Task&lt;DistributionList&gt;.</returns>
		Task<DistributionList> GetDistributionListByIdAsync(int distributionListId);
		/// <summary>
		/// Gets the distribution lists by department identifier asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;List&lt;DistributionList&gt;&gt;.</returns>
		Task<List<DistributionList>> GetDistributionListsByDepartmentIdAsync(int departmentId);
		/// <summary>
		/// Gets the distribution list by address asynchronous.
		/// </summary>
		/// <param name="emailAddress">The email address.</param>
		/// <returns>Task&lt;DistributionList&gt;.</returns>
		Task<DistributionList> GetDistributionListByAddressAsync(string emailAddress);

		/// <summary>
		/// Deletes the distribution lists by identifier asynchronous.
		/// </summary>
		/// <param name="distributionListId">The distribution list identifier.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> DeleteDistributionListsByIdAsync(int distributionListId, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Saves the distribution list asynchronous.
		/// </summary>
		/// <param name="distributionList">The distribution list.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;DistributionList&gt;.</returns>
		Task<DistributionList> SaveDistributionListAsync(DistributionList distributionList, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Saves the distribution list only asynchronous.
		/// </summary>
		/// <param name="distributionList">The distribution list.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;DistributionList&gt;.</returns>
		Task<DistributionList> SaveDistributionListOnlyAsync(DistributionList distributionList, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Gets all active distribution lists asynchronous.
		/// </summary>
		/// <returns>Task&lt;List&lt;DistributionList&gt;&gt;.</returns>
		Task<List<DistributionList>> GetAllActiveDistributionListsAsync();

		/// <summary>
		/// Gets all list members by list identifier asynchronous.
		/// </summary>
		/// <param name="listId">The list identifier.</param>
		/// <returns>Task&lt;List&lt;DistributionListMember&gt;&gt;.</returns>
		Task<List<DistributionListMember>> GetAllListMembersByListIdAsync(int listId);
		/// <summary>
		/// Removes the user from all lists asynchronous.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> RemoveUserFromAllListsAsync(string userId, CancellationToken cancellationToken = default(CancellationToken));
	}
}
