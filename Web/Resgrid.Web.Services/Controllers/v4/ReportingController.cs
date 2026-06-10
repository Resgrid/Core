using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model.Reporting;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.Reporting;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Reporting and analytics for the caller's department: a composite dashboard, realtime personnel/
	/// unit availability, response-time (NFPA), utilization and participation analytics, and incident
	/// CSV export.
	///
	/// SECURITY: every endpoint is hard-scoped to the authenticated user's claim DepartmentId — there is
	/// deliberately no departmentId parameter, so a client can never request another department's data.
	/// Every endpoint also requires the caller's Reports/View permission (Reports_View policy).
	/// System-wide (cross-department) reporting is intentionally NOT exposed over HTTP; it is available
	/// only to the in-process BackOffice, which resolves IPlatformReportingService directly and calls it
	/// with departmentId = null.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class ReportingController : V4AuthenticatedApiControllerbase
	{
		private const int MaxDayWindow = 366;
		private const int MaxMonthWindowDays = 366 * 5;
		private const int MaxTopN = 50;

		private readonly IPlatformReportingService _reportingService;

		public ReportingController(IPlatformReportingService reportingService)
		{
			_reportingService = reportingService;
		}

		/// <summary>
		/// Composite dashboard for the caller's department: scalar totals, dense (zero-filled, UTC)
		/// time series, top-N breakdowns, and realtime personnel/unit availability.
		/// </summary>
		/// <param name="from">Window start (UTC).</param>
		/// <param name="to">Window end (UTC).</param>
		/// <param name="granularity">Series bucketing: 0 = day, 1 = month.</param>
		/// <param name="topN">Max slices per breakdown before an "Other" bucket (clamped to 1–50).</param>
		[HttpGet("GetDashboard")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Reports_View)]
		public async Task<ActionResult<DashboardReportResult>> GetDashboard(DateTime from, DateTime to,
			int granularity = 0, int topN = 5, CancellationToken cancellationToken = default)
		{
			var gran = granularity == 1 ? ReportGranularity.Month : ReportGranularity.Day;
			topN = Math.Clamp(topN, 1, MaxTopN);
			var (startUtc, endUtc) = NormalizeWindow(from, to, gran == ReportGranularity.Month ? MaxMonthWindowDays : MaxDayWindow);

			var report = await _reportingService.GetDashboardReportAsync(DepartmentId, startUtc, endUtc, gran, topN, false, cancellationToken);
			report.TimeZone = TimeZone;

			var result = new DashboardReportResult { Data = report, PageSize = 1, Status = ResponseHelper.Success };
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>Response-time / NFPA analytics (alarm handling, turnout, travel, total response).</summary>
		[HttpGet("GetResponseTimes")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Reports_View)]
		public async Task<ActionResult<ResponseTimeReportResult>> GetResponseTimes(DateTime from, DateTime to,
			CancellationToken cancellationToken = default)
		{
			var (startUtc, endUtc) = NormalizeWindow(from, to, MaxMonthWindowDays);

			var report = await _reportingService.GetResponseTimeReportAsync(DepartmentId, startUtc, endUtc, false, cancellationToken);

			var result = new ResponseTimeReportResult { Data = report, PageSize = 1, Status = ResponseHelper.Success };
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>Unit Hour Utilization and workload analytics.</summary>
		[HttpGet("GetUtilization")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Reports_View)]
		public async Task<ActionResult<UtilizationReportResult>> GetUtilization(DateTime from, DateTime to,
			CancellationToken cancellationToken = default)
		{
			var (startUtc, endUtc) = NormalizeWindow(from, to, MaxMonthWindowDays);

			var report = await _reportingService.GetUtilizationReportAsync(DepartmentId, startUtc, endUtc, false, cancellationToken);

			var result = new UtilizationReportResult { Data = report, PageSize = 1, Status = ResponseHelper.Success };
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>Personnel participation and certification-compliance analytics.</summary>
		[HttpGet("GetParticipation")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Reports_View)]
		public async Task<ActionResult<ParticipationReportResult>> GetParticipation(DateTime from, DateTime to,
			CancellationToken cancellationToken = default)
		{
			var (startUtc, endUtc) = NormalizeWindow(from, to, MaxMonthWindowDays);

			var report = await _reportingService.GetParticipationReportAsync(DepartmentId, startUtc, endUtc, false, cancellationToken);

			var result = new ParticipationReportResult { Data = report, PageSize = 1, Status = ResponseHelper.Success };
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>
		/// Streams a CSV export of the caller's department incidents for the window using the requested
		/// field mapping (0 = Generic, 1 = NFIRS, 2 = NEMSIS).
		/// </summary>
		[HttpGet("ExportIncidents")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Reports_View)]
		public async Task<IActionResult> ExportIncidents(DateTime from, DateTime to, int profile = 0,
			CancellationToken cancellationToken = default)
		{
			var (startUtc, endUtc) = NormalizeWindow(from, to, MaxMonthWindowDays);
			var exportProfile = Enum.IsDefined(typeof(ExportProfile), profile) ? (ExportProfile)profile : ExportProfile.Generic;

			var stream = await _reportingService.ExportIncidentsCsvAsync(DepartmentId, startUtc, endUtc, exportProfile, cancellationToken);
			var fileName = $"incidents_{DepartmentId}_{startUtc:yyyyMMdd}_{endUtc:yyyyMMdd}_{exportProfile}.csv";
			return File(stream, "text/csv", fileName);
		}

		/// <summary>
		/// Returns the standardized required fields the given export profile cannot fill from Resgrid data
		/// (the gap report). 0 = Generic, 1 = NFIRS, 2 = NEMSIS.
		/// </summary>
		[HttpGet("GetExportGaps")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Reports_View)]
		public ActionResult<ExportGapReportResult> GetExportGaps(int profile = 0)
		{
			var exportProfile = Enum.IsDefined(typeof(ExportProfile), profile) ? (ExportProfile)profile : ExportProfile.Generic;
			var gaps = _reportingService.GetUnmappedRequiredExportFields(exportProfile);

			var result = new ExportGapReportResult { PageSize = gaps.Count, Status = ResponseHelper.Success };
			result.Data.AddRange(gaps);
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		// Normalizes the window to UTC, corrects a reversed range, and clamps the span to bound query cost.
		private static (DateTime startUtc, DateTime endUtc) NormalizeWindow(DateTime from, DateTime to, int maxDays)
		{
			var start = ToUtc(from);
			var end = ToUtc(to);
			if (start > end)
				(start, end) = (end, start);
			if ((end - start).TotalDays > maxDays)
				start = end.AddDays(-maxDays);
			return (start, end);
		}

		private static DateTime ToUtc(DateTime value)
			=> value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(value, DateTimeKind.Utc) : value.ToUniversalTime();
	}
}
