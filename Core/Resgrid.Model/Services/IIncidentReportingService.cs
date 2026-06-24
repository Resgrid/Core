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
	}
}
