using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Localization;
using Newtonsoft.Json;
using Resgrid.Model;
using Resgrid.Model.Inventories;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Models.Records;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Areas.User.Controllers
{
	[Area("User")]
	[Authorize(Policy = ResgridResources.Record_Create)]
	[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None), RequestSizeLimit(1024 * 1024)]
	public class RecordsInventoryController : SecureBaseController
	{
		private readonly IRmsInventoryUsageAdapter _usage;
		private readonly IRecordsEvidenceService _evidence;
		private readonly IRecordsAuthorizationService _auth;
		private readonly IRecordsCutoverService _cutover;
		private readonly IRecordsService _records;
		private readonly IIncidentReportsService _incidents;
		private readonly IInventoryService _inventory;
		private readonly IDepartmentGroupsService _groups;
		private readonly IUnitsService _units;
		private readonly IInventoryCatalogService _modernCatalog;
		private readonly IInventoryMigrationService _migration;
		private readonly IProtectedGrantContext _grant;
		private readonly IDepartmentDataProtectionService _protection;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.Inventory.Inventory> _strings;
		public RecordsInventoryController(IRmsInventoryUsageAdapter usage, IRecordsEvidenceService evidence, IRecordsAuthorizationService auth, IRecordsCutoverService cutover,
			IRecordsService records, IIncidentReportsService incidents, IInventoryService inventory, IDepartmentGroupsService groups, IUnitsService units,
			IInventoryCatalogService modernCatalog = null, IInventoryMigrationService migration = null, IProtectedGrantContext grant = null,
			IDepartmentDataProtectionService protection = null, IStringLocalizer<Resgrid.Localization.Areas.User.Inventory.Inventory> strings = null)
		{ _usage=usage; _evidence=evidence; _auth=auth; _cutover=cutover; _records=records; _incidents=incidents; _inventory=inventory; _groups=groups; _units=units; _modernCatalog=modernCatalog; _migration=migration; _grant=grant; _protection=protection; _strings=strings; }
		private InventoryActor Actor => new() { DepartmentId = DepartmentId, UserId = UserId, GrantToken = _grant?.GrantToken };
		private string UnableToComplete => _strings?["UnableToComplete"].Value ?? "Unable to complete this inventory action.";
		private Task<bool> IsModernAsync() => _migration?.IsMigratedAsync(DepartmentId) ?? Task.FromResult(false);
		private async Task<long?> VersionAsync(string recordId, RmsRecordKind kind)
		{
			if (!(await _cutover.GetModuleStateAsync(DepartmentId)).RecordsUsable || !await _auth.CanUserViewRecordAsync(UserId,recordId,DepartmentId) || !await _auth.HasPermissionAsync(UserId,DepartmentId,PermissionTypes.ViewRestrictedRecords) || !await _auth.CanUseSourceInventoryAsync(UserId,DepartmentId,null)) return null;
			if (kind==RmsRecordKind.Operational) return (await _records.GetAsync(DepartmentId,recordId))?.Record.RowVersion;
			if (kind==RmsRecordKind.IncidentReport) return (await _incidents.GetAsync(DepartmentId,recordId))?.Report.RowVersion;
			return null;
		}
		[HttpGet]
		public async Task<IActionResult> Edit(string recordId, RmsRecordKind kind=RmsRecordKind.Operational)
		{
			return await RenderAsync(new RecordInventoryView { RecordId = recordId, Kind = kind, ErrorMessage = TempData["InventoryMessage"] as string });
		}
		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> Reopen(string recordId, RmsRecordKind kind=RmsRecordKind.Operational) => Edit(recordId, kind);
		private async Task<IActionResult> RenderAsync(RecordInventoryView model, bool preserveVersion = false)
		{
			Response.Headers.CacheControl = "no-store";
			var version = await VersionAsync(model.RecordId, model.Kind); if (!version.HasValue) return NotFound();
			if (!preserveVersion) model.RowVersion = version.Value;
			model.ModernInventory = await IsModernAsync();
			model.ProtectionEnforced = _protection != null && await _protection.IsProtectionEnforcedAsync(DepartmentId);
			model.ProtectedGrant = _grant?.GrantToken;
			model.ProtectedGrantExpiresOnUtc = HttpProtectedGrantContext.ReadExpiry(Request);
			model.ProtectionRedacted = false;
			model.Types = new(); model.Groups = new(); model.Units = new(); model.Locations = new(); model.Lots = new(); model.Assets = new(); model.Usage = new();
			ViewBag.InventoryUnavailable = false;
			try
			{
				if (model.ProtectionEnforced && string.IsNullOrWhiteSpace(model.ProtectedGrant)) throw new InventoryException(403, "ProtectedDataRequired");
				if (model.ModernInventory)
				{
					if (_modernCatalog == null) throw new InventoryException(503, "InventoryUnavailable");
					var items = (await ChoicesAsync<InventoryItem>()).Where(x => !x.IsDeleted && x.IsActive).ToList();
					var names = items.ToDictionary(x => x.Id, x => Details<InventoryItemContent>(x).Name ?? x.Id);
					model.Types = items.Select(x => new SelectListItem { Value = x.Id, Text = names[x.Id] + " (" + Details<InventoryItemContent>(x).UnitOfMeasure + ")" }).ToList();
					model.Locations = (await ChoicesAsync<InventoryLocation>()).Where(x => !x.IsDeleted).Select(x => new SelectListItem { Value = x.Id, Text = Details<InventoryLabel>(x).Name ?? x.Id }).ToList();
					model.Lots = (await ChoicesAsync<InventoryLot>()).Where(x => !x.IsDeleted && names.ContainsKey(x.ItemId)).Select(x => new SelectListItem { Value = x.Id, Text = names[x.ItemId] + " · " + (Details<InventoryLotContent>(x).LotNumber ?? x.Id) }).ToList();
					model.Assets = (await ChoicesAsync<InventoryAsset>()).Where(x => !x.IsDeleted && x.Status == (int)InventoryAssetStatus.InService && names.ContainsKey(x.ItemId)).Select(x => new SelectListItem { Value = x.Id, Text = names[x.ItemId] + " · " + (Details<InventoryAssetContent>(x).SerialNumber ?? x.Id) }).ToList();
				}
				else
				{
					model.Types=(await _inventory.GetAllTypesForDepartmentAsync(DepartmentId)).Select(t=>new SelectListItem {Value=t.InventoryTypeId.ToString(),Text=t.Type+" ("+t.UnitOfMesasure+")"}).ToList();
					model.Groups=(await _groups.GetAllStationGroupsForDepartmentAsync(DepartmentId)).Select(g=>new SelectListItem {Value=g.DepartmentGroupId.ToString(),Text=g.Name}).ToList();
					model.Units=(await _units.GetUnitsForDepartmentAsync(DepartmentId)).Select(u=>new SelectListItem {Value=u.UnitId.ToString(),Text=u.Name}).ToList();
				}
				model.Usage = await _usage.GetUsageForRecordAsync(DepartmentId, model.RecordId);
			}
			catch (InventoryException ex)
			{
				model.Types.Clear(); model.Locations.Clear(); model.Lots.Clear(); model.Assets.Clear(); model.Usage.Clear();
				if (ex.Code == "ProtectedDataRequired")
				{
					model.ProtectionEnforced = true; model.ProtectionRedacted = true; model.ProtectedGrant = null; model.ProtectedGrantExpiresOnUtc = null;
					model.ErrorMessage = _strings?["ProtectedDataRequired"].Value ?? "Verify your identity to view protected inventory.";
				}
				else
				{
					ViewBag.InventoryUnavailable = true;
					model.ErrorMessage = ex.Code == "InventoryChoiceLimitExceeded" ? "There are too many inventory choices to load this form. Ask an administrator to reduce the active inventory selection before recording usage." : UnableToComplete;
				}
			}
			catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException || ex is JsonException)
			{
				ViewBag.InventoryUnavailable = true; model.ErrorMessage = UnableToComplete;
				model.Types.Clear(); model.Locations.Clear(); model.Lots.Clear(); model.Assets.Clear(); model.Usage.Clear();
			}
			if (!(await VersionAsync(model.RecordId, model.Kind)).HasValue) return NotFound();
			return View("Edit", model);
		}
		private async Task<List<T>> ChoicesAsync<T>() where T : InventoryRow
		{
			var choices = new List<T>();
			for (var page = 0; page < 20; page++)
			{
				var result = await _modernCatalog.ListAsync<T>(Actor, page);
				if (result?.Items == null) throw new InventoryException(503, "InventoryUnavailable");
				choices.AddRange(result.Items);
				if (choices.Count > 5000) throw new InventoryException(409, "InventoryChoiceLimitExceeded");
				if (!result.HasMore) return choices;
			}
			throw new InventoryException(409, "InventoryChoiceLimitExceeded");
		}
		private static T Details<T>(InventoryRow row) where T : new() => string.IsNullOrWhiteSpace(row.Content) ? new T() : JsonConvert.DeserializeObject<T>(row.Content) ?? new T();
		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Consume(RecordInventoryView model,CancellationToken cancellationToken)
		{
			Response.Headers.CacheControl = "no-store";
			if (!(await VersionAsync(model.RecordId,model.Kind)).HasValue) return NotFound();
			var modern = await IsModernAsync();
			if (!ModelState.IsValid) { model.ErrorMessage = UnableToComplete; return await RenderAsync(model, true); }
			var saved = false; string message;
			try
			{
				if (modern)
				{
					// The model creates a request ID for a new form only. Network submissions must explicitly carry it.
					if (!Request.HasFormContentType || !Request.Form.TryGetValue(nameof(model.RequestId), out var supplied) || supplied.Count != 1 || !Guid.TryParseExact(supplied[0], "D", out var requestId) || requestId == Guid.Empty || model.RequestId != supplied[0])
						throw new InventoryException(400, "RequestIdRequired");
					await _usage.ConsumeModernAsync(Actor, model.RecordId, model.Kind, model.RowVersion, new InventoryCommand
					{
						RequestId = requestId.ToString("D"),
						Lines = new List<InventoryPosting> { new() { Type = InventoryTransactionType.Consume, ItemId = model.ItemId, FromLocationId = model.LocationId, LotId = model.LotId, AssetId = model.AssetId, Quantity = model.Quantity, Note = model.Note } }
					}, cancellationToken);
				}
				else await _usage.ConsumeAsync(DepartmentId,UserId,model.RecordId,model.Kind,model.RowVersion,model.TypeId,model.GroupId,model.UnitId,model.Quantity,model.Note,cancellationToken,_grant?.GrantToken);
				saved = true;
				try { await CaptureAsync(model.RecordId,model.Kind,cancellationToken); message="Usage saved and supporting evidence captured."; }
				catch(Exception ex) when(ex is InvalidOperationException || ex is ArgumentException || ex is UnauthorizedAccessException || ex is InventoryException) { message="The consumption was saved. Evidence could not be captured; use Refresh evidence after resolving access or draft changes. Do not enter the consumption again."; }
			}
			catch(UnauthorizedAccessException) { return Forbid(); }
			catch(RecordConcurrencyException) { message="The draft changed. Check the recorded usage below before entering another consumption."; }
			catch(InventoryException ex) { message = ex.Code == "IndependentWitnessRequired" ? "Controlled-substance usage requires the independent witness process in Inventory. No consumption was recorded here." : UnableToComplete; }
			catch(Exception ex) when(ex is InvalidOperationException || ex is ArgumentException) { message=UnableToComplete; }
			if (modern || _protection != null && await _protection.IsProtectionEnforcedAsync(DepartmentId))
			{
				if (saved) { ModelState.Clear(); model = new RecordInventoryView { RecordId = model.RecordId, Kind = model.Kind }; }
				model.ErrorMessage = message; return await RenderAsync(model, !saved);
			}
			TempData["InventoryMessage"] = message;
			return RedirectToAction("Edit",new {recordId=model.RecordId,kind=model.Kind});
		}
		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> RefreshEvidence(string recordId,RmsRecordKind kind,CancellationToken cancellationToken)
		{
			Response.Headers.CacheControl = "no-store";
			if (!(await VersionAsync(recordId,kind)).HasValue) return NotFound();
			string message;
			try { await CaptureAsync(recordId,kind,cancellationToken); message="Supporting evidence captured."; }
			catch(UnauthorizedAccessException) { return Forbid(); }
			catch(Exception ex) when(ex is InvalidOperationException || ex is ArgumentException || ex is InventoryException) { message=UnableToComplete; }
			if (await IsModernAsync() || _protection != null && await _protection.IsProtectionEnforcedAsync(DepartmentId))
				return await RenderAsync(new RecordInventoryView { RecordId = recordId, Kind = kind, ErrorMessage = message });
			TempData["InventoryMessage"] = message;
			return RedirectToAction("Edit",new {recordId,kind});
		}
		private Task<RmsEvidenceArtifact> CaptureAsync(string id,RmsRecordKind kind,CancellationToken ct) => _evidence.CaptureAsync(new RecordEvidenceCaptureRequest {DepartmentId=DepartmentId,RecordId=id,RecordKind=kind,Kind=RmsEvidenceKind.InventoryUsage,CapturedByUserId=UserId,CaptureReason="Officer recorded inventory consumption",OriginClient=RmsOriginClient.Web},true,ct);
	}
}
