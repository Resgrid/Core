using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;

namespace Resgrid.Web.Services.Controllers.v4
{
	[Route("api/v{VersionId:apiVersion}/[controller]"), ApiVersion("4.0"), ApiExplorerSettings(GroupName="v4")]
	[Authorize(Policy=ResgridResources.Record_View)]
	public class RecordCustomFieldsController : V4AuthenticatedApiControllerbase
	{
		private readonly IRecordsUdfService _udf;
		private readonly IRecordsService _records;
		private readonly IIncidentReportsService _incidents;
		private readonly IRecordsDocumentService _documents;
		private readonly IRecordsAuthorizationService _auth;
		private readonly IRecordsCutoverService _cutover;
		public RecordCustomFieldsController(IRecordsUdfService udf, IRecordsService records, IIncidentReportsService incidents, IRecordsDocumentService documents, IRecordsAuthorizationService auth, IRecordsCutoverService cutover)
		{ _udf=udf; _records=records; _incidents=incidents; _documents=documents; _auth=auth; _cutover=cutover; }
		[HttpGet("GetForm")]
		public async Task<IActionResult> GetForm(string recordId, RmsRecordKind kind=RmsRecordKind.Operational, string revisionId=null, bool mobile=false)
		{
			if (!(await _cutover.GetModuleStateAsync(DepartmentId)).FlagEnabled) return NotFound();
			if (kind!=RmsRecordKind.Operational && kind!=RmsRecordKind.IncidentReport) return BadRequest();
			try
			{
				if (!await _auth.CanUserViewRecordAsync(UserId,recordId,DepartmentId)) return Forbid();
				RecordUdfSection section;
				if (revisionId!=null)
				{
					var doc=await _documents.GetAsync(DepartmentId,UserId,recordId,kind,revisionId);
					if (doc==null) return NotFound();
					section=(Newtonsoft.Json.Linq.JObject.Parse(doc.ContentJson)["CustomFields"] as Newtonsoft.Json.Linq.JObject)?.ToObject<RecordUdfSection>();
				}
				else if (kind==RmsRecordKind.Operational)
				{
					var record=await _records.GetAsync(DepartmentId,recordId); if(record==null) return NotFound(); section=record.CustomFields;
				}
				else
				{
					var record=await _incidents.GetAsync(DepartmentId,recordId); if(record==null) return NotFound(); section=record.CustomFields;
				}
				section=await _udf.ProjectAsync(DepartmentId,UserId,section,mobile);
				if (!await _auth.CanUserViewRecordAsync(UserId,recordId,DepartmentId)) return Forbid();
				Response.Headers["Cache-Control"]="no-store"; return Ok(section);
			}
			catch(UnauthorizedAccessException) {return Forbid();}
		}
	}
}
