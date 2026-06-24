using System;
using System.Collections.Generic;

namespace Resgrid.Model
{
	/// <summary>ICS-201/209-style summary metrics for an incident (§3.13).</summary>
	public class IncidentReportSummary
	{
		public int CallId { get; set; }
		public string IncidentCommandId { get; set; }
		public DateTime? EstablishedOn { get; set; }
		public DateTime? ClosedOn { get; set; }
		public double DurationMinutes { get; set; }
		public string CurrentCommanderUserId { get; set; }
		public int LaneCount { get; set; }
		public int ActiveAssignmentCount { get; set; }
		public int ObjectiveCount { get; set; }
		public int CompletedObjectiveCount { get; set; }
		public int TimelineEntryCount { get; set; }
		public int RoleCount { get; set; }
		public int AccountabilityGreen { get; set; }
		public int AccountabilityWarning { get; set; }
		public int AccountabilityCritical { get; set; }
	}

	/// <summary>A complete after-action bundle for an incident.</summary>
	public class IncidentAfterActionReport
	{
		public IncidentReportSummary Summary { get; set; }
		public List<CommandStructureNode> Nodes { get; set; } = new List<CommandStructureNode>();
		public List<ResourceAssignment> Assignments { get; set; } = new List<ResourceAssignment>();
		public List<TacticalObjective> Objectives { get; set; } = new List<TacticalObjective>();
		public List<CommandLogEntry> Timeline { get; set; } = new List<CommandLogEntry>();
		public List<IncidentRoleAssignment> Roles { get; set; } = new List<IncidentRoleAssignment>();
	}
}
