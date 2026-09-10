using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Localization;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Models.Records;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// RMS-6 records analytics (RMS plan section 6, RMS-6): executive summary, response performance, workload,
	/// accreditation and community risk. The service scopes every figure to what the viewer may open; this
	/// controller only resolves the window and the filter lists.
	/// </summary>
	[Area("User")]
	[Authorize(Policy = ResgridResources.Record_View)]
	public class RecordsAnalyticsController : RecordsPreventionMvcControllerBase
	{
		private const string Flag = FeatureFlagKeys.RecordsAnalytics;

		private readonly IRecordsAnalyticsService _analytics;
		private readonly IRecordDefinitionsService _definitions;
		private readonly IDepartmentGroupsService _groups;

		public RecordsAnalyticsController(IRecordsAnalyticsService analytics, IRecordDefinitionsService definitions, IDepartmentGroupsService groups, IRecordsCutoverService cutover,
			IFeatureToggleService featureToggles, IStringLocalizer<Resgrid.Localization.Areas.User.Records.Records> localizer) : base(cutover, featureToggles, localizer)
		{
			_analytics = analytics;
			_definitions = definitions;
			_groups = groups;
		}

		[HttpGet]
		public Task<IActionResult> Index(string start = null, string end = null, int? stationGroupId = null, string definitionKey = null)
			=> Page(new RecordsExecutiveView { Action = nameof(Index), ShowDefinitionFilter = true }, start, end, stationGroupId, definitionKey, null, null,
				async (m, q, ct) => m.Summary = await _analytics.GetExecutiveSummaryAsync(DepartmentId, UserId, q, ct));

		[HttpGet]
		public Task<IActionResult> ResponsePerformance(string start = null, string end = null, int? stationGroupId = null, string definitionKey = null, int? turnoutTargetSeconds = null, int? travelTargetSeconds = null)
			=> Page(new RecordsResponsePerformanceView { Action = nameof(ResponsePerformance), ShowDefinitionFilter = true, ShowTargets = true }, start, end, stationGroupId, definitionKey, turnoutTargetSeconds, travelTargetSeconds,
				async (m, q, ct) => m.Performance = await _analytics.GetResponsePerformanceAsync(DepartmentId, UserId, q, ct));

		[HttpGet]
		public Task<IActionResult> Workload(string start = null, string end = null, int? stationGroupId = null, string definitionKey = null)
			=> Page(new RecordsWorkloadView { Action = nameof(Workload), ShowDefinitionFilter = true }, start, end, stationGroupId, definitionKey, null, null,
				async (m, q, ct) => m.Workload = await _analytics.GetWorkloadAsync(DepartmentId, UserId, q, ct));

		[HttpGet]
		public Task<IActionResult> Accreditation(string start = null, string end = null, int? stationGroupId = null, int? turnoutTargetSeconds = null, int? travelTargetSeconds = null)
			=> Page(new RecordsAccreditationView { Action = nameof(Accreditation), ShowTargets = true }, start, end, stationGroupId, null, turnoutTargetSeconds, travelTargetSeconds,
				async (m, q, ct) => m.Accreditation = await _analytics.GetAccreditationAsync(DepartmentId, UserId, q, ct));

		[HttpGet]
		public Task<IActionResult> Readiness(string start = null, string end = null, int? stationGroupId = null)
			=> Page(new RecordsReadinessView { Action = nameof(Readiness) }, start, end, stationGroupId, null, null, null,
				async (m, q, ct) => m.Readiness = await _analytics.GetReadinessAsync(DepartmentId, UserId, q, ct));

		[HttpGet]
		public Task<IActionResult> CommunityRisk(string start = null, string end = null, int? stationGroupId = null)
			=> Page(new RecordsCommunityRiskView { Action = nameof(CommunityRisk) }, start, end, stationGroupId, null, null, null,
				async (m, q, ct) => m.Risk = await _analytics.GetCommunityRiskAsync(DepartmentId, UserId, q, ct));

		private async Task<IActionResult> Page<T>(T model, string start, string end, int? stationGroupId, string definitionKey, int? turnoutTarget, int? travelTarget, Func<T, RecordsAnalyticsQuery, CancellationToken, Task> load) where T : RecordsAnalyticsBaseView
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			try
			{
				Prepare(model);
				// Dates arrive as yyyy-MM-dd from the filter form; the end date is inclusive on the page, exclusive in the query.
				var endDay = ParseUtc(end)?.Date ?? DateTime.UtcNow.Date;
				var startDay = ParseUtc(start)?.Date ?? endDay.AddDays(-RecordsAnalyticsLimits.DefaultWindowDays);
				var query = new RecordsAnalyticsQuery
				{
					Start = startDay, End = endDay.AddDays(1), StationGroupId = stationGroupId > 0 ? stationGroupId : null, DefinitionKey = string.IsNullOrWhiteSpace(definitionKey) ? null : definitionKey,
					TurnoutTargetSeconds = Math.Clamp(turnoutTarget ?? 80, 0, 3600), TravelTargetSeconds = Math.Clamp(travelTarget ?? 240, 0, 7200)
				};
				await load(model, query, HttpContext.RequestAborted);
				model.Start = model.Result?.Start ?? query.Start.Value; model.End = (model.Result?.End ?? query.End.Value).AddDays(-1);
				model.StationGroupId = query.StationGroupId; model.DefinitionKey = query.DefinitionKey;
				model.TurnoutTargetSeconds = query.TurnoutTargetSeconds; model.TravelTargetSeconds = query.TravelTargetSeconds;
				await PopulateAsync(model);
				return View(model);
			}
			catch (UnauthorizedAccessException) { return Forbid(); }
			catch (RecordsModuleDisabledException) { return NotFound(); }
		}

		private async Task PopulateAsync(RecordsAnalyticsBaseView model)
		{
			model.Groups = new[] { new SelectListItem { Value = "", Text = Localizer["AnalyticsAllGroups"].Value } }
				.Concat(((await _groups.GetAllGroupsForDepartmentAsync(DepartmentId)) ?? new List<DepartmentGroup>()).OrderBy(g => g.Name).Select(g => new SelectListItem { Value = g.DepartmentGroupId.ToString(), Text = g.Name, Selected = g.DepartmentGroupId == model.StationGroupId }))
				.ToList();
			if (!model.ShowDefinitionFilter) return;
			model.Definitions = new[] { new SelectListItem { Value = "", Text = Localizer["AllDefinitions"].Value } }
				.Concat(((await _definitions.ListAsync(DepartmentId)) ?? new List<RecordDefinitionSummary>()).Select(d => new SelectListItem { Value = d.Key, Text = d.Name, Selected = string.Equals(d.Key, model.DefinitionKey, StringComparison.OrdinalIgnoreCase) }))
				.ToList();
		}
	}
}
