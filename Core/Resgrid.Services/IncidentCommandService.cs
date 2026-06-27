using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Live incident-command management. Persistence-backed; every mutation appends a CommandLogEntry to the
	/// incident timeline. NOTE: queries currently filter department-scoped result sets by CallId in memory —
	/// a per-CallId repository query is a follow-up optimization. SignalR/Workflow event publication is wired
	/// in a later pass (§3.6 / §3.12).
	/// </summary>
	public class IncidentCommandService : IIncidentCommandService
	{
		private readonly IIncidentCommandRepository _incidentCommandRepository;
		private readonly ICommandStructureNodeRepository _commandStructureNodeRepository;
		private readonly IResourceAssignmentRepository _resourceAssignmentRepository;
		private readonly ITacticalObjectiveRepository _tacticalObjectiveRepository;
		private readonly IIncidentTimerRepository _incidentTimerRepository;
		private readonly IIncidentMapAnnotationRepository _incidentMapAnnotationRepository;
		private readonly ICommandLogEntryRepository _commandLogEntryRepository;
		private readonly ICommandTransferRepository _commandTransferRepository;
		private readonly ICommandsService _commandsService;
		private readonly ICallsService _callsService;
		private readonly ICheckInTimerService _checkInTimerService;
		private readonly IIncidentVoiceService _incidentVoiceService;
		private readonly IIncidentRoleAssignmentRepository _incidentRoleAssignmentRepository;
		private readonly IEventAggregator _eventAggregator;
		private readonly ICoreEventService _coreEventService;

		public IncidentCommandService(
			IIncidentCommandRepository incidentCommandRepository,
			ICommandStructureNodeRepository commandStructureNodeRepository,
			IResourceAssignmentRepository resourceAssignmentRepository,
			ITacticalObjectiveRepository tacticalObjectiveRepository,
			IIncidentTimerRepository incidentTimerRepository,
			IIncidentMapAnnotationRepository incidentMapAnnotationRepository,
			ICommandLogEntryRepository commandLogEntryRepository,
			ICommandTransferRepository commandTransferRepository,
			ICommandsService commandsService,
			ICallsService callsService,
			ICheckInTimerService checkInTimerService,
			IIncidentVoiceService incidentVoiceService,
			IIncidentRoleAssignmentRepository incidentRoleAssignmentRepository,
			IEventAggregator eventAggregator,
			ICoreEventService coreEventService)
		{
			_incidentCommandRepository = incidentCommandRepository;
			_commandStructureNodeRepository = commandStructureNodeRepository;
			_resourceAssignmentRepository = resourceAssignmentRepository;
			_tacticalObjectiveRepository = tacticalObjectiveRepository;
			_incidentTimerRepository = incidentTimerRepository;
			_incidentMapAnnotationRepository = incidentMapAnnotationRepository;
			_commandLogEntryRepository = commandLogEntryRepository;
			_commandTransferRepository = commandTransferRepository;
			_commandsService = commandsService;
			_callsService = callsService;
			_checkInTimerService = checkInTimerService;
			_incidentVoiceService = incidentVoiceService;
			_incidentRoleAssignmentRepository = incidentRoleAssignmentRepository;
			_eventAggregator = eventAggregator;
			_coreEventService = coreEventService;
		}

		#region Command lifecycle

		public async Task<IncidentCommand> EstablishCommandAsync(int departmentId, int callId, string userId, int? commandDefinitionId, CancellationToken cancellationToken = default(CancellationToken))
		{
			// The call must belong to the caller's department. CallId is an auto-increment integer (guessable),
			// so this prevents establishing command on another department's call.
			var call = await _callsService.GetCallByIdAsync(callId);
			if (call == null || call.DepartmentId != departmentId)
				return null;

			// Idempotent: if command is already established and active for this call, return it.
			var existing = await GetActiveCommandForCallAsync(departmentId, callId);
			if (existing != null)
				return existing;

			var command = new IncidentCommand
			{
				IncidentCommandId = Guid.NewGuid().ToString(),
				DepartmentId = departmentId,
				CallId = callId,
				SourceCommandDefinitionId = commandDefinitionId,
				EstablishedByUserId = userId,
				EstablishedOn = DateTime.UtcNow,
				CurrentCommanderUserId = userId,
				Status = (int)IncidentCommandStatus.Active
			};

			try
			{
				// Explicit insert: the GUID is pre-set, and SaveOrUpdateAsync would treat a non-empty IdType-1 id
				// as an UPDATE (zero rows) instead of an insert.
				command = await _incidentCommandRepository.InsertAsync(Touch(command), cancellationToken);
			}
			catch (Exception)
			{
				// The active-command check above is check-then-insert and races under concurrency; the partial
				// unique index (UX_IncidentCommands_Department_Call_Active) is the real guard. If we lost the race,
				// adopt the winner — same idempotent result as the check, rather than surfacing a 500.
				var winner = await GetActiveCommandForCallAsync(departmentId, callId);
				if (winner != null)
					return winner;
				throw;
			}

			await WriteLogAsync(command.IncidentCommandId, departmentId, callId, CommandLogEntryType.CommandEstablished, "Command established", userId, cancellationToken);

			// Enable personnel accountability (check-in timers) on the call when the department has a config.
			await EnableAccountabilityIfConfiguredAsync(departmentId, callId, userId, command.IncidentCommandId, cancellationToken);

			_eventAggregator.SendMessage<CommandEstablishedEvent>(new CommandEstablishedEvent { DepartmentId = departmentId, CallId = callId, IncidentCommandId = command.IncidentCommandId, EstablishedByUserId = userId });

			// Seed lanes from the command definition template, if one was supplied and its lanes were loaded.
			if (commandDefinitionId.HasValue)
			{
				var definition = await _commandsService.GetCommandByIdAsync(commandDefinitionId.Value);

				// The template must belong to the caller's department. CommandDefinitionId is an auto-increment
				// integer (guessable), so this prevents seeding from / disclosing another department's template.
				if (definition?.Assignments != null && definition.DepartmentId == departmentId)
				{
					foreach (var role in definition.Assignments.OrderBy(r => r.SortOrder))
					{
						var node = new CommandStructureNode
						{
							CommandStructureNodeId = Guid.NewGuid().ToString(),
							IncidentCommandId = command.IncidentCommandId,
							DepartmentId = departmentId,
							CallId = callId,
							NodeType = role.LaneType,
							Name = role.Name,
							SortOrder = role.SortOrder,
							SourceRoleId = role.CommandDefinitionRoleId
						};

						await _commandStructureNodeRepository.InsertAsync(Touch(node), cancellationToken);
					}
				}
			}

			return command;
		}

		public async Task<IncidentCommand> GetActiveCommandForCallAsync(int departmentId, int callId)
		{
			var items = await _incidentCommandRepository.GetAllByDepartmentIdAsync(departmentId);
			return items?.FirstOrDefault(x => x.CallId == callId && x.Status == (int)IncidentCommandStatus.Active);
		}

		public async Task<List<IncidentCommand>> GetActiveCommandsForDepartmentAsync(int departmentId)
		{
			var items = await _incidentCommandRepository.GetAllByDepartmentIdAsync(departmentId);
			if (items == null)
				return new List<IncidentCommand>();

			return items.Where(x => x.Status == (int)IncidentCommandStatus.Active).ToList();
		}

		public async Task<IncidentCommand> GetCommandByIdAsync(string incidentCommandId)
		{
			return await _incidentCommandRepository.GetByIdAsync(incidentCommandId);
		}

		public async Task<IncidentCommand> GetCommandForCallAsync(int departmentId, int callId)
		{
			var items = await _incidentCommandRepository.GetAllByDepartmentIdAsync(departmentId);
			return items?.Where(x => x.CallId == callId).OrderByDescending(x => x.EstablishedOn).FirstOrDefault();
		}

		public async Task<List<PersonnelCallCheckInStatus>> GetAccountabilityForCallAsync(int departmentId, int callId)
		{
			var call = await _callsService.GetCallByIdAsync(callId);
			if (call == null || call.DepartmentId != departmentId || !call.CheckInTimersEnabled)
				return new List<PersonnelCallCheckInStatus>();

			var statuses = await _checkInTimerService.GetCallPersonnelCheckInStatusesAsync(call);
			return statuses ?? new List<PersonnelCallCheckInStatus>();
		}

		public async Task<List<string>> EvaluateCriticalParAsync(int departmentId, int callId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var newlyCritical = new List<string>();

			// The call must belong to the caller's department and have accountability enabled.
			var call = await _callsService.GetCallByIdAsync(callId);
			if (call == null || call.DepartmentId != departmentId || !call.CheckInTimersEnabled)
				return newlyCritical;

			// Without an active command there is nothing to append the alert to (and no incident to alert on).
			var command = await GetActiveCommandForCallAsync(departmentId, callId);
			if (command == null)
				return newlyCritical;

			var statuses = await _checkInTimerService.GetCallPersonnelCheckInStatusesAsync(call);
			var critical = statuses?
				.Where(s => !string.IsNullOrWhiteSpace(s.UserId) && string.Equals(s.Status, "Critical", StringComparison.OrdinalIgnoreCase))
				.ToList() ?? new List<PersonnelCallCheckInStatus>();
			if (!critical.Any())
				return newlyCritical;

			// Timeline-based dedup: most-recent ParCritical marker per subject user on this call.
			var timeline = await GetTimelineForCallAsync(departmentId, callId);
			var lastMarkerByUser = timeline
				.Where(e => e.EntryType == (int)CommandLogEntryType.ParCritical && !string.IsNullOrWhiteSpace(e.UserId))
				.GroupBy(e => e.UserId)
				.ToDictionary(g => g.Key, g => g.Max(e => e.OccurredOn));

			foreach (var status in critical)
			{
				// A "Critical episode" begins at the member's last check-in (or when command was established if
				// they never checked in). A marker at/after that baseline means we already alerted this episode;
				// once they check in again the baseline moves past the marker and the next lapse re-alerts.
				var baseline = status.LastCheckIn ?? command.EstablishedOn;
				if (lastMarkerByUser.TryGetValue(status.UserId, out var lastMarker) && lastMarker >= baseline)
					continue;

				var overdueBy = Math.Abs(Math.Round(status.MinutesRemaining, 1));
				var who = string.IsNullOrWhiteSpace(status.FullName) ? status.UserId : status.FullName;

				// The marker (UserId = the SUBJECT member) is both the dedup record and — via WriteLogAsync —
				// the real-time IncidentCommandUpdated push that refreshes the board/PAR view.
				await WriteLogAsync(command.IncidentCommandId, departmentId, callId, CommandLogEntryType.ParCritical,
					$"PAR critical: {who} overdue for check-in by {overdueBy} min", status.UserId, cancellationToken);

				_eventAggregator.SendMessage<CriticalParDetectedEvent>(new CriticalParDetectedEvent
				{
					DepartmentId = departmentId,
					CallId = callId,
					UserId = status.UserId
				});

				newlyCritical.Add(status.UserId);
			}

			return newlyCritical;
		}

		private async Task EnableAccountabilityIfConfiguredAsync(int departmentId, int callId, string userId, string incidentCommandId, CancellationToken cancellationToken)
		{
			var configs = await _checkInTimerService.GetTimerConfigsForDepartmentAsync(departmentId);
			if (configs == null || !configs.Any(c => c.IsEnabled))
				return;

			var call = await _callsService.GetCallByIdAsync(callId);
			if (call == null || call.DepartmentId != departmentId || call.CheckInTimersEnabled)
				return;

			call.CheckInTimersEnabled = true;
			await _callsService.SaveCallAsync(call, cancellationToken);

			await WriteLogAsync(incidentCommandId, departmentId, callId, CommandLogEntryType.Note, "Personnel accountability check-in timers enabled", userId, cancellationToken);
		}

		public async Task<IncidentRoleAssignment> AssignIncidentRoleAsync(IncidentRoleAssignment assignment, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			// The parent incident command must belong to the caller's department; stamp the authoritative
			// CallId from it so this row can't be filed under a different call than its parent command.
			var command = await GetOwnedCommandAsync(assignment.IncidentCommandId, assignment.DepartmentId);
			if (command == null)
				return null;
			assignment.CallId = command.CallId;

			assignment.AssignedByUserId = userId;
			if (assignment.AssignedOn == default(DateTime))
				assignment.AssignedOn = DateTime.UtcNow;

			var (saved, _, rejected) = await UpsertOwnedAsync(_incidentRoleAssignmentRepository, assignment, assignment.DepartmentId,
				e => e.DepartmentId, (stored, incoming) =>
				{
					incoming.AssignedOn = stored.AssignedOn;
					incoming.AssignedByUserId = stored.AssignedByUserId;
					incoming.RemovedOn = stored.RemovedOn;
				}, cancellationToken);
			if (rejected)
				return null;
			assignment = saved;

			await WriteLogAsync(assignment.IncidentCommandId, assignment.DepartmentId, assignment.CallId, CommandLogEntryType.RoleAssigned, $"Role {(IncidentRoleType)assignment.RoleType} assigned", userId, cancellationToken);

			_eventAggregator.SendMessage<IncidentRoleAssignedEvent>(new IncidentRoleAssignedEvent { DepartmentId = assignment.DepartmentId, CallId = assignment.CallId, UserId = assignment.UserId, RoleType = assignment.RoleType });
			return assignment;
		}

		public async Task<bool> RemoveIncidentRoleAsync(int departmentId, string incidentRoleAssignmentId, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var assignment = await _incidentRoleAssignmentRepository.GetByIdAsync(incidentRoleAssignmentId);
			if (assignment == null || assignment.DepartmentId != departmentId)
				return false;

			assignment.RemovedOn = DateTime.UtcNow;
			await _incidentRoleAssignmentRepository.SaveOrUpdateAsync(Touch(assignment), cancellationToken);

			await WriteLogAsync(assignment.IncidentCommandId, assignment.DepartmentId, assignment.CallId, CommandLogEntryType.RoleRemoved, $"Role {(IncidentRoleType)assignment.RoleType} removed", userId, cancellationToken);
			return true;
		}

		public async Task<List<IncidentRoleAssignment>> GetIncidentRolesAsync(int departmentId, int callId)
		{
			var items = await _incidentRoleAssignmentRepository.GetAllByDepartmentIdAsync(departmentId);
			if (items == null)
				return new List<IncidentRoleAssignment>();

			return items.Where(x => x.CallId == callId && x.RemovedOn == null).ToList();
		}

		public async Task<IncidentCapabilities> GetCapabilitiesForUserAsync(int departmentId, int callId, string userId)
		{
			var caps = IncidentCapabilities.None;

			var command = await GetActiveCommandForCallAsync(departmentId, callId);
			if (command != null && (string.Equals(userId, command.CurrentCommanderUserId) || string.Equals(userId, command.EstablishedByUserId)))
				caps = IncidentCapabilities.All;

			var roles = await GetIncidentRolesAsync(departmentId, callId);
			foreach (var role in roles.Where(r => string.Equals(r.UserId, userId)))
				caps |= IncidentRoleCapabilityMap.GetCapabilities((IncidentRoleType)role.RoleType);

			return caps;
		}

		public async Task<IncidentCommandBoard> GetCommandBoardAsync(int departmentId, int callId)
		{
			var command = await GetActiveCommandForCallAsync(departmentId, callId);
			if (command == null)
				return null;

			// The board is the IC app's primary polled read, so piggyback the PAR sweep here to keep
			// accountability alerts flowing without a worker. Never let a sweep failure break the read.
			try
			{
				await EvaluateCriticalParAsync(departmentId, callId);
			}
			catch (Exception ex)
			{
				Resgrid.Framework.Logging.LogException(ex);
			}

			var board = new IncidentCommandBoard
			{
				Command = command,
				Nodes = await GetNodesForCallAsync(departmentId, callId),
				Assignments = (await GetAssignmentsForCallAsync(departmentId, callId)).Where(a => a.ReleasedOn == null).ToList(),
				Objectives = await GetObjectivesForCallAsync(departmentId, callId),
				Timers = await GetActiveTimersForCallAsync(departmentId, callId),
				Annotations = await GetAnnotationsForCallAsync(departmentId, callId),
				Accountability = await GetAccountabilityForCallAsync(departmentId, callId),
				Roles = await GetIncidentRolesAsync(departmentId, callId)
			};

			return board;
		}

		public async Task<IncidentCommandBundle> GetBundleForDepartmentAsync(int departmentId, bool includeAccountability = true)
		{
			// Capture the cursor before reading so the client's first incremental /Sync/Changes call doesn't miss a
			// row committed during this read (a re-returned row is harmless — the client upserts idempotently).
			var bundle = new IncidentCommandBundle { ServerTimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };

			var active = await GetActiveCommandsForDepartmentAsync(departmentId);
			if (active.Count == 0)
				return bundle;

			// Pull each board table ONCE for the whole department and index by CallId, instead of re-scanning every
			// table per incident. The per-call getters each do a full GetAllByDepartmentIdAsync, and GetCommandBoardAsync
			// additionally fires the write-side PAR sweep — so assembling N boards that way is O(active incidents ×
			// department size) plus N marker-writes / SignalR pushes. Doing it here keeps the bundle O(number of tables)
			// and side-effect free, which is what hurts departments with many open/active incidents.
			var nodes = ToCallLookup(await _commandStructureNodeRepository.GetAllByDepartmentIdAsync(departmentId), x => x.CallId);
			var assignments = ToCallLookup(await _resourceAssignmentRepository.GetAllByDepartmentIdAsync(departmentId), x => x.CallId);
			var objectives = ToCallLookup(await _tacticalObjectiveRepository.GetAllByDepartmentIdAsync(departmentId), x => x.CallId);
			var timers = ToCallLookup(await _incidentTimerRepository.GetAllByDepartmentIdAsync(departmentId), x => x.CallId);
			var annotations = ToCallLookup(await _incidentMapAnnotationRepository.GetAllByDepartmentIdAsync(departmentId), x => x.CallId);
			var roles = ToCallLookup(await _incidentRoleAssignmentRepository.GetAllByDepartmentIdAsync(departmentId), x => x.CallId);

			foreach (var command in active)
			{
				var callId = command.CallId;

				var board = new IncidentCommandBoard
				{
					Command = command,
					// These mirror the per-call getter filters exactly (DeletedOn / ReleasedOn / RemovedOn tombstones +
					// the active-timer rule), so the bundled board matches what GetCommandBoardAsync would return.
					Nodes = nodes[callId].Where(x => x.DeletedOn == null).OrderBy(x => x.SortOrder).ToList(),
					Assignments = assignments[callId].Where(x => x.ReleasedOn == null).ToList(),
					Objectives = objectives[callId].OrderBy(x => x.SortOrder).ToList(),
					Timers = timers[callId].Where(x => x.Status != (int)IncidentTimerStatus.Stopped).ToList(),
					Annotations = annotations[callId].Where(x => x.DeletedOn == null).ToList(),
					Roles = roles[callId].Where(x => x.RemovedOn == null).ToList()
				};

				// Accountability/PAR is the one per-incident read here, and it is READ-ONLY (no marker writes / SignalR
				// pushes — unlike GetCommandBoardAsync's sweep). A department with very many open incidents can opt out
				// via includeAccountability=false and fetch PAR per incident on demand.
				if (includeAccountability)
					board.Accountability = await GetAccountabilityForCallAsync(departmentId, callId);

				bundle.Boards.Add(board);
			}

			return bundle;
		}

		/// <summary>Indexes a department-wide row set by CallId; a missing key yields an empty sequence (no exception).</summary>
		private static ILookup<int, T> ToCallLookup<T>(IEnumerable<T> items, Func<T, int> callIdSelector)
			=> (items ?? Enumerable.Empty<T>()).ToLookup(callIdSelector);

		public async Task<IncidentCommandChanges> GetChangesSinceAsync(int departmentId, DateTime sinceUtc)
		{
			// Capture the cursor before reading so a row committed during the read is not missed next time (it may be
			// returned again on the next sync — harmless, the client upserts idempotently).
			var changes = new IncidentCommandChanges { ServerTimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };

			// On an initial full sync (since=0 → DateTime.MinValue) return EVERY row — including any with a null
			// ModifiedOn (e.g. rows created before the change-tracking column existed) — so the first pull is complete;
			// incremental syncs keep the strict "changed since the cursor" filter. Method-group conversion is
			// contravariance-aware, so this Func<IChangeTracked,bool> binds to each entity-typed Where(). Soft-deleted/
			// closed/released rows are intentionally surfaced (with their state columns) so the client reconciles them.
			var fullSync = sinceUtc == DateTime.MinValue;
			bool Changed(IChangeTracked e) => fullSync || (e.ModifiedOn.HasValue && e.ModifiedOn.Value > sinceUtc);

			var commands = await _incidentCommandRepository.GetAllByDepartmentIdAsync(departmentId);
			if (commands != null)
				changes.Commands = commands.Where(Changed).ToList();

			var nodes = await _commandStructureNodeRepository.GetAllByDepartmentIdAsync(departmentId);
			if (nodes != null)
				changes.Nodes = nodes.Where(Changed).ToList();

			var assignments = await _resourceAssignmentRepository.GetAllByDepartmentIdAsync(departmentId);
			if (assignments != null)
				changes.Assignments = assignments.Where(Changed).ToList();

			var objectives = await _tacticalObjectiveRepository.GetAllByDepartmentIdAsync(departmentId);
			if (objectives != null)
				changes.Objectives = objectives.Where(Changed).ToList();

			var timers = await _incidentTimerRepository.GetAllByDepartmentIdAsync(departmentId);
			if (timers != null)
				changes.Timers = timers.Where(Changed).ToList();

			var annotations = await _incidentMapAnnotationRepository.GetAllByDepartmentIdAsync(departmentId);
			if (annotations != null)
				changes.Annotations = annotations.Where(Changed).ToList();

			var roles = await _incidentRoleAssignmentRepository.GetAllByDepartmentIdAsync(departmentId);
			if (roles != null)
				changes.Roles = roles.Where(Changed).ToList();

			// The timeline is append-only (no ModifiedOn); its natural cursor is OccurredOn.
			var timeline = await _commandLogEntryRepository.GetAllByDepartmentIdAsync(departmentId);
			if (timeline != null)
				changes.TimelineEntries = timeline.Where(x => x.OccurredOn > sinceUtc).ToList();

			return changes;
		}

		public async Task<IncidentCommand> CloseCommandAsync(int departmentId, string incidentCommandId, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var command = await _incidentCommandRepository.GetByIdAsync(incidentCommandId);
			if (command == null || command.DepartmentId != departmentId)
				return null;

			command.Status = (int)IncidentCommandStatus.Closed;
			command.ClosedOn = DateTime.UtcNow;
			command = await _incidentCommandRepository.SaveOrUpdateAsync(Touch(command), cancellationToken);

			await WriteLogAsync(command.IncidentCommandId, command.DepartmentId, command.CallId, CommandLogEntryType.CommandClosed, "Command closed", userId, cancellationToken);

			// Auto-close any on-demand incident voice channels for this call.
			await _incidentVoiceService.CloseIncidentChannelsForCallAsync(command.DepartmentId, command.CallId, userId, cancellationToken);

			_eventAggregator.SendMessage<IncidentClosedEvent>(new IncidentClosedEvent { DepartmentId = command.DepartmentId, CallId = command.CallId, IncidentCommandId = command.IncidentCommandId });
			return command;
		}

		public async Task<CommandTransfer> TransferCommandAsync(int departmentId, string incidentCommandId, string fromUserId, string toUserId, string notes, CancellationToken cancellationToken = default(CancellationToken))
		{
			var command = await _incidentCommandRepository.GetByIdAsync(incidentCommandId);
			if (command == null || command.DepartmentId != departmentId)
				return null;

			command.CurrentCommanderUserId = toUserId;
			await _incidentCommandRepository.SaveOrUpdateAsync(Touch(command), cancellationToken);

			var transfer = new CommandTransfer
			{
				CommandTransferId = Guid.NewGuid().ToString(),
				IncidentCommandId = incidentCommandId,
				DepartmentId = command.DepartmentId,
				CallId = command.CallId,
				FromUserId = fromUserId,
				ToUserId = toUserId,
				TransferredOn = DateTime.UtcNow,
				Notes = notes
			};
			transfer = await _commandTransferRepository.InsertAsync(transfer, cancellationToken);

			await WriteLogAsync(command.IncidentCommandId, command.DepartmentId, command.CallId, CommandLogEntryType.CommandTransferred, "Command transferred", fromUserId, cancellationToken);

			_eventAggregator.SendMessage<CommandTransferredEvent>(new CommandTransferredEvent { DepartmentId = command.DepartmentId, CallId = command.CallId, IncidentCommandId = incidentCommandId, FromUserId = fromUserId, ToUserId = toUserId });
			return transfer;
		}

		public async Task<IncidentCommand> UpdateActionPlanAsync(int departmentId, string incidentCommandId, string actionPlan, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var command = await _incidentCommandRepository.GetByIdAsync(incidentCommandId);
			if (command == null || command.DepartmentId != departmentId)
				return null;

			command.IncidentActionPlan = actionPlan;
			command = await _incidentCommandRepository.SaveOrUpdateAsync(Touch(command), cancellationToken);

			await WriteLogAsync(command.IncidentCommandId, command.DepartmentId, command.CallId, CommandLogEntryType.Note, "Incident action plan updated", userId, cancellationToken);
			return command;
		}

		#endregion Command lifecycle

		#region Structure (lanes)

		public async Task<CommandStructureNode> SaveNodeAsync(CommandStructureNode node, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			// The parent incident command must belong to the caller's department (node.DepartmentId is set
			// from the authenticated claim by the controller); stamp the authoritative CallId from it so this
			// row can't be filed under a different call than its parent command.
			var command = await GetOwnedCommandAsync(node.IncidentCommandId, node.DepartmentId);
			if (command == null)
				return null;
			node.CallId = command.CallId;

			var (saved, isNew, rejected) = await UpsertOwnedAsync(_commandStructureNodeRepository, node, node.DepartmentId,
				e => e.DepartmentId, (stored, incoming) => incoming.DeletedOn = stored.DeletedOn, cancellationToken);
			if (rejected)
				return null;
			node = saved;

			await WriteLogAsync(node.IncidentCommandId, node.DepartmentId, node.CallId,
				isNew ? CommandLogEntryType.NodeAdded : CommandLogEntryType.NodeUpdated,
				$"Lane '{node.Name}' {(isNew ? "added" : "updated")}", userId, cancellationToken);
			return node;
		}

		public async Task<bool> DeleteNodeAsync(int departmentId, string commandStructureNodeId, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var node = await _commandStructureNodeRepository.GetByIdAsync(commandStructureNodeId);
			if (node == null || node.DepartmentId != departmentId)
				return false;

			// Soft-delete (tombstone) rather than hard-delete so the removal propagates to offline clients on the
			// next delta sync; ModifiedOn is stamped so the change is picked up by the "changed since" query.
			node.DeletedOn = DateTime.UtcNow;
			await _commandStructureNodeRepository.SaveOrUpdateAsync(Touch(node), cancellationToken);

			await WriteLogAsync(node.IncidentCommandId, node.DepartmentId, node.CallId, CommandLogEntryType.NodeRemoved, $"Lane '{node.Name}' removed", userId, cancellationToken);
			return true;
		}

		public async Task<List<CommandStructureNode>> GetNodesForCallAsync(int departmentId, int callId)
		{
			var items = await _commandStructureNodeRepository.GetAllByDepartmentIdAsync(departmentId);
			if (items == null)
				return new List<CommandStructureNode>();

			return items.Where(x => x.CallId == callId && x.DeletedOn == null).OrderBy(x => x.SortOrder).ToList();
		}

		#endregion Structure (lanes)

		#region Resource assignments

		public async Task<ResourceAssignment> AssignResourceAsync(ResourceAssignment assignment, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			// The parent incident command must belong to the caller's department; stamp the authoritative
			// CallId from it so this row can't be filed under a different call than its parent command.
			var command = await GetOwnedCommandAsync(assignment.IncidentCommandId, assignment.DepartmentId);
			if (command == null)
				return null;
			assignment.CallId = command.CallId;

			if (assignment.AssignedOn == default(DateTime))
				assignment.AssignedOn = DateTime.UtcNow;
			assignment.AssignedByUserId = userId;

			var (saved, _, rejected) = await UpsertOwnedAsync(_resourceAssignmentRepository, assignment, assignment.DepartmentId,
				e => e.DepartmentId, (stored, incoming) =>
				{
					incoming.AssignedOn = stored.AssignedOn;
					incoming.AssignedByUserId = stored.AssignedByUserId;
					incoming.ReleasedOn = stored.ReleasedOn;
				}, cancellationToken);
			if (rejected)
				return null;
			assignment = saved;

			await WriteLogAsync(assignment.IncidentCommandId, assignment.DepartmentId, assignment.CallId, CommandLogEntryType.ResourceAssigned, "Resource assigned", userId, cancellationToken);

			_eventAggregator.SendMessage<IncidentResourceAssignedEvent>(new IncidentResourceAssignedEvent { DepartmentId = assignment.DepartmentId, CallId = assignment.CallId, IncidentCommandId = assignment.IncidentCommandId, ResourceKind = assignment.ResourceKind, ResourceId = assignment.ResourceId });
			return assignment;
		}

		public async Task<ResourceAssignment> MoveResourceAsync(int departmentId, string resourceAssignmentId, string targetNodeId, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var assignment = await _resourceAssignmentRepository.GetByIdAsync(resourceAssignmentId);
			if (assignment == null || assignment.DepartmentId != departmentId)
				return null;

			// The target lane must exist and live on the SAME incident (department + call) as the assignment;
			// otherwise the move would point the resource at a non-existent/foreign lane and corrupt the board.
			var targetNode = await _commandStructureNodeRepository.GetByIdAsync(targetNodeId);
			if (targetNode == null || targetNode.DepartmentId != departmentId || targetNode.CallId != assignment.CallId)
				return null;

			assignment.CommandStructureNodeId = targetNodeId;
			assignment = await _resourceAssignmentRepository.SaveOrUpdateAsync(Touch(assignment), cancellationToken);

			await WriteLogAsync(assignment.IncidentCommandId, assignment.DepartmentId, assignment.CallId, CommandLogEntryType.ResourceMoved, "Resource moved", userId, cancellationToken);
			return assignment;
		}

		public async Task<bool> ReleaseResourceAsync(int departmentId, string resourceAssignmentId, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var assignment = await _resourceAssignmentRepository.GetByIdAsync(resourceAssignmentId);
			if (assignment == null || assignment.DepartmentId != departmentId)
				return false;

			assignment.ReleasedOn = DateTime.UtcNow;
			await _resourceAssignmentRepository.SaveOrUpdateAsync(Touch(assignment), cancellationToken);

			await WriteLogAsync(assignment.IncidentCommandId, assignment.DepartmentId, assignment.CallId, CommandLogEntryType.ResourceReleased, "Resource released", userId, cancellationToken);

			_eventAggregator.SendMessage<IncidentResourceReleasedEvent>(new IncidentResourceReleasedEvent { DepartmentId = assignment.DepartmentId, CallId = assignment.CallId, ResourceAssignmentId = assignment.ResourceAssignmentId });
			return true;
		}

		public async Task<List<ResourceAssignment>> GetAssignmentsForCallAsync(int departmentId, int callId)
		{
			var items = await _resourceAssignmentRepository.GetAllByDepartmentIdAsync(departmentId);
			if (items == null)
				return new List<ResourceAssignment>();

			return items.Where(x => x.CallId == callId).ToList();
		}

		#endregion Resource assignments

		#region Objectives

		public async Task<TacticalObjective> SaveObjectiveAsync(TacticalObjective objective, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			// The parent incident command must belong to the caller's department; stamp the authoritative
			// CallId from it so this row can't be filed under a different call than its parent command.
			var command = await GetOwnedCommandAsync(objective.IncidentCommandId, objective.DepartmentId);
			if (command == null)
				return null;
			objective.CallId = command.CallId;

			var (saved, isNew, rejected) = await UpsertOwnedAsync(_tacticalObjectiveRepository, objective, objective.DepartmentId,
				e => e.DepartmentId, (stored, incoming) =>
				{
					// Completion is owned by CompleteObjectiveAsync; a Save (edit/replay) must not reset it.
					incoming.Status = stored.Status;
					incoming.CompletedByUserId = stored.CompletedByUserId;
					incoming.CompletedOn = stored.CompletedOn;
				}, cancellationToken);
			if (rejected)
				return null;
			objective = saved;

			if (isNew)
				await WriteLogAsync(objective.IncidentCommandId, objective.DepartmentId, objective.CallId, CommandLogEntryType.ObjectiveAdded, $"Objective '{objective.Name}' added", userId, cancellationToken);

			return objective;
		}

		public async Task<TacticalObjective> CompleteObjectiveAsync(int departmentId, string tacticalObjectiveId, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var objective = await _tacticalObjectiveRepository.GetByIdAsync(tacticalObjectiveId);
			if (objective == null || objective.DepartmentId != departmentId)
				return null;

			objective.Status = (int)TacticalObjectiveStatus.Complete;
			objective.CompletedByUserId = userId;
			objective.CompletedOn = DateTime.UtcNow;
			objective = await _tacticalObjectiveRepository.SaveOrUpdateAsync(Touch(objective), cancellationToken);

			await WriteLogAsync(objective.IncidentCommandId, objective.DepartmentId, objective.CallId, CommandLogEntryType.ObjectiveCompleted, $"Objective '{objective.Name}' completed", userId, cancellationToken);

			_eventAggregator.SendMessage<IncidentObjectiveCompletedEvent>(new IncidentObjectiveCompletedEvent { DepartmentId = objective.DepartmentId, CallId = objective.CallId, IncidentCommandId = objective.IncidentCommandId, TacticalObjectiveId = objective.TacticalObjectiveId, Name = objective.Name });
			return objective;
		}

		public async Task<List<TacticalObjective>> GetObjectivesForCallAsync(int departmentId, int callId)
		{
			var items = await _tacticalObjectiveRepository.GetAllByDepartmentIdAsync(departmentId);
			if (items == null)
				return new List<TacticalObjective>();

			return items.Where(x => x.CallId == callId).OrderBy(x => x.SortOrder).ToList();
		}

		#endregion Objectives

		#region Timers

		public async Task<IncidentTimer> StartTimerAsync(IncidentTimer timer, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			// The parent incident command must belong to the caller's department; stamp the authoritative
			// CallId from it so this row can't be filed under a different call than its parent command.
			var command = await GetOwnedCommandAsync(timer.IncidentCommandId, timer.DepartmentId);
			if (command == null)
				return null;
			timer.CallId = command.CallId;

			timer.StartedOn = DateTime.UtcNow;
			timer.Status = (int)IncidentTimerStatus.Running;
			if (timer.IntervalSeconds > 0)
				timer.NextDueOn = timer.StartedOn.AddSeconds(timer.IntervalSeconds);

			var (saved, _, rejected) = await UpsertOwnedAsync(_incidentTimerRepository, timer, timer.DepartmentId,
				e => e.DepartmentId, (stored, incoming) =>
				{
					// Existing id => a replayed start; keep the original run state rather than restarting the timer.
					incoming.StartedOn = stored.StartedOn;
					incoming.Status = stored.Status;
					incoming.NextDueOn = stored.NextDueOn;
					incoming.AcknowledgedOn = stored.AcknowledgedOn;
				}, cancellationToken);
			if (rejected)
				return null;
			timer = saved;

			await WriteLogAsync(timer.IncidentCommandId, timer.DepartmentId, timer.CallId, CommandLogEntryType.TimerStarted, $"Timer '{timer.Name}' started", userId, cancellationToken);
			return timer;
		}

		public async Task<IncidentTimer> AcknowledgeTimerAsync(int departmentId, string incidentTimerId, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var timer = await _incidentTimerRepository.GetByIdAsync(incidentTimerId);
			if (timer == null || timer.DepartmentId != departmentId)
				return null;

			timer.AcknowledgedOn = DateTime.UtcNow;
			timer.Status = (int)IncidentTimerStatus.Acknowledged;
			if (timer.IntervalSeconds > 0)
				timer.NextDueOn = timer.AcknowledgedOn.Value.AddSeconds(timer.IntervalSeconds);

			timer = await _incidentTimerRepository.SaveOrUpdateAsync(Touch(timer), cancellationToken);

			await WriteLogAsync(timer.IncidentCommandId, timer.DepartmentId, timer.CallId, CommandLogEntryType.TimerAcknowledged, $"Timer '{timer.Name}' acknowledged", userId, cancellationToken);
			return timer;
		}

		public async Task<List<IncidentTimer>> GetActiveTimersForCallAsync(int departmentId, int callId)
		{
			var items = await _incidentTimerRepository.GetAllByDepartmentIdAsync(departmentId);
			if (items == null)
				return new List<IncidentTimer>();

			return items.Where(x => x.CallId == callId && x.Status != (int)IncidentTimerStatus.Stopped).ToList();
		}

		#endregion Timers

		#region Map annotations

		public async Task<IncidentMapAnnotation> SaveAnnotationAsync(IncidentMapAnnotation annotation, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			// The parent incident command must belong to the caller's department; stamp the authoritative
			// CallId from it so this row can't be filed under a different call than its parent command.
			var command = await GetOwnedCommandAsync(annotation.IncidentCommandId, annotation.DepartmentId);
			if (command == null)
				return null;
			annotation.CallId = command.CallId;

			if (annotation.CreatedOn == default(DateTime))
				annotation.CreatedOn = DateTime.UtcNow;
			if (string.IsNullOrWhiteSpace(annotation.CreatedByUserId))
				annotation.CreatedByUserId = userId;

			var (saved, isNew, rejected) = await UpsertOwnedAsync(_incidentMapAnnotationRepository, annotation, annotation.DepartmentId,
				e => e.DepartmentId, (stored, incoming) =>
				{
					incoming.CreatedOn = stored.CreatedOn;
					incoming.CreatedByUserId = stored.CreatedByUserId;
					incoming.DeletedOn = stored.DeletedOn;
				}, cancellationToken);
			if (rejected)
				return null;
			annotation = saved;

			if (isNew)
				await WriteLogAsync(annotation.IncidentCommandId, annotation.DepartmentId, annotation.CallId, CommandLogEntryType.AnnotationAdded, "Map annotation added", userId, cancellationToken);

			return annotation;
		}

		public async Task<bool> DeleteAnnotationAsync(int departmentId, string incidentMapAnnotationId, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var annotation = await _incidentMapAnnotationRepository.GetByIdAsync(incidentMapAnnotationId);
			if (annotation == null || annotation.DepartmentId != departmentId)
				return false;

			annotation.DeletedOn = DateTime.UtcNow;
			await _incidentMapAnnotationRepository.SaveOrUpdateAsync(Touch(annotation), cancellationToken);

			await WriteLogAsync(annotation.IncidentCommandId, annotation.DepartmentId, annotation.CallId, CommandLogEntryType.AnnotationRemoved, "Map annotation removed", userId, cancellationToken);
			return true;
		}

		public async Task<List<IncidentMapAnnotation>> GetAnnotationsForCallAsync(int departmentId, int callId)
		{
			var items = await _incidentMapAnnotationRepository.GetAllByDepartmentIdAsync(departmentId);
			if (items == null)
				return new List<IncidentMapAnnotation>();

			return items.Where(x => x.CallId == callId && x.DeletedOn == null).ToList();
		}

		#endregion Map annotations

		#region Timeline

		public async Task<List<CommandLogEntry>> GetTimelineForCallAsync(int departmentId, int callId)
		{
			var items = await _commandLogEntryRepository.GetAllByDepartmentIdAsync(departmentId);
			if (items == null)
				return new List<CommandLogEntry>();

			return items.Where(x => x.CallId == callId).OrderBy(x => x.OccurredOn).ToList();
		}

		public async Task<CommandLogEntry> AddLogEntryAsync(string incidentCommandId, int departmentId, int callId, CommandLogEntryType type, string description, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			return await WriteLogAsync(incidentCommandId, departmentId, callId, type, description, userId, cancellationToken);
		}

		#endregion Timeline

		#region Private helpers

		/// <summary>
		/// Stamps the offline-sync change cursor on an entity. Called on every insert and update so the delta
		/// endpoint can surface the row as "changed since" and reconnect conflict resolution can compare write
		/// times (last-write-wins). See docs/architecture/offline-first-architecture.md.
		/// </summary>
		private static T Touch<T>(T entity) where T : IChangeTracked
		{
			entity.ModifiedOn = DateTime.UtcNow;
			return entity;
		}

		/// <summary>
		/// Idempotent upsert for an owned child entity. Create-vs-update is resolved by the entity id's EXISTENCE
		/// (not merely whether an id was supplied), which is what makes offline replay safe:
		///   • no id, or a client-supplied id that does not exist yet -> INSERT with that (or a generated) GUID, so
		///     an offline-created row replays without duplicating;
		///   • id already present -> it must belong to <paramref name="departmentId"/> (else rejected) and is
		///     UPDATED, with <paramref name="preserve"/> copying server-owned fields off the stored row so a
		///     replayed create payload cannot clobber them.
		/// Returns rejected=true only for a foreign-department row. A plain SaveOrUpdateAsync cannot do the create
		/// here: for string-GUID (IdType 1) entities it only inserts when the id is blank and otherwise issues a
		/// blind UPDATE, so a client-supplied PK would silently update zero rows. See offline-first-architecture.md.
		/// </summary>
		private static async Task<(T entity, bool isNew, bool rejected)> UpsertOwnedAsync<T>(
			IRepository<T> repository, T entity, int departmentId, Func<T, int> departmentOf,
			Action<T, T> preserve, CancellationToken cancellationToken) where T : class, IEntity, IChangeTracked
		{
			var id = entity.IdValue?.ToString();

			T stored = null;
			if (!string.IsNullOrWhiteSpace(id))
			{
				stored = await repository.GetByIdAsync(id);
				if (stored != null && departmentOf(stored) != departmentId)
					return (null, false, true);
			}

			if (stored == null)
			{
				if (string.IsNullOrWhiteSpace(id))
					entity.IdValue = Guid.NewGuid().ToString();

				return (await repository.InsertAsync(Touch(entity), cancellationToken), true, false);
			}

			preserve?.Invoke(stored, entity);
			return (await repository.SaveOrUpdateAsync(Touch(entity), cancellationToken), false, false);
		}

		/// <summary>
		/// Loads the parent incident command and returns it only if it belongs to the given department (else null).
		/// Gates create/update of child entities AND supplies the authoritative CallId to stamp onto them — a caller
		/// must not be trusted to supply a CallId that matches the parent command.
		/// </summary>
		private async Task<IncidentCommand> GetOwnedCommandAsync(string incidentCommandId, int departmentId)
		{
			if (string.IsNullOrWhiteSpace(incidentCommandId))
				return null;

			var command = await _incidentCommandRepository.GetByIdAsync(incidentCommandId);
			return command != null && command.DepartmentId == departmentId ? command : null;
		}

		private async Task<CommandLogEntry> WriteLogAsync(string incidentCommandId, int departmentId, int callId, CommandLogEntryType type, string description, string userId, CancellationToken cancellationToken)
		{
			var entry = new CommandLogEntry
			{
				CommandLogEntryId = Guid.NewGuid().ToString(),
				IncidentCommandId = incidentCommandId,
				DepartmentId = departmentId,
				CallId = callId,
				EntryType = (int)type,
				Description = description,
				UserId = userId,
				OccurredOn = DateTime.UtcNow
			};

			var saved = await _commandLogEntryRepository.InsertAsync(entry, cancellationToken);

			// Real-time: every command mutation flows through here, so push one board-changed signal.
			await _coreEventService.IncidentCommandUpdatedAsync(departmentId, callId);
			return saved;
		}

		#endregion Private helpers
	}
}
