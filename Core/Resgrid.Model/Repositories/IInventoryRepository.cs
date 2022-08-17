using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface IInventoryRepository
	/// Implements the <see cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.Inventory}" />
	/// </summary>
	/// <seealso cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.Inventory}" />
	public interface IInventoryRepository: IRepository<Inventory>
	{
		/// <summary>
		/// Gets the inventory by type identifier asynchronous.
		/// </summary>
		/// <param name="typeId">The type identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;Inventory&gt;&gt;.</returns>
		Task<IEnumerable<Inventory>> GetInventoryByTypeIdAsync(int typeId);

		/// <summary>
		/// Gets all inventories by department identifier asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;Inventory&gt;&gt;.</returns>
		Task<IEnumerable<Inventory>> GetAllInventoriesByDepartmentIdAsync(int departmentId);

		/// <summary>
		/// Gets the inventory by identifier asynchronous.
		/// </summary>
		/// <param name="inventoryId">The inventory identifier.</param>
		/// <returns>Task&lt;Inventory&gt;.</returns>
		Task<Inventory> GetInventoryByIdAsync(int inventoryId);

		Task<bool> DeleteInventoriesByGroupIdAsync(int groupId, int departmentId, CancellationToken cancellationToken = default(CancellationToken));
	}
}
