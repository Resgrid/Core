using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Localization;
using Newtonsoft.Json;
using Resgrid.Model.Inventories;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.Inventory;

namespace Resgrid.Web.Services.Controllers.v4
{
	[Route("api/v{VersionId:apiVersion}/[controller]"), ApiVersion("4.0"), ApiExplorerSettings(GroupName = "v4"), Authorize]
	[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None), RequestSizeLimit(1024 * 1024)]
	public sealed partial class InventoryController : V4AuthenticatedApiControllerbase, IAsyncActionFilter, IOrderedFilter
	{
		public int Order => -3000; // Sanitize model-binding errors before ApiController's automatic response.
		private readonly IInventoryCatalogService _catalog;
		private readonly IInventoryPurchasingService _purchasing;
		private readonly IInventoryStockService _stock;
		private readonly IInventoryTransferService _transfers;
		private readonly IInventoryIssuanceService _issuance;
		private readonly IInventoryMigrationService _migration;
		private readonly IInventoryAuthorizationService _authorization;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.Inventory.Inventory> _strings;
		public InventoryController(IInventoryCatalogService catalog, IInventoryStockService stock, IInventoryTransferService transfers, IInventoryIssuanceService issuance,
			IInventoryMigrationService migration, IInventoryAuthorizationService authorization, IStringLocalizer<Resgrid.Localization.Areas.User.Inventory.Inventory> strings, IInventoryPurchasingService purchasing = null)
		{ _catalog = catalog; _stock = stock; _transfers = transfers; _issuance = issuance; _migration = migration; _authorization = authorization; _strings = strings; _purchasing = purchasing; }
		private InventoryActor Actor => new InventoryActor { DepartmentId = DepartmentId, UserId = UserId, GrantToken = Request.Headers[DataProtectionController.GrantHeader].ToString() };
		private OkObjectResult Reply<T>(T value, int count = 1, bool more = false, int page = 0)
		{
			var response = new InventoryApiResult<T> { Data = value, Status = ResponseHelper.Success, PageSize = count, HasMore = more, Page = page };
			ResponseHelper.PopulateV4ResponseData(response); return Ok(response);
		}
		private static T Required<T>(T input) where T : class => input ?? throw new InventoryException(400, "InvalidInput");
		private static void RequireRequestId(string requestId)
		{
			if (!Guid.TryParseExact(requestId, "D", out var id) || id == Guid.Empty) throw new InventoryException(400, "RequestIdRequired");
		}
		private static InventoryCommand Command(InventoryCommand input) { Required(input); RequireRequestId(input.RequestId); return input; }
		[NonAction]
		public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
		{
			Response.Headers["Cache-Control"] = "no-store";
			if (!context.ModelState.IsValid) { context.Result = Failure(new InventoryException(400, "InvalidInput")); return; }
			var executed = await next();
			if (executed.Exception == null || executed.Exception is OperationCanceledException) return;
			var failure = executed.Exception switch
			{
				InventoryException inventory => inventory,
				UnauthorizedAccessException => new InventoryException(403, "PermissionRequired"),
				ArgumentException or JsonException => new InventoryException(400, "InvalidInput"),
				InvalidOperationException => new InventoryException(409, "OperationUnavailable"),
				_ => new InventoryException(500, "OperationFailed")
			};
			if (failure.StatusCode == 500) Resgrid.Framework.Logging.LogError($"Inventory API failed: {executed.Exception.GetType().FullName}.");
			executed.ExceptionHandled = true; executed.Result = Failure(failure);
		}
		private ObjectResult Failure(InventoryException exception)
		{
			var protectedData = exception.Code == "ProtectedDataRequired";
			var problem = new ProblemDetails { Status = exception.StatusCode, Type = protectedData ? "protected_data_required" : "inventory_" + exception.Code, Title = _strings["UnableToComplete"] };
			problem.Extensions["code"] = exception.Code; problem.Extensions["IsRedacted"] = protectedData;
			return new ObjectResult(problem) { StatusCode = exception.StatusCode };
		}
		private async Task<IActionResult> Page<T>(int page) where T : InventoryRow
		{ var result = await _catalog.ListAsync<T>(Actor, page); return Reply(result, result.Items.Count, result.HasMore, page); }
		private async Task<IActionResult> Query<T>(InventoryQuery filter, int page) where T : InventoryRow
		{ var result = await _catalog.QueryAsync<T>(Actor, filter, page); return Reply(result, result.Items.Count, result.HasMore, page); }
		private async Task<IActionResult> Archive<T>(InventoryArchiveInput input) where T : InventoryMutableRow
		{ Required(input); await _catalog.ArchiveAsync<T>(Actor, input.Id, input.Revision); return Reply(new { input.Id, Archived = true }); }

		/// <summary>One page of catalog items. Continue with page+1 while HasMore is true.</summary>
		[HttpGet("GetAll"), HttpGet("GetItems")]
		public Task<IActionResult> GetAll(int page = 0) => Page<InventoryItem>(page);
		[HttpGet("GetItem")]
		public async Task<IActionResult> GetItem(string itemId) => Reply(await _catalog.GetAsync<InventoryItem>(Actor, itemId));
		[HttpPost("SaveItem")]
		public async Task<IActionResult> SaveItem([FromBody] InventoryItemInput input) => Reply(await _catalog.SaveItemAsync(Actor, Required(input)));
		[HttpPost("ArchiveItem"), Authorize(Policy = Resgrid.Providers.Claims.ResgridResources.Inventory_Delete)]
		public Task<IActionResult> ArchiveItem([FromBody] InventoryArchiveInput input) => Archive<InventoryItem>(input);
		[HttpGet("GetCategories")]
		public Task<IActionResult> GetCategories(int page = 0) => Page<InventoryCategory>(page);
		[HttpGet("GetCategory")]
		public async Task<IActionResult> GetCategory(string id) => Reply(await _catalog.GetAsync<InventoryCategory>(Actor, id));
		[HttpPost("SaveCategory")]
		public async Task<IActionResult> SaveCategory([FromBody] InventoryCategoryInput input)
		{ Required(input); return Reply(await _catalog.SaveCategoryAsync(Actor, input.Id, input.Revision, input.Name, input.ParentCategoryId)); }
		[HttpPost("ArchiveCategory"), Authorize(Policy = Resgrid.Providers.Claims.ResgridResources.Inventory_Delete)]
		public Task<IActionResult> ArchiveCategory([FromBody] InventoryArchiveInput input) => Archive<InventoryCategory>(input);
		[HttpGet("GetLocations")]
		public Task<IActionResult> GetLocations(int page = 0) => Page<InventoryLocation>(page);
		[HttpGet("GetLocation")]
		public async Task<IActionResult> GetLocation(string id) => Reply(await _catalog.GetAsync<InventoryLocation>(Actor, id));
		[HttpPost("SaveLocation")]
		public async Task<IActionResult> SaveLocation([FromBody] InventoryLocationInput input) => Reply(await _catalog.SaveLocationAsync(Actor, Required(input)));
		[HttpPost("ArchiveLocation"), Authorize(Policy = Resgrid.Providers.Claims.ResgridResources.Inventory_Delete)]
		public Task<IActionResult> ArchiveLocation([FromBody] InventoryArchiveInput input) => Archive<InventoryLocation>(input);
		[HttpGet("GetLots")]
		public Task<IActionResult> GetLots(int page = 0) => Page<InventoryLot>(page);
		[HttpGet("GetLot")]
		public async Task<IActionResult> GetLot(string id) => Reply(await _catalog.GetAsync<InventoryLot>(Actor, id));
		[HttpPost("CreateLot")]
		public async Task<IActionResult> CreateLot([FromBody] InventoryCreateLotInput input)
		{ Required(input); return Reply(await _catalog.SaveLotAsync(Actor, new InventoryLot { ItemId = input.ItemId, ExpiresOn = input.ExpiresOn }, Required(input.Details))); }
		[HttpGet("GetStocks")]
		public Task<IActionResult> GetStocks(int page = 0, string itemId = null, string locationId = null) => Query<InventoryStock>(new InventoryQuery { ItemId = itemId, LocationId = locationId }, page);
		[HttpGet("GetTransactions")]
		public Task<IActionResult> GetTransactions(int page = 0, string itemId = null, string locationId = null, string assetId = null) => Query<InventoryTransaction>(new InventoryQuery { ItemId = itemId, LocationId = locationId, AssetId = assetId }, page);
		[HttpGet("GetTransaction")]
		public async Task<IActionResult> GetTransaction(string id) => Reply(await _catalog.GetAsync<InventoryTransaction>(Actor, id));
		[HttpGet("GetHistory")]
		public async Task<IActionResult> GetHistory(InventoryReferenceType referenceType, string referenceId)
		{
			if (!Enum.IsDefined(referenceType) || referenceType == InventoryReferenceType.None || string.IsNullOrWhiteSpace(referenceId) || referenceId.Length > 128) throw new InventoryException(400, "InvalidReference");
			var result = await _stock.GetByReferenceAsync(Actor, referenceType, referenceId); return Reply(result, result.Count);
		}
		[HttpGet("GetTransfers")]
		public Task<IActionResult> GetTransfers(int page = 0) => Page<InventoryTransfer>(page);
		[HttpGet("GetTransfer")]
		public async Task<IActionResult> GetTransfer(string id) => Reply(await _catalog.GetAsync<InventoryTransfer>(Actor, id));
		[HttpGet("GetTransferItems")]
		public async Task<IActionResult> GetTransferItems(int page = 0)
		{
			var result = await _catalog.ListAsync<InventoryTransferItem>(Actor, page);
			return Reply(result, result.Items.Count, result.HasMore, page);
		}
		[HttpGet("GetAssets")]
		public Task<IActionResult> GetAssets(int page = 0) => Page<InventoryAsset>(page);
		[HttpGet("GetAsset")]
		public async Task<IActionResult> GetAsset(string id) => Reply(await _catalog.GetAsync<InventoryAsset>(Actor, id));
		[HttpGet("GetIssuances")]
		public Task<IActionResult> GetIssuances(int page = 0, string itemId = null, string userId = null) => Query<InventoryIssuance>(new InventoryQuery { ItemId = itemId, IssuedToUserId = userId }, page);
		[HttpGet("GetIssuance")]
		public async Task<IActionResult> GetIssuance(string id) => Reply(await _catalog.GetAsync<InventoryIssuance>(Actor, id));
		[HttpGet("GetKits")]
		public Task<IActionResult> GetKits(int page = 0) => Page<InventoryKit>(page);
		[HttpGet("GetKit")]
		public async Task<IActionResult> GetKit(string id) => Reply(await _catalog.GetAsync<InventoryKit>(Actor, id));
		[HttpGet("GetKitItems")]
		public Task<IActionResult> GetKitItems(int page = 0) => Page<InventoryKitItem>(page);
		[HttpPost("SaveKit")]
		public async Task<IActionResult> SaveKit([FromBody] InventoryKitInput input) => Reply(await _issuance.SaveKitAsync(Actor, Required(input)));
		[HttpPost("ArchiveKit"), Authorize(Policy = Resgrid.Providers.Claims.ResgridResources.Inventory_Delete)]
		public Task<IActionResult> ArchiveKit([FromBody] InventoryArchiveInput input) => Archive<InventoryKit>(input);

		[HttpGet("GetMigrationStatus")]
		public async Task<IActionResult> GetMigrationStatus()
		{ var actor = Actor; await _authorization.RequireAsync(actor); return Reply(new { Migrated = await _migration.IsMigratedAsync(actor.DepartmentId) }); }
		[HttpPost("Migrate")]
		public async Task<IActionResult> Migrate() => Reply(await _migration.MigrateLegacyAsync(Actor));
		[HttpPost("PostTransaction")]
		public async Task<IActionResult> PostTransaction([FromBody] InventoryCommand input, CancellationToken cancellationToken) => Reply(await _stock.PostTransactionAsync(Actor, Command(input), cancellationToken));
		[HttpPost("CreateTransfer")]
		public async Task<IActionResult> CreateTransfer([FromBody] InventoryCommand input) => Reply(await _transfers.CreateAndCompleteTransferAsync(Actor, Command(input)));
		[HttpPost("CreateAsset")]
		public async Task<IActionResult> CreateAsset([FromBody] InventoryAssetInput input)
		{ Required(input); RequireRequestId(input.RequestId); return Reply(await _issuance.CreateAssetAsync(Actor, input)); }
		[HttpPost("Issue")]
		public async Task<IActionResult> Issue([FromBody] InventoryIssueInput input)
		{ Required(input); RequireRequestId(input.RequestId); return Reply(await _issuance.IssueAsync(Actor, input)); }
		[HttpPost("Return")]
		public async Task<IActionResult> Return([FromBody] InventoryReturnInput input)
		{ Required(input); RequireRequestId(input.RequestId); return Reply(await _issuance.ReturnAsync(Actor, input)); }
		[HttpPost("StatusChange")]
		public async Task<IActionResult> StatusChange([FromBody] InventoryCommand input) => Reply(await _issuance.ChangeAssetStatusAsync(Actor, Command(input)));
		[HttpPost("IssueKit")]
		public async Task<IActionResult> IssueKit([FromBody] InventoryKitIssueInput input)
		{ Required(input); RequireRequestId(input.RequestId); return Reply(await _issuance.IssueKitAsync(Actor, input)); }
		[HttpPost("Witness")]
		public async Task<IActionResult> Witness([FromBody] InventoryWitnessInput input)
		{ Required(input); RequireRequestId(input.RequestId); return Reply(await _stock.WitnessAsync(Actor, input.RequestId, input.Attestation)); }
		[HttpPost("RebuildStocks")]
		public async Task<IActionResult> RebuildStocks() { await _stock.RebuildStocksAsync(Actor); return Reply(new { Rebuilt = true }); }
		[HttpGet("GetUnitEquipment")]
		public async Task<IActionResult> GetUnitEquipment(int unitId)
		{ if (unitId <= 0) throw new InventoryException(400, "InvalidIdentifier"); var result = await _issuance.GetUnitEquipmentAsync(Actor, unitId); return Reply(result, result.Count); }
		[HttpGet("GetIssuable")]
		public async Task<IActionResult> GetIssuable(string itemId = null, string locationId = null)
		{ var result = await _issuance.GetIssuableAsync(Actor, itemId, locationId); return Reply(result, result.Count); }

		/// <summary>Adjustment compatibility route: provide a positive delta and exactly one explicit source/destination location.</summary>
		[HttpPut("UpdateItem")]
		public async Task<IActionResult> UpdateItem([FromBody] InventoryAdjustmentInput input, CancellationToken cancellationToken)
		{
			Required(input); RequireRequestId(input.RequestId);
			if ((input.FromLocationId == null) == (input.ToLocationId == null) || input.Quantity <= 0) throw new InventoryException(400, "ExplicitAdjustmentRequired");
			var command = new InventoryCommand { RequestId = input.RequestId, Lines = new List<InventoryPosting> { new InventoryPosting { ItemId = input.ItemId, AssetId = input.AssetId, LotId = input.LotId,
				FromLocationId = input.FromLocationId, ToLocationId = input.ToLocationId, Quantity = input.Quantity, Type = InventoryTransactionType.Adjust, ExpectedAssetRevision = input.ExpectedAssetRevision, Note = input.Note } } };
			return Reply(await _stock.PostTransactionAsync(Actor, command, cancellationToken));
		}
		/// <summary>Bulk items at/below their reorder point, using only stock locations this caller can view. Page is the catalog page.</summary>
		[HttpGet("GetLowStockItems")]
		public async Task<IActionResult> GetLowStockItems(int page = 0)
		{
			var actor = Actor; var items = await _catalog.ListAsync<InventoryItem>(actor, page);
			var itemIds = items.Items.Where(i => !i.IsDeleted && i.IsActive && i.TrackingMode == (int)InventoryTrackingMode.Bulk).Select(i => i.Id).ToArray();
			var totals = await _stock.GetVisibleQuantitiesAsync(actor, itemIds);
			var result = new InventoryPage<InventoryLowStockItem> { HasMore = items.HasMore };
			foreach (var item in items.Items.Where(i => totals.ContainsKey(i.Id)))
			{
				var details = JsonConvert.DeserializeObject<InventoryItemContent>(item.Content ?? "{}") ?? new InventoryItemContent();
				var threshold = details.ReorderPoint ?? details.MinLevel;
				if (threshold.HasValue && totals[item.Id] <= threshold.Value) result.Items.Add(new InventoryLowStockItem { Item = item, VisibleQuantity = totals[item.Id], ReorderPoint = threshold.Value });
			}
			return Reply(result, result.Items.Count, result.HasMore, page);
		}
	}
}
