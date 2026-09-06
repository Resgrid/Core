using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// The NERIS incident-analysis filing (RMS-3, registry M0167): the fire/hazmat investigation the contract
	/// posts to <c>/incident_analysis/{neris_id_incident}</c> after the incident itself exists.
	/// <para>
	/// It is a second submittable artifact for the same incident, not a section of it, which is the whole reason
	/// it has its own service: an analysis that will not validate must never block the incident report, and an
	/// incident that is still in flight must not lose its analysis. Finalizing an analysis before its incident is
	/// filed is allowed and expected — the submission simply waits for the incident's NERIS id.
	/// </para>
	/// </summary>
	public interface IIncidentAnalysisService
	{
		/// <summary>Starts (or returns) the analysis for an incident report; one per report.</summary>
		Task<IncidentAnalysisAggregate> StartForReportAsync(int departmentId, string userId, string incidentReportId, RmsOriginClient origin = RmsOriginClient.Web, CancellationToken cancellationToken = default);

		Task<IncidentAnalysisAggregate> GetAsync(int departmentId, string analysisId, bool includeHistory = false);

		Task<IncidentAnalysisAggregate> GetForReportAsync(int departmentId, string incidentReportId, bool includeHistory = false);

		/// <summary>
		/// ETag-guarded draft save. <paramref name="canWriteRestricted"/> false keeps the stored VIN and plate on
		/// each vehicle row rather than accepting or erasing them. It defaults to false: a caller that has not
		/// resolved the claim must not be granted restricted writes by omission.
		/// </summary>
		Task<IncidentAnalysisAggregate> SaveDraftAsync(int departmentId, string userId, string analysisId, long expectedRowVersion, IncidentAnalysisDraftInput input, bool canWriteRestricted = false, CancellationToken cancellationToken = default);

		/// <summary>Local validation against the pinned contract; never touches the incident's own issue list.</summary>
		Task<List<RmsValidationIssue>> ValidateAsync(int departmentId, string analysisId, CancellationToken cancellationToken = default);

		/// <summary>Writes the immutable revision and queues the filing when the incident is already at the destination.</summary>
		Task<IncidentAnalysisAggregate> FinalizeAsync(int departmentId, string userId, string analysisId, long expectedRowVersion, CancellationToken cancellationToken = default);

		/// <summary>Queues (or re-queues) the current revision; used when the incident was filed after the analysis was finalized.</summary>
		Task<IncidentAnalysisAggregate> QueueSubmissionAsync(int departmentId, string userId, string analysisId, CancellationToken cancellationToken = default);

		/// <summary>Queues every finalized analysis whose incident has since been filed; driven by worker 41.</summary>
		Task<int> QueueAwaitingIncidentAsync(int departmentId, CancellationToken cancellationToken = default);

		Task<IncidentAnalysisAggregate> VoidAsync(int departmentId, string userId, string analysisId, string reasonCode, string reasonText, CancellationToken cancellationToken = default);

		/// <summary>The snapshot the mapper consumes, from the draft rows or from a revision's copies.</summary>
		Task<NerisIncidentAnalysisSnapshot> BuildSnapshotAsync(int departmentId, string analysisId, string revisionId = null);
	}
}
