using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Manages a live incident-command instance on a Call: establishing/closing/transferring command, editing the
	/// command structure (lanes), assigning resources, objectives, timers, map annotations, and the action timeline.
	/// Every mutation appends a <see cref="CommandLogEntry"/>.
	/// </summary>
	public interface IIncidentCommandService
	{
		// Command lifecycle
		Task<IncidentCommand> EstablishCommandAsync(int departmentId, int callId, string userId, int? commandDefinitionId, CancellationToken cancellationToken = default(CancellationToken));
		Task<IncidentCommand> GetActiveCommandForCallAsync(int departmentId, int callId);
		Task<IncidentCommand> GetCommandByIdAsync(string incidentCommandId);
		Task<IncidentCommand> GetCommandForCallAsync(int departmentId, int callId);
		Task<IncidentCommandBoard> GetCommandBoardAsync(int departmentId, int callId);
		Task<List<PersonnelCallCheckInStatus>> GetAccountabilityForCallAsync(int departmentId, int callId);

		// Incident roles (§3.11)
		Task<IncidentRoleAssignment> AssignIncidentRoleAsync(IncidentRoleAssignment assignment, string userId, CancellationToken cancellationToken = default(CancellationToken));
		Task<bool> RemoveIncidentRoleAsync(int departmentId, string incidentRoleAssignmentId, string userId, CancellationToken cancellationToken = default(CancellationToken));
		Task<List<IncidentRoleAssignment>> GetIncidentRolesAsync(int departmentId, int callId);
		Task<IncidentCapabilities> GetCapabilitiesForUserAsync(int departmentId, int callId, string userId);
		Task<IncidentCommand> CloseCommandAsync(int departmentId, string incidentCommandId, string userId, CancellationToken cancellationToken = default(CancellationToken));
		Task<CommandTransfer> TransferCommandAsync(int departmentId, string incidentCommandId, string fromUserId, string toUserId, string notes, CancellationToken cancellationToken = default(CancellationToken));
		Task<IncidentCommand> UpdateActionPlanAsync(int departmentId, string incidentCommandId, string actionPlan, string userId, CancellationToken cancellationToken = default(CancellationToken));

		// Structure (lanes)
		Task<CommandStructureNode> SaveNodeAsync(CommandStructureNode node, string userId, CancellationToken cancellationToken = default(CancellationToken));
		Task<bool> DeleteNodeAsync(int departmentId, string commandStructureNodeId, string userId, CancellationToken cancellationToken = default(CancellationToken));
		Task<List<CommandStructureNode>> GetNodesForCallAsync(int departmentId, int callId);

		// Resource assignments
		Task<ResourceAssignment> AssignResourceAsync(ResourceAssignment assignment, string userId, CancellationToken cancellationToken = default(CancellationToken));
		Task<ResourceAssignment> MoveResourceAsync(int departmentId, string resourceAssignmentId, string targetNodeId, string userId, CancellationToken cancellationToken = default(CancellationToken));
		Task<bool> ReleaseResourceAsync(int departmentId, string resourceAssignmentId, string userId, CancellationToken cancellationToken = default(CancellationToken));
		Task<List<ResourceAssignment>> GetAssignmentsForCallAsync(int departmentId, int callId);

		// Objectives / benchmarks
		Task<TacticalObjective> SaveObjectiveAsync(TacticalObjective objective, string userId, CancellationToken cancellationToken = default(CancellationToken));
		Task<TacticalObjective> CompleteObjectiveAsync(int departmentId, string tacticalObjectiveId, string userId, CancellationToken cancellationToken = default(CancellationToken));
		Task<List<TacticalObjective>> GetObjectivesForCallAsync(int departmentId, int callId);

		// Timers (scene/benchmark/role)
		Task<IncidentTimer> StartTimerAsync(IncidentTimer timer, string userId, CancellationToken cancellationToken = default(CancellationToken));
		Task<IncidentTimer> AcknowledgeTimerAsync(int departmentId, string incidentTimerId, string userId, CancellationToken cancellationToken = default(CancellationToken));
		Task<List<IncidentTimer>> GetActiveTimersForCallAsync(int departmentId, int callId);

		// Map annotations
		Task<IncidentMapAnnotation> SaveAnnotationAsync(IncidentMapAnnotation annotation, string userId, CancellationToken cancellationToken = default(CancellationToken));
		Task<bool> DeleteAnnotationAsync(int departmentId, string incidentMapAnnotationId, string userId, CancellationToken cancellationToken = default(CancellationToken));
		Task<List<IncidentMapAnnotation>> GetAnnotationsForCallAsync(int departmentId, int callId);

		// Timeline
		Task<List<CommandLogEntry>> GetTimelineForCallAsync(int departmentId, int callId);
		Task<CommandLogEntry> AddLogEntryAsync(string incidentCommandId, int departmentId, int callId, CommandLogEntryType type, string description, string userId, CancellationToken cancellationToken = default(CancellationToken));
	}
}
