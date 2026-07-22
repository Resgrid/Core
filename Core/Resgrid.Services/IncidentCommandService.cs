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
using Resgrid.Model.Queue;
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
		private readonly IGeoLocationProvider _geoLocationProvider;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IIncidentAdHocUnitRepository _incidentAdHocUnitRepository;
		private readonly IIncidentAdHocPersonnelRepository _incidentAdHocPersonnelRepository;
		private readonly IIncidentNeedUpdateRepository _incidentNeedUpdateRepository;
		private readonly IIncidentMapRepository _incidentMapRepository;
		private readonly IIncidentNeedEntityRepository _incidentNeedEntityRepository;
		private readonly ICallDispatchStatusService _callDispatchStatusService;
		private readonly IQueueService _queueService;

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
			IIncidentCommandNotificationService incidentCommandNotificationService,
			IGeoLocationProvider geoLocationProvider,
			IDepartmentGroupsService departmentGroupsService,
			IIncidentAdHocUnitRepository incidentAdHocUnitRepository,
			IIncidentAdHocPersonnelRepository incidentAdHocPersonnelRepository,
			IIncidentNeedUpdateRepository incidentNeedUpdateRepository,
			IIncidentMapRepository incidentMapRepository,
			IIncidentNeedEntityRepository incidentNeedEntityRepository,
			ICallDispatchStatusService callDispatchStatusService,
			IQueueService queueService)
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
			_geoLocationProvider = geoLocationProvider;
			_departmentGroupsService = departmentGroupsService;
			_incidentAdHocUnitRepository = incidentAdHocUnitRepository;
			_incidentAdHocPersonnelRepository = incidentAdHocPersonnelRepository;
			_incidentNeedUpdateRepository = incidentNeedUpdateRepository;
			_incidentMapRepository = incidentMapRepository;
			_incidentNeedEntityRepository = incidentNeedEntityRepository;
			_callDispatchStatusService = callDispatchStatusService;
			_queueService = queueService;
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
			return await EnrichCheckInStatusNamesAsync(statuses) ?? new List<PersonnelCallCheckInStatus>();
		}

		/// <summary>
		/// The check-in timer service intentionally leaves FullName null; resolve it here so PAR rows (board,
		/// bundle, log text) carry a member's name instead of their user id GUID.
		/// </summary>
		private async Task<List<PersonnelCallCheckInStatus>> EnrichCheckInStatusNamesAsync(List<PersonnelCallCheckInStatus> statuses)
		{
			if (statuses == null || statuses.Count == 0)
				return statuses;

			var missing = statuses.Where(s => string.IsNullOrWhiteSpace(s.FullName) && !string.IsNullOrWhiteSpace(s.UserId)).ToList();
			if (missing.Count == 0)
				return statuses;

			try
			{
				var profiles = await _userProfileService.GetSelectedUserProfilesAsync(missing.Select(s => s.UserId).Distinct().ToList());
				var profilesById = (profiles ?? new List<UserProfile>()).Where(p => p != null && !string.IsNullOrWhiteSpace(p.UserId))
					.GroupBy(p => p.UserId).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

				foreach (var status in missing)
				{
					if (profilesById.TryGetValue(status.UserId, out var profile))
						status.FullName = profile.FullName.AsFirstNameLastName;
				}
			}
			catch (Exception ex)
			{
				Resgrid.Framework.Logging.LogException(ex);
			}

			return statuses;
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
				// Always resolve through the label helper so the log carries "Full Name (Group)" — never a user GUID.
				var who = await ResolveUserLabelAsync(departmentId, status.UserId, status.FullName);

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

			await WriteLogAsync(assignment.IncidentCommandId, assignment.DepartmentId, assignment.CallId, CommandLogEntryType.RoleAssigned,
				$"Role {(IncidentRoleType)assignment.RoleType} assigned to {await ResolveUserLabelAsync(assignment.DepartmentId, assignment.UserId)}", userId, cancellationToken);

			_eventAggregator.SendMessage<IncidentRoleAssignedEvent>(new IncidentRoleAssignedEvent { DepartmentId = assignment.DepartmentId, CallId = assignment.CallId, UserId = assignment.UserId, RoleType = assignment.RoleType });
			return assignment;
		}

		public async Task<bool> RemoveIncidentRoleAsync(int departmentId, string incidentRoleAssignmentId, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var assignment = await _incidentRoleAssignmentRepository.GetByIdAsync(incidentRoleAssignmentId);
			if (assignment == null || assignment.DepartmentId != departmentId || !await IsCommandActiveAsync(assignment.IncidentCommandId))
				return false;

			assignment.RemovedOn = DateTime.UtcNow;
			await _incidentRoleAssignmentRepository.SaveOrUpdateAsync(Touch(assignment), cancellationToken);

			await WriteLogAsync(assignment.IncidentCommandId, assignment.DepartmentId, assignment.CallId, CommandLogEntryType.RoleRemoved,
				$"Role {(IncidentRoleType)assignment.RoleType} removed from {await ResolveUserLabelAsync(assignment.DepartmentId, assignment.UserId)}", userId, cancellationToken);
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
				Attachments = await GetAttachmentsForCallAsync(departmentId, callId),
				Maps = await GetIncidentMapsForCallAsync(departmentId, callId)
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
			var maps = ToCallLookup(await _incidentMapRepository.GetAllByDepartmentIdAsync(departmentId), x => x.CallId);

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
					Attachments = attachments[callId].Where(x => x.DeletedOn == null).OrderBy(x => x.UploadedOn).ToList(),
					Maps = maps[callId].Where(x => x.DeletedOn == null).OrderBy(x => x.CreatedOn).ToList()
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

			var incidentMaps = await _incidentMapRepository.GetAllByDepartmentIdAsync(departmentId);
			if (incidentMaps != null)
				changes.Maps = incidentMaps.Where(Changed).ToList();

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

		public async Task<IncidentCommand> ReopenCommandAsync(int departmentId, string incidentCommandId, string reason, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var command = await _incidentCommandRepository.GetByIdAsync(incidentCommandId);
			if (command == null || command.DepartmentId != departmentId)
				return null;

			// Idempotent: reopening an already-open command (offline replay / double tap) is a no-op.
			if (command.Status == (int)IncidentCommandStatus.Active)
				return command;

			var active = await GetActiveCommandForCallAsync(departmentId, command.CallId);
			if (active != null)
				throw new InvalidOperationException("Another command is already active on this call; close it before reopening this one.");

			command.Status = (int)IncidentCommandStatus.Active;
			command.ClosedOn = null;
			try
			{
				command = await _incidentCommandRepository.SaveOrUpdateAsync(Touch(command), cancellationToken);
			}
			catch (Exception)
			{
				// Check-then-update races under concurrency; the partial unique index
				// (UX_IncidentCommands_Department_Call_Active) is the real guard.
				if (await GetActiveCommandForCallAsync(departmentId, command.CallId) != null)
					throw new InvalidOperationException("Another command is already active on this call; close it before reopening this one.");
				throw;
			}

			var reopenedBy = await ResolveUserLabelAsync(departmentId, userId);
			var description = string.IsNullOrWhiteSpace(reason)
				? $"Command reopened by {reopenedBy}"
				: $"Command reopened by {reopenedBy}: {TrimToLength(reason, 2000)}";
			await WriteLogAsync(command.IncidentCommandId, command.DepartmentId, command.CallId, CommandLogEntryType.CommandReopened, description, userId, cancellationToken);

			_eventAggregator.SendMessage<IncidentReopenedEvent>(new IncidentReopenedEvent
			{
				DepartmentId = command.DepartmentId,
				CallId = command.CallId,
				IncidentCommandId = command.IncidentCommandId,
				Reason = reason,
				ReopenedByUserId = userId
			});
			return command;
		}

		public async Task<IncidentCommand> UpdateCommandInfoAsync(int departmentId, string incidentCommandId, IncidentCommandInfoUpdate update, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (update == null)
				throw new ArgumentNullException(nameof(update));

			var command = await GetOwnedCommandAsync(incidentCommandId, departmentId);
			if (command == null)
				return null;

			if (update.Name != null)
				command.Name = TrimToLength(update.Name, 500);
			if (update.EstablishedOn.HasValue)
				command.EstablishedOn = update.EstablishedOn.Value;
			if (update.EstimatedEndOn.HasValue)
				command.EstimatedEndOn = update.EstimatedEndOn;
			else if (update.ClearEstimatedEndOn)
				command.EstimatedEndOn = null;
			if (update.ImportantInformation != null)
				command.ImportantInformation = TrimToLength(update.ImportantInformation, 8000);
			if (update.IcsLevel.HasValue)
				command.IcsLevel = update.IcsLevel.Value;

			var commandPost = await ApplyLocationEditAsync(command.CommandPostLocationText, command.CommandPostLatitude, command.CommandPostLongitude,
				update.CommandPostLocationText, update.CommandPostLatitude, update.CommandPostLongitude);
			command.CommandPostLocationText = commandPost.text;
			command.CommandPostLatitude = commandPost.latitude;
			command.CommandPostLongitude = commandPost.longitude;

			var staging = await ApplyLocationEditAsync(command.StagingLocationText, command.StagingLatitude, command.StagingLongitude,
				update.StagingLocationText, update.StagingLatitude, update.StagingLongitude);
			command.StagingLocationText = staging.text;
			command.StagingLatitude = staging.latitude;
			command.StagingLongitude = staging.longitude;

			var rehab = await ApplyLocationEditAsync(command.RehabLocationText, command.RehabLatitude, command.RehabLongitude,
				update.RehabLocationText, update.RehabLatitude, update.RehabLongitude);
			command.RehabLocationText = rehab.text;
			command.RehabLatitude = rehab.latitude;
			command.RehabLongitude = rehab.longitude;

			command = await _incidentCommandRepository.SaveOrUpdateAsync(Touch(command), cancellationToken);

			await WriteLogAsync(command.IncidentCommandId, command.DepartmentId, command.CallId, CommandLogEntryType.CommandDetailsUpdated, "Incident information updated", userId, cancellationToken);

			if (commandPost.changed)
			{
				_eventAggregator.SendMessage<IncidentCommandPostUpdatedEvent>(new IncidentCommandPostUpdatedEvent
				{
					DepartmentId = command.DepartmentId,
					CallId = command.CallId,
					IncidentCommandId = command.IncidentCommandId,
					Latitude = command.CommandPostLatitude,
					Longitude = command.CommandPostLongitude,
					UpdatedByUserId = userId
				});
			}

			_eventAggregator.SendMessage<IncidentCommandDetailsUpdatedEvent>(new IncidentCommandDetailsUpdatedEvent
			{
				DepartmentId = command.DepartmentId,
				CallId = command.CallId,
				IncidentCommandId = command.IncidentCommandId,
				UpdatedByUserId = userId
			});
			return command;
		}

		public async Task<List<IncidentCommandSummary>> GetCommandSummariesForDepartmentAsync(int departmentId, bool includeClosed = false)
		{
			var commands = await _incidentCommandRepository.GetAllByDepartmentIdAsync(departmentId);
			var selected = (commands ?? Enumerable.Empty<IncidentCommand>())
				.Where(x => includeClosed || x.Status == (int)IncidentCommandStatus.Active)
				.OrderByDescending(x => x.EstablishedOn)
				.ToList();
			if (selected.Count == 0)
				return new List<IncidentCommandSummary>();

			// "Assigned" = an unreleased assignment placed in a lane or staging (staging is itself a node type).
			var assignments = await _resourceAssignmentRepository.GetAllByDepartmentIdAsync(departmentId);
			var assignmentsByCommand = (assignments ?? Enumerable.Empty<ResourceAssignment>())
				.Where(a => a.ReleasedOn == null && !string.IsNullOrWhiteSpace(a.CommandStructureNodeId))
				.ToLookup(a => a.IncidentCommandId);

			var profilesById = new Dictionary<string, UserProfile>(StringComparer.OrdinalIgnoreCase);
			try
			{
				var commanderIds = selected.Select(x => x.CurrentCommanderUserId).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();
				var profiles = commanderIds.Count > 0 ? await _userProfileService.GetSelectedUserProfilesAsync(commanderIds) : null;
				foreach (var profile in (profiles ?? new List<UserProfile>()).Where(p => p != null && !string.IsNullOrWhiteSpace(p.UserId)))
					profilesById[profile.UserId] = profile;
			}
			catch (Exception ex)
			{
				Resgrid.Framework.Logging.LogException(ex);
			}

			static bool IsUnitKind(int kind) => kind == (int)ResourceAssignmentKind.RealUnit ||
				kind == (int)ResourceAssignmentKind.LinkedDeptUnit || kind == (int)ResourceAssignmentKind.AdHocUnit;

			// Best-effort per call: a failing lookup only loses that call's display fields.
			var callsById = new Dictionary<int, Call>();
			foreach (var callId in selected.Select(x => x.CallId).Distinct())
			{
				try
				{
					var loadedCall = await _callsService.GetCallByIdAsync(callId);
					if (loadedCall != null)
						callsById[callId] = loadedCall;
				}
				catch (Exception ex)
				{
					Resgrid.Framework.Logging.LogException(ex);
				}
			}

			var summaries = new List<IncidentCommandSummary>();
			foreach (var command in selected)
			{
				callsById.TryGetValue(command.CallId, out var call);
				var activeAssignments = assignmentsByCommand[command.IncidentCommandId].ToList();
				profilesById.TryGetValue(command.CurrentCommanderUserId ?? string.Empty, out var commanderProfile);

				summaries.Add(new IncidentCommandSummary
				{
					IncidentCommandId = command.IncidentCommandId,
					DepartmentId = command.DepartmentId,
					CallId = command.CallId,
					Name = command.Name,
					CallName = call?.Name,
					CallNumber = call?.Number,
					CallAddress = call?.Address,
					Status = command.Status,
					EstablishedOn = command.EstablishedOn,
					ClosedOn = command.ClosedOn,
					CommanderUserId = command.CurrentCommanderUserId,
					CommanderName = commanderProfile != null ? commanderProfile.FullName.AsFirstNameLastName : command.CurrentCommanderUserId,
					CommandPostLocationText = command.CommandPostLocationText,
					CommandPostLatitude = command.CommandPostLatitude,
					CommandPostLongitude = command.CommandPostLongitude,
					AssignedUnitCount = activeAssignments.Count(a => IsUnitKind(a.ResourceKind)),
					AssignedPersonnelCount = activeAssignments.Count(a => !IsUnitKind(a.ResourceKind))
				});
			}

			return summaries;
		}

		public async Task<IncidentCommandBoard> GetCommandBoardByIdAsync(int departmentId, string incidentCommandId)
		{
			var command = await GetOwnedCommandAsync(incidentCommandId, departmentId, requireActive: false);
			if (command == null)
				return null;

			var callId = command.CallId;

			// A call can carry several commands over its life (closed ones plus at most one active), and the
			// per-call getters return rows across all of them — filter every child set to THIS command.
			var board = new IncidentCommandBoard
			{
				Command = command,
				Nodes = (await GetNodesForCallAsync(departmentId, callId)).Where(x => x.IncidentCommandId == incidentCommandId).ToList(),
				Assignments = (await GetAssignmentsForCallAsync(departmentId, callId)).Where(x => x.IncidentCommandId == incidentCommandId && x.ReleasedOn == null).ToList(),
				Objectives = (await GetObjectivesForCallAsync(departmentId, callId)).Where(x => x.IncidentCommandId == incidentCommandId).ToList(),
				Needs = (await GetNeedsForCallAsync(departmentId, callId)).Where(x => x.IncidentCommandId == incidentCommandId).ToList(),
				Timers = (await GetActiveTimersForCallAsync(departmentId, callId)).Where(x => x.IncidentCommandId == incidentCommandId).ToList(),
				Annotations = (await GetAnnotationsForCallAsync(departmentId, callId)).Where(x => x.IncidentCommandId == incidentCommandId).ToList(),
				Accountability = command.Status == (int)IncidentCommandStatus.Active
					? await GetAccountabilityForCallAsync(departmentId, callId)
					: new List<PersonnelCallCheckInStatus>(),
				Roles = (await GetIncidentRolesAsync(departmentId, callId)).Where(x => x.IncidentCommandId == incidentCommandId).ToList(),
				Notes = (await GetNotesForCallAsync(departmentId, callId)).Where(x => x.IncidentCommandId == incidentCommandId).ToList(),
				Attachments = (await GetAttachmentsForCallAsync(departmentId, callId)).Where(x => x.IncidentCommandId == incidentCommandId).ToList(),
				Maps = (await GetIncidentMapsForCallAsync(departmentId, callId)).Where(x => x.IncidentCommandId == incidentCommandId).ToList()
			};

			return board;
		}

		public async Task<CommandTransfer> TransferCommandAsync(int departmentId, string incidentCommandId, string fromUserId, string toUserId, string notes, CancellationToken cancellationToken = default(CancellationToken))
		{
			var command = await _incidentCommandRepository.GetByIdAsync(incidentCommandId);
			if (command == null || command.DepartmentId != departmentId || command.Status != (int)IncidentCommandStatus.Active)
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

			await WriteLogAsync(command.IncidentCommandId, command.DepartmentId, command.CallId, CommandLogEntryType.CommandTransferred,
				$"Command transferred from {await ResolveUserLabelAsync(departmentId, fromUserId)} to {await ResolveUserLabelAsync(departmentId, toUserId)}", fromUserId, cancellationToken);

			_eventAggregator.SendMessage<CommandTransferredEvent>(new CommandTransferredEvent { DepartmentId = command.DepartmentId, CallId = command.CallId, IncidentCommandId = incidentCommandId, FromUserId = fromUserId, ToUserId = toUserId });

			await _incidentCommandNotificationService.NotifyCommandTransferredAsync(command, fromUserId, toUserId, cancellationToken);
			return transfer;
		}

		public async Task<IncidentCommand> UpdateActionPlanAsync(int departmentId, string incidentCommandId, string actionPlan, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var command = await _incidentCommandRepository.GetByIdAsync(incidentCommandId);
			if (command == null || command.DepartmentId != departmentId || command.Status != (int)IncidentCommandStatus.Active)
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

			// Public notes land verbatim on the incident log; non-public notes only leave an attributed marker
			// (author, their command/ICS standing, and the note id) so the log never carries a private body.
			var author = await BuildNoteAuthorLabelAsync(command, userId);
			var logText = saved.Visibility == (int)IncidentContentVisibility.Public
				? $"Note from {author}: {saved.Body}"
				: $"{author} added a non-public note (note id {saved.IncidentNoteId})";
			await WriteLogAsync(command.IncidentCommandId, command.DepartmentId, command.CallId, CommandLogEntryType.IncidentNoteAdded, logText, userId, cancellationToken);
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
			if (note == null || note.DepartmentId != departmentId || note.DeletedOn.HasValue || !await IsCommandActiveAsync(note.IncidentCommandId))
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
			if (attachment == null || attachment.DepartmentId != departmentId || attachment.DeletedOn.HasValue || !await IsCommandActiveAsync(attachment.IncidentCommandId))
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
			// Sharing toggles stay available after close — a public feed may need to be published/pulled post-incident.
			var command = await GetOwnedCommandAsync(incidentCommandId, departmentId, requireActive: false);
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
			var command = await GetOwnedCommandAsync(incidentCommandId, departmentId, requireActive: false);
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
			if (node == null || node.DepartmentId != departmentId || !await IsCommandActiveAsync(node.IncidentCommandId))
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

			var assignedLabel = await ResolveResourceLabelAsync(assignment.DepartmentId, assignment.ResourceKind, assignment.ResourceId);
			await WriteLogAsync(assignment.IncidentCommandId, assignment.DepartmentId, assignment.CallId, CommandLogEntryType.ResourceAssigned,
				string.IsNullOrWhiteSpace(assignedLaneName) ? $"Resource assigned: {assignedLabel}" : $"Resource assigned: {assignedLabel} to lane '{assignedLaneName}'", userId, cancellationToken);

			_eventAggregator.SendMessage<IncidentResourceAssignedEvent>(new IncidentResourceAssignedEvent { DepartmentId = assignment.DepartmentId, CallId = assignment.CallId, IncidentCommandId = assignment.IncidentCommandId, ResourceKind = assignment.ResourceKind, ResourceId = assignment.ResourceId });

			await _incidentCommandNotificationService.NotifyResourceAssignedAsync(assignment, assignedLaneName, cancellationToken);
			return assignment;
		}

		public async Task<ResourceAssignment> MoveResourceAsync(int departmentId, string resourceAssignmentId, string targetNodeId, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var assignment = await _resourceAssignmentRepository.GetByIdAsync(resourceAssignmentId);
			if (assignment == null || assignment.DepartmentId != departmentId || !await IsCommandActiveAsync(assignment.IncidentCommandId))
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

			await WriteLogAsync(assignment.IncidentCommandId, assignment.DepartmentId, assignment.CallId, CommandLogEntryType.ResourceMoved,
				$"Resource moved: {await ResolveResourceLabelAsync(assignment.DepartmentId, assignment.ResourceKind, assignment.ResourceId)} to lane '{targetNode.Name}'", userId, cancellationToken);

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
			if (assignment == null || assignment.DepartmentId != departmentId || !await IsCommandActiveAsync(assignment.IncidentCommandId))
				return false;

			assignment.ReleasedOn = DateTime.UtcNow;
			await _resourceAssignmentRepository.SaveOrUpdateAsync(Touch(assignment), cancellationToken);

			await WriteLogAsync(assignment.IncidentCommandId, assignment.DepartmentId, assignment.CallId, CommandLogEntryType.ResourceReleased,
				$"Resource released: {await ResolveResourceLabelAsync(assignment.DepartmentId, assignment.ResourceKind, assignment.ResourceId)}", userId, cancellationToken);

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

		public async Task<TacticalObjective> CompleteObjectiveAsync(int departmentId, string tacticalObjectiveId, string userId, TacticalObjectiveOutcome outcome = TacticalObjectiveOutcome.NotSet, string note = null, CancellationToken cancellationToken = default(CancellationToken))
		{
			var objective = await _tacticalObjectiveRepository.GetByIdAsync(tacticalObjectiveId);
			if (objective == null || objective.DepartmentId != departmentId || !await IsCommandActiveAsync(objective.IncidentCommandId))
				return null;

			objective.Status = (int)TacticalObjectiveStatus.Complete;
			objective.ProgressPercent = 100;
			objective.CompletedByUserId = userId;
			objective.CompletedOn = DateTime.UtcNow;
			objective.Outcome = (int)outcome;
			objective.CompletionNote = TrimToLength(note, 2000);
			objective = await _tacticalObjectiveRepository.SaveOrUpdateAsync(Touch(objective), cancellationToken);

			// The log carries the close-out audit: outcome, author (never a GUID), and the note.
			var description = $"Objective '{objective.Name}' completed";
			if (outcome != TacticalObjectiveOutcome.NotSet)
				description = $"{description} — {outcome}";
			description = $"{description} by {await ResolveUserLabelAsync(departmentId, userId)}";
			if (!string.IsNullOrWhiteSpace(objective.CompletionNote))
				description = $"{description}: {objective.CompletionNote}";

			await WriteLogAsync(objective.IncidentCommandId, objective.DepartmentId, objective.CallId, CommandLogEntryType.ObjectiveCompleted, description, userId, cancellationToken);

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
			if (objective == null || objective.DepartmentId != departmentId || !await IsCommandActiveAsync(objective.IncidentCommandId))
				return null;

			progressPercent = Math.Max(0, Math.Min(100, progressPercent));

			// 100% IS completion — route through the completion stamping so the timeline, events, and
			// CompletedBy/On behave exactly as an explicit CompleteObjective would. Reaching 100% via
			// progress implies the objective was achieved, so it closes out as Successful.
			if (progressPercent == 100)
				return await CompleteObjectiveAsync(departmentId, tacticalObjectiveId, userId, TacticalObjectiveOutcome.Successful, null, cancellationToken);

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

		public async Task<IncidentNeed> SetNeedStatusAsync(int departmentId, string incidentNeedId, IncidentNeedStatus status, int? quantityFulfilled, string userId, string note = null, CancellationToken cancellationToken = default(CancellationToken))
		{
			var need = await _incidentNeedRepository.GetByIdAsync(incidentNeedId);
			if (need == null || need.DepartmentId != departmentId || !await IsCommandActiveAsync(need.IncidentCommandId))
				return null;

			var previousStatus = need.Status;
			var previousQuantityFulfilled = need.QuantityFulfilled;

			need.Status = (int)status;
			// The fulfilled quantity may move DOWN as well as up (a filling resource got called off to another incident).
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

			note = TrimToLength(note, 2000);

			// Append-only audit row: who changed what (status and/or fill), when, and their note.
			await _incidentNeedUpdateRepository.InsertAsync(new IncidentNeedUpdate
			{
				IncidentNeedUpdateId = Guid.NewGuid().ToString(),
				IncidentNeedId = need.IncidentNeedId,
				IncidentCommandId = need.IncidentCommandId,
				DepartmentId = need.DepartmentId,
				CallId = need.CallId,
				PreviousStatus = previousStatus,
				NewStatus = need.Status,
				PreviousQuantityFulfilled = previousQuantityFulfilled,
				NewQuantityFulfilled = need.QuantityFulfilled,
				Note = note,
				CreatedByUserId = userId,
				CreatedOn = DateTime.UtcNow
			}, cancellationToken);

			var actor = await ResolveUserLabelAsync(departmentId, userId);
			string change;
			if (status == IncidentNeedStatus.Met)
				change = "filled";
			else if (status == IncidentNeedStatus.Cancelled)
				change = need.QuantityRequested > 0 && need.QuantityFulfilled < need.QuantityRequested ? "closed without being filled" : "closed";
			else if (need.QuantityFulfilled != previousQuantityFulfilled)
				change = $"fill changed {previousQuantityFulfilled} to {need.QuantityFulfilled}{(need.QuantityRequested > 0 ? $" of {need.QuantityRequested}" : string.Empty)}";
			else
				change = $"status changed to {status}";

			var description = $"Need '{need.Name}' {change} by {actor}";
			if (!string.IsNullOrWhiteSpace(note))
				description = $"{description}: {note}";

			await WriteLogAsync(need.IncidentCommandId, need.DepartmentId, need.CallId,
				status == IncidentNeedStatus.Met ? CommandLogEntryType.NeedMet : CommandLogEntryType.NeedUpdated,
				description, userId, cancellationToken);

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

		public async Task<List<IncidentNeedUpdate>> GetNeedUpdatesAsync(int departmentId, string incidentNeedId)
		{
			if (string.IsNullOrWhiteSpace(incidentNeedId))
				return new List<IncidentNeedUpdate>();

			var items = await _incidentNeedUpdateRepository.GetAllByDepartmentIdAsync(departmentId);
			var updates = (items ?? Enumerable.Empty<IncidentNeedUpdate>())
				.Where(x => string.Equals(x.IncidentNeedId, incidentNeedId, StringComparison.OrdinalIgnoreCase))
				.OrderByDescending(x => x.CreatedOn)
				.ToList();

			// Resolve author display names in one profile batch so the UI never shows a raw user GUID.
			try
			{
				var userIds = updates.Select(x => x.CreatedByUserId).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();
				var profiles = userIds.Count > 0 ? await _userProfileService.GetSelectedUserProfilesAsync(userIds) : null;
				var profilesById = (profiles ?? new List<UserProfile>()).Where(p => p != null && !string.IsNullOrWhiteSpace(p.UserId))
					.GroupBy(p => p.UserId).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

				foreach (var update in updates)
				{
					update.CreatedByUserName = !string.IsNullOrWhiteSpace(update.CreatedByUserId) && profilesById.TryGetValue(update.CreatedByUserId, out var profile)
						? profile.FullName.AsFirstNameLastName
						: update.CreatedByUserId;
				}
			}
			catch (Exception ex)
			{
				Resgrid.Framework.Logging.LogException(ex);
			}

			return updates;
		}

		public async Task<IncidentNeed> RequestNeedEntitiesAsync(int departmentId, string incidentCommandId, string name, string description, List<IncidentNeedEntity> entities, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (string.IsNullOrWhiteSpace(name) || entities == null || entities.Count == 0)
				throw new ArgumentException("A need name and at least one requested entity are required.");
			if (entities.Any(e => !Enum.IsDefined(typeof(NeedEntityKind), e.EntityKind) || string.IsNullOrWhiteSpace(e.EntityId)))
				throw new ArgumentException("Every requested entity needs a valid kind and id.");

			var command = await GetOwnedCommandAsync(incidentCommandId, departmentId);
			if (command == null)
				return null;

			var need = new IncidentNeed
			{
				IncidentNeedId = Guid.NewGuid().ToString(),
				IncidentCommandId = command.IncidentCommandId,
				DepartmentId = departmentId,
				CallId = command.CallId,
				Name = TrimToLength(name, 500),
				Description = TrimToLength(description, 2000),
				Category = (int)IncidentNeedCategory.Entity,
				Status = (int)IncidentNeedStatus.Open,
				QuantityRequested = entities.Count,
				CreatedByUserId = userId,
				CreatedOn = DateTime.UtcNow
			};
			need = await _incidentNeedRepository.InsertAsync(Touch(need), cancellationToken);

			// Resolve display names + persist the request rows.
			var labels = new List<string>();
			foreach (var entity in entities)
			{
				entity.IncidentNeedEntityId = Guid.NewGuid().ToString();
				entity.IncidentNeedId = need.IncidentNeedId;
				entity.IncidentCommandId = command.IncidentCommandId;
				entity.DepartmentId = departmentId;
				entity.CallId = command.CallId;
				entity.CreatedByUserId = userId;
				entity.CreatedOn = DateTime.UtcNow;
				entity.EntityName = TrimToLength(await ResolveNeedEntityNameAsync(departmentId, entity), 500);
				labels.Add(entity.EntityName ?? entity.EntityId);
			}

			// Add the entities to the call and dispatch ONLY them (no full re-dispatch), tagged as a
			// command request. Failures here must not lose the need itself.
			var dispatched = false;
			try
			{
				dispatched = await DispatchNeedEntitiesAsync(departmentId, command.CallId, entities, cancellationToken);
			}
			catch (Exception ex)
			{
				Resgrid.Framework.Logging.LogException(ex);
			}

			foreach (var entity in entities)
			{
				if (dispatched)
					entity.DispatchedOn = DateTime.UtcNow;
				await _incidentNeedEntityRepository.InsertAsync(entity, cancellationToken);
			}

			var author = await BuildNoteAuthorLabelAsync(command, userId);
			await WriteLogAsync(need.IncidentCommandId, need.DepartmentId, need.CallId, CommandLogEntryType.NeedAdded,
				$"Need '{need.Name}' requested by {author}: {string.Join(", ", labels)}{(dispatched ? " — dispatched to the call individually" : string.Empty)}", userId, cancellationToken);

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

		public async Task<List<IncidentNeedEntity>> GetNeedEntitiesAsync(int departmentId, string incidentNeedId)
		{
			if (string.IsNullOrWhiteSpace(incidentNeedId))
				return new List<IncidentNeedEntity>();

			var items = await _incidentNeedEntityRepository.GetAllByDepartmentIdAsync(departmentId);
			return (items ?? Enumerable.Empty<IncidentNeedEntity>())
				.Where(x => string.Equals(x.IncidentNeedId, incidentNeedId, StringComparison.OrdinalIgnoreCase))
				.OrderBy(x => x.CreatedOn)
				.ToList();
		}

		public async Task RecordNeedEntityStatusAsync(int departmentId, int callId, NeedEntityKind entityKind, string entityId, string statusText, string savedByUserId, CancellationToken cancellationToken = default(CancellationToken))
		{
			try
			{
				var command = await GetActiveCommandForCallAsync(departmentId, callId);
				if (command == null)
					return;

				var all = await _incidentNeedEntityRepository.GetAllByDepartmentIdAsync(departmentId);
				var callEntities = (all ?? Enumerable.Empty<IncidentNeedEntity>()).Where(x => x.CallId == callId).ToList();
				if (callEntities.Count == 0)
					return;

				// Direct match (the unit/user itself was requested) …
				var matches = callEntities.Where(x => x.EntityKind == (int)entityKind && string.Equals(x.EntityId, entityId, StringComparison.OrdinalIgnoreCase)).ToList();

				// … or an indirect match: the responder satisfies a requested role/group.
				if (entityKind == NeedEntityKind.User)
				{
					if (callEntities.Any(x => x.EntityKind == (int)NeedEntityKind.Group))
					{
						var group = await _departmentGroupsService.GetGroupForUserAsync(entityId, departmentId);
						if (group != null)
							matches.AddRange(callEntities.Where(x => x.EntityKind == (int)NeedEntityKind.Group && x.EntityId == group.DepartmentGroupId.ToString()));
					}
					if (callEntities.Any(x => x.EntityKind == (int)NeedEntityKind.Role))
					{
						var roles = await _personnelRolesService.GetRolesForUserAsync(entityId, departmentId);
						var roleIds = new HashSet<string>((roles ?? new List<PersonnelRole>()).Select(r => r.PersonnelRoleId.ToString()));
						matches.AddRange(callEntities.Where(x => x.EntityKind == (int)NeedEntityKind.Role && roleIds.Contains(x.EntityId)));
					}
				}
				else if (entityKind == NeedEntityKind.Unit && callEntities.Any(x => x.EntityKind == (int)NeedEntityKind.Group) && int.TryParse(entityId, out var respondingUnitId))
				{
					var unit = await _unitsService.GetUnitByIdAsync(respondingUnitId);
					if (unit?.StationGroupId != null)
						matches.AddRange(callEntities.Where(x => x.EntityKind == (int)NeedEntityKind.Group && x.EntityId == unit.StationGroupId.Value.ToString()));
				}

				if (matches.Count == 0)
					return;

				var needs = await _incidentNeedRepository.GetAllByDepartmentIdAsync(departmentId);
				var needNamesById = (needs ?? Enumerable.Empty<IncidentNeed>()).GroupBy(n => n.IncidentNeedId).ToDictionary(g => g.Key, g => g.First().Name);

				var label = entityKind == NeedEntityKind.Unit && int.TryParse(entityId, out var labelUnitId)
					? await ResolveUnitLabelAsync(labelUnitId)
					: await ResolveUserLabelAsync(departmentId, entityId);

				foreach (var needId in matches.Select(m => m.IncidentNeedId).Distinct())
				{
					var needName = needNamesById.TryGetValue(needId, out var resolved) ? resolved : "Entity request";
					await WriteLogAsync(command.IncidentCommandId, departmentId, callId, CommandLogEntryType.NeedUpdated,
						$"{label} responded to need '{needName}' — {statusText}", savedByUserId, cancellationToken);
				}
			}
			catch (Exception ex)
			{
				// A response-logging failure must never break the underlying status save.
				Resgrid.Framework.Logging.LogException(ex);
			}
		}

		/// <summary>Display-name snapshot for a requested entity (never a raw id in log text).</summary>
		private async Task<string> ResolveNeedEntityNameAsync(int departmentId, IncidentNeedEntity entity)
		{
			try
			{
				switch ((NeedEntityKind)entity.EntityKind)
				{
					case NeedEntityKind.Unit:
						if (int.TryParse(entity.EntityId, out var unitId))
							return await ResolveUnitLabelAsync(unitId);
						break;
					case NeedEntityKind.User:
						return await ResolveUserLabelAsync(departmentId, entity.EntityId);
					case NeedEntityKind.Role:
						if (int.TryParse(entity.EntityId, out var roleId))
						{
							var role = await _personnelRolesService.GetRoleByIdAsync(roleId);
							if (role != null)
								return $"role '{role.Name}'";
						}
						break;
					case NeedEntityKind.Group:
						if (int.TryParse(entity.EntityId, out var groupId))
						{
							var group = await _departmentGroupsService.GetGroupByIdAsync(groupId);
							if (group != null)
								return $"group '{group.Name}'";
						}
						break;
				}
			}
			catch (Exception ex)
			{
				Resgrid.Framework.Logging.LogException(ex);
			}
			return entity.EntityId;
		}

		/// <summary>
		/// Adds the requested entities to the call's dispatch lists and broadcasts ONLY them (the same
		/// subset-dispatch primitive EditCall uses), tagging the notification as a command request.
		/// </summary>
		private async Task<bool> DispatchNeedEntitiesAsync(int departmentId, int callId, List<IncidentNeedEntity> entities, CancellationToken cancellationToken)
		{
			var call = await _callsService.GetCallByIdAsync(callId);
			if (call == null || call.DepartmentId != departmentId)
				return false;
			call = await _callsService.PopulateCallData(call, true, false, false, true, true, true, false, false, false);

			// Defensive: a call with no prior dispatches of a type can come back with a null collection.
			call.Dispatches ??= new List<CallDispatch>();
			call.GroupDispatches ??= new List<CallDispatchGroup>();
			call.UnitDispatches ??= new List<CallDispatchUnit>();
			call.RoleDispatches ??= new List<CallDispatchRole>();

			var newUserIds = new List<string>();
			var newGroupIds = new List<int>();
			var newUnitIds = new List<int>();
			var newRoleIds = new List<int>();

			foreach (var entity in entities)
			{
				switch ((NeedEntityKind)entity.EntityKind)
				{
					case NeedEntityKind.Unit:
						if (int.TryParse(entity.EntityId, out var unitId) && !call.HasUnitBeenDispatched(unitId))
						{
							call.UnitDispatches.Add(new CallDispatchUnit { CallId = call.CallId, UnitId = unitId });
							newUnitIds.Add(unitId);
						}
						break;
					case NeedEntityKind.User:
						if (!call.HasUserBeenDispatched(entity.EntityId))
						{
							call.Dispatches.Add(new CallDispatch { CallId = call.CallId, UserId = entity.EntityId });
							newUserIds.Add(entity.EntityId);
						}
						break;
					case NeedEntityKind.Group:
						if (int.TryParse(entity.EntityId, out var groupId) && !call.HasGroupBeenDispatched(groupId))
						{
							call.GroupDispatches.Add(new CallDispatchGroup { CallId = call.CallId, DepartmentGroupId = groupId });
							newGroupIds.Add(groupId);
						}
						break;
					case NeedEntityKind.Role:
						if (int.TryParse(entity.EntityId, out var roleId) && (call.RoleDispatches == null || !call.RoleDispatches.Any(r => r.RoleId == roleId)))
						{
							call.RoleDispatches.Add(new CallDispatchRole { CallId = call.CallId, RoleId = roleId });
							newRoleIds.Add(roleId);
						}
						break;
				}
			}

			if (newUserIds.Count == 0 && newGroupIds.Count == 0 && newUnitIds.Count == 0 && newRoleIds.Count == 0)
				return false;

			call = await _callsService.SaveCallAsync(call, cancellationToken);

			try
			{
				await _callDispatchStatusService.ApplyDispatchStatusesAsync(call, newGroupIds, newUnitIds, cancellationToken);
			}
			catch (Exception ex)
			{
				Resgrid.Framework.Logging.LogException(ex);
			}

			var cqi = new CallQueueItem { Call = call };
			cqi.SetBroadcastDispatches(newUserIds, newGroupIds, newUnitIds, newRoleIds);
			// The queue item's Call is a deep clone used only for notification content — tag it so the
			// dispatched resources see this came from Incident Command, without touching the stored call.
			cqi.Call.NatureOfCall = string.IsNullOrWhiteSpace(cqi.Call.NatureOfCall)
				? "Requested by Incident Command"
				: $"{cqi.Call.NatureOfCall} [Requested by Incident Command]";

			try
			{
				var profiles = await _userProfileService.GetAllProfilesForDepartmentAsync(departmentId);
				cqi.Profiles = profiles?.Values.ToList();
			}
			catch (Exception ex)
			{
				Resgrid.Framework.Logging.LogException(ex);
			}

			await _queueService.EnqueueCallBroadcastAsync(cqi, cancellationToken);
			return true;
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
			if (timer == null || timer.DepartmentId != departmentId || !await IsCommandActiveAsync(timer.IncidentCommandId))
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

			// Every map edit is logged with the author's name and command/ICS standing (spec: who + role + when).
			var annotationCommand = await GetCommandByIdAsync(annotation.IncidentCommandId);
			var annotationAuthor = annotationCommand != null ? await BuildNoteAuthorLabelAsync(annotationCommand, userId) : await ResolveUserLabelAsync(annotation.DepartmentId, userId);
			var annotationKind = string.IsNullOrWhiteSpace(annotation.Label) ? ((IncidentMapAnnotationType)annotation.AnnotationType).ToString() : $"{(IncidentMapAnnotationType)annotation.AnnotationType} '{annotation.Label}'";
			await WriteLogAsync(annotation.IncidentCommandId, annotation.DepartmentId, annotation.CallId, CommandLogEntryType.AnnotationAdded,
				$"Incident map {(isNew ? "markup added" : "markup updated")} ({annotationKind}) by {annotationAuthor}", userId, cancellationToken);

			return annotation;
		}

		public async Task<bool> DeleteAnnotationAsync(int departmentId, string incidentMapAnnotationId, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var annotation = await _incidentMapAnnotationRepository.GetByIdAsync(incidentMapAnnotationId);
			if (annotation == null || annotation.DepartmentId != departmentId || !await IsCommandActiveAsync(annotation.IncidentCommandId))
				return false;

			annotation.DeletedOn = DateTime.UtcNow;
			await _incidentMapAnnotationRepository.SaveOrUpdateAsync(Touch(annotation), cancellationToken);

			var removalCommand = await GetCommandByIdAsync(annotation.IncidentCommandId);
			var removalAuthor = removalCommand != null ? await BuildNoteAuthorLabelAsync(removalCommand, userId) : await ResolveUserLabelAsync(annotation.DepartmentId, userId);
			await WriteLogAsync(annotation.IncidentCommandId, annotation.DepartmentId, annotation.CallId, CommandLogEntryType.AnnotationRemoved,
				$"Incident map markup removed by {removalAuthor}", userId, cancellationToken);
			return true;
		}

		public async Task<List<IncidentMapAnnotation>> GetAnnotationsForCallAsync(int departmentId, int callId)
		{
			var items = await _incidentMapAnnotationRepository.GetAllByDepartmentIdAsync(departmentId);
			if (items == null)
				return new List<IncidentMapAnnotation>();

			return items.Where(x => x.CallId == callId && x.DeletedOn == null).ToList();
		}

		public async Task<IncidentCommand> UpdateMapViewAsync(int departmentId, string incidentCommandId, string centerLatitude, string centerLongitude, string zoomLevel, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (!TryParseCoordinates(centerLatitude, centerLongitude, out var latitudeValue, out var longitudeValue))
				throw new ArgumentException("A valid map center latitude and longitude are required.");
			if (!decimal.TryParse(zoomLevel?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var zoomValue) || zoomValue < 0 || zoomValue > 22)
				throw new ArgumentException("A valid map zoom level (0-22) is required.");

			var command = await GetOwnedCommandAsync(incidentCommandId, departmentId);
			if (command == null)
				return null;

			var isNew = string.IsNullOrWhiteSpace(command.MapZoomLevel);
			command.MapCenterLatitude = latitudeValue.ToString("0.######", CultureInfo.InvariantCulture);
			command.MapCenterLongitude = longitudeValue.ToString("0.######", CultureInfo.InvariantCulture);
			command.MapZoomLevel = zoomValue.ToString("0.##", CultureInfo.InvariantCulture);
			command = await _incidentCommandRepository.SaveOrUpdateAsync(Touch(command), cancellationToken);

			// Log with the author's name and command/ICS standing (spec: who + role + when).
			var author = await BuildNoteAuthorLabelAsync(command, userId);
			await WriteLogAsync(command.IncidentCommandId, command.DepartmentId, command.CallId, CommandLogEntryType.MapViewUpdated,
				$"Incident map {(isNew ? "created" : "view updated")} by {author}", userId, cancellationToken);

			return command;
		}

		public async Task<IncidentMap> SaveIncidentMapAsync(IncidentMap map, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (map == null || string.IsNullOrWhiteSpace(map.IncidentCommandId) || string.IsNullOrWhiteSpace(map.Name))
				throw new ArgumentException("An incident command and map name are required.");
			if (!string.IsNullOrWhiteSpace(map.CenterLatitude) || !string.IsNullOrWhiteSpace(map.CenterLongitude))
			{
				if (!TryParseCoordinates(map.CenterLatitude, map.CenterLongitude, out _, out _))
					throw new ArgumentException("A valid map center latitude and longitude are required.");
			}

			var command = await GetOwnedCommandAsync(map.IncidentCommandId, map.DepartmentId);
			if (command == null)
				return null;
			map.CallId = command.CallId;
			map.Name = TrimToLength(map.Name, 500);
			map.Description = TrimToLength(map.Description, 2000);

			// Server-stamped create audit; on updates the preserve callback restores the stored values.
			map.CreatedByUserId = userId;
			map.CreatedOn = DateTime.UtcNow;

			var (saved, isNew, rejected) = await UpsertOwnedAsync(_incidentMapRepository, map, map.DepartmentId,
				e => e.DepartmentId, (stored, incoming) =>
				{
					incoming.CreatedByUserId = stored.CreatedByUserId;
					incoming.CreatedOn = stored.CreatedOn;
					incoming.DeletedOn = stored.DeletedOn;
					incoming.UpdatedByUserId = userId;
					incoming.UpdatedOn = DateTime.UtcNow;
				}, cancellationToken);
			if (rejected)
				return null;
			map = saved;

			// Log with the author's name and command/ICS standing (spec: who + role + when).
			var author = await BuildNoteAuthorLabelAsync(command, userId);
			await WriteLogAsync(map.IncidentCommandId, map.DepartmentId, map.CallId, CommandLogEntryType.MapViewUpdated,
				$"Incident map '{map.Name}' {(isNew ? "created" : "updated")} by {author}", userId, cancellationToken);

			return map;
		}

		public async Task<bool> DeleteIncidentMapAsync(int departmentId, string incidentMapId, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var map = await _incidentMapRepository.GetByIdAsync(incidentMapId);
			if (map == null || map.DepartmentId != departmentId || map.DeletedOn.HasValue || !await IsCommandActiveAsync(map.IncidentCommandId))
				return false;

			map.DeletedOn = DateTime.UtcNow;
			await _incidentMapRepository.SaveOrUpdateAsync(Touch(map), cancellationToken);

			var mapCommand = await GetCommandByIdAsync(map.IncidentCommandId);
			var author = mapCommand != null ? await BuildNoteAuthorLabelAsync(mapCommand, userId) : await ResolveUserLabelAsync(departmentId, userId);
			await WriteLogAsync(map.IncidentCommandId, map.DepartmentId, map.CallId, CommandLogEntryType.MapViewUpdated,
				$"Incident map '{map.Name}' removed by {author}", userId, cancellationToken);
			return true;
		}

		public async Task<List<IncidentMap>> GetIncidentMapsForCallAsync(int departmentId, int callId)
		{
			var items = await _incidentMapRepository.GetAllByDepartmentIdAsync(departmentId);
			if (items == null)
				return new List<IncidentMap>();

			return items.Where(x => x.CallId == callId && x.DeletedOn == null).OrderBy(x => x.CreatedOn).ToList();
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
		/// must not be trusted to supply a CallId that matches the parent command. By default a CLOSED (ended)
		/// command is also rejected: an ended command is read-only.
		/// </summary>
		private async Task<IncidentCommand> GetOwnedCommandAsync(string incidentCommandId, int departmentId, bool requireActive = true)
		{
			if (string.IsNullOrWhiteSpace(incidentCommandId))
				return null;

			var command = await _incidentCommandRepository.GetByIdAsync(incidentCommandId);
			if (command == null || command.DepartmentId != departmentId)
				return null;

			return requireActive && command.Status != (int)IncidentCommandStatus.Active ? null : command;
		}

		/// <summary>An ended (closed) command is read-only — mutations on its child rows are rejected.</summary>
		private async Task<bool> IsCommandActiveAsync(string incidentCommandId)
		{
			if (string.IsNullOrWhiteSpace(incidentCommandId))
				return false;

			var command = await _incidentCommandRepository.GetByIdAsync(incidentCommandId);
			return command != null && command.Status == (int)IncidentCommandStatus.Active;
		}

		/// <summary>
		/// Human label for a member: "Full Name (Group Name)". Falls back progressively (name only, then the raw
		/// user id) so log text composition never throws.
		/// </summary>
		private async Task<string> ResolveUserLabelAsync(int departmentId, string userId, string knownName = null)
		{
			if (string.IsNullOrWhiteSpace(userId))
				return string.IsNullOrWhiteSpace(knownName) ? "Unknown member" : knownName;

			var label = string.IsNullOrWhiteSpace(knownName) ? userId : knownName;
			try
			{
				if (string.IsNullOrWhiteSpace(knownName))
				{
					var profile = await _userProfileService.GetProfileByUserIdAsync(userId);
					if (profile != null)
						label = profile.FullName.AsFirstNameLastName;
				}

				var group = await _departmentGroupsService.GetGroupForUserAsync(userId, departmentId);
				if (!string.IsNullOrWhiteSpace(group?.Name))
					label = $"{label} ({group.Name})";
			}
			catch (Exception ex)
			{
				Resgrid.Framework.Logging.LogException(ex);
			}
			return label;
		}

		/// <summary>Human label for a unit: "Unit Name (Station/Group Name)".</summary>
		private async Task<string> ResolveUnitLabelAsync(int unitId)
		{
			var label = $"Unit {unitId}";
			try
			{
				var unit = await _unitsService.GetUnitByIdAsync(unitId);
				if (unit != null)
				{
					label = unit.Name;
					if (unit.StationGroupId.HasValue)
					{
						var group = await _departmentGroupsService.GetGroupByIdAsync(unit.StationGroupId.Value);
						if (!string.IsNullOrWhiteSpace(group?.Name))
							label = $"{label} ({group.Name})";
					}
				}
			}
			catch (Exception ex)
			{
				Resgrid.Framework.Logging.LogException(ex);
			}
			return label;
		}

		/// <summary>
		/// Human label for a polymorphic resource assignment target so timeline text never shows a raw id/GUID:
		/// units as "Name (Group)", personnel as "Full Name (Group)", ad-hoc resources as "Name (Agency)".
		/// </summary>
		private async Task<string> ResolveResourceLabelAsync(int departmentId, int resourceKind, string resourceId)
		{
			try
			{
				switch ((ResourceAssignmentKind)resourceKind)
				{
					case ResourceAssignmentKind.RealUnit:
					case ResourceAssignmentKind.LinkedDeptUnit:
						if (int.TryParse(resourceId, out var unitId))
							return await ResolveUnitLabelAsync(unitId);
						break;
					case ResourceAssignmentKind.RealPersonnel:
					case ResourceAssignmentKind.LinkedDeptPersonnel:
						return await ResolveUserLabelAsync(departmentId, resourceId);
					case ResourceAssignmentKind.AdHocUnit:
						var adHocUnit = await _incidentAdHocUnitRepository.GetByIdAsync(resourceId);
						if (adHocUnit != null)
							return string.IsNullOrWhiteSpace(adHocUnit.ExternalAgencyName) ? adHocUnit.Name : $"{adHocUnit.Name} ({adHocUnit.ExternalAgencyName})";
						break;
					case ResourceAssignmentKind.AdHocPersonnel:
						var adHocPersonnel = await _incidentAdHocPersonnelRepository.GetByIdAsync(resourceId);
						if (adHocPersonnel != null)
							return string.IsNullOrWhiteSpace(adHocPersonnel.ExternalAgencyName) ? adHocPersonnel.Name : $"{adHocPersonnel.Name} ({adHocPersonnel.ExternalAgencyName})";
						break;
				}
			}
			catch (Exception ex)
			{
				Resgrid.Framework.Logging.LogException(ex);
			}
			return "Unknown resource";
		}

		/// <summary>
		/// Note-author attribution for the timeline: "Full Name (Group) [Incident Commander, Safety Officer]" —
		/// the bracket names the author's command standing (IC and/or any ICS roles held on this incident).
		/// </summary>
		private async Task<string> BuildNoteAuthorLabelAsync(IncidentCommand command, string userId)
		{
			var label = await ResolveUserLabelAsync(command.DepartmentId, userId);

			var standings = new List<string>();
			if (string.Equals(userId, command.CurrentCommanderUserId, StringComparison.OrdinalIgnoreCase))
				standings.Add("Incident Commander");

			try
			{
				var roles = await GetIncidentRolesAsync(command.DepartmentId, command.CallId);
				standings.AddRange(roles.Where(r => string.Equals(r.UserId, userId, StringComparison.OrdinalIgnoreCase))
					.Select(r => ((IncidentRoleType)r.RoleType).ToString()));
			}
			catch (Exception ex)
			{
				Resgrid.Framework.Logging.LogException(ex);
			}

			return standings.Count > 0 ? $"{label} [{string.Join(", ", standings.Distinct())}]" : label;
		}

		/// <summary>Forward-geocodes a free-text location; (null, null) when it can't be resolved.</summary>
		private async Task<(string latitude, string longitude)> GeocodeLocationTextAsync(string locationText)
		{
			if (string.IsNullOrWhiteSpace(locationText))
				return (null, null);

			try
			{
				var result = await _geoLocationProvider.GetLatLonFromAddress(locationText.Trim());
				var parts = result?.Split(',');
				if (parts != null && parts.Length >= 2 && TryParseCoordinates(parts[0], parts[1], out var latitude, out var longitude))
					return (latitude.ToString("0.######", CultureInfo.InvariantCulture), longitude.ToString("0.######", CultureInfo.InvariantCulture));
			}
			catch (Exception ex)
			{
				Resgrid.Framework.Logging.LogException(ex);
			}
			return (null, null);
		}

		/// <summary>
		/// Merges one location edit (free text + coordinates) over the stored values: null inputs leave the stored
		/// value, empty strings clear it, and supplied coordinates must parse as a valid pair. Whenever the merged
		/// text is set while the merged coordinates are blank, the text is geocoded to backfill them.
		/// </summary>
		private async Task<(string text, string latitude, string longitude, bool changed)> ApplyLocationEditAsync(
			string currentText, string currentLatitude, string currentLongitude,
			string newText, string newLatitude, string newLongitude)
		{
			var text = newText == null ? currentText : TrimToLength(newText, 1000);

			string latitude;
			string longitude;
			if (newLatitude == null && newLongitude == null)
			{
				latitude = currentLatitude;
				longitude = currentLongitude;
			}
			else if (string.IsNullOrWhiteSpace(newLatitude) || string.IsNullOrWhiteSpace(newLongitude))
			{
				latitude = null;
				longitude = null;
			}
			else
			{
				if (!TryParseCoordinates(newLatitude, newLongitude, out var latitudeValue, out var longitudeValue))
					throw new ArgumentException("A valid latitude and longitude are required.");
				latitude = latitudeValue.ToString("0.######", CultureInfo.InvariantCulture);
				longitude = longitudeValue.ToString("0.######", CultureInfo.InvariantCulture);
			}

			if (!string.IsNullOrWhiteSpace(text) && (string.IsNullOrWhiteSpace(latitude) || string.IsNullOrWhiteSpace(longitude)))
			{
				var geocoded = await GeocodeLocationTextAsync(text);
				if (geocoded.latitude != null)
				{
					latitude = geocoded.latitude;
					longitude = geocoded.longitude;
				}
			}

			var changed = !string.Equals(text ?? string.Empty, currentText ?? string.Empty) ||
				!string.Equals(latitude ?? string.Empty, currentLatitude ?? string.Empty) ||
				!string.Equals(longitude ?? string.Empty, currentLongitude ?? string.Empty);
			return (text, latitude, longitude, changed);
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
