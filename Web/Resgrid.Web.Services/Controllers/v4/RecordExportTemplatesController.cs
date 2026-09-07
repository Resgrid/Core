using System;
using System.Linq;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.Records;
using Resgrid.Web.ServicesCore.Helpers;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Department report exports over v4 (RMS plan section 5.6, RMS-3e): the templates a Workflow step or the hourly
	/// sweep (worker 45) renders for agencies without an API, their runs, and on-demand renders. Authoring needs
	/// ManageRecordReports (checked by the service); every render is an Export audit against each record it contains.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
	[Authorize(Policy = ResgridResources.Record_Export)]
	public class RecordExportTemplatesController : V4AuthenticatedApiControllerbase
	{
		private readonly IRecordsExportService _exports;
		private readonly IRecordsCutoverService _cutoverService;
		private readonly IRecordsAuthorizationService _authorization;

		public RecordExportTemplatesController(IRecordsExportService exports, IRecordsCutoverService cutoverService, IRecordsAuthorizationService authorization)
		{
			_exports = exports;
			_cutoverService = cutoverService;
			_authorization = authorization;
		}

		[HttpGet("List")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<RecordExportTemplatesResult>> List()
		{
			if (!await FlagOnAsync()) return NotFound();
			if (!await CanManageAsync()) return Forbid();
			var result = new RecordExportTemplatesResult { Data = (await _exports.GetTemplatesAsync(DepartmentId)).Select(RecordsRms1bApiMapper.ToTemplate).ToList(), Status = ResponseHelper.Success };
			result.PageSize = result.Data.Count;
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		[HttpGet("Get")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<RecordExportTemplateResult>> Get(string id)
		{
			if (!await FlagOnAsync()) return NotFound();
			if (!await CanManageAsync()) return Forbid();
			var template = await _exports.GetTemplateAsync(DepartmentId, id);
			if (template == null) return NotFound();
			return Ok(Wrap(template));
		}

		/// <summary>Creates (no TemplateId) or updates (TemplateId + RowVersion / If-Match). Narrative or restricted columns need AcknowledgeEgress.</summary>
		[HttpPost("Save")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status409Conflict)]
		public async Task<ActionResult<RecordExportTemplateResult>> Save([FromBody] SaveRecordExportTemplateInput input, CancellationToken cancellationToken)
		{
			if (input == null) return BadRequest();
			var usable = await UsableAsync();
			if (usable != null) return usable;
			var template = RecordsRms1bApiMapper.ToTemplate(input);
			template.RowVersion = RecordsApiContract.ParseETag(Request.Headers[RecordsApiContract.IfMatchHeader]) ?? input.RowVersion;
			try
			{
				var validation = await _exports.ValidateAsync(DepartmentId, UserId, template);
				if (!validation.IsValid) return Problem(statusCode: StatusCodes.Status400BadRequest, title: string.Join(" ", validation.Errors), type: "record_export_validation");
				var saved = await _exports.SaveAsync(DepartmentId, UserId, template, input.AcknowledgeEgress, cancellationToken);
				var result = Wrap(saved);
				result.Warnings = validation.Warnings.ToList();
				return Ok(result);
			}
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpDelete("Delete")]
		[ProducesResponseType(StatusCodes.Status204NoContent)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken)
		{
			var usable = await UsableAsync();
			if (usable != null) return usable;
			try { return await _exports.DeleteAsync(DepartmentId, UserId, id, cancellationToken) ? NoContent() : NotFound(); }
			catch (Exception ex) { return Fail(ex); }
		}

		/// <summary>Renders the template now and stores the run; the file comes from Download. TriggeringRecord scope needs RecordId.</summary>
		[HttpPost("Run")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		public async Task<ActionResult<RecordExportRunResult>> Run(string id, [FromBody] RunRecordExportInput input, CancellationToken cancellationToken)
		{
			var usable = await UsableAsync();
			if (usable != null) return usable;
			var template = await _exports.GetTemplateAsync(DepartmentId, id);
			if (template == null) return NotFound();
			try
			{
				var request = new RecordsExportRequest { Trigger = RmsExportTrigger.Manual, ActingUserId = UserId, Purpose = "API export " + template.Name, RecordId = input?.RecordId?.Trim(), RecordKind = input?.RecordKind.HasValue == true ? (RmsRecordKind?)input.RecordKind.Value : null, WindowStart = input?.WindowStart, WindowEnd = input?.WindowEnd };
				if ((RmsExportScope)template.Scope == RmsExportScope.TriggeringRecord && string.IsNullOrWhiteSpace(request.RecordId))
					return Problem(statusCode: StatusCodes.Status400BadRequest, title: "A TriggeringRecord template needs the RecordId to export.", type: "record_export_validation");
				var run = await _exports.RenderAsync(DepartmentId, template, request, cancellationToken);
				var result = new RecordExportRunResult { Data = RecordsRms1bApiMapper.ToRun(run), Status = ResponseHelper.Created, PageSize = 1 };
				ResponseHelper.PopulateV4ResponseData(result);
				return Ok(result);
			}
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpGet("Runs")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<RecordExportRunsResult>> Runs(string id, int take = 50)
		{
			if (!await FlagOnAsync()) return NotFound();
			if (!await CanManageAsync()) return Forbid();
			var result = new RecordExportRunsResult { Data = (await _exports.GetRunsAsync(DepartmentId, id, Math.Clamp(take, 1, 200))).Select(RecordsRms1bApiMapper.ToRun).ToList(), Status = ResponseHelper.Success };
			result.PageSize = result.Data.Count;
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>The stored file of a run; under ADP the bytes are opened through the seam for this caller.</summary>
		[HttpGet("Download")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<IActionResult> Download(string runId)
		{
			if (!await FlagOnAsync()) return NotFound();
			if (!await CanManageAsync()) return Forbid();
			try
			{
				var run = await _exports.GetRunAsync(DepartmentId, runId, true);
				if (run?.Data == null) return NotFound();
				return File(run.Data, string.IsNullOrWhiteSpace(run.ContentType) ? "application/octet-stream" : run.ContentType, run.FileName ?? "export");
			}
			catch (Exception ex) { return Fail(ex); }
		}

		private RecordExportTemplateResult Wrap(RmsExportTemplate template)
		{
			var result = new RecordExportTemplateResult { Data = RecordsRms1bApiMapper.ToTemplate(template), Status = ResponseHelper.Success, PageSize = 1 };
			ResponseHelper.PopulateV4ResponseData(result);
			Response.Headers[RecordsApiContract.ETagHeader] = result.Data.ETag;
			return result;
		}

		private Task<bool> CanManageAsync() => _authorization.HasPermissionAsync(UserId, DepartmentId, PermissionTypes.ManageRecordReports);

		private ActionResult Fail(Exception ex)
		{
			switch (ex)
			{
				case UnauthorizedAccessException _: return Forbid();
				case RecordProtectedContentException protectedContent: return Problem(statusCode: StatusCodes.Status403Forbidden, title: protectedContent.Message, type: protectedContent.Reason);
				case RecordConcurrencyException conflict: return Problem(statusCode: StatusCodes.Status409Conflict, title: conflict.Message, type: "record_export_conflict");
				case ArgumentException argument: return Problem(statusCode: StatusCodes.Status400BadRequest, title: argument.Message, type: "record_export_validation");
				case InvalidOperationException invalid: return Problem(statusCode: StatusCodes.Status409Conflict, title: invalid.Message, type: "record_export_state");
				default: throw ex;
			}
		}

		private async Task<bool> FlagOnAsync() => (await _cutoverService.GetModuleStateAsync(DepartmentId)).FlagEnabled;

		private async Task<ActionResult> UsableAsync()
		{
			var state = await _cutoverService.GetModuleStateAsync(DepartmentId);
			if (!state.FlagEnabled) return NotFound();
			if (!await CanManageAsync()) return Forbid();
			return state.RecordsUsable ? null : Problem(statusCode: StatusCodes.Status409Conflict, title: "Records is not activated for this department.", type: "records_not_activated");
		}
	}
}
