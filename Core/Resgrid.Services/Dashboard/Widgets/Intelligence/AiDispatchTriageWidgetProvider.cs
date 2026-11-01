using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.AiDispatch;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Dashboard.Widgets.Intelligence;
using Resgrid.Model.Services;

namespace Resgrid.Services.Dashboard.Widgets.Intelligence
{
	/// <summary>
	/// aiDispatch.triage (plan §7.1): AI dispatch enrichments on calls that are still active (IAiDispatchAdminService
	/// .GetTriageSummaryAsync — outcome counts plus one joined read). Group-scoped dispatch applies as on the dispatch board: when the
	/// viewer is not department-wide, only calls in their dispatch scope are listed and counted as needing review.
	/// </summary>
	[DashboardWidget("aiDispatch.triage")]
	public class AiDispatchTriageWidgetProvider : IDashboardWidgetDataProvider
	{
		private const int ReadLimit = 50;

		private readonly IAiDispatchAdminService _aiDispatchAdminService;
		private readonly IDispatchScopeService _dispatchScopeService;
		private readonly ICallsService _callsService;

		public AiDispatchTriageWidgetProvider(IAiDispatchAdminService aiDispatchAdminService, IDispatchScopeService dispatchScopeService, ICallsService callsService)
		{
			_aiDispatchAdminService = aiDispatchAdminService;
			_dispatchScopeService = dispatchScopeService;
			_callsService = callsService;
		}

		public async Task<object> GetDataAsync(DashboardWidgetContext context, CancellationToken cancellationToken)
		{
			var departmentId = context.DepartmentId;
			var hours = context.GetInt("hours", 24);
			var max = context.GetInt("maxRows", 5);

			var summary = await _aiDispatchAdminService.GetTriageSummaryAsync(departmentId, hours, ReadLimit, cancellationToken) ?? new AiDispatchTriageSummary();
			var items = (summary.ActiveCalls ?? new List<AiDispatchTriageItem>()).Where(i => i.CallId > 0).ToList();

			var scope = await _dispatchScopeService.GetScopeForUserAsync(departmentId, context.UserId);
			if (scope != null && !scope.IsDepartmentWide && items.Count > 0)
			{
				var active = (await _callsService.GetActiveCallsByDepartmentAsync(departmentId) ?? new List<Call>()).Where(c => c.DepartmentId == departmentId).ToList();
				var visible = new HashSet<int>((await _dispatchScopeService.FilterCallsAsync(scope, active) ?? new List<Call>()).Select(c => c.CallId));
				items = items.Where(i => visible.Contains(i.CallId)).ToList();
			}

			var counts = summary.OutcomeCounts ?? new Dictionary<string, int>();
			return new AiDispatchTriageWidgetData
			{
				Hours = hours,
				NeedsReview = items.Count(i => i.NeedsReview),
				InProgress = counts.TryGetValue(AiDispatchOutcomes.InProgress, out var inProgress) ? inProgress : 0,
				Applied = counts.TryGetValue(AiDispatchOutcomes.Applied, out var applied) ? applied : 0,
				Total = counts.Values.Sum(),
				Rows = items.OrderByDescending(i => i.NeedsReview).ThenByDescending(i => i.CreatedOnUtc).Take(max).Select(i => new AiDispatchTriageRow
				{
					CallId = i.CallId,
					CallNumber = i.CallNumber,
					Outcome = i.Outcome,
					ConfidencePercent = i.Confidence.HasValue ? (int)Math.Round(i.Confidence.Value * 100m) : null,
					RelatedCallId = i.RelatedCallId,
					RelatedCallNumber = i.RelatedCallNumber,
					CreatedOnUtc = i.CreatedOnUtc,
					NeedsReview = i.NeedsReview
				}).ToList()
			};
		}
	}
}
