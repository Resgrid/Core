using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class InventoryService: IInventoryService
	{
		private readonly IInventoryTypesRepository _inventoryTypesRepository;
		private readonly IInventoryRepository _inventoryRepository;
		private readonly IDepartmentGroupsService _departmentGroupsService;

		public InventoryService(IInventoryTypesRepository inventoryTypesRepository, IInventoryRepository inventoryRepository, IDepartmentGroupsService departmentGroupsService)
		{
			_inventoryTypesRepository = inventoryTypesRepository;
			_inventoryRepository = inventoryRepository;
			_departmentGroupsService = departmentGroupsService;
		}

		public async Task<InventoryType> GetTypeByIdAsync(int typeId)
		{
			return await _inventoryTypesRepository.GetByIdAsync(typeId);
		}

		public async Task<InventoryType> SaveTypeAsync(InventoryType type, CancellationToken cancellationToken = default(CancellationToken))
		{
			return await _inventoryTypesRepository.SaveOrUpdateAsync(type, cancellationToken);
		}

		public async Task<Inventory> GetInventoryByIdAsync(int inventoryId)
		{
			var inventory = await _inventoryRepository.GetInventoryByIdAsync(inventoryId);

			if (inventory != null && inventory.GroupId > 0)
				inventory.Group = await _departmentGroupsService.GetGroupByIdAsync(inventory.GroupId);

			return inventory;
		}

		public async Task<Inventory> SaveInventoryAsync(Inventory inventory, CancellationToken cancellationToken = default(CancellationToken))
		{
			return await _inventoryRepository.SaveOrUpdateAsync(inventory, cancellationToken);
		}

		public async Task<bool> DeleteTypeAsync(int typeId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var type = await GetTypeByIdAsync(typeId);
			var inventories = await _inventoryRepository.GetInventoryByTypeIdAsync(typeId);

			foreach (var inventory in inventories)
			{
				await _inventoryRepository.DeleteAsync(inventory, cancellationToken);
			}

			await _inventoryTypesRepository.DeleteAsync(type, cancellationToken);

			return true;
		}

		public async Task<List<Inventory>> GetAllTransactionsForDepartmentAsync(int departmentId)
		{
			var inventories = await _inventoryRepository.GetAllInventoriesByDepartmentIdAsync(departmentId);

			foreach (var inventory in inventories)
			{
				inventory.Group = await _departmentGroupsService.GetGroupByIdAsync(inventory.GroupId);
			}

			return inventories.ToList();
		}

		public async Task<List<InventoryType>> GetAllTypesForDepartmentAsync(int departmentId)
		{
			var types = await _inventoryTypesRepository.GetAllByDepartmentIdAsync(departmentId);
			return types.ToList();
		}

		public async Task<List<Inventory>> GetConsolidatedInventoryForDepartment(int departmentId)
		{
			var consolidated = new List<Inventory>();
			var inventory = await GetAllTransactionsForDepartmentAsync(departmentId);

			foreach (var inv in inventory)
			{
				var current = consolidated.FirstOrDefault(x => x.TypeId == inv.TypeId && x.GroupId == inv.GroupId && x.UnitId == inv.UnitId);

				if (current != null)
					current.Amount += inv.Amount;
				else
				{
					consolidated.Add(inv);
				}
			}

			return consolidated;
		}

		public async Task<bool> DeleteInventoriesByGroupIdAsync(int groupId, int departmentId, CancellationToken cancellationToken = default(CancellationToken))
		{
			return await _inventoryRepository.DeleteInventoriesByGroupIdAsync(groupId, departmentId, cancellationToken);
		}
	}
}
