using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Paged, indexed list filter for the Records work queue (RMS plan section 5.10 work queue). Free-text
	/// search is served by the RMS-owned records index, not by this filter; nothing here runs LIKE over
	/// narrative.
	/// </summary>
	public sealed class RmsRecordQuery
	{
		public IList<int> States { get; set; }
		public string DefinitionKey { get; set; }
		public int? Year { get; set; }
		public int? CallId { get; set; }
		public string AuthorUserId { get; set; }
		public string OwnerUserId { get; set; }
		public int? StationGroupId { get; set; }
		/// <summary>When non-null, restricts to Records whose group scope intersects these ids or whose author/owner/participant/reviewer is <see cref="ViewerUserId"/>.</summary>
		public IList<int> VisibleGroupIds { get; set; }
		public string ViewerUserId { get; set; }
		public bool IncludeLegacy { get; set; }
		public int Skip { get; set; }
		public int Take { get; set; } = 50;
	}

	/// <summary>RmsOperationalRecords: every read is department-scoped; there is no ID-only lookup.</summary>
	public interface IRmsOperationalRecordsRepository : IRepository<RmsOperationalRecord>
	{
		Task<RmsOperationalRecord> GetByIdForDepartmentAsync(int departmentId, string recordId);
		Task<RmsOperationalRecord> GetByIdempotencyKeyAsync(int departmentId, string idempotencyKey);
		Task<IEnumerable<RmsOperationalRecord>> GetByCallAsync(int departmentId, int callId);
		/// <summary>Records of one definition whose StartedOn falls in [start, end], in the given states; the report feed.</summary>
		Task<IEnumerable<RmsOperationalRecord>> GetByDefinitionAndStartedRangeAsync(int departmentId, string definitionKey, IEnumerable<int> states, DateTime start, DateTime end);
		Task<IEnumerable<RmsOperationalRecord>> GetByOwnerAndStatesAsync(int departmentId, string ownerUserId, IEnumerable<int> states);
		Task<IEnumerable<RmsOperationalRecord>> GetByDepartmentAndStatesAsync(int departmentId, IEnumerable<int> states, int? year, int skip, int take);
		Task<int> CountByDepartmentAsync(int departmentId, IEnumerable<int> states);
		Task<int> CountVisibleAsync(int departmentId, IEnumerable<int> states, List<int> visibleGroupIds, string userId);
		Task<int> CountCreatedSinceAsync(int departmentId, DateTime sinceUtc);
		Task<int> CountFinalizedSinceAsync(int departmentId, DateTime sinceUtc);
		/// <summary>Every live Record in the department, any state.</summary>
		Task<int> CountAllAsync(int departmentId);
		/// <summary>Live Records in Draft, ReadyForReview, Returned or Approved (accountability view, plan 4.7).</summary>
		Task<IEnumerable<RmsOperationalRecord>> GetOpenAsync(int departmentId);
		/// <summary>Live Finalized/Amended Records whose FinalizedOn is at or after the instant.</summary>
		Task<IEnumerable<RmsOperationalRecord>> GetFinalizedSinceAsync(int departmentId, DateTime sinceUtc);
		/// <summary>Retention candidates (RMS-3, worker 43): live, closed Records finalized before the cutoff, oldest first.</summary>
		Task<IEnumerable<RmsOperationalRecord>> GetRetentionCandidatesAsync(int departmentId, DateTime cutoffUtc, int take, string afterId = null);
		/// <summary>Live Records with no RmsRecordGroupScope row: they stay department-wide under group scoping (plan 5.7.1).</summary>
		Task<int> CountWithoutGroupScopeAsync(int departmentId);
		Task<IEnumerable<int>> GetYearsAsync(int departmentId);
		/// <summary>Highest sequence already issued for a number prefix (e.g. "TRN-2026-"), 0 when none.</summary>
		Task<int> GetMaxRecordNumberSequenceAsync(int departmentId, string numberPrefix);
		/// <summary>
		/// Optimistic-concurrency guard: bumps RowVersion only if it still equals <paramref name="expectedRowVersion"/>.
		/// Returns false on a stale ETag. Run inside the caller's unit of work so the row lock serializes writers.
		/// </summary>
		Task<bool> TryBumpRowVersionAsync(int departmentId, string recordId, long expectedRowVersion, CancellationToken cancellationToken = default);
	}

	public interface IRmsOperationalRecordDetailsRepository : IRepository<RmsOperationalRecordDetail>
	{
		Task<RmsOperationalRecordDetail> GetDraftAsync(int departmentId, string recordId);
		Task<RmsOperationalRecordDetail> GetByRevisionAsync(int departmentId, string recordId, string revisionId);
		/// <summary>Working-draft detail rows for many records at once.</summary>
		Task<IEnumerable<RmsOperationalRecordDetail>> GetDraftsForRecordsAsync(int departmentId, IEnumerable<string> recordIds);
	}

	public interface IRmsRecordParticipantsRepository : IRepository<RmsRecordParticipant>
	{
		/// <summary><paramref name="revisionId"/> null returns the working-draft rows.</summary>
		Task<IEnumerable<RmsRecordParticipant>> GetForRecordAsync(int departmentId, string recordId, string revisionId);
		Task<IEnumerable<RmsRecordParticipant>> GetByUserAsync(int departmentId, string userId);
		/// <summary>Working-draft participant rows for many records at once.</summary>
		Task<IEnumerable<RmsRecordParticipant>> GetForRecordsAsync(int departmentId, IEnumerable<string> recordIds);
		Task<int> DeleteDraftForRecordAsync(int departmentId, string recordId, CancellationToken cancellationToken = default);
	}

	public interface IRmsRecordUnitResponsesRepository : IRepository<RmsRecordUnitResponse>
	{
		Task<IEnumerable<RmsRecordUnitResponse>> GetForRecordAsync(int departmentId, string recordId, string revisionId);
		Task<IEnumerable<RmsRecordUnitResponse>> GetByUnitAsync(int departmentId, int unitId);
		/// <summary>Working-draft unit response rows for many records at once.</summary>
		Task<IEnumerable<RmsRecordUnitResponse>> GetForRecordsAsync(int departmentId, IEnumerable<string> recordIds);
		Task<int> DeleteDraftForRecordAsync(int departmentId, string recordId, CancellationToken cancellationToken = default);
	}

	public interface IRmsRecordAttachmentsRepository : IRepository<RmsRecordAttachment>
	{
		/// <summary>Includes attachments removed from the draft. Caller must verify immutable revision membership and authorize the live parent.</summary>
		Task<RmsRecordAttachment> GetHistoricalByIdForDepartmentAsync(int departmentId, string attachmentId);
		Task<bool> ApplyScanResultAsync(int departmentId, string attachmentId, long expectedVersion, RmsAttachmentScanState state, DateTime now, CancellationToken cancellationToken = default);
		/// <summary>Metadata only: never loads Data.</summary>
		Task<IEnumerable<RmsRecordAttachment>> GetMetadataForRecordAsync(int departmentId, string recordId);
		/// <summary>Loads Data; authorized per Record by the caller on every request.</summary>
		Task<RmsRecordAttachment> GetByIdForDepartmentAsync(int departmentId, string attachmentId);
		/// <summary>
		/// Attachments still sitting at <see cref="RmsAttachmentScanState.Pending"/> — stored because the scanner
		/// was unreachable and the department chose availability over fail-closed. Worker 43 rescans them; without
		/// this sweep a Pending attachment would stay unscanned forever (RMS-1 gap closed in RMS-3).
		/// </summary>
		Task<IEnumerable<RmsRecordAttachment>> GetPendingScanAsync(int departmentId, int take);
	}

	public interface IRmsExternalReferencesRepository : IRepository<RmsExternalReference>
	{
		Task<IEnumerable<RmsExternalReference>> GetForRecordAsync(int departmentId, string recordId);
	}

	public interface IDomainEventOutboxRepository : IRepository<DomainEventOutboxEntry>
	{
		Task<long> GetNextSequenceAsync(int departmentId, string aggregateId);
		/// <summary>Leases up to <paramref name="batchSize"/> pending rows whose NextAttemptOn has passed (or is null) to <paramref name="leaseOwner"/>.</summary>
		Task<IEnumerable<DomainEventOutboxEntry>> ClaimPendingBatchAsync(string leaseOwner, TimeSpan leaseDuration, int batchSize, DateTime utcNow, CancellationToken cancellationToken = default);
		/// <summary>Leases one specific pending row (post-commit fast path); null when it is no longer pending or is leased elsewhere.</summary>
		Task<DomainEventOutboxEntry> ClaimByIdAsync(long domainEventOutboxId, string leaseOwner, TimeSpan leaseDuration, DateTime utcNow, CancellationToken cancellationToken = default);
		Task<bool> MarkDispatchedAsync(long domainEventOutboxId, DateTime utcNow, CancellationToken cancellationToken = default);
		Task<bool> MarkFailedAsync(long domainEventOutboxId, string error, DateTime? nextAttemptOn, bool terminal, CancellationToken cancellationToken = default);
		Task<int> CountByStateAsync(int state);
		Task<DateTime?> GetOldestPendingCreatedOnAsync();
		Task<int> PurgeDispatchedOlderThanAsync(DateTime cutoffUtc, CancellationToken cancellationToken = default);
	}

	public interface IRmsDepartmentCutoversRepository : IRepository<RmsDepartmentCutover>
	{
		Task<RmsDepartmentCutover> GetByDepartmentIdAsync(int departmentId);
		/// <summary>Every department whose cutover is currently Active (the search index sweep scope).</summary>
		Task<IEnumerable<RmsDepartmentCutover>> GetActiveAsync();
	}

	public interface IRmsDepartmentCutoverEventsRepository : IRepository<RmsDepartmentCutoverEvent>
	{
		Task<IEnumerable<RmsDepartmentCutoverEvent>> GetForCutoverAsync(int departmentId, int cutoverId);
	}

	public interface IRmsRevisionsRepository : IRepository<RmsRevision>
	{
		Task<IEnumerable<RmsRevision>> GetByIdsForDepartmentAsync(int departmentId, IEnumerable<string> revisionIds);
		Task<IEnumerable<RmsRevision>> GetForRecordAsync(int departmentId, string recordId);
		Task<RmsRevision> GetByIdForDepartmentAsync(int departmentId, string revisionId);
	}

	public interface IRmsAccessAuditsRepository : IRepository<RmsAccessAudit>
	{
		Task<IEnumerable<RmsAccessAudit>> GetForRecordAsync(int departmentId, string recordId, int take);
	}

	public interface IRmsRecordSearchProjectionsRepository : IRepository<RmsRecordSearchProjection>
	{
		Task<RmsRecordSearchProjection> GetByRecordIdAsync(int departmentId, string recordId);
		Task<IEnumerable<RmsRecordSearchProjection>> QueryAsync(int departmentId, RmsRecordQuery query);
		Task<int> CountAsync(int departmentId, RmsRecordQuery query);
		Task<IEnumerable<int>> GetYearsAsync(int departmentId);
		/// <summary>Projections (including soft-deleted ones) modified after <paramref name="since"/>, oldest first; the search index catch-up feed.</summary>
		/// <summary>
		/// Rows after the (ModifiedOn, id) cursor, oldest first. The id is part of the cursor because several rows
		/// routinely share a modified timestamp: a timestamp-only cursor skips whatever sat after the page break
		/// inside that group, and an offline client never learns those rows changed.
		/// </summary>
		Task<IEnumerable<RmsRecordSearchProjection>> GetModifiedSinceAsync(int departmentId, DateTime? since, int take, string sinceId = null);
		/// <summary>Live projections by record id, for post-retrieval loading of search hits.</summary>
		Task<IEnumerable<RmsRecordSearchProjection>> GetByIdsAsync(int departmentId, IEnumerable<string> recordIds);
	}

	public interface IRmsSearchIndexStatesRepository : IRepository<RmsSearchIndexState>
	{
		Task<RmsSearchIndexState> GetAsync(int departmentId, string indexName);
	}

	public interface IRmsRecordGroupScopesRepository : IRepository<RmsRecordGroupScope>
	{
		/// <summary>Live share identities for cache-scope invalidation. Excludes free-text reasons and expired/revoked grants.</summary>
		Task<IEnumerable<RmsRecordShare>> GetEffectiveSharesAsync(int departmentId, IEnumerable<int> groupIds);
		Task<IEnumerable<RmsRecordGroupScope>> GetForRecordAsync(int departmentId, string recordId);
		Task<IEnumerable<RmsRecordGroupScope>> GetForRecordsAsync(int departmentId, IEnumerable<string> recordIds);
		/// <summary>Deletes the Record's scope rows and inserts the supplied set; run inside the owning transaction.</summary>
		Task ReplaceForRecordAsync(int departmentId, string recordId, IEnumerable<RmsRecordGroupScope> scopes, CancellationToken cancellationToken = default);
		/// <summary>Distinct live Records anchored to each group, keyed by DepartmentGroupId (impact preview, plan 5.7.1).</summary>
		Task<IDictionary<int, int>> CountRecordsByGroupAsync(int departmentId);
	}

	public interface IRmsRecordSharesRepository : IRepository<RmsRecordShare>
	{
		Task<IEnumerable<RmsRecordShare>> GetForRecordAsync(int departmentId, string recordId);
	}

	/// <summary>
	/// Persisted due state per (Record, obligation) — registry M0170, RMS-3. The uniqueness of that pair is what
	/// makes worker 42's "at most once per due-state transition" guarantee hold across missed and repeated runs.
	/// </summary>
	/// <summary>
	/// Immutable evidence artifacts (registry M0169, RMS-3). There is no update path by design: a correction
	/// supersedes an artifact rather than editing it, so the checksum of what was attested to stays true.
	/// </summary>
	public interface IRmsEvidenceArtifactsRepository : IRepository<RmsEvidenceArtifact>
	{
		/// <summary>Metadata-only history across draft and signed revisions, including superseded evidence.</summary>
		Task<IEnumerable<RmsEvidenceArtifact>> GetHistoryAsync(int departmentId, string recordId, int skip, int take);
		Task<RmsEvidenceArtifact> GetByIdForDepartmentAsync(int departmentId, string artifactId);
		/// <summary><paramref name="revisionId"/> null returns the working-draft artifacts.</summary>
		Task<IEnumerable<RmsEvidenceArtifact>> GetForRecordAsync(int departmentId, string recordId, string revisionId, bool includeSuperseded);
		/// <summary>The current artifact of one kind on the draft, or null; capture supersedes it.</summary>
		Task<RmsEvidenceArtifact> GetCurrentDraftOfKindAsync(int departmentId, string recordId, RmsEvidenceKind kind, string sourceEntityId);
		/// <summary>Stamps every unbound draft artifact with the revision being written at finalize.</summary>
		Task<int> BindDraftToRevisionAsync(int departmentId, string recordId, string revisionId, DateTime utcNow, CancellationToken cancellationToken = default);
		Task<int> CountForRecordAsync(int departmentId, string recordId);
	}

	public interface IRmsRecordDueStatesRepository : IRepository<RmsRecordDueState>
	{
		Task<RmsRecordDueState> GetAsync(int departmentId, string recordId, RmsRecordObligation obligation);
		Task<IEnumerable<RmsRecordDueState>> GetForRecordAsync(int departmentId, string recordId);
		/// <summary>Open obligations (not yet cleared) for a department, oldest deadline first.</summary>
		Task<IEnumerable<RmsRecordDueState>> GetOpenForDepartmentAsync(int departmentId, int take);
		/// <summary>Count of obligations currently sitting overdue; the accountability view and dashboards read it.</summary>
		Task<int> CountOverdueAsync(int departmentId);
		Task<int> CountVisibleOverdueAsync(int departmentId, List<int> visibleGroupIds, string userId);
		Task<int> ClearForRecordAsync(int departmentId, string recordId, DateTime utcNow, CancellationToken cancellationToken = default);
	}

	/// <summary>Public-records requests (registry M0171, RMS-3); the statutory clock is what these are read by.</summary>
	public interface IRmsDisclosureRequestsRepository : IRepository<RmsDisclosureRequest>
	{
		Task<bool> TryBumpRowVersionAsync(int departmentId, string requestId, long expectedVersion, CancellationToken cancellationToken = default);
		Task<RmsDisclosureRequest> GetByIdForDepartmentAsync(int departmentId, string requestId);
		Task<IEnumerable<RmsDisclosureRequest>> GetForDepartmentAsync(int departmentId, IEnumerable<int> states, int skip, int take);
		Task<int> CountByStateAsync(int departmentId, RmsDisclosureState state);
		/// <summary>Open requests past their statutory due date; the number an officer is accountable for.</summary>
		Task<int> CountOverdueAsync(int departmentId, DateTime utcNow);
		Task<int> GetMaxRequestNumberSequenceAsync(int departmentId, string numberPrefix);
	}

	/// <summary>
	/// Immutable produced sets (registry M0171, RMS-3). A production is never edited: a supplemental release is a
	/// new production, so what was handed over stays exactly what was handed over.
	/// </summary>
	public interface IRmsDisclosureProductionsRepository : IRepository<RmsDisclosureProduction>
	{
		Task<bool> TryReleaseAsync(int departmentId, string productionId, long expectedVersion, string userId, DateTime releasedOn, string deliveryMethod, string deliveryReference, CancellationToken cancellationToken = default);
		Task<RmsDisclosureProduction> GetByIdForDepartmentAsync(int departmentId, string productionId);
		Task<IEnumerable<RmsDisclosureProduction>> GetForRequestAsync(int departmentId, string requestId);
		Task<int> GetMaxProductionNumberAsync(int departmentId, string requestId);
	}

	public interface IRmsRecordLegalHoldsRepository : IRepository<RmsRecordLegalHold>
	{
		Task<bool> TryReleaseAsync(int departmentId, string holdId, long expectedVersion, string userId, string reason, DateTime releasedOn, CancellationToken cancellationToken = default);
		Task<RmsRecordLegalHold> GetByIdForDepartmentAsync(int departmentId, string holdId);
		/// <summary>Every hold still in force for the department; the retention sweep loads them once per pass.</summary>
		Task<IEnumerable<RmsRecordLegalHold>> GetActiveForDepartmentAsync(int departmentId);
		Task<IEnumerable<RmsRecordLegalHold>> GetForRecordAsync(int departmentId, string recordId);
		Task<IEnumerable<RmsRecordLegalHold>> GetAllForDepartmentAsync(int departmentId);
	}
}
