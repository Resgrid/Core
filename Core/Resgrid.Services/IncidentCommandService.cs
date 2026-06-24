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

			command = await _incidentCommandRepository.SaveOrUpdateAsync(command, cancellationToken);

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

						await _commandStructureNodeRepository.SaveOrUpdateAsync(node, cancellationToken);
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
			// The parent incident command must belong to the caller's department.
			if (!await CommandBelongsToDepartmentAsync(assignment.IncidentCommandId, assignment.DepartmentId))
				return null;

			if (string.IsNullOrWhiteSpace(assignment.IncidentRoleAssignmentId))
			{
				assignment.IncidentRoleAssignmentId = Guid.NewGuid().ToString();
			}
			else
			{
				// On update, the existing row must belong to the caller's department.
				var existing = await _incidentRoleAssignmentRepository.GetByIdAsync(assignment.IncidentRoleAssignmentId);
				if (existing == null || existing.DepartmentId != assignment.DepartmentId)
					return null;
			}

			assignment.AssignedByUserId = userId;
			if (assignment.AssignedOn == default(DateTime))
				assignment.AssignedOn = DateTime.UtcNow;

			assignment = await _incidentRoleAssignmentRepository.SaveOrUpdateAsync(assignment, cancellationToken);

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
			await _incidentRoleAssignmentRepository.SaveOrUpdateAsync(assignment, cancellationToken);

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

		public async Task<IncidentCommand> CloseCommandAsync(int departmentId, string incidentCommandId, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var command = await _incidentCommandRepository.GetByIdAsync(incidentCommandId);
			if (command == null || command.DepartmentId != departmentId)
				return null;

			command.Status = (int)IncidentCommandStatus.Closed;
			command.ClosedOn = DateTime.UtcNow;
			command = await _incidentCommandRepository.SaveOrUpdateAsync(command, cancellationToken);

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
			await _incidentCommandRepository.SaveOrUpdateAsync(command, cancellationToken);

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
			transfer = await _commandTransferRepository.SaveOrUpdateAsync(transfer, cancellationToken);

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
			command = await _incidentCommandRepository.SaveOrUpdateAsync(command, cancellationToken);

			await WriteLogAsync(command.IncidentCommandId, command.DepartmentId, command.CallId, CommandLogEntryType.Note, "Incident action plan updated", userId, cancellationToken);
			return command;
		}

		#endregion Command lifecycle

		#region Structure (lanes)

		public async Task<CommandStructureNode> SaveNodeAsync(CommandStructureNode node, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			// The parent incident command must belong to the caller's department (node.DepartmentId is set
			// from the authenticated claim by the controller).
			if (!await CommandBelongsToDepartmentAsync(node.IncidentCommandId, node.DepartmentId))
				return null;

			var isNew = string.IsNullOrWhiteSpace(node.CommandStructureNodeId);
			if (isNew)
			{
				node.CommandStructureNodeId = Guid.NewGuid().ToString();
			}
			else
			{
				// On update, the existing row must belong to the caller's department (no foreign-row takeover).
				var existing = await _commandStructureNodeRepository.GetByIdAsync(node.CommandStructureNodeId);
				if (existing == null || existing.DepartmentId != node.DepartmentId)
					return null;
			}

			node = await _commandStructureNodeRepository.SaveOrUpdateAsync(node, cancellationToken);

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

			var result = await _commandStructureNodeRepository.DeleteAsync(node, cancellationToken);

			await WriteLogAsync(node.IncidentCommandId, node.DepartmentId, node.CallId, CommandLogEntryType.NodeRemoved, $"Lane '{node.Name}' removed", userId, cancellationToken);
			return result;
		}

		public async Task<List<CommandStructureNode>> GetNodesForCallAsync(int departmentId, int callId)
		{
			var items = await _commandStructureNodeRepository.GetAllByDepartmentIdAsync(departmentId);
			if (items == null)
				return new List<CommandStructureNode>();

			return items.Where(x => x.CallId == callId).OrderBy(x => x.SortOrder).ToList();
		}

		#endregion Structure (lanes)

		#region Resource assignments

		public async Task<ResourceAssignment> AssignResourceAsync(ResourceAssignment assignment, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			// The parent incident command must belong to the caller's department.
			if (!await CommandBelongsToDepartmentAsync(assignment.IncidentCommandId, assignment.DepartmentId))
				return null;

			if (string.IsNullOrWhiteSpace(assignment.ResourceAssignmentId))
			{
				assignment.ResourceAssignmentId = Guid.NewGuid().ToString();
			}
			else
			{
				// On update, the existing row must belong to the caller's department.
				var existing = await _resourceAssignmentRepository.GetByIdAsync(assignment.ResourceAssignmentId);
				if (existing == null || existing.DepartmentId != assignment.DepartmentId)
					return null;
			}

			if (assignment.AssignedOn == default(DateTime))
				assignment.AssignedOn = DateTime.UtcNow;

			assignment.AssignedByUserId = userId;
			assignment = await _resourceAssignmentRepository.SaveOrUpdateAsync(assignment, cancellationToken);

			await WriteLogAsync(assignment.IncidentCommandId, assignment.DepartmentId, assignment.CallId, CommandLogEntryType.ResourceAssigned, "Resource assigned", userId, cancellationToken);

			_eventAggregator.SendMessage<IncidentResourceAssignedEvent>(new IncidentResourceAssignedEvent { DepartmentId = assignment.DepartmentId, CallId = assignment.CallId, IncidentCommandId = assignment.IncidentCommandId, ResourceKind = assignment.ResourceKind, ResourceId = assignment.ResourceId });
			return assignment;
		}

		public async Task<ResourceAssignment> MoveResourceAsync(int departmentId, string resourceAssignmentId, string targetNodeId, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var assignment = await _resourceAssignmentRepository.GetByIdAsync(resourceAssignmentId);
			if (assignment == null || assignment.DepartmentId != departmentId)
				return null;

			assignment.CommandStructureNodeId = targetNodeId;
			assignment = await _resourceAssignmentRepository.SaveOrUpdateAsync(assignment, cancellationToken);

			await WriteLogAsync(assignment.IncidentCommandId, assignment.DepartmentId, assignment.CallId, CommandLogEntryType.ResourceMoved, "Resource moved", userId, cancellationToken);
			return assignment;
		}

		public async Task<bool> ReleaseResourceAsync(int departmentId, string resourceAssignmentId, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var assignment = await _resourceAssignmentRepository.GetByIdAsync(resourceAssignmentId);
			if (assignment == null || assignment.DepartmentId != departmentId)
				return false;

			assignment.ReleasedOn = DateTime.UtcNow;
			await _resourceAssignmentRepository.SaveOrUpdateAsync(assignment, cancellationToken);

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
			// The parent incident command must belong to the caller's department.
			if (!await CommandBelongsToDepartmentAsync(objective.IncidentCommandId, objective.DepartmentId))
				return null;

			var isNew = string.IsNullOrWhiteSpace(objective.TacticalObjectiveId);
			if (isNew)
			{
				objective.TacticalObjectiveId = Guid.NewGuid().ToString();
			}
			else
			{
				// On update, the existing row must belong to the caller's department.
				var existing = await _tacticalObjectiveRepository.GetByIdAsync(objective.TacticalObjectiveId);
				if (existing == null || existing.DepartmentId != objective.DepartmentId)
					return null;
			}

			objective = await _tacticalObjectiveRepository.SaveOrUpdateAsync(objective, cancellationToken);

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
			objective = await _tacticalObjectiveRepository.SaveOrUpdateAsync(objective, cancellationToken);

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
			// The parent incident command must belong to the caller's department.
			if (!await CommandBelongsToDepartmentAsync(timer.IncidentCommandId, timer.DepartmentId))
				return null;

			if (string.IsNullOrWhiteSpace(timer.IncidentTimerId))
			{
				timer.IncidentTimerId = Guid.NewGuid().ToString();
			}
			else
			{
				// On update, the existing row must belong to the caller's department.
				var existing = await _incidentTimerRepository.GetByIdAsync(timer.IncidentTimerId);
				if (existing == null || existing.DepartmentId != timer.DepartmentId)
					return null;
			}

			timer.StartedOn = DateTime.UtcNow;
			timer.Status = (int)IncidentTimerStatus.Running;
			if (timer.IntervalSeconds > 0)
				timer.NextDueOn = timer.StartedOn.AddSeconds(timer.IntervalSeconds);

			timer = await _incidentTimerRepository.SaveOrUpdateAsync(timer, cancellationToken);

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

			timer = await _incidentTimerRepository.SaveOrUpdateAsync(timer, cancellationToken);

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
			// The parent incident command must belong to the caller's department.
			if (!await CommandBelongsToDepartmentAsync(annotation.IncidentCommandId, annotation.DepartmentId))
				return null;

			var isNew = string.IsNullOrWhiteSpace(annotation.IncidentMapAnnotationId);
			if (isNew)
			{
				annotation.IncidentMapAnnotationId = Guid.NewGuid().ToString();
				annotation.CreatedOn = DateTime.UtcNow;
				annotation.CreatedByUserId = userId;
			}
			else
			{
				// On update, the existing row must belong to the caller's department.
				var existing = await _incidentMapAnnotationRepository.GetByIdAsync(annotation.IncidentMapAnnotationId);
				if (existing == null || existing.DepartmentId != annotation.DepartmentId)
					return null;
			}

			annotation = await _incidentMapAnnotationRepository.SaveOrUpdateAsync(annotation, cancellationToken);

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
			await _incidentMapAnnotationRepository.SaveOrUpdateAsync(annotation, cancellationToken);

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
		/// Verifies the parent incident command exists and belongs to the given department. Used to gate
		/// create/update of child entities so resources can only be attached to the caller's own incidents.
		/// </summary>
		private async Task<bool> CommandBelongsToDepartmentAsync(string incidentCommandId, int departmentId)
		{
			if (string.IsNullOrWhiteSpace(incidentCommandId))
				return false;

			var command = await _incidentCommandRepository.GetByIdAsync(incidentCommandId);
			return command != null && command.DepartmentId == departmentId;
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

			var saved = await _commandLogEntryRepository.SaveOrUpdateAsync(entry, cancellationToken);

			// Real-time: every command mutation flows through here, so push one board-changed signal.
			await _coreEventService.IncidentCommandUpdatedAsync(departmentId, callId);
			return saved;
		}

		#endregion Private helpers
	}
}
