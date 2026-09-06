using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
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
			public string RecordId {get;set;}
			public RmsRecordKind Kind {get;set;}
			public long ExpectedRowVersion {get;set;}
			public int TypeId {get;set;}
			public int GroupId {get;set;}
			public int? UnitId {get;set;}
			public decimal Quantity {get;set;}
			public string Note {get;set;}
		}
		private readonly IRmsInventoryUsageAdapter _usage;
		private readonly IRecordsEvidenceService _evidence;
		private readonly IRecordsAuthorizationService _auth;
		private readonly IRecordsCutoverService _cutover;
		public RecordInventoryController(IRmsInventoryUsageAdapter usage,IRecordsEvidenceService evidence,IRecordsAuthorizationService auth,IRecordsCutoverService cutover)
		{_usage=usage;_evidence=evidence;_auth=auth;_cutover=cutover;}
		private async Task<bool> Allowed(string id) => (await _cutover.GetModuleStateAsync(DepartmentId)).RecordsUsable && await _auth.CanUserViewRecordAsync(UserId,id,DepartmentId) && await _auth.HasPermissionAsync(UserId,DepartmentId,PermissionTypes.ViewRestrictedRecords) && await _auth.CanUseSourceInventoryAsync(UserId,DepartmentId,null);
		[HttpGet("Usage")]
		public async Task<IActionResult> Usage(string recordId)
		{
			Response.Headers.CacheControl="no-store";
			if (!await Allowed(recordId)) return NotFound();
			var usage=await _usage.GetUsageForRecordAsync(DepartmentId,recordId);
			return await Allowed(recordId) ? Ok(usage) : Forbid();
		}
		[HttpPost("Consume")]
		public async Task<IActionResult> Consume(ConsumeInput input,CancellationToken cancellationToken)
		{
			Response.Headers.CacheControl="no-store";
			if (input==null) return BadRequest();
			if (!await Allowed(input.RecordId)) return NotFound();
			try
			{
				var usage=await _usage.ConsumeAsync(DepartmentId,UserId,input.RecordId,input.Kind,input.ExpectedRowVersion,input.TypeId,input.GroupId,input.UnitId,input.Quantity,input.Note,cancellationToken);
				string evidenceId=null;
				try { evidenceId=(await _evidence.CaptureAsync(new RecordEvidenceCaptureRequest {DepartmentId=DepartmentId,RecordId=input.RecordId,RecordKind=input.Kind,Kind=RmsEvidenceKind.InventoryUsage,CapturedByUserId=UserId,CaptureReason="Officer recorded inventory consumption",OriginClient=RmsOriginClient.Api},true,cancellationToken)).RmsEvidenceArtifactId; }
				catch(Exception ex) when(ex is InvalidOperationException || ex is ArgumentException || ex is UnauthorizedAccessException) { }
				if (!await Allowed(input.RecordId)) return Forbid();
				return StatusCode(201,new {usage,evidenceId,evidenceCaptureRequired=evidenceId==null});
			}
			catch(RecordConcurrencyException) {return Conflict(new {error="The draft changed. Reload its version and recorded usage before retrying."});}
			catch(UnauthorizedAccessException) {return Forbid();}
			catch(Exception ex) when(ex is InvalidOperationException || ex is ArgumentException) {return BadRequest(new {error=ex.Message});}
		}
	}
}
