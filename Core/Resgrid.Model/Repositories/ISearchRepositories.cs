using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Search;

namespace Resgrid.Model.Repositories
{
	public interface ISearchProjectionsRepository : IRepository<SearchProjection>
	{
		Task<SearchProjection> GetAsync(int departmentId, string entityType, string entityId);

		/// <summary>Insert or update by (DepartmentId, EntityType, EntityId); bumps RowVersion and ModifiedOn, clears DeletedOn.</summary>
		Task<SearchProjection> UpsertAsync(SearchProjection projection, CancellationToken cancellationToken = default);

		/// <summary>Soft-deletes one row; returns false when it did not exist.</summary>
		Task<bool> SoftDeleteAsync(int departmentId, string entityType, string entityId, CancellationToken cancellationToken = default);

		/// <summary>Soft-deletes live rows of one family that were not touched since <paramref name="notTouchedSince"/> (rebuild reconciliation).</summary>
		Task<int> SoftDeleteStaleAsync(int departmentId, string entityType, DateTime notTouchedSince, CancellationToken cancellationToken = default);

		/// <summary>Rows (including soft-deleted) after the (ModifiedOn, id) cursor, oldest first — the catch-up feed.</summary>
		Task<IEnumerable<SearchProjection>> GetModifiedSinceAsync(int departmentId, DateTime? since, int take, string sinceId = null);

		/// <summary>Live rows for a department, paged, for a full index rebuild.</summary>
		Task<IEnumerable<SearchProjection>> GetLivePageAsync(int departmentId, int skip, int take);

		/// <summary>Live rows by projection id, for post-retrieval loading of hits.</summary>
		Task<IEnumerable<SearchProjection>> GetByIdsAsync(int departmentId, IEnumerable<string> projectionIds);

		/// <summary>Live rows of one family by entity id, for hits found outside the index (street address matching).</summary>
		Task<IEnumerable<SearchProjection>> GetByEntityIdsAsync(int departmentId, string entityType, IEnumerable<string> entityIds);

		/// <summary>
		/// One keyset page of the call history backfill (M0261): the department's non-deleted calls with a CallId below
		/// <paramref name="beforeCallId"/> (every call when null), newest CallId first.
		/// </summary>
		Task<IEnumerable<Call>> GetCallsForBackfillAsync(int departmentId, int? beforeCallId, int take);

		Task<int> HardDeleteDepartmentAsync(int departmentId, CancellationToken cancellationToken = default);
	}

	public interface ISearchIndexStatesRepository : IRepository<SearchIndexState>
	{
		Task<SearchIndexState> GetAsync(string indexName, int departmentId);

		Task<IEnumerable<SearchIndexState>> GetAllForIndexAsync(string indexName);

		/// <summary>
		/// Inserts <paramref name="state"/> unless a row for its (IndexName, DepartmentId) already exists, in one statement,
		/// so concurrent callers never trip the unique index. Returns false, and leaves the existing row untouched, when
		/// another writer got there first.
		/// </summary>
		Task<bool> InsertIfMissingAsync(SearchIndexState state, CancellationToken cancellationToken = default);
	}

	public interface ISearchIndexLeasesRepository : IRepository<SearchIndexLease>
	{
		/// <summary>Takes or renews the lease when it is free, expired, or already held by <paramref name="owner"/>.</summary>
		Task<bool> TryAcquireAsync(string indexName, string owner, TimeSpan duration, DateTime utcNow, CancellationToken cancellationToken = default);

		Task ReleaseAsync(string indexName, string owner, CancellationToken cancellationToken = default);

		Task RecordPublishedAsync(string indexName, string owner, string revision, DateTime utcNow, CancellationToken cancellationToken = default);
	}
}
