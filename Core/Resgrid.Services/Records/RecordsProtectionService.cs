using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;

namespace Resgrid.Services.Records
{
	/// <summary>
	/// The RMS protection seam (RMS plan section 5.9; ADP catalog v10). Every Records service writes cataloged
	/// columns through <c>Protect*</c> and reads them through <c>Reveal*</c>; this class is the only place that
	/// knows which RmsProtectedFields map belongs to which entity, which grant the caller holds, and which
	/// workload purposes a department has acknowledged. It owns no state and touches no repository.
	/// </summary>
	public sealed class RecordsProtectionService : IRecordsProtectionService, IRecordsProtectedReadService
	{
		public const string NerisSubmissionPurpose = "neris-submission";
		public const string RecordsExportPurpose = "records-export";

		private readonly IProtectedReadService _reads;
		private readonly IProtectedWriteService _writes;
		private readonly IProtectedGrantContext _grant;
		private readonly IDepartmentDataProtectionService _dataProtection;
		private readonly INerisProfileService _neris;

		public RecordsProtectionService(IProtectedReadService reads, IProtectedWriteService writes, IProtectedGrantContext grant,
			IDepartmentDataProtectionService dataProtection, INerisProfileService neris)
		{
			_reads = reads;
			_writes = writes;
			_grant = grant;
			_dataProtection = dataProtection;
			_neris = neris;
		}

		public async Task<int> GetCatalogVersionAsync(int departmentId)
		{
			try { return await _dataProtection.GetPinnedCatalogVersionAsync(departmentId); }
			catch (Exception ex) { Logging.LogException(ex, $"Pinned catalog version lookup failed for department {departmentId}."); return 0; }
		}

		/// <summary>
		/// The catalog version stamped on a row that was just sealed. Unlike <see cref="GetCatalogVersionAsync"/>,
		/// which reports 0 for the read-side callers that only decorate a projection, this fails the write: a row
		/// carrying real envelopes but a recorded version of 0 is invisible to the enrollment upgrade sweep that
		/// is supposed to migrate it, and the write has not been persisted yet when this runs.
		/// </summary>
		private async Task<int> RequiredCatalogVersionAsync(int departmentId, string operation)
		{
			var version = await GetCatalogVersionAsync(departmentId);
			if (version <= 0)
				throw new RecordProtectedContentException("catalog_version_unavailable", operation);
			return version;
		}

		public async Task<bool> IsEnforcedAsync(int departmentId)
		{
			try { return await _dataProtection.IsProtectionEnforcedAsync(departmentId); }
			catch (Exception ex) { Logging.LogException(ex, $"Protection-state lookup failed for department {departmentId}; treating as enforced."); return true; }
		}

		#region Writes

		private string GrantToken => _grant.GrantToken;
		private bool Workload => _grant.IsWorkloadCaller;

		private async Task ApplyAsync<T>(int departmentId, T row, T existing, string rowKey,
			IReadOnlyDictionary<string, (Func<T, string> Get, Action<T, string> Set)> accessors, Action<T, int> mark, string userId, string operation,
			CancellationToken cancellationToken) where T : class
		{
			if (row == null)
				return;

			var marked = false;
			var result = await _writes.PrepareRecordsEntityWriteAsync(departmentId, row, existing, rowKey, accessors, () => marked = true,
				GrantToken, userId ?? _grant.UserId, Workload, cancellationToken);
			if (!result.Success)
				throw new RecordProtectedContentException(result.Reason, operation);
			if (marked)
				mark(row, await RequiredCatalogVersionAsync(departmentId, operation));
		}

		private async Task ApplyCompanionsAsync<T>(int departmentId, T row, string rowKey,
			IReadOnlyDictionary<string, (Func<T, decimal?> Get, Action<T, decimal?> Set, Func<T, string> GetEnvelope, Action<T, string> SetEnvelope)> companions,
			Action<T, int> mark, string userId, string operation, CancellationToken cancellationToken) where T : class
		{
			if (row == null)
				return;

			var marked = false;
			var result = await _writes.PrepareRecordsCompanionWriteAsync(departmentId, row, rowKey, companions, () => marked = true,
				GrantToken, userId ?? _grant.UserId, Workload, cancellationToken);
			if (!result.Success)
				throw new RecordProtectedContentException(result.Reason, operation);
			if (marked)
				mark(row, await RequiredCatalogVersionAsync(departmentId, operation));
		}

		public Task ProtectDetailsAsync(int departmentId, RmsOperationalRecordDetail row, RmsOperationalRecordDetail existing, string userId = null, CancellationToken cancellationToken = default)
			=> ApplyAsync(departmentId, row, existing, row?.RmsOperationalRecordDetailId, RmsProtectedFields.Details, (r, v) => { r.IsProtected = true; r.ProtectedCatalogVersion = v; }, userId, "record details", cancellationToken);

		public async Task ProtectAttachmentAsync(int departmentId, RmsRecordAttachment row, RmsRecordAttachment existing, string userId = null, CancellationToken cancellationToken = default)
		{
			if (row == null)
				return;
			var result = await _writes.PrepareRecordsAttachmentWriteAsync(departmentId, row, existing, GrantToken, userId ?? _grant.UserId, Workload, cancellationToken);
			if (!result.Success)
				throw new RecordProtectedContentException(result.Reason, "attachment");
			if (row.IsProtected && row.ProtectedCatalogVersion == 0)
				row.ProtectedCatalogVersion = await RequiredCatalogVersionAsync(departmentId, "attachment");
		}

		public Task ProtectRevisionAsync(int departmentId, RmsRevision row, string userId = null, CancellationToken cancellationToken = default)
			=> ApplyAsync(departmentId, row, null, row?.RmsRevisionId, RmsProtectedFields.Revisions, (r, v) => { r.IsProtected = true; r.ProtectedCatalogVersion = v; }, userId, "revision", cancellationToken);

		public async Task ProtectLocationAsync(int departmentId, RmsLocation row, RmsLocation existing, string userId = null, CancellationToken cancellationToken = default)
		{
			await ApplyAsync(departmentId, row, existing, row?.RmsLocationId, RmsProtectedFields.Locations, (r, v) => { r.IsProtected = true; r.ProtectedCatalogVersion = v; }, userId, "location", cancellationToken);
			await ApplyCompanionsAsync(departmentId, row, row?.RmsLocationId, RmsProtectedFields.LocationCompanions, (r, v) => { r.IsProtected = true; r.ProtectedCatalogVersion = v; }, userId, "location", cancellationToken);
		}

		public Task ProtectNarrativeAsync(int departmentId, RmsNarrative row, RmsNarrative existing, string userId = null, CancellationToken cancellationToken = default)
			=> ApplyAsync(departmentId, row, existing, row?.RmsNarrativeId, RmsProtectedFields.Narratives, (r, v) => { r.IsProtected = true; r.ProtectedCatalogVersion = v; }, userId, "narrative", cancellationToken);

		public Task ProtectSourceFactAsync(int departmentId, RmsSourceFact row, RmsSourceFact existing, string userId = null, CancellationToken cancellationToken = default)
			=> ApplyAsync(departmentId, row, existing, row?.RmsSourceFactId, RmsProtectedFields.SourceFacts, (r, v) => { r.IsProtected = true; r.ProtectedCatalogVersion = v; }, userId, "source fact", cancellationToken);

		public Task ProtectCasualtyAsync(int departmentId, RmsCasualtyRescue row, RmsCasualtyRescue existing, string userId = null, CancellationToken cancellationToken = default)
			=> ApplyAsync(departmentId, row, existing, row?.RmsCasualtyRescueId, RmsProtectedFields.Casualties, (r, v) => { r.IsProtected = true; r.ProtectedCatalogVersion = v; }, userId, "casualty", cancellationToken);

		public async Task ProtectExposureAsync(int departmentId, RmsExposure row, RmsExposure existing, string userId = null, CancellationToken cancellationToken = default)
		{
			await ApplyAsync(departmentId, row, existing, row?.RmsExposureId, RmsProtectedFields.Exposures, (r, v) => { r.IsProtected = true; r.ProtectedCatalogVersion = v; }, userId, "exposure", cancellationToken);
			await ApplyCompanionsAsync(departmentId, row, row?.RmsExposureId, RmsProtectedFields.ExposureCompanions, (r, v) => { r.IsProtected = true; r.ProtectedCatalogVersion = v; }, userId, "exposure", cancellationToken);
		}

		public Task ProtectModuleAsync(int departmentId, RmsIncidentModule row, RmsIncidentModule existing, string userId = null, CancellationToken cancellationToken = default)
			=> ApplyAsync(departmentId, row, existing, row?.RmsIncidentModuleId, RmsProtectedFields.Modules, (r, v) => { r.IsProtected = true; r.ProtectedCatalogVersion = v; }, userId, "incident module", cancellationToken);

		public Task ProtectPropertyAsync(int departmentId, RmsIncidentProperty row, RmsIncidentProperty existing, string userId = null, CancellationToken cancellationToken = default)
			=> ApplyAsync(departmentId, row, existing, row?.RmsIncidentPropertyId, RmsProtectedFields.Properties, (r, v) => { r.IsProtected = true; r.ProtectedCatalogVersion = v; }, userId, "incident property", cancellationToken);

		public Task ProtectVehicleAsync(int departmentId, RmsIncidentVehicle row, RmsIncidentVehicle existing, string userId = null, CancellationToken cancellationToken = default)
			=> ApplyAsync(departmentId, row, existing, row?.RmsIncidentVehicleId, RmsProtectedFields.Vehicles, (r, v) => { r.IsProtected = true; r.ProtectedCatalogVersion = v; }, userId, "incident vehicle", cancellationToken);

		public Task ProtectResourceAsync(int departmentId, RmsIncidentResource row, RmsIncidentResource existing, string userId = null, CancellationToken cancellationToken = default)
			=> ApplyAsync(departmentId, row, existing, row?.RmsIncidentResourceId, RmsProtectedFields.Resources, (r, v) => { r.IsProtected = true; r.ProtectedCatalogVersion = v; }, userId, "incident resource", cancellationToken);

		public Task ProtectSubmissionAsync(int departmentId, RmsSubmission row, string userId = null, CancellationToken cancellationToken = default)
			=> ApplyAsync(departmentId, row, null, row?.RmsSubmissionId, RmsProtectedFields.Submissions, (r, v) => { r.IsProtected = true; r.ProtectedCatalogVersion = v; }, userId, "submission", cancellationToken);

		public Task ProtectSignatureAsync(int departmentId, RmsSignature row, string userId = null, CancellationToken cancellationToken = default)
			=> ApplyAsync(departmentId, row, null, row?.RmsSignatureId, RmsProtectedFields.Signatures, (r, v) => { r.IsProtected = true; r.ProtectedCatalogVersion = v; }, userId, "signature", cancellationToken);

		public Task ProtectEvidenceAsync(int departmentId, RmsEvidenceArtifact row, string userId = null, CancellationToken cancellationToken = default)
			=> ApplyAsync(departmentId, row, null, row?.RmsEvidenceArtifactId, RmsProtectedFields.Evidence, (r, v) => { r.IsProtected = true; r.ProtectedCatalogVersion = v; }, userId, "evidence", cancellationToken);

		public Task ProtectDisclosureRequestAsync(int departmentId, RmsDisclosureRequest row, RmsDisclosureRequest existing, string userId = null, CancellationToken cancellationToken = default)
			=> ApplyAsync(departmentId, row, existing, row?.RmsDisclosureRequestId, RmsProtectedFields.DisclosureRequests, (r, v) => { r.IsProtected = true; r.ProtectedCatalogVersion = v; }, userId, "disclosure request", cancellationToken);

		public Task ProtectDisclosureProductionAsync(int departmentId, RmsDisclosureProduction row, string userId = null, CancellationToken cancellationToken = default)
			=> ApplyAsync(departmentId, row, null, row?.RmsDisclosureProductionId, RmsProtectedFields.DisclosureProductions, (r, v) => { r.IsProtected = true; r.ProtectedCatalogVersion = v; }, userId, "disclosure production", cancellationToken);

		public Task ProtectLegalHoldAsync(int departmentId, RmsRecordLegalHold row, RmsRecordLegalHold existing, string userId = null, CancellationToken cancellationToken = default)
			=> ApplyAsync(departmentId, row, existing, row?.RmsRecordLegalHoldId, RmsProtectedFields.LegalHolds, (r, v) => { r.IsProtected = true; r.ProtectedCatalogVersion = v; }, userId, "legal hold", cancellationToken);

		public async Task ProtectExportRunAsync(int departmentId, RmsExportRun row, string userId = null, CancellationToken cancellationToken = default)
		{
			if (row == null)
				return;
			var marked = false;
			var result = await _writes.PrepareRecordsBinaryWriteAsync(departmentId, RmsProtectedFields.ExportRunDataFieldId, row.RmsExportRunId, row.Data, bytes => row.Data = bytes,
				() => marked = true, GrantToken, userId ?? _grant.UserId, Workload, cancellationToken);
			if (!result.Success)
				throw new RecordProtectedContentException(result.Reason, "export run");
			if (marked)
			{
				row.IsProtected = true;
				row.ProtectedCatalogVersion = await RequiredCatalogVersionAsync(departmentId, "export run");
			}
		}

		#endregion

		#region Ambient reveals

		private string User => _grant.UserId;

		private static IReadOnlyList<(T Entity, string RowKey)> Rows<T>(IEnumerable<T> rows, Func<T, string> key) where T : class
			=> (rows ?? Enumerable.Empty<T>()).Where(r => r != null).Select(r => (r, key(r))).ToList();

		public async Task ProtectValuesAsync(int departmentId, IReadOnlyList<RmsRecordValue> rows, string userId = null, CancellationToken cancellationToken = default)
		{
			foreach (var row in (rows ?? Array.Empty<RmsRecordValue>()).Where(r => r != null && r.ProtectionRequired))
				await ApplyAsync(departmentId, row, null, row.RmsRecordValueId, RmsProtectedFields.Values, (r, v) => { r.IsProtected = true; r.ProtectedCatalogVersion = v; }, userId, "typed value", cancellationToken);
		}

		public Task<ProtectedReadResult> RevealValuesAsync(int departmentId, IReadOnlyList<RmsRecordValue> rows, CancellationToken cancellationToken = default)
			=> _reads.ResolveRecordsEntitiesForReadAsync(departmentId, Rows(rows, r => r.RmsRecordValueId), RmsProtectedFields.Values, GrantToken, User, cancellationToken);

		public Task<ProtectedReadResult> RevealValuesForWorkloadAsync(int departmentId, IReadOnlyList<RmsRecordValue> rows, string purpose, CancellationToken cancellationToken = default)
			=> _reads.ResolveRecordsEntitiesForWorkloadAsync(departmentId, purpose, Rows(rows, r => r.RmsRecordValueId), RmsProtectedFields.Values, cancellationToken);

		public Task<ProtectedReadResult> RevealAsync(int departmentId, RecordAggregate aggregate, CancellationToken cancellationToken = default)
			=> ResolveAggregateAsync(departmentId, aggregate, GrantToken, User, cancellationToken);

		public Task<ProtectedReadResult> RevealAsync(int departmentId, IncidentReportAggregate aggregate, CancellationToken cancellationToken = default)
			=> ResolveIncidentAsync(departmentId, aggregate, GrantToken, User, cancellationToken);

		public Task<ProtectedReadResult> RevealAsync(int departmentId, IncidentAnalysisAggregate aggregate, CancellationToken cancellationToken = default)
			=> ResolveAnalysisAsync(departmentId, aggregate, GrantToken, User, cancellationToken);

		public Task<ProtectedReadResult> RevealDetailsAsync(int departmentId, IReadOnlyList<RmsOperationalRecordDetail> rows, CancellationToken cancellationToken = default)
			=> _reads.ResolveRecordsEntitiesForReadAsync(departmentId, Rows(rows, r => r.RmsOperationalRecordDetailId), RmsProtectedFields.Details, GrantToken, User, cancellationToken);

		public Task<ProtectedReadResult> RevealRevisionsAsync(int departmentId, IReadOnlyList<RmsRevision> rows, CancellationToken cancellationToken = default)
			=> ResolveRevisionsAsync(departmentId, rows, GrantToken, User, cancellationToken);

		public Task<ProtectedReadResult> RevealAttachmentsAsync(int departmentId, IReadOnlyList<RmsRecordAttachment> rows, bool includeData, CancellationToken cancellationToken = default)
			=> ResolveAttachmentsAsync(departmentId, rows, GrantToken, User, includeData, cancellationToken);

		public Task<ProtectedReadResult> RevealEvidenceAsync(int departmentId, IReadOnlyList<RmsEvidenceArtifact> rows, CancellationToken cancellationToken = default)
			=> ResolveEvidenceAsync(departmentId, rows, GrantToken, User, cancellationToken);

		public Task<ProtectedReadResult> RevealDisclosureRequestsAsync(int departmentId, IReadOnlyList<RmsDisclosureRequest> rows, CancellationToken cancellationToken = default)
			=> ResolveDisclosureRequestsAsync(departmentId, rows, GrantToken, User, cancellationToken);

		public Task<ProtectedReadResult> RevealDisclosureProductionsAsync(int departmentId, IReadOnlyList<RmsDisclosureProduction> rows, CancellationToken cancellationToken = default)
			=> ResolveDisclosureProductionsAsync(departmentId, rows, GrantToken, User, cancellationToken);

		public Task<ProtectedReadResult> RevealLegalHoldsAsync(int departmentId, IReadOnlyList<RmsRecordLegalHold> rows, CancellationToken cancellationToken = default)
			=> ResolveLegalHoldsAsync(departmentId, rows, GrantToken, User, cancellationToken);

		public Task<ProtectedReadResult> RevealSubmissionsAsync(int departmentId, IReadOnlyList<RmsSubmission> rows, CancellationToken cancellationToken = default)
			=> ResolveSubmissionsAsync(departmentId, rows, GrantToken, User, cancellationToken);

		public async Task<ProtectedReadResult> RevealExportRunsAsync(int departmentId, IReadOnlyList<RmsExportRun> rows, bool includeData, CancellationToken cancellationToken = default)
		{
			var result = new ProtectedReadResult();
			foreach (var run in (rows ?? Array.Empty<RmsExportRun>()).Where(r => r != null))
			{
				if (!includeData)
				{
					if (run.Data != null && ProtectedReadService.IsBinaryEnveloped(run.Data))
					{
						run.Data = null;
						result.IsProtected = true;
						result.RedactedFields.Add(RmsProtectedFields.ExportRunDataFieldId);
					}
					continue;
				}

				var current = run;
				result.Merge(await _reads.ResolveRecordsBinaryForReadAsync(departmentId, RmsProtectedFields.ExportRunDataFieldId, current.RmsExportRunId, current.Data,
					bytes => current.Data = bytes, GrantToken, User, cancellationToken));
			}
			return result;
		}

		#endregion


		#region RMS-5 prevention and investigations (catalog v13)

		public Task ProtectOccupancyAsync(int departmentId, RmsOccupancy row, RmsOccupancy existing, string userId = null, CancellationToken cancellationToken = default)
			=> ApplyAsync(departmentId, row, existing, row?.RmsOccupancyId, RmsProtectedFields.Occupancies, (r, v) => { r.IsProtected = true; r.ProtectedCatalogVersion = v; }, userId, "occupancy", cancellationToken);

		public Task<ProtectedReadResult> RevealOccupanciesAsync(int departmentId, IReadOnlyList<RmsOccupancy> rows, CancellationToken cancellationToken = default)
			=> _reads.ResolveRecordsEntitiesForReadAsync(departmentId, Rows(rows, r => r.RmsOccupancyId), RmsProtectedFields.Occupancies, GrantToken, User, cancellationToken);

		public Task ProtectOccupancyHazardAsync(int departmentId, RmsOccupancyHazard row, RmsOccupancyHazard existing, string userId = null, CancellationToken cancellationToken = default)
			=> ApplyAsync(departmentId, row, existing, row?.RmsOccupancyHazardId, RmsProtectedFields.OccupancyHazards, (r, v) => { r.IsProtected = true; r.ProtectedCatalogVersion = v; }, userId, "occupancy hazard", cancellationToken);

		public Task<ProtectedReadResult> RevealOccupancyHazardsAsync(int departmentId, IReadOnlyList<RmsOccupancyHazard> rows, CancellationToken cancellationToken = default)
			=> _reads.ResolveRecordsEntitiesForReadAsync(departmentId, Rows(rows, r => r.RmsOccupancyHazardId), RmsProtectedFields.OccupancyHazards, GrantToken, User, cancellationToken);

		public Task ProtectInspectionAsync(int departmentId, RmsInspection row, RmsInspection existing, string userId = null, CancellationToken cancellationToken = default)
			=> ApplyAsync(departmentId, row, existing, row?.RmsInspectionId, RmsProtectedFields.Inspections, (r, v) => { r.IsProtected = true; r.ProtectedCatalogVersion = v; }, userId, "inspection", cancellationToken);

		public Task<ProtectedReadResult> RevealInspectionsAsync(int departmentId, IReadOnlyList<RmsInspection> rows, CancellationToken cancellationToken = default)
			=> _reads.ResolveRecordsEntitiesForReadAsync(departmentId, Rows(rows, r => r.RmsInspectionId), RmsProtectedFields.Inspections, GrantToken, User, cancellationToken);

		public Task ProtectViolationAsync(int departmentId, RmsViolation row, RmsViolation existing, string userId = null, CancellationToken cancellationToken = default)
			=> ApplyAsync(departmentId, row, existing, row?.RmsViolationId, RmsProtectedFields.Violations, (r, v) => { r.IsProtected = true; r.ProtectedCatalogVersion = v; }, userId, "violation", cancellationToken);

		public Task<ProtectedReadResult> RevealViolationsAsync(int departmentId, IReadOnlyList<RmsViolation> rows, CancellationToken cancellationToken = default)
			=> _reads.ResolveRecordsEntitiesForReadAsync(departmentId, Rows(rows, r => r.RmsViolationId), RmsProtectedFields.Violations, GrantToken, User, cancellationToken);

		public Task ProtectPermitAsync(int departmentId, RmsPermit row, RmsPermit existing, string userId = null, CancellationToken cancellationToken = default)
			=> ApplyAsync(departmentId, row, existing, row?.RmsPermitId, RmsProtectedFields.Permits, (r, v) => { r.IsProtected = true; r.ProtectedCatalogVersion = v; }, userId, "permit", cancellationToken);

		public Task<ProtectedReadResult> RevealPermitsAsync(int departmentId, IReadOnlyList<RmsPermit> rows, CancellationToken cancellationToken = default)
			=> _reads.ResolveRecordsEntitiesForReadAsync(departmentId, Rows(rows, r => r.RmsPermitId), RmsProtectedFields.Permits, GrantToken, User, cancellationToken);

		public Task ProtectPlanReviewAsync(int departmentId, RmsPlanReview row, RmsPlanReview existing, string userId = null, CancellationToken cancellationToken = default)
			=> ApplyAsync(departmentId, row, existing, row?.RmsPlanReviewId, RmsProtectedFields.PlanReviews, (r, v) => { r.IsProtected = true; r.ProtectedCatalogVersion = v; }, userId, "plan review", cancellationToken);

		public Task<ProtectedReadResult> RevealPlanReviewsAsync(int departmentId, IReadOnlyList<RmsPlanReview> rows, CancellationToken cancellationToken = default)
			=> _reads.ResolveRecordsEntitiesForReadAsync(departmentId, Rows(rows, r => r.RmsPlanReviewId), RmsProtectedFields.PlanReviews, GrantToken, User, cancellationToken);

		public Task ProtectInvestigationCaseAsync(int departmentId, RmsInvestigationCase row, RmsInvestigationCase existing, string userId = null, CancellationToken cancellationToken = default)
			=> ApplyAsync(departmentId, row, existing, row?.RmsInvestigationCaseId, RmsProtectedFields.InvestigationCases, (r, v) => { r.IsProtected = true; r.ProtectedCatalogVersion = v; }, userId, "investigation case", cancellationToken);

		public Task<ProtectedReadResult> RevealInvestigationCasesAsync(int departmentId, IReadOnlyList<RmsInvestigationCase> rows, CancellationToken cancellationToken = default)
			=> _reads.ResolveRecordsEntitiesForReadAsync(departmentId, Rows(rows, r => r.RmsInvestigationCaseId), RmsProtectedFields.InvestigationCases, GrantToken, User, cancellationToken);

		public Task ProtectInvestigationNoteAsync(int departmentId, RmsInvestigationNote row, RmsInvestigationNote existing, string userId = null, CancellationToken cancellationToken = default)
			=> ApplyAsync(departmentId, row, existing, row?.RmsInvestigationNoteId, RmsProtectedFields.InvestigationNotes, (r, v) => { r.IsProtected = true; r.ProtectedCatalogVersion = v; }, userId, "investigation note", cancellationToken);

		public Task<ProtectedReadResult> RevealInvestigationNotesAsync(int departmentId, IReadOnlyList<RmsInvestigationNote> rows, CancellationToken cancellationToken = default)
			=> _reads.ResolveRecordsEntitiesForReadAsync(departmentId, Rows(rows, r => r.RmsInvestigationNoteId), RmsProtectedFields.InvestigationNotes, GrantToken, User, cancellationToken);

		public Task ProtectInvestigationEvidenceAsync(int departmentId, RmsInvestigationEvidence row, RmsInvestigationEvidence existing, string userId = null, CancellationToken cancellationToken = default)
			=> ApplyAsync(departmentId, row, existing, row?.RmsInvestigationEvidenceId, RmsProtectedFields.InvestigationEvidence, (r, v) => { r.IsProtected = true; r.ProtectedCatalogVersion = v; }, userId, "investigation evidence", cancellationToken);

		public Task<ProtectedReadResult> RevealInvestigationEvidenceAsync(int departmentId, IReadOnlyList<RmsInvestigationEvidence> rows, CancellationToken cancellationToken = default)
			=> _reads.ResolveRecordsEntitiesForReadAsync(departmentId, Rows(rows, r => r.RmsInvestigationEvidenceId), RmsProtectedFields.InvestigationEvidence, GrantToken, User, cancellationToken);

		public Task ProtectInvestigationCustodyAsync(int departmentId, RmsInvestigationCustody row, string userId = null, CancellationToken cancellationToken = default)
			=> ApplyAsync(departmentId, row, null, row?.RmsInvestigationCustodyId, RmsProtectedFields.InvestigationCustody, (r, v) => { r.IsProtected = true; r.ProtectedCatalogVersion = v; }, userId, "custody transfer", cancellationToken);

		public Task<ProtectedReadResult> RevealInvestigationCustodyAsync(int departmentId, IReadOnlyList<RmsInvestigationCustody> rows, CancellationToken cancellationToken = default)
			=> _reads.ResolveRecordsEntitiesForReadAsync(departmentId, Rows(rows, r => r.RmsInvestigationCustodyId), RmsProtectedFields.InvestigationCustody, GrantToken, User, cancellationToken);

		public Task ProtectInvestigationReferralAsync(int departmentId, RmsInvestigationReferral row, RmsInvestigationReferral existing, string userId = null, CancellationToken cancellationToken = default)
			=> ApplyAsync(departmentId, row, existing, row?.RmsInvestigationReferralId, RmsProtectedFields.InvestigationReferrals, (r, v) => { r.IsProtected = true; r.ProtectedCatalogVersion = v; }, userId, "referral", cancellationToken);

		public Task<ProtectedReadResult> RevealInvestigationReferralsAsync(int departmentId, IReadOnlyList<RmsInvestigationReferral> rows, CancellationToken cancellationToken = default)
			=> _reads.ResolveRecordsEntitiesForReadAsync(departmentId, Rows(rows, r => r.RmsInvestigationReferralId), RmsProtectedFields.InvestigationReferrals, GrantToken, User, cancellationToken);

		public Task ProtectQualityReviewAsync(int departmentId, RmsQualityReview row, RmsQualityReview existing, string userId = null, CancellationToken cancellationToken = default)
			=> ApplyAsync(departmentId, row, existing, row?.RmsQualityReviewId, RmsProtectedFields.QualityReviews, (r, v) => { r.IsProtected = true; r.ProtectedCatalogVersion = v; }, userId, "quality review", cancellationToken);

		public Task<ProtectedReadResult> RevealQualityReviewsAsync(int departmentId, IReadOnlyList<RmsQualityReview> rows, CancellationToken cancellationToken = default)
			=> _reads.ResolveRecordsEntitiesForReadAsync(departmentId, Rows(rows, r => r.RmsQualityReviewId), RmsProtectedFields.QualityReviews, GrantToken, User, cancellationToken);

		public async Task ProtectPreventionAttachmentAsync(int departmentId, RmsPreventionAttachment row, RmsPreventionAttachment existing, string userId = null, CancellationToken cancellationToken = default)
		{
			if (row == null)
				return;
			await ApplyAsync(departmentId, row, existing, row.RmsPreventionAttachmentId, RmsProtectedFields.PreventionAttachments, (r, v) => { r.IsProtected = true; r.ProtectedCatalogVersion = v; }, userId, "prevention attachment", cancellationToken);
			if (row.Data == null || row.Data.Length == 0 || ProtectedReadService.IsBinaryEnveloped(row.Data))
				return;
			var marked = false;
			var current = row;
			var result = await _writes.PrepareRecordsBinaryWriteAsync(departmentId, RmsProtectedFields.PreventionAttachmentDataFieldId, row.RmsPreventionAttachmentId, row.Data,
				bytes => current.Data = bytes, () => marked = true, GrantToken, userId ?? _grant.UserId, Workload, cancellationToken);
			if (!result.Success)
				throw new RecordProtectedContentException(result.Reason, "prevention attachment");
			if (marked)
			{
				row.IsProtected = true;
				if (row.ProtectedCatalogVersion == 0)
					row.ProtectedCatalogVersion = await RequiredCatalogVersionAsync(departmentId, "prevention attachment");
			}
		}

		public async Task<ProtectedReadResult> RevealPreventionAttachmentsAsync(int departmentId, IReadOnlyList<RmsPreventionAttachment> rows, bool includeData, CancellationToken cancellationToken = default)
		{
			var result = await _reads.ResolveRecordsEntitiesForReadAsync(departmentId, Rows(rows, r => r.RmsPreventionAttachmentId), RmsProtectedFields.PreventionAttachments, GrantToken, User, cancellationToken);
			foreach (var attachment in (rows ?? Array.Empty<RmsPreventionAttachment>()).Where(r => r != null))
			{
				if (attachment.Data == null || !ProtectedReadService.IsBinaryEnveloped(attachment.Data))
					continue;
				if (!includeData)
				{
					attachment.Data = null;
					result.IsProtected = true;
					result.RedactedFields.Add(RmsProtectedFields.PreventionAttachmentDataFieldId);
					continue;
				}
				var current = attachment;
				result.Merge(await _reads.ResolveRecordsBinaryForReadAsync(departmentId, RmsProtectedFields.PreventionAttachmentDataFieldId, current.RmsPreventionAttachmentId, current.Data,
					bytes => current.Data = bytes, GrantToken, User, cancellationToken));
			}
			return result;
		}

		#endregion

		#region Explicit-grant reads (IRecordsProtectedReadService)

		public async Task<ProtectedReadResult> ResolveAggregateAsync(int departmentId, RecordAggregate aggregate, string grantToken, string userId, CancellationToken cancellationToken = default)
		{
			var result = new ProtectedReadResult();
			if (aggregate == null)
				return result;

			if (aggregate.Details != null)
				result.Merge(await _reads.ResolveRecordsEntitiesForReadAsync(departmentId, Rows(new[] { aggregate.Details }, r => r.RmsOperationalRecordDetailId), RmsProtectedFields.Details, grantToken, userId, cancellationToken));
			result.Merge(await ResolveAttachmentsAsync(departmentId, aggregate.Attachments, grantToken, userId, false, cancellationToken));
			result.Merge(await ResolveRevisionsAsync(departmentId, aggregate.Revisions, grantToken, userId, cancellationToken));
			aggregate.Protection = result;
			return result;
		}

		public async Task<ProtectedReadResult> ResolveIncidentAsync(int departmentId, IncidentReportAggregate aggregate, string grantToken, string userId, CancellationToken cancellationToken = default)
		{
			var result = new ProtectedReadResult();
			if (aggregate == null)
				return result;

			if (aggregate.Location != null)
			{
				result.Merge(await _reads.ResolveRecordsEntitiesForReadAsync(departmentId, Rows(new[] { aggregate.Location }, r => r.RmsLocationId), RmsProtectedFields.Locations, grantToken, userId, cancellationToken));
				result.Merge(await _reads.ResolveRecordsCompanionsForReadAsync(departmentId, Rows(new[] { aggregate.Location }, r => r.RmsLocationId), RmsProtectedFields.LocationCompanions, grantToken, userId, cancellationToken));
			}
			if (aggregate.Narrative != null)
				result.Merge(await _reads.ResolveRecordsEntitiesForReadAsync(departmentId, Rows(new[] { aggregate.Narrative }, r => r.RmsNarrativeId), RmsProtectedFields.Narratives, grantToken, userId, cancellationToken));
			result.Merge(await _reads.ResolveRecordsEntitiesForReadAsync(departmentId, Rows(aggregate.Facts, r => r.RmsSourceFactId), RmsProtectedFields.SourceFacts, grantToken, userId, cancellationToken));
			result.Merge(await _reads.ResolveRecordsEntitiesForReadAsync(departmentId, Rows(aggregate.Casualties, r => r.RmsCasualtyRescueId), RmsProtectedFields.Casualties, grantToken, userId, cancellationToken));
			result.Merge(await _reads.ResolveRecordsEntitiesForReadAsync(departmentId, Rows(aggregate.Exposures, r => r.RmsExposureId), RmsProtectedFields.Exposures, grantToken, userId, cancellationToken));
			result.Merge(await _reads.ResolveRecordsCompanionsForReadAsync(departmentId, Rows(aggregate.Exposures, r => r.RmsExposureId), RmsProtectedFields.ExposureCompanions, grantToken, userId, cancellationToken));
			result.Merge(await _reads.ResolveRecordsEntitiesForReadAsync(departmentId, Rows(aggregate.Modules, r => r.RmsIncidentModuleId), RmsProtectedFields.Modules, grantToken, userId, cancellationToken));
			result.Merge(await _reads.ResolveRecordsEntitiesForReadAsync(departmentId, Rows(aggregate.Resources, r => r.RmsIncidentResourceId), RmsProtectedFields.Resources, grantToken, userId, cancellationToken));
			result.Merge(await ResolveAttachmentsAsync(departmentId, aggregate.Attachments, grantToken, userId, false, cancellationToken));
			result.Merge(await ResolveEvidenceAsync(departmentId, aggregate.Evidence, grantToken, userId, cancellationToken));
			result.Merge(await ResolveSubmissionsAsync(departmentId, aggregate.Submissions, grantToken, userId, cancellationToken));
			result.Merge(await _reads.ResolveRecordsEntitiesForReadAsync(departmentId, Rows(aggregate.Signatures, r => r.RmsSignatureId), RmsProtectedFields.Signatures, grantToken, userId, cancellationToken));
			result.Merge(await ResolveRevisionsAsync(departmentId, aggregate.Revisions, grantToken, userId, cancellationToken));
			aggregate.Protection = result;
			return result;
		}

		public async Task<ProtectedReadResult> ResolveAnalysisAsync(int departmentId, IncidentAnalysisAggregate aggregate, string grantToken, string userId, CancellationToken cancellationToken = default)
		{
			var result = new ProtectedReadResult();
			if (aggregate == null)
				return result;

			result.Merge(await _reads.ResolveRecordsEntitiesForReadAsync(departmentId, Rows(aggregate.Modules, r => r.RmsIncidentModuleId), RmsProtectedFields.Modules, grantToken, userId, cancellationToken));
			result.Merge(await _reads.ResolveRecordsEntitiesForReadAsync(departmentId, Rows(aggregate.Properties, r => r.RmsIncidentPropertyId), RmsProtectedFields.Properties, grantToken, userId, cancellationToken));
			result.Merge(await _reads.ResolveRecordsEntitiesForReadAsync(departmentId, Rows(aggregate.Vehicles, r => r.RmsIncidentVehicleId), RmsProtectedFields.Vehicles, grantToken, userId, cancellationToken));
			result.Merge(await ResolveSubmissionsAsync(departmentId, aggregate.Submissions, grantToken, userId, cancellationToken));
			result.Merge(await ResolveRevisionsAsync(departmentId, aggregate.Revisions, grantToken, userId, cancellationToken));
			aggregate.Protection = result;
			return result;
		}

		public Task<ProtectedReadResult> ResolveRevisionsAsync(int departmentId, IReadOnlyList<RmsRevision> revisions, string grantToken, string userId, CancellationToken cancellationToken = default)
			=> _reads.ResolveRecordsEntitiesForReadAsync(departmentId, Rows(revisions, r => r.RmsRevisionId), RmsProtectedFields.Revisions, grantToken, userId, cancellationToken);

		public Task<ProtectedReadResult> ResolveAttachmentsAsync(int departmentId, IReadOnlyList<RmsRecordAttachment> attachments, string grantToken, string userId, bool includeData, CancellationToken cancellationToken = default)
			=> _reads.ResolveRecordsAttachmentsForReadAsync(departmentId, attachments ?? Array.Empty<RmsRecordAttachment>(), grantToken, userId, includeData, cancellationToken);

		public Task<ProtectedReadResult> ResolveDisclosureRequestsAsync(int departmentId, IReadOnlyList<RmsDisclosureRequest> requests, string grantToken, string userId, CancellationToken cancellationToken = default)
			=> _reads.ResolveRecordsEntitiesForReadAsync(departmentId, Rows(requests, r => r.RmsDisclosureRequestId), RmsProtectedFields.DisclosureRequests, grantToken, userId, cancellationToken);

		public Task<ProtectedReadResult> ResolveDisclosureProductionsAsync(int departmentId, IReadOnlyList<RmsDisclosureProduction> productions, string grantToken, string userId, CancellationToken cancellationToken = default)
			=> _reads.ResolveRecordsEntitiesForReadAsync(departmentId, Rows(productions, r => r.RmsDisclosureProductionId), RmsProtectedFields.DisclosureProductions, grantToken, userId, cancellationToken);

		public Task<ProtectedReadResult> ResolveEvidenceAsync(int departmentId, IReadOnlyList<RmsEvidenceArtifact> artifacts, string grantToken, string userId, CancellationToken cancellationToken = default)
			=> _reads.ResolveRecordsEntitiesForReadAsync(departmentId, Rows(artifacts, r => r.RmsEvidenceArtifactId), RmsProtectedFields.Evidence, grantToken, userId, cancellationToken);

		public Task<ProtectedReadResult> ResolveLegalHoldsAsync(int departmentId, IReadOnlyList<RmsRecordLegalHold> holds, string grantToken, string userId, CancellationToken cancellationToken = default)
			=> _reads.ResolveRecordsEntitiesForReadAsync(departmentId, Rows(holds, r => r.RmsRecordLegalHoldId), RmsProtectedFields.LegalHolds, grantToken, userId, cancellationToken);

		public Task<ProtectedReadResult> ResolveSubmissionsAsync(int departmentId, IReadOnlyList<RmsSubmission> submissions, string grantToken, string userId, CancellationToken cancellationToken = default)
			=> _reads.ResolveRecordsEntitiesForReadAsync(departmentId, Rows(submissions, r => r.RmsSubmissionId), RmsProtectedFields.Submissions, grantToken, userId, cancellationToken);

		#endregion

		#region Workload lane

		public async Task<bool> ResolveSubmissionForWorkloadAsync(int departmentId, RmsSubmission submission, string purpose, CancellationToken cancellationToken = default)
		{
			if (submission == null || !ProtectedDataEnvelope.HasEnvelopePrefix(submission.PayloadJson))
				return true;

			// The egress acknowledgement is the department's decision, recorded on its NERIS profile (RMS plan
			// section 5.9.4); without it the payload stays sealed and the submission fails closed.
			RmsNerisProfile profile;
			try { profile = await _neris.GetProfileAsync(departmentId); }
			catch (Exception ex) { Logging.LogException(ex, $"NERIS profile lookup failed for department {departmentId}; refusing protected egress."); return false; }
			if (profile == null || !profile.AllowProtectedContentEgress)
				return false;

			var result = await _reads.ResolveRecordsEntitiesForWorkloadAsync(departmentId, purpose ?? NerisSubmissionPurpose,
				Rows(new[] { submission }, r => r.RmsSubmissionId), RmsProtectedFields.Submissions, cancellationToken);
			return result.RedactedFields.Count == 0 && !ProtectedDataEnvelope.HasEnvelopePrefix(submission.PayloadJson);
		}

		public async Task<ProtectedReadResult> RevealForWorkloadAsync(int departmentId, RecordAggregate aggregate, string purpose, CancellationToken cancellationToken = default)
		{
			var result = new ProtectedReadResult();
			if (aggregate == null)
				return result;
			if (aggregate.Details != null)
				result.Merge(await _reads.ResolveRecordsEntitiesForWorkloadAsync(departmentId, purpose, Rows(new[] { aggregate.Details }, r => r.RmsOperationalRecordDetailId), RmsProtectedFields.Details, cancellationToken));
			result.Merge(await _reads.ResolveRecordsEntitiesForWorkloadAsync(departmentId, purpose, Rows(aggregate.Attachments, r => r.RmsRecordAttachmentId), RmsProtectedFields.Attachments, cancellationToken));
			aggregate.Protection = result;
			return result;
		}

		public async Task<ProtectedReadResult> RevealForWorkloadAsync(int departmentId, IncidentReportAggregate aggregate, string purpose, CancellationToken cancellationToken = default)
		{
			var result = new ProtectedReadResult();
			if (aggregate == null)
				return result;
			if (aggregate.Location != null)
				result.Merge(await _reads.ResolveRecordsEntitiesForWorkloadAsync(departmentId, purpose, Rows(new[] { aggregate.Location }, r => r.RmsLocationId), RmsProtectedFields.Locations, cancellationToken));
			if (aggregate.Narrative != null)
				result.Merge(await _reads.ResolveRecordsEntitiesForWorkloadAsync(departmentId, purpose, Rows(new[] { aggregate.Narrative }, r => r.RmsNarrativeId), RmsProtectedFields.Narratives, cancellationToken));
			result.Merge(await _reads.ResolveRecordsEntitiesForWorkloadAsync(departmentId, purpose, Rows(aggregate.Casualties, r => r.RmsCasualtyRescueId), RmsProtectedFields.Casualties, cancellationToken));
			result.Merge(await _reads.ResolveRecordsEntitiesForWorkloadAsync(departmentId, purpose, Rows(aggregate.Exposures, r => r.RmsExposureId), RmsProtectedFields.Exposures, cancellationToken));
			result.Merge(await _reads.ResolveRecordsEntitiesForWorkloadAsync(departmentId, purpose, Rows(aggregate.Attachments, r => r.RmsRecordAttachmentId), RmsProtectedFields.Attachments, cancellationToken));
			aggregate.Protection = result;
			return result;
		}

		#endregion
	}
}
