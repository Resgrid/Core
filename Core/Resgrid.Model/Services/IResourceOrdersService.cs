using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	public interface IResourceOrdersService
	{
		/// <summary>
		/// Gets all asynchronous.
		/// </summary>
		/// <returns>Task&lt;List&lt;ResourceOrder&gt;&gt;.</returns>
		Task<List<ResourceOrder>> GetAllAsync();
		/// <summary>
		/// Gets all open asynchronous.
		/// </summary>
		/// <returns>Task&lt;List&lt;ResourceOrder&gt;&gt;.</returns>
		Task<List<ResourceOrder>> GetAllOpenAsync();
		/// <summary>
		/// Gets the open orders by department identifier asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;List&lt;ResourceOrder&gt;&gt;.</returns>
		Task<List<ResourceOrder>> GetOpenOrdersByDepartmentIdAsync(int departmentId);
		/// <summary>
		/// Gets all orders by department identifier asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;List&lt;ResourceOrder&gt;&gt;.</returns>
		Task<List<ResourceOrder>> GetAllOrdersByDepartmentIdAsync(int departmentId);
		/// <summary>
		/// Gets the settings by department identifier asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;ResourceOrderSetting&gt;.</returns>
		Task<ResourceOrderSetting> GetSettingsByDepartmentIdAsync(int departmentId);
		/// <summary>
		/// Saves the settings asynchronous.
		/// </summary>
		/// <param name="settings">The settings.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;ResourceOrderSetting&gt;.</returns>
		Task<ResourceOrderSetting> SaveSettingsAsync(ResourceOrderSetting settings, CancellationToken cancellationToken = default(CancellationToken));
		/// <summary>
		/// Creates the order asynchronous.
		/// </summary>
		/// <param name="order">The order.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;ResourceOrder&gt;.</returns>
		Task<ResourceOrder> CreateOrderAsync(ResourceOrder order, CancellationToken cancellationToken = default(CancellationToken));
		/// <summary>
		/// Gets the order by identifier asynchronous.
		/// </summary>
		/// <param name="orderId">The order identifier.</param>
		/// <returns>Task&lt;ResourceOrder&gt;.</returns>
		Task<ResourceOrder> GetOrderByIdAsync(int orderId);
		/// <summary>
		/// Gets the order item by identifier asynchronous.
		/// </summary>
		/// <param name="orderItemId">The order item identifier.</param>
		/// <returns>Task&lt;ResourceOrderItem&gt;.</returns>
		Task<ResourceOrderItem> GetOrderItemByIdAsync(int orderItemId);
		/// <summary>
		/// Gets the order fill by identifier asynchronous.
		/// </summary>
		/// <param name="orderFillId">The order fill identifier.</param>
		/// <returns>Task&lt;ResourceOrderFill&gt;.</returns>
		Task<ResourceOrderFill> GetOrderFillByIdAsync(int orderFillId);
		/// <summary>
		/// Creates the fill asynchronous.
		/// </summary>
		/// <param name="fill">The fill.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;ResourceOrderFill&gt;.</returns>
		Task<ResourceOrderFill> CreateFillAsync(ResourceOrderFill fill, CancellationToken cancellationToken = default(CancellationToken));
		/// <summary>
		/// Sets the fill status asynchronous.
		/// </summary>
		/// <param name="fillId">The fill identifier.</param>
		/// <param name="userId">The user identifier.</param>
		/// <param name="accepted">if set to <c>true</c> [accepted].</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> SetFillStatusAsync(int fillId, string userId, bool accepted, CancellationToken cancellationToken = default(CancellationToken));
		/// <summary>
		/// Gets the open available orders asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;List&lt;ResourceOrder&gt;&gt;.</returns>
		Task<List<ResourceOrder>> GetOpenAvailableOrdersAsync(int departmentId);
	}
}
