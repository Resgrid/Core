using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Repositories;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// NERIS incident report lifecycle (RMS plan sections 4.2, 5.2.1, 5.3; RMS-2): start from a Call with source-
	/// aware prefill, draft saves, local/destination validation, review, finalization with attestation and an
	/// immutable revision, submission queueing, correction after rejection, amendment, void and cancel. One
	/// report per responding entity per Call; a second start returns the existing report.
	/// </summary>
	public interface IIncidentReportsService
	{
		Task<IncidentReportAggregate> StartFromCallAsync(int departmentId, string userId, int callId, RmsOriginClient origin = RmsOriginClient.Web, CancellationToken cancellationToken = default);

		Task<IncidentReportAggregate> GetAsync(int departmentId, string reportId, bool includeHistory = false);

		Task<IncidentReportAggregate> GetForCallAsync(int departmentId, int callId);

		/// <summary>
		/// ETag-guarded draft save. <paramref name="canWriteRestricted"/> false keeps the restricted halves of the
		/// casualty rows as they stand instead of accepting or erasing them, so a reviewer without
		/// RecordRestricted_View can still correct the rest of the report (RMS-3).
		/// </summary>
		Task<IncidentReportAggregate> SaveDraftAsync(int departmentId, string userId, string reportId, long expectedRowVersion, IncidentReportDraftInput input, bool canWriteRestricted = true, CancellationToken cancellationToken = default);

		/// <summary>Runs local validation (and the destination's validate endpoint when asked and configured) and stores the issues on the report.</summary>
		Task<List<RmsValidationIssue>> ValidateAsync(int departmentId, string reportId, bool includeDestination, CancellationToken cancellationToken = default);

		Task<IncidentReportAggregate> SubmitForReviewAsync(int departmentId, string userId, string reportId, long expectedRowVersion, CancellationToken cancellationToken = default);

		Task<IncidentReportAggregate> ReturnForCorrectionAsync(int departmentId, string userId, string reportId, string reasonCode, string reasonText, CancellationToken cancellationToken = default);

		/// <summary>Validates, writes the revision and attestation signature, queues the submission when the profile allows, and emits the lifecycle events.</summary>
		Task<IncidentReportAggregate> FinalizeAsync(int departmentId, string userId, string reportId, long expectedRowVersion, string attestationStatementVersion, string ipAddress, string reasonCode, string reasonText, CancellationToken cancellationToken = default);

		/// <summary>After a rejection: a new revision (N+1 referencing N) and a new idempotency key, then back into the submission queue.</summary>
		Task<IncidentReportAggregate> CorrectAndResubmitAsync(int departmentId, string userId, string reportId, long expectedRowVersion, string attestationStatementVersion, string ipAddress, string reasonCode, string reasonText, CancellationToken cancellationToken = default);

		Task<IncidentReportAggregate> OpenAmendmentAsync(int departmentId, string userId, string reportId, CancellationToken cancellationToken = default);

		Task<IncidentReportAggregate> AbandonAmendmentAsync(int departmentId, string userId, string reportId, CancellationToken cancellationToken = default);

		/// <summary>Queues (or re-queues) the current revision for the destination; used when auto-submit is off or after a failed delivery.</summary>
		Task<IncidentReportAggregate> QueueSubmissionAsync(int departmentId, string userId, string reportId, CancellationToken cancellationToken = default);

		Task<IncidentReportAggregate> VoidAsync(int departmentId, string userId, string reportId, string reasonCode, string reasonText, CancellationToken cancellationToken = default);

		Task<IncidentReportAggregate> CancelAsync(int departmentId, string userId, string reportId, CancellationToken cancellationToken = default);

		Task<List<RmsIncidentReport>> QueryAsync(int departmentId, RmsIncidentReportQuery query);

		Task<int> CountAsync(int departmentId, RmsIncidentReportQuery query);

		Task<List<int>> GetYearsAsync(int departmentId);

		/// <summary>
		/// The conditional sections this report's selected incident types demand or suggest (RMS-3). The authoring
		/// surfaces render exactly this, so a client never keeps its own copy of the progressive rules.
		/// </summary>
		Task<List<NerisSectionRequirement>> GetSectionRequirementsAsync(int departmentId, string reportId);

		/// <summary>The snapshot the mapper consumes, from the draft rows or from a revision's copies.</summary>
		Task<NerisIncidentSnapshot> BuildSnapshotAsync(int departmentId, string reportId, string revisionId = null);

		Task RecordAccessAsync(int departmentId, string userId, string reportId, string revisionId, RmsAccessAuditAction action, string purpose = null, string ipAddress = null);
	}

	/// <summary>Result of one worker sweep over the submission queue.</summary>
	public class RecordsSubmissionSweepResult
	{
		public int Claimed { get; set; }
		public int Delivered { get; set; }
		public int Accepted { get; set; }
		public int Rejected { get; set; }
		public int Deferred { get; set; }
		public int Failed { get; set; }
		public int Errors { get; set; }
		public string Message { get; set; }
	}

	/// <summary>
	/// Worker 41 (RmsSubmissionCommand): claims due submissions, talks to the destination outside any database
	/// transaction, persists the immutable response artifact, moves the report through Submitted / Accepted /
	/// Rejected, retries transient failures with backoff, and emits triggers 108–111 plus notification 33.
	/// </summary>
	public interface IRecordsSubmissionService
	{
		Task<RecordsSubmissionSweepResult> SweepAsync(CancellationToken cancellationToken = default);

		/// <summary>Processes one claimed submission; public so a single delivery can be driven and tested directly.</summary>
		Task<RmsSubmission> ProcessAsync(RmsSubmission submission, CancellationToken cancellationToken = default);
	}
}
