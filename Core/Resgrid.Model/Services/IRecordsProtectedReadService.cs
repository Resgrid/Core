using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Attended read resolution for RMS aggregates under Advanced Data Protection (RMS plan section 5.9; ADP
	/// Appendix B). Every method resolves cataloged rgdp envelopes in place for a grant-holding caller and
	/// leaves the exact REDACTED sentinel (or null, for binaries and coordinates) otherwise; the result says
	/// whether anything was withheld and why, so a page can show the step-up banner. Nothing is written.
	/// </summary>
	public interface IRecordsProtectedReadService
	{
		Task<ProtectedReadResult> ResolveAggregateAsync(int departmentId, RecordAggregate aggregate, string grantToken, string userId, CancellationToken cancellationToken = default);

		Task<ProtectedReadResult> ResolveIncidentAsync(int departmentId, IncidentReportAggregate aggregate, string grantToken, string userId, CancellationToken cancellationToken = default);

		Task<ProtectedReadResult> ResolveAnalysisAsync(int departmentId, IncidentAnalysisAggregate aggregate, string grantToken, string userId, CancellationToken cancellationToken = default);

		Task<ProtectedReadResult> ResolveRevisionsAsync(int departmentId, IReadOnlyList<RmsRevision> revisions, string grantToken, string userId, CancellationToken cancellationToken = default);

		Task<ProtectedReadResult> ResolveAttachmentsAsync(int departmentId, IReadOnlyList<RmsRecordAttachment> attachments, string grantToken, string userId, bool includeData, CancellationToken cancellationToken = default);

		Task<ProtectedReadResult> ResolveDisclosureRequestsAsync(int departmentId, IReadOnlyList<RmsDisclosureRequest> requests, string grantToken, string userId, CancellationToken cancellationToken = default);

		Task<ProtectedReadResult> ResolveDisclosureProductionsAsync(int departmentId, IReadOnlyList<RmsDisclosureProduction> productions, string grantToken, string userId, CancellationToken cancellationToken = default);

		Task<ProtectedReadResult> ResolveEvidenceAsync(int departmentId, IReadOnlyList<RmsEvidenceArtifact> artifacts, string grantToken, string userId, CancellationToken cancellationToken = default);

		Task<ProtectedReadResult> ResolveLegalHoldsAsync(int departmentId, IReadOnlyList<RmsRecordLegalHold> holds, string grantToken, string userId, CancellationToken cancellationToken = default);

		Task<ProtectedReadResult> ResolveSubmissionsAsync(int departmentId, IReadOnlyList<RmsSubmission> submissions, string grantToken, string userId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Workload decrypt for a reporting-destination submission (worker 41): allowed only when the department's
		/// NERIS profile carries the protected-egress acknowledgement, and only through the broker's purpose-bound
		/// workload lane. Returns false, leaving the payload enveloped, when either is missing.
		/// </summary>
		Task<bool> ResolveSubmissionForWorkloadAsync(int departmentId, RmsSubmission submission, string purpose, CancellationToken cancellationToken = default);
	}
}
