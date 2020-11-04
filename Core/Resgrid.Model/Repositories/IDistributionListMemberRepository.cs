using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface IDistributionListMemberRepository
	/// Implements the <see cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.DistributionListMember}" />
	/// </summary>
	/// <seealso cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.DistributionListMember}" />
	public interface IDistributionListMemberRepository: IRepository<DistributionListMember>
	{
		/// <summary>
		/// Gets the distribution list member by list identifier asynchronous.
		/// </summary>
		/// <param name="listId">The list identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;DistributionListMember&gt;&gt;.</returns>
		Task<IEnumerable<DistributionListMember>> GetDistributionListMemberByListIdAsync(int listId);

		/// <summary>
		/// Gets the distribution list member by user identifier asynchronous.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;DistributionListMember&gt;&gt;.</returns>
		Task<IEnumerable<DistributionListMember>> GetDistributionListMemberByUserIdAsync(string userId);
	}
}
