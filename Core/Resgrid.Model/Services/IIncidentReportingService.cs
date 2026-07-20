using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Per-incident reporting & analytics (§3.13): incident status summary (ICS-201/209), after-action bundle,
	/// and timeline export. Built on top of the incident-command data.
	/// </summary>
	public interface IIncidentReportingService
	{
		Task<IncidentReportSummary> GetIncidentSummaryAsync(int departmentId, int callId);
		Task<IncidentAfterActionReport> GetAfterActionReportAsync(int departmentId, int callId);
		Task<string> ExportTimelineCsvAsync(int departmentId, int callId);

		/// <summary>NFIRS/NERIS-oriented key times and resource counts for federal/NFPA reporting.</summary>
		Task<IncidentTimesReport> GetIncidentTimesReportAsync(int departmentId, int callId);

		/// <summary>Per-resource lane utilization (which lanes, how long) across the incident.</summary>
		Task<ResourceUtilizationReport> GetResourceUtilizationReportAsync(int departmentId, int callId);

		/// <summary>Full after-action export as a multi-section CSV (summary, times, roles, lanes, utilization, timeline).</summary>
		Task<string> ExportAfterActionCsvAsync(int departmentId, int callId);
	}
}
