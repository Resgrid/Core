using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository
{
	/// <summary>Department call-number sequences (M0264): one atomic upsert per write and dialect, so concurrent calls never share a number.</summary>
	public class CallNumberSequencesRepository : RmsRepositoryBase<CallNumberSequence>, ICallNumberSequencesRepository
	{
		public CallNumberSequencesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<CallNumberSequence> GetSequenceAsync(int departmentId, string scopeKey)
		{
			return QueryFirstOrDefaultAsync<CallNumberSequence>(
				$"SELECT * FROM {Tbl("CallNumberSequences")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("ScopeKey")} = {P}ScopeKey",
				new { DepartmentId = departmentId, ScopeKey = scopeKey });
		}

		public Task<int> TakeNextAsync(int departmentId, string scopeKey, int seed, CancellationToken cancellationToken = default)
		{
			// The row stores the LAST sequence issued. Both statements insert it on first use (issuing seed + 1) and otherwise
			// advance it, returning the sequence issued, in a single atomic statement.
			var table = Tbl("CallNumberSequences");
			string sql;
			if (IsPostgres)
			{
				sql = $@"INSERT INTO {table} AS t (departmentid, scopekey, lastsequence, floorsequence, modifiedon)
					VALUES ({P}DepartmentId, {P}ScopeKey, {P}Seed + 1, 0, {P}Now)
					ON CONFLICT (departmentid, scopekey) DO UPDATE SET lastsequence = t.lastsequence + 1, modifiedon = EXCLUDED.modifiedon
					RETURNING lastsequence";
			}
			else
			{
				sql = $@"MERGE {table} WITH (HOLDLOCK) AS t
					USING (SELECT {P}DepartmentId AS DepartmentId, {P}ScopeKey AS ScopeKey) AS s ON t.[DepartmentId] = s.DepartmentId AND t.[ScopeKey] = s.ScopeKey
					WHEN MATCHED THEN UPDATE SET [LastSequence] = t.[LastSequence] + 1, [ModifiedOn] = {P}Now
					WHEN NOT MATCHED THEN INSERT ([DepartmentId], [ScopeKey], [LastSequence], [FloorSequence], [ModifiedOn]) VALUES (s.DepartmentId, s.ScopeKey, {P}Seed + 1, 0, {P}Now)
					OUTPUT inserted.[LastSequence];";
			}

			return ScalarAsync<int>(sql, new { DepartmentId = departmentId, ScopeKey = scopeKey, Seed = Math.Max(0, seed), Now = DatabaseTimestamp(DateTime.UtcNow) }, cancellationToken);
		}

		public Task RaiseFloorAsync(int departmentId, string scopeKey, int nextSequence, string userId, DateTime now, CancellationToken cancellationToken = default)
		{
			var table = Tbl("CallNumberSequences");
			string sql;
			if (IsPostgres)
			{
				sql = $@"INSERT INTO {table} AS t (departmentid, scopekey, lastsequence, floorsequence, floorseton, floorsetbyuserid, modifiedon)
					VALUES ({P}DepartmentId, {P}ScopeKey, {P}Next - 1, {P}Next, {P}Now, {P}UserId, {P}Now)
					ON CONFLICT (departmentid, scopekey) DO UPDATE SET lastsequence = GREATEST(t.lastsequence, EXCLUDED.lastsequence),
						floorsequence = GREATEST(t.floorsequence, EXCLUDED.floorsequence), floorseton = EXCLUDED.floorseton,
						floorsetbyuserid = EXCLUDED.floorsetbyuserid, modifiedon = EXCLUDED.modifiedon";
			}
			else
			{
				sql = $@"MERGE {table} WITH (HOLDLOCK) AS t
					USING (SELECT {P}DepartmentId AS DepartmentId, {P}ScopeKey AS ScopeKey) AS s ON t.[DepartmentId] = s.DepartmentId AND t.[ScopeKey] = s.ScopeKey
					WHEN MATCHED THEN UPDATE SET
						[LastSequence] = CASE WHEN t.[LastSequence] > {P}Next - 1 THEN t.[LastSequence] ELSE {P}Next - 1 END,
						[FloorSequence] = CASE WHEN t.[FloorSequence] > {P}Next THEN t.[FloorSequence] ELSE {P}Next END,
						[FloorSetOn] = {P}Now, [FloorSetByUserId] = {P}UserId, [ModifiedOn] = {P}Now
					WHEN NOT MATCHED THEN INSERT ([DepartmentId], [ScopeKey], [LastSequence], [FloorSequence], [FloorSetOn], [FloorSetByUserId], [ModifiedOn])
						VALUES (s.DepartmentId, s.ScopeKey, {P}Next - 1, {P}Next, {P}Now, {P}UserId, {P}Now);";
			}

			return ExecuteAsync(sql, new { DepartmentId = departmentId, ScopeKey = scopeKey, Next = Math.Max(1, nextSequence), UserId = userId, Now = DatabaseTimestamp(now) }, cancellationToken);
		}

		public Task<int> RaiseLastSequenceAsync(int departmentId, string scopeKey, int lastSequence, CancellationToken cancellationToken = default)
		{
			var table = Tbl("CallNumberSequences");
			string sql;
			if (IsPostgres)
			{
				sql = $@"INSERT INTO {table} AS t (departmentid, scopekey, lastsequence, floorsequence, modifiedon)
					VALUES ({P}DepartmentId, {P}ScopeKey, {P}Last, 0, {P}Now)
					ON CONFLICT (departmentid, scopekey) DO UPDATE SET lastsequence = GREATEST(t.lastsequence, EXCLUDED.lastsequence), modifiedon = EXCLUDED.modifiedon
					RETURNING lastsequence";
			}
			else
			{
				sql = $@"MERGE {table} WITH (HOLDLOCK) AS t
					USING (SELECT {P}DepartmentId AS DepartmentId, {P}ScopeKey AS ScopeKey) AS s ON t.[DepartmentId] = s.DepartmentId AND t.[ScopeKey] = s.ScopeKey
					WHEN MATCHED THEN UPDATE SET [LastSequence] = CASE WHEN t.[LastSequence] > {P}Last THEN t.[LastSequence] ELSE {P}Last END, [ModifiedOn] = {P}Now
					WHEN NOT MATCHED THEN INSERT ([DepartmentId], [ScopeKey], [LastSequence], [FloorSequence], [ModifiedOn]) VALUES (s.DepartmentId, s.ScopeKey, {P}Last, 0, {P}Now)
					OUTPUT inserted.[LastSequence];";
			}

			return ScalarAsync<int>(sql, new { DepartmentId = departmentId, ScopeKey = scopeKey, Last = Math.Max(0, lastSequence), Now = DatabaseTimestamp(DateTime.UtcNow) }, cancellationToken);
		}

		public async Task<bool> TrySetLastSequenceAsync(int departmentId, string scopeKey, int lastSequence, int expectedLastSequence, CancellationToken cancellationToken = default)
		{
			var sql = $"UPDATE {Tbl("CallNumberSequences")} SET {Col("LastSequence")} = {P}Last, {Col("ModifiedOn")} = {P}Now " +
				$"WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("ScopeKey")} = {P}ScopeKey AND {Col("LastSequence")} = {P}Expected";

			return await ExecuteAsync(sql, new { DepartmentId = departmentId, ScopeKey = scopeKey, Last = Math.Max(0, lastSequence), Expected = expectedLastSequence, Now = DatabaseTimestamp(DateTime.UtcNow) }, cancellationToken) == 1;
		}

		public async Task<List<string>> GetDeletedCallNumbersAsync(int departmentId, DateTime fromUtc, DateTime toUtc)
		{
			var sql = $"SELECT c.{Col("Number")} FROM {Tbl("Calls")} c WHERE c.{Col("DepartmentId")} = {P}DepartmentId AND c.{Col("IsDeleted")} = {P}Deleted " +
				$"AND c.{Col("Number")} IS NOT NULL AND c.{Col("LoggedOn")} >= {P}FromUtc AND c.{Col("LoggedOn")} < {P}ToUtc";

			return (await QueryAsync<string>(sql, new { DepartmentId = departmentId, Deleted = true, FromUtc = DatabaseTimestamp(fromUtc), ToUtc = DatabaseTimestamp(toUtc) })).ToList();
		}

		public async Task<List<string>> GetCallNumbersAsync(int departmentId, DateTime fromUtc, DateTime toUtc)
		{
			var sql = $"SELECT c.{Col("Number")} FROM {Tbl("Calls")} c WHERE c.{Col("DepartmentId")} = {P}DepartmentId " +
				$"AND c.{Col("Number")} IS NOT NULL AND c.{Col("LoggedOn")} >= {P}FromUtc AND c.{Col("LoggedOn")} < {P}ToUtc";

			return (await QueryAsync<string>(sql, new { DepartmentId = departmentId, FromUtc = DatabaseTimestamp(fromUtc), ToUtc = DatabaseTimestamp(toUtc) })).ToList();
		}

		public async Task<int> GetHighestIssuedAsync(int departmentId, string numberPrefix, string numberSuffix, DateTime? fromUtc, DateTime? toUtc)
		{
			numberPrefix ??= string.Empty;
			numberSuffix ??= string.Empty;
			// Bounded by the call's logged time where the pattern has a period, so the IX_Calls_DepartmentId_LoggedOn range does the work.
			var range = fromUtc.HasValue && toUtc.HasValue ? $" AND c.{Col("LoggedOn")} >= {P}FromUtc AND c.{Col("LoggedOn")} < {P}ToUtc" : string.Empty;
			var numbers = $"SELECT c.{Col("Number")} AS n FROM {Tbl("Calls")} c WHERE c.{Col("DepartmentId")} = {P}DepartmentId AND c.{Col("Number")} LIKE {P}Pattern ESCAPE '!'{range}";
			// The CASE keeps SUBSTRING from seeing a negative length when the prefix and suffix overlap in a short number.
			var sql = IsPostgres
				? $"SELECT MAX(CAST(x.mid AS BIGINT)) FROM (SELECT CASE WHEN CHAR_LENGTH(r.n) > {P}PrefixLength + {P}SuffixLength THEN SUBSTRING(r.n FROM {P}PrefixLength + 1 FOR CHAR_LENGTH(r.n) - {P}PrefixLength - {P}SuffixLength) END AS mid FROM ({numbers}) r) x WHERE x.mid ~ '^[0-9]{{1,18}}$'"
				: $"SELECT MAX(TRY_CAST(x.mid AS BIGINT)) FROM (SELECT CASE WHEN LEN(r.n) > {P}PrefixLength + {P}SuffixLength THEN SUBSTRING(r.n, {P}PrefixLength + 1, LEN(r.n) - {P}PrefixLength - {P}SuffixLength) END AS mid FROM ({numbers}) r) x WHERE LEN(x.mid) BETWEEN 1 AND 18 AND x.mid NOT LIKE '%[^0-9]%'";
			var max = await ScalarAsync<long?>(sql, new
			{
				DepartmentId = departmentId,
				Pattern = Queries.Calls.SearchCallsQuery.EscapeLike(numberPrefix) + "%" + Queries.Calls.SearchCallsQuery.EscapeLike(numberSuffix),
				PrefixLength = numberPrefix.Length,
				SuffixLength = numberSuffix.Length,
				FromUtc = fromUtc.HasValue ? DatabaseTimestamp(fromUtc.Value) : (DateTime?)null,
				ToUtc = toUtc.HasValue ? DatabaseTimestamp(toUtc.Value) : (DateTime?)null
			});

			return (int)Math.Min(Math.Max(max ?? 0, 0), int.MaxValue - 1);
		}
	}
}
