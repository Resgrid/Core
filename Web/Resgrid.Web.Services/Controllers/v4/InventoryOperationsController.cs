using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Resgrid.Model.Inventories;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;

namespace Resgrid.Web.Services.Controllers.v4
{
	public sealed partial class InventoryController
	{
		private IInventoryOperationsService OperationsService => HttpContext.RequestServices.GetService<IInventoryOperationsService>()
			?? throw new InventoryException(409, "OperationUnavailable");
		[HttpGet("GetCounts")]
		public Task<IActionResult> GetCounts(int page = 0, string locationId = null)
			=> Query<InventoryCount>(new InventoryQuery { LocationId = locationId }, page);
		[HttpGet("GetCount")]
		public async Task<IActionResult> GetCount(string id) => Reply(await OperationsService.GetCountAsync(Actor, id));
		[HttpPost("StartCount")]
		public async Task<IActionResult> StartCount([FromBody] InventoryCountInput input)
		{
			Required(input); RequireRequestId(input.Id); return Reply(await OperationsService.StartCountAsync(Actor, input));
		}
		[HttpPost("SaveCount")]
		public async Task<IActionResult> SaveCount([FromBody] InventoryCountUpdate input) => Reply(await OperationsService.SaveCountAsync(Actor, Required(input)));
		[HttpPost("CompleteCount")]
		public async Task<IActionResult> CompleteCount([FromBody] InventoryCountComplete input)
		{
			Required(input); RequireRequestId(input.RequestId); return Reply(await OperationsService.CompleteCountAsync(Actor, input));
		}
		[HttpPost("CancelCount")]
		public async Task<IActionResult> CancelCount([FromBody] InventoryCountComplete input)
		{
			Required(input); await OperationsService.CancelCountAsync(Actor, input.CountId, input.Revision);
			return Reply(new { input.CountId, Cancelled = true });
		}
		[HttpGet("GetAlerts")]
		public Task<IActionResult> GetAlerts(int page = 0, string itemId = null, string locationId = null, string assetId = null)
			=> Query<InventoryAlert>(new InventoryQuery { ItemId = itemId, LocationId = locationId, AssetId = assetId }, page);
		[HttpPost("RefreshAlerts")]
		public async Task<IActionResult> RefreshAlerts()
		{
			await OperationsService.RefreshAlertsAsync(Actor); return Reply(new { Refreshed = true });
		}
		[HttpPost("BuildReport"), Authorize(Policy = ResgridResources.Reports_View)]
		public async Task<IActionResult> BuildReport([FromBody] InventoryReportInput input)
		{
			var report = await OperationsService.BuildReportAsync(Actor, Required(input)); return Reply(report, report.Rows.Count);
		}
		[HttpGet("GetExpiring"), Authorize(Policy = ResgridResources.Reports_View)]
		public async Task<IActionResult> GetExpiring(DateTime? fromUtc = null, DateTime? untilUtc = null, string itemId = null, string locationId = null)
		{
			var report = await OperationsService.BuildReportAsync(Actor, new InventoryReportInput { Kind = InventoryReportKind.Expiration,
				FromUtc = fromUtc, UntilUtc = untilUtc, ItemId = itemId, LocationId = locationId });
			return Reply(report, report.Rows.Count);
		}
	}
}
