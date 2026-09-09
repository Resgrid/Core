using System;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using Resgrid.Model;
using Resgrid.Model.Inventories;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Services;
using Resgrid.Web.Areas.User.Models.Inventory;

namespace Resgrid.Web.Areas.User.Controllers
{
	public sealed partial class InventoryController
	{
		private IInventoryOperationsService OperationsService => HttpContext.RequestServices.GetService<IInventoryOperationsService>()
			?? throw new InventoryException(409, "OperationUnavailable");
		private async Task<bool> MayViewInventoryReportsAsync()
		{
			var authorization = HttpContext.RequestServices.GetService<Microsoft.AspNetCore.Authorization.IAuthorizationService>();
			return authorization != null && (await authorization.AuthorizeAsync(User, null, ResgridResources.Reports_View)).Succeeded;
		}
		private async Task RequireInventoryReportsAsync()
		{
			await _auth.RequireAsync(Actor);
			if (!await MayViewInventoryReportsAsync()) throw new InventoryException(403, "PermissionRequired");
		}

		[HttpGet]
		public async Task<IActionResult> Operations(string tab = "Counts", int page = 0, string id = null, string itemId = null, string locationId = null, InventoryReportKind? kind = null)
		{
			if (tab is not ("Counts" or "Alerts" or "Reports") || page < 0 || page > 10000) throw new InventoryException(400, "InvalidPage");
			if (kind.HasValue && !Enum.IsDefined(kind.Value)) throw new InventoryException(400, "InvalidInput");
			var view = new InventoryWorkspaceView { Tab = tab, Page = page, Id = id, ItemId = itemId, LocationId = locationId, ReportKind = kind ?? InventoryReportKind.OnHand,
				Migrated = await _migration.IsMigratedAsync(DepartmentId), CanViewReports = await MayViewInventoryReportsAsync() };
			var groupId = (await _groups.GetGroupForUserAsync(UserId, DepartmentId))?.DepartmentGroupId;
			view.CanWrite = await MayWriteAsync(PermissionTypes.AdjustInventory, groupId);
			if (tab == "Reports" && !view.CanViewReports) throw new InventoryException(403, "PermissionRequired");
			if (!view.Migrated) return View("Operations", view);
			// Protected browser navigation opens a shell; disclosure requires the CSRF-protected reveal POST.
			if (HttpMethods.IsGet(Request.Method) && await _protection.IsProtectionEnforcedAsync(DepartmentId))
			{
				view.Locked = true; return View("Operations", view);
			}
			try
			{
				view.Items = await PurchasingChoicesAsync<InventoryItem>();
				view.Locations = await PurchasingChoicesAsync<InventoryLocation>();
				if (tab == "Counts")
				{
					if (id == null) await QueryAsync<InventoryCount>(view, new InventoryQuery { LocationId = locationId });
					else view.CountDetail = await OperationsService.GetCountAsync(Actor, id);
				}
				else if (tab == "Alerts") await QueryAsync<InventoryAlert>(view, new InventoryQuery { ItemId = itemId, LocationId = locationId });
				else if (tab == "Reports")
				{
					foreach (var unit in await _units.GetUnitsForDepartmentAsync(DepartmentId) ?? new())
						if (await _auth.CanLocationAsync(Actor, new InventoryLocation { DepartmentId = DepartmentId, LocationType = (int)InventoryLocationType.Unit, UnitId = unit.UnitId }))
							view.Units.Add(new InventoryChoice { Id = unit.UnitId.ToString(CultureInfo.InvariantCulture), Name = unit.Name });
					foreach (var person in await _departments.GetAllPersonnelNamesForDepartmentAsync(DepartmentId) ?? new())
						if (await _auth.CanLocationAsync(Actor, new InventoryLocation { DepartmentId = DepartmentId, LocationType = (int)InventoryLocationType.Personnel, UserId = person.UserId }))
							view.People.Add(new InventoryChoice { Id = person.UserId, Name = person.Name });
				}
				return View("Operations", view);
			}
			catch (InventoryException error) when (error.Code == "ProtectedDataRequired" && HttpMethods.IsGet(Request.Method))
			{
				return View("Operations", new InventoryWorkspaceView { Locked = true, Migrated = view.Migrated, CanWrite = view.CanWrite,
					CanViewReports = view.CanViewReports, Tab = tab, Page = page, Id = id, ItemId = itemId, LocationId = locationId, ReportKind = view.ReportKind });
			}
		}

		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> ReopenOperations(string tab = "Counts", int page = 0, string id = null, string itemId = null, string locationId = null, InventoryReportKind? kind = null)
			=> Operations(tab, page, id, itemId, locationId, kind);
		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> GetCounts(int page = 0, string locationId = null)
			=> Json(await _catalog.QueryAsync<InventoryCount>(Actor, new InventoryQuery { LocationId = locationId }, page));
		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> GetCount(string id) => Json(await OperationsService.GetCountAsync(Actor, id));
		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> GetAlerts(int page = 0, string itemId = null, string locationId = null, string assetId = null)
			=> Json(await _catalog.QueryAsync<InventoryAlert>(Actor, new InventoryQuery { ItemId = itemId, LocationId = locationId, AssetId = assetId }, page));
		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> StartCount(InventoryCountInput input)
		{
			if (input == null) throw new InventoryException(400, "InvalidInput");
			PurchasingRequestId(input.Id); return Json(await OperationsService.StartCountAsync(Actor, input));
		}
		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> SaveCount(InventoryCountUpdate input)
		{
			if (input == null) throw new InventoryException(400, "InvalidInput");
			return Json(await OperationsService.SaveCountAsync(Actor, input));
		}
		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> CompleteCount(InventoryCountComplete input)
		{
			if (input == null) throw new InventoryException(400, "InvalidInput");
			PurchasingRequestId(input.RequestId); return Json(await OperationsService.CompleteCountAsync(Actor, input));
		}
		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> CancelCount(string countId, int revision)
		{
			await OperationsService.CancelCountAsync(Actor, countId, revision); return Json(new { success = true });
		}
		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> RefreshAlerts()
		{
			await OperationsService.RefreshAlertsAsync(Actor); return Json(new { success = true });
		}
		[HttpPost, ValidateAntiForgeryToken, Authorize(Policy = ResgridResources.Reports_View)]
		public async Task<IActionResult> BuildReport(InventoryReportInput input)
		{
			if (input == null) throw new InventoryException(400, "InvalidInput");
			await RequireInventoryReportsAsync(); return Json(await OperationsService.BuildReportAsync(Actor, input));
		}
		[HttpPost, ValidateAntiForgeryToken, Authorize(Policy = ResgridResources.Reports_View)]
		public async Task<IActionResult> ReportPdf(InventoryReportInput input, [FromServices] IPdfProvider pdf)
		{
			if (input == null) throw new InventoryException(400, "InvalidInput");
			if (pdf == null) throw new InventoryException(409, "OperationUnavailable");
			await RequireInventoryReportsAsync();
			var report = await OperationsService.BuildReportAsync(Actor, input);
			byte[] data;
			try { data = pdf.ConvertHtmlToPdf(InventoryReportDocuments.Build(report, CultureInfo.CurrentUICulture)); }
			catch (InventoryException) { throw; }
			catch (Exception error) when (error is not OperationCanceledException)
			{
				Resgrid.Framework.Logging.LogError($"Inventory report rendering failed: {error.GetType().FullName}.");
				throw new InventoryException(409, "ReportUnavailable");
			}
			if (data == null || data.Length == 0) throw new InventoryException(409, "OperationUnavailable");
			// Freeze effective date bounds and re-read every source after potentially slow PDF conversion.
			// Default expiration reports deliberately have no lower bound, unlike an explicit date-range query.
			var freezeBounds = input.Kind != InventoryReportKind.Expiration || input.FromUtc.HasValue || input.UntilUtc.HasValue;
			var current = await OperationsService.BuildReportAsync(Actor, new InventoryReportInput { Kind = input.Kind, FromUtc = freezeBounds ? report.FromUtc : null,
				UntilUtc = freezeBounds ? report.UntilUtc : null, ItemId = input.ItemId, LocationId = input.LocationId, UnitId = input.UnitId, UserId = input.UserId });
			if (!JToken.DeepEquals(InventoryReportSnapshot(report), InventoryReportSnapshot(current))) throw new InventoryException(409, "ReportChanged");
			await RequireInventoryReportsAsync();
			Response.Headers["Cache-Control"] = "no-store";
			return File(data, "application/pdf", "inventory-" + report.Kind.ToString().ToLowerInvariant() + ".pdf");
		}
		private static JToken InventoryReportSnapshot(InventoryReport report)
			=> JToken.FromObject(new { report.Kind, report.Columns, report.Rows, report.Totals });
	}
}
