using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Dashboard.Widgets.Operations;
using Resgrid.Model.Dashboard.Widgets.Personnel;
using Resgrid.Model.Services;

namespace Resgrid.Services.Dashboard.Widgets.Personnel
{
	/// <summary>
	/// personnel.myStatus (plan §7.3): today's Actions and Staffing iboxes (HomeController.Dashboard). The department's own
	/// statuses and staffing levels (with their colours) or the built-in buttons the classic page shows; the viewer's latest
	/// status and staffing; the stations and dispatch-scoped active calls they can respond to; and the classic actions the
	/// widget posts to. The stored staffing note is never returned.
	/// </summary>
	[DashboardWidget("personnel.myStatus")]
	public class PersonnelMyStatusWidgetProvider : IDashboardWidgetDataProvider
	{
		private const int MaxCalls = 20;

		/// <summary>The classic page's built-in buttons, in its order (Responding carries the respond-to-station/call menu).</summary>
		private static readonly ActionTypes[] BuiltInButtons =
		{
			ActionTypes.Responding, ActionTypes.NotResponding, ActionTypes.StandingBy, ActionTypes.AvailableStation, ActionTypes.OnScene
		};

		private readonly ICustomStateService _customStateService;
		private readonly IActionLogsService _actionLogsService;
		private readonly IUserStateService _userStateService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly ICallsService _callsService;
		private readonly IDispatchScopeService _dispatchScopeService;

		public PersonnelMyStatusWidgetProvider(ICustomStateService customStateService, IActionLogsService actionLogsService, IUserStateService userStateService,
			IDepartmentGroupsService departmentGroupsService, ICallsService callsService, IDispatchScopeService dispatchScopeService)
		{
			_customStateService = customStateService;
			_actionLogsService = actionLogsService;
			_userStateService = userStateService;
			_departmentGroupsService = departmentGroupsService;
			_callsService = callsService;
			_dispatchScopeService = dispatchScopeService;
		}

		public async Task<object> GetDataAsync(DashboardWidgetContext context, CancellationToken cancellationToken)
		{
			var departmentId = context.DepartmentId;
			var palette = await DashboardStatusPalette.LoadAsync(_customStateService, departmentId);

			var data = new PersonnelMyStatusWidgetData
			{
				UserId = context.UserId,
				SetStatus = new DashboardWidgetEndpoint { Url = "/User/Home/SetCustomAction", Method = "POST", Body = "query", Parameters = new[] { "actionType", "note" } },
				RespondToCall = new DashboardWidgetEndpoint { Url = "/User/Home/UserRespondingToCall", Method = "POST", Body = "query", Parameters = new[] { "callId" } },
				RespondToStation = new DashboardWidgetEndpoint { Url = "/User/Home/UserRespondingToStation", Method = "POST", Body = "query", Parameters = new[] { "stationId" } },
				SetStaffing = new DashboardWidgetEndpoint { Url = "/User/Home/SetCustomStaffing", Method = "POST", Body = "query", Parameters = new[] { "userId", "staffingLevel" } },
				SetStaffingWithNote = new DashboardWidgetEndpoint
				{
					Url = "/User/Home/SetUserState", Method = "POST", Body = "form",
					Parameters = new[] { "CustomStaffingActive", "UserState", "UserStateEnum", "StateNote" }
				}
			};

			var statuses = (await _customStateService.GetActivePersonnelStateForDepartmentAsync(departmentId))?.GetActiveDetails() ?? new List<CustomStateDetail>();
			if (statuses.Count > 0)
			{
				data.CustomStatuses = true;
				data.Statuses = statuses.Select(d => new PersonnelMyStatusOption
				{
					Id = d.CustomStateDetailId,
					Chip = DashboardStatusPalette.FromCustomStatus(d),
					DetailType = d.DetailType,
					NoteType = d.NoteType
				}).ToList();
			}
			else
			{
				data.Statuses = BuiltInButtons.Select(a => new PersonnelMyStatusOption
				{
					Id = (int)a,
					Chip = palette.PersonnelStatus((int)a),
					DetailType = a == ActionTypes.Responding ? (int)CustomStateDetailTypes.CallsAndStations : (int)CustomStateDetailTypes.None,
					NoteType = (int)CustomStateNoteTypes.None
				}).ToList();
			}

			var staffing = (await _customStateService.GetActiveStaffingLevelsForDepartmentAsync(departmentId))?.GetActiveDetails() ?? new List<CustomStateDetail>();
			if (staffing.Count > 0)
			{
				data.CustomStaffing = true;
				data.StaffingLevels = staffing.Select(d => new PersonnelMyStatusOption
				{
					Id = d.CustomStateDetailId,
					Chip = palette.Staffing(d.CustomStateDetailId),
					DetailType = (int)CustomStateDetailTypes.None,
					NoteType = d.NoteType
				}).ToList();
			}
			else
			{
				data.StaffingLevels = Enum.GetValues(typeof(UserStateTypes)).Cast<UserStateTypes>()
					.Where(s => (int)s <= (int)UserStateTypes.OnShift)
					.Select(s => new PersonnelMyStatusOption
					{
						Id = (int)s,
						Chip = palette.Staffing((int)s),
						DetailType = (int)CustomStateDetailTypes.None,
						NoteType = (int)CustomStateNoteTypes.Optional
					}).ToList();
			}

			var stations = (await _departmentGroupsService.GetAllStationGroupsForDepartmentAsync(departmentId) ?? new List<DepartmentGroup>())
				.Where(g => g != null && g.DepartmentId == departmentId).ToList();
			data.Stations = stations.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
				.Select(s => new PersonnelMyStatusDestination { Id = s.DepartmentGroupId, Name = s.Name }).ToList();

			var calls = await DashboardCallReads.GetScopedActiveCallsAsync(_callsService, _dispatchScopeService, departmentId, context.UserId, context.NowUtc);
			if (calls.Count > 0)
			{
				var priority = await DashboardCallReads.PriorityLookupAsync(_callsService, departmentId);
				data.Calls = calls.OrderByDescending(c => c.LoggedOn).Take(MaxCalls).Select(c => new PersonnelMyStatusDestination
				{
					Id = c.CallId,
					Number = c.Number,
					Name = DashboardCallReads.Safe(c.Name),
					Color = priority(c.Priority).Color
				}).ToList();
			}

			var status = await _actionLogsService.GetLastActionLogForUserAsync(context.UserId, departmentId);
			if (status != null && status.DepartmentId == departmentId)
			{
				data.CurrentStatus = palette.PersonnelStatus(status.ActionTypeId);
				data.CurrentStatusOnUtc = status.Timestamp;
				foreach (var call in calls)
					call.Name = DashboardCallReads.Safe(call.Name);
				data.CurrentStatusDestination = DashboardPersonnelReads.Destination(status, stations, calls, palette.PersonnelStatusOption(status.ActionTypeId)?.DetailType).Name;
			}

			// The service hands back an auto-generated Available row for someone who never set one; only a real row is "current".
			var state = await _userStateService.GetLastUserStateByUserIdAsync(context.UserId);
			if (state != null && state.UserStateId > 0 && !state.AutoGenerated && state.DepartmentId == departmentId)
			{
				data.CurrentStaffing = palette.Staffing(state.State);
				data.CurrentStaffingOnUtc = state.Timestamp;
			}

			return data;
		}
	}
}
