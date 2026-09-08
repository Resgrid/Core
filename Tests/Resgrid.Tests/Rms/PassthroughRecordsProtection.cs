using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Tests.Rms
{
	/// <summary>
	/// The RMS protection seam for fixtures that test everything except Advanced Data Protection: writes pass
	/// through untouched, reads reveal nothing because nothing is sealed, and the workload lanes always open.
	/// The ADP behaviour itself is covered by RecordsProtectionServiceTests against the real service.
	/// </summary>
	public sealed class PassthroughRecordsProtection : IRecordsProtectionService
	{
		public bool Enforced { get; set; }
		public int CatalogVersion { get; set; }
		public bool AllowWorkload { get; set; } = true;
		public List<string> Writes { get; } = new List<string>();

		private Task Write(string what) { Writes.Add(what); return Task.CompletedTask; }
		private static Task<ProtectedReadResult> Empty() => Task.FromResult(new ProtectedReadResult());

		public Task<int> GetCatalogVersionAsync(int departmentId) => Task.FromResult(CatalogVersion);
		public Task<bool> IsEnforcedAsync(int departmentId) => Task.FromResult(Enforced);
		public Task ProtectDetailsAsync(int departmentId, RmsOperationalRecordDetail row, RmsOperationalRecordDetail existing, string userId = null, CancellationToken cancellationToken = default) => Write("details");
		public Task ProtectAttachmentAsync(int departmentId, RmsRecordAttachment row, RmsRecordAttachment existing, string userId = null, CancellationToken cancellationToken = default) => Write("attachment");
		public Task ProtectRevisionAsync(int departmentId, RmsRevision row, string userId = null, CancellationToken cancellationToken = default) => Write("revision");
		public Task ProtectLocationAsync(int departmentId, RmsLocation row, RmsLocation existing, string userId = null, CancellationToken cancellationToken = default) => Write("location");
		public Task ProtectNarrativeAsync(int departmentId, RmsNarrative row, RmsNarrative existing, string userId = null, CancellationToken cancellationToken = default) => Write("narrative");
		public Task ProtectSourceFactAsync(int departmentId, RmsSourceFact row, RmsSourceFact existing, string userId = null, CancellationToken cancellationToken = default) => Write("fact");
		public Task ProtectCasualtyAsync(int departmentId, RmsCasualtyRescue row, RmsCasualtyRescue existing, string userId = null, CancellationToken cancellationToken = default) => Write("casualty");
		public Task ProtectExposureAsync(int departmentId, RmsExposure row, RmsExposure existing, string userId = null, CancellationToken cancellationToken = default) => Write("exposure");
		public Task ProtectModuleAsync(int departmentId, RmsIncidentModule row, RmsIncidentModule existing, string userId = null, CancellationToken cancellationToken = default) => Write("module");
		public Task ProtectPropertyAsync(int departmentId, RmsIncidentProperty row, RmsIncidentProperty existing, string userId = null, CancellationToken cancellationToken = default) => Write("property");
		public Task ProtectVehicleAsync(int departmentId, RmsIncidentVehicle row, RmsIncidentVehicle existing, string userId = null, CancellationToken cancellationToken = default) => Write("vehicle");
		public Task ProtectResourceAsync(int departmentId, RmsIncidentResource row, RmsIncidentResource existing, string userId = null, CancellationToken cancellationToken = default) => Write("resource");
		public Task ProtectSubmissionAsync(int departmentId, RmsSubmission row, string userId = null, CancellationToken cancellationToken = default) => Write("submission");
		public Task ProtectSignatureAsync(int departmentId, RmsSignature row, string userId = null, CancellationToken cancellationToken = default) => Write("signature");
		public Task ProtectEvidenceAsync(int departmentId, RmsEvidenceArtifact row, string userId = null, CancellationToken cancellationToken = default) => Write("evidence");
		public Task ProtectDisclosureRequestAsync(int departmentId, RmsDisclosureRequest row, RmsDisclosureRequest existing, string userId = null, CancellationToken cancellationToken = default) => Write("disclosure-request");
		public Task ProtectDisclosureProductionAsync(int departmentId, RmsDisclosureProduction row, string userId = null, CancellationToken cancellationToken = default) => Write("disclosure-production");
		public Task ProtectLegalHoldAsync(int departmentId, RmsRecordLegalHold row, RmsRecordLegalHold existing, string userId = null, CancellationToken cancellationToken = default) => Write("legal-hold");
		public Task ProtectExportRunAsync(int departmentId, RmsExportRun row, string userId = null, CancellationToken cancellationToken = default) => Write("export-run");
		public Task ProtectValuesAsync(int departmentId, IReadOnlyList<RmsRecordValue> rows, string userId = null, CancellationToken cancellationToken = default) => Write("values:" + (rows == null ? 0 : System.Linq.Enumerable.Count(rows, r => r.ProtectionRequired)));
		public Task<ProtectedReadResult> RevealValuesAsync(int departmentId, IReadOnlyList<RmsRecordValue> rows, CancellationToken cancellationToken = default) => Empty();
		public Task<ProtectedReadResult> RevealValuesForWorkloadAsync(int departmentId, IReadOnlyList<RmsRecordValue> rows, string purpose, CancellationToken cancellationToken = default) => Empty();

		public Task<ProtectedReadResult> RevealAsync(int departmentId, RecordAggregate aggregate, CancellationToken cancellationToken = default) { var r = new ProtectedReadResult(); if (aggregate != null) aggregate.Protection = r; return Task.FromResult(r); }
		public Task<ProtectedReadResult> RevealAsync(int departmentId, IncidentReportAggregate aggregate, CancellationToken cancellationToken = default) { var r = new ProtectedReadResult(); if (aggregate != null) aggregate.Protection = r; return Task.FromResult(r); }
		public Task<ProtectedReadResult> RevealAsync(int departmentId, IncidentAnalysisAggregate aggregate, CancellationToken cancellationToken = default) { var r = new ProtectedReadResult(); if (aggregate != null) aggregate.Protection = r; return Task.FromResult(r); }
		public Task<ProtectedReadResult> RevealDetailsAsync(int departmentId, IReadOnlyList<RmsOperationalRecordDetail> rows, CancellationToken cancellationToken = default) => Empty();
		public Task<ProtectedReadResult> RevealRevisionsAsync(int departmentId, IReadOnlyList<RmsRevision> rows, CancellationToken cancellationToken = default) => Empty();
		public Task<ProtectedReadResult> RevealAttachmentsAsync(int departmentId, IReadOnlyList<RmsRecordAttachment> rows, bool includeData, CancellationToken cancellationToken = default) => Empty();
		public Task<ProtectedReadResult> RevealEvidenceAsync(int departmentId, IReadOnlyList<RmsEvidenceArtifact> rows, CancellationToken cancellationToken = default) => Empty();
		public Task<ProtectedReadResult> RevealDisclosureRequestsAsync(int departmentId, IReadOnlyList<RmsDisclosureRequest> rows, CancellationToken cancellationToken = default) => Empty();
		public Task<ProtectedReadResult> RevealDisclosureProductionsAsync(int departmentId, IReadOnlyList<RmsDisclosureProduction> rows, CancellationToken cancellationToken = default) => Empty();
		public Task<ProtectedReadResult> RevealLegalHoldsAsync(int departmentId, IReadOnlyList<RmsRecordLegalHold> rows, CancellationToken cancellationToken = default) => Empty();
		public Task<ProtectedReadResult> RevealSubmissionsAsync(int departmentId, IReadOnlyList<RmsSubmission> rows, CancellationToken cancellationToken = default) => Empty();

		// RMS-5 / catalog v13 rows
		public Task ProtectOccupancyAsync(int departmentId, RmsOccupancy row, RmsOccupancy existing, string userId = null, CancellationToken cancellationToken = default) => Write("occupancy");
		public Task<ProtectedReadResult> RevealOccupanciesAsync(int departmentId, IReadOnlyList<RmsOccupancy> rows, CancellationToken cancellationToken = default) => Empty();
		public Task ProtectOccupancyHazardAsync(int departmentId, RmsOccupancyHazard row, RmsOccupancyHazard existing, string userId = null, CancellationToken cancellationToken = default) => Write("occupancy hazard");
		public Task<ProtectedReadResult> RevealOccupancyHazardsAsync(int departmentId, IReadOnlyList<RmsOccupancyHazard> rows, CancellationToken cancellationToken = default) => Empty();
		public Task ProtectInspectionAsync(int departmentId, RmsInspection row, RmsInspection existing, string userId = null, CancellationToken cancellationToken = default) => Write("inspection");
		public Task<ProtectedReadResult> RevealInspectionsAsync(int departmentId, IReadOnlyList<RmsInspection> rows, CancellationToken cancellationToken = default) => Empty();
		public Task ProtectViolationAsync(int departmentId, RmsViolation row, RmsViolation existing, string userId = null, CancellationToken cancellationToken = default) => Write("violation");
		public Task<ProtectedReadResult> RevealViolationsAsync(int departmentId, IReadOnlyList<RmsViolation> rows, CancellationToken cancellationToken = default) => Empty();
		public Task ProtectPermitAsync(int departmentId, RmsPermit row, RmsPermit existing, string userId = null, CancellationToken cancellationToken = default) => Write("permit");
		public Task<ProtectedReadResult> RevealPermitsAsync(int departmentId, IReadOnlyList<RmsPermit> rows, CancellationToken cancellationToken = default) => Empty();
		public Task ProtectPlanReviewAsync(int departmentId, RmsPlanReview row, RmsPlanReview existing, string userId = null, CancellationToken cancellationToken = default) => Write("plan review");
		public Task<ProtectedReadResult> RevealPlanReviewsAsync(int departmentId, IReadOnlyList<RmsPlanReview> rows, CancellationToken cancellationToken = default) => Empty();
		public Task ProtectInvestigationCaseAsync(int departmentId, RmsInvestigationCase row, RmsInvestigationCase existing, string userId = null, CancellationToken cancellationToken = default) => Write("investigation case");
		public Task<ProtectedReadResult> RevealInvestigationCasesAsync(int departmentId, IReadOnlyList<RmsInvestigationCase> rows, CancellationToken cancellationToken = default) => Empty();
		public Task ProtectInvestigationNoteAsync(int departmentId, RmsInvestigationNote row, RmsInvestigationNote existing, string userId = null, CancellationToken cancellationToken = default) => Write("investigation note");
		public Task<ProtectedReadResult> RevealInvestigationNotesAsync(int departmentId, IReadOnlyList<RmsInvestigationNote> rows, CancellationToken cancellationToken = default) => Empty();
		public Task ProtectInvestigationEvidenceAsync(int departmentId, RmsInvestigationEvidence row, RmsInvestigationEvidence existing, string userId = null, CancellationToken cancellationToken = default) => Write("investigation evidence");
		public Task<ProtectedReadResult> RevealInvestigationEvidenceAsync(int departmentId, IReadOnlyList<RmsInvestigationEvidence> rows, CancellationToken cancellationToken = default) => Empty();
		public Task ProtectInvestigationCustodyAsync(int departmentId, RmsInvestigationCustody row, string userId = null, CancellationToken cancellationToken = default) => Write("custody transfer");
		public Task<ProtectedReadResult> RevealInvestigationCustodyAsync(int departmentId, IReadOnlyList<RmsInvestigationCustody> rows, CancellationToken cancellationToken = default) => Empty();
		public Task ProtectInvestigationReferralAsync(int departmentId, RmsInvestigationReferral row, RmsInvestigationReferral existing, string userId = null, CancellationToken cancellationToken = default) => Write("referral");
		public Task<ProtectedReadResult> RevealInvestigationReferralsAsync(int departmentId, IReadOnlyList<RmsInvestigationReferral> rows, CancellationToken cancellationToken = default) => Empty();
		public Task ProtectQualityReviewAsync(int departmentId, RmsQualityReview row, RmsQualityReview existing, string userId = null, CancellationToken cancellationToken = default) => Write("quality review");
		public Task<ProtectedReadResult> RevealQualityReviewsAsync(int departmentId, IReadOnlyList<RmsQualityReview> rows, CancellationToken cancellationToken = default) => Empty();
		public Task ProtectPreventionAttachmentAsync(int departmentId, RmsPreventionAttachment row, RmsPreventionAttachment existing, string userId = null, CancellationToken cancellationToken = default) => Write("prevention attachment");
		public Task<ProtectedReadResult> RevealPreventionAttachmentsAsync(int departmentId, IReadOnlyList<RmsPreventionAttachment> rows, bool includeData, CancellationToken cancellationToken = default) => Empty();
		public Task<ProtectedReadResult> RevealExportRunsAsync(int departmentId, IReadOnlyList<RmsExportRun> rows, bool includeData, CancellationToken cancellationToken = default) => Empty();
		public Task<bool> ResolveSubmissionForWorkloadAsync(int departmentId, RmsSubmission submission, string purpose, CancellationToken cancellationToken = default) => Task.FromResult(AllowWorkload);
		public Task<ProtectedReadResult> RevealForWorkloadAsync(int departmentId, RecordAggregate aggregate, string purpose, CancellationToken cancellationToken = default) => Empty();
		public Task<ProtectedReadResult> RevealForWorkloadAsync(int departmentId, IncidentReportAggregate aggregate, string purpose, CancellationToken cancellationToken = default) => Empty();
	}
}
