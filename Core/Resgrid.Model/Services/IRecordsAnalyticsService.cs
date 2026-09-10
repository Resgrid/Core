using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// RMS-6 records analytics (RMS plan section 6, RMS-6): response-performance, workload and executive dashboards
	/// over finalized Records, their revisions and their due-state history, plus the accreditation and
	/// community-risk dashboards that also read RMS-5 prevention history.
	/// <para>
	/// Every read sits on the authorization path every other Records read uses: the viewer must be an active member
	/// with Records access, the <c>Records.Analytics</c> flag must be on, and a group-scoped viewer sees figures
	/// computed only over the Records they could open — cross-group department totals stay with department
	/// administrators (plan section 11, question 38). No narrative, no restricted section and no protected column
	/// is read; the dashboards count headers, attested unit and participant rows and prevention aggregates.
	/// </para>
	/// </summary>
	public interface IRecordsAnalyticsService
	{
		Task<bool> IsModuleEnabledAsync(int departmentId);
		Task<RecordsResponsePerformance> GetResponsePerformanceAsync(int departmentId, string userId, RecordsAnalyticsQuery query, CancellationToken cancellationToken = default);
		Task<RecordsWorkload> GetWorkloadAsync(int departmentId, string userId, RecordsAnalyticsQuery query, CancellationToken cancellationToken = default);
		Task<RecordsExecutiveSummary> GetExecutiveSummaryAsync(int departmentId, string userId, RecordsAnalyticsQuery query, CancellationToken cancellationToken = default);
		Task<RecordsAccreditation> GetAccreditationAsync(int departmentId, string userId, RecordsAnalyticsQuery query, CancellationToken cancellationToken = default);
		Task<RecordsCommunityRisk> GetCommunityRiskAsync(int departmentId, string userId, RecordsAnalyticsQuery query, CancellationToken cancellationToken = default);

		/// <summary>
		/// Apparatus and equipment readiness composed from the checklists, maintenance and inventory modules through their
		/// own authorized reads, plus readiness-packet evidence on Records; RMS computes no readiness of its own.
		/// </summary>
		Task<RecordsReadiness> GetReadinessAsync(int departmentId, string userId, RecordsAnalyticsQuery query, CancellationToken cancellationToken = default);
	}
}
