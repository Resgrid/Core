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
		Task<int> CountCreatedSinceAsync(int departmentId, DateTime sinceUtc);
		Task<int> CountFinalizedSinceAsync(int departmentId, DateTime sinceUtc);
		/// <summary>Every live Record in the department, any state.</summary>
		Task<int> CountAllAsync(int departmentId);
		/// <summary>Live Records in Draft, ReadyForReview, Returned or Approved (accountability view, plan 4.7).</summary>
		Task<IEnumerable<RmsOperationalRecord>> GetOpenAsync(int departmentId);
		/// <summary>Live Finalized/Amended Records whose FinalizedOn is at or after the instant.</summary>
		Task<IEnumerable<RmsOperationalRecord>> GetFinalizedSinceAsync(int departmentId, DateTime sinceUtc);
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
		/// <summary>Metadata only: never loads Data.</summary>
		Task<IEnumerable<RmsRecordAttachment>> GetMetadataForRecordAsync(int departmentId, string recordId);
		/// <summary>Loads Data; authorized per Record by the caller on every request.</summary>
		Task<RmsRecordAttachment> GetByIdForDepartmentAsync(int departmentId, string attachmentId);
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
		Task<IEnumerable<RmsRecordSearchProjection>> GetModifiedSinceAsync(int departmentId, DateTime? since, int take);
		/// <summary>Live projections by record id, for post-retrieval loading of search hits.</summary>
		Task<IEnumerable<RmsRecordSearchProjection>> GetByIdsAsync(int departmentId, IEnumerable<string> recordIds);
	}

	public interface IRmsSearchIndexStatesRepository : IRepository<RmsSearchIndexState>
	{
		Task<RmsSearchIndexState> GetAsync(int departmentId, string indexName);
	}

	public interface IRmsRecordGroupScopesRepository : IRepository<RmsRecordGroupScope>
	{
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
}
