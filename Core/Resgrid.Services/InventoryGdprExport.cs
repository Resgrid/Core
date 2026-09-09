using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Inventories;
using Resgrid.Model.Repositories;
namespace Resgrid.Services
{
	public partial class GdprDataExportService
	{
		private readonly IInventoryStore _inventoryStore;
		private async Task<object> BuildInventoryDataAsync(string userId, int departmentId)
		{
			if (_inventoryStore == null) throw new InvalidOperationException("Inventory export storage is unavailable.");
			if (_checklistProtection == null) throw new InvalidOperationException("Inventory export protection is unavailable.");
			async Task<List<T>> Relevant<T>(Func<T, bool> include) where T : InventoryRow
			{
				var result = new List<T>();
				for (var skip = 0; skip <= 100000; skip += 500)
				{
					var rows = await _inventoryStore.ListAsync<T>(departmentId, skip);
					foreach (var row in rows.Take(500).Where(x => x.DepartmentId == departmentId && include(x))) result.Add(await _checklistProtection.Value.ForDisplayAsync(departmentId, row, InventoryTables.Fields<T>()));
					if (rows.Count <= 500) return result;
				}
				throw new InvalidOperationException("Inventory export exceeds the supported department size.");
			}
			var locations = await Relevant<InventoryLocation>(x => x.UserId == userId || x.CreatedBy == userId);
			var locationIds = locations.Where(x => x.UserId == userId).Select(x => x.Id).ToHashSet();
			var issuances = await Relevant<InventoryIssuance>(x => x.IssuedToUserId == userId || x.CreatedBy == userId);
			var issuanceIds = issuances.Select(x => x.Id).ToHashSet();
			var operations = await Relevant<InventoryOperation>(x => x.CreatedBy == userId || x.WitnessUserId == userId);
			return new { Locations = locations, Issuances = issuances,
				Counts = await Relevant<InventoryCount>(x => x.CreatedBy == userId),
				CountItems = await Relevant<InventoryCountItem>(x => x.CreatedBy == userId),
				AlertDeliveries = await Relevant<InventoryAlertDelivery>(x => x.UserId == userId),
				Vendors = await Relevant<InventoryVendor>(x => x.CreatedBy == userId),
				PurchaseOrders = await Relevant<InventoryPurchaseOrder>(x => x.CreatedBy == userId),
				PurchaseOrderItems = await Relevant<InventoryPurchaseOrderItem>(x => x.CreatedBy == userId),
				RecordUsages = await Relevant<RecordInventoryUsage>(x => x.CreatedBy == userId),
				Assets = await Relevant<InventoryAsset>(x => x.CreatedBy == userId || locationIds.Contains(x.CurrentLocationId)),
				Transactions = await Relevant<InventoryTransaction>(x => x.CreatedBy == userId || issuanceIds.Contains(x.IssuanceId) || locationIds.Contains(x.FromLocationId) || locationIds.Contains(x.ToLocationId)),
				Operations = operations.Where(x => x.CreatedBy == userId),
				// A witness's participation does not grant export access to the performer's command or other actors' data.
				// Keep structural facts and declare withheld content without parsing or decrypting the receipt.
				WitnessedOperations = operations.Where(x => x.CreatedBy != userId && x.WitnessUserId == userId)
					.Select(x => new { x.Id, x.DepartmentId, x.RequestId, x.State, x.WitnessUserId, x.ModifiedOn,
						Content = string.IsNullOrEmpty(x.Content) ? null : ProtectedDataEnvelope.RedactionValue }) };
		}
	}
}
