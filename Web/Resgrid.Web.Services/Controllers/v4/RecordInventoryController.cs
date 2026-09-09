using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Inventories;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;

namespace Resgrid.Web.Services.Controllers.v4
{
	[Route("api/v{VersionId:apiVersion}/[controller]"), ApiVersion("4.0"), ApiExplorerSettings(GroupName="v4")]
	[Authorize(Policy=ResgridResources.Record_Create)]
	public class RecordInventoryController : V4AuthenticatedApiControllerbase
	{
		public class ConsumeInput
		{
			public Resgrid.Model.Inventories.InventoryCommand Command { get; set; }
			public string RecordId {get;set;}
			public RmsRecordKind Kind {get;set;}
			public long ExpectedRowVersion {get;set;}
			public int TypeId {get;set;}
			public int GroupId {get;set;}
			public int? UnitId {get;set;}
			public decimal Quantity {get;set;}
			public string Note {get;set;}
		}
		public class UsageInput
		{
			public string RecordId { get; set; }
			public RmsRecordKind Kind { get; set; }
			public long ExpectedRowVersion { get; set; }
			public RecordInventoryUsageRequest Request { get; set; }
		}
		public class CorrectionInput
		{
			public string RecordId { get; set; }
			public RmsRecordKind Kind { get; set; }
			public long ExpectedRowVersion { get; set; }
			public RecordInventoryUsageCorrection Correction { get; set; }
		}
		private InventoryActor Actor => new() { DepartmentId = DepartmentId, UserId = UserId, GrantToken = Request.Headers[DataProtectionController.GrantHeader].ToString() };
		private readonly IRmsInventoryUsageAdapter _usage;
		private readonly IRecordsEvidenceService _evidence;
		private readonly IRecordsAuthorizationService _auth;
		private readonly IRecordsCutoverService _cutover;
		public RecordInventoryController(IRmsInventoryUsageAdapter usage,IRecordsEvidenceService evidence,IRecordsAuthorizationService auth,IRecordsCutoverService cutover)
		{_usage=usage;_evidence=evidence;_auth=auth;_cutover=cutover;}
		private async Task<bool> Allowed(string id) => (await _cutover.GetModuleStateAsync(DepartmentId)).RecordsUsable && await _auth.CanUserViewRecordAsync(UserId,id,DepartmentId) && await _auth.HasPermissionAsync(UserId,DepartmentId,PermissionTypes.ViewRestrictedRecords) && await _auth.CanUseSourceInventoryAsync(UserId,DepartmentId,null);
		[HttpGet("Usage")]
		public async Task<IActionResult> Usage(string recordId, RmsRecordKind kind = RmsRecordKind.Operational)
		{
			Response.Headers.CacheControl="no-store";
			if (!await Allowed(recordId)) return NotFound();
			try
			{
				var usage = await _usage.GetAuthorizedUsageAsync(Actor, recordId, kind);
				return await Allowed(recordId) ? Ok(usage) : Forbid();
			}
			catch (InventoryException ex) { return InventoryFailure(ex); }
			catch (UnauthorizedAccessException) { return Forbid(); }
			catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException) { return BadRequest(new { error = "Inventory usage could not be read." }); }
		}
		[HttpPost("RecordUsage")]
		public Task<IActionResult> RecordUsage(UsageInput input, CancellationToken cancellationToken) => input == null ? Task.FromResult<IActionResult>(BadRequest())
			: ExecuteUsage(input.RecordId, input.Kind, () => _usage.RecordModernUsageAsync(Actor, input.RecordId, input.Kind, input.ExpectedRowVersion, input.Request, cancellationToken), cancellationToken);
		[HttpPost("ReverseUsage")]
		public Task<IActionResult> ReverseUsage(CorrectionInput input, CancellationToken cancellationToken) => input == null ? Task.FromResult<IActionResult>(BadRequest())
			: ExecuteUsage(input.RecordId, input.Kind, () => _usage.ReverseModernUsageAsync(Actor, input.RecordId, input.Kind, input.ExpectedRowVersion, input.Correction, cancellationToken), cancellationToken);
		private IActionResult InventoryFailure(InventoryException ex) => StatusCode(ex.StatusCode, new { code = ex.Code, error = "Inventory action could not be completed.", type = ex.Code == "ProtectedDataRequired" ? "protected_data_required" : "inventory_error" });
		private async Task<IActionResult> ExecuteUsage<T>(string recordId, RmsRecordKind kind, Func<Task<T>> action, CancellationToken ct)
		{
			Response.Headers.CacheControl = "no-store";
			if (!await Allowed(recordId)) return NotFound();
			try
			{
				var usage = await action(); string evidenceId = null;
				try { evidenceId = (await _evidence.CaptureAsync(new RecordEvidenceCaptureRequest { DepartmentId = DepartmentId, RecordId = recordId, RecordKind = kind, Kind = RmsEvidenceKind.InventoryUsage,
					CapturedByUserId = UserId, CaptureReason = "Officer recorded inventory usage or correction", OriginClient = RmsOriginClient.Api }, true, ct)).RmsEvidenceArtifactId; }
				catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException || ex is UnauthorizedAccessException || ex is InventoryException) { }
				return await Allowed(recordId) ? StatusCode(201, new { usage, evidenceId, evidenceCaptureRequired = evidenceId == null }) : Forbid();
			}
			catch (RecordConcurrencyException) { return Conflict(new { error = "The draft changed. Reload its version and recorded usage before retrying." }); }
			catch (InventoryException ex) { return InventoryFailure(ex); }
			catch (UnauthorizedAccessException) { return Forbid(); }
			catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException) { return BadRequest(new { error = "Inventory usage could not be recorded." }); }
		}
		[HttpPost("Consume")]
		public async Task<IActionResult> Consume(ConsumeInput input,CancellationToken cancellationToken)
		{
			Response.Headers.CacheControl="no-store";
			if (input==null) return BadRequest();
			if (!await Allowed(input.RecordId)) return NotFound();
			try
			{
				var grant = Request.Headers[DataProtectionController.GrantHeader].ToString();
				var usage=input.Command != null
					? await _usage.ConsumeModernAsync(new Resgrid.Model.Inventories.InventoryActor { DepartmentId = DepartmentId, UserId = UserId, GrantToken = grant }, input.RecordId, input.Kind, input.ExpectedRowVersion, input.Command, cancellationToken)
					: await _usage.ConsumeAsync(DepartmentId,UserId,input.RecordId,input.Kind,input.ExpectedRowVersion,input.TypeId,input.GroupId,input.UnitId,input.Quantity,input.Note,cancellationToken,grant);
				string evidenceId=null;
				try { evidenceId=(await _evidence.CaptureAsync(new RecordEvidenceCaptureRequest {DepartmentId=DepartmentId,RecordId=input.RecordId,RecordKind=input.Kind,Kind=RmsEvidenceKind.InventoryUsage,CapturedByUserId=UserId,CaptureReason="Officer recorded inventory consumption",OriginClient=RmsOriginClient.Api},true,cancellationToken)).RmsEvidenceArtifactId; }
				catch(Exception ex) when(ex is InvalidOperationException || ex is ArgumentException || ex is UnauthorizedAccessException || ex is InventoryException) { }
				if (!await Allowed(input.RecordId)) return Forbid();
				return StatusCode(201,new {usage,evidenceId,evidenceCaptureRequired=evidenceId==null});
			}
			catch(RecordConcurrencyException) {return Conflict(new {error="The draft changed. Reload its version and recorded usage before retrying."});}
			catch(Resgrid.Model.Inventories.InventoryException ex) { return StatusCode(ex.StatusCode, new { code=ex.Code, error="Inventory consumption could not be completed.", type=ex.Code=="ProtectedDataRequired" ? "protected_data_required" : "inventory_error" }); }
			catch(UnauthorizedAccessException) {return Forbid();}
			catch(Exception ex) when(ex is InvalidOperationException || ex is ArgumentException) {return BadRequest(new {error="Inventory consumption could not be completed."});}
		}
	}
}
