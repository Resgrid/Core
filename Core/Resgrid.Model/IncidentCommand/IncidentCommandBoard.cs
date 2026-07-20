using System.Collections.Generic;

namespace Resgrid.Model
{
	/// <summary>
	/// Composite, one-shot snapshot of a live incident command board (Tablet Command "Real Time Sync" view).
	/// </summary>
	public class IncidentCommandBoard
	{
		public IncidentCommand Command { get; set; }

		public List<CommandStructureNode> Nodes { get; set; } = new List<CommandStructureNode>();

		public List<ResourceAssignment> Assignments { get; set; } = new List<ResourceAssignment>();

		public List<TacticalObjective> Objectives { get; set; } = new List<TacticalObjective>();

		public List<IncidentTimer> Timers { get; set; } = new List<IncidentTimer>();

		public List<IncidentMapAnnotation> Annotations { get; set; } = new List<IncidentMapAnnotation>();

		/// <summary>Personnel accountability / PAR status (from the Checkin feature) for the incident.</summary>
		public List<PersonnelCallCheckInStatus> Accountability { get; set; } = new List<PersonnelCallCheckInStatus>();

		/// <summary>Active functional command-role assignments for the incident (§3.11).</summary>
		public List<IncidentRoleAssignment> Roles { get; set; } = new List<IncidentRoleAssignment>();

		/// <summary>Active internal and public operational status notes.</summary>
		public List<IncidentNote> Notes { get; set; } = new List<IncidentNote>();

		/// <summary>Active attachment metadata; binary content is downloaded separately.</summary>
		public List<IncidentAttachment> Attachments { get; set; } = new List<IncidentAttachment>();
	}
}
