using System.Collections.Generic;

namespace Resgrid.Model.Services
{
	public interface IInventoryService
	{
		List<Inventory> GetAllTransactionsForDepartment(int departmentId);
		List<InventoryType> GetAllTypesForDepartment(int departmentId);
		List<Inventory> GetConsolidatedInventoryForDepartment(int departmentId);
		InventoryType SaveType(InventoryType type);
		InventoryType GetTypeById(int typeId);
		void DeleteType(int typeId);
		Inventory SaveInventory(Inventory inventory);
		Inventory GetInventoryById(int inventoryId);
	}
}