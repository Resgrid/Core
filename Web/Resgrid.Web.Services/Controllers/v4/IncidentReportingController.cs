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

		public IncidentReportingController(IIncidentReportingService incidentReportingService)
		{
			_incidentReportingService = incidentReportingService;
		}
		#endregion Members and Constructors

		/// <summary>Gets the ICS-201/209-style incident status summary.</summary>
		[HttpGet("GetIncidentSummary/{callId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_View)]
		public async Task<ActionResult<ICModels.IncidentReportSummaryResult>> GetIncidentSummary(int callId)
		{
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
			var csv = await _incidentReportingService.ExportTimelineCsvAsync(DepartmentId, callId);
			var bytes = Encoding.UTF8.GetBytes(csv);
			return File(bytes, "text/csv", $"incident-{callId}-timeline.csv");
		}
	}
}
