using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
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
		private readonly IUnitsService _unitsService;
		private readonly IPersonnelRolesService _personnelRolesService;
		private readonly IIncidentNoteRepository _incidentNoteRepository;
		private readonly IIncidentAttachmentRepository _incidentAttachmentRepository;
		private readonly IIncidentWeatherProvider _incidentWeatherProvider;
		private readonly IIncidentNeedRepository _incidentNeedRepository;
		private readonly IUserProfileService _userProfileService;
		private readonly IIncidentCommandNotificationService _incidentCommandNotificationService;

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
			ICoreEventService coreEventService,
			IUnitsService unitsService,
			IPersonnelRolesService personnelRolesService,
			IIncidentNoteRepository incidentNoteRepository,
			IIncidentAttachmentRepository incidentAttachmentRepository,
			IIncidentWeatherProvider incidentWeatherProvider,
			IIncidentNeedRepository incidentNeedRepository,
			IUserProfileService userProfileService,
			IIncidentCommandNotificationService incidentCommandNotificationService)
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
			_unitsService = unitsService;
			_personnelRolesService = personnelRolesService;
			_incidentNoteRepository = incidentNoteRepository;
			_incidentAttachmentRepository = incidentAttachmentRepository;
			_incidentWeatherProvider = incidentWeatherProvider;
			_incidentNeedRepository = incidentNeedRepository;
			_userProfileService = userProfileService;
			_incidentCommandNotificationService = incidentCommandNotificationService;
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

			// Resolve the board template. An explicitly supplied id wins (must belong to the caller's
			// department — CommandDefinitionId is an auto-increment integer and guessable). Otherwise fall
			// back to the department's pre-configured board for the call's type, then to the "Any Call Type"
			// definition, so establishing command auto-applies the department's default board layout.
			CommandDefinition definition = null;
			if (commandDefinitionId.HasValue)
			{
				definition = await _commandsService.GetCommandByIdAsync(commandDefinitionId.Value);
				if (definition == null || definition.DepartmentId != departmentId)
					definition = null;
			}
			else
			{
				definition = await _commandsService.GetCommandForCallTypeAsync(departmentId, await ResolveCallTypeIdAsync(call));
			}

			var command = new IncidentCommand
			{
				IncidentCommandId = Guid.NewGuid().ToString(),
				DepartmentId = departmentId,
				CallId = callId,
				SourceCommandDefinitionId = definition?.CommandDefinitionId,
				EstablishedByUserId = userId,
				EstablishedOn = DateTime.UtcNow,
				CurrentCommanderUserId = userId,
				PublicShareEnabled = false,
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

			// Seed lanes from the resolved template so the board opens with the department's
			// pre-configured default layout (per-incident editable afterwards).
			if (definition?.Assignments != null)
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
						Color = role.Color,
						SortOrder = role.SortOrder,
						SourceRoleId = role.CommandDefinitionRoleId,
						MinUnitPersonnel = role.MinUnitPersonnel,
						MaxUnitPersonnel = role.MaxUnitPersonnel,
						MinUnits = role.MinUnits,
						MaxUnits = role.MaxUnits,
						MinTimeInRole = role.MinTimeInRole,
						MaxTimeInRole = role.MaxTimeInRole,
						ForceRequirements = role.ForceRequirements
					};

					await _commandStructureNodeRepository.InsertAsync(Touch(node), cancellationToken);
				}
			}

			// Seed the template's interval timer (CommandDefinition.Timer/TimerMinutes) as a running
			// incident-scoped benchmark timer.
			if (definition != null && definition.Timer && definition.TimerMinutes > 0)
			{
				await StartTimerAsync(new IncidentTimer
				{
					IncidentTimerId = Guid.NewGuid().ToString(),
					IncidentCommandId = command.IncidentCommandId,
					DepartmentId = departmentId,
					TimerType = (int)IncidentTimerType.Benchmark,
					ScopeType = (int)IncidentTimerScopeType.Incident,
					Name = string.IsNullOrWhiteSpace(definition.Name) ? "Command Timer" : $"{definition.Name} Timer",
					IntervalSeconds = definition.TimerMinutes * 60
				}, userId, cancellationToken);
			}

			return command;
		}

		/// <summary>
		/// Maps a call's free-text Type (calls store the call-type NAME, not the id) back to the department's
		/// CallTypeId so the matching command definition template can be resolved. Null when the call has no
		/// type or it no longer matches a configured call type.
		/// </summary>
		private async Task<int?> ResolveCallTypeIdAsync(Call call)
		{
			if (string.IsNullOrWhiteSpace(call?.Type))
				return null;

			var types = await _callsService.GetCallTypesForDepartmentAsync(call.DepartmentId);
			var match = types?.FirstOrDefault(x => string.Equals(x.Type, call.Type, StringComparison.OrdinalIgnoreCase));
			return match?.CallTypeId;
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
				Needs = await GetNeedsForCallAsync(departmentId, callId),
				Timers = await GetActiveTimersForCallAsync(departmentId, callId),
				Annotations = await GetAnnotationsForCallAsync(departmentId, callId),
				Accountability = await GetAccountabilityForCallAsync(departmentId, callId),
				Roles = await GetIncidentRolesAsync(departmentId, callId),
				Notes = await GetNotesForCallAsync(departmentId, callId),
				Attachments = await GetAttachmentsForCallAsync(departmentId, callId)
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
			var needs = ToCallLookup(await _incidentNeedRepository.GetAllByDepartmentIdAsync(departmentId), x => x.CallId);
			var timers = ToCallLookup(await _incidentTimerRepository.GetAllByDepartmentIdAsync(departmentId), x => x.CallId);
			var annotations = ToCallLookup(await _incidentMapAnnotationRepository.GetAllByDepartmentIdAsync(departmentId), x => x.CallId);
			var roles = ToCallLookup(await _incidentRoleAssignmentRepository.GetAllByDepartmentIdAsync(departmentId), x => x.CallId);
			var notes = ToCallLookup(await _incidentNoteRepository.GetAllByDepartmentIdAsync(departmentId), x => x.CallId);
			var attachments = ToCallLookup(await _incidentAttachmentRepository.GetAllMetadataByDepartmentIdAsync(departmentId), x => x.CallId);

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
					Needs = needs[callId].Where(x => x.Status != (int)IncidentNeedStatus.Cancelled).OrderBy(x => x.SortOrder).ToList(),
					Timers = timers[callId].Where(x => x.Status != (int)IncidentTimerStatus.Stopped).ToList(),
					Annotations = annotations[callId].Where(x => x.DeletedOn == null).ToList(),
					Roles = roles[callId].Where(x => x.RemovedOn == null).ToList(),
					Notes = notes[callId].Where(x => x.DeletedOn == null).OrderBy(x => x.CreatedOn).ToList(),
					Attachments = attachments[callId].Where(x => x.DeletedOn == null).OrderBy(x => x.UploadedOn).ToList()
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

			var needs = await _incidentNeedRepository.GetAllByDepartmentIdAsync(departmentId);
			if (needs != null)
				changes.Needs = needs.Where(Changed).ToList();

			var timers = await _incidentTimerRepository.GetAllByDepartmentIdAsync(departmentId);
			if (timers != null)
				changes.Timers = timers.Where(Changed).ToList();

			var annotations = await _incidentMapAnnotationRepository.GetAllByDepartmentIdAsync(departmentId);
			if (annotations != null)
				changes.Annotations = annotations.Where(Changed).ToList();

			var roles = await _incidentRoleAssignmentRepository.GetAllByDepartmentIdAsync(departmentId);
			if (roles != null)
				changes.Roles = roles.Where(Changed).ToList();

			var notes = await _incidentNoteRepository.GetAllByDepartmentIdAsync(departmentId);
			if (notes != null)
				changes.Notes = notes.Where(Changed).ToList();

			var attachments = await _incidentAttachmentRepository.GetAllMetadataByDepartmentIdAsync(departmentId);
			if (attachments != null)
				changes.Attachments = attachments.Where(Changed).ToList();

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

			await _incidentCommandNotificationService.NotifyCommandTransferredAsync(command, fromUserId, toUserId, cancellationToken);
			return transfer;
		}

		public async Task<IncidentCommand> UpdateActionPlanAsync(int departmentId, string incidentCommandId, string actionPlan, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var command = await _incidentCommandRepository.GetByIdAsync(incidentCommandId);
			if (command == null || command.DepartmentId != departmentId)
				return null;

			command.IncidentActionPlan = actionPlan;
			command = await _incidentCommandRepository.SaveOrUpdateAsync(Touch(command), cancellationToken);

			await WriteLogAsync(command.IncidentCommandId, command.DepartmentId, command.CallId, CommandLogEntryType.ActionPlanUpdated, "Incident action plan updated", userId, cancellationToken);
			_eventAggregator.SendMessage<IncidentActionPlanUpdatedEvent>(new IncidentActionPlanUpdatedEvent
			{
				DepartmentId = command.DepartmentId,
				CallId = command.CallId,
				IncidentCommandId = command.IncidentCommandId,
				ActionPlan = actionPlan,
				UpdatedByUserId = userId
			});
			return command;
		}

		public async Task<IncidentCommand> UpdateCommandPostAsync(int departmentId, string incidentCommandId, string latitude, string longitude, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (!TryParseCoordinates(latitude, longitude, out var latitudeValue, out var longitudeValue))
				throw new ArgumentException("A valid latitude and longitude are required.");

			var command = await GetOwnedCommandAsync(incidentCommandId, departmentId);
			if (command == null)
				return null;

			command.CommandPostLatitude = latitudeValue.ToString("0.######", CultureInfo.InvariantCulture);
			command.CommandPostLongitude = longitudeValue.ToString("0.######", CultureInfo.InvariantCulture);
			command = await _incidentCommandRepository.SaveOrUpdateAsync(Touch(command), cancellationToken);

			await WriteLogAsync(command.IncidentCommandId, command.DepartmentId, command.CallId, CommandLogEntryType.CommandPostUpdated, "Command post location updated", userId, cancellationToken);
			_eventAggregator.SendMessage<IncidentCommandPostUpdatedEvent>(new IncidentCommandPostUpdatedEvent
			{
				DepartmentId = command.DepartmentId,
				CallId = command.CallId,
				IncidentCommandId = command.IncidentCommandId,
				Latitude = command.CommandPostLatitude,
				Longitude = command.CommandPostLongitude,
				UpdatedByUserId = userId
			});
			return command;
		}

		public async Task<IncidentCommand> UpdateCommandDetailsAsync(int departmentId, string incidentCommandId, DateTime? estimatedEndOn, string importantInformation, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var command = await GetOwnedCommandAsync(incidentCommandId, departmentId);
			if (command == null)
				return null;

			command.EstimatedEndOn = estimatedEndOn;
			command.ImportantInformation = TrimToLength(importantInformation, 8000);
			command = await _incidentCommandRepository.SaveOrUpdateAsync(Touch(command), cancellationToken);

			await WriteLogAsync(command.IncidentCommandId, command.DepartmentId, command.CallId, CommandLogEntryType.CommandDetailsUpdated, "Command details updated", userId, cancellationToken);
			_eventAggregator.SendMessage<IncidentCommandDetailsUpdatedEvent>(new IncidentCommandDetailsUpdatedEvent
			{
				DepartmentId = command.DepartmentId,
				CallId = command.CallId,
				IncidentCommandId = command.IncidentCommandId,
				UpdatedByUserId = userId
			});
			return command;
		}

		public async Task<ResourceIncidentView> GetResourceIncidentViewAsync(int departmentId, int callId, string userId, int? unitId, bool includePrivate)
		{
			var command = await GetCommandForCallAsync(departmentId, callId);
			if (command == null)
				return null;

			var view = new ResourceIncidentView
			{
				IncidentCommandId = command.IncidentCommandId,
				CallId = command.CallId,
				Status = command.Status,
				EstablishedOn = command.EstablishedOn,
				EstimatedEndOn = command.EstimatedEndOn,
				ClosedOn = command.ClosedOn,
				ImportantInformation = command.ImportantInformation,
				IncidentActionPlan = command.IncidentActionPlan,
				Commander = await BuildUserContactAsync(command.CurrentCommanderUserId),
				Objectives = await GetObjectivesForCallAsync(departmentId, callId),
				Needs = await GetNeedsForCallAsync(departmentId, callId),
				Notes = await GetNotesForCallAsync(departmentId, callId, publicOnly: !includePrivate),
				Attachments = await GetAttachmentsForCallAsync(departmentId, callId, publicOnly: !includePrivate)
			};

			// The caller's own active lane assignment: a unit client resolves by unit id, a responder by user id.
			var assignments = await GetAssignmentsForCallAsync(departmentId, callId);
			var mine = unitId.HasValue
				? assignments.FirstOrDefault(a => a.ReleasedOn == null && a.ResourceKind == (int)Model.ResourceAssignmentKind.RealUnit && a.ResourceId == unitId.Value.ToString())
				: assignments.FirstOrDefault(a => a.ReleasedOn == null && a.ResourceKind == (int)Model.ResourceAssignmentKind.RealPersonnel && a.ResourceId == userId);

			if (mine != null && !string.IsNullOrWhiteSpace(mine.CommandStructureNodeId))
			{
				var node = await _commandStructureNodeRepository.GetByIdAsync(mine.CommandStructureNodeId);
				if (node != null && node.DepartmentId == departmentId && node.DeletedOn == null)
				{
					view.MyAssignment = new ResourceLaneAssignmentView
					{
						ResourceAssignmentId = mine.ResourceAssignmentId,
						CommandStructureNodeId = node.CommandStructureNodeId,
						LaneName = node.Name,
						NodeType = node.NodeType,
						Color = node.Color,
						AssignedOn = mine.AssignedOn,
						PrimaryLead = await BuildLeadContactAsync(node.PrimaryLeadUserId, node.PrimaryLeadName, node.PrimaryLeadPhone, node.PrimaryLeadEmail),
						SecondaryLead = await BuildLeadContactAsync(node.SecondaryLeadUserId, node.SecondaryLeadName, node.SecondaryLeadPhone, node.SecondaryLeadEmail),
						PrimaryObjective = view.Objectives.FirstOrDefault(o => o.TacticalObjectiveId == node.PrimaryObjectiveId),
						SecondaryObjective = view.Objectives.FirstOrDefault(o => o.TacticalObjectiveId == node.SecondaryObjectiveId),
						LinkedNeed = view.Needs.FirstOrDefault(n => n.IncidentNeedId == node.LinkedNeedId)
					};
				}
			}

			return view;
		}

		/// <summary>Contact card for a Resgrid user (name from the profile; phone/email as the profile exposes them).</summary>
		private async Task<IncidentContactInfo> BuildUserContactAsync(string userId)
		{
			if (string.IsNullOrWhiteSpace(userId))
				return null;

			var contact = new IncidentContactInfo { UserId = userId, Name = userId };
			try
			{
				var profile = await _userProfileService.GetProfileByUserIdAsync(userId);
				if (profile != null)
				{
					contact.Name = profile.FullName.AsFirstNameLastName;
					contact.Phone = profile.MobileNumber;
					contact.Email = profile.MembershipEmail;
				}
			}
			catch (Exception ex)
			{
				Resgrid.Framework.Logging.LogException(ex);
			}
			return contact;
		}

		/// <summary>Contact card for a lane lead: a Resgrid user (resolved) or an external contact (as entered).</summary>
		private async Task<IncidentContactInfo> BuildLeadContactAsync(string leadUserId, string leadName, string leadPhone, string leadEmail)
		{
			if (!string.IsNullOrWhiteSpace(leadUserId))
				return await BuildUserContactAsync(leadUserId);

			if (string.IsNullOrWhiteSpace(leadName) && string.IsNullOrWhiteSpace(leadPhone) && string.IsNullOrWhiteSpace(leadEmail))
				return null;

			return new IncidentContactInfo { Name = leadName, Phone = leadPhone, Email = leadEmail };
		}

		/// <summary>
		/// Detects primary/secondary lead changes on a lane save and raises one <see cref="LaneLeadChangedEvent"/>
		/// per changed slot (also logged to the timeline). New lanes only announce leads that were set at creation.
		/// </summary>
		private async Task PublishLeadChangesAsync(CommandStructureNode stored, CommandStructureNode saved, bool isNew, string userId, CancellationToken cancellationToken)
		{
			var slots = new[]
			{
				new
				{
					IsPrimary = true,
					PrevUserId = isNew ? null : stored?.PrimaryLeadUserId,
					PrevName = isNew ? null : stored?.PrimaryLeadName,
					NewUserId = saved.PrimaryLeadUserId,
					NewName = saved.PrimaryLeadName
				},
				new
				{
					IsPrimary = false,
					PrevUserId = isNew ? null : stored?.SecondaryLeadUserId,
					PrevName = isNew ? null : stored?.SecondaryLeadName,
					NewUserId = saved.SecondaryLeadUserId,
					NewName = saved.SecondaryLeadName
				}
			};

			foreach (var slot in slots)
			{
				var changed = !string.Equals(slot.PrevUserId ?? string.Empty, slot.NewUserId ?? string.Empty, StringComparison.OrdinalIgnoreCase)
					|| !string.Equals(slot.PrevName ?? string.Empty, slot.NewName ?? string.Empty, StringComparison.OrdinalIgnoreCase);
				if (!changed)
					continue;

				// A brand-new lane with empty lead slots has nothing to announce.
				if (isNew && string.IsNullOrWhiteSpace(slot.NewUserId) && string.IsNullOrWhiteSpace(slot.NewName))
					continue;

				await WriteLogAsync(saved.IncidentCommandId, saved.DepartmentId, saved.CallId, CommandLogEntryType.LaneLeadChanged,
					$"Lane '{saved.Name}' {(slot.IsPrimary ? "primary" : "secondary")} lead changed", userId, cancellationToken);

				_eventAggregator.SendMessage<LaneLeadChangedEvent>(new LaneLeadChangedEvent
				{
					DepartmentId = saved.DepartmentId,
					CallId = saved.CallId,
					IncidentCommandId = saved.IncidentCommandId,
					CommandStructureNodeId = saved.CommandStructureNodeId,
					LaneName = saved.Name,
					IsPrimary = slot.IsPrimary,
					PreviousLeadUserId = slot.PrevUserId,
					PreviousLeadName = slot.PrevName,
					NewLeadUserId = slot.NewUserId,
					NewLeadName = slot.NewName
				});

				await _incidentCommandNotificationService.NotifyLaneLeadChangedAsync(saved.DepartmentId, saved.CallId, saved.Name, slot.IsPrimary,
					slot.PrevUserId, slot.PrevName, slot.NewUserId, slot.NewName, cancellationToken);
			}
		}

		public async Task<IncidentNote> AddNoteAsync(IncidentNote note, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (note == null || string.IsNullOrWhiteSpace(note.IncidentCommandId) || string.IsNullOrWhiteSpace(note.Body))
				throw new ArgumentException("An incident command and note body are required.");
			if (note.Body.Length > Resgrid.Config.IncidentCommandConfig.MaxNoteLength)
				throw new ArgumentException($"Incident notes cannot exceed {Resgrid.Config.IncidentCommandConfig.MaxNoteLength} characters.");
			if (!Enum.IsDefined(typeof(IncidentNoteType), note.NoteType) || !Enum.IsDefined(typeof(IncidentContentVisibility), note.Visibility))
				throw new ArgumentException("The incident note type or visibility is invalid.");
			if (note.ContainmentPercent.HasValue && (note.ContainmentPercent.Value < 0 || note.ContainmentPercent.Value > 100))
				throw new ArgumentOutOfRangeException(nameof(note.ContainmentPercent), "Containment must be between 0 and 100 percent.");

			var command = await GetOwnedCommandAsync(note.IncidentCommandId, note.DepartmentId);
			if (command == null)
				return null;

			note.IncidentNoteId = Guid.NewGuid().ToString();
			note.CallId = command.CallId;
			note.Title = TrimToLength(note.Title, 250);
			note.Body = note.Body.Trim();
			note.CreatedByUserId = userId;
			note.CreatedOn = DateTime.UtcNow;
			note.DeletedOn = null;
			note.DeletedByUserId = null;

			var saved = await _incidentNoteRepository.InsertAsync(Touch(note), cancellationToken);
			await WriteLogAsync(command.IncidentCommandId, command.DepartmentId, command.CallId, CommandLogEntryType.IncidentNoteAdded, $"Incident note added: {saved.Title ?? ((IncidentNoteType)saved.NoteType).ToString()}", userId, cancellationToken);
			_eventAggregator.SendMessage<IncidentNoteAddedEvent>(new IncidentNoteAddedEvent
			{
				DepartmentId = saved.DepartmentId,
				CallId = saved.CallId,
				IncidentCommandId = saved.IncidentCommandId,
				IncidentNoteId = saved.IncidentNoteId,
				Visibility = saved.Visibility,
				NoteType = saved.NoteType,
				Title = saved.Title,
				Body = saved.Body,
				ContainmentPercent = saved.ContainmentPercent,
				CreatedByUserId = userId
			});
			return saved;
		}

		public async Task<List<IncidentNote>> GetNotesForCallAsync(int departmentId, int callId, bool publicOnly = false)
		{
			var notes = await _incidentNoteRepository.GetAllByDepartmentIdAsync(departmentId);
			if (notes == null)
				return new List<IncidentNote>();

			return notes.Where(x => x.CallId == callId && x.DeletedOn == null &&
				(!publicOnly || x.Visibility == (int)IncidentContentVisibility.Public))
				.OrderBy(x => x.CreatedOn)
				.ToList();
		}

		public async Task<bool> RemoveNoteAsync(int departmentId, string incidentNoteId, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var note = await _incidentNoteRepository.GetByIdAsync(incidentNoteId);
			if (note == null || note.DepartmentId != departmentId || note.DeletedOn.HasValue)
				return false;
			var capabilities = await GetCapabilitiesForUserAsync(departmentId, note.CallId, userId);
			var required = IncidentCapabilities.ManageNotes;
			if (note.Visibility == (int)IncidentContentVisibility.Public)
				required |= IncidentCapabilities.ManagePublicInformation;
			if ((capabilities & required) != required)
				return false;

			note.DeletedOn = DateTime.UtcNow;
			note.DeletedByUserId = userId;
			await _incidentNoteRepository.SaveOrUpdateAsync(Touch(note), cancellationToken);
			await WriteLogAsync(note.IncidentCommandId, note.DepartmentId, note.CallId, CommandLogEntryType.IncidentNoteRemoved, "Incident note removed", userId, cancellationToken);
			_eventAggregator.SendMessage<IncidentNoteRemovedEvent>(new IncidentNoteRemovedEvent
			{
				DepartmentId = note.DepartmentId,
				CallId = note.CallId,
				IncidentCommandId = note.IncidentCommandId,
				IncidentNoteId = note.IncidentNoteId,
				Visibility = note.Visibility,
				RemovedByUserId = userId
			});
			return true;
		}

		public async Task<IncidentAttachment> AddAttachmentAsync(IncidentAttachment attachment, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (attachment == null || string.IsNullOrWhiteSpace(attachment.IncidentCommandId) || attachment.Data == null || attachment.Data.Length == 0)
				throw new ArgumentException("An incident command and file data are required.");
			if (!Enum.IsDefined(typeof(IncidentContentVisibility), attachment.Visibility))
				throw new ArgumentException("The incident attachment visibility is invalid.");
			if (attachment.Data.Length > Resgrid.Config.IncidentCommandConfig.MaxAttachmentBytes)
				throw new ArgumentException($"Incident files cannot exceed {Resgrid.Config.IncidentCommandConfig.MaxAttachmentBytes} bytes.");

			// Browsers and API clients can submit either path separator regardless of the host OS.
			var safeFileName = Path.GetFileName((attachment.FileName ?? string.Empty).Replace('\\', '/'));
			if (string.IsNullOrWhiteSpace(safeFileName) || IsBlockedAttachment(safeFileName, attachment.ContentType))
				throw new ArgumentException("The incident file name or type is not allowed.");

			var command = await GetOwnedCommandAsync(attachment.IncidentCommandId, attachment.DepartmentId);
			if (command == null)
				return null;

			attachment.IncidentAttachmentId = Guid.NewGuid().ToString();
			attachment.CallId = command.CallId;
			attachment.FileName = TrimToLength(safeFileName, 512);
			attachment.ContentType = TrimToLength(string.IsNullOrWhiteSpace(attachment.ContentType) ? "application/octet-stream" : attachment.ContentType, 200);
			attachment.ContentLength = attachment.Data.LongLength;
			attachment.Sha256Hash = Convert.ToHexString(SHA256.HashData(attachment.Data)).ToLowerInvariant();
			attachment.Description = TrimToLength(attachment.Description, 1000);
			attachment.UploadedByUserId = userId;
			attachment.UploadedOn = DateTime.UtcNow;
			attachment.DeletedOn = null;
			attachment.DeletedByUserId = null;

			var saved = await _incidentAttachmentRepository.InsertAsync(Touch(attachment), cancellationToken);
			await WriteLogAsync(command.IncidentCommandId, command.DepartmentId, command.CallId, CommandLogEntryType.IncidentAttachmentAdded, $"Incident file added: {saved.FileName}", userId, cancellationToken);
			_eventAggregator.SendMessage<IncidentAttachmentAddedEvent>(new IncidentAttachmentAddedEvent
			{
				DepartmentId = saved.DepartmentId,
				CallId = saved.CallId,
				IncidentCommandId = saved.IncidentCommandId,
				IncidentAttachmentId = saved.IncidentAttachmentId,
				Visibility = saved.Visibility,
				FileName = saved.FileName,
				ContentType = saved.ContentType,
				ContentLength = saved.ContentLength,
				Sha256Hash = saved.Sha256Hash,
				Description = saved.Description,
				UploadedByUserId = userId
			});
			return saved;
		}

		public async Task<List<IncidentAttachment>> GetAttachmentsForCallAsync(int departmentId, int callId, bool publicOnly = false)
		{
			var attachments = await _incidentAttachmentRepository.GetAllMetadataByDepartmentIdAsync(departmentId);
			if (attachments == null)
				return new List<IncidentAttachment>();

			return attachments.Where(x => x.CallId == callId && x.DeletedOn == null &&
				(!publicOnly || x.Visibility == (int)IncidentContentVisibility.Public))
				.OrderBy(x => x.UploadedOn)
				.ToList();
		}

		public async Task<IncidentAttachment> GetAttachmentAsync(int departmentId, string incidentAttachmentId)
		{
			var attachment = await _incidentAttachmentRepository.GetByIdAsync(incidentAttachmentId);
			return attachment != null && attachment.DepartmentId == departmentId && !attachment.DeletedOn.HasValue ? attachment : null;
		}

		public async Task<bool> RemoveAttachmentAsync(int departmentId, string incidentAttachmentId, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var attachment = await _incidentAttachmentRepository.GetByIdAsync(incidentAttachmentId);
			if (attachment == null || attachment.DepartmentId != departmentId || attachment.DeletedOn.HasValue)
				return false;
			var capabilities = await GetCapabilitiesForUserAsync(departmentId, attachment.CallId, userId);
			var required = IncidentCapabilities.ManageDocuments;
			if (attachment.Visibility == (int)IncidentContentVisibility.Public)
				required |= IncidentCapabilities.ManagePublicInformation;
			if ((capabilities & required) != required)
				return false;

			attachment.DeletedOn = DateTime.UtcNow;
			attachment.DeletedByUserId = userId;
			await _incidentAttachmentRepository.SaveOrUpdateAsync(Touch(attachment), cancellationToken);
			await WriteLogAsync(attachment.IncidentCommandId, attachment.DepartmentId, attachment.CallId, CommandLogEntryType.IncidentAttachmentRemoved, $"Incident file removed: {attachment.FileName}", userId, cancellationToken);
			_eventAggregator.SendMessage<IncidentAttachmentRemovedEvent>(new IncidentAttachmentRemovedEvent
			{
				DepartmentId = attachment.DepartmentId,
				CallId = attachment.CallId,
				IncidentCommandId = attachment.IncidentCommandId,
				IncidentAttachmentId = attachment.IncidentAttachmentId,
				Visibility = attachment.Visibility,
				FileName = attachment.FileName,
				RemovedByUserId = userId
			});
			return true;
		}

		public async Task<IncidentCommand> EnablePublicSharingAsync(int departmentId, string incidentCommandId, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var command = await GetOwnedCommandAsync(incidentCommandId, departmentId);
			if (command == null)
				return null;

			if (!command.PublicShareEnabled || string.IsNullOrWhiteSpace(command.PublicShareToken))
				command.PublicShareToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
			command.PublicShareEnabled = true;
			command = await _incidentCommandRepository.SaveOrUpdateAsync(Touch(command), cancellationToken);
			await WriteLogAsync(command.IncidentCommandId, command.DepartmentId, command.CallId, CommandLogEntryType.PublicSharingEnabled, "Public incident sharing enabled", userId, cancellationToken);
			_eventAggregator.SendMessage<IncidentPublicSharingChangedEvent>(new IncidentPublicSharingChangedEvent
			{
				DepartmentId = command.DepartmentId,
				CallId = command.CallId,
				IncidentCommandId = command.IncidentCommandId,
				Enabled = true,
				UpdatedByUserId = userId
			});
			return command;
		}

		public async Task<IncidentCommand> DisablePublicSharingAsync(int departmentId, string incidentCommandId, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var command = await GetOwnedCommandAsync(incidentCommandId, departmentId);
			if (command == null)
				return null;

			command.PublicShareEnabled = false;
			command.PublicShareToken = null;
			command = await _incidentCommandRepository.SaveOrUpdateAsync(Touch(command), cancellationToken);
			await WriteLogAsync(command.IncidentCommandId, command.DepartmentId, command.CallId, CommandLogEntryType.PublicSharingDisabled, "Public incident sharing disabled", userId, cancellationToken);
			_eventAggregator.SendMessage<IncidentPublicSharingChangedEvent>(new IncidentPublicSharingChangedEvent
			{
				DepartmentId = command.DepartmentId,
				CallId = command.CallId,
				IncidentCommandId = command.IncidentCommandId,
				Enabled = false,
				UpdatedByUserId = userId
			});
			return command;
		}

		public async Task<IncidentPublicInformation> GetPublicInformationAsync(string publicShareToken)
		{
			if (!IsValidPublicShareToken(publicShareToken))
				return null;
			var command = await _incidentCommandRepository.GetByPublicShareTokenAsync(publicShareToken);
			if (command == null)
				return null;

			var notes = await GetNotesForCallAsync(command.DepartmentId, command.CallId, true);
			var attachments = await GetAttachmentsForCallAsync(command.DepartmentId, command.CallId, true);
			var lastUpdated = new[]
			{
				command.ModifiedOn,
				notes.Select(x => x.ModifiedOn).Where(x => x.HasValue).OrderByDescending(x => x).FirstOrDefault(),
				attachments.Select(x => x.ModifiedOn).Where(x => x.HasValue).OrderByDescending(x => x).FirstOrDefault()
			}.Where(x => x.HasValue).OrderByDescending(x => x).FirstOrDefault();

			return new IncidentPublicInformation
			{
				IncidentCommandId = command.IncidentCommandId,
				EstablishedOn = command.EstablishedOn,
				Status = command.Status,
				ClosedOn = command.ClosedOn,
				LastUpdatedOn = lastUpdated,
				Notes = notes.Select(x => new PublicIncidentNote
				{
					IncidentNoteId = x.IncidentNoteId,
					NoteType = x.NoteType,
					Title = x.Title,
					Body = x.Body,
					ContainmentPercent = x.ContainmentPercent,
					CreatedOn = x.CreatedOn
				}).ToList(),
				Attachments = attachments.Select(x => new PublicIncidentAttachment
				{
					IncidentAttachmentId = x.IncidentAttachmentId,
					FileName = x.FileName,
					ContentType = x.ContentType,
					ContentLength = x.ContentLength,
					Sha256Hash = x.Sha256Hash,
					Description = x.Description,
					UploadedOn = x.UploadedOn
				}).ToList()
			};
		}

		public async Task<IncidentAttachment> GetPublicAttachmentAsync(string publicShareToken, string incidentAttachmentId)
		{
			if (!IsValidPublicShareToken(publicShareToken) || string.IsNullOrWhiteSpace(incidentAttachmentId))
				return null;
			var command = await _incidentCommandRepository.GetByPublicShareTokenAsync(publicShareToken);
			if (command == null)
				return null;

			var attachment = await _incidentAttachmentRepository.GetByIdAsync(incidentAttachmentId);
			return attachment != null && attachment.IncidentCommandId == command.IncidentCommandId &&
			       attachment.Visibility == (int)IncidentContentVisibility.Public && !attachment.DeletedOn.HasValue
				? attachment
				: null;
		}

		public async Task<IncidentWeather> GetWeatherForIncidentAsync(int departmentId, int callId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var command = await GetCommandForCallAsync(departmentId, callId);
			if (command == null)
				return null;

			decimal latitude;
			decimal longitude;
			if (!TryParseCoordinates(command.CommandPostLatitude, command.CommandPostLongitude, out latitude, out longitude))
			{
				var call = await _callsService.GetCallByIdAsync(callId);
				var coordinates = call?.GeoLocationData?.Split(',');
				if (call == null || call.DepartmentId != departmentId || coordinates == null || coordinates.Length < 2 ||
				    !TryParseCoordinates(coordinates[0], coordinates[1], out latitude, out longitude))
					throw new InvalidOperationException("The incident does not have a valid command-post or call location.");
			}

			return await _incidentWeatherProvider.GetWeatherAsync(latitude, longitude, Resgrid.Config.IncidentCommandConfig.ForecastHours, cancellationToken);
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

			// Capture the stored lead slots inside the preserve callback so a lead change can be detected
			// and broadcast after the upsert (assigned resources are notified who's coming on / going off).
			CommandStructureNode storedLeads = null;
			var (saved, isNew, rejected) = await UpsertOwnedAsync(_commandStructureNodeRepository, node, node.DepartmentId,
				e => e.DepartmentId, (stored, incoming) =>
				{
					incoming.DeletedOn = stored.DeletedOn;
					storedLeads = stored;
				}, cancellationToken);
			if (rejected)
				return null;
			node = saved;

			await WriteLogAsync(node.IncidentCommandId, node.DepartmentId, node.CallId,
				isNew ? CommandLogEntryType.NodeAdded : CommandLogEntryType.NodeUpdated,
				$"Lane '{node.Name}' {(isNew ? "added" : "updated")}", userId, cancellationToken);

			await PublishLeadChangesAsync(storedLeads, node, isNew, userId, cancellationToken);
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

			// Evaluate the target lane's template requirements (unit types / personnel roles). A forced
			// violation throws so the API can reject with the reason; an advisory violation is stamped on
			// the assignment (RequirementsWarning) for the IC app to render, never blocking the assignment.
			assignment.RequirementsWarning = false;
			assignment.RequirementsWarningMessage = null;
			string assignedLaneName = null;
			if (!string.IsNullOrWhiteSpace(assignment.CommandStructureNodeId))
			{
				// The lane must live on the SAME incident (department + call) as this assignment; a lane from
				// another call is a foreign-incident lane and must not have its requirements applied here.
				var node = await _commandStructureNodeRepository.GetByIdAsync(assignment.CommandStructureNodeId);
				if (node != null && node.DepartmentId == assignment.DepartmentId && node.CallId == assignment.CallId)
				{
					assignedLaneName = node.Name;
					var (violation, enforced) = await EvaluateNodeRequirementsAsync(node, assignment.DepartmentId, assignment.ResourceKind, assignment.ResourceId);
					if (violation != null && enforced)
						throw new CommandRequirementsNotMetException(violation);

					assignment.RequirementsWarning = violation != null;
					assignment.RequirementsWarningMessage = violation;
				}
			}

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

			await _incidentCommandNotificationService.NotifyResourceAssignedAsync(assignment, assignedLaneName, cancellationToken);
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

			// Evaluate the target lane's template requirements before moving the resource into it: a forced
			// violation rejects the move, an advisory one re-stamps the warning for the destination lane.
			var (violation, enforced) = await EvaluateNodeRequirementsAsync(targetNode, departmentId, assignment.ResourceKind, assignment.ResourceId);
			if (violation != null && enforced)
				throw new CommandRequirementsNotMetException(violation);

			assignment.RequirementsWarning = violation != null;
			assignment.RequirementsWarningMessage = violation;

			// Early-rotation advisory (MinTimeInRole): rotating a resource out before the source lane's
			// minimum stint never blocks — the IC may have good reason — but the move gets flagged.
			if (!string.IsNullOrWhiteSpace(assignment.CommandStructureNodeId) && assignment.CommandStructureNodeId != targetNodeId)
			{
				var sourceNode = await _commandStructureNodeRepository.GetByIdAsync(assignment.CommandStructureNodeId);
				if (sourceNode != null && sourceNode.MinTimeInRole > 0)
				{
					var minutesInLane = (DateTime.UtcNow - assignment.AssignedOn).TotalMinutes;
					if (minutesInLane < sourceNode.MinTimeInRole)
					{
						var earlyWarning = $"Rotated out of lane '{sourceNode.Name}' after {Math.Round(minutesInLane)} of its minimum {sourceNode.MinTimeInRole} minutes.";
						assignment.RequirementsWarning = true;
						assignment.RequirementsWarningMessage = string.IsNullOrWhiteSpace(assignment.RequirementsWarningMessage) ? earlyWarning : $"{assignment.RequirementsWarningMessage} {earlyWarning}";
					}
				}
			}

			var fromNodeId = assignment.CommandStructureNodeId;
			assignment.CommandStructureNodeId = targetNodeId;
			assignment = await _resourceAssignmentRepository.SaveOrUpdateAsync(Touch(assignment), cancellationToken);

			await WriteLogAsync(assignment.IncidentCommandId, assignment.DepartmentId, assignment.CallId, CommandLogEntryType.ResourceMoved, "Resource moved", userId, cancellationToken);

			_eventAggregator.SendMessage<IncidentResourceMovedEvent>(new IncidentResourceMovedEvent
			{
				DepartmentId = assignment.DepartmentId,
				CallId = assignment.CallId,
				IncidentCommandId = assignment.IncidentCommandId,
				ResourceAssignmentId = assignment.ResourceAssignmentId,
				ResourceKind = assignment.ResourceKind,
				ResourceId = assignment.ResourceId,
				FromNodeId = fromNodeId,
				ToNodeId = targetNodeId
			});

			string fromLaneName = null;
			if (!string.IsNullOrWhiteSpace(fromNodeId) && fromNodeId != targetNodeId)
				fromLaneName = (await _commandStructureNodeRepository.GetByIdAsync(fromNodeId))?.Name;
			await _incidentCommandNotificationService.NotifyResourceMovedAsync(assignment, fromLaneName, targetNode.Name, cancellationToken);
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

			await _incidentCommandNotificationService.NotifyResourceReleasedAsync(assignment, cancellationToken);
			return true;
		}

		/// <summary>
		/// Evaluates a lane's template requirements for a resource: own-department units should match one of
		/// the required unit types, and own-department personnel should hold one of the required personnel
		/// roles. Returns the violation message (null when compliant / not applicable) and whether the lane
		/// FORCES its requirements. Callers throw <see cref="CommandRequirementsNotMetException"/> for forced
		/// violations and stamp advisory ones onto the assignment (RequirementsWarning) so the IC app can
		/// render a warning on the resource chip. An empty requirement set leaves that resource kind
		/// unrestricted; linked-department and ad-hoc resources carry no own-department type/role metadata
		/// to validate, so they are never flagged.
		/// </summary>
		private async Task<(string violation, bool enforced)> EvaluateNodeRequirementsAsync(CommandStructureNode node, int departmentId, int resourceKind, string resourceId)
		{
			if (node == null)
				return (null, false);

			// Ad-hoc (external mutual-aid style) units and personnel created at incident time always bypass
			// lane requirements — even when the lane forces them — and never get an advisory warning. The IC
			// vouches for outside resources; the department's unit types/roles don't apply to them.
			if (resourceKind == (int)ResourceAssignmentKind.AdHocUnit || resourceKind == (int)ResourceAssignmentKind.AdHocPersonnel)
				return (null, false);

			var role = node.SourceRoleId.HasValue ? await _commandsService.GetRoleWithRequirementsAsync(node.SourceRoleId.Value) : null;
			var enforced = node.ForceRequirements || (role?.ForceRequirements ?? false);
			var isUnitKind = resourceKind == (int)ResourceAssignmentKind.RealUnit || resourceKind == (int)ResourceAssignmentKind.LinkedDeptUnit;

			// Lane capacity (MaxUnits): every active unit-kind assignment occupies a slot — ad-hoc units
			// included (they bypass qualification checks but still take up space in the lane).
			if (isUnitKind && node.MaxUnits > 0)
			{
				var laneAssignments = await GetAssignmentsForCallAsync(departmentId, node.CallId);
				var unitCount = laneAssignments.Count(a => a.ReleasedOn == null
					&& a.CommandStructureNodeId == node.CommandStructureNodeId
					&& (a.ResourceKind == (int)ResourceAssignmentKind.RealUnit || a.ResourceKind == (int)ResourceAssignmentKind.LinkedDeptUnit || a.ResourceKind == (int)ResourceAssignmentKind.AdHocUnit)
					&& !(a.ResourceKind == resourceKind && a.ResourceId == resourceId));

				if (unitCount >= node.MaxUnits)
					return ($"Lane '{node.Name}' is at its maximum of {node.MaxUnits} unit(s).", enforced);
			}

			// Unit staffing (MinUnitPersonnel/MaxUnitPersonnel): riders currently on the unit's active
			// role seats. Own-department units only — riders can't be resolved for linked-department units.
			if (resourceKind == (int)ResourceAssignmentKind.RealUnit && (node.MinUnitPersonnel > 0 || node.MaxUnitPersonnel > 0)
				&& int.TryParse(resourceId, out var staffedUnitId))
			{
				var riders = await _unitsService.GetActiveRolesForUnitAsync(staffedUnitId);
				var riderCount = riders?.Count(r => !string.IsNullOrWhiteSpace(r.UserId)) ?? 0;

				if (node.MinUnitPersonnel > 0 && riderCount < node.MinUnitPersonnel)
					return ($"Lane '{node.Name}' requires at least {node.MinUnitPersonnel} personnel riding the unit (currently {riderCount}).", enforced);

				if (node.MaxUnitPersonnel > 0 && riderCount > node.MaxUnitPersonnel)
					return ($"Lane '{node.Name}' allows at most {node.MaxUnitPersonnel} personnel riding the unit (currently {riderCount}).", enforced);
			}

			if (role == null)
				return (null, enforced);

			string violation = null;

			if (resourceKind == (int)ResourceAssignmentKind.RealUnit)
			{
				var requiredUnitTypes = role.RequiredUnitTypes?.Select(x => x.UnitTypeId).ToList();
				if (requiredUnitTypes == null || requiredUnitTypes.Count == 0)
					return (null, enforced);

				Unit unit = null;
				if (int.TryParse(resourceId, out var unitId))
					unit = await _unitsService.GetUnitByIdAsync(unitId);

				if (unit == null || unit.DepartmentId != departmentId)
				{
					// An unresolvable unit cannot be verified against the requirement.
					violation = $"Lane '{node.Name}' requires specific unit types and the unit could not be verified.";
				}
				else
				{
					// Units store the unit-type NAME, not the id; map it back through the department's unit types.
					int? unitTypeId = null;
					if (!string.IsNullOrWhiteSpace(unit.Type))
					{
						var types = await _unitsService.GetUnitTypesForDepartmentAsync(departmentId);
						unitTypeId = types?.FirstOrDefault(x => string.Equals(x.Type, unit.Type, StringComparison.OrdinalIgnoreCase))?.UnitTypeId;
					}

					if (!unitTypeId.HasValue || !requiredUnitTypes.Contains(unitTypeId.Value))
						violation = $"Unit '{unit.Name}' does not match the unit types required by lane '{node.Name}'.";
				}
			}
			else if (resourceKind == (int)ResourceAssignmentKind.RealPersonnel)
			{
				var requiredRoles = role.RequiredRoles?.Select(x => x.PersonnelRoleId).ToList();
				if (requiredRoles == null || requiredRoles.Count == 0)
					return (null, enforced);

				var userRoles = await _personnelRolesService.GetRolesForUserAsync(resourceId, departmentId);
				if (userRoles == null || !userRoles.Any(x => requiredRoles.Contains(x.PersonnelRoleId)))
					violation = $"This member does not hold a personnel role required by lane '{node.Name}'.";
			}

			return (violation, enforced);
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
					// Completion and progress are owned by CompleteObjectiveAsync / UpdateObjectiveProgressAsync;
					// a Save (edit/replay) must not reset them.
					incoming.Status = stored.Status;
					incoming.CompletedByUserId = stored.CompletedByUserId;
					incoming.CompletedOn = stored.CompletedOn;
					incoming.ProgressPercent = stored.ProgressPercent;
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
			objective.ProgressPercent = 100;
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

		public async Task<TacticalObjective> UpdateObjectiveProgressAsync(int departmentId, string tacticalObjectiveId, int progressPercent, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var objective = await _tacticalObjectiveRepository.GetByIdAsync(tacticalObjectiveId);
			if (objective == null || objective.DepartmentId != departmentId)
				return null;

			progressPercent = Math.Max(0, Math.Min(100, progressPercent));

			// 100% IS completion — route through the completion stamping so the timeline, events, and
			// CompletedBy/On behave exactly as an explicit CompleteObjective would.
			if (progressPercent == 100)
				return await CompleteObjectiveAsync(departmentId, tacticalObjectiveId, userId, cancellationToken);

			objective.ProgressPercent = progressPercent;
			if (objective.Status == (int)TacticalObjectiveStatus.Pending && progressPercent > 0)
				objective.Status = (int)TacticalObjectiveStatus.InProgress;
			else if (objective.Status == (int)TacticalObjectiveStatus.InProgress && progressPercent == 0)
				objective.Status = (int)TacticalObjectiveStatus.Pending;

			objective = await _tacticalObjectiveRepository.SaveOrUpdateAsync(Touch(objective), cancellationToken);

			await WriteLogAsync(objective.IncidentCommandId, objective.DepartmentId, objective.CallId, CommandLogEntryType.ObjectiveProgressUpdated,
				$"Objective '{objective.Name}' progress {objective.ProgressPercent}%", userId, cancellationToken);

			return objective;
		}

		#endregion Objectives

		#region Needs

		public async Task<IncidentNeed> SaveNeedAsync(IncidentNeed need, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			// The parent incident command must belong to the caller's department; stamp the authoritative
			// CallId from it so this row can't be filed under a different call than its parent command.
			var command = await GetOwnedCommandAsync(need.IncidentCommandId, need.DepartmentId);
			if (command == null)
				return null;
			need.CallId = command.CallId;

			if (need.CreatedOn == default(DateTime))
				need.CreatedOn = DateTime.UtcNow;
			if (string.IsNullOrWhiteSpace(need.CreatedByUserId))
				need.CreatedByUserId = userId;

			var (saved, isNew, rejected) = await UpsertOwnedAsync(_incidentNeedRepository, need, need.DepartmentId,
				e => e.DepartmentId, (stored, incoming) =>
				{
					// Fulfillment is owned by SetNeedStatusAsync; a Save (edit/replay) must not reset it.
					incoming.Status = stored.Status;
					incoming.QuantityFulfilled = stored.QuantityFulfilled;
					incoming.MetByUserId = stored.MetByUserId;
					incoming.MetOn = stored.MetOn;
					incoming.CreatedByUserId = stored.CreatedByUserId;
					incoming.CreatedOn = stored.CreatedOn;
				}, cancellationToken);
			if (rejected)
				return null;
			need = saved;

			await WriteLogAsync(need.IncidentCommandId, need.DepartmentId, need.CallId,
				isNew ? CommandLogEntryType.NeedAdded : CommandLogEntryType.NeedUpdated,
				$"Need '{need.Name}' {(isNew ? "added" : "updated")}", userId, cancellationToken);

			_eventAggregator.SendMessage<IncidentNeedChangedEvent>(new IncidentNeedChangedEvent
			{
				DepartmentId = need.DepartmentId,
				CallId = need.CallId,
				IncidentCommandId = need.IncidentCommandId,
				IncidentNeedId = need.IncidentNeedId,
				Name = need.Name,
				Status = need.Status
			});
			return need;
		}

		public async Task<IncidentNeed> SetNeedStatusAsync(int departmentId, string incidentNeedId, IncidentNeedStatus status, int? quantityFulfilled, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var need = await _incidentNeedRepository.GetByIdAsync(incidentNeedId);
			if (need == null || need.DepartmentId != departmentId)
				return null;

			need.Status = (int)status;
			if (quantityFulfilled.HasValue)
				need.QuantityFulfilled = Math.Max(0, quantityFulfilled.Value);

			if (status == IncidentNeedStatus.Met)
			{
				need.MetByUserId = userId;
				need.MetOn = DateTime.UtcNow;
				if (need.QuantityRequested > 0 && !quantityFulfilled.HasValue)
					need.QuantityFulfilled = need.QuantityRequested;
			}
			else
			{
				need.MetByUserId = null;
				need.MetOn = null;
			}

			need = await _incidentNeedRepository.SaveOrUpdateAsync(Touch(need), cancellationToken);

			await WriteLogAsync(need.IncidentCommandId, need.DepartmentId, need.CallId,
				status == IncidentNeedStatus.Met ? CommandLogEntryType.NeedMet : CommandLogEntryType.NeedUpdated,
				$"Need '{need.Name}' {(status == IncidentNeedStatus.Met ? "met" : $"status changed to {status}")}", userId, cancellationToken);

			_eventAggregator.SendMessage<IncidentNeedChangedEvent>(new IncidentNeedChangedEvent
			{
				DepartmentId = need.DepartmentId,
				CallId = need.CallId,
				IncidentCommandId = need.IncidentCommandId,
				IncidentNeedId = need.IncidentNeedId,
				Name = need.Name,
				Status = need.Status
			});
			return need;
		}

		public async Task<List<IncidentNeed>> GetNeedsForCallAsync(int departmentId, int callId)
		{
			var items = await _incidentNeedRepository.GetAllByDepartmentIdAsync(departmentId);
			if (items == null)
				return new List<IncidentNeed>();

			return items.Where(x => x.CallId == callId).OrderBy(x => x.SortOrder).ThenBy(x => x.CreatedOn).ToList();
		}

		#endregion Needs

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

		private static bool TryParseCoordinates(string latitude, string longitude, out decimal latitudeValue, out decimal longitudeValue)
		{
			latitudeValue = 0;
			longitudeValue = 0;
			var validLatitude = decimal.TryParse(latitude?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out latitudeValue);
			var validLongitude = decimal.TryParse(longitude?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out longitudeValue);
			var valid = validLatitude && validLongitude;
			return valid && latitudeValue >= -90 && latitudeValue <= 90 && longitudeValue >= -180 && longitudeValue <= 180;
		}

		private static string TrimToLength(string value, int maximumLength)
		{
			if (string.IsNullOrWhiteSpace(value))
				return null;
			value = value.Trim();
			return value.Length <= maximumLength ? value : value.Substring(0, maximumLength);
		}

		private static bool IsBlockedAttachment(string fileName, string contentType)
		{
			var extension = Path.GetExtension(fileName)?.ToLowerInvariant();
			var blockedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
			{
				".exe", ".dll", ".com", ".scr", ".msi", ".bat", ".cmd", ".ps1", ".vbs", ".js", ".jar"
			};
			if (!string.IsNullOrWhiteSpace(extension) && blockedExtensions.Contains(extension))
				return true;

			var normalizedContentType = contentType?.Split(';')[0].Trim().ToLowerInvariant();
			return normalizedContentType == "application/x-msdownload" ||
			       normalizedContentType == "application/x-msdos-program" ||
			       normalizedContentType == "application/x-sh";
		}

		private static bool IsValidPublicShareToken(string token)
		{
			return !string.IsNullOrWhiteSpace(token) && token.Length == 64 && token.All(Uri.IsHexDigit);
		}

		#endregion Private helpers
	}
}
