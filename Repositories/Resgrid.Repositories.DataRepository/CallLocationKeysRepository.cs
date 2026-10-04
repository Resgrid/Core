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
	public class CallLocationKeysRepository : RmsRepositoryBase<CallLocationKey>, ICallLocationKeysRepository
	{
		public CallLocationKeysRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		private static string False => IsPostgres ? "FALSE" : "0";
		private static string True => IsPostgres ? "TRUE" : "1";

		/// <summary>"TOP (@Take)" after SELECT on SQL Server; nothing on PostgreSQL (see <see cref="Limit"/>).</summary>
		private static string Top => IsPostgres ? string.Empty : $"TOP (@Take) ";

		private static string Limit => IsPostgres ? " LIMIT @Take" : string.Empty;

		private string CandidateColumns(string keyAlias, string loggedOn) =>
			$"{keyAlias}.{Col("CallId")} AS CallId, {loggedOn} AS LoggedOn, {keyAlias}.{Col("AddressKey")} AS AddressKey, " +
			$"{keyAlias}.{Col("AddressCanonical")} AS AddressCanonical, {keyAlias}.{Col("Latitude")} AS Latitude, {keyAlias}.{Col("Longitude")} AS Longitude";

		public async Task UpsertAsync(IEnumerable<CallLocationKey> keys, CancellationToken cancellationToken = default)
		{
			var rows = (keys ?? Enumerable.Empty<CallLocationKey>()).Select(k => new
			{
				k.CallId,
				k.DepartmentId,
				k.AddressKey,
				k.AddressCanonical,
				k.Latitude,
				k.Longitude,
				LoggedOn = DatabaseTimestamp(k.LoggedOn),
				k.KeyVersion,
				IndexedOn = DatabaseTimestamp(k.IndexedOn)
			}).ToList();

			if (rows.Count == 0)
				return;

			var table = Tbl("CallLocationKeys");
			string sql;
			if (IsPostgres)
			{
				sql = $@"INSERT INTO {table} (callid, departmentid, addresskey, addresscanonical, latitude, longitude, loggedon, keyversion, indexedon)
					VALUES (@CallId, @DepartmentId, @AddressKey, @AddressCanonical, @Latitude, @Longitude, @LoggedOn, @KeyVersion, @IndexedOn)
					ON CONFLICT (callid) DO UPDATE SET departmentid = EXCLUDED.departmentid, addresskey = EXCLUDED.addresskey,
						addresscanonical = EXCLUDED.addresscanonical, latitude = EXCLUDED.latitude, longitude = EXCLUDED.longitude,
						loggedon = EXCLUDED.loggedon, keyversion = EXCLUDED.keyversion, indexedon = EXCLUDED.indexedon";
			}
			else
			{
				sql = $@"MERGE {table} WITH (HOLDLOCK) AS t
					USING (SELECT @CallId AS CallId) AS s ON t.[CallId] = s.CallId
					WHEN MATCHED THEN UPDATE SET [DepartmentId] = @DepartmentId, [AddressKey] = @AddressKey, [AddressCanonical] = @AddressCanonical,
						[Latitude] = @Latitude, [Longitude] = @Longitude, [LoggedOn] = @LoggedOn, [KeyVersion] = @KeyVersion, [IndexedOn] = @IndexedOn
					WHEN NOT MATCHED THEN INSERT ([CallId], [DepartmentId], [AddressKey], [AddressCanonical], [Latitude], [Longitude], [LoggedOn], [KeyVersion], [IndexedOn])
						VALUES (@CallId, @DepartmentId, @AddressKey, @AddressCanonical, @Latitude, @Longitude, @LoggedOn, @KeyVersion, @IndexedOn);";
			}

			await ExecuteAsync(sql, rows, cancellationToken);
		}

		public async Task DeleteForCallAsync(int callId, CancellationToken cancellationToken = default)
		{
			await ExecuteAsync($"DELETE FROM {Tbl("CallLocationKeys")} WHERE {Col("CallId")} = {P}CallId", new { CallId = callId }, cancellationToken);
		}

		public Task<int> DeleteForDepartmentAsync(int departmentId, CancellationToken cancellationToken = default) =>
			ExecuteAsync($"DELETE FROM {Tbl("CallLocationKeys")} WHERE {Col("DepartmentId")} = {P}DepartmentId", new { DepartmentId = departmentId }, cancellationToken);

		public async Task<List<CallLocationCandidate>> GetByAddressKeyAsync(int departmentId, string addressKey, int take)
		{
			var sql = $@"SELECT {Top}{CandidateColumns("k", "k." + Col("LoggedOn"))}, {True} AS Indexed
				FROM {Tbl("CallLocationKeys")} k
				INNER JOIN {Tbl("Calls")} c ON c.{Col("CallId")} = k.{Col("CallId")} AND c.{Col("IsDeleted")} = {False}
				WHERE k.{Col("DepartmentId")} = {P}DepartmentId AND k.{Col("AddressKey")} = {P}AddressKey
				ORDER BY k.{Col("LoggedOn")} DESC{Limit}";

			return (await QueryAsync<CallLocationCandidate>(sql, new { DepartmentId = departmentId, AddressKey = addressKey, Take = take })).ToList();
		}

		public async Task<List<CallLocationCandidate>> GetWithinBoundsAsync(int departmentId, decimal minLatitude, decimal maxLatitude, decimal minLongitude, decimal maxLongitude, int take)
		{
			var sql = $@"SELECT {Top}{CandidateColumns("k", "k." + Col("LoggedOn"))}, {True} AS Indexed
				FROM {Tbl("CallLocationKeys")} k
				INNER JOIN {Tbl("Calls")} c ON c.{Col("CallId")} = k.{Col("CallId")} AND c.{Col("IsDeleted")} = {False}
				WHERE k.{Col("DepartmentId")} = {P}DepartmentId
					AND k.{Col("Latitude")} BETWEEN {P}MinLatitude AND {P}MaxLatitude
					AND k.{Col("Longitude")} BETWEEN {P}MinLongitude AND {P}MaxLongitude
				ORDER BY k.{Col("LoggedOn")} DESC{Limit}";

			return (await QueryAsync<CallLocationCandidate>(sql, new
			{
				DepartmentId = departmentId,
				MinLatitude = minLatitude,
				MaxLatitude = maxLatitude,
				MinLongitude = minLongitude,
				MaxLongitude = maxLongitude,
				Take = take
			})).ToList();
		}

		public async Task<List<CallLocationCandidate>> GetContactCallCandidatesAsync(int departmentId, IEnumerable<string> contactIds, int take)
		{
			var ids = InListValue(contactIds?.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct());
			if (ids.Length == 0)
				return new List<CallLocationCandidate>();

			var indexed = IsPostgres ? $"(k.{Col("CallId")} IS NOT NULL)" : $"CAST(CASE WHEN k.[CallId] IS NULL THEN 0 ELSE 1 END AS bit)";
			var sql = $@"SELECT {Top}c.{Col("CallId")} AS CallId, c.{Col("LoggedOn")} AS LoggedOn, k.{Col("AddressKey")} AS AddressKey,
					k.{Col("AddressCanonical")} AS AddressCanonical, k.{Col("Latitude")} AS Latitude, k.{Col("Longitude")} AS Longitude, {indexed} AS Indexed
				FROM (SELECT DISTINCT cc.{Col("CallId")} AS CallId FROM {Tbl("CallContacts")} cc
					WHERE cc.{Col("DepartmentId")} = {P}DepartmentId AND {InList("ContactId", "ContactIds", "cc")}) x
				INNER JOIN {Tbl("Calls")} c ON c.{Col("CallId")} = x.CallId AND c.{Col("DepartmentId")} = {P}DepartmentId AND c.{Col("IsDeleted")} = {False}
				LEFT JOIN {Tbl("CallLocationKeys")} k ON k.{Col("CallId")} = c.{Col("CallId")}
				ORDER BY c.{Col("LoggedOn")} DESC{Limit}";

			return (await QueryAsync<CallLocationCandidate>(sql, new { DepartmentId = departmentId, ContactIds = ids, Take = take })).ToList();
		}

		public async Task<Dictionary<string, int>> GetCallCountsByContactAsync(int departmentId)
		{
			var count = IsPostgres ? $"COUNT(DISTINCT cc.{Col("CallId")})::int" : $"COUNT(DISTINCT cc.[CallId])";
			var sql = $@"SELECT cc.{Col("ContactId")} AS ContactId, {count} AS CallCount
				FROM {Tbl("CallContacts")} cc
				INNER JOIN {Tbl("Calls")} c ON c.{Col("CallId")} = cc.{Col("CallId")} AND c.{Col("IsDeleted")} = {False}
				WHERE cc.{Col("DepartmentId")} = {P}DepartmentId
				GROUP BY cc.{Col("ContactId")}";

			var rows = await QueryAsync<ContactCallCountRow>(sql, new { DepartmentId = departmentId });
			var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
			foreach (var row in rows.Where(r => r.ContactId != null))
				result[row.ContactId] = row.CallCount;
			return result;
		}

		public async Task<List<Call>> GetCallsAsync(int departmentId, IEnumerable<int> callIds)
		{
			var ids = InListValue(callIds?.Distinct());
			if (ids.Length == 0)
				return new List<Call>();

			var sql = $"SELECT * FROM {Tbl("Calls")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {InList("CallId", "CallIds")} AND {Col("IsDeleted")} = {False}";
			return (await QueryAsync<Call>(sql, new { DepartmentId = departmentId, CallIds = ids })).ToList();
		}

		public async Task<List<CallNote>> GetNotesForCallsAsync(IEnumerable<int> callIds)
		{
			var ids = InListValue(callIds?.Distinct());
			if (ids.Length == 0)
				return new List<CallNote>();

			var sql = $"SELECT * FROM {Tbl("CallNotes")} WHERE {InList("CallId", "CallIds")} AND {Col("IsDeleted")} = {False} ORDER BY {Col("Timestamp")}";
			return (await QueryAsync<CallNote>(sql, new { CallIds = ids })).ToList();
		}

		public Task<CallLocationIndexState> GetStateAsync(int departmentId) =>
			QueryFirstOrDefaultAsync<CallLocationIndexState>($"SELECT * FROM {Tbl("CallLocationIndexStates")} WHERE {Col("DepartmentId")} = {P}DepartmentId", new { DepartmentId = departmentId });

		public async Task SaveStateAsync(CallLocationIndexState state, CancellationToken cancellationToken = default)
		{
			var table = Tbl("CallLocationIndexStates");
			var row = new
			{
				state.DepartmentId,
				state.KeyVersion,
				state.NextCallId,
				CompletedOn = state.CompletedOn.HasValue ? DatabaseTimestamp(state.CompletedOn.Value) : (DateTime?)null,
				state.IsSuppressed,
				ModifiedOn = DatabaseTimestamp(state.ModifiedOn)
			};

			string sql;
			if (IsPostgres)
			{
				sql = $@"INSERT INTO {table} (departmentid, keyversion, nextcallid, completedon, issuppressed, modifiedon)
					VALUES (@DepartmentId, @KeyVersion, @NextCallId, @CompletedOn, @IsSuppressed, @ModifiedOn)
					ON CONFLICT (departmentid) DO UPDATE SET keyversion = EXCLUDED.keyversion, nextcallid = EXCLUDED.nextcallid,
						completedon = EXCLUDED.completedon, issuppressed = EXCLUDED.issuppressed, modifiedon = EXCLUDED.modifiedon";
			}
			else
			{
				sql = $@"MERGE {table} WITH (HOLDLOCK) AS t
					USING (SELECT @DepartmentId AS DepartmentId) AS s ON t.[DepartmentId] = s.DepartmentId
					WHEN MATCHED THEN UPDATE SET [KeyVersion] = @KeyVersion, [NextCallId] = @NextCallId, [CompletedOn] = @CompletedOn,
						[IsSuppressed] = @IsSuppressed, [ModifiedOn] = @ModifiedOn
					WHEN NOT MATCHED THEN INSERT ([DepartmentId], [KeyVersion], [NextCallId], [CompletedOn], [IsSuppressed], [ModifiedOn])
						VALUES (@DepartmentId, @KeyVersion, @NextCallId, @CompletedOn, @IsSuppressed, @ModifiedOn);";
			}

			await ExecuteAsync(sql, row, cancellationToken);
		}

		public async Task<List<int>> GetDepartmentsNeedingIndexAsync(int keyVersion, int take)
		{
			// Departments already under way finish before new ones start.
			var sql = $@"SELECT {Top}d.{Col("DepartmentId")}
				FROM {Tbl("Departments")} d
				LEFT JOIN {Tbl("CallLocationIndexStates")} s ON s.{Col("DepartmentId")} = d.{Col("DepartmentId")}
				WHERE s.{Col("DepartmentId")} IS NULL
					OR (s.{Col("IsSuppressed")} = {False} AND (s.{Col("CompletedOn")} IS NULL OR s.{Col("KeyVersion")} < {P}KeyVersion))
				ORDER BY CASE WHEN s.{Col("DepartmentId")} IS NULL THEN 1 ELSE 0 END, d.{Col("DepartmentId")}{Limit}";

			return (await QueryAsync<int>(sql, new { KeyVersion = keyVersion, Take = take })).ToList();
		}

		public async Task<List<int>> GetDepartmentsToSuppressAsync(int take)
		{
			var sql = $@"SELECT {Top}s.{Col("DepartmentId")}
				FROM {Tbl("CallLocationIndexStates")} s
				INNER JOIN {Tbl("DepartmentDataProtectionPolicies")} p ON p.{Col("DepartmentId")} = s.{Col("DepartmentId")}
				WHERE s.{Col("IsSuppressed")} = {False} AND p.{Col("State")} <> 0
				ORDER BY s.{Col("DepartmentId")}{Limit}";

			return (await QueryAsync<int>(sql, new { Take = take })).ToList();
		}

		public async Task<List<int>> GetDepartmentsToResumeAsync(int take)
		{
			var sql = $@"SELECT {Top}s.{Col("DepartmentId")}
				FROM {Tbl("CallLocationIndexStates")} s
				LEFT JOIN {Tbl("DepartmentDataProtectionPolicies")} p ON p.{Col("DepartmentId")} = s.{Col("DepartmentId")}
				WHERE s.{Col("IsSuppressed")} = {True} AND (p.{Col("DepartmentId")} IS NULL OR p.{Col("State")} = 0)
				ORDER BY s.{Col("DepartmentId")}{Limit}";

			return (await QueryAsync<int>(sql, new { Take = take })).ToList();
		}

		public async Task<List<CallLocationSource>> GetSourcesAsync(int departmentId, int? beforeCallId, int take)
		{
			var sql = $@"SELECT {Top}{Col("CallId")} AS CallId, {Col("DepartmentId")} AS DepartmentId, {Col("Address")} AS Address,
					{Col("GeoLocationData")} AS GeoLocationData, {Col("LoggedOn")} AS LoggedOn
				FROM {Tbl("Calls")}
				WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("IsDeleted")} = {False}" +
				(beforeCallId.HasValue ? $" AND {Col("CallId")} < {P}BeforeCallId" : string.Empty) +
				$" ORDER BY {Col("CallId")} DESC{Limit}";

			return (await QueryAsync<CallLocationSource>(sql, new { DepartmentId = departmentId, BeforeCallId = beforeCallId, Take = take })).ToList();
		}

		private sealed class ContactCallCountRow
		{
			public string ContactId { get; set; }
			public int CallCount { get; set; }
		}
	}
}
