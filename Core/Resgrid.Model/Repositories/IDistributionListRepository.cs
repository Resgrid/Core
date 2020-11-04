using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface IDistributionListRepository
	/// Implements the <see cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.DistributionList}" />
	/// </summary>
	/// <seealso cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.DistributionList}" />
	public interface IDistributionListRepository: IRepository<DistributionList>
	{
		/// <summary>
		/// Gets the distribution list by email address.
		/// </summary>
		/// <param name="email">The email.</param>
		/// <returns>Task&lt;IEnumerable&lt;DistributionList&gt;&gt;.</returns>
		Task<DistributionList> GetDistributionListByEmailAddressAsync(string email);

		/// <summary>
		/// Gets all active distribution lists asynchronous.
		/// </summary>
		/// <returns>Task&lt;IEnumerable&lt;DistributionList&gt;&gt;.</returns>
		Task<IEnumerable<DistributionList>> GetAllActiveDistributionListsAsync();

		/// <summary>
		/// Gets the dispatch protocols by department identifier asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;DistributionList&gt;&gt;.</returns>
		Task<IEnumerable<DistributionList>> GetDispatchProtocolsByDepartmentIdAsync(int departmentId);

		/// <summary>
		/// Gets the distribution list by identifier asynchronous.
		/// </summary>
		/// <param name="distributionListId">The distribution list identifier.</param>
		/// <returns>Task&lt;DistributionList&gt;.</returns>
		Task<DistributionList> GetDistributionListByIdAsync(int distributionListId);
	}
}
