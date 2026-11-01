using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Dashboard.Widgets.Operations;
using Resgrid.Model.Services;

namespace Resgrid.Services.Dashboard.Widgets.Operations
{
	/// <summary>
	/// calls.pending (plan §7.1): DispatchController.GetPendingCallsList and GetScheduledCallsList in one card, trimmed by the
	/// viewer's dispatch scope. "Dispatch now" is offered by the edit rule those lists use (AuthorizationService.CanUserEditCallAsync:
	/// department admin or the call's reporter) to callers who create calls; DispatchCallNow re-checks everything itself.
	/// </summary>
	[DashboardWidget("calls.pending")]
	public class CallsPendingWidgetProvider : IDashboardWidgetDataProvider
	{
		private readonly ICallsService _callsService;
		private readonly IDispatchScopeService _dispatchScopeService;

		public CallsPendingWidgetProvider(ICallsService callsService, IDispatchScopeService dispatchScopeService)
		{
			_callsService = callsService;
			_dispatchScopeService = dispatchScopeService;
		}

		public async Task<object> GetDataAsync(DashboardWidgetContext context, CancellationToken cancellationToken)
		{
			var departmentId = context.DepartmentId;
			var show = context.GetString("show", "all");

			var pending = await DashboardCallReads.ScopeAsync(_dispatchScopeService, departmentId, context.UserId,
				OfDepartment(await _callsService.GetPendingCallsByDepartmentIdAsync(departmentId), departmentId));
			var scheduled = await DashboardCallReads.ScopeAsync(_dispatchScopeService, departmentId, context.UserId,
				OfDepartment(await _callsService.GetAllNonDispatchedScheduledCallsByDepartmentIdAsync(departmentId), departmentId));

			var canCreate = context.Viewer != null && context.Viewer.HasPolicy("Call_Create");
			var data = new CallsPendingWidgetData
			{
				PendingCount = pending.Count,
				ScheduledCount = scheduled.Count,
				DispatchNow = canCreate
					? new DashboardWidgetEndpoint { Url = "/User/Dispatch/DispatchCallNow", Method = "POST", Body = "form", Parameters = new[] { "callId", "returnTo" } }
					: null
			};

			var rows = new List<(Call Call, string Kind)>();
			if (show != "scheduled")
				rows.AddRange(pending.OrderBy(c => c.LoggedOn).Select(c => (c, "pending")));
			if (show != "pending")
				rows.AddRange(scheduled.OrderBy(c => c.DispatchOn ?? DateTime.MaxValue).Select(c => (c, "scheduled")));

			var selected = rows.Take(context.GetInt("maxRows", 10)).ToList();
			if (selected.Count == 0)
				return data;

			var priority = await DashboardCallReads.PriorityLookupAsync(_callsService, departmentId);
			var isAdmin = context.Viewer != null && context.Viewer.IsDepartmentAdmin;

			foreach (var (call, kind) in selected)
			{
				data.Calls.Add(new CallsPendingWidgetRow
				{
					CallId = call.CallId,
					Number = call.Number,
					Name = DashboardCallReads.Safe(call.Name),
					Type = string.IsNullOrWhiteSpace(call.Type) ? null : call.Type,
					Address = DashboardCallReads.Safe(call.Address),
					Priority = priority(call.Priority),
					Kind = kind,
					LoggedOnUtc = call.LoggedOn,
					DispatchOnUtc = call.DispatchOn,
					CanDispatchNow = canCreate && (isAdmin || string.Equals(call.ReportingUserId, context.UserId, StringComparison.Ordinal)),
					Url = DashboardCallReads.ViewCallUrl(call.CallId)
				});
			}

			return data;
		}

		private static List<Call> OfDepartment(IEnumerable<Call> calls, int departmentId) =>
			(calls ?? Enumerable.Empty<Call>()).Where(c => c != null && c.DepartmentId == departmentId && !c.IsDeleted).ToList();
	}
}
