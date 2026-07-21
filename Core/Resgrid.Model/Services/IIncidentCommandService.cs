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

		/// <summary>
		/// Offline-first delta pull: returns every change-tracked incident-command row (and append-only timeline entry)
		/// for the department whose ModifiedOn/OccurredOn is newer than <paramref name="sinceUtc"/>. Includes
		/// soft-deleted/closed/released rows so a reconnecting client can reconcile removals. See the offline-first doc.
		/// </summary>
		Task<IncidentCommandChanges> GetChangesSinceAsync(int departmentId, System.DateTime sinceUtc);

		/// <summary>Returns every ACTIVE incident command for the department (Status == Active).</summary>
		Task<List<IncidentCommand>> GetActiveCommandsForDepartmentAsync(int departmentId);

		/// <summary>
		/// Offline shift-start aggregate: a render-ready board (incl. computed accountability / PAR) for every active
		/// incident in the department, plus the next-sync cursor, in one pull — cutting shift-start round-trips vs.
		/// fanning out per incident. Ad-hoc resources live in a sibling service and are aggregated by the caller.
		/// </summary>
		Task<IncidentCommandBundle> GetBundleForDepartmentAsync(int departmentId, bool includeAccountability = true);

		/// <summary>
		/// Sweeps personnel accountability (PAR) for the call and raises <c>CriticalParDetectedEvent</c> once per
		/// member each time they transition into the Critical (overdue) state. Idempotent via a timeline marker —
		/// re-alerts only after a member checks in and lapses again. Returns the user ids flagged this pass (empty
		/// when nothing changed). Safe to call from a read path, a manual endpoint, or a recurring worker.
		/// </summary>
		Task<List<string>> EvaluateCriticalParAsync(int departmentId, int callId, CancellationToken cancellationToken = default(CancellationToken));

		// Incident roles (§3.11)
		Task<IncidentRoleAssignment> AssignIncidentRoleAsync(IncidentRoleAssignment assignment, string userId, CancellationToken cancellationToken = default(CancellationToken));
		Task<bool> RemoveIncidentRoleAsync(int departmentId, string incidentRoleAssignmentId, string userId, CancellationToken cancellationToken = default(CancellationToken));
		Task<List<IncidentRoleAssignment>> GetIncidentRolesAsync(int departmentId, int callId);
		Task<IncidentCapabilities> GetCapabilitiesForUserAsync(int departmentId, int callId, string userId);
		Task<IncidentCommand> CloseCommandAsync(int departmentId, string incidentCommandId, string userId, CancellationToken cancellationToken = default(CancellationToken));
		Task<CommandTransfer> TransferCommandAsync(int departmentId, string incidentCommandId, string fromUserId, string toUserId, string notes, CancellationToken cancellationToken = default(CancellationToken));
		Task<IncidentCommand> UpdateActionPlanAsync(int departmentId, string incidentCommandId, string actionPlan, string userId, CancellationToken cancellationToken = default(CancellationToken));
		Task<IncidentCommand> UpdateCommandPostAsync(int departmentId, string incidentCommandId, string latitude, string longitude, string userId, CancellationToken cancellationToken = default(CancellationToken));

		// Operational status notes
		Task<IncidentNote> AddNoteAsync(IncidentNote note, string userId, CancellationToken cancellationToken = default(CancellationToken));
		Task<List<IncidentNote>> GetNotesForCallAsync(int departmentId, int callId, bool publicOnly = false);
		Task<bool> RemoveNoteAsync(int departmentId, string incidentNoteId, string userId, CancellationToken cancellationToken = default(CancellationToken));

		// Incident-level documents/files
		Task<IncidentAttachment> AddAttachmentAsync(IncidentAttachment attachment, string userId, CancellationToken cancellationToken = default(CancellationToken));
		Task<List<IncidentAttachment>> GetAttachmentsForCallAsync(int departmentId, int callId, bool publicOnly = false);
		Task<IncidentAttachment> GetAttachmentAsync(int departmentId, string incidentAttachmentId);
		Task<bool> RemoveAttachmentAsync(int departmentId, string incidentAttachmentId, string userId, CancellationToken cancellationToken = default(CancellationToken));

		// Public incident information feed
		Task<IncidentCommand> EnablePublicSharingAsync(int departmentId, string incidentCommandId, string userId, CancellationToken cancellationToken = default(CancellationToken));
		Task<IncidentCommand> DisablePublicSharingAsync(int departmentId, string incidentCommandId, string userId, CancellationToken cancellationToken = default(CancellationToken));
		Task<IncidentPublicInformation> GetPublicInformationAsync(string publicShareToken);
		Task<IncidentAttachment> GetPublicAttachmentAsync(string publicShareToken, string incidentAttachmentId);

		// Real-time commander weather (command-post coordinates first, then call coordinates)
		Task<IncidentWeather> GetWeatherForIncidentAsync(int departmentId, int callId, CancellationToken cancellationToken = default(CancellationToken));

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

		/// <summary>
		/// Sets an objective's progress (0-100, clamped). Progress over 0 moves a Pending objective to
		/// InProgress; 100 completes it (stamping CompletedBy/On) exactly as <see cref="CompleteObjectiveAsync"/> would.
		/// </summary>
		Task<TacticalObjective> UpdateObjectiveProgressAsync(int departmentId, string tacticalObjectiveId, int progressPercent, string userId, CancellationToken cancellationToken = default(CancellationToken));

		// Command-level needs (resources/logistics/etc.)
		Task<IncidentNeed> SaveNeedAsync(IncidentNeed need, string userId, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Transitions a need's fulfillment status (optionally updating the fulfilled quantity). Transitioning
		/// to Met stamps MetBy/MetOn; leaving Met clears them.
		/// </summary>
		Task<IncidentNeed> SetNeedStatusAsync(int departmentId, string incidentNeedId, IncidentNeedStatus status, int? quantityFulfilled, string userId, CancellationToken cancellationToken = default(CancellationToken));
		Task<List<IncidentNeed>> GetNeedsForCallAsync(int departmentId, int callId);

		/// <summary>Updates command-level details every resource should see: estimated end and important information.</summary>
		Task<IncidentCommand> UpdateCommandDetailsAsync(int departmentId, string incidentCommandId, System.DateTime? estimatedEndOn, string importantInformation, string userId, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Read-only incident view for a responder (userId) or unit (unitId != null): commander contact, timing,
		/// important information, objectives, needs, visibility-filtered notes/attachments, and the caller's own
		/// lane assignment with leads and lane objectives. Null when the call has no incident command.
		/// </summary>
		Task<ResourceIncidentView> GetResourceIncidentViewAsync(int departmentId, int callId, string userId, int? unitId, bool includePrivate);

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
