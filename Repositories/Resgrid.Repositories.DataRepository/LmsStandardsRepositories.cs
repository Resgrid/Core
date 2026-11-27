using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Resgrid.Model;
using Resgrid.Model.Lms;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository
{
	// Training T5 standards (round 6C, M0392-M0394). Portable SQL over the LMS repository base: member ids are citext on PostgreSQL, so their
	// parameters are cast (pg-citext-text-param-equality); token hashes and statement ids are exact; claims use FOR UPDATE SKIP LOCKED on
	// PostgreSQL and READPAST on SQL Server; nullable timestamps never decide the SQL shape in SQL (pg-untyped-null-param-is-null).

	public class LmsContentPackagesRepository : LmsRepositoryBase<LmsContentPackage>, ILmsContentPackagesRepository
	{
		public LmsContentPackagesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<LmsContentPackage> GetByIdForDepartmentAsync(int departmentId, int lmsContentPackageId) =>
			QueryFirstOrDefaultAsync<LmsContentPackage>(
				$"SELECT * FROM {Tbl("LmsContentPackages")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsContentPackageId")} = {P}Id",
				new { DepartmentId = departmentId, Id = lmsContentPackageId });

		public async Task<List<LmsContentPackage>> GetForDepartmentAsync(int departmentId, bool includeDeleted = false) =>
			(await QueryAsync<LmsContentPackage>(
				$"SELECT * FROM {Tbl("LmsContentPackages")} WHERE {Col("DepartmentId")} = {P}DepartmentId" +
				(includeDeleted ? string.Empty : $" AND {Col("IsDeleted")} = {P}No") + $" ORDER BY {Col("CreatedOn")} DESC, {Col("LmsContentPackageId")} DESC",
				new { DepartmentId = departmentId, No = false })).ToList();

		public async Task<LmsContentPackage> ClaimNextAsync(DateTime utcNow, DateTime staleClaimBeforeUtc, CancellationToken cancellationToken = default)
		{
			var parameters = new { Now = DatabaseTimestamp(utcNow), Stale = DatabaseTimestamp(staleClaimBeforeUtc), No = false };
			var due = $"{Col("IsDeleted")} = {P}No AND ({Col("Status")} = {(int)LmsContentPackageStatuses.Queued} OR ({Col("Status")} = {(int)LmsContentPackageStatuses.Processing} AND {Col("ClaimedOn")} < {P}Stale))";
			var sql = IsPostgres
				? $"UPDATE {Tbl("LmsContentPackages")} SET {Col("Status")} = {(int)LmsContentPackageStatuses.Processing}, {Col("ClaimedOn")} = {P}Now, {Col("Attempts")} = {Col("Attempts")} + 1 " +
				  $"WHERE {Col("LmsContentPackageId")} = (SELECT {Col("LmsContentPackageId")} FROM {Tbl("LmsContentPackages")} WHERE {due} ORDER BY {Col("CreatedOn")} LIMIT 1 FOR UPDATE SKIP LOCKED) RETURNING *"
				: $"WITH next AS (SELECT TOP (1) * FROM {Tbl("LmsContentPackages")} WITH (ROWLOCK, READPAST, UPDLOCK) WHERE {due} ORDER BY {Col("CreatedOn")}) " +
				  $"UPDATE next SET {Col("Status")} = {(int)LmsContentPackageStatuses.Processing}, {Col("ClaimedOn")} = {P}Now, {Col("Attempts")} = {Col("Attempts")} + 1 OUTPUT INSERTED.*;";
			return await QueryFirstOrDefaultAsync<LmsContentPackage>(sql, parameters, cancellationToken);
		}

		public async Task<bool> TryMoveAsync(LmsContentPackage package, LmsContentPackageStatuses from, CancellationToken cancellationToken = default)
		{
			if (package == null)
				return false;
			var parameters = new DynamicParameters();
			parameters.Add("Id", package.LmsContentPackageId);
			parameters.Add("DepartmentId", package.DepartmentId);
			parameters.Add("From", (int)from);
			parameters.Add("Status", package.Status);
			parameters.Add("FileCount", package.FileCount);
			parameters.Add("ContentBytes", package.ContentBytes);
			parameters.Add("LastError", package.LastError, System.Data.DbType.String);
			parameters.Add("ReadyOn", Stamp(package.ReadyOn), System.Data.DbType.DateTime2);
			return await ExecuteAsync(
				$"UPDATE {Tbl("LmsContentPackages")} SET {Col("Status")} = {P}Status, {Col("FileCount")} = {P}FileCount, {Col("ContentBytes")} = {P}ContentBytes, " +
				$"{Col("LastError")} = {P}LastError, {Col("ReadyOn")} = {P}ReadyOn " +
				$"WHERE {Col("LmsContentPackageId")} = {P}Id AND {Col("DepartmentId")} = {P}DepartmentId AND {Col("Status")} = {P}From", parameters, cancellationToken) == 1;
		}
	}

	public class LmsRuntimeRegistrationsRepository : LmsRepositoryBase<LmsRuntimeRegistration>, ILmsRuntimeRegistrationsRepository
	{
		public LmsRuntimeRegistrationsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<LmsRuntimeRegistration> GetByIdForDepartmentAsync(int departmentId, int lmsRuntimeRegistrationId) =>
			QueryFirstOrDefaultAsync<LmsRuntimeRegistration>(
				$"SELECT * FROM {Tbl("LmsRuntimeRegistrations")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsRuntimeRegistrationId")} = {P}Id",
				new { DepartmentId = departmentId, Id = lmsRuntimeRegistrationId });

		public Task<LmsRuntimeRegistration> GetAsync(int departmentId, int lmsEnrollmentId, int lmsLessonId) =>
			QueryFirstOrDefaultAsync<LmsRuntimeRegistration>(
				$"SELECT * FROM {Tbl("LmsRuntimeRegistrations")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsEnrollmentId")} = {P}EnrollmentId AND {Col("LmsLessonId")} = {P}LessonId",
				new { DepartmentId = departmentId, EnrollmentId = lmsEnrollmentId, LessonId = lmsLessonId });

		public Task<LmsRuntimeRegistration> GetForLessonSubjectAsync(int departmentId, int lmsLessonId, string registrationKey) =>
			string.IsNullOrWhiteSpace(registrationKey) ? Task.FromResult<LmsRuntimeRegistration>(null) :
			QueryFirstOrDefaultAsync<LmsRuntimeRegistration>(
				$"SELECT * FROM {Tbl("LmsRuntimeRegistrations")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsLessonId")} = {P}LessonId " +
				$"AND {Col("RegistrationKey")} = {P}RegistrationKey AND {Col("Kind")} = {(int)LmsRuntimeKinds.Lti} ORDER BY {Col("LmsRuntimeRegistrationId")} DESC",
				new { DepartmentId = departmentId, LessonId = lmsLessonId, RegistrationKey = registrationKey.Trim() });

		public async Task<(LmsRuntimeRegistration Registration, bool Inserted)> InsertOrGetAsync(LmsRuntimeRegistration registration, CancellationToken cancellationToken = default)
		{
			var existing = await GetAsync(registration.DepartmentId, registration.LmsEnrollmentId, registration.LmsLessonId);
			if (existing != null)
				return (existing, false);
			if (await TryInsertUniqueAsync(registration, cancellationToken))
				return (registration, true);
			return (await GetAsync(registration.DepartmentId, registration.LmsEnrollmentId, registration.LmsLessonId), false);
		}

		public Task<bool> TryUpdateAsync(LmsRuntimeRegistration registration, long expectedRowVersion, CancellationToken cancellationToken = default) =>
			TryUpdateVersionedAsync(registration, "LmsRuntimeRegistrations", "LmsRuntimeRegistrationId", registration?.LmsRuntimeRegistrationId ?? 0, registration?.DepartmentId ?? 0,
				expectedRowVersion, v => registration.RowVersion = v, cancellationToken);
	}

	public class LmsRuntimeSessionsRepository : LmsRepositoryBase<LmsRuntimeSession>, ILmsRuntimeSessionsRepository
	{
		public LmsRuntimeSessionsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		private Task<LmsRuntimeSession> ByAsync(string column, string hash) =>
			string.IsNullOrWhiteSpace(hash) ? Task.FromResult<LmsRuntimeSession>(null) :
			QueryFirstOrDefaultAsync<LmsRuntimeSession>($"SELECT * FROM {Tbl("LmsRuntimeSessions")} WHERE {Col(column)} = {P}Hash", new { Hash = hash });

		public Task<LmsRuntimeSession> GetByTokenHashAsync(string tokenHash) => ByAsync("TokenHash", tokenHash);

		public Task<LmsRuntimeSession> GetByFetchTokenHashAsync(string fetchTokenHash) => ByAsync("FetchTokenHash", fetchTokenHash);

		public Task<LmsRuntimeSession> GetByAuthTokenHashAsync(string authTokenHash) => ByAsync("AuthTokenHash", authTokenHash);

		public async Task<bool> TryMarkFetchedAsync(int lmsRuntimeSessionId, string authTokenHash, DateTime utcNow, CancellationToken cancellationToken = default) =>
			await ExecuteAsync(
				$"UPDATE {Tbl("LmsRuntimeSessions")} SET {Col("FetchedOn")} = {P}Now, {Col("AuthTokenHash")} = {P}Hash " +
				$"WHERE {Col("LmsRuntimeSessionId")} = {P}Id AND {Col("FetchedOn")} IS NULL",
				new { Id = lmsRuntimeSessionId, Hash = authTokenHash, Now = DatabaseTimestamp(utcNow) }, cancellationToken) == 1;

		public async Task<bool> TryMarkUsedAsync(int lmsRuntimeSessionId, DateTime utcNow, CancellationToken cancellationToken = default) =>
			await ExecuteAsync(
				$"UPDATE {Tbl("LmsRuntimeSessions")} SET {Col("UsedOn")} = {P}Now WHERE {Col("LmsRuntimeSessionId")} = {P}Id AND {Col("UsedOn")} IS NULL",
				new { Id = lmsRuntimeSessionId, Now = DatabaseTimestamp(utcNow) }, cancellationToken) == 1;

		public Task MarkEndedAsync(int lmsRuntimeSessionId, DateTime utcNow, CancellationToken cancellationToken = default) =>
			ExecuteAsync(
				$"UPDATE {Tbl("LmsRuntimeSessions")} SET {Col("EndedOn")} = {P}Now WHERE {Col("LmsRuntimeSessionId")} = {P}Id AND {Col("EndedOn")} IS NULL",
				new { Id = lmsRuntimeSessionId, Now = DatabaseTimestamp(utcNow) }, cancellationToken);

		public Task<int> DeleteExpiredAsync(DateTime beforeUtc, CancellationToken cancellationToken = default) =>
			ExecuteAsync($"DELETE FROM {Tbl("LmsRuntimeSessions")} WHERE {Col("ExpiresOn")} < {P}Before", new { Before = DatabaseTimestamp(beforeUtc) }, cancellationToken);

		/// <summary>Round 10 A8 (M0421, IX_LmsRuntimeSessions_AppSession): the newest session an in-app launch session minted.</summary>
		public Task<LmsRuntimeSession> GetLatestForAppSessionAsync(int departmentId, int appSessionId) =>
			appSessionId <= 0 ? Task.FromResult<LmsRuntimeSession>(null) :
			QueryFirstOrDefaultAsync<LmsRuntimeSession>(
				$"SELECT * FROM {Tbl("LmsRuntimeSessions")} WHERE {Col("AppSessionId")} = {P}AppSessionId AND {Col("DepartmentId")} = {P}DepartmentId " +
				$"ORDER BY {Col("LmsRuntimeSessionId")} DESC {Take()}",
				new { AppSessionId = appSessionId, DepartmentId = departmentId, Take = 1 });
	}

	public class LmsCmi5StatementsRepository : LmsRepositoryBase<LmsCmi5Statement>, ILmsCmi5StatementsRepository
	{
		public LmsCmi5StatementsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		/// <summary>An AU re-sending a statement is normal: the stored one is found first; the unique index settles a concurrent twin.</summary>
		public async Task<bool> TryInsertAsync(LmsCmi5Statement statement, CancellationToken cancellationToken = default)
		{
			if (await GetByStatementIdAsync(statement.DepartmentId, statement.LmsRuntimeRegistrationId, statement.StatementId) != null)
				return false;
			return await TryInsertUniqueAsync(statement, cancellationToken);
		}

		public Task<LmsCmi5Statement> GetByStatementIdAsync(int departmentId, int lmsRuntimeRegistrationId, string statementId) =>
			QueryFirstOrDefaultAsync<LmsCmi5Statement>(
				$"SELECT * FROM {Tbl("LmsCmi5Statements")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsRuntimeRegistrationId")} = {P}RegistrationId " +
				$"AND {Col("StatementId")} = {P}StatementId",
				new { DepartmentId = departmentId, RegistrationId = lmsRuntimeRegistrationId, StatementId = statementId });

		public Task<int> CountForRegistrationAsync(int departmentId, int lmsRuntimeRegistrationId) =>
			ScalarAsync<int>(
				$"SELECT COUNT(1) FROM {Tbl("LmsCmi5Statements")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsRuntimeRegistrationId")} = {P}RegistrationId",
				new { DepartmentId = departmentId, RegistrationId = lmsRuntimeRegistrationId });

		public async Task<List<LmsCmi5Statement>> GetForRegistrationAsync(int departmentId, int lmsRuntimeRegistrationId, int take) =>
			(await QueryAsync<LmsCmi5Statement>(
				$"SELECT * FROM {Tbl("LmsCmi5Statements")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsRuntimeRegistrationId")} = {P}RegistrationId " +
				$"ORDER BY {Col("LmsCmi5StatementId")} DESC {Take()}",
				new { DepartmentId = departmentId, RegistrationId = lmsRuntimeRegistrationId, Take = Math.Clamp(take, 1, 5000) })).ToList();
	}

	public class LmsCmi5StateDocumentsRepository : LmsRepositoryBase<LmsCmi5StateDocument>, ILmsCmi5StateDocumentsRepository
	{
		public LmsCmi5StateDocumentsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<LmsCmi5StateDocument> GetAsync(int departmentId, int lmsRuntimeRegistrationId, string activityId, string stateId) =>
			QueryFirstOrDefaultAsync<LmsCmi5StateDocument>(
				$"SELECT * FROM {Tbl("LmsCmi5StateDocuments")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsRuntimeRegistrationId")} = {P}RegistrationId " +
				$"AND {Col("ActivityId")} = {P}ActivityId AND {Col("StateId")} = {P}StateId",
				new { DepartmentId = departmentId, RegistrationId = lmsRuntimeRegistrationId, ActivityId = activityId ?? string.Empty, StateId = stateId ?? string.Empty });

		public Task<int> DeleteAsync(int departmentId, int lmsRuntimeRegistrationId, string activityId, string stateId, CancellationToken cancellationToken = default) =>
			ExecuteAsync(
				$"DELETE FROM {Tbl("LmsCmi5StateDocuments")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsRuntimeRegistrationId")} = {P}RegistrationId " +
				$"AND {Col("ActivityId")} = {P}ActivityId" + (stateId == null ? string.Empty : $" AND {Col("StateId")} = {P}StateId"),
				new { DepartmentId = departmentId, RegistrationId = lmsRuntimeRegistrationId, ActivityId = activityId ?? string.Empty, StateId = stateId ?? string.Empty }, cancellationToken);
	}

	public class LmsLtiToolsRepository : LmsRepositoryBase<LmsLtiTool>, ILmsLtiToolsRepository
	{
		public LmsLtiToolsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<List<LmsLtiTool>> GetForDepartmentAsync(int departmentId, bool includeDeleted = false) =>
			(await QueryAsync<LmsLtiTool>(
				$"SELECT * FROM {Tbl("LmsLtiTools")} WHERE {Col("DepartmentId")} = {P}DepartmentId" + (includeDeleted ? string.Empty : $" AND {Col("IsDeleted")} = {P}No") +
				$" ORDER BY {Col("Name")}, {Col("LmsLtiToolId")}",
				new { DepartmentId = departmentId, No = false })).ToList();

		public Task<LmsLtiTool> GetByIdForDepartmentAsync(int departmentId, int lmsLtiToolId) =>
			QueryFirstOrDefaultAsync<LmsLtiTool>(
				$"SELECT * FROM {Tbl("LmsLtiTools")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsLtiToolId")} = {P}Id",
				new { DepartmentId = departmentId, Id = lmsLtiToolId });

		public Task<LmsLtiTool> GetByClientIdAsync(int departmentId, string clientId) =>
			string.IsNullOrWhiteSpace(clientId) ? Task.FromResult<LmsLtiTool>(null) :
			QueryFirstOrDefaultAsync<LmsLtiTool>(
				$"SELECT * FROM {Tbl("LmsLtiTools")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("ClientId")} = {P}ClientId",
				new { DepartmentId = departmentId, ClientId = clientId.Trim() });
	}

	public class LmsLtiPlatformKeysRepository : LmsRepositoryBase<LmsLtiPlatformKey>, ILmsLtiPlatformKeysRepository
	{
		public LmsLtiPlatformKeysRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<List<LmsLtiPlatformKey>> GetForDepartmentAsync(int departmentId) =>
			(await QueryAsync<LmsLtiPlatformKey>(
				$"SELECT * FROM {Tbl("LmsLtiPlatformKeys")} WHERE {Col("DepartmentId")} = {P}DepartmentId ORDER BY {Col("CreatedOn")} DESC, {Col("LmsLtiPlatformKeyId")} DESC",
				new { DepartmentId = departmentId })).ToList();

		public Task<int> RetireOthersAsync(int departmentId, int keepKeyId, DateTime utcNow, CancellationToken cancellationToken = default) =>
			ExecuteAsync(
				$"UPDATE {Tbl("LmsLtiPlatformKeys")} SET {Col("Status")} = {(int)LmsLtiKeyStatuses.Retired}, {Col("RetiredOn")} = {P}Now " +
				$"WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("Status")} = {(int)LmsLtiKeyStatuses.Active} AND {Col("LmsLtiPlatformKeyId")} <> {P}Keep",
				new { DepartmentId = departmentId, Keep = keepKeyId, Now = DatabaseTimestamp(utcNow) }, cancellationToken);
	}

	public class LmsLtiAccessTokensRepository : LmsRepositoryBase<LmsLtiAccessToken>, ILmsLtiAccessTokensRepository
	{
		public LmsLtiAccessTokensRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<bool> TryInsertAsync(LmsLtiAccessToken token, CancellationToken cancellationToken = default) => TryInsertUniqueAsync(token, cancellationToken);

		public Task<LmsLtiAccessToken> GetByHashAsync(int departmentId, string tokenHash) =>
			string.IsNullOrWhiteSpace(tokenHash) ? Task.FromResult<LmsLtiAccessToken>(null) :
			QueryFirstOrDefaultAsync<LmsLtiAccessToken>(
				$"SELECT * FROM {Tbl("LmsLtiAccessTokens")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("TokenHash")} = {P}Hash",
				new { DepartmentId = departmentId, Hash = tokenHash });

		public Task<int> DeleteExpiredAsync(DateTime beforeUtc, CancellationToken cancellationToken = default) =>
			ExecuteAsync($"DELETE FROM {Tbl("LmsLtiAccessTokens")} WHERE {Col("ExpiresOn")} < {P}Before", new { Before = DatabaseTimestamp(beforeUtc) }, cancellationToken);
	}

	public class LmsXapiOutboxRepository : LmsRepositoryBase<LmsXapiOutboxItem>, ILmsXapiOutboxRepository
	{
		public LmsXapiOutboxRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<List<LmsXapiOutboxItem>> ClaimDueAsync(DateTime utcNow, DateTime staleClaimBeforeUtc, int take, CancellationToken cancellationToken = default)
		{
			var parameters = new { Now = DatabaseTimestamp(utcNow), Stale = DatabaseTimestamp(staleClaimBeforeUtc), Take = Math.Clamp(take, 1, 1000) };
			var due = $"(({Col("Status")} = {(int)LmsXapiOutboxStatuses.Pending} AND {Col("NextAttemptOn")} <= {P}Now) OR " +
				$"({Col("Status")} = {(int)LmsXapiOutboxStatuses.Sending} AND {Col("ClaimedOn")} < {P}Stale))";
			var sql = IsPostgres
				? $"UPDATE {Tbl("LmsXapiOutbox")} SET {Col("Status")} = {(int)LmsXapiOutboxStatuses.Sending}, {Col("ClaimedOn")} = {P}Now, {Col("Attempts")} = {Col("Attempts")} + 1 " +
				  $"WHERE {Col("LmsXapiOutboxItemId")} IN (SELECT {Col("LmsXapiOutboxItemId")} FROM {Tbl("LmsXapiOutbox")} WHERE {due} ORDER BY {Col("CreatedOn")}, {Col("LmsXapiOutboxItemId")} " +
				  $"LIMIT {P}Take FOR UPDATE SKIP LOCKED) RETURNING *"
				: $"WITH next AS (SELECT TOP ({P}Take) * FROM {Tbl("LmsXapiOutbox")} WITH (ROWLOCK, READPAST, UPDLOCK) WHERE {due} ORDER BY {Col("CreatedOn")}, {Col("LmsXapiOutboxItemId")}) " +
				  $"UPDATE next SET {Col("Status")} = {(int)LmsXapiOutboxStatuses.Sending}, {Col("ClaimedOn")} = {P}Now, {Col("Attempts")} = {Col("Attempts")} + 1 OUTPUT INSERTED.*;";
			return (await QueryAsync<LmsXapiOutboxItem>(sql, parameters, cancellationToken)).ToList();
		}

		public async Task<bool> TryFinishAsync(LmsXapiOutboxItem item, CancellationToken cancellationToken = default)
		{
			if (item == null)
				return false;
			var parameters = new DynamicParameters();
			parameters.Add("Id", item.LmsXapiOutboxItemId);
			parameters.Add("Status", item.Status);
			parameters.Add("Sending", (int)LmsXapiOutboxStatuses.Sending);
			parameters.Add("NextAttemptOn", DatabaseTimestamp(item.NextAttemptOn), System.Data.DbType.DateTime2);
			parameters.Add("LastError", item.LastError, System.Data.DbType.String);
			parameters.Add("DeliveredOn", Stamp(item.DeliveredOn), System.Data.DbType.DateTime2);
			return await ExecuteAsync(
				$"UPDATE {Tbl("LmsXapiOutbox")} SET {Col("Status")} = {P}Status, {Col("NextAttemptOn")} = {P}NextAttemptOn, {Col("LastError")} = {P}LastError, " +
				$"{Col("DeliveredOn")} = {P}DeliveredOn WHERE {Col("LmsXapiOutboxItemId")} = {P}Id AND {Col("Status")} = {P}Sending", parameters, cancellationToken) == 1;
		}

		public async Task<(int Pending, int Failed, int Delivered)> GetCountsAsync(int departmentId)
		{
			var rows = (await QueryAsync<StatusCount>(
				$"SELECT {Col("Status")} AS Status, CAST(COUNT(1) AS bigint) AS Total FROM {Tbl("LmsXapiOutbox")} WHERE {Col("DepartmentId")} = {P}DepartmentId GROUP BY {Col("Status")}",
				new { DepartmentId = departmentId })).ToList();
			int Of(params LmsXapiOutboxStatuses[] statuses) => (int)Math.Min(int.MaxValue, rows.Where(r => statuses.Contains((LmsXapiOutboxStatuses)r.Status)).Sum(r => r.Total));
			return (Of(LmsXapiOutboxStatuses.Pending, LmsXapiOutboxStatuses.Sending), Of(LmsXapiOutboxStatuses.Failed), Of(LmsXapiOutboxStatuses.Delivered));
		}

		private sealed class StatusCount
		{
			public int Status { get; set; }
			public long Total { get; set; }
		}

		public async Task<List<LmsXapiOutboxItem>> GetRecentFailedAsync(int departmentId, int take) =>
			(await QueryAsync<LmsXapiOutboxItem>(
				$"SELECT {Cols("LmsXapiOutboxItemId", "DepartmentId", "StatementId", "VerbId", "Status", "Attempts", "NextAttemptOn", "LastError", "CreatedOn")} " +
				$"FROM {Tbl("LmsXapiOutbox")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("Status")} = {(int)LmsXapiOutboxStatuses.Failed} " +
				$"ORDER BY {Col("LmsXapiOutboxItemId")} DESC {Take()}",
				new { DepartmentId = departmentId, Take = Math.Clamp(take, 1, 200) })).ToList();

		public Task<int> RequeueFailedAsync(int departmentId, DateTime utcNow, CancellationToken cancellationToken = default) =>
			ExecuteAsync(
				$"UPDATE {Tbl("LmsXapiOutbox")} SET {Col("Status")} = {(int)LmsXapiOutboxStatuses.Pending}, {Col("Attempts")} = 0, {Col("NextAttemptOn")} = {P}Now " +
				$"WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("Status")} = {(int)LmsXapiOutboxStatuses.Failed}",
				new { DepartmentId = departmentId, Now = DatabaseTimestamp(utcNow) }, cancellationToken);

		public Task<int> PurgeDeliveredAsync(DateTime beforeUtc, CancellationToken cancellationToken = default) =>
			ExecuteAsync(
				$"DELETE FROM {Tbl("LmsXapiOutbox")} WHERE {Col("Status")} = {(int)LmsXapiOutboxStatuses.Delivered} AND {Col("DeliveredOn")} < {P}Before",
				new { Before = DatabaseTimestamp(beforeUtc) }, cancellationToken);
	}
}
