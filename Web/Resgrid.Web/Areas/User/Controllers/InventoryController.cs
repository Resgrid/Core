using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Localization;
using Resgrid.Model;
using Resgrid.Model.Inventories;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Models.Inventory;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Areas.User.Controllers
{
	[WorkOrderFormCulture, Area("User"), Authorize, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None), RequestSizeLimit(1024 * 1024)]
	public sealed partial class InventoryController : SecureBaseController
	{
		private readonly IInventoryCatalogService _catalog;
		private readonly IInventoryPurchasingService _purchasing;
		private readonly IInventoryStockService _stock;
		private readonly IInventoryTransferService _transfers;
		private readonly IInventoryIssuanceService _issuance;
		private readonly IInventoryMigrationService _migration;
		private readonly IInventoryAuthorizationService _auth;
		private readonly IProtectedGrantContext _grant;
		private readonly IDepartmentDataProtectionService _protection;
		private readonly IUnitsService _units;
		private readonly IDepartmentGroupsService _groups;
		private readonly IDepartmentsService _departments;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.Inventory.Inventory> _strings;
		public InventoryController(IInventoryCatalogService catalog, IInventoryStockService stock, IInventoryTransferService transfers, IInventoryIssuanceService issuance,
			IInventoryMigrationService migration, IInventoryAuthorizationService auth, IProtectedGrantContext grant, IDepartmentDataProtectionService protection,
			IUnitsService units, IDepartmentGroupsService groups, IDepartmentsService departments, IStringLocalizer<Resgrid.Localization.Areas.User.Inventory.Inventory> strings, IInventoryPurchasingService purchasing = null)
		{ _catalog = catalog; _stock = stock; _transfers = transfers; _issuance = issuance; _migration = migration; _auth = auth; _grant = grant; _protection = protection; _units = units; _groups = groups; _departments = departments; _strings = strings; _purchasing = purchasing; }
		private InventoryActor Actor => new() { DepartmentId = DepartmentId, UserId = UserId, GrantToken = _grant.GrantToken };
		public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
		{
			Response.Headers["Cache-Control"] = "no-store";
			try { await _auth.RequireAsync(Actor); } catch (InventoryException ex) { context.Result = StatusCode(ex.StatusCode); return; }
			ViewBag.ProtectionEnforced = await _protection.IsProtectionEnforcedAsync(DepartmentId); ViewBag.ProtectedGrant = _grant.GrantToken; ViewBag.GrantExpiresOn = HttpProtectedGrantContext.ReadExpiry(Request);
			if (!ModelState.IsValid) { context.Result = BadRequest(new { message = _strings["UnableToComplete"].Value }); return; }
			var executed = await next();
			if (executed.Exception is InventoryException error)
			{
				executed.ExceptionHandled = true;
				if (HttpMethods.IsGet(Request.Method) && error.Code == "ProtectedDataRequired") executed.Result = View("Workspace", new InventoryWorkspaceView { Locked = true,
					Tab = context.ActionArguments.TryGetValue("tab", out var tab) ? tab as string ?? "OnHand" : "OnHand", Id = context.ActionArguments.TryGetValue("id", out var id) ? id as string : null,
					Page = context.ActionArguments.TryGetValue("page", out var page) && page is int pageNumber ? pageNumber : 0,
					UnitId = context.ActionArguments.TryGetValue("unitId", out var unit) && unit is int unitNumber ? unitNumber : null, UserId = context.ActionArguments.TryGetValue("userId", out var person) ? person as string : null,
					ItemId = context.ActionArguments.TryGetValue("itemId", out var item) ? item as string : null, LocationId = context.ActionArguments.TryGetValue("locationId", out var location) ? location as string : null });
				else
				{
					var localized = _strings[error.Code];
					executed.Result = StatusCode(error.StatusCode, new { message = localized.ResourceNotFound ? _strings["UnableToComplete"].Value : localized.Value, code = error.Code });
				}
			}
		}
		private async Task PageAsync<T>(InventoryWorkspaceView view) where T : InventoryRow
		{ var result = await _catalog.ListAsync<T>(Actor, view.Page); view.Rows = result.Items.Cast<InventoryRow>().ToList(); view.HasMore = result.HasMore; }
		private async Task QueryAsync<T>(InventoryWorkspaceView view, InventoryQuery filter) where T : InventoryRow
		{ var result = await _catalog.QueryAsync<T>(Actor, filter, view.Page); view.Rows = result.Items.Cast<InventoryRow>().ToList(); view.HasMore = result.HasMore; }
		private async Task<bool> MayWriteAsync(PermissionTypes permission, int? groupId)
		{
			try { await _auth.RequireAsync(Actor, true, permission, groupId); return true; }
			catch (InventoryException ex) when (ex.StatusCode is 403 or 409) { return false; }
		}
		[HttpGet]
		public async Task<IActionResult> Index(string tab = "OnHand", int page = 0, string id = null, int? unitId = null, string userId = null, string itemId = null, string locationId = null)
		{
			var view = new InventoryWorkspaceView { Tab = tab, Page = page, Id = id, UnitId = unitId, UserId = userId, ItemId = itemId, LocationId = locationId, Migrated = await _migration.IsMigratedAsync(DepartmentId) };
			var actorGroupId = (await _groups.GetGroupForUserAsync(UserId, DepartmentId))?.DepartmentGroupId;
			view.CanWrite = await MayWriteAsync(PermissionTypes.AdjustInventory, actorGroupId);
			view.CanTransfer = await MayWriteAsync(PermissionTypes.TransferInventory, actorGroupId);
			view.CanIssue = await MayWriteAsync(PermissionTypes.IssueInventory, actorGroupId);
			view.CanWitness = await MayWriteAsync(PermissionTypes.ManageControlledSubstances, actorGroupId);
			if (!view.Migrated) return View("Workspace", view);
			var items = await _catalog.ListAsync<InventoryItem>(Actor); view.Items = items.Items;
			var locations = await _catalog.ListAsync<InventoryLocation>(Actor); view.Locations = locations.Items;
			var categories = await _catalog.ListAsync<InventoryCategory>(Actor); view.Categories = categories.Items;
			view.ChoicesHaveMore = items.HasMore || locations.HasMore || categories.HasMore;
			var assets = await _catalog.ListAsync<InventoryAsset>(Actor); view.Assets = assets.Items;
			var lots = await _catalog.ListAsync<InventoryLot>(Actor); view.Lots = lots.Items;
			view.ChoicesHaveMore |= assets.HasMore || lots.HasMore;
			if (_purchasing != null && view.CanWrite && tab is "Items" or "Lots")
			{
				try
				{
					view.VendorContacts = await _purchasing.GetVendorContactsAsync(Actor);
					view.Vendors = await PurchasingChoicesAsync<InventoryVendor>();
					view.CanChooseVendors = true;
				}
				catch (InventoryException error) when (error.StatusCode == 403) { /* Catalog editing remains available without purchasing access. */ }
			}
			switch (tab)
			{
				case "OnHand": await QueryAsync<InventoryStock>(view, new InventoryQuery { ItemId = itemId, LocationId = locationId }); break;
				case "Items": await PageAsync<InventoryItem>(view); break;
				case "Categories": await PageAsync<InventoryCategory>(view); break;
				case "Locations": await PageAsync<InventoryLocation>(view); break;
				case "Lots": await PageAsync<InventoryLot>(view); break;
				case "Assets": await PageAsync<InventoryAsset>(view); break;
				case "Issuances": await PageAsync<InventoryIssuance>(view); break;
				case "Kits":
					await PageAsync<InventoryKit>(view);
					foreach (var kit in view.Rows)
					{
						var contents = await _catalog.QueryAsync<InventoryKitItem>(Actor, new InventoryQuery { KitId = kit.Id });
						if (contents.HasMore) throw new InventoryException(409, "InventoryTooLarge");
						view.KitContents.AddRange(contents.Items);
					}
					foreach (var component in view.KitContents.Select(x => x.ItemId).Distinct().Where(x => view.Items.All(i => i.Id != x))) view.Items.Add(await _catalog.GetAsync<InventoryItem>(Actor, component));
					break;
				case "Transfers": await PageAsync<InventoryTransfer>(view); break;
				case "History": await QueryAsync<InventoryTransaction>(view, new InventoryQuery { ItemId = itemId, LocationId = locationId }); break;
				case "AssetDetail": await QueryAsync<InventoryTransaction>(view, new InventoryQuery { AssetId = id }); view.Rows.Insert(0, await _catalog.GetAsync<InventoryAsset>(Actor, id)); break;
				case "Transaction": view.Rows.Add(await _catalog.GetAsync<InventoryTransaction>(Actor, id)); break;
				case "UnitEquipment": if (!unitId.HasValue) throw new InventoryException(400, "HolderRequired"); view.Rows.AddRange((await _issuance.GetUnitEquipmentAsync(Actor, unitId.Value)).Select(e => (InventoryRow)e.Asset ?? e.Stock)); break;
				case "PersonnelGear": if (string.IsNullOrWhiteSpace(userId)) throw new InventoryException(400, "HolderRequired"); await QueryAsync<InventoryIssuance>(view, new InventoryQuery { IssuedToUserId = userId }); break;
				default: throw new InventoryException(400, "InvalidPage");
			}
			foreach (var unit in await _units.GetUnitsForDepartmentAsync(DepartmentId))
				if (await _auth.CanLocationAsync(Actor, new InventoryLocation { DepartmentId = DepartmentId, LocationType = 2, UnitId = unit.UnitId })) view.Units.Add(new() { Id = unit.UnitId.ToString(), Name = unit.Name });
			if (view.CanWrite && tab == "Locations")
				foreach (var group in await _groups.GetAllGroupsForDepartmentAsync(DepartmentId))
					if (await _auth.CanLocationAsync(Actor, new InventoryLocation { DepartmentId = DepartmentId, LocationType = 1, GroupId = group.DepartmentGroupId })) view.Groups.Add(new() { Id = group.DepartmentGroupId.ToString(), Name = group.Name });
			foreach (var person in await _departments.GetAllPersonnelNamesForDepartmentAsync(DepartmentId))
				if (await _auth.CanLocationAsync(Actor, new InventoryLocation { DepartmentId = DepartmentId, LocationType = 3, UserId = person.UserId })) view.People.Add(new() { Id = person.UserId, Name = person.Name });
			return View("Workspace", view);
		}
		[HttpPost, ValidateAntiForgeryToken] public Task<IActionResult> Reopen(string tab = "OnHand", int page = 0, string id = null, int? unitId = null, string userId = null, string itemId = null, string locationId = null) => Index(tab, page, id, unitId, userId, itemId, locationId);
		[HttpPost, ValidateAntiForgeryToken] public async Task<IActionResult> Initialize() => Json(await _migration.MigrateLegacyAsync(Actor));
		[HttpPost, ValidateAntiForgeryToken] public async Task<IActionResult> SaveItem(InventoryItemInput input) => Json(await _catalog.SaveItemAsync(Actor, input));
		[HttpPost, ValidateAntiForgeryToken] public async Task<IActionResult> SaveCategory(string id, int revision, string name, string parentId) => Json(await _catalog.SaveCategoryAsync(Actor, id, revision, name, parentId));
		[HttpPost, ValidateAntiForgeryToken] public async Task<IActionResult> SaveLocation(InventoryLocationInput input) => Json(await _catalog.SaveLocationAsync(Actor, input));
		[HttpPost, ValidateAntiForgeryToken] public async Task<IActionResult> CreateLot(string itemId, DateTime? expiresOn, InventoryLotContent details) => Json(await _catalog.SaveLotAsync(Actor, new InventoryLot { ItemId = itemId, ExpiresOn = expiresOn }, details));
		[HttpPost, ValidateAntiForgeryToken] public async Task<IActionResult> Post(InventoryCommand command) => Json(await _stock.PostTransactionAsync(Actor, command));
		[HttpPost, ValidateAntiForgeryToken] public async Task<IActionResult> Transfer(InventoryCommand command) => Json(await _transfers.CreateAndCompleteTransferAsync(Actor, command));
		[HttpPost, ValidateAntiForgeryToken] public async Task<IActionResult> CreateAsset(InventoryAssetInput input) => Json(await _issuance.CreateAssetAsync(Actor, input));
		[HttpPost, ValidateAntiForgeryToken] public async Task<IActionResult> Issue(InventoryIssueInput input) => Json(await _issuance.IssueAsync(Actor, input));
		[HttpPost, ValidateAntiForgeryToken] public async Task<IActionResult> Return(InventoryReturnInput input) => Json(await _issuance.ReturnAsync(Actor, input));
		[HttpPost, ValidateAntiForgeryToken] public async Task<IActionResult> Status(InventoryCommand command) => Json(await _issuance.ChangeAssetStatusAsync(Actor, command));
		[HttpPost, ValidateAntiForgeryToken] public async Task<IActionResult> SaveKit(InventoryKitInput input) => Json(await _issuance.SaveKitAsync(Actor, input));
		[HttpPost, ValidateAntiForgeryToken] public async Task<IActionResult> IssueKit(InventoryKitIssueInput input, string userId, int? unitId)
		{ if (input?.Lines == null) throw new InventoryException(400, "InvalidKit"); foreach (var line in input.Lines) { line.UserId = userId; line.UnitId = unitId; } return Json(await _issuance.IssueKitAsync(Actor, input)); }
		[HttpPost, ValidateAntiForgeryToken] public async Task<IActionResult> Witness(string requestId, string attestation) => Json(await _stock.WitnessAsync(Actor, requestId, attestation));
		[HttpPost, ValidateAntiForgeryToken] public async Task<IActionResult> Rebuild() { await _stock.RebuildStocksAsync(Actor); return Json(new { success = true }); }
		[HttpPost, ValidateAntiForgeryToken, Authorize(Policy = Resgrid.Providers.Claims.ResgridResources.Inventory_Delete)]
		public async Task<IActionResult> Archive(string kind, string id, int revision)
		{
			switch (kind) { case "Items": await _catalog.ArchiveAsync<InventoryItem>(Actor, id, revision); break; case "Categories": await _catalog.ArchiveAsync<InventoryCategory>(Actor, id, revision); break; case "Locations": await _catalog.ArchiveAsync<InventoryLocation>(Actor, id, revision); break; case "Kits": await _catalog.ArchiveAsync<InventoryKit>(Actor, id, revision); break; default: throw new InventoryException(400, "InvalidInput"); }
			return Json(new { success = true });
		}
		[HttpGet] public IActionResult ManageTypes() => RedirectToAction("Index", new { tab = "Items" });
		[HttpGet] public IActionResult AddType() => RedirectToAction("Index", new { tab = "Items" });
		[HttpGet] public IActionResult EditType(int typeId) => RedirectToAction("Index", new { tab = "Items" });
		[HttpGet] public IActionResult Adjust() => RedirectToAction("Index");
		[HttpGet] public IActionResult History() => RedirectToAction("Index", new { tab = "History" });
		[HttpGet] public IActionResult ByUnit(int unitId) => RedirectToAction("Index", new { tab = "UnitEquipment", unitId });
		[HttpGet] public IActionResult UnitEquipment(int unitId) => RedirectToAction("Index", new { tab = "UnitEquipment", unitId });
		[HttpGet] public IActionResult PersonnelGear(string userId) => RedirectToAction("Index", new { tab = "PersonnelGear", userId });
		[HttpGet] public IActionResult AssetDetail(string id) => RedirectToAction("Index", new { tab = "AssetDetail", id });
		[HttpGet] public async Task<IActionResult> ViewEntry(int inventoryId)
		{
			var row = await _catalog.GetLegacyTransactionAsync(Actor, inventoryId);
			if (row != null) return RedirectToAction("Index", new { tab = "Transaction", id = row.Id });
			return NotFound();
		}
	}
}
