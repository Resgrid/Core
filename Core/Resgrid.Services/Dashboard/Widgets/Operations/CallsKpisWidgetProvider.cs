using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Dashboard.Widgets.Operations;
using Resgrid.Model.Reporting;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.Dashboard.Widgets.Operations
{
	/// <summary>
	/// calls.kpis (plan §7.1). Active, pending and scheduled are the Dispatch lists' counts (GetWaitingCallCounts), trimmed
	/// by the viewer's dispatch scope; awaiting resources is the top-bar badge's count (dispatchers with conditional run
	/// cards on). Today and the 7-day series come from one grouped reporting query (calls logged per UTC day).
	/// </summary>
	[DashboardWidget("calls.kpis")]
	public class CallsKpisWidgetProvider : IDashboardWidgetDataProvider
	{
		private readonly ICallsService _callsService;
		private readonly IDispatchScopeService _dispatchScopeService;
		private readonly IReportingRepository _reportingRepository;
		private readonly IOutstandingRequirementsService _outstandingRequirementsService;
		private readonly IFeatureToggleService _featureToggleService;

		public CallsKpisWidgetProvider(ICallsService callsService, IDispatchScopeService dispatchScopeService, IReportingRepository reportingRepository,
			IOutstandingRequirementsService outstandingRequirementsService, IFeatureToggleService featureToggleService)
		{
			_callsService = callsService;
			_dispatchScopeService = dispatchScopeService;
			_reportingRepository = reportingRepository;
			_outstandingRequirementsService = outstandingRequirementsService;
			_featureToggleService = featureToggleService;
		}

		public async Task<object> GetDataAsync(DashboardWidgetContext context, CancellationToken cancellationToken)
		{
			var departmentId = context.DepartmentId;
			var now = context.NowUtc;
			var data = new CallsKpisWidgetData();

			var scope = await _dispatchScopeService.GetScopeForUserAsync(departmentId, context.UserId);

			var active = OfDepartment(await _callsService.GetActiveCallsByDepartmentAsync(departmentId), departmentId);
			var scopedActive = await FilterAsync(scope, active.Where(c => DashboardCallReads.IsDispatched(c, now)).ToList());
			data.ActiveCount = scopedActive.Count;

			data.PendingCount = (await FilterAsync(scope, OfDepartment(await _callsService.GetPendingCallsByDepartmentIdAsync(departmentId), departmentId))).Count;
			data.ScheduledCount = (await FilterAsync(scope, OfDepartment(await _callsService.GetAllNonDispatchedScheduledCallsByDepartmentIdAsync(departmentId), departmentId))).Count;

			var today = now.Date;
			var start = today.AddDays(-6);
			var buckets = (await _reportingRepository.GetCallsByDateBucketAsync(departmentId, start, today.AddDays(1).AddTicks(-1), ReportGranularity.Day, cancellationToken)
					?? Enumerable.Empty<CountByDateBucketResult>())
				.GroupBy(b => b.Bucket.Date)
				.ToDictionary(g => g.Key, g => g.Sum(b => b.Total));

			for (var day = start; day <= today; day = day.AddDays(1))
			{
				buckets.TryGetValue(day, out var count);
				data.Last7Days.Add(new CallsKpisDay { DayUtc = DateTime.SpecifyKind(day, DateTimeKind.Utc), Count = count });
			}

			data.TodayCount = data.Last7Days.Last().Count;

			// The top-bar badge's rule: dispatchers only, with run cards and their conditions on; scoped like the awaiting list.
			if (context.Viewer != null && context.Viewer.HasPolicy("Call_Create") &&
				await _featureToggleService.IsEnabledAsync(FeatureFlagKeys.DispatchRunCards, departmentId) &&
				await _featureToggleService.IsEnabledAsync(FeatureFlagKeys.DispatchRunCardConditions, departmentId))
			{
				var visibleCallIds = scope != null && scope.IsDepartmentWide ? null : new HashSet<int>((await FilterAsync(scope, active)).Select(c => c.CallId));
				data.AwaitingResourcesCount = await _outstandingRequirementsService.GetAwaitingCallCountAsync(departmentId, visibleCallIds, cancellationToken);
				data.AwaitingResourcesUrl = "/User/Dispatch/AwaitingResources";
			}

			return data;
		}

		private static List<Call> OfDepartment(IEnumerable<Call> calls, int departmentId) =>
			(calls ?? Enumerable.Empty<Call>()).Where(c => c != null && c.DepartmentId == departmentId && !c.IsDeleted).ToList();

		private async Task<List<Call>> FilterAsync(DispatchScope scope, List<Call> calls)
		{
			// GetScopeForUserAsync never returns null; if it ever did, nothing scoped is counted (fail closed).
			if (scope == null)
				return new List<Call>();

			if (calls.Count == 0 || scope.IsDepartmentWide)
				return calls;

			return await _dispatchScopeService.FilterCallsAsync(scope, calls) ?? new List<Call>();
		}
	}
}
