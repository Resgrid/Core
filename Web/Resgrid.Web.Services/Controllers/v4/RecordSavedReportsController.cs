using System;
using System.Linq;
using System.Net.Mime;
using System.Text;
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
	/// Department saved reports over v4 (RMS plan section 4.1, RMS-1B): allowlisted typed columns, bounded filters,
	/// one group-by and count/sum/avg/min/max. Managing needs RecordReport_Update; running needs Record_View and
	/// honors the runner's group scope and restricted permission.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
	public class RecordSavedReportsController : V4AuthenticatedApiControllerbase
	{
		private readonly IRecordSavedReportsService _reports;
		private readonly IRecordsCutoverService _cutoverService;

		public RecordSavedReportsController(IRecordSavedReportsService reports, IRecordsCutoverService cutoverService)
		{
			_reports = reports;
			_cutoverService = cutoverService;
		}

		[HttpGet("List")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<ActionResult<RecordSavedReportsResult>> List()
		{
			if (!await FlagOnAsync()) return NotFound();
			var result = new RecordSavedReportsResult { Data = (await _reports.GetForDepartmentAsync(DepartmentId)).Select(RecordsRms1bApiMapper.ToReport).ToList(), Status = ResponseHelper.Success };
			result.PageSize = result.Data.Count;
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		[HttpGet("Get")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<ActionResult<RecordSavedReportResult>> Get(string id)
		{
			if (!await FlagOnAsync()) return NotFound();
			var report = await _reports.GetAsync(DepartmentId, id);
			if (report == null) return NotFound();
			return Ok(Wrap(report));
		}

		[HttpPost("Validate")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.RecordReport_Update)]
		public async Task<ActionResult<RecordReportValidationResult>> Validate([FromBody] SaveRecordSavedReportInput input)
		{
			if (input == null) return BadRequest();
			if (!await FlagOnAsync()) return NotFound();
			var result = new RecordReportValidationResult { Data = await _reports.ValidateAsync(DepartmentId, RecordsRms1bApiMapper.ToReport(input)), Status = ResponseHelper.Success, PageSize = 1 };
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>Creates (no ReportId) or updates (ReportId + RowVersion / If-Match) a saved report.</summary>
		[HttpPost("Save")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status409Conflict)]
		[Authorize(Policy = ResgridResources.RecordReport_Update)]
		public async Task<ActionResult<RecordSavedReportResult>> Save([FromBody] SaveRecordSavedReportInput input, CancellationToken cancellationToken)
		{
			if (input == null) return BadRequest();
			var usable = await UsableAsync();
			if (usable != null) return usable;
			var report = RecordsRms1bApiMapper.ToReport(input);
			report.RowVersion = RecordsApiContract.ParseETag(Request.Headers[RecordsApiContract.IfMatchHeader]) ?? input.RowVersion;
			try { return Ok(Wrap(await _reports.SaveAsync(DepartmentId, UserId, report, cancellationToken))); }
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpDelete("Delete")]
		[ProducesResponseType(StatusCodes.Status204NoContent)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Authorize(Policy = ResgridResources.RecordReport_Update)]
		public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken)
		{
			var usable = await UsableAsync();
			if (usable != null) return usable;
			try { return await _reports.DeleteAsync(DepartmentId, UserId, id, cancellationToken) ? NoContent() : NotFound(); }
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpPost("Run")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<ActionResult<RecordReportRunResult>> Run(string id, CancellationToken cancellationToken)
		{
			if (!await FlagOnAsync()) return NotFound();
			try
			{
				var result = new RecordReportRunResult { Data = await _reports.RunAsync(DepartmentId, UserId, id, cancellationToken), Status = ResponseHelper.Success, PageSize = 1 };
				ResponseHelper.PopulateV4ResponseData(result);
				return Ok(result);
			}
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpGet("RunCsv")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<IActionResult> RunCsv(string id, CancellationToken cancellationToken)
		{
			if (!await FlagOnAsync()) return NotFound();
			try
			{
				var run = await _reports.RunAsync(DepartmentId, UserId, id, cancellationToken);
				return File(Encoding.UTF8.GetBytes(_reports.ToCsv(run)), "text/csv", (run.Name ?? "report").Replace(' ', '-') + ".csv");
			}
			catch (Exception ex) { return Fail(ex); }
		}

		private RecordSavedReportResult Wrap(RmsSavedReportDefinition report)
		{
			var result = new RecordSavedReportResult { Data = RecordsRms1bApiMapper.ToReport(report), Status = ResponseHelper.Success, PageSize = 1 };
			ResponseHelper.PopulateV4ResponseData(result);
			Response.Headers[RecordsApiContract.ETagHeader] = result.Data.ETag;
			return result;
		}

		private ActionResult Fail(Exception ex)
		{
			switch (ex)
			{
				case UnauthorizedAccessException _: return Forbid();
				case RecordConcurrencyException conflict: return Problem(statusCode: StatusCodes.Status409Conflict, title: conflict.Message, type: "record_report_conflict");
				case ArgumentException argument: return Problem(statusCode: StatusCodes.Status400BadRequest, title: argument.Message, type: "record_report_validation");
				case InvalidOperationException invalid: return Problem(statusCode: StatusCodes.Status409Conflict, title: invalid.Message, type: "record_report_state");
				default: throw ex;
			}
		}

		private async Task<bool> FlagOnAsync() => (await _cutoverService.GetModuleStateAsync(DepartmentId)).FlagEnabled;

		private async Task<ActionResult> UsableAsync()
		{
			var state = await _cutoverService.GetModuleStateAsync(DepartmentId);
			if (!state.FlagEnabled) return NotFound();
			return state.RecordsUsable ? null : Problem(statusCode: StatusCodes.Status409Conflict, title: "Records is not activated for this department.", type: "records_not_activated");
		}
	}
}
