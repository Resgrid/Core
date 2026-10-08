using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository
{
	/// <summary>Department document-number sequences (M0268): one atomic upsert per write and dialect, so two documents never share a number.</summary>
	public class DocumentNumberSequencesRepository : RmsRepositoryBase<DocumentNumberSequence>, IDocumentNumberSequencesRepository
	{
		/// <summary>Where each document kind keeps its issued numbers, as table and text column.</summary>
		private static readonly Dictionary<string, (string Table, string Column)> NumberColumns = new Dictionary<string, (string, string)>(StringComparer.Ordinal)
		{
			[DocumentNumberKinds.WorkOrder] = ("WorkOrders", "DisplayNumber"),
			[DocumentNumberKinds.Invoice] = ("Invoices", "DisplayNumber"),
			[DocumentNumberKinds.Bid] = ("Bids", "DisplayNumber"),
			[DocumentNumberKinds.TimeReport] = ("DeploymentTimeReports", "DisplayNumber"),
			[DocumentNumberKinds.RecordsRequest] = ("RmsDisclosureRequests", "RequestNumber"),
			[DocumentNumberKinds.Occupancy] = ("RmsOccupancies", "OccupancyNumber"),
			[DocumentNumberKinds.Inspection] = ("RmsInspections", "InspectionNumber"),
			[DocumentNumberKinds.Permit] = ("RmsPermits", "PermitNumber"),
			[DocumentNumberKinds.Investigation] = ("RmsInvestigationCases", "CaseNumber"),
			[DocumentNumberKinds.Evidence] = ("RmsInvestigationEvidence", "EvidenceNumber")
		};

		public DocumentNumberSequencesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<DocumentNumberSequence> GetSequenceAsync(int departmentId, string kind, string scopeKey)
		{
			return QueryFirstOrDefaultAsync<DocumentNumberSequence>(
				$"SELECT * FROM {Tbl("DocumentNumberSequences")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("Kind")} = {P}Kind AND {Col("ScopeKey")} = {P}ScopeKey",
				new { DepartmentId = departmentId, Kind = kind, ScopeKey = scopeKey });
		}

		public Task<int> TakeNextAsync(int departmentId, string kind, string scopeKey, int seed, CancellationToken cancellationToken = default)
		{
			// The row stores the LAST sequence issued. Both statements insert it on first use (issuing seed + 1) and otherwise
			// advance it, returning the sequence issued, in a single atomic statement.
			var table = Tbl("DocumentNumberSequences");
			string sql;
			if (IsPostgres)
			{
				sql = $@"INSERT INTO {table} AS t (departmentid, kind, scopekey, lastsequence, floorsequence, modifiedon)
					VALUES ({P}DepartmentId, {P}Kind, {P}ScopeKey, {P}Seed + 1, 0, {P}Now)
					ON CONFLICT (departmentid, kind, scopekey) DO UPDATE SET lastsequence = t.lastsequence + 1, modifiedon = EXCLUDED.modifiedon
					RETURNING lastsequence";
			}
			else
			{
				sql = $@"MERGE {table} WITH (HOLDLOCK) AS t
					USING (SELECT {P}DepartmentId AS DepartmentId, {P}Kind AS Kind, {P}ScopeKey AS ScopeKey) AS s
						ON t.[DepartmentId] = s.DepartmentId AND t.[Kind] = s.Kind AND t.[ScopeKey] = s.ScopeKey
					WHEN MATCHED THEN UPDATE SET [LastSequence] = t.[LastSequence] + 1, [ModifiedOn] = {P}Now
					WHEN NOT MATCHED THEN INSERT ([DepartmentId], [Kind], [ScopeKey], [LastSequence], [FloorSequence], [ModifiedOn]) VALUES (s.DepartmentId, s.Kind, s.ScopeKey, {P}Seed + 1, 0, {P}Now)
					OUTPUT inserted.[LastSequence];";
			}

			return ScalarAsync<int>(sql, new { DepartmentId = departmentId, Kind = kind, ScopeKey = scopeKey, Seed = Math.Max(0, seed), Now = DatabaseTimestamp(DateTime.UtcNow) }, cancellationToken);
		}

		public Task RaiseFloorAsync(int departmentId, string kind, string scopeKey, int nextSequence, string userId, DateTime now, CancellationToken cancellationToken = default)
		{
			var table = Tbl("DocumentNumberSequences");
			string sql;
			if (IsPostgres)
			{
				sql = $@"INSERT INTO {table} AS t (departmentid, kind, scopekey, lastsequence, floorsequence, floorseton, floorsetbyuserid, modifiedon)
					VALUES ({P}DepartmentId, {P}Kind, {P}ScopeKey, {P}Next - 1, {P}Next, {P}Now, {P}UserId, {P}Now)
					ON CONFLICT (departmentid, kind, scopekey) DO UPDATE SET lastsequence = GREATEST(t.lastsequence, EXCLUDED.lastsequence),
						floorsequence = GREATEST(t.floorsequence, EXCLUDED.floorsequence), floorseton = EXCLUDED.floorseton,
						floorsetbyuserid = EXCLUDED.floorsetbyuserid, modifiedon = EXCLUDED.modifiedon";
			}
			else
			{
				sql = $@"MERGE {table} WITH (HOLDLOCK) AS t
					USING (SELECT {P}DepartmentId AS DepartmentId, {P}Kind AS Kind, {P}ScopeKey AS ScopeKey) AS s
						ON t.[DepartmentId] = s.DepartmentId AND t.[Kind] = s.Kind AND t.[ScopeKey] = s.ScopeKey
					WHEN MATCHED THEN UPDATE SET
						[LastSequence] = CASE WHEN t.[LastSequence] > {P}Next - 1 THEN t.[LastSequence] ELSE {P}Next - 1 END,
						[FloorSequence] = CASE WHEN t.[FloorSequence] > {P}Next THEN t.[FloorSequence] ELSE {P}Next END,
						[FloorSetOn] = {P}Now, [FloorSetByUserId] = {P}UserId, [ModifiedOn] = {P}Now
					WHEN NOT MATCHED THEN INSERT ([DepartmentId], [Kind], [ScopeKey], [LastSequence], [FloorSequence], [FloorSetOn], [FloorSetByUserId], [ModifiedOn])
						VALUES (s.DepartmentId, s.Kind, s.ScopeKey, {P}Next - 1, {P}Next, {P}Now, {P}UserId, {P}Now);";
			}

			return ExecuteAsync(sql, new { DepartmentId = departmentId, Kind = kind, ScopeKey = scopeKey, Next = Math.Max(1, nextSequence), UserId = userId, Now = DatabaseTimestamp(now) }, cancellationToken);
		}

		public async Task<int> GetHighestIssuedAsync(int departmentId, string kind, string numberPrefix, string numberSuffix)
		{
			if (kind == null || !NumberColumns.TryGetValue(kind, out var source))
				return 0;

			numberPrefix ??= string.Empty;
			numberSuffix ??= string.Empty;
			var numbers = $"SELECT x.{Col(source.Column)} AS n FROM {Tbl(source.Table)} x WHERE x.{Col("DepartmentId")} = {P}DepartmentId AND x.{Col(source.Column)} LIKE {P}Pattern ESCAPE '!'";
			// The CASE keeps SUBSTRING from seeing a negative length when the prefix and suffix overlap in a short number.
			var sql = IsPostgres
				? $"SELECT MAX(CAST(y.mid AS BIGINT)) FROM (SELECT CASE WHEN CHAR_LENGTH(r.n) > {P}PrefixLength + {P}SuffixLength THEN SUBSTRING(r.n FROM {P}PrefixLength + 1 FOR CHAR_LENGTH(r.n) - {P}PrefixLength - {P}SuffixLength) END AS mid FROM ({numbers}) r) y WHERE y.mid ~ '^[0-9]{{1,18}}$'"
				: $"SELECT MAX(TRY_CAST(y.mid AS BIGINT)) FROM (SELECT CASE WHEN LEN(r.n) > {P}PrefixLength + {P}SuffixLength THEN SUBSTRING(r.n, {P}PrefixLength + 1, LEN(r.n) - {P}PrefixLength - {P}SuffixLength) END AS mid FROM ({numbers}) r) y WHERE LEN(y.mid) BETWEEN 1 AND 18 AND y.mid NOT LIKE '%[^0-9]%'";
			var max = await ScalarAsync<long?>(sql, new
			{
				DepartmentId = departmentId,
				Pattern = Queries.Calls.SearchCallsQuery.EscapeLike(numberPrefix) + "%" + Queries.Calls.SearchCallsQuery.EscapeLike(numberSuffix),
				PrefixLength = numberPrefix.Length,
				SuffixLength = numberSuffix.Length
			});

			return (int)Math.Min(Math.Max(max ?? 0, 0), int.MaxValue - 1);
		}
	}
}
