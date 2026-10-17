using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class CallClosureService : ICallClosureService
	{
		private readonly IIncidentCommandService _incidentCommandService;
		private readonly ICommunicationService _communicationService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IPersonnelRolesService _personnelRolesService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly IUserProfileService _userProfileService;
		private readonly IShiftsService _shiftsService;
		private readonly IUnitsService _unitsService;

		public CallClosureService(IIncidentCommandService incidentCommandService, ICommunicationService communicationService,
			IDepartmentGroupsService departmentGroupsService, IPersonnelRolesService personnelRolesService, IDepartmentsService departmentsService,
			IDepartmentSettingsService departmentSettingsService, IUserProfileService userProfileService, IShiftsService shiftsService,
			IUnitsService unitsService)
		{
			_incidentCommandService = incidentCommandService;
			_communicationService = communicationService;
			_departmentGroupsService = departmentGroupsService;
			_personnelRolesService = personnelRolesService;
			_departmentsService = departmentsService;
			_departmentSettingsService = departmentSettingsService;
			_userProfileService = userProfileService;
			_shiftsService = shiftsService;
			_unitsService = unitsService;
		}

		public async Task<IncidentCommand> GetBlockingIncidentCommandAsync(int departmentId, int callId)
		{
			var command = await _incidentCommandService.GetActiveCommandForCallAsync(departmentId, callId);

			return command != null && command.Status == (int)IncidentCommandStatus.Active ? command : null;
		}

		public async Task<int> NotifyCallClosedAsync(Call call, string closedByUserId, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (call == null)
				return 0;

			var departmentId = call.DepartmentId;

			// People reached through the dispatch (Responder app) and through the incident command (IC app). A person
			// in both gets one SMS/email and a push in each app.
			var responderUsers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var commandUsers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var unitIds = new HashSet<int>();

			// The closing notice goes to the people the dispatch alerted, routed the way CallBroadcast routed it, not to
			// every member of every dispatched group: with "dispatch shift instead of group" on, a group dispatch only
			// alerted that group's on-duty roster at dispatch time.
			foreach (var userId in DispatchRecipientResolver.Resolve(GetDispatchedOnUtc(call), await BuildDispatchRoutesAsync(call)).SelectedUserIds)
				AddUser(responderUsers, userId);

			foreach (var unitDispatch in call.UnitDispatches ?? Enumerable.Empty<CallDispatchUnit>())
				unitIds.Add(unitDispatch.UnitId);

			// The incident command team: the latest command for the call (the active one has to be closed before the
			// call can be), its commander and founder, everyone holding an incident role, and the people and units on
			// the command board.
			try
			{
				var command = await _incidentCommandService.GetCommandForCallAsync(departmentId, call.CallId);

				if (command != null)
				{
					AddUser(commandUsers, command.CurrentCommanderUserId);
					AddUser(commandUsers, command.EstablishedByUserId);

					foreach (var role in await _incidentCommandService.GetIncidentRolesAsync(departmentId, call.CallId) ?? new List<IncidentRoleAssignment>())
						AddUser(commandUsers, role.UserId);

					foreach (var assignment in await _incidentCommandService.GetAssignmentsForCallAsync(departmentId, call.CallId) ?? new List<ResourceAssignment>())
					{
						if (assignment.ReleasedOn != null || string.IsNullOrWhiteSpace(assignment.ResourceId))
							continue;

						if (assignment.ResourceKind == (int)ResourceAssignmentKind.RealPersonnel)
							AddUser(commandUsers, assignment.ResourceId);
						else if (assignment.ResourceKind == (int)ResourceAssignmentKind.RealUnit && int.TryParse(assignment.ResourceId, out var unitId) && unitId > 0)
							unitIds.Add(unitId);
					}
				}
			}
			catch (Exception ex)
			{
				// A failure reading the command must not stop the dispatched crews from hearing the call closed.
				Logging.LogException(ex);
			}

			if (!string.IsNullOrWhiteSpace(closedByUserId))
			{
				responderUsers.Remove(closedByUserId);
				commandUsers.Remove(closedByUserId);
			}

			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId, false);
			var departmentNumber = await _departmentSettingsService.GetTextToCallNumberForDepartmentAsync(departmentId);
			var notified = 0;

			foreach (var userId in responderUsers.Union(commandUsers, StringComparer.OrdinalIgnoreCase))
			{
				cancellationToken.ThrowIfCancellationRequested();

				try
				{
					var profile = await _userProfileService.GetProfileByUserIdAsync(userId);

					if (await _communicationService.SendCallClosedAsync(call, userId, departmentId, departmentNumber, department, profile,
						sendToResponderApp: responderUsers.Contains(userId), sendToICApp: commandUsers.Contains(userId)))
						notified++;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
				}
			}

			foreach (var unitId in unitIds)
			{
				cancellationToken.ThrowIfCancellationRequested();

				try
				{
					if (await _communicationService.SendCallClosedUnitAsync(call, unitId, department))
						notified++;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
				}
			}

			return notified;
		}

		/// <summary>
		/// The call's dispatch routes as CallBroadcast expands them: direct, groups (the on-duty roster when the department
		/// dispatches shifts instead of groups, every member when that roster is empty), the crews and station groups of
		/// dispatched units when the department alerts them, then roles. Unit crews are the current crews. A lookup that
		/// fails drops only its own section's routes (logged), so the direct dispatches and the other sections still get
		/// the closing notice.
		/// </summary>
		private async Task<List<DispatchRoute>> BuildDispatchRoutesAsync(Call call)
		{
			var departmentId = call.DepartmentId;
			var routes = new List<DispatchRoute>();

			foreach (var dispatch in call.Dispatches ?? Enumerable.Empty<CallDispatch>())
			{
				if (!string.IsNullOrWhiteSpace(dispatch.UserId))
					routes.Add(new DispatchRoute(DispatchRouteKind.Direct, "direct", new[] { dispatch.UserId }));
			}

			var groupIds = (call.GroupDispatches ?? Enumerable.Empty<CallDispatchGroup>()).Select(x => x.DepartmentGroupId).Distinct().ToList();

			if (groupIds.Any())
			{
				try
				{
					var useShift = await _departmentSettingsService.GetDispatchShiftInsteadOfGroupAsync(departmentId);
					var onDuty = useShift
						? await _shiftsService.GetOnDutyUserIdsForGroupsAsync(departmentId, groupIds, GetDispatchedOnUtc(call))
						: null;
					onDuty ??= new Dictionary<int, List<string>>();

					foreach (var groupId in groupIds)
					{
						onDuty.TryGetValue(groupId, out var roster);
						roster ??= new List<string>();

						var members = useShift && roster.Count > 0 ? Array.Empty<string>() :
							(await _departmentGroupsService.GetAllMembersForGroupAsync(groupId) ?? new List<DepartmentGroupMember>()).Select(x => x.UserId).ToArray();
						routes.Add(new DispatchRoute(DispatchRouteKind.Group, groupId.ToString(), members, useShift, roster));
					}
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
				}
			}

			var unitDispatches = (call.UnitDispatches ?? Enumerable.Empty<CallDispatchUnit>()).ToList();

			if (unitDispatches.Any())
			{
				try
				{
					var crew = await _departmentSettingsService.GetUnitDispatchAlsoDispatchToAssignedPersonnelAsync(departmentId);
					var group = await _departmentSettingsService.GetUnitDispatchAlsoDispatchToGroupAsync(departmentId);

					foreach (var unitDispatch in unitDispatches)
					{
						if (crew)
						{
							var members = await _unitsService.GetCurrentRolesForUnitAsync(unitDispatch.UnitId);
							if (members != null)
								routes.Add(new DispatchRoute(DispatchRouteKind.UnitCrew, unitDispatch.UnitId.ToString(), members.Select(x => x.UserId).ToArray()));
						}

						if (group)
						{
							var unit = await _unitsService.GetUnitByIdAsync(unitDispatch.UnitId);
							if (unit?.StationGroupId != null)
							{
								var members = await _departmentGroupsService.GetAllMembersForGroupAsync(unit.StationGroupId.Value) ?? new List<DepartmentGroupMember>();
								routes.Add(new DispatchRoute(DispatchRouteKind.UnitGroup, unit.StationGroupId.Value.ToString(), members.Select(x => x.UserId).ToArray()));
							}
						}
					}
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
				}
			}

			try
			{
				foreach (var roleDispatch in call.RoleDispatches ?? Enumerable.Empty<CallDispatchRole>())
				{
					var members = await _personnelRolesService.GetAllMembersOfRoleAsync(roleDispatch.RoleId) ?? new List<PersonnelRoleUser>();
					routes.Add(new DispatchRoute(DispatchRouteKind.Role, roleDispatch.RoleId.ToString(), members.Select(x => x.UserId).ToArray()));
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
			}

			return routes;
		}

		/// <summary>When the call last went out, the instant whose shift roster the broadcast used (CallDispatchStatusService uses the same).</summary>
		private static DateTime GetDispatchedOnUtc(Call call)
		{
			var dispatchedOn = call.LastDispatchedOn ?? call.DispatchOn ?? (call.LoggedOn != default(DateTime) ? call.LoggedOn : DateTime.UtcNow);

			return DateTime.SpecifyKind(dispatchedOn, DateTimeKind.Utc);
		}

		private static void AddUser(HashSet<string> users, string userId)
		{
			if (!string.IsNullOrWhiteSpace(userId))
				users.Add(userId);
		}
	}
}
