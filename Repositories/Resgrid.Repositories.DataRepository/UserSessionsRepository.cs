using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository
{
	public class UserSessionsRepository : RepositoryBase<UserSession>, IUserSessionsRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly IUnitOfWork _unitOfWork;
		private readonly string _table;
		private readonly bool _isPostgres;

		public UserSessionsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration,
			IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_unitOfWork = unitOfWork;
			_isPostgres = DataConfig.DatabaseType == DatabaseTypes.Postgres;
			_table = _isPostgres
				? $"{sqlConfiguration.SchemaName}.usersessions"
				: $"{sqlConfiguration.SchemaName}.[UserSessions]";
		}

		public async Task<IReadOnlyList<UserSession>> GetActiveByUserAsync(string userId, DateTime utcNow)
		{
			var sql = _isPostgres
				? $"SELECT * FROM {_table} WHERE userid = @UserId AND state = @State AND expireson > @UtcNow ORDER BY lastactiveon DESC"
				: $"SELECT * FROM {_table} WHERE [UserId] = @UserId AND [State] = @State AND [ExpiresOn] > @UtcNow ORDER BY [LastActiveOn] DESC";

			var sessions = await WithConnectionAsync(connection => connection.QueryAsync<UserSession>(
				sql, new { UserId = userId, State = (int)UserSessionState.Active, UtcNow = utcNow }, _unitOfWork?.Transaction));
			return sessions.ToList();
		}

		public Task<UserSession> GetByAuthorizationIdAsync(string authorizationId)
		{
			var sql = _isPostgres
				? $"SELECT * FROM {_table} WHERE openiddictauthorizationid = @AuthorizationId"
				: $"SELECT * FROM {_table} WHERE [OpenIddictAuthorizationId] = @AuthorizationId";
			return WithConnectionAsync(connection => connection.QueryFirstOrDefaultAsync<UserSession>(
				sql, new { AuthorizationId = authorizationId }, _unitOfWork?.Transaction));
		}

		/// <summary>
		/// Enforces the department's MaxConcurrentSessions limit and inserts the session as one serialized
		/// unit of work, closing the check-then-insert race two simultaneous logins would otherwise win.
		/// SQL Server holds a range lock on the counted key range (UPDLOCK, HOLDLOCK); PostgreSQL serializes
		/// on a transaction-scoped advisory lock keyed to the user and department.
		/// </summary>
		public async Task<bool> TryInsertWithinDepartmentSessionLimitAsync(UserSession session, int departmentId,
			DateTime policyGateOn, int maxConcurrentSessions, DateTime utcNow, CancellationToken cancellationToken)
		{
			if (session == null)
				throw new ArgumentNullException(nameof(session));
			if (maxConcurrentSessions <= 0)
				throw new ArgumentOutOfRangeException(nameof(maxConcurrentSessions));

			Utf8WriteGuard.Sanitize(session);

			var parameters = new DynamicParameters(session);
			parameters.Add("LimitDepartmentId", departmentId);
			parameters.Add("PolicyGateOn", policyGateOn);
			parameters.Add("MaxConcurrentSessions", maxConcurrentSessions);
			parameters.Add("UtcNow", utcNow);
			parameters.Add("ActiveState", (int)UserSessionState.Active);

			// The lock key must be stable across processes, so it cannot use string.GetHashCode (randomized
			// per process). PostgreSQL stores userid as citext, so the key is folded to lower case to keep
			// two differently-cased spellings of the same user on the same lock.
			var lockKey = StableLockKey($"usersessions:{session.UserId?.ToLowerInvariant()}:{departmentId}");

			if (_unitOfWork?.Connection != null)
			{
				var ambient = _unitOfWork.CreateOrGetConnection();

				// An ambient transaction already provides the scope the locks need; joining it also keeps the
				// caller's rollback semantics. Without one, a short local transaction is opened instead, because
				// the PostgreSQL advisory lock is released the moment its transaction ends.
				if (_unitOfWork.Transaction != null)
					return await GuardedInsertAsync(ambient, _unitOfWork.Transaction, parameters, lockKey, cancellationToken);

				using var ambientScope = await ambient.BeginTransactionAsync(cancellationToken);
				var ambientInserted = await GuardedInsertAsync(ambient, ambientScope, parameters, lockKey, cancellationToken);
				await ambientScope.CommitAsync(cancellationToken);
				return ambientInserted;
			}

			using var connection = _connectionProvider.Create();
			await connection.OpenAsync(cancellationToken);

			// Own transaction so the count and the insert cannot be interleaved by another login. Disposing
			// without a commit rolls back, so a failure between the two statements leaves no partial state.
			using var transaction = await connection.BeginTransactionAsync(cancellationToken);
			var inserted = await GuardedInsertAsync(connection, transaction, parameters, lockKey, cancellationToken);
			await transaction.CommitAsync(cancellationToken);
			return inserted;
		}

		private async Task<bool> GuardedInsertAsync(DbConnection connection, DbTransaction transaction,
			DynamicParameters parameters, long lockKey, CancellationToken cancellationToken)
		{
			if (_isPostgres)
			{
				// Advisory lock is released when the enclosing transaction ends; it only serializes logins
				// for this user and department and never blocks reads of the table.
				await connection.ExecuteAsync(new Dapper.CommandDefinition("SELECT pg_advisory_xact_lock(@LockKey);",
					new { LockKey = lockKey }, transaction, cancellationToken: cancellationToken));
			}

			var columns = _isPostgres
				? @"usersessionid, userid, departmentid, authenticationgeneration, state, stateversion,
					clientapplication, clientinstanceidhash, devicename, devicetype, operatingsystem, browser,
					applicationversion, authenticationmethod, departmentssoconfigid, openiddictauthorizationid,
					webcookieticketkey, createdon, lastactiveon, expireson, firstipaddress, lastipaddress,
					lastcountry, lastregion, lastcity, useragent, islegacyadopted, revokedon, revokedbyuserid,
					revocationreason"
				: @"[UserSessionId], [UserId], [DepartmentId], [AuthenticationGeneration], [State], [StateVersion],
					[ClientApplication], [ClientInstanceIdHash], [DeviceName], [DeviceType], [OperatingSystem], [Browser],
					[ApplicationVersion], [AuthenticationMethod], [DepartmentSsoConfigId], [OpenIddictAuthorizationId],
					[WebCookieTicketKey], [CreatedOn], [LastActiveOn], [ExpiresOn], [FirstIpAddress], [LastIpAddress],
					[LastCountry], [LastRegion], [LastCity], [UserAgent], [IsLegacyAdopted], [RevokedOn], [RevokedByUserId],
					[RevocationReason]";

			const string values = @"@UserSessionId, @UserId, @DepartmentId, @AuthenticationGeneration, @State, @StateVersion,
					@ClientApplication, @ClientInstanceIdHash, @DeviceName, @DeviceType, @OperatingSystem, @Browser,
					@ApplicationVersion, @AuthenticationMethod, @DepartmentSsoConfigId, @OpenIddictAuthorizationId,
					@WebCookieTicketKey, @CreatedOn, @LastActiveOn, @ExpiresOn, @FirstIpAddress, @LastIpAddress,
					@LastCountry, @LastRegion, @LastCity, @UserAgent, @IsLegacyAdopted, @RevokedOn, @RevokedByUserId,
					@RevocationReason";

			// The counted set mirrors the policy rule exactly: active, unexpired sessions this user holds in
			// the department that were created on or after the policy gate.
			var countSql = _isPostgres
				? $@"SELECT COUNT(*) FROM {_table}
					WHERE userid = @UserId AND departmentid = @LimitDepartmentId AND state = @ActiveState
						AND expireson > @UtcNow AND createdon >= @PolicyGateOn"
				: $@"SELECT COUNT(*) FROM {_table} WITH (UPDLOCK, HOLDLOCK)
					WHERE [UserId] = @UserId AND [DepartmentId] = @LimitDepartmentId AND [State] = @ActiveState
						AND [ExpiresOn] > @UtcNow AND [CreatedOn] >= @PolicyGateOn";

			var sql = $@"INSERT INTO {_table} ({columns})
				SELECT {values}
				WHERE ({countSql}) < @MaxConcurrentSessions;";

			var rows = await connection.ExecuteAsync(new Dapper.CommandDefinition(sql, parameters, transaction,
				cancellationToken: cancellationToken));
			return rows > 0;
		}

		private static long StableLockKey(string value)
		{
			// FNV-1a 64-bit: deterministic across processes and runtime versions.
			// The unchecked block is load-bearing, not an oversight. Wrapping multiplication IS the FNV
			// algorithm, and the final cast reinterprets the full 64-bit width rather than converting a
			// magnitude - roughly half of all hashes have the high bit set and exceed long.MaxValue.
			// pg_advisory_xact_lock takes a bigint, so every one of those bit patterns is a valid key.
			// Making either operation checked would throw on ordinary input.
			unchecked
			{
				const ulong offsetBasis = 14695981039346656037;
				const ulong prime = 1099511628211;

				var hash = offsetBasis;
				foreach (var character in value ?? string.Empty)
				{
					hash ^= character;
					hash *= prime;
				}

				return (long)hash;
			}
		}

		public Task<int> TouchAsync(string sessionId, DateTime occurredOn, DateTime writeBefore, string ipAddress,
			string country, string region, string city, string userAgent, CancellationToken cancellationToken)
		{
			var sql = _isPostgres
				? $@"UPDATE {_table} SET lastactiveon = @OccurredOn, lastipaddress = @IpAddress,
					lastcountry = @Country, lastregion = @Region, lastcity = @City, useragent = @UserAgent
					WHERE usersessionid = @SessionId AND state = @State AND lastactiveon <= @WriteBefore"
				: $@"UPDATE {_table} SET [LastActiveOn] = @OccurredOn, [LastIpAddress] = @IpAddress,
					[LastCountry] = @Country, [LastRegion] = @Region, [LastCity] = @City, [UserAgent] = @UserAgent
					WHERE [UserSessionId] = @SessionId AND [State] = @State AND [LastActiveOn] <= @WriteBefore";

			return ExecuteAsync(sql, new
			{
				SessionId = sessionId,
				OccurredOn = occurredOn,
				WriteBefore = writeBefore,
				IpAddress = ipAddress,
				Country = country,
				Region = region,
				City = city,
				UserAgent = userAgent,
				State = (int)UserSessionState.Active
			}, cancellationToken);
		}

		public Task<int> UpdateDepartmentAsync(string targetUserId, string sessionId, int departmentId,
			CancellationToken cancellationToken)
		{
			var sql = _isPostgres
				? $@"UPDATE {_table} SET departmentid = @DepartmentId, stateversion = stateversion + 1
					WHERE userid = @TargetUserId AND usersessionid = @SessionId AND state = @ActiveState"
				: $@"UPDATE {_table} SET [DepartmentId] = @DepartmentId, [StateVersion] = [StateVersion] + 1
					WHERE [UserId] = @TargetUserId AND [UserSessionId] = @SessionId AND [State] = @ActiveState";

			return ExecuteAsync(sql, new
			{
				TargetUserId = targetUserId,
				SessionId = sessionId,
				DepartmentId = departmentId,
				ActiveState = (int)UserSessionState.Active
			}, cancellationToken);
		}

		public Task<int> RevokeAsync(string targetUserId, string sessionId, string actorUserId, int reason,
			DateTime revokedOn, CancellationToken cancellationToken)
		{
			var predicate = _isPostgres
				? "userid = @TargetUserId AND usersessionid = @SessionId"
				: "[UserId] = @TargetUserId AND [UserSessionId] = @SessionId";
			return RevokeWhereAsync(predicate, new { TargetUserId = targetUserId, SessionId = sessionId }, actorUserId, reason, revokedOn, cancellationToken);
		}

		public Task<int> RevokeOthersAsync(string userId, string currentSessionId, int reason,
			DateTime revokedOn, CancellationToken cancellationToken)
		{
			var predicate = _isPostgres
				? "userid = @TargetUserId AND usersessionid <> @CurrentSessionId"
				: "[UserId] = @TargetUserId AND [UserSessionId] <> @CurrentSessionId";
			return RevokeWhereAsync(predicate, new { TargetUserId = userId, CurrentSessionId = currentSessionId }, userId, reason, revokedOn, cancellationToken);
		}

		public Task<int> RevokeAllAsync(string targetUserId, string actorUserId, int reason,
			DateTime revokedOn, CancellationToken cancellationToken)
		{
			var predicate = _isPostgres ? "userid = @TargetUserId" : "[UserId] = @TargetUserId";
			return RevokeWhereAsync(predicate, new { TargetUserId = targetUserId }, actorUserId, reason, revokedOn, cancellationToken);
		}

		public Task<int> RevokeDepartmentAsync(string targetUserId, int departmentId, int reason,
			DateTime revokedOn, CancellationToken cancellationToken)
		{
			var predicate = _isPostgres
				? "userid = @TargetUserId AND departmentid = @DepartmentId"
				: "[UserId] = @TargetUserId AND [DepartmentId] = @DepartmentId";
			return RevokeWhereAsync(predicate, new { TargetUserId = targetUserId, DepartmentId = departmentId }, targetUserId, reason, revokedOn, cancellationToken);
		}

		public Task<int> PurgeInactiveBeforeAsync(DateTime historyBeforeUtc, CancellationToken cancellationToken)
		{
			var sql = _isPostgres
				? $@"DELETE FROM {_table}
					WHERE (state <> @ActiveState AND revokedon IS NOT NULL AND revokedon < @HistoryBeforeUtc)
						OR expireson < @HistoryBeforeUtc"
				: $@"DELETE FROM {_table}
					WHERE ([State] <> @ActiveState AND [RevokedOn] IS NOT NULL AND [RevokedOn] < @HistoryBeforeUtc)
						OR [ExpiresOn] < @HistoryBeforeUtc";

			return ExecuteAsync(sql, new
			{
				ActiveState = (int)UserSessionState.Active,
				HistoryBeforeUtc = historyBeforeUtc
			}, cancellationToken);
		}

		private Task<int> RevokeWhereAsync(string predicate, object values, string actorUserId, int reason,
			DateTime revokedOn, CancellationToken cancellationToken)
		{
			var sql = _isPostgres
				? $@"UPDATE {_table} SET state = @RevokedState, stateversion = stateversion + 1,
					revokedon = @RevokedOn, revokedbyuserid = @ActorUserId, revocationreason = @Reason
					WHERE {predicate} AND state = @ActiveState"
				: $@"UPDATE {_table} SET [State] = @RevokedState, [StateVersion] = [StateVersion] + 1,
					[RevokedOn] = @RevokedOn, [RevokedByUserId] = @ActorUserId, [RevocationReason] = @Reason
					WHERE {predicate} AND [State] = @ActiveState";

			var parameters = new DynamicParameters(values);
			parameters.Add("RevokedState", (int)UserSessionState.Revoked);
			parameters.Add("ActiveState", (int)UserSessionState.Active);
			parameters.Add("RevokedOn", revokedOn);
			parameters.Add("ActorUserId", actorUserId);
			parameters.Add("Reason", reason);
			return ExecuteAsync(sql, parameters, cancellationToken);
		}

		private Task<int> ExecuteAsync(string sql, object parameters, CancellationToken cancellationToken)
		{
			return WithConnectionAsync(connection => connection.ExecuteAsync(
				new Dapper.CommandDefinition(sql, parameters, _unitOfWork?.Transaction, cancellationToken: cancellationToken)));
		}

		private async Task<TResult> WithConnectionAsync<TResult>(Func<DbConnection, Task<TResult>> operation)
		{
			if (_unitOfWork?.Connection != null)
				return await operation(_unitOfWork.CreateOrGetConnection());

			using var connection = _connectionProvider.Create();
			await connection.OpenAsync();
			return await operation(connection);
		}
	}
}
