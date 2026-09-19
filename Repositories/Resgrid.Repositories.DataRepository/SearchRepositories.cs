using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Search;
using Resgrid.Framework;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository
{
	/// <summary>Unified Search projections (plan R2.3): the safe row per entity that feeds the global index.</summary>
	public class SearchProjectionsRepository : RmsRepositoryBase<SearchProjection>, ISearchProjectionsRepository
	{
		public SearchProjectionsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		/// <summary>PostgreSQL 23505 or SQL Server 2601/2627: the only failures the insert-then-update race is allowed to absorb.</summary>
		internal static bool IsUniqueViolation(Exception ex)
		{
			if (ex is Npgsql.PostgresException postgres)
				return postgres.SqlState == "23505";
			if (ex is Microsoft.Data.SqlClient.SqlException sql)
				return sql.Number == 2601 || sql.Number == 2627;
			return false;
		}

		public Task<SearchProjection> GetAsync(int departmentId, string entityType, string entityId)
		{
			return QueryFirstOrDefaultAsync<SearchProjection>(
				$"SELECT * FROM {Tbl("SearchProjections")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("EntityType")} = {P}EntityType AND {Col("EntityId")} = {P}EntityId",
				new { DepartmentId = departmentId, EntityType = entityType, EntityId = entityId });
		}

		public async Task<SearchProjection> UpsertAsync(SearchProjection projection, CancellationToken cancellationToken = default)
		{
			if (projection == null) throw new ArgumentNullException(nameof(projection));
			if (projection.DepartmentId <= 0 || string.IsNullOrWhiteSpace(projection.EntityType) || string.IsNullOrWhiteSpace(projection.EntityId))
				throw new ArgumentException("A search projection needs a department, entity type and entity id.");

			var now = DateTime.UtcNow;
			var existing = await GetAsync(projection.DepartmentId, projection.EntityType, projection.EntityId);
			if (existing == null)
			{
				projection.SearchProjectionId = string.IsNullOrWhiteSpace(projection.SearchProjectionId) ? Guid.NewGuid().ToString() : projection.SearchProjectionId;
				projection.CreatedOn = now;
				projection.ModifiedOn = now;
				projection.RowVersion = 1;
				projection.DeletedOn = null;
				try
				{
					return await InsertAsync(projection, cancellationToken, true);
				}
				catch (Exception ex) when (IsUniqueViolation(ex))
				{
					// Two writers raced on the unique (DepartmentId, EntityType, EntityId) index; fall through to update.
					// Only that conflict is a race: a connection or permission failure propagates to the caller's guard.
					Logging.LogException(ex, $"Search projection insert conflicted for {projection.EntityType} {projection.EntityId} in department {projection.DepartmentId}; retrying as an update.");
					existing = await GetAsync(projection.DepartmentId, projection.EntityType, projection.EntityId);
					if (existing == null) throw;
				}
			}

			projection.SearchProjectionId = existing.SearchProjectionId;
			projection.CreatedOn = existing.CreatedOn;
			projection.ModifiedOn = now;
			projection.RowVersion = existing.RowVersion + 1;
			projection.DeletedOn = null;
			return await UpdateAsync(projection, cancellationToken, true);
		}

		public async Task<bool> SoftDeleteAsync(int departmentId, string entityType, string entityId, CancellationToken cancellationToken = default)
		{
			var now = DatabaseTimestamp(DateTime.UtcNow);
			var rows = await ExecuteAsync(
				$"UPDATE {Tbl("SearchProjections")} SET {Col("DeletedOn")} = {P}Now, {Col("ModifiedOn")} = {P}Now, {Col("RowVersion")} = {Col("RowVersion")} + 1 " +
				$"WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("EntityType")} = {P}EntityType AND {Col("EntityId")} = {P}EntityId AND {Col("DeletedOn")} IS NULL",
				new { Now = now, DepartmentId = departmentId, EntityType = entityType, EntityId = entityId }, cancellationToken);
			return rows > 0;
		}

		public Task<int> SoftDeleteStaleAsync(int departmentId, string entityType, DateTime notTouchedSince, CancellationToken cancellationToken = default)
		{
			var now = DatabaseTimestamp(DateTime.UtcNow);
			return ExecuteAsync(
				$"UPDATE {Tbl("SearchProjections")} SET {Col("DeletedOn")} = {P}Now, {Col("ModifiedOn")} = {P}Now, {Col("RowVersion")} = {Col("RowVersion")} + 1 " +
				$"WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("EntityType")} = {P}EntityType AND {Col("DeletedOn")} IS NULL AND {Col("ModifiedOn")} < {P}Since",
				new { Now = now, DepartmentId = departmentId, EntityType = entityType, Since = DatabaseTimestamp(notTouchedSince) }, cancellationToken);
		}

		public Task<IEnumerable<SearchProjection>> GetModifiedSinceAsync(int departmentId, DateTime? since, int take, string sinceId = null)
		{
			var parameters = new DynamicParameters();
			parameters.Add("DepartmentId", departmentId);
			parameters.Add("Skip", 0);
			parameters.Add("Take", take <= 0 ? 500 : Math.Min(take, 5000));
			var sinceClause = string.Empty;
			if (since.HasValue)
			{
				// The predicate has to match the ordering, tie-breaker included, or rows that share the cursor's
				// timestamp and sort after it are dropped on the next page.
				if (string.IsNullOrWhiteSpace(sinceId))
				{
					sinceClause = $" AND {Col("ModifiedOn")} > {P}Since";
				}
				else
				{
					sinceClause = $" AND ({Col("ModifiedOn")} > {P}Since OR ({Col("ModifiedOn")} = {P}Since AND {Col("SearchProjectionId")} > {P}SinceId))";
					parameters.Add("SinceId", sinceId);
				}

				parameters.Add("Since", DatabaseTimestamp(since.Value), System.Data.DbType.DateTime2);
			}

			return QueryAsync<SearchProjection>(
				$"SELECT * FROM {Tbl("SearchProjections")} WHERE {Col("DepartmentId")} = {P}DepartmentId{sinceClause} ORDER BY {Col("ModifiedOn")} ASC, {Col("SearchProjectionId")} ASC {Paging()}",
				parameters);
		}

		public Task<IEnumerable<SearchProjection>> GetLivePageAsync(int departmentId, int skip, int take)
		{
			var parameters = new DynamicParameters();
			parameters.Add("DepartmentId", departmentId);
			parameters.Add("Skip", Math.Max(0, skip));
			parameters.Add("Take", take <= 0 ? 500 : Math.Min(take, 5000));
			return QueryAsync<SearchProjection>(
				$"SELECT * FROM {Tbl("SearchProjections")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("DeletedOn")} IS NULL ORDER BY {Col("SearchProjectionId")} ASC {Paging()}",
				parameters);
		}

		public async Task<IEnumerable<SearchProjection>> GetByIdsAsync(int departmentId, IEnumerable<string> projectionIds)
		{
			var rows = new List<SearchProjection>();
			foreach (var ids in (projectionIds ?? Enumerable.Empty<string>()).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().Chunk(1000))
			{
				var parameters = new DynamicParameters();
				parameters.Add("DepartmentId", departmentId);
				parameters.Add("Ids", IsPostgres ? (object)ids : ids.ToList());
				rows.AddRange(await QueryAsync<SearchProjection>(
					$"SELECT * FROM {Tbl("SearchProjections")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("DeletedOn")} IS NULL AND {InList("SearchProjectionId", "Ids")}",
					parameters));
			}
			return rows;
		}

		public Task<int> HardDeleteDepartmentAsync(int departmentId, CancellationToken cancellationToken = default)
		{
			return ExecuteAsync($"DELETE FROM {Tbl("SearchProjections")} WHERE {Col("DepartmentId")} = {P}DepartmentId", new { DepartmentId = departmentId }, cancellationToken);
		}
	}

	public class SearchIndexStatesRepository : RmsRepositoryBase<SearchIndexState>, ISearchIndexStatesRepository
	{
		public SearchIndexStatesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<SearchIndexState> GetAsync(string indexName, int departmentId)
		{
			return QueryFirstOrDefaultAsync<SearchIndexState>(
				$"SELECT * FROM {Tbl("SearchIndexStates")} WHERE {Col("IndexName")} = {P}IndexName AND {Col("DepartmentId")} = {P}DepartmentId",
				new { IndexName = indexName, DepartmentId = departmentId });
		}

		public Task<IEnumerable<SearchIndexState>> GetAllForIndexAsync(string indexName)
		{
			return QueryAsync<SearchIndexState>(
				$"SELECT * FROM {Tbl("SearchIndexStates")} WHERE {Col("IndexName")} = {P}IndexName ORDER BY {Col("DepartmentId")} ASC",
				new { IndexName = indexName });
		}
	}

	/// <summary>The single-writer publish lease (plan R7 writer sequence step 2). One row per index name, compare-and-set.</summary>
	public class SearchIndexLeasesRepository : RmsRepositoryBase<SearchIndexLease>, ISearchIndexLeasesRepository
	{
		public SearchIndexLeasesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<bool> TryAcquireAsync(string indexName, string owner, TimeSpan duration, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var now = DatabaseTimestamp(utcNow);
			var until = DatabaseTimestamp(utcNow.Add(duration));
			var updated = await ExecuteAsync(
				$"UPDATE {Tbl("SearchIndexLeases")} SET {Col("LeaseOwner")} = {P}Owner, {Col("LeaseExpiresOn")} = {P}Until, {Col("ModifiedOn")} = {P}Now " +
				$"WHERE {Col("IndexName")} = {P}IndexName AND ({Col("LeaseOwner")} IS NULL OR {Col("LeaseExpiresOn")} IS NULL OR {Col("LeaseExpiresOn")} < {P}Now OR {Col("LeaseOwner")} = {P}Owner)",
				new { Owner = owner, Until = until, Now = now, IndexName = indexName }, cancellationToken);
			if (updated == 1)
				return true;

			var exists = await ScalarAsync<int>($"SELECT COUNT(1) FROM {Tbl("SearchIndexLeases")} WHERE {Col("IndexName")} = {P}IndexName", new { IndexName = indexName }, cancellationToken);
			if (exists > 0)
				return false;

			try
			{
				await ExecuteAsync(
					$"INSERT INTO {Tbl("SearchIndexLeases")} ({Cols("IndexName", "LeaseOwner", "LeaseExpiresOn", "ModifiedOn")}) VALUES ({P}IndexName, {P}Owner, {P}Until, {P}Now)",
					new { IndexName = indexName, Owner = owner, Until = until, Now = now }, cancellationToken);
				return true;
			}
			catch (Exception ex) when (SearchProjectionsRepository.IsUniqueViolation(ex))
			{
				// Lost the insert race; the other writer holds it. Any other database failure propagates so the
				// publish is reported as failed rather than as "lease held elsewhere".
				Logging.LogException(ex, $"Search index lease insert for '{indexName}' lost the race to another writer.");
				return false;
			}
		}

		public Task ReleaseAsync(string indexName, string owner, CancellationToken cancellationToken = default)
		{
			return ExecuteAsync(
				$"UPDATE {Tbl("SearchIndexLeases")} SET {Col("LeaseOwner")} = NULL, {Col("LeaseExpiresOn")} = NULL, {Col("ModifiedOn")} = {P}Now WHERE {Col("IndexName")} = {P}IndexName AND {Col("LeaseOwner")} = {P}Owner",
				new { Now = DatabaseTimestamp(DateTime.UtcNow), IndexName = indexName, Owner = owner }, cancellationToken);
		}

		public Task RecordPublishedAsync(string indexName, string owner, string revision, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			return ExecuteAsync(
				$"UPDATE {Tbl("SearchIndexLeases")} SET {Col("LastPublishedRevision")} = {P}Revision, {Col("LastPublishedOn")} = {P}Now, {Col("ModifiedOn")} = {P}Now WHERE {Col("IndexName")} = {P}IndexName AND {Col("LeaseOwner")} = {P}Owner",
				new { Revision = revision, Now = DatabaseTimestamp(utcNow), IndexName = indexName, Owner = owner }, cancellationToken);
		}
	}
}
