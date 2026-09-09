using System.Collections.Generic;
using System.Threading.Tasks;
using Resgrid.Model.Inventories;

namespace Resgrid.Model.Services
{
	public interface IInventoryPurchasingService
	{
		Task<List<InventoryVendorChoice>> GetVendorContactsAsync(InventoryActor actor);
		Task<InventoryVendor> SaveVendorAsync(InventoryActor actor, InventoryVendorInput input);
		Task<InventoryPurchaseOrderDetail> GetPurchaseOrderAsync(InventoryActor actor, string id);
		Task<InventoryPurchaseOrderDetail> SavePurchaseOrderAsync(InventoryActor actor, InventoryPurchaseOrderInput input);
		Task<InventoryResult> ChangePurchaseOrderStatusAsync(InventoryActor actor, InventoryPurchaseOrderChange input, InventoryPurchaseOrderStatus status);
		Task<InventoryResult> ReceivePurchaseOrderAsync(InventoryActor actor, InventoryPurchaseReceiptInput input);
		Task<InventoryValuation> GetValuationAsync(InventoryActor actor, string locationId = null);
	}
}
