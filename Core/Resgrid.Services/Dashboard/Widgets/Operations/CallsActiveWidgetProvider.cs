using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Dashboard.Widgets.Operations;
using Resgrid.Model.Helpers;
using Resgrid.Model.Services;

namespace Resgrid.Services.Dashboard.Widgets.Operations
{
	/// <summary>
	/// calls.active (plan §7.1): the Dispatch dashboard's active list (DispatchController.GetActiveCallsList: dispatched active
	/// calls in the viewer's dispatch scope, newest first), with dispatch counts and the incident-command badge. The home
	/// group filter places each call in one group with the Call Summary report's rules (ICallGroupAssignmentService).
	/// </summary>
	[DashboardWidget("calls.active")]
	public class CallsActiveWidgetProvider : IDashboardWidgetDataProvider
	{
		private readonly ICallsService _callsService;
		private readonly IDispatchScopeService _dispatchScopeService;
		private readonly IIncidentCommandService _incidentCommandService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly ICallGroupAssignmentService _callGroupAssignmentService;

		public CallsActiveWidgetProvider(ICallsService callsService, IDispatchScopeService dispatchScopeService, IIncidentCommandService incidentCommandService,
			IDepartmentGroupsService departmentGroupsService, ICallGroupAssignmentService callGroupAssignmentService)
		{
			_callsService = callsService;
			_dispatchScopeService = dispatchScopeService;
			_incidentCommandService = incidentCommandService;
			_departmentGroupsService = departmentGroupsService;
			_callGroupAssignmentService = callGroupAssignmentService;
		}

		public async Task<object> GetDataAsync(DashboardWidgetContext context, CancellationToken cancellationToken)
		{
			var departmentId = context.DepartmentId;
			var data = new CallsActiveWidgetData
			{
				NewCallUrl = context.Viewer != null && context.Viewer.HasPolicy("Call_Create") ? "/User/Dispatch/NewCall" : null
			};

			var calls = await DashboardCallReads.GetScopedActiveCallsAsync(_callsService, _dispatchScopeService, departmentId, context.UserId, context.NowUtc);

			// Priority filter: only the department's own priority ids count (a stale or foreign id is dropped, never trusted).
			var priorities = DashboardCallReads.ParseIds(context.GetStrings("priorities"));
			if (priorities.Count > 0)
			{
				priorities.IntersectWith(await DashboardCallReads.PriorityIdsAsync(_callsService, departmentId));
				if (priorities.Count > 0)
					calls = calls.Where(c => priorities.Contains(c.Priority)).ToList();
			}

			var groupId = DashboardCallReads.ParseId(context.GetString("group"));
			if (groupId.HasValue)
			{
				var groups = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(departmentId) ?? new List<DepartmentGroup>();
				if (!groups.Any(g => g.DepartmentGroupId == groupId.Value && g.DepartmentId == departmentId))
				{
					data.GroupFilterIgnored = true;
				}
				else if (calls.Count > 0)
				{
					// A group includes the groups beneath it, like the Call Summary report's filter.
					var groupIds = DepartmentGroupHierarchy.GetSelfAndDescendantIds(groups, groupId.Value);
					var assignments = await _callGroupAssignmentService.AssignCallsToGroupsAsync(departmentId, calls)
						?? new Dictionary<int, Resgrid.Model.Reporting.CallGroupAssignment>();

					calls = calls.Where(c => assignments.TryGetValue(c.CallId, out var assignment) && assignment?.DepartmentGroupId != null &&
						groupIds.Contains(assignment.DepartmentGroupId.Value)).ToList();
				}
			}

			data.TotalCount = calls.Count;

			var rows = calls.OrderByDescending(c => c.LoggedOn).Take(context.GetInt("maxRows", 10)).ToList();
			if (rows.Count == 0)
				return data;

			var counts = await _callsService.GetActiveCallDispatchCountsAsync(departmentId, cancellationToken) ?? new Dictionary<int, CallDispatchCounts>();
			var commandCallIds = new HashSet<int>((await _incidentCommandService.GetActiveCommandsForDepartmentAsync(departmentId) ?? new List<IncidentCommand>())
				.Where(c => c != null && c.DepartmentId == departmentId)
				.Select(c => c.CallId));
			var priority = await DashboardCallReads.PriorityLookupAsync(_callsService, departmentId);

			foreach (var call in rows)
			{
				counts.TryGetValue(call.CallId, out var count);

				data.Calls.Add(new CallsActiveWidgetRow
				{
					CallId = call.CallId,
					Number = call.Number,
					Name = DashboardCallReads.Safe(call.Name),
					Type = string.IsNullOrWhiteSpace(call.Type) ? null : call.Type,
					Address = DashboardCallReads.Safe(call.Address),
					Priority = priority(call.Priority),
					LoggedOnUtc = call.LoggedOn,
					UnitCount = count?.UnitCount ?? 0,
					PersonnelCount = count?.PersonnelCount ?? 0,
					HasIncidentCommand = commandCallIds.Contains(call.CallId),
					Url = DashboardCallReads.ViewCallUrl(call.CallId)
				});
			}

			return data;
		}
	}
}
