using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface IResourceOrdersRepository
	/// Implements the <see cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.ResourceOrder}" />
	/// </summary>
	/// <seealso cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.ResourceOrder}" />
	public interface IResourceOrdersRepository: IRepository<ResourceOrder>
	{
		/// <summary>
		/// Gets all open orders asynchronous.
		/// </summary>
		/// <returns>Task&lt;IEnumerable&lt;ResourceOrder&gt;&gt;.</returns>
		Task<IEnumerable<ResourceOrder>> GetAllOpenOrdersAsync();

		/// <summary>
		/// Saves the order.
		/// </summary>
		/// <param name="order">The order.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;ResourceOrder&gt;.</returns>
		Task<ResourceOrder> SaveOrderAsync(ResourceOrder order, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Saves the fill asynchronous.
		/// </summary>
		/// <param name="fill">The fill.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;ResourceOrderFill&gt;.</returns>
		Task<ResourceOrderFill> SaveFillAsync(ResourceOrderFill fill, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Gets all non department open visible orders asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;ResourceOrder&gt;&gt;.</returns>
		Task<IEnumerable<ResourceOrder>> GetAllNonDepartmentOpenVisibleOrdersAsync(int departmentId);

		Task<IEnumerable<ResourceOrderItem>> GetAllItemsByResourceOrderIdAsync(int resourceOrderId);
	}
}
