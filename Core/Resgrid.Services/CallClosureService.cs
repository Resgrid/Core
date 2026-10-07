using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
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

		public CallClosureService(IIncidentCommandService incidentCommandService, ICommunicationService communicationService,
			IDepartmentGroupsService departmentGroupsService, IPersonnelRolesService personnelRolesService, IDepartmentsService departmentsService,
			IDepartmentSettingsService departmentSettingsService, IUserProfileService userProfileService)
		{
			_incidentCommandService = incidentCommandService;
			_communicationService = communicationService;
			_departmentGroupsService = departmentGroupsService;
			_personnelRolesService = personnelRolesService;
			_departmentsService = departmentsService;
			_departmentSettingsService = departmentSettingsService;
			_userProfileService = userProfileService;
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

			foreach (var dispatch in call.Dispatches ?? Enumerable.Empty<CallDispatch>())
				AddUser(responderUsers, dispatch.UserId);

			foreach (var groupDispatch in call.GroupDispatches ?? Enumerable.Empty<CallDispatchGroup>())
			{
				var members = await _departmentGroupsService.GetAllMembersForGroupAsync(groupDispatch.DepartmentGroupId);
				foreach (var member in members ?? new List<DepartmentGroupMember>())
					AddUser(responderUsers, member.UserId);
			}

			foreach (var roleDispatch in call.RoleDispatches ?? Enumerable.Empty<CallDispatchRole>())
			{
				var members = await _personnelRolesService.GetAllMembersOfRoleAsync(roleDispatch.RoleId);
				foreach (var member in members ?? new List<PersonnelRoleUser>())
					AddUser(responderUsers, member.UserId);
			}

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

		private static void AddUser(HashSet<string> users, string userId)
		{
			if (!string.IsNullOrWhiteSpace(userId))
				users.Add(userId);
		}
	}
}
