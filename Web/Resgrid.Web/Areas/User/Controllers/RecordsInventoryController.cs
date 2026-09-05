using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Models.Records;

namespace Resgrid.Web.Areas.User.Controllers
{
	[Area("User")]
	[Authorize(Policy = ResgridResources.Record_Create)]
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
		public RecordsInventoryController(IRmsInventoryUsageAdapter usage, IRecordsEvidenceService evidence, IRecordsAuthorizationService auth, IRecordsCutoverService cutover,
			IRecordsService records, IIncidentReportsService incidents, IInventoryService inventory, IDepartmentGroupsService groups, IUnitsService units)
		{ _usage=usage; _evidence=evidence; _auth=auth; _cutover=cutover; _records=records; _incidents=incidents; _inventory=inventory; _groups=groups; _units=units; }
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
			Response.Headers.CacheControl="no-store";
			var version=await VersionAsync(recordId,kind); if(!version.HasValue) return NotFound();
			var model=new RecordInventoryView {RecordId=recordId,Kind=kind,RowVersion=version.Value,ErrorMessage=TempData["InventoryMessage"] as string,Usage=await _usage.GetUsageForRecordAsync(DepartmentId,recordId)};
			model.Types=(await _inventory.GetAllTypesForDepartmentAsync(DepartmentId)).Select(t=>new SelectListItem {Value=t.InventoryTypeId.ToString(),Text=t.Type+" ("+t.UnitOfMesasure+")"}).ToList();
			model.Groups=(await _groups.GetAllStationGroupsForDepartmentAsync(DepartmentId)).Select(g=>new SelectListItem {Value=g.DepartmentGroupId.ToString(),Text=g.Name}).ToList();
			model.Units=(await _units.GetUnitsForDepartmentAsync(DepartmentId)).Select(u=>new SelectListItem {Value=u.UnitId.ToString(),Text=u.Name}).ToList();
			if (!(await VersionAsync(recordId,kind)).HasValue) return NotFound();
			return View(model);
		}
		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Consume(RecordInventoryView model,CancellationToken cancellationToken)
		{
			if (!(await VersionAsync(model.RecordId,model.Kind)).HasValue) return NotFound();
			try
			{
				await _usage.ConsumeAsync(DepartmentId,UserId,model.RecordId,model.Kind,model.RowVersion,model.TypeId,model.GroupId,model.UnitId,model.Quantity,model.Note,cancellationToken);
				try { await CaptureAsync(model.RecordId,model.Kind,cancellationToken); TempData["InventoryMessage"]="Usage saved and supporting evidence captured."; }
				catch(Exception ex) when(ex is InvalidOperationException || ex is ArgumentException || ex is UnauthorizedAccessException) { TempData["InventoryMessage"]="The consumption was saved. Evidence could not be captured; use Refresh evidence after resolving access or draft changes. Do not enter the consumption again."; }
			}
			catch(UnauthorizedAccessException) { return Forbid(); }
			catch(RecordConcurrencyException) { TempData["InventoryMessage"]="The draft changed. Check the recorded usage below before entering another consumption."; }
			catch(Exception ex) when(ex is InvalidOperationException || ex is ArgumentException) { TempData["InventoryMessage"]=ex.Message; }
			return RedirectToAction("Edit",new {recordId=model.RecordId,kind=model.Kind});
		}
		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> RefreshEvidence(string recordId,RmsRecordKind kind,CancellationToken cancellationToken)
		{
			if (!(await VersionAsync(recordId,kind)).HasValue) return NotFound();
			try { await CaptureAsync(recordId,kind,cancellationToken); TempData["InventoryMessage"]="Supporting evidence captured."; }
			catch(UnauthorizedAccessException) { return Forbid(); }
			catch(Exception ex) when(ex is InvalidOperationException || ex is ArgumentException) { TempData["InventoryMessage"]=ex.Message; }
			return RedirectToAction("Edit",new {recordId,kind});
		}
		private Task<RmsEvidenceArtifact> CaptureAsync(string id,RmsRecordKind kind,CancellationToken ct) => _evidence.CaptureAsync(new RecordEvidenceCaptureRequest {DepartmentId=DepartmentId,RecordId=id,RecordKind=kind,Kind=RmsEvidenceKind.InventoryUsage,CapturedByUserId=UserId,CaptureReason="Officer recorded inventory consumption",OriginClient=RmsOriginClient.Web},true,ct);
	}
}
