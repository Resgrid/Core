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
	/// calls.awaitingResources (plan §7.1): DispatchController.GetAwaitingResources — every active call still awaiting a
	/// run-card resource, limited to the viewer's dispatch scope exactly as that action does (GetVisibleActiveCallIdsAsync).
	/// The gates (Call_Create, Dispatch.RunCards, Dispatch.RunCardConditions) are the top-bar badge's.
	/// </summary>
	[DashboardWidget("calls.awaitingResources")]
	public class CallsAwaitingResourcesWidgetProvider : IDashboardWidgetDataProvider
	{
		private const int UnitsPerRequirement = 3;

		private readonly IOutstandingRequirementsService _outstandingRequirementsService;
		private readonly ICallsService _callsService;
		private readonly IDispatchScopeService _dispatchScopeService;

		public CallsAwaitingResourcesWidgetProvider(IOutstandingRequirementsService outstandingRequirementsService, ICallsService callsService,
			IDispatchScopeService dispatchScopeService)
		{
			_outstandingRequirementsService = outstandingRequirementsService;
			_callsService = callsService;
			_dispatchScopeService = dispatchScopeService;
		}

		public async Task<object> GetDataAsync(DashboardWidgetContext context, CancellationToken cancellationToken)
		{
			var departmentId = context.DepartmentId;
			var scope = await _dispatchScopeService.GetScopeForUserAsync(departmentId, context.UserId);

			// Null means "every call" to the service; only a department-wide viewer gets that (a missing scope fails closed).
			HashSet<int> visibleCallIds = null;
			if (scope == null || !scope.IsDepartmentWide)
			{
				var active = (await _callsService.GetActiveCallsByDepartmentAsync(departmentId) ?? new List<Call>())
					.Where(c => c != null && c.DepartmentId == departmentId).ToList();

				visibleCallIds = scope == null || active.Count == 0
					? new HashSet<int>()
					: new HashSet<int>((await _dispatchScopeService.FilterCallsAsync(scope, active) ?? new List<Call>()).Select(c => c.CallId));
			}

			var awaiting = await _outstandingRequirementsService.GetAwaitingCallsAsync(departmentId, visibleCallIds, cancellationToken)
				?? new List<AwaitingResourcesCall>();

			var data = new CallsAwaitingResourcesWidgetData
			{
				TotalCount = awaiting.Count,
				DispatchUnit = new DashboardWidgetEndpoint { Url = "/User/Dispatch/DispatchToAwaitingCall", Method = "POST", Body = "form", Parameters = new[] { "callId", "unitId" } },
				Dismiss = new DashboardWidgetEndpoint { Url = "/User/Dispatch/DismissOutstandingRequirement", Method = "POST", Body = "form", Parameters = new[] { "callId", "requirementId", "note" } }
			};

			foreach (var call in awaiting.Take(context.GetInt("maxRows", 5)))
			{
				var requirements = call.Requirements ?? new List<AwaitingResourceRequirement>();

				data.Calls.Add(new CallsAwaitingResourcesCall
				{
					CallId = call.CallId,
					Number = call.CallNumber,
					Name = DashboardCallReads.Safe(call.CallName),
					Address = DashboardCallReads.Safe(call.Address),
					Priority = new DashboardCallPriority
					{
						Id = call.Priority,
						Name = call.PriorityName,
						Color = DashboardStatusPalette.Hex(call.PriorityColor) ?? "#008000"
					},
					LoggedOnUtc = call.LoggedOn,
					WaitingSinceUtc = requirements.Count > 0 ? requirements.Min(r => r.CreatedOn) : (System.DateTime?)null,
					Url = DashboardCallReads.ViewCallUrl(call.CallId),
					Requirements = requirements.Select(r => new CallsAwaitingResourcesRequirement
					{
						RequirementId = r.CallOutstandingRequirementId,
						Label = r.Label,
						RequiredCount = r.RequiredCount,
						ResolvedCount = r.ResolvedCount,
						SentInstead = r.SentInstead,
						ResponseCode = r.ResponseCode,
						ResponseCodeColor = DashboardStatusPalette.Hex(r.ResponseCodeColor),
						CreatedOnUtc = r.CreatedOn,
						AvailableUnits = (r.AvailableUnits ?? new List<AwaitingCandidateUnit>()).Take(UnitsPerRequirement).Select(u => new CallsAwaitingResourcesUnit
						{
							UnitId = u.UnitId,
							Name = u.UnitName,
							Type = u.UnitTypeName,
							ResponseSeconds = u.ResponseSeconds
						}).ToList()
					}).ToList()
				});
			}

			return data;
		}
	}
}
