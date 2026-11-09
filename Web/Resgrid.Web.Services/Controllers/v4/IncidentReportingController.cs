using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Helpers;
using System.Text;
using System.Threading.Tasks;
using ICModels = Resgrid.Web.Services.Models.v4.IncidentCommand;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Per-incident reporting & analytics (§3.13): incident status summary (ICS-201/209), after-action bundle,
	/// and timeline export.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class IncidentReportingController : V4AuthenticatedApiControllerbase
	{
		#region Members and Constructors
		private readonly IIncidentReportingService _incidentReportingService;
		private readonly ICommandAccessService _commandAccessService;
		private readonly IIncidentCommandService _incidentCommandService;

		public IncidentReportingController(IIncidentReportingService incidentReportingService, ICommandAccessService commandAccessService, IIncidentCommandService incidentCommandService)
		{
			_incidentReportingService = incidentReportingService;
			_commandAccessService = commandAccessService;
			_incidentCommandService = incidentCommandService;
		}

		/// <summary>Whether the caller may read this incident's board: Security &gt; View Command Boards, or being on the incident.</summary>
		private Task<bool> CanViewBoardAsync(int callId) => _incidentCommandService.CanViewBoardAsync(DepartmentId, callId, UserId);

		/// <summary>A refusal the apps treat as final (403), never as an expired session (401).</summary>
		private ObjectResult Refused() => StatusCode(StatusCodes.Status403Forbidden, "You are not authorized for this incident command action.");
		#endregion Members and Constructors

		/// <summary>Gets the ICS-201/209-style incident status summary.</summary>
		[HttpGet("GetIncidentSummary/{callId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_View)]
		public async Task<ActionResult<ICModels.IncidentReportSummaryResult>> GetIncidentSummary(int callId)
		{
			if (!await CanViewBoardAsync(callId))
				return Refused();

			var result = new ICModels.IncidentReportSummaryResult();
			var summary = await _incidentReportingService.GetIncidentSummaryAsync(DepartmentId, callId);

			if (summary == null)
			{
				result.Status = ResponseHelper.NotFound;
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}

			result.Data = summary;
			result.PageSize = 1;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>Gets the complete after-action report bundle for an incident.</summary>
		[HttpGet("GetAfterActionReport/{callId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_View)]
		public async Task<ActionResult<ICModels.IncidentAfterActionReportResult>> GetAfterActionReport(int callId)
		{
			if (!await CanViewBoardAsync(callId))
				return Refused();

			var result = new ICModels.IncidentAfterActionReportResult();
			var report = await _incidentReportingService.GetAfterActionReportAsync(DepartmentId, callId);

			if (report == null)
			{
				result.Status = ResponseHelper.NotFound;
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}

			result.Data = report;
			result.PageSize = 1;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>Exports the incident command timeline as a CSV file.</summary>
		[HttpGet("ExportIncident/{callId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_View)]
		public async Task<IActionResult> ExportIncident(int callId)
		{
			if (!await CanViewBoardAsync(callId))
				return Refused();

			var csv = await _incidentReportingService.ExportTimelineCsvAsync(DepartmentId, callId);
			var bytes = Encoding.UTF8.GetBytes(csv);
			return File(bytes, "text/csv", $"incident-{callId}-timeline.csv");
		}

		/// <summary>Gets the NFIRS/NERIS-oriented key-times report (alarm, command, benchmarks, resource counts).</summary>
		[HttpGet("GetIncidentTimes/{callId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_View)]
		public async Task<ActionResult<ICModels.IncidentTimesReportResult>> GetIncidentTimes(int callId)
		{
			if (!await CanViewBoardAsync(callId))
				return Refused();

			var result = new ICModels.IncidentTimesReportResult();
			var report = await _incidentReportingService.GetIncidentTimesReportAsync(DepartmentId, callId);

			if (report == null)
			{
				result.Status = ResponseHelper.NotFound;
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}

			result.Data = report;
			result.PageSize = 1;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>Gets per-resource lane utilization (which lanes each resource worked and for how long).</summary>
		[HttpGet("GetResourceUtilization/{callId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_View)]
		public async Task<ActionResult<ICModels.ResourceUtilizationReportResult>> GetResourceUtilization(int callId)
		{
			if (!await CanViewBoardAsync(callId))
				return Refused();

			var result = new ICModels.ResourceUtilizationReportResult();
			var report = await _incidentReportingService.GetResourceUtilizationReportAsync(DepartmentId, callId);

			if (report == null)
			{
				result.Status = ResponseHelper.NotFound;
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}

			result.Data = report;
			result.PageSize = 1;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>Exports the full after-action report (summary, times, lanes, utilization, timeline) as CSV.</summary>
		[HttpGet("ExportAfterAction/{callId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_View)]
		public async Task<IActionResult> ExportAfterAction(int callId)
		{
			if (!await CanViewBoardAsync(callId))
				return Refused();

			var csv = await _incidentReportingService.ExportAfterActionCsvAsync(DepartmentId, callId);
			if (string.IsNullOrEmpty(csv))
				return NotFound();

			var bytes = Encoding.UTF8.GetBytes(csv);
			return File(bytes, "text/csv", $"incident-{callId}-after-action.csv");
		}
	}
}
