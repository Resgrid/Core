using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Models.v4.Records;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// RMS-6 records analytics (RMS plan section 6, RMS-6): response-performance, workload, executive, accreditation and
	/// community-risk dashboards over finalized Records. Member principals with Record_View only; a group-scoped member
	/// gets figures over the Records they can open, a department administrator the department (plan section 11, q. 38).
	/// The window is [start, end) in UTC, defaulting to the last 90 days and clamped to 366; a truncated result says so.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
	[Authorize(Policy = ResgridResources.Record_View)]
	public class RecordAnalyticsController : RecordsPreventionApiControllerBase
	{
		private readonly IRecordsAnalyticsService _analytics;

		public RecordAnalyticsController(IRecordsAnalyticsService analytics, IRecordsCutoverService cutover) : base(cutover)
		{
			_analytics = analytics;
		}

		// The targets are clamped to the same bounds the web filter form enforces, so an out-of-range value from an API
		// caller cannot skew the within-target percentages the dashboards report.
		private static RecordsAnalyticsQuery Query(DateTime? start, DateTime? end, int? stationGroupId, string definitionKey, int turnoutTargetSeconds, int travelTargetSeconds)
			=> new RecordsAnalyticsQuery
			{
				Start = start, End = end, StationGroupId = stationGroupId, DefinitionKey = definitionKey,
				TurnoutTargetSeconds = Math.Clamp(turnoutTargetSeconds, 0, 3600), TravelTargetSeconds = Math.Clamp(travelTargetSeconds, 0, 7200)
			};

		/// <summary>Turnout, travel, total response, time on scene and first arrival; by unit, station group, hour of day and month.</summary>
		[HttpGet("ResponsePerformance")]
		public async Task<ActionResult<RecordsResponsePerformanceResult>> ResponsePerformance(DateTime? start = null, DateTime? end = null, int? stationGroupId = null, string definitionKey = null, int turnoutTargetSeconds = 80, int travelTargetSeconds = 240, CancellationToken cancellationToken = default)
		{
			if (!await FlagOnAsync()) return NotFound();
			try { return Ok(Done(new RecordsResponsePerformanceResult { Data = await _analytics.GetResponsePerformanceAsync(DepartmentId, UserId, Query(start, end, stationGroupId, definitionKey, turnoutTargetSeconds, travelTargetSeconds), cancellationToken), PageSize = 1 })); }
			catch (Exception ex) { return Fail(ex); }
		}

		/// <summary>Records, hours and unit utilization by definition, month, author, station group, unit and person, plus the weekday/hour heat map.</summary>
		[HttpGet("Workload")]
		public async Task<ActionResult<RecordsWorkloadResult>> Workload(DateTime? start = null, DateTime? end = null, int? stationGroupId = null, string definitionKey = null, CancellationToken cancellationToken = default)
		{
			if (!await FlagOnAsync()) return NotFound();
			try { return Ok(Done(new RecordsWorkloadResult { Data = await _analytics.GetWorkloadAsync(DepartmentId, UserId, Query(start, end, stationGroupId, definitionKey, 80, 240), cancellationToken), PageSize = 1 })); }
			catch (Exception ex) { return Fail(ex); }
		}

		/// <summary>Headline figures against the preceding window of equal length, lifecycle medians, return and acceptance rates, queues and overdue obligations.</summary>
		[HttpGet("Executive")]
		public async Task<ActionResult<RecordsExecutiveSummaryResult>> Executive(DateTime? start = null, DateTime? end = null, int? stationGroupId = null, string definitionKey = null, CancellationToken cancellationToken = default)
		{
			if (!await FlagOnAsync()) return NotFound();
			try { return Ok(Done(new RecordsExecutiveSummaryResult { Data = await _analytics.GetExecutiveSummaryAsync(DepartmentId, UserId, Query(start, end, stationGroupId, definitionKey, 80, 240), cancellationToken), PageSize = 1 })); }
			catch (Exception ex) { return Fail(ex); }
		}

		/// <summary>Inspection, violation, permit, hydrant, occupancy, training and response evidence; prevention sections only when their module is on.</summary>
		[HttpGet("Accreditation")]
		public async Task<ActionResult<RecordsAccreditationResult>> Accreditation(DateTime? start = null, DateTime? end = null, int? stationGroupId = null, int turnoutTargetSeconds = 80, int travelTargetSeconds = 240, CancellationToken cancellationToken = default)
		{
			if (!await FlagOnAsync()) return NotFound();
			try { return Ok(Done(new RecordsAccreditationResult { Data = await _analytics.GetAccreditationAsync(DepartmentId, UserId, Query(start, end, stationGroupId, null, turnoutTargetSeconds, travelTargetSeconds), cancellationToken), PageSize = 1 })); }
			catch (Exception ex) { return Fail(ex); }
		}

		/// <summary>Apparatus and equipment readiness composed from the checklists, maintenance and inventory modules (each authorizes the caller itself) plus readiness-packet evidence; the unit board joins them on the unit id.</summary>
		[HttpGet("Readiness")]
		public async Task<ActionResult<RecordsReadinessResult>> Readiness(DateTime? start = null, DateTime? end = null, int? stationGroupId = null, CancellationToken cancellationToken = default)
		{
			if (!await FlagOnAsync()) return NotFound();
			try { return Ok(Done(new RecordsReadinessResult { Data = await _analytics.GetReadinessAsync(DepartmentId, UserId, Query(start, end, stationGroupId, null, 80, 240), cancellationToken), PageSize = 1 })); }
			catch (Exception ex) { return Fail(ex); }
		}

		/// <summary>Incident mix, occupancy risk profile, open violations, hydrants and CRR activity; prevention sections only when their module is on.</summary>
		[HttpGet("CommunityRisk")]
		public async Task<ActionResult<RecordsCommunityRiskResult>> CommunityRisk(DateTime? start = null, DateTime? end = null, int? stationGroupId = null, CancellationToken cancellationToken = default)
		{
			if (!await FlagOnAsync()) return NotFound();
			try { return Ok(Done(new RecordsCommunityRiskResult { Data = await _analytics.GetCommunityRiskAsync(DepartmentId, UserId, Query(start, end, stationGroupId, null, 80, 240), cancellationToken), PageSize = 1 })); }
			catch (Exception ex) { return Fail(ex); }
		}
	}
}
