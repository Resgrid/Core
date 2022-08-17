using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	public interface IInventoryService
	{
		/// <summary>
		/// Gets the type by identifier asynchronous.
		/// </summary>
		/// <param name="typeId">The type identifier.</param>
		/// <returns>Task&lt;InventoryType&gt;.</returns>
		Task<InventoryType> GetTypeByIdAsync(int typeId);

		/// <summary>
		/// Saves the type asynchronous.
		/// </summary>
		/// <param name="type">The type.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;InventoryType&gt;.</returns>
		Task<InventoryType> SaveTypeAsync(InventoryType type, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Gets the inventory by identifier asynchronous.
		/// </summary>
		/// <param name="inventoryId">The inventory identifier.</param>
		/// <returns>Task&lt;Inventory&gt;.</returns>
		Task<Inventory> GetInventoryByIdAsync(int inventoryId);

		/// <summary>
		/// Saves the inventory asynchronous.
		/// </summary>
		/// <param name="inventory">The inventory.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;Inventory&gt;.</returns>
		Task<Inventory> SaveInventoryAsync(Inventory inventory, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Deletes the type asynchronous.
		/// </summary>
		/// <param name="typeId">The type identifier.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> DeleteTypeAsync(int typeId, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Gets all transactions for department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;List&lt;Inventory&gt;&gt;.</returns>
		Task<List<Inventory>> GetAllTransactionsForDepartmentAsync(int departmentId);

		/// <summary>
		/// Gets all types for department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;List&lt;InventoryType&gt;&gt;.</returns>
		Task<List<InventoryType>> GetAllTypesForDepartmentAsync(int departmentId);

		/// <summary>
		/// Gets the consolidated inventory for department.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;List&lt;Inventory&gt;&gt;.</returns>
		Task<List<Inventory>> GetConsolidatedInventoryForDepartment(int departmentId);

		Task<bool> DeleteInventoriesByGroupIdAsync(int groupId, int departmentId, CancellationToken cancellationToken = default(CancellationToken));
	}
}
