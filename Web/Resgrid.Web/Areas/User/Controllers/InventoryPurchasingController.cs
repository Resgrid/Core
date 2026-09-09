using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Inventories;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Models.Inventory;

namespace Resgrid.Web.Areas.User.Controllers
{
	public sealed partial class InventoryController
	{
		private IInventoryPurchasingService PurchasingService => _purchasing ?? throw new InventoryException(409, "OperationUnavailable");
		private static void PurchasingRequestId(string value)
		{
			if (!Guid.TryParseExact(value, "D", out var id) || id == Guid.Empty) throw new InventoryException(400, "RequestIdRequired");
		}
		private async Task<List<T>> PurchasingChoicesAsync<T>() where T : InventoryRow
		{
			var choices = new List<T>();
			for (var page = 0; page < 20; page++)
			{
				var result = await _catalog.ListAsync<T>(Actor, page);
				choices.AddRange(result.Items);
				if (!result.HasMore) return choices;
			}
			throw new InventoryException(409, "InventoryTooLarge");
		}

		[HttpGet]
		public async Task<IActionResult> Purchasing(string tab = "PurchaseOrders", int page = 0, string id = null, string locationId = null)
		{
			if (tab is not ("Vendors" or "PurchaseOrders" or "Valuation") || page < 0 || page > 10000) throw new InventoryException(400, "InvalidPage");
			var view = new InventoryWorkspaceView { Tab = tab, Page = page, Id = id, LocationId = locationId };
			try
			{
				view.Migrated = await _migration.IsMigratedAsync(DepartmentId);
				view.CanWrite = await MayWriteAsync(PermissionTypes.AdjustInventory, null);
				if (!view.Migrated) return View("Purchasing", view);
				if (tab == "Valuation")
				{
					view.Locations = await PurchasingChoicesAsync<InventoryLocation>();
					view.Valuation = await PurchasingService.GetValuationAsync(Actor, locationId);
				}
				else
				{
					view.VendorContacts = await PurchasingService.GetVendorContactsAsync(Actor);
					view.Vendors = await PurchasingChoicesAsync<InventoryVendor>();
					if (tab == "Vendors") await PageAsync<InventoryVendor>(view);
					else
					{
						if (id == null) await PageAsync<InventoryPurchaseOrder>(view);
						else view.PurchaseOrder = await PurchasingService.GetPurchaseOrderAsync(Actor, id);
						view.Items = await PurchasingChoicesAsync<InventoryItem>();
						if (view.PurchaseOrder != null)
						{
							// Historical lines retain their frozen labels; load their current tracking rules separately.
							foreach (var itemId in view.PurchaseOrder.Lines.Select(x => x.ItemId).Distinct().Where(itemId => view.Items.All(x => x.Id != itemId)))
								view.Items.Add(await _catalog.GetAsync<InventoryItem>(Actor, itemId));
							view.Locations = await PurchasingChoicesAsync<InventoryLocation>();
							view.Lots = await PurchasingChoicesAsync<InventoryLot>();
						}
					}
				}
				return View("Purchasing", view);
			}
			catch (InventoryException error) when (error.Code == "ProtectedDataRequired" && HttpMethods.IsGet(Request.Method))
			{
				return View("Purchasing", new InventoryWorkspaceView { Locked = true, Tab = tab, Page = page, Id = id, LocationId = locationId });
			}
		}

		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> ReopenPurchasing(string tab = "PurchaseOrders", int page = 0, string id = null, string locationId = null) => Purchasing(tab, page, id, locationId);
		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> SaveVendor(InventoryVendorInput input)
		{
			if (input == null) throw new InventoryException(400, "InvalidInput");
			return Json(await PurchasingService.SaveVendorAsync(Actor, input));
		}
		[HttpPost, ValidateAntiForgeryToken, Authorize(Policy = Resgrid.Providers.Claims.ResgridResources.Inventory_Delete)]
		public async Task<IActionResult> ArchiveVendor(string id, int revision)
		{
			await _catalog.ArchiveAsync<InventoryVendor>(Actor, id, revision);
			return Json(new { success = true });
		}
		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> SavePurchaseOrder(InventoryPurchaseOrderInput input)
		{
			if (input == null) throw new InventoryException(400, "InvalidInput");
			PurchasingRequestId(input.Id);
			return Json(await PurchasingService.SavePurchaseOrderAsync(Actor, input));
		}
		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> OrderPurchaseOrder(InventoryPurchaseOrderChange input)
		{
			if (input == null) throw new InventoryException(400, "InvalidInput");
			PurchasingRequestId(input.RequestId);
			return Json(await PurchasingService.ChangePurchaseOrderStatusAsync(Actor, input, InventoryPurchaseOrderStatus.Ordered));
		}
		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> CancelPurchaseOrder(InventoryPurchaseOrderChange input)
		{
			if (input == null) throw new InventoryException(400, "InvalidInput");
			PurchasingRequestId(input.RequestId);
			return Json(await PurchasingService.ChangePurchaseOrderStatusAsync(Actor, input, InventoryPurchaseOrderStatus.Cancelled));
		}
		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> ReceivePurchaseOrder(InventoryPurchaseReceiptInput input)
		{
			if (input == null) throw new InventoryException(400, "InvalidInput");
			PurchasingRequestId(input.RequestId);
			return Json(await PurchasingService.ReceivePurchaseOrderAsync(Actor, input));
		}
	}
}
