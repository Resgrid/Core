using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// The RMS side of Advanced Data Protection (RMS plan section 5.9; ADP catalog v10). One seam for every
	/// Records write and read of a cataloged column:
	/// <list type="bullet">
	/// <item><b>Protect*</b> encrypts the cataloged columns of a row in place when the department is in an
	/// encrypt-new-writes state, marks the row protected, and throws <see cref="RecordProtectedContentException"/>
	/// when the broker refuses (missing grant, epoch revoked, broker down). Unprotected departments pass through
	/// untouched. The <c>existing</c> row is the stored copy an update replaces, so a REDACTED placeholder the
	/// editor never had revealed restores the stored envelope instead of overwriting it.</item>
	/// <item><b>Reveal*</b> resolves envelopes in place for the ambient caller (<see cref="IProtectedGrantContext"/>):
	/// plaintext for a grant-holding member, the exact REDACTED sentinel (or null for binaries and coordinates)
	/// otherwise. The returned <see cref="ProtectedReadResult"/> says what was withheld and why, so a page can show
	/// the step-up banner and an operation that needs the content can fail closed
	/// (<see cref="ProtectedReadResultExtensions.RequireRevealed"/>).</item>
	/// <item><b>*ForWorkload</b> is the purpose-bound workload lane (ADP plan 3.4): no grant, no user, decrypt only
	/// for a purpose the department has acknowledged — NERIS delivery and agency exports.</item>
	/// </list>
	/// </summary>
	public interface IRecordsProtectionService
	{
		/// <summary>The department's pinned catalog version (0 when protection is off).</summary>
		Task<int> GetCatalogVersionAsync(int departmentId);

		/// <summary>True when reads for this department are enforced (envelopes stay concealed without a grant).</summary>
		Task<bool> IsEnforcedAsync(int departmentId);

		Task ProtectDetailsAsync(int departmentId, RmsOperationalRecordDetail row, RmsOperationalRecordDetail existing, string userId = null, CancellationToken cancellationToken = default);
		Task ProtectAttachmentAsync(int departmentId, RmsRecordAttachment row, RmsRecordAttachment existing, string userId = null, CancellationToken cancellationToken = default);
		Task ProtectRevisionAsync(int departmentId, RmsRevision row, string userId = null, CancellationToken cancellationToken = default);
		Task ProtectLocationAsync(int departmentId, RmsLocation row, RmsLocation existing, string userId = null, CancellationToken cancellationToken = default);
		Task ProtectNarrativeAsync(int departmentId, RmsNarrative row, RmsNarrative existing, string userId = null, CancellationToken cancellationToken = default);
		Task ProtectSourceFactAsync(int departmentId, RmsSourceFact row, RmsSourceFact existing, string userId = null, CancellationToken cancellationToken = default);
		Task ProtectCasualtyAsync(int departmentId, RmsCasualtyRescue row, RmsCasualtyRescue existing, string userId = null, CancellationToken cancellationToken = default);
		Task ProtectExposureAsync(int departmentId, RmsExposure row, RmsExposure existing, string userId = null, CancellationToken cancellationToken = default);
		Task ProtectModuleAsync(int departmentId, RmsIncidentModule row, RmsIncidentModule existing, string userId = null, CancellationToken cancellationToken = default);
		Task ProtectPropertyAsync(int departmentId, RmsIncidentProperty row, RmsIncidentProperty existing, string userId = null, CancellationToken cancellationToken = default);
		Task ProtectVehicleAsync(int departmentId, RmsIncidentVehicle row, RmsIncidentVehicle existing, string userId = null, CancellationToken cancellationToken = default);
		Task ProtectResourceAsync(int departmentId, RmsIncidentResource row, RmsIncidentResource existing, string userId = null, CancellationToken cancellationToken = default);
		Task ProtectSubmissionAsync(int departmentId, RmsSubmission row, string userId = null, CancellationToken cancellationToken = default);
		Task ProtectSignatureAsync(int departmentId, RmsSignature row, string userId = null, CancellationToken cancellationToken = default);
		Task ProtectEvidenceAsync(int departmentId, RmsEvidenceArtifact row, string userId = null, CancellationToken cancellationToken = default);
		Task ProtectDisclosureRequestAsync(int departmentId, RmsDisclosureRequest row, RmsDisclosureRequest existing, string userId = null, CancellationToken cancellationToken = default);
		Task ProtectDisclosureProductionAsync(int departmentId, RmsDisclosureProduction row, string userId = null, CancellationToken cancellationToken = default);
		Task ProtectLegalHoldAsync(int departmentId, RmsRecordLegalHold row, RmsRecordLegalHold existing, string userId = null, CancellationToken cancellationToken = default);
		Task ProtectExportRunAsync(int departmentId, RmsExportRun row, string userId = null, CancellationToken cancellationToken = default);

		/// <summary>Seals Protected-classified typed values (catalog v11): each row flagged ProtectionRequired packs its typed columns into its envelope; other rows pass through.</summary>
		Task ProtectValuesAsync(int departmentId, IReadOnlyList<RmsRecordValue> rows, string userId = null, CancellationToken cancellationToken = default);

		/// <summary>Reveals sealed typed values for the ambient caller; a refused row keeps its envelope and shapes as the withheld cell.</summary>
		Task<ProtectedReadResult> RevealValuesAsync(int departmentId, IReadOnlyList<RmsRecordValue> rows, CancellationToken cancellationToken = default);

		/// <summary>Reveals sealed typed values through the purpose-bound workload lane (records-export).</summary>
		Task<ProtectedReadResult> RevealValuesForWorkloadAsync(int departmentId, IReadOnlyList<RmsRecordValue> rows, string purpose, CancellationToken cancellationToken = default);

		Task<ProtectedReadResult> RevealAsync(int departmentId, RecordAggregate aggregate, CancellationToken cancellationToken = default);
		Task<ProtectedReadResult> RevealAsync(int departmentId, IncidentReportAggregate aggregate, CancellationToken cancellationToken = default);
		Task<ProtectedReadResult> RevealAsync(int departmentId, IncidentAnalysisAggregate aggregate, CancellationToken cancellationToken = default);
		Task<ProtectedReadResult> RevealDetailsAsync(int departmentId, IReadOnlyList<RmsOperationalRecordDetail> rows, CancellationToken cancellationToken = default);
		Task<ProtectedReadResult> RevealRevisionsAsync(int departmentId, IReadOnlyList<RmsRevision> rows, CancellationToken cancellationToken = default);
		Task<ProtectedReadResult> RevealAttachmentsAsync(int departmentId, IReadOnlyList<RmsRecordAttachment> rows, bool includeData, CancellationToken cancellationToken = default);
		Task<ProtectedReadResult> RevealEvidenceAsync(int departmentId, IReadOnlyList<RmsEvidenceArtifact> rows, CancellationToken cancellationToken = default);
		Task<ProtectedReadResult> RevealDisclosureRequestsAsync(int departmentId, IReadOnlyList<RmsDisclosureRequest> rows, CancellationToken cancellationToken = default);
		Task<ProtectedReadResult> RevealDisclosureProductionsAsync(int departmentId, IReadOnlyList<RmsDisclosureProduction> rows, CancellationToken cancellationToken = default);
		Task<ProtectedReadResult> RevealLegalHoldsAsync(int departmentId, IReadOnlyList<RmsRecordLegalHold> rows, CancellationToken cancellationToken = default);
		Task<ProtectedReadResult> RevealSubmissionsAsync(int departmentId, IReadOnlyList<RmsSubmission> rows, CancellationToken cancellationToken = default);

		// ---- RMS-5 prevention and investigations, RMS-4 quality review (catalog v13) ----
		Task ProtectOccupancyAsync(int departmentId, RmsOccupancy row, RmsOccupancy existing, string userId = null, CancellationToken cancellationToken = default);
		Task<ProtectedReadResult> RevealOccupanciesAsync(int departmentId, IReadOnlyList<RmsOccupancy> rows, CancellationToken cancellationToken = default);
		Task ProtectOccupancyHazardAsync(int departmentId, RmsOccupancyHazard row, RmsOccupancyHazard existing, string userId = null, CancellationToken cancellationToken = default);
		Task<ProtectedReadResult> RevealOccupancyHazardsAsync(int departmentId, IReadOnlyList<RmsOccupancyHazard> rows, CancellationToken cancellationToken = default);
		Task ProtectInspectionAsync(int departmentId, RmsInspection row, RmsInspection existing, string userId = null, CancellationToken cancellationToken = default);
		Task<ProtectedReadResult> RevealInspectionsAsync(int departmentId, IReadOnlyList<RmsInspection> rows, CancellationToken cancellationToken = default);
		Task ProtectViolationAsync(int departmentId, RmsViolation row, RmsViolation existing, string userId = null, CancellationToken cancellationToken = default);
		Task<ProtectedReadResult> RevealViolationsAsync(int departmentId, IReadOnlyList<RmsViolation> rows, CancellationToken cancellationToken = default);
		Task ProtectPermitAsync(int departmentId, RmsPermit row, RmsPermit existing, string userId = null, CancellationToken cancellationToken = default);
		Task<ProtectedReadResult> RevealPermitsAsync(int departmentId, IReadOnlyList<RmsPermit> rows, CancellationToken cancellationToken = default);
		Task ProtectPlanReviewAsync(int departmentId, RmsPlanReview row, RmsPlanReview existing, string userId = null, CancellationToken cancellationToken = default);
		Task<ProtectedReadResult> RevealPlanReviewsAsync(int departmentId, IReadOnlyList<RmsPlanReview> rows, CancellationToken cancellationToken = default);
		Task ProtectInvestigationCaseAsync(int departmentId, RmsInvestigationCase row, RmsInvestigationCase existing, string userId = null, CancellationToken cancellationToken = default);
		Task<ProtectedReadResult> RevealInvestigationCasesAsync(int departmentId, IReadOnlyList<RmsInvestigationCase> rows, CancellationToken cancellationToken = default);
		Task ProtectInvestigationNoteAsync(int departmentId, RmsInvestigationNote row, RmsInvestigationNote existing, string userId = null, CancellationToken cancellationToken = default);
		Task<ProtectedReadResult> RevealInvestigationNotesAsync(int departmentId, IReadOnlyList<RmsInvestigationNote> rows, CancellationToken cancellationToken = default);
		Task ProtectInvestigationEvidenceAsync(int departmentId, RmsInvestigationEvidence row, RmsInvestigationEvidence existing, string userId = null, CancellationToken cancellationToken = default);
		Task<ProtectedReadResult> RevealInvestigationEvidenceAsync(int departmentId, IReadOnlyList<RmsInvestigationEvidence> rows, CancellationToken cancellationToken = default);
		Task ProtectInvestigationCustodyAsync(int departmentId, RmsInvestigationCustody row, string userId = null, CancellationToken cancellationToken = default);
		Task<ProtectedReadResult> RevealInvestigationCustodyAsync(int departmentId, IReadOnlyList<RmsInvestigationCustody> rows, CancellationToken cancellationToken = default);
		Task ProtectInvestigationReferralAsync(int departmentId, RmsInvestigationReferral row, RmsInvestigationReferral existing, string userId = null, CancellationToken cancellationToken = default);
		Task<ProtectedReadResult> RevealInvestigationReferralsAsync(int departmentId, IReadOnlyList<RmsInvestigationReferral> rows, CancellationToken cancellationToken = default);
		Task ProtectQualityReviewAsync(int departmentId, RmsQualityReview row, RmsQualityReview existing, string userId = null, CancellationToken cancellationToken = default);
		Task<ProtectedReadResult> RevealQualityReviewsAsync(int departmentId, IReadOnlyList<RmsQualityReview> rows, CancellationToken cancellationToken = default);
		/// <summary>Seals a prevention/investigation attachment: name and description as text, the bytes as a binary envelope (catalog v13).</summary>
		Task ProtectPreventionAttachmentAsync(int departmentId, RmsPreventionAttachment row, RmsPreventionAttachment existing, string userId = null, CancellationToken cancellationToken = default);
		/// <summary>Reveals prevention attachment metadata, and the bytes when includeData; a concealed binary is nulled and named in RedactedFields.</summary>
		Task<ProtectedReadResult> RevealPreventionAttachmentsAsync(int departmentId, IReadOnlyList<RmsPreventionAttachment> rows, bool includeData, CancellationToken cancellationToken = default);
		Task<ProtectedReadResult> RevealExportRunsAsync(int departmentId, IReadOnlyList<RmsExportRun> rows, bool includeData, CancellationToken cancellationToken = default);

		/// <summary>
		/// Workload decrypt of a queued submission's payload for a reporting destination (worker 41): allowed only
		/// when the department's NERIS profile carries the protected-egress acknowledgement and only through the
		/// broker's purpose-bound lane. Returns false, leaving the payload enveloped, when either is missing.
		/// </summary>
		Task<bool> ResolveSubmissionForWorkloadAsync(int departmentId, RmsSubmission submission, string purpose, CancellationToken cancellationToken = default);

		/// <summary>Workload reveal of a record aggregate for an acknowledged export purpose (worker 45 / Workflow exports).</summary>
		Task<ProtectedReadResult> RevealForWorkloadAsync(int departmentId, RecordAggregate aggregate, string purpose, CancellationToken cancellationToken = default);

		Task<ProtectedReadResult> RevealForWorkloadAsync(int departmentId, IncidentReportAggregate aggregate, string purpose, CancellationToken cancellationToken = default);
	}

	public static class ProtectedReadResultExtensions
	{
		/// <summary>
		/// Fails closed: an operation that must see the content (finalize, queue a submission, restore a draft,
		/// diff, produce a packet) cannot run on placeholders. Throws with the machine-readable reason the
		/// clients map onto the step-up flow.
		/// </summary>
		public static ProtectedReadResult RequireRevealed(this ProtectedReadResult result, string operation)
		{
			if (result != null && result.RedactedFields != null && result.RedactedFields.Count > 0)
				throw new RecordProtectedContentException(result.ProtectedReason ?? "step_up_required", operation);
			return result;
		}

		/// <summary>Folds one result into another (aggregate reveals run several batches).</summary>
		public static ProtectedReadResult Merge(this ProtectedReadResult target, ProtectedReadResult other)
		{
			if (target == null)
				return other;
			if (other == null)
				return target;

			target.IsProtected |= other.IsProtected;
			if (other.RedactedFields != null)
			{
				foreach (var field in other.RedactedFields)
				{
					if (!target.RedactedFields.Contains(field))
						target.RedactedFields.Add(field);
				}
			}
			target.ProtectedReason ??= other.ProtectedReason;
			return target;
		}
	}
}
