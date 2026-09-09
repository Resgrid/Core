using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model.Inventories;
using Resgrid.Web.Services.Models.v4.Inventory;

namespace Resgrid.Web.Services.Controllers.v4
{
	public sealed partial class InventoryController
	{
		[HttpGet("GetVendorContacts")]
		public async Task<IActionResult> GetVendorContacts() => Reply(await _purchasing.GetVendorContactsAsync(Actor));
		[HttpGet("GetVendors")]
		public Task<IActionResult> GetVendors(int page = 0) => Page<InventoryVendor>(page);
		[HttpGet("GetVendor")]
		public async Task<IActionResult> GetVendor(string id) => Reply(await _catalog.GetAsync<InventoryVendor>(Actor, id));
		[HttpPost("SaveVendor")]
		public async Task<IActionResult> SaveVendor([FromBody] InventoryVendorInput input) => Reply(await _purchasing.SaveVendorAsync(Actor, Required(input)));
		[HttpPost("ArchiveVendor"), Authorize(Policy = Resgrid.Providers.Claims.ResgridResources.Inventory_Delete)]
		public Task<IActionResult> ArchiveVendor([FromBody] InventoryArchiveInput input) => Archive<InventoryVendor>(input);
		[HttpGet("GetPurchaseOrders")]
		public Task<IActionResult> GetPurchaseOrders(int page = 0) => Page<InventoryPurchaseOrder>(page);
		[HttpGet("GetPurchaseOrder")]
		public async Task<IActionResult> GetPurchaseOrder(string id) => Reply(await _purchasing.GetPurchaseOrderAsync(Actor, id));
		[HttpPost("SavePurchaseOrder")]
		public async Task<IActionResult> SavePurchaseOrder([FromBody] InventoryPurchaseOrderInput input)
		{ Required(input); RequireRequestId(input.Id); return Reply(await _purchasing.SavePurchaseOrderAsync(Actor, input)); }
		[HttpPost("OrderPurchaseOrder")]
		public async Task<IActionResult> OrderPurchaseOrder([FromBody] InventoryPurchaseOrderChange input)
		{ Required(input); RequireRequestId(input.RequestId); return Reply(await _purchasing.ChangePurchaseOrderStatusAsync(Actor, input, InventoryPurchaseOrderStatus.Ordered)); }
		[HttpPost("CancelPurchaseOrder")]
		public async Task<IActionResult> CancelPurchaseOrder([FromBody] InventoryPurchaseOrderChange input)
		{ Required(input); RequireRequestId(input.RequestId); return Reply(await _purchasing.ChangePurchaseOrderStatusAsync(Actor, input, InventoryPurchaseOrderStatus.Cancelled)); }
		[HttpPost("ReceivePurchaseOrder")]
		public async Task<IActionResult> ReceivePurchaseOrder([FromBody] InventoryPurchaseReceiptInput input)
		{ Required(input); RequireRequestId(input.RequestId); return Reply(await _purchasing.ReceivePurchaseOrderAsync(Actor, input)); }
		[HttpGet("GetValuation")]
		public async Task<IActionResult> GetValuation(string locationId = null) => Reply(await _purchasing.GetValuationAsync(Actor, locationId));
	}
}
