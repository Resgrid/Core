using System;
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

		public IncidentReportingService(IIncidentCommandService incidentCommandService)
		{
			_incidentCommandService = incidentCommandService;
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

		private static string Escape(string value)
		{
			if (string.IsNullOrEmpty(value))
				return string.Empty;

			if (value.Contains(",") || value.Contains("\"") || value.Contains("\n"))
				return "\"" + value.Replace("\"", "\"\"") + "\"";

			return value;
		}
	}
}
