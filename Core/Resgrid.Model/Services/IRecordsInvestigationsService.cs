using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	public class InvestigationCaseAggregate
	{
		public RmsInvestigationCase Case { get; set; }
		public List<RmsInvestigationCaseMember> Members { get; set; } = new List<RmsInvestigationCaseMember>();
		public List<RmsInvestigationCaseIncident> Incidents { get; set; } = new List<RmsInvestigationCaseIncident>();
		public List<RmsInvestigationNote> Notes { get; set; } = new List<RmsInvestigationNote>();
		public List<RmsInvestigationEvidence> Evidence { get; set; } = new List<RmsInvestigationEvidence>();
		public List<RmsInvestigationReferral> Referrals { get; set; } = new List<RmsInvestigationReferral>();
		public List<RmsPreventionAttachment> Attachments { get; set; } = new List<RmsPreventionAttachment>();
		public RmsInvestigationRole? CallerRole { get; set; }
		public ProtectedReadResult Protection { get; set; } = new ProtectedReadResult();
	}

	/// <summary>
	/// Investigation cases (RMS plan section 4.4, RMS-5). Every read and change requires <c>RecordRestricted_View</c>
	/// and an active membership on the case; department administrators are not exempt (plan section 5.7). Every read is
	/// audited to RmsAccessAudits with purpose "investigation", exports likewise. Findings never touch the linked incident
	/// revision; <see cref="RmsInvestigationCase.RecommendsIncidentAmendment"/> is the only bridge.
	/// </summary>
	public interface IRecordsInvestigationsService
	{
		Task<bool> IsModuleEnabledAsync(int departmentId);

		/// <summary>Cases the caller is an active member of. Nobody lists cases they are not on.</summary>
		Task<List<RmsInvestigationCase>> ListMyCasesAsync(int departmentId, string userId, bool includeClosed);
		Task<InvestigationCaseAggregate> GetAsync(int departmentId, string userId, string caseId, string ipAddress = null);
		/// <summary>Opens a case; the opener becomes Lead. Needs RecordRestricted_View.</summary>
		Task<RmsInvestigationCase> OpenAsync(int departmentId, string userId, string title, string occupancyId, int? callId, string incidentSummary, CancellationToken cancellationToken = default);
		Task<RmsInvestigationCase> UpdateAsync(int departmentId, string userId, RmsInvestigationCase input, CancellationToken cancellationToken = default);

		Task<RmsInvestigationCaseMember> AddMemberAsync(int departmentId, string userId, string caseId, string memberUserId, RmsInvestigationRole role, CancellationToken cancellationToken = default);
		Task RemoveMemberAsync(int departmentId, string userId, string caseId, string memberId, CancellationToken cancellationToken = default);

		/// <summary>Links an incident report the caller can view; pins its current official revision.</summary>
		Task<RmsInvestigationCaseIncident> LinkIncidentAsync(int departmentId, string userId, string caseId, string recordId, CancellationToken cancellationToken = default);
		Task UnlinkIncidentAsync(int departmentId, string userId, string caseId, string linkId, CancellationToken cancellationToken = default);

		Task<RmsInvestigationNote> AddNoteAsync(int departmentId, string userId, string caseId, RmsInvestigationNoteKind kind, DateTime occurredOn, string subject, string body, CancellationToken cancellationToken = default);
		Task<RmsInvestigationNote> UpdateNoteAsync(int departmentId, string userId, string noteId, string subject, string body, CancellationToken cancellationToken = default);

		Task<RmsInvestigationEvidence> AddEvidenceAsync(int departmentId, string userId, string caseId, RmsInvestigationEvidence input, CancellationToken cancellationToken = default);
		/// <summary>Appends a custody transfer; the evidence row's custodian and state follow. Custody history is never edited.</summary>
		Task<RmsInvestigationCustody> TransferCustodyAsync(int departmentId, string userId, string evidenceId, string toUserId, string toExternal, string reason, RmsEvidenceState resultingState, CancellationToken cancellationToken = default);
		Task<List<RmsInvestigationCustody>> GetCustodyChainAsync(int departmentId, string userId, string evidenceId);

		Task<RmsInvestigationReferral> AddReferralAsync(int departmentId, string userId, string caseId, string agency, string reason, string referenceNumber, CancellationToken cancellationToken = default);
		Task<RmsInvestigationReferral> UpdateReferralStateAsync(int departmentId, string userId, string referralId, RmsReferralState state, CancellationToken cancellationToken = default);

		/// <summary>Records cause, origin and findings (Lead or Investigator); moves the case to PendingReview.</summary>
		Task<RmsInvestigationCase> RecordFindingsAsync(int departmentId, string userId, string caseId, RmsFireCauseClassification classification, string causeDetail, string originDescription, string findings, bool recommendsIncidentAmendment, CancellationToken cancellationToken = default);
		/// <summary>Approves findings; the approver must be a Reviewer or Lead and may not be the findings author.</summary>
		Task<RmsInvestigationCase> ApproveFindingsAsync(int departmentId, string userId, string caseId, CancellationToken cancellationToken = default);
		Task<RmsInvestigationCase> ReturnFindingsAsync(int departmentId, string userId, string caseId, string reason, CancellationToken cancellationToken = default);
		/// <summary>Closes the case (approved findings required) and locks every note.</summary>
		Task<RmsInvestigationCase> CloseAsync(int departmentId, string userId, string caseId, string closureReason, CancellationToken cancellationToken = default);
		Task<RmsInvestigationCase> ReopenAsync(int departmentId, string userId, string caseId, string reason, CancellationToken cancellationToken = default);

		/// <summary>Export packet (JSON) of everything the caller may see; audited as Export.</summary>
		Task<byte[]> ExportAsync(int departmentId, string userId, string caseId, string ipAddress = null, CancellationToken cancellationToken = default);
		Task<List<RmsAccessAudit>> GetAccessAuditAsync(int departmentId, string userId, string caseId, int take);
	}

	/// <summary>Post-finalization quality review (RMS plan section 4.7, RMS-4 optional module).</summary>
	public interface IRecordsQualityReviewService
	{
		Task<bool> IsModuleEnabledAsync(int departmentId);
		Task<List<RmsQualityRubric>> GetRubricsAsync(int departmentId, string userId, bool includeInactive);
		Task<RmsQualityRubric> GetRubricAsync(int departmentId, string userId, string rubricId);
		Task<RmsQualityRubric> SaveRubricAsync(int departmentId, string userId, RmsQualityRubric input, List<RmsQualityCriterion> criteria, CancellationToken cancellationToken = default);
		/// <summary>Deterministically samples finalized, not-yet-reviewed records for the rubric (seeded by the rubric id and day) into pending reviews.</summary>
		Task<List<RmsQualityReview>> SampleAsync(int departmentId, string userId, string rubricId, DateTime sinceUtc, CancellationToken cancellationToken = default);
		Task<List<RmsQualityReview>> GetPendingAsync(int departmentId, string userId, int take);
		Task<RmsQualityReview> GetReviewAsync(int departmentId, string userId, string reviewId);
		Task<RmsQualityReview> ScoreAsync(int departmentId, string userId, string reviewId, List<RmsQualityFinding> findings, string note, bool amendmentRecommended, CancellationToken cancellationToken = default);
		Task<List<RmsQualityReview>> GetForRecordAsync(int departmentId, string userId, string recordId);
		Task<RecordsQualityTrends> GetTrendsAsync(int departmentId, string userId, DateTime sinceUtc);
	}

	/// <summary>RMS-4 release baseline telemetry.</summary>
	public interface IRecordsReleaseTelemetryService
	{
		Task<RecordsReleaseTelemetry> GetAsync(int departmentId, string userId, int windowHours = 24);
		/// <summary>Builds the snapshot without a caller (worker) and writes it as one structured log line.</summary>
		Task<RecordsReleaseTelemetry> LogSnapshotAsync(int departmentId, CancellationToken cancellationToken = default);
	}
}
