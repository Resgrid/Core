using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.Records
{
	/// <summary>
	/// Gathers a call's report sources (see <see cref="ICallSourceDataService"/>) and hands them to
	/// <see cref="CallSourceDataBuilder"/>. Each source is read on its own: one that fails is named in the warnings and the
	/// rest still arrive, because a source outage must never stop an officer from writing the report.
	/// </summary>
	public class CallSourceDataService : ICallSourceDataService
	{
		private readonly ICallsService _calls;
		private readonly IUnitsService _units;
		private readonly IActionLogsService _actionLogs;
		private readonly ICustomStateService _customStates;
		private readonly IDepartmentsService _departments;
		private readonly IIncidentCommandService _commands;
		private readonly IIncidentResourcesService _resources;
		private readonly ICheckInTimerService _checkIns;
		private readonly IUnitStateRoleRepository _unitStateRoles;

		public CallSourceDataService(ICallsService calls, IUnitsService units, IActionLogsService actionLogs, ICustomStateService customStates,
			IDepartmentsService departments, IIncidentCommandService commands, IIncidentResourcesService resources, ICheckInTimerService checkIns,
			IUnitStateRoleRepository unitStateRoles)
		{
			_calls = calls;
			_units = units;
			_actionLogs = actionLogs;
			_customStates = customStates;
			_departments = departments;
			_commands = commands;
			_resources = resources;
			_checkIns = checkIns;
			_unitStateRoles = unitStateRoles;
		}

		public async Task<CallSourceData> GetForCallAsync(int departmentId, int callId)
		{
			if (callId <= 0)
				return null;

			var call = await _calls.GetCallByIdAsync(callId);
			return await GetForCallAsync(departmentId, call);
		}

		public async Task<CallSourceData> GetForCallAsync(int departmentId, Call call)
		{
			if (call == null || call.DepartmentId != departmentId)
				return null;

			var input = new CallSourceInputs { Call = call, Now = DateTime.UtcNow };

			if (call.UnitDispatches == null || call.Dispatches == null)
				call = await Read(input, "dispatches", () => _calls.PopulateCallData(call, true, false, false, false, true, false, false, false, false), call) ?? call;
			input.Call = call;

			input.UnitStates = (await Read(input, "unit statuses", () => _units.GetUnitStatesForCallAsync(departmentId, call.CallId), null) ?? new List<UnitState>()).Where(s => s != null).ToList();
			input.ActionLogs = (await Read(input, "personnel statuses", () => _actionLogs.GetActionLogsForCallAsync(departmentId, call.CallId), null) ?? new List<ActionLog>())
				.Where(l => l != null && (l.DepartmentId == 0 || l.DepartmentId == departmentId)).ToList();

			var customStates = await Read(input, "status definitions", () => _customStates.GetAllCustomStatesForDepartmentAsync(departmentId), null) ?? new List<CustomState>();
			input.UnitStatuses = CallStatusLinkage.BuildStatusLookup(_customStates.GetDefaultUnitStatuses(), customStates, CustomStateTypes.Unit);
			input.PersonnelStatuses = CallStatusLinkage.BuildStatusLookup(_customStates.GetDefaultPersonStatuses(), customStates, CustomStateTypes.Personnel);
			input.UnitBaseTypes = await Read(input, "unit status types", () => _units.GetCustomUnitStateBaseTypesAsync(departmentId), null) ?? CallStatusLinkage.BuildUnitBaseTypeMap(customStates);
			input.PersonnelBaseTypes = CallStatusLinkage.BuildBaseTypeMap(customStates, CustomStateTypes.Personnel);

			// The units that worked the call, deleted ones included.
			var units = (await Read(input, "units", () => _units.GetUnitsForDepartmentIncludingDeletedAsync(departmentId), null) ?? new List<Unit>()).Where(u => u != null).GroupBy(u => u.UnitId).ToDictionary(g => g.Key, g => g.First());
			foreach (var state in input.UnitStates.Where(s => s.Unit != null && !units.ContainsKey(s.UnitId) && s.Unit.DepartmentId == departmentId))
				units[state.UnitId] = state.Unit;
			input.Units = units;

			var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (var person in await Read(input, "personnel names", () => _departments.GetAllPersonnelNamesForDepartmentAsync(departmentId), null) ?? new List<PersonName>())
			{
				if (person != null && !string.IsNullOrWhiteSpace(person.UserId) && !names.ContainsKey(person.UserId))
					names[person.UserId] = person.Name?.Trim();
			}
			input.Names = names;

			var stateIds = input.UnitStates.Where(s => s.UnitStateId > 0).Select(s => s.UnitStateId).Distinct().ToList();
			if (stateIds.Count > 0)
				input.Crew = (await Read(input, "unit crews", async () => (await _unitStateRoles.GetRolesForUnitStatesAsync(stateIds))?.ToList(), null) ?? new List<UnitStateRole>()).Where(r => r != null).ToList();

			input.CheckIns = (await Read(input, "check-ins", () => _checkIns.GetCheckInsForCallAsync(call.CallId), null) ?? new List<CheckInRecord>())
				.Where(c => c != null && c.DepartmentId == departmentId).ToList();

			await ReadCommandAsync(input, departmentId, call.CallId);

			return CallSourceDataBuilder.Build(input);
		}

		private async Task ReadCommandAsync(CallSourceInputs input, int departmentId, int callId)
		{
			var command = await Read(input, "incident command", () => _commands.GetCommandForCallAsync(departmentId, callId), null);
			if (command == null || command.DepartmentId != departmentId)
				return;

			input.Command = command;
			input.CommandTimeline = (await Read(input, "command timeline", () => _commands.GetTimelineForCallAsync(departmentId, callId), null) ?? new List<CommandLogEntry>())
				.Where(e => e != null && e.DepartmentId == departmentId).ToList();
			input.Assignments = (await Read(input, "command assignments", () => _commands.GetAssignmentsForCallAsync(departmentId, callId), null) ?? new List<ResourceAssignment>()).Where(a => a != null).ToList();
			input.Objectives = (await Read(input, "command objectives", () => _commands.GetObjectivesForCallAsync(departmentId, callId), null) ?? new List<TacticalObjective>())
				.Where(o => o != null && o.DepartmentId == departmentId).ToList();
			input.AdHocUnits = await Read(input, "mutual aid units", () => _resources.GetAdHocUnitsForCallAsync(departmentId, callId, includeReleased: true), null) ?? new List<IncidentAdHocUnit>();
			input.AdHocPersonnel = await Read(input, "mutual aid personnel", () => _resources.GetAdHocPersonnelForCallAsync(departmentId, callId, includeReleased: true), null) ?? new List<IncidentAdHocPersonnel>();

			var established = input.CommandTimeline.Where(e => e.EntryType == (int)CommandLogEntryType.CommandEstablished).Select(e => (DateTime?)e.OccurredOn).Concat(new DateTime?[] { command.EstablishedOn });
			input.FirstCommandEstablishedOn = established.Where(d => d.HasValue && d.Value > DateTime.MinValue).Min();

			// A linked department's unit or member is named by its own department, read once each.
			var linkedDepartments = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
			var linkedNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			var departmentNames = new Dictionary<int, string>();
			foreach (var assignment in input.Assignments.Where(a => a.ResourceKind == (int)ResourceAssignmentKind.LinkedDeptUnit || a.ResourceKind == (int)ResourceAssignmentKind.LinkedDeptPersonnel))
			{
				if (string.IsNullOrWhiteSpace(assignment.ResourceId) || linkedDepartments.ContainsKey(assignment.ResourceId))
					continue;

				try
				{
					if (assignment.ResourceKind == (int)ResourceAssignmentKind.LinkedDeptUnit && int.TryParse(assignment.ResourceId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unitId))
					{
						var unit = await _units.GetUnitByIdAsync(unitId);
						if (unit == null || unit.DepartmentId == departmentId)
							continue;
						linkedDepartments[assignment.ResourceId] = unit.DepartmentId;
						linkedNames[assignment.ResourceId] = unit.Name;
					}
					else if (assignment.ResourceKind == (int)ResourceAssignmentKind.LinkedDeptPersonnel)
					{
						var member = await _departments.GetDepartmentMemberAsync(assignment.ResourceId, departmentId);
						if (member != null)
							continue;
						var other = await _departments.GetDepartmentByUserIdAsync(assignment.ResourceId);
						if (other == null || other.DepartmentId == departmentId)
							continue;
						linkedDepartments[assignment.ResourceId] = other.DepartmentId;
						departmentNames[other.DepartmentId] = other.Name;
					}

					var linkedDepartmentId = linkedDepartments[assignment.ResourceId];
					if (!departmentNames.ContainsKey(linkedDepartmentId))
						departmentNames[linkedDepartmentId] = (await _departments.GetDepartmentByIdAsync(linkedDepartmentId, false))?.Name;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Linked resource {assignment.ResourceId} on call {callId} could not be named for report sources.");
				}
			}

			input.LinkedResourceDepartments = linkedDepartments;
			input.LinkedResourceNames = linkedNames;
			input.DepartmentNames = departmentNames;
		}

		private static async Task<T> Read<T>(CallSourceInputs input, string source, Func<Task<T>> read, T fallback)
		{
			try
			{
				return await read();
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Report source '{source}' could not be read for call {input.Call?.CallId}; the rest of the sources are still used.");
				input.Warnings.Add(source);
				return fallback;
			}
		}
	}
}
