using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Resgrid.Model;
using Resgrid.Model.Reporting;

namespace Resgrid.Services.Records
{
	/// <summary>Everything <see cref="CallSourceDataBuilder"/> reads, already loaded and filtered to the call's department.</summary>
	public class CallSourceInputs
	{
		public Call Call { get; set; }
		public List<UnitState> UnitStates { get; set; } = new List<UnitState>();
		public List<ActionLog> ActionLogs { get; set; } = new List<ActionLog>();
		public IReadOnlyDictionary<int, CustomStateDetail> UnitStatuses { get; set; } = new Dictionary<int, CustomStateDetail>();
		public IReadOnlyDictionary<int, CustomStateDetail> PersonnelStatuses { get; set; } = new Dictionary<int, CustomStateDetail>();
		public IReadOnlyDictionary<int, int> UnitBaseTypes { get; set; } = new Dictionary<int, int>();
		public IReadOnlyDictionary<int, int> PersonnelBaseTypes { get; set; } = new Dictionary<int, int>();
		/// <summary>The department's units (and any other unit a row names), by id.</summary>
		public IReadOnlyDictionary<int, Unit> Units { get; set; } = new Dictionary<int, Unit>();
		/// <summary>Member display names by user id (case-insensitive).</summary>
		public IReadOnlyDictionary<string, string> Names { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		/// <summary>Crew seats recorded with the unit states.</summary>
		public List<UnitStateRole> Crew { get; set; } = new List<UnitStateRole>();
		public List<CheckInRecord> CheckIns { get; set; } = new List<CheckInRecord>();

		/// <summary>The call's newest Incident Command, or null when it never ran under one.</summary>
		public IncidentCommand Command { get; set; }
		/// <summary>The earliest establishment across all the call's commands (a command may be closed and a new one opened).</summary>
		public DateTime? FirstCommandEstablishedOn { get; set; }
		public List<CommandLogEntry> CommandTimeline { get; set; } = new List<CommandLogEntry>();
		public List<ResourceAssignment> Assignments { get; set; } = new List<ResourceAssignment>();
		public List<TacticalObjective> Objectives { get; set; } = new List<TacticalObjective>();
		public List<IncidentAdHocUnit> AdHocUnits { get; set; } = new List<IncidentAdHocUnit>();
		public List<IncidentAdHocPersonnel> AdHocPersonnel { get; set; } = new List<IncidentAdHocPersonnel>();
		/// <summary>Linked-department units' and members' departments (resource id to department id).</summary>
		public IReadOnlyDictionary<string, int> LinkedResourceDepartments { get; set; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		/// <summary>Linked departments' names.</summary>
		public IReadOnlyDictionary<int, string> DepartmentNames { get; set; } = new Dictionary<int, string>();
		/// <summary>Linked-department unit names by unit id (resource id).</summary>
		public IReadOnlyDictionary<string, string> LinkedResourceNames { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

		public List<string> Warnings { get; set; } = new List<string>();
		public DateTime Now { get; set; } = DateTime.UtcNow;
	}

	/// <summary>
	/// Turns a call's raw sources into <see cref="CallSourceData"/>: a time-ordered entry list for lookups, and per-unit,
	/// per-person and command summaries for prefill. Pure: no reads, so the rules are tested directly. Unit times follow
	/// <see cref="CallUnitTimesCalculator"/> (the Call Unit Times report), so a report and the call agree.
	/// </summary>
	public static class CallSourceDataBuilder
	{
		/// <summary>Command timeline entries a report author looks up; map edits, timers and sharing changes are left out.</summary>
		private static readonly HashSet<CommandLogEntryType> ReportableCommandEntries = new HashSet<CommandLogEntryType>
		{
			CommandLogEntryType.CommandEstablished, CommandLogEntryType.CommandTransferred, CommandLogEntryType.CommandClosed, CommandLogEntryType.CommandReopened,
			CommandLogEntryType.ResourceAssigned, CommandLogEntryType.ResourceMoved, CommandLogEntryType.ResourceReleased, CommandLogEntryType.AdHocResourceCreated,
			CommandLogEntryType.RoleAssigned, CommandLogEntryType.ParCritical, CommandLogEntryType.Note, CommandLogEntryType.IncidentNoteAdded,
			CommandLogEntryType.NeedAdded, CommandLogEntryType.NeedUpdated, CommandLogEntryType.NeedMet, CommandLogEntryType.CheckIn
		};

		public static CallSourceData Build(CallSourceInputs input)
		{
			var call = input.Call;
			var data = new CallSourceData
			{
				CallId = call.CallId,
				Number = Plain(call.Number),
				IncidentNumber = Plain(call.IncidentNumber),
				Type = Plain(call.Type),
				Address = Plain(call.Address),
				Nature = Plain(call.NatureOfCall),
				Priority = call.Priority,
				LoggedOn = call.LoggedOn,
				DispatchedOn = call.HasBeenDispatched == true ? call.DispatchOn : null,
				ClosedOn = call.ClosedOn,
				CapturedOn = input.Now,
				Warnings = input.Warnings.ToList()
			};

			AddCallEntries(input, data);
			var stateEntries = AddUnitStateEntries(input, data);
			var logEntries = AddPersonnelEntries(input, data);
			AddCommandEntries(input, data);
			AddCheckInEntries(input, data);

			BuildUnits(input, data, stateEntries);
			BuildPersonnel(input, data, logEntries);
			data.Command = BuildCommand(input);

			data.Entries = data.Entries.OrderBy(e => e.TimestampUtc).ThenBy(e => (int)e.Kind).ThenBy(e => e.SubjectName, StringComparer.OrdinalIgnoreCase).ToList();
			return data;
		}

		#region Entries

		private static void AddCallEntries(CallSourceInputs input, CallSourceData data)
		{
			var call = input.Call;
			var subject = string.IsNullOrWhiteSpace(data.Number) ? "#" + call.CallId.ToString(CultureInfo.InvariantCulture) : data.Number;
			data.Entries.Add(new CallSourceEntry
			{
				Id = "call:created", Kind = CallSourceEntryKind.Call, TimestampUtc = call.LoggedOn, SubjectName = subject, Label = "Call created",
				Milestone = CallSourceMilestone.CallCreated, SetByUserId = call.ReportingUserId, SetByName = Name(input, call.ReportingUserId)
			});

			if (call.ClosedOn.HasValue)
			{
				data.Entries.Add(new CallSourceEntry
				{
					Id = "call:closed", Kind = CallSourceEntryKind.Call, TimestampUtc = call.ClosedOn.Value, SubjectName = subject, Label = "Call closed",
					Milestone = CallSourceMilestone.CallClosed, SetByUserId = call.ClosedByUserId, SetByName = Name(input, call.ClosedByUserId)
				});
			}

			foreach (var dispatch in (call.UnitDispatches ?? new List<CallDispatchUnit>()).Where(d => d != null))
			{
				data.Entries.Add(new CallSourceEntry
				{
					Id = "unit-dispatch:" + dispatch.CallDispatchUnitId.ToString(CultureInfo.InvariantCulture), Kind = CallSourceEntryKind.UnitDispatch,
					TimestampUtc = dispatch.DispatchedOn, UnitId = dispatch.UnitId, SubjectName = UnitName(input, dispatch.UnitId), Label = "Dispatched",
					Milestone = CallSourceMilestone.Dispatched
				});
			}

			foreach (var dispatch in (call.Dispatches ?? new List<CallDispatch>()).Where(d => d != null && !string.IsNullOrWhiteSpace(d.UserId)))
			{
				data.Entries.Add(new CallSourceEntry
				{
					Id = "personnel-dispatch:" + dispatch.CallDispatchId.ToString(CultureInfo.InvariantCulture), Kind = CallSourceEntryKind.PersonnelDispatch,
					TimestampUtc = dispatch.DispatchedOn, UserId = dispatch.UserId, SubjectName = Name(input, dispatch.UserId) ?? dispatch.UserId, Label = "Dispatched",
					Milestone = CallSourceMilestone.Dispatched
				});
			}
		}

		private static Dictionary<UnitState, CallSourceEntry> AddUnitStateEntries(CallSourceInputs input, CallSourceData data)
		{
			var map = new Dictionary<UnitState, CallSourceEntry>();
			foreach (var state in input.UnitStates.Where(s => s != null).OrderBy(s => s.Timestamp).ThenBy(s => s.UnitStateId))
			{
				input.UnitStatuses.TryGetValue(state.State, out var detail);
				var entry = new CallSourceEntry
				{
					Id = "unit-state:" + state.UnitStateId.ToString(CultureInfo.InvariantCulture) + (CallStatusAttribution.IsInferred(state.DestinationSource) ? ":inferred" : string.Empty),
					Kind = CallSourceEntryKind.UnitStatus,
					TimestampUtc = state.Timestamp,
					UnitId = state.UnitId,
					SubjectName = state.Unit?.Name ?? UnitName(input, state.UnitId),
					Label = detail?.ButtonText ?? BuiltInUnitText(state.State),
					Color = detail?.ButtonColor,
					Detail = Plain(state.Note),
					Milestone = UnitMilestone(state.State, input.UnitBaseTypes),
					Linkage = state.DestinationSource,
					Origin = state.SetByOrigin ?? (int)StatusSetOrigins.Unknown,
					SetByUserId = state.SetByUserId,
					SetByName = Name(input, state.SetByUserId)
				};
				data.Entries.Add(entry);
				map[state] = entry;
			}

			return map;
		}

		private static Dictionary<ActionLog, CallSourceEntry> AddPersonnelEntries(CallSourceInputs input, CallSourceData data)
		{
			var map = new Dictionary<ActionLog, CallSourceEntry>();
			var stateUnits = input.UnitStates.Where(s => s != null && s.UnitStateId > 0).GroupBy(s => s.UnitStateId).ToDictionary(g => g.Key, g => g.First().UnitId);
			foreach (var log in input.ActionLogs.Where(l => l != null && !string.IsNullOrWhiteSpace(l.UserId)).OrderBy(l => l.Timestamp).ThenBy(l => l.ActionLogId))
			{
				input.PersonnelStatuses.TryGetValue(log.ActionTypeId, out var detail);
				int? unitId = log.UnitStateId.HasValue && stateUnits.TryGetValue(log.UnitStateId.Value, out var riding) ? riding : (int?)null;
				var label = detail?.ButtonText ?? BuiltInPersonnelText(log);
				var entry = new CallSourceEntry
				{
					Id = "personnel-status:" + log.ActionLogId.ToString(CultureInfo.InvariantCulture) + (CallStatusAttribution.IsInferred(log.DestinationSource) ? ":inferred" : string.Empty),
					Kind = CallSourceEntryKind.PersonnelStatus,
					TimestampUtc = log.Timestamp,
					UserId = log.UserId,
					UnitId = unitId,
					SubjectName = Name(input, log.UserId) ?? log.UserId,
					Label = label,
					Color = detail?.ButtonColor,
					Detail = JoinDetail(Plain(log.Note), log.ActionTypeId == (int)ActionTypes.OnUnit || string.IsNullOrWhiteSpace(log.UnitName) ? null : log.UnitName),
					Milestone = PersonnelMilestone(log.ActionTypeId, input.PersonnelBaseTypes),
					Linkage = log.DestinationSource,
					Origin = log.SetByOrigin ?? (int)StatusSetOrigins.Unknown,
					SetByUserId = log.SetByUserId,
					SetByName = Name(input, log.SetByUserId)
				};
				data.Entries.Add(entry);
				map[log] = entry;
			}

			return map;
		}

		private static void AddCommandEntries(CallSourceInputs input, CallSourceData data)
		{
			foreach (var log in input.CommandTimeline.Where(l => l != null && ReportableCommandEntries.Contains((CommandLogEntryType)l.EntryType)))
			{
				var type = (CommandLogEntryType)log.EntryType;
				var entry = new CallSourceEntry
				{
					Id = "command:" + log.CommandLogEntryId,
					Kind = CallSourceEntryKind.Command,
					TimestampUtc = log.OccurredOn,
					SubjectName = Plain(input.Command?.Name) ?? "Incident Command",
					Label = CommandLabel(type),
					Detail = Plain(log.Description),
					Milestone = type == CommandLogEntryType.CommandEstablished ? CallSourceMilestone.CommandEstablished
						: type == CommandLogEntryType.CommandClosed ? CallSourceMilestone.CommandClosed : CallSourceMilestone.None,
					Origin = (int)StatusSetOrigins.Unknown
				};

				// A PAR entry is about the member who is overdue, not written by them.
				if (type == CommandLogEntryType.ParCritical)
				{
					entry.UserId = log.UserId;
					entry.SubjectName = Name(input, log.UserId) ?? entry.SubjectName;
				}
				else
				{
					entry.SetByUserId = log.UserId;
					entry.SetByName = Name(input, log.UserId);
				}

				data.Entries.Add(entry);
			}

			foreach (var objective in input.Objectives.Where(o => o != null && o.Status == (int)TacticalObjectiveStatus.Complete && o.CompletedOn.HasValue))
			{
				var field = NerisTacticTimestamps.MatchObjective(objective.Name);
				data.Entries.Add(new CallSourceEntry
				{
					Id = "objective:" + objective.TacticalObjectiveId,
					Kind = CallSourceEntryKind.Objective,
					TimestampUtc = objective.CompletedOn.Value,
					SubjectName = Plain(input.Command?.Name) ?? "Incident Command",
					Label = Plain(objective.Name) ?? "Objective",
					Detail = Plain(objective.CompletionNote),
					Milestone = field != null ? CallSourceMilestone.TacticMilestone : CallSourceMilestone.None,
					TacticTimestamp = field,
					SetByUserId = objective.CompletedByUserId,
					SetByName = Name(input, objective.CompletedByUserId)
				});
			}
		}

		private static void AddCheckInEntries(CallSourceInputs input, CallSourceData data)
		{
			foreach (var checkIn in input.CheckIns.Where(c => c != null && c.CallId == input.Call.CallId))
			{
				var person = !string.IsNullOrWhiteSpace(checkIn.UserId) ? Name(input, checkIn.UserId) : null;
				data.Entries.Add(new CallSourceEntry
				{
					Id = "check-in:" + checkIn.CheckInRecordId,
					Kind = CallSourceEntryKind.CheckIn,
					TimestampUtc = checkIn.Timestamp,
					UserId = checkIn.UserId,
					UnitId = checkIn.UnitId,
					SubjectName = checkIn.UnitId.HasValue ? UnitName(input, checkIn.UnitId.Value) : person ?? checkIn.UserId,
					Label = "Check-in",
					Detail = JoinDetail(checkIn.UnitId.HasValue ? person : null, Plain(checkIn.Note))
				});
			}
		}

		#endregion

		#region Summaries

		private static void BuildUnits(CallSourceInputs input, CallSourceData data, Dictionary<UnitState, CallSourceEntry> stateEntries)
		{
			var call = input.Call;
			var dispatches = (call.UnitDispatches ?? new List<CallDispatchUnit>()).Where(d => d != null).ToList();
			var commandUnits = input.Assignments.Where(a => a != null && a.ResourceKind == (int)ResourceAssignmentKind.RealUnit && int.TryParse(a.ResourceId, out _))
				.GroupBy(a => int.Parse(a.ResourceId, CultureInfo.InvariantCulture)).ToDictionary(g => g.Key, g => g.ToList());
			var unitIds = dispatches.Select(d => d.UnitId).Concat(input.UnitStates.Where(s => s != null).Select(s => s.UnitId)).Concat(commandUnits.Keys).Distinct().ToList();
			var times = CallUnitTimesCalculator.Compute(dispatches, input.UnitStates, input.UnitBaseTypes).ToDictionary(r => r.UnitId);
			var crewByState = input.Crew.Where(c => c != null && !string.IsNullOrWhiteSpace(c.UserId)).GroupBy(c => c.UnitStateId).ToDictionary(g => g.Key, g => g.ToList());
			var riders = input.ActionLogs.Where(l => l != null && l.UnitStateId.HasValue && !string.IsNullOrWhiteSpace(l.UserId)).ToList();

			foreach (var unitId in unitIds)
			{
				input.Units.TryGetValue(unitId, out var unit);
				var states = input.UnitStates.Where(s => s != null && s.UnitId == unitId).OrderBy(s => s.Timestamp).ThenBy(s => s.UnitStateId).ToList();
				times.TryGetValue(unitId, out var row);
				commandUnits.TryGetValue(unitId, out var assignments);

				var summary = new CallSourceUnit
				{
					UnitId = unitId,
					Name = unit?.Name ?? states.Select(s => s.Unit?.Name).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? "#" + unitId.ToString(CultureInfo.InvariantCulture),
					Type = unit?.Type,
					StationGroupId = unit?.StationGroupId,
					WasDispatched = dispatches.Any(d => d.UnitId == unitId),
					AssignedByCommand = assignments != null,
					DispatchedOn = row?.DispatchedOn,
					EnrouteOn = row?.EnrouteOn,
					OnSceneOn = row?.OnSceneOn,
					StagingOn = row?.StagingOn,
					ClearedOn = row?.ClearedOn,
					TimesSource = (int)(row?.Source ?? CallUnitTimesSources.DispatchOnly),
					CommandAssignedOn = assignments?.Min(a => a.AssignedOn),
					CommandReleasedOn = assignments != null && assignments.All(a => a.ReleasedOn.HasValue) ? assignments.Max(a => a.ReleasedOn) : null
				};

				UnitState First(Func<UnitState, bool> match) => states.FirstOrDefault(match);
				bool Is(UnitState s, params UnitStateTypes[] kinds) => CallStatusLinkage.ResolveUnitStateKind(s.State, input.UnitBaseTypes) is UnitStateTypes kind && kinds.Contains(kind);

				// Cancelled en route only counts when the unit never arrived.
				if (!summary.OnSceneOn.HasValue)
					summary.CancelledOn = First(s => Is(s, UnitStateTypes.Cancelled))?.Timestamp;
				if (summary.ClearedOn.HasValue)
					summary.InServiceOn = First(s => s.Timestamp >= summary.ClearedOn.Value && Is(s, UnitStateTypes.Available))?.Timestamp;

				void Track(CallSourceMilestone milestone, DateTime? at, params UnitStateTypes[] kinds)
				{
					if (!at.HasValue)
						return;
					var state = kinds.Length == 0
						? First(s => s.Timestamp == at.Value && CallStatusLinkage.IsClearingUnitState(s.State, input.UnitBaseTypes))
						: First(s => s.Timestamp == at.Value && Is(s, kinds));
					if (state != null && stateEntries.TryGetValue(state, out var entry))
						summary.TimeEntries[milestone] = entry;
				}

				if (summary.DispatchedOn.HasValue)
				{
					var dispatchEntry = data.Entries.FirstOrDefault(e => e.Kind == CallSourceEntryKind.UnitDispatch && e.UnitId == unitId && e.TimestampUtc == summary.DispatchedOn.Value);
					if (dispatchEntry != null)
						summary.TimeEntries[CallSourceMilestone.Dispatched] = dispatchEntry;
				}
				Track(CallSourceMilestone.Enroute, summary.EnrouteOn, UnitStateTypes.Responding, UnitStateTypes.Enroute);
				Track(CallSourceMilestone.OnScene, summary.OnSceneOn, UnitStateTypes.OnScene);
				Track(CallSourceMilestone.Staging, summary.StagingOn, UnitStateTypes.Staging);
				Track(CallSourceMilestone.Cancelled, summary.CancelledOn, UnitStateTypes.Cancelled);
				Track(CallSourceMilestone.Cleared, summary.ClearedOn);
				Track(CallSourceMilestone.InService, summary.InServiceOn, UnitStateTypes.Available);

				// Crew: the seats recorded with the unit's statuses on this call, plus anyone a status placed on the unit.
				var stateIds = new HashSet<int>(states.Where(s => s.UnitStateId > 0).Select(s => s.UnitStateId));
				var crew = new List<string>();
				foreach (var id in stateIds.Where(crewByState.ContainsKey))
					crew.AddRange(crewByState[id].Select(c => c.UserId));
				crew.AddRange(riders.Where(l => stateIds.Contains(l.UnitStateId.Value)).Select(l => l.UserId));
				summary.CrewUserIds = crew.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(id => Name(input, id) ?? id, StringComparer.OrdinalIgnoreCase).ToList();
				summary.CrewNames = summary.CrewUserIds.Select(id => Name(input, id) ?? id).ToList();
				summary.Staffing = summary.CrewUserIds.Count > 0 ? summary.CrewUserIds.Count : (int?)null;

				data.Units.Add(summary);
			}

			data.Units = data.Units.OrderBy(u => u.DispatchedOn ?? u.EnrouteOn ?? u.OnSceneOn ?? u.CommandAssignedOn ?? DateTime.MaxValue).ThenBy(u => u.Name, StringComparer.OrdinalIgnoreCase).ToList();
		}

		private static void BuildPersonnel(CallSourceInputs input, CallSourceData data, Dictionary<ActionLog, CallSourceEntry> logEntries)
		{
			var call = input.Call;
			var people = new Dictionary<string, CallSourcePerson>(StringComparer.OrdinalIgnoreCase);
			CallSourcePerson Person(string userId)
			{
				if (!people.TryGetValue(userId, out var person))
				{
					person = new CallSourcePerson { UserId = userId, Name = Name(input, userId) ?? userId };
					people[userId] = person;
				}
				return person;
			}
			void Seen(CallSourcePerson person, DateTime at)
			{
				if (!person.FirstOn.HasValue || at < person.FirstOn.Value) person.FirstOn = at;
				if (!person.LastOn.HasValue || at > person.LastOn.Value) person.LastOn = at;
			}

			foreach (var dispatch in (call.Dispatches ?? new List<CallDispatch>()).Where(d => d != null && !string.IsNullOrWhiteSpace(d.UserId)))
				Person(dispatch.UserId).WasDispatched = true;

			var unitByState = input.UnitStates.Where(s => s != null && s.UnitStateId > 0).GroupBy(s => s.UnitStateId).ToDictionary(g => g.Key, g => g.First());
			foreach (var log in logEntries.Keys.OrderBy(l => l.Timestamp))
			{
				var person = Person(log.UserId);
				var entry = logEntries[log];
				Seen(person, log.Timestamp);

				if (log.UnitStateId.HasValue && unitByState.TryGetValue(log.UnitStateId.Value, out var riding))
				{
					person.UnitId = riding.UnitId;
					person.Engaged = true;
				}

				var clearing = CallStatusLinkage.IsClearingPersonnelStatus(log.ActionTypeId, input.PersonnelBaseTypes);
				if (!clearing && log.ActionTypeId != (int)ActionTypes.NotResponding)
					person.Engaged = true;

				switch (entry.Milestone)
				{
					case CallSourceMilestone.Enroute:
						person.RespondingOn ??= log.Timestamp;
						break;
					case CallSourceMilestone.OnScene:
						person.OnSceneOn ??= log.Timestamp;
						break;
				}

				if (clearing && person.Engaged && !person.ClearedOn.HasValue)
					person.ClearedOn = log.Timestamp;
			}

			// Crew seats recorded with unit statuses put a member on the unit even when no personnel status was written.
			foreach (var seat in input.Crew.Where(c => c != null && !string.IsNullOrWhiteSpace(c.UserId)))
			{
				if (!unitByState.TryGetValue(seat.UnitStateId, out var state))
					continue;

				var person = Person(seat.UserId);
				person.Engaged = true;
				person.UnitId ??= state.UnitId;
				if (person.UnitId == state.UnitId && string.IsNullOrWhiteSpace(person.Role) && !string.IsNullOrWhiteSpace(seat.Role))
					person.Role = seat.Role.Trim();
				Seen(person, state.Timestamp);
			}

			foreach (var assignment in input.Assignments.Where(a => a != null && a.ResourceKind == (int)ResourceAssignmentKind.RealPersonnel && !string.IsNullOrWhiteSpace(a.ResourceId)))
			{
				var person = Person(assignment.ResourceId);
				person.AssignedByCommand = true;
				person.Engaged = true;
				Seen(person, assignment.AssignedOn);
				if (assignment.ReleasedOn.HasValue)
					Seen(person, assignment.ReleasedOn.Value);
			}

			foreach (var checkIn in input.CheckIns.Where(c => c != null && c.CallId == call.CallId && !string.IsNullOrWhiteSpace(c.UserId) && !c.UnitId.HasValue))
			{
				var person = Person(checkIn.UserId);
				person.Engaged = true;
				Seen(person, checkIn.Timestamp);
			}

			foreach (var person in people.Values.Where(p => p.UnitId.HasValue))
				person.UnitName = UnitName(input, person.UnitId.Value);

			data.Personnel = people.Values.OrderByDescending(p => p.Engaged).ThenBy(p => p.FirstOn ?? DateTime.MaxValue).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
		}

		private static CallSourceCommand BuildCommand(CallSourceInputs input)
		{
			var command = input.Command;
			if (command == null)
				return null;

			var summary = new CallSourceCommand
			{
				IncidentCommandId = command.IncidentCommandId,
				Name = Plain(command.Name),
				EstablishedOn = input.FirstCommandEstablishedOn ?? command.EstablishedOn,
				EstablishedByUserId = command.EstablishedByUserId,
				ClosedOn = command.ClosedOn
			};

			foreach (var commander in new[] { command.EstablishedByUserId, command.CurrentCommanderUserId }.Where(id => !string.IsNullOrWhiteSpace(id)))
			{
				if (!summary.CommanderUserIds.Contains(commander, StringComparer.OrdinalIgnoreCase))
					summary.CommanderUserIds.Add(commander);
			}

			summary.CommanderNames = summary.CommanderUserIds.Select(id => Name(input, id) ?? id).ToList();
			summary.TacticTimestamps = NerisTacticTimestamps.FromCommand(summary.EstablishedOn,
				input.Objectives.Where(o => o != null && o.Status == (int)TacticalObjectiveStatus.Complete).Select(o => (o.Name, o.CompletedOn)));

			// Mutual aid: ad-hoc resources from a named agency, and units or members lent by a linked Resgrid department.
			var aid = new Dictionary<string, CallSourceMutualAid>(StringComparer.OrdinalIgnoreCase);
			void Add(string agency, int? departmentId, string resource, DateTime at)
			{
				if (string.IsNullOrWhiteSpace(agency))
					return;
				var key = departmentId.HasValue ? "dept:" + departmentId.Value.ToString(CultureInfo.InvariantCulture) : agency.Trim();
				if (!aid.TryGetValue(key, out var row))
				{
					row = new CallSourceMutualAid { AgencyName = agency.Trim(), LinkedDepartmentId = departmentId, FirstOn = at };
					aid[key] = row;
				}
				if (!string.IsNullOrWhiteSpace(resource) && !row.ResourceNames.Contains(resource.Trim(), StringComparer.OrdinalIgnoreCase))
					row.ResourceNames.Add(resource.Trim());
				if (!row.FirstOn.HasValue || at < row.FirstOn.Value)
					row.FirstOn = at;
			}

			foreach (var unit in input.AdHocUnits.Where(u => u != null))
				Add(Plain(unit.ExternalAgencyName), null, Plain(unit.Name), unit.CreatedOn);
			foreach (var person in input.AdHocPersonnel.Where(p => p != null))
				Add(Plain(person.ExternalAgencyName), null, Plain(person.Name), person.CreatedOn);
			foreach (var assignment in input.Assignments.Where(a => a != null && (a.ResourceKind == (int)ResourceAssignmentKind.LinkedDeptUnit || a.ResourceKind == (int)ResourceAssignmentKind.LinkedDeptPersonnel)))
			{
				if (string.IsNullOrWhiteSpace(assignment.ResourceId) || !input.LinkedResourceDepartments.TryGetValue(assignment.ResourceId, out var departmentId))
					continue;
				input.DepartmentNames.TryGetValue(departmentId, out var departmentName);
				input.LinkedResourceNames.TryGetValue(assignment.ResourceId, out var resourceName);
				Add(departmentName ?? "Department " + departmentId.ToString(CultureInfo.InvariantCulture), departmentId, resourceName, assignment.AssignedOn);
			}

			summary.MutualAid = aid.Values.OrderBy(a => a.FirstOn ?? DateTime.MaxValue).ToList();
			return summary;
		}

		#endregion

		#region Helpers

		public static CallSourceMilestone UnitMilestone(int rawState, IReadOnlyDictionary<int, int> baseTypes)
		{
			switch (CallStatusLinkage.ResolveUnitStateKind(rawState, baseTypes))
			{
				case UnitStateTypes.Committed: return CallSourceMilestone.Dispatched;
				case UnitStateTypes.Responding:
				case UnitStateTypes.Enroute: return CallSourceMilestone.Enroute;
				case UnitStateTypes.OnScene: return CallSourceMilestone.OnScene;
				case UnitStateTypes.Staging: return CallSourceMilestone.Staging;
				case UnitStateTypes.Cancelled: return CallSourceMilestone.Cancelled;
				case UnitStateTypes.Released:
				case UnitStateTypes.Returning: return CallSourceMilestone.Cleared;
				case UnitStateTypes.Available: return CallSourceMilestone.InService;
				default: return CallSourceMilestone.None;
			}
		}

		public static CallSourceMilestone PersonnelMilestone(int rawStatus, IReadOnlyDictionary<int, int> baseTypes)
		{
			if (rawStatus <= CallStatusLinkage.MaxBuiltInStatusId)
			{
				switch ((ActionTypes)rawStatus)
				{
					case ActionTypes.Responding:
					case ActionTypes.RespondingToScene:
					case ActionTypes.RespondingToStation: return CallSourceMilestone.Enroute;
					case ActionTypes.OnScene: return CallSourceMilestone.OnScene;
					case ActionTypes.StandingBy:
					case ActionTypes.AvailableStation: return CallSourceMilestone.InService;
					default: return CallSourceMilestone.None;
				}
			}

			if (baseTypes == null || !baseTypes.TryGetValue(rawStatus, out var baseType))
				return CallSourceMilestone.None;

			switch ((ActionBaseTypes)baseType)
			{
				case ActionBaseTypes.Dispatched: return CallSourceMilestone.Dispatched;
				case ActionBaseTypes.Responding:
				case ActionBaseTypes.Enroute: return CallSourceMilestone.Enroute;
				case ActionBaseTypes.OnScene:
				case ActionBaseTypes.MadeContact:
				case ActionBaseTypes.Investigating:
				case ActionBaseTypes.AtPatient:
				case ActionBaseTypes.Searching: return CallSourceMilestone.OnScene;
				case ActionBaseTypes.Staging: return CallSourceMilestone.Staging;
				case ActionBaseTypes.Cleared:
				case ActionBaseTypes.Returning: return CallSourceMilestone.Cleared;
				case ActionBaseTypes.Available: return CallSourceMilestone.InService;
				default: return CallSourceMilestone.None;
			}
		}

		/// <summary>Free text from a source that may be sealed under Protected Data: a sealed or redacted value is left out, never shown.</summary>
		public static string Plain(string value)
		{
			if (string.IsNullOrWhiteSpace(value) || ProtectedDataEnvelope.HasEnvelopePrefix(value) || value == ProtectedDataEnvelope.RedactionValue)
				return null;
			return value.Trim();
		}

		private static string Name(CallSourceInputs input, string userId)
		{
			if (string.IsNullOrWhiteSpace(userId))
				return null;
			return input.Names.TryGetValue(userId, out var name) && !string.IsNullOrWhiteSpace(name) ? name : null;
		}

		private static string UnitName(CallSourceInputs input, int unitId)
		{
			return input.Units.TryGetValue(unitId, out var unit) && !string.IsNullOrWhiteSpace(unit?.Name) ? unit.Name : "#" + unitId.ToString(CultureInfo.InvariantCulture);
		}

		private static string BuiltInUnitText(int rawState)
		{
			return rawState <= CallStatusLinkage.MaxBuiltInStatusId && Enum.IsDefined(typeof(UnitStateTypes), rawState)
				? new UnitState { State = rawState }.GetStatusText()
				: "Status " + rawState.ToString(CultureInfo.InvariantCulture);
		}

		private static string BuiltInPersonnelText(ActionLog log)
		{
			return log.ActionTypeId <= CallStatusLinkage.MaxBuiltInStatusId && Enum.IsDefined(typeof(ActionTypes), log.ActionTypeId)
				? log.GetActionText()
				: "Status " + log.ActionTypeId.ToString(CultureInfo.InvariantCulture);
		}

		private static string CommandLabel(CommandLogEntryType type)
		{
			switch (type)
			{
				case CommandLogEntryType.CommandEstablished: return "Command established";
				case CommandLogEntryType.CommandTransferred: return "Command transferred";
				case CommandLogEntryType.CommandClosed: return "Command closed";
				case CommandLogEntryType.CommandReopened: return "Command reopened";
				case CommandLogEntryType.ResourceAssigned: return "Resource assigned";
				case CommandLogEntryType.ResourceMoved: return "Resource moved";
				case CommandLogEntryType.ResourceReleased: return "Resource released";
				case CommandLogEntryType.AdHocResourceCreated: return "Resource added";
				case CommandLogEntryType.RoleAssigned: return "Role assigned";
				case CommandLogEntryType.ParCritical: return "PAR overdue";
				case CommandLogEntryType.Note:
				case CommandLogEntryType.IncidentNoteAdded: return "Note";
				case CommandLogEntryType.NeedAdded: return "Need added";
				case CommandLogEntryType.NeedUpdated: return "Need updated";
				case CommandLogEntryType.NeedMet: return "Need met";
				case CommandLogEntryType.CheckIn: return "Check-in";
				default: return type.ToString();
			}
		}

		private static string JoinDetail(string first, string second)
		{
			var parts = new[] { first, second }.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
			return parts.Count == 0 ? null : string.Join(" · ", parts);
		}

		#endregion
	}
}
