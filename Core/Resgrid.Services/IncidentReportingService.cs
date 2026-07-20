using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Per-incident reporting & analytics, aggregated from the incident-command data. Read-only; depends only on
	/// IIncidentCommandService (no cycle).
	/// </summary>
	public class IncidentReportingService : IIncidentReportingService
	{
		private readonly IIncidentCommandService _incidentCommandService;
		private readonly ICallsService _callsService;
		private readonly IIncidentResourcesService _incidentResourcesService;

		public IncidentReportingService(IIncidentCommandService incidentCommandService, ICallsService callsService, IIncidentResourcesService incidentResourcesService)
		{
			_incidentCommandService = incidentCommandService;
			_callsService = callsService;
			_incidentResourcesService = incidentResourcesService;
		}

		public async Task<IncidentReportSummary> GetIncidentSummaryAsync(int departmentId, int callId)
		{
			var command = await _incidentCommandService.GetCommandForCallAsync(departmentId, callId);
			if (command == null)
				return null;

			var nodes = await _incidentCommandService.GetNodesForCallAsync(departmentId, callId);
			var assignments = await _incidentCommandService.GetAssignmentsForCallAsync(departmentId, callId);
			var objectives = await _incidentCommandService.GetObjectivesForCallAsync(departmentId, callId);
			var timeline = await _incidentCommandService.GetTimelineForCallAsync(departmentId, callId);
			var roles = await _incidentCommandService.GetIncidentRolesAsync(departmentId, callId);
			var par = await _incidentCommandService.GetAccountabilityForCallAsync(departmentId, callId);

			var end = command.ClosedOn ?? DateTime.UtcNow;

			return new IncidentReportSummary
			{
				CallId = callId,
				IncidentCommandId = command.IncidentCommandId,
				EstablishedOn = command.EstablishedOn,
				ClosedOn = command.ClosedOn,
				DurationMinutes = Math.Round((end - command.EstablishedOn).TotalMinutes, 1),
				CurrentCommanderUserId = command.CurrentCommanderUserId,
				LaneCount = nodes.Count,
				ActiveAssignmentCount = assignments.Count(a => a.ReleasedOn == null),
				ObjectiveCount = objectives.Count,
				CompletedObjectiveCount = objectives.Count(o => o.Status == (int)TacticalObjectiveStatus.Complete),
				TimelineEntryCount = timeline.Count,
				RoleCount = roles.Count,
				AccountabilityGreen = par.Count(p => string.Equals(p.Status, "Green", StringComparison.OrdinalIgnoreCase)),
				AccountabilityWarning = par.Count(p => string.Equals(p.Status, "Warning", StringComparison.OrdinalIgnoreCase)),
				AccountabilityCritical = par.Count(p => string.Equals(p.Status, "Critical", StringComparison.OrdinalIgnoreCase))
			};
		}

		public async Task<IncidentAfterActionReport> GetAfterActionReportAsync(int departmentId, int callId)
		{
			var summary = await GetIncidentSummaryAsync(departmentId, callId);
			if (summary == null)
				return null;

			return new IncidentAfterActionReport
			{
				Summary = summary,
				Nodes = await _incidentCommandService.GetNodesForCallAsync(departmentId, callId),
				Assignments = await _incidentCommandService.GetAssignmentsForCallAsync(departmentId, callId),
				Objectives = await _incidentCommandService.GetObjectivesForCallAsync(departmentId, callId),
				Timeline = await _incidentCommandService.GetTimelineForCallAsync(departmentId, callId),
				Roles = await _incidentCommandService.GetIncidentRolesAsync(departmentId, callId)
			};
		}

		public async Task<string> ExportTimelineCsvAsync(int departmentId, int callId)
		{
			var timeline = await _incidentCommandService.GetTimelineForCallAsync(departmentId, callId);

			var sb = new StringBuilder();
			sb.AppendLine("OccurredOn,EntryType,Description,UserId");

			foreach (var entry in timeline)
			{
				sb.AppendLine($"{entry.OccurredOn:o},{(CommandLogEntryType)entry.EntryType},{Escape(entry.Description)},{Escape(entry.UserId)}");
			}

			return sb.ToString();
		}

		public async Task<IncidentTimesReport> GetIncidentTimesReportAsync(int departmentId, int callId)
		{
			var command = await _incidentCommandService.GetCommandForCallAsync(departmentId, callId);
			if (command == null)
				return null;

			var call = await _callsService.GetCallByIdAsync(callId);
			// CallId is guessable — never report on another department's call.
			if (call != null && call.DepartmentId != departmentId)
				return null;

			var assignments = await _incidentCommandService.GetAssignmentsForCallAsync(departmentId, callId);
			var objectives = await _incidentCommandService.GetObjectivesForCallAsync(departmentId, callId);
			var adHocUnits = await _incidentResourcesService.GetAdHocUnitsForCallAsync(departmentId, callId);
			var adHocPersonnel = await _incidentResourcesService.GetAdHocPersonnelForCallAsync(departmentId, callId);

			var alarm = call?.LoggedOn;
			var completedBenchmarks = objectives
				.Where(o => o.Status == (int)TacticalObjectiveStatus.Complete && o.CompletedOn.HasValue)
				.OrderBy(o => o.CompletedOn)
				.ToList();

			var end = command.ClosedOn ?? DateTime.UtcNow;

			return new IncidentTimesReport
			{
				CallId = callId,
				AlarmOn = alarm,
				CommandEstablishedOn = command.EstablishedOn,
				FirstResourceAssignedOn = assignments.OrderBy(a => a.AssignedOn).FirstOrDefault()?.AssignedOn,
				FirstBenchmarkCompletedOn = completedBenchmarks.FirstOrDefault()?.CompletedOn,
				LastBenchmarkCompletedOn = completedBenchmarks.LastOrDefault()?.CompletedOn,
				CommandClosedOn = command.ClosedOn,
				DurationMinutes = System.Math.Round((end - (alarm ?? command.EstablishedOn)).TotalMinutes, 1),
				UnitResourceCount = assignments
					.Where(a => a.ResourceKind == (int)ResourceAssignmentKind.RealUnit || a.ResourceKind == (int)ResourceAssignmentKind.LinkedDeptUnit)
					.Select(a => a.ResourceId).Distinct().Count(),
				PersonnelResourceCount = assignments
					.Where(a => a.ResourceKind == (int)ResourceAssignmentKind.RealPersonnel || a.ResourceKind == (int)ResourceAssignmentKind.LinkedDeptPersonnel)
					.Select(a => a.ResourceId).Distinct().Count(),
				MutualAidResourceCount = adHocUnits.Count(u => !string.IsNullOrWhiteSpace(u.ExternalAgencyName))
					+ adHocPersonnel.Count(person => !string.IsNullOrWhiteSpace(person.ExternalAgencyName)),
				Benchmarks = completedBenchmarks.Select(o => new BenchmarkTime
				{
					Name = o.Name,
					CompletedOn = o.CompletedOn,
					MinutesFromAlarm = alarm.HasValue && o.CompletedOn.HasValue ? System.Math.Round((o.CompletedOn.Value - alarm.Value).TotalMinutes, 1) : (double?)null
				}).ToList()
			};
		}

		public async Task<ResourceUtilizationReport> GetResourceUtilizationReportAsync(int departmentId, int callId)
		{
			var command = await _incidentCommandService.GetCommandForCallAsync(departmentId, callId);
			if (command == null)
				return null;

			var nodes = await _incidentCommandService.GetNodesForCallAsync(departmentId, callId);
			var assignments = await _incidentCommandService.GetAssignmentsForCallAsync(departmentId, callId);
			var laneNames = nodes.ToDictionary(n => n.CommandStructureNodeId, n => n.Name);

			var rows = assignments
				.OrderBy(a => a.AssignedOn)
				.Select(a => new ResourceUtilizationRow
				{
					ResourceKind = a.ResourceKind,
					ResourceId = a.ResourceId,
					LaneName = !string.IsNullOrWhiteSpace(a.CommandStructureNodeId) && laneNames.ContainsKey(a.CommandStructureNodeId) ? laneNames[a.CommandStructureNodeId] : string.Empty,
					AssignedOn = a.AssignedOn,
					ReleasedOn = a.ReleasedOn,
					Minutes = System.Math.Round(((a.ReleasedOn ?? command.ClosedOn ?? DateTime.UtcNow) - a.AssignedOn).TotalMinutes, 1)
				})
				.ToList();

			return new ResourceUtilizationReport { CallId = callId, Rows = rows };
		}

		public async Task<string> ExportAfterActionCsvAsync(int departmentId, int callId)
		{
			var report = await GetAfterActionReportAsync(departmentId, callId);
			if (report == null)
				return string.Empty;

			var times = await GetIncidentTimesReportAsync(departmentId, callId);
			var utilization = await GetResourceUtilizationReportAsync(departmentId, callId);

			var sb = new StringBuilder();

			sb.AppendLine("Section,Field,Value");
			sb.AppendLine($"Summary,CallId,{report.Summary.CallId}");
			sb.AppendLine($"Summary,EstablishedOn,{report.Summary.EstablishedOn:o}");
			sb.AppendLine($"Summary,ClosedOn,{report.Summary.ClosedOn:o}");
			sb.AppendLine($"Summary,DurationMinutes,{report.Summary.DurationMinutes}");
			sb.AppendLine($"Summary,Lanes,{report.Summary.LaneCount}");
			sb.AppendLine($"Summary,ActiveAssignments,{report.Summary.ActiveAssignmentCount}");
			sb.AppendLine($"Summary,Objectives,{report.Summary.CompletedObjectiveCount}/{report.Summary.ObjectiveCount}");

			if (times != null)
			{
				sb.AppendLine($"Times,AlarmOn,{times.AlarmOn:o}");
				sb.AppendLine($"Times,CommandEstablishedOn,{times.CommandEstablishedOn:o}");
				sb.AppendLine($"Times,FirstResourceAssignedOn,{times.FirstResourceAssignedOn:o}");
				sb.AppendLine($"Times,FirstBenchmarkCompletedOn,{times.FirstBenchmarkCompletedOn:o}");
				sb.AppendLine($"Times,LastBenchmarkCompletedOn,{times.LastBenchmarkCompletedOn:o}");
				sb.AppendLine($"Times,UnitResources,{times.UnitResourceCount}");
				sb.AppendLine($"Times,PersonnelResources,{times.PersonnelResourceCount}");
				sb.AppendLine($"Times,MutualAidResources,{times.MutualAidResourceCount}");
				foreach (var benchmark in times.Benchmarks)
					sb.AppendLine($"Benchmark,{Escape(benchmark.Name)},{benchmark.CompletedOn:o}");
			}

			sb.AppendLine();
			sb.AppendLine("Lane,NodeType,Name");
			foreach (var node in report.Nodes)
				sb.AppendLine($"Lane,{(CommandNodeType)node.NodeType},{Escape(node.Name)}");

			if (utilization != null)
			{
				sb.AppendLine();
				sb.AppendLine("ResourceKind,ResourceId,Lane,AssignedOn,ReleasedOn,Minutes");
				foreach (var row in utilization.Rows)
					sb.AppendLine($"{(ResourceAssignmentKind)row.ResourceKind},{Escape(row.ResourceId)},{Escape(row.LaneName)},{row.AssignedOn:o},{row.ReleasedOn:o},{row.Minutes}");
			}

			sb.AppendLine();
			sb.AppendLine("OccurredOn,EntryType,Description,UserId");
			foreach (var entry in report.Timeline)
				sb.AppendLine($"{entry.OccurredOn:o},{(CommandLogEntryType)entry.EntryType},{Escape(entry.Description)},{Escape(entry.UserId)}");

			return sb.ToString();
		}

		private static string Escape(string value)
		{
			if (string.IsNullOrEmpty(value))
				return string.Empty;

			// A leading =, +, -, @, tab or CR makes Excel/Sheets evaluate the cell as a formula on import
			// (CSV injection). Plain numeric values (e.g. "-122.5") are exempt so coordinates/numbers survive.
			if ((value[0] == '=' || value[0] == '+' || value[0] == '-' || value[0] == '@' || value[0] == '\t' || value[0] == '\r')
				&& !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
				value = "'" + value;

			if (value.Contains(",") || value.Contains("\"") || value.Contains("\n") || value.Contains("\r"))
				return "\"" + value.Replace("\"", "\"\"") + "\"";

			return value;
		}
	}
}
