using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Interface IAffiliateService
	/// </summary>
	public interface IAffiliateService
	{
		/// <summary>
		/// Gets the affiliate by identifier asynchronous.
		/// </summary>
		/// <param name="affiliateId">The affiliate identifier.</param>
		/// <returns>Task&lt;Affiliate&gt;.</returns>
		Task<Affiliate> GetAffiliateByIdAsync(int affiliateId);

		/// <summary>
		/// Gets the affiliate by code asynchronous.
		/// </summary>
		/// <param name="affiliateCode">The affiliate code.</param>
		/// <returns>Task&lt;Affiliate&gt;.</returns>
		Task<Affiliate> GetAffiliateByCodeAsync(string affiliateCode);

		/// <summary>
		/// Saves the affiliate asynchronous.
		/// </summary>
		/// <param name="affiliate">The affiliate.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;Affiliate&gt;.</returns>
		Task<Affiliate> SaveAffiliateAsync(Affiliate affiliate, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Gets all affiliate codes asynchronous.
		/// </summary>
		/// <returns>Task&lt;HashSet&lt;System.String&gt;&gt;.</returns>
		Task<HashSet<string>> GetAllAffiliateCodesAsync();

		/// <summary>
		/// Creates the new affiliate asynchronous.
		/// </summary>
		/// <param name="affiliate">The affiliate.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;Affiliate&gt;.</returns>
		Task<Affiliate> CreateNewAffiliateAsync(Affiliate affiliate, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Gets all asynchronous.
		/// </summary>
		/// <returns>Task&lt;List&lt;Affiliate&gt;&gt;.</returns>
		Task<List<Affiliate>> GetAllAsync();

		/// <summary>
		/// Gets the active affiliate by code asynchronous.
		/// </summary>
		/// <param name="affiliateCode">The affiliate code.</param>
		/// <returns>Task&lt;Affiliate&gt;.</returns>
		Task<Affiliate> GetActiveAffiliateByCodeAsync(string affiliateCode);
	}
}
