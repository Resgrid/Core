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
