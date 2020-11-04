using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface IResourceOrderFillRepository
	/// Implements the <see cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.ResourceOrderFill}" />
	/// </summary>
	/// <seealso cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.ResourceOrderFill}" />
	public interface IResourceOrderFillRepository: IRepository<ResourceOrderFill>
	{
		/// <summary>
		/// Updates the fill status asynchronous.
		/// </summary>
		/// <param name="fillId">The fill identifier.</param>
		/// <param name="userId">The user identifier.</param>
		/// <param name="accepted">if set to <c>true</c> [accepted].</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> UpdateFillStatusAsync(int fillId, string userId, bool accepted, CancellationToken cancellationToken);
	}
}
