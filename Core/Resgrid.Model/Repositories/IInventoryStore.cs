using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Inventories;

namespace Resgrid.Model.Repositories
{
	public interface IInventoryStore
	{
		Task LockDepartmentAsync(int departmentId);
		Task<T> GetAsync<T>(int departmentId, string id) where T : InventoryRow;
		Task<List<T>> ListAsync<T>(int departmentId, int skip = 0) where T : InventoryRow;
		Task<List<T>> QueryAsync<T>(int departmentId, InventoryQuery filter, int skip = 0) where T : InventoryRow;
		Task<List<T>> RelatedAsync<T>(int departmentId, string column, string id) where T : InventoryRow;
		Task InsertAsync<T>(T row) where T : InventoryRow;
		Task UpdateAsync<T>(T row, int expectedRevision) where T : InventoryRow;
		Task<InventoryOperation> RequestAsync(int departmentId, string requestId);
		Task<InventoryStock> ApplyStockDeltaAsync(int departmentId, string itemId, string locationId, string lotId, decimal delta, string userId);
		Task<InventoryItem> LegacyItemAsync(int departmentId, int typeId);
		Task<InventoryTransaction> LegacyTransactionAsync(int departmentId, int inventoryId);
		Task RebuildStocksAsync(int departmentId);
		Task<bool> HasLegacyMigrationAsync(int departmentId);
	}
}
