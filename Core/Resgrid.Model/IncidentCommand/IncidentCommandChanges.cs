using System.Collections.Generic;

namespace Resgrid.Model
{
	/// <summary>
	/// Delta payload for offline sync: every change-tracked incident-command row whose <see cref="IChangeTracked.ModifiedOn"/>
	/// (or, for the append-only timeline, OccurredOn) is newer than the client's last sync cursor, scoped to a department.
	/// Soft-deleted / closed / released rows ARE included (with their state columns set) so the client can remove or
	/// update them locally. The client stores <see cref="ServerTimestampMs"/> and passes it back as the next `since`.
	/// Ad-hoc resources are not change-tracked and are pulled separately (full refetch). See
	/// docs/architecture/offline-first-architecture.md.
	/// </summary>
	public class IncidentCommandChanges
	{
		/// <summary>Server clock (Unix epoch ms) captured at the start of the read; the client's next-sync cursor.</summary>
		public long ServerTimestampMs { get; set; }

		public List<IncidentCommand> Commands { get; set; } = new List<IncidentCommand>();

		public List<CommandStructureNode> Nodes { get; set; } = new List<CommandStructureNode>();

		public List<ResourceAssignment> Assignments { get; set; } = new List<ResourceAssignment>();

		public List<TacticalObjective> Objectives { get; set; } = new List<TacticalObjective>();

		public List<IncidentNeed> Needs { get; set; } = new List<IncidentNeed>();

		public List<IncidentTimer> Timers { get; set; } = new List<IncidentTimer>();

		public List<IncidentMapAnnotation> Annotations { get; set; } = new List<IncidentMapAnnotation>();

		public List<IncidentMap> Maps { get; set; } = new List<IncidentMap>();

		public List<IncidentRoleAssignment> Roles { get; set; } = new List<IncidentRoleAssignment>();

		public List<IncidentAdHocUnit> AdHocUnits { get; set; } = new List<IncidentAdHocUnit>();

		public List<IncidentAdHocPersonnel> AdHocPersonnel { get; set; } = new List<IncidentAdHocPersonnel>();

		public List<IncidentNote> Notes { get; set; } = new List<IncidentNote>();

		/// <summary>Attachment metadata only; <see cref="IncidentAttachment.Data"/> is JSON-ignored.</summary>
		public List<IncidentAttachment> Attachments { get; set; } = new List<IncidentAttachment>();

		public List<CommandLogEntry> TimelineEntries { get; set; } = new List<CommandLogEntry>();
	}
}
