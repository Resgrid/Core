using System.Collections.Generic;
using System.Linq;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class InventoryService: IInventoryService
	{
		private readonly IGenericDataRepository<InventoryType> _inventoryTypesRepository;
		private readonly IGenericDataRepository<Inventory> _inventoryRepository;

		public InventoryService(IGenericDataRepository<InventoryType> inventoryTypesRepository, IGenericDataRepository<Inventory> inventoryRepository)
		{
			_inventoryTypesRepository = inventoryTypesRepository;
			_inventoryRepository = inventoryRepository;
		}

		public InventoryType GetTypeById(int typeId)
		{
			return _inventoryTypesRepository.GetAll().FirstOrDefault(x => x.InventoryTypeId == typeId);
		}

		public InventoryType SaveType(InventoryType type)
		{
			_inventoryTypesRepository.SaveOrUpdate(type);

			return type;
		}

		public Inventory GetInventoryById(int inventoryId)
		{
			return _inventoryRepository.GetAll().FirstOrDefault(x => x.InventoryId == inventoryId);
		}

		public Inventory SaveInventory(Inventory inventory)
		{
			_inventoryRepository.SaveOrUpdate(inventory);

			return inventory;
		}

		public void DeleteType(int typeId)
		{
			var type = GetTypeById(typeId);
			var inventories = _inventoryRepository.GetAll().Where(x => x.TypeId == typeId);

			_inventoryRepository.DeleteAll(inventories);
			_inventoryTypesRepository.DeleteOnSubmit(type);
		}

		public List<Inventory> GetAllTransactionsForDepartment(int departmentId)
		{
			return _inventoryRepository.GetAll().Where(x => x.DepartmentId == departmentId).ToList();
		}

		public List<InventoryType> GetAllTypesForDepartment(int departmentId)
		{
			return _inventoryTypesRepository.GetAll().Where(x => x.DepartmentId == departmentId).ToList();
		}

		public List<Inventory> GetConsolidatedInventoryForDepartment(int departmentId)
		{
			var consolidated = new List<Inventory>();
			var inventory = GetAllTransactionsForDepartment(departmentId);

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
	}
}
