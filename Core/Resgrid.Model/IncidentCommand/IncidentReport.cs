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

	/// <summary>
	/// NFIRS/NERIS-oriented key-times report: the timestamps and counts a department needs when
	/// filling federal incident reporting (NFIRS Basic Module / NERIS incident times).
	/// </summary>
	public class IncidentTimesReport
	{
		public int CallId { get; set; }
		/// <summary>Alarm time — when the call was logged (NFIRS "Alarm Date/Time").</summary>
		public DateTime? AlarmOn { get; set; }
		public DateTime? CommandEstablishedOn { get; set; }
		/// <summary>First resource assignment on the board (proxy for first tactical commitment).</summary>
		public DateTime? FirstResourceAssignedOn { get; set; }
		/// <summary>First completed benchmark objective (e.g. primary search complete).</summary>
		public DateTime? FirstBenchmarkCompletedOn { get; set; }
		/// <summary>Last completed objective (NFIRS "Controlled" proxy when benchmarks model control).</summary>
		public DateTime? LastBenchmarkCompletedOn { get; set; }
		public DateTime? CommandClosedOn { get; set; }
		public double DurationMinutes { get; set; }
		/// <summary>Distinct department + linked units committed to the incident.</summary>
		public int UnitResourceCount { get; set; }
		/// <summary>Distinct department + linked personnel committed to the incident.</summary>
		public int PersonnelResourceCount { get; set; }
		/// <summary>Ad-hoc resources from an outside agency (NFIRS "Mutual aid received" indicator).</summary>
		public int MutualAidResourceCount { get; set; }
		/// <summary>Every completed benchmark with its elapsed minutes from alarm.</summary>
		public List<BenchmarkTime> Benchmarks { get; set; } = new List<BenchmarkTime>();
	}

	/// <summary>A completed benchmark objective and when it was reached relative to the alarm.</summary>
	public class BenchmarkTime
	{
		public string Name { get; set; }
		public DateTime? CompletedOn { get; set; }
		public double? MinutesFromAlarm { get; set; }
	}

	/// <summary>Per-resource utilization across the incident: which lanes it worked and for how long.</summary>
	public class ResourceUtilizationReport
	{
		public int CallId { get; set; }
		public List<ResourceUtilizationRow> Rows { get; set; } = new List<ResourceUtilizationRow>();
	}

	/// <summary>One assignment stint: a resource's time in one lane.</summary>
	public class ResourceUtilizationRow
	{
		public int ResourceKind { get; set; }
		public string ResourceId { get; set; }
		public string LaneName { get; set; }
		public DateTime AssignedOn { get; set; }
		public DateTime? ReleasedOn { get; set; }
		public double Minutes { get; set; }
	}
}
