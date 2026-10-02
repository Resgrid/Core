using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Resgrid.Config;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Security;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository
{
	/// <summary>
	/// Responder approval requests (plan section 5.6; workbook section 8.3). Every transition is one guarded UPDATE whose
	/// success is exactly one row, on its own connection, and advances <c>Version</c>. The one-pending-per-user rule is a
	/// filtered unique index absorbed by the insert statement itself.
	/// </summary>
	public sealed class MfaApprovalRequestRepository : IMfaApprovalRequestRepository
	{
		private const int Pending = (int)MfaApprovalRequestState.Pending;
		private const int Approved = (int)MfaApprovalRequestState.Approved;
		private const int Denied = (int)MfaApprovalRequestState.Denied;
		private const int Expired = (int)MfaApprovalRequestState.Expired;
		private const int Canceled = (int)MfaApprovalRequestState.Canceled;
		private const int Consumed = (int)MfaApprovalRequestState.Consumed;
		private const int TooManyAttempts = (int)MfaApprovalEndReason.TooManyAttempts;

		private const string Columns = @"MfaApprovalRequestId, UserId, RequesterKind, RequesterId, ClientApplication, InstallationLabel, SharedMode,
			DepartmentId, Purpose, Operation, LockVersion, AuthenticationGeneration, MatchNumberHash, OriginRegion, State, Version, Attempts,
			MaxAttempts, CreatedOnUtc, ExpiresOnUtc, DecidedOnUtc, ConsumedOnUtc, EndReason, ApproverSessionId, ApproverPasskeyId";

		private const string InsertValues = @"@MfaApprovalRequestId, @UserId, @RequesterKind, @RequesterId, @ClientApplication, @InstallationLabel,
			@SharedMode, @DepartmentId, @Purpose, @Operation, @LockVersion, @AuthenticationGeneration, @MatchNumberHash, @OriginRegion, 0, 1, 0,
			@MaxAttempts, @CreatedOnUtc, @ExpiresOnUtc, NULL, NULL, NULL, NULL, NULL";

		private readonly IConnectionProvider _connections;
		private readonly bool _postgres;
		private readonly string _table;

		public MfaApprovalRequestRepository(IConnectionProvider connections, SqlConfiguration configuration)
		{
			_connections = connections;
			_postgres = DataConfig.DatabaseType == DatabaseTypes.Postgres;
			_table = configuration.SchemaName + (_postgres ? ".mfaapprovalrequests" : ".[MfaApprovalRequests]");
		}

		public async Task<bool> TryInsertPendingAsync(MfaApprovalRequest request, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			await using var connection = _connections.Create();
			await connection.OpenAsync(cancellationToken);

			// A pending row past its expiry must not hold the one-pending slot.
			await connection.ExecuteAsync(new CommandDefinition(
				$"UPDATE {_table} SET State = {Expired}, Version = Version + 1 WHERE UserId = @UserId AND State = {Pending} AND ExpiresOnUtc <= @Now",
				new { request.UserId, Now = Timestamp(utcNow) }, cancellationToken: cancellationToken));

			var sql = _postgres
				? $@"INSERT INTO {_table} ({Columns}) VALUES ({InsertValues})
					ON CONFLICT (userid) WHERE state = {Pending} DO NOTHING"
				: $@"MERGE {_table} WITH (HOLDLOCK) AS target
					USING (SELECT @UserId AS UserId) AS source
						ON target.UserId = source.UserId AND target.State = {Pending}
					WHEN NOT MATCHED THEN
						INSERT ({Columns}) VALUES ({InsertValues});";

			return await connection.ExecuteAsync(new CommandDefinition(sql, new
			{
				request.MfaApprovalRequestId,
				request.UserId,
				request.RequesterKind,
				request.RequesterId,
				request.ClientApplication,
				request.InstallationLabel,
				request.SharedMode,
				request.DepartmentId,
				request.Purpose,
				request.Operation,
				request.LockVersion,
				request.AuthenticationGeneration,
				request.MatchNumberHash,
				request.OriginRegion,
				request.MaxAttempts,
				CreatedOnUtc = Timestamp(request.CreatedOnUtc),
				ExpiresOnUtc = Timestamp(request.ExpiresOnUtc)
			}, cancellationToken: cancellationToken)) == 1;
		}

		public async Task<MfaApprovalRequest> GetAsync(string approvalRequestId, CancellationToken cancellationToken = default)
		{
			await using var connection = _connections.Create();
			await connection.OpenAsync(cancellationToken);
			return await connection.QuerySingleOrDefaultAsync<MfaApprovalRequest>(new CommandDefinition(
				$"SELECT {Columns} FROM {_table} WHERE MfaApprovalRequestId = @Id",
				new { Id = approvalRequestId }, cancellationToken: cancellationToken));
		}

		public async Task<MfaApprovalRequest> GetPendingForUserAsync(string userId, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			await using var connection = _connections.Create();
			await connection.OpenAsync(cancellationToken);
			return await connection.QueryFirstOrDefaultAsync<MfaApprovalRequest>(new CommandDefinition(
				$"SELECT {Columns} FROM {_table} WHERE UserId = @UserId AND State = {Pending} AND ExpiresOnUtc > @Now ORDER BY CreatedOnUtc DESC",
				new { UserId = userId, Now = Timestamp(utcNow) }, cancellationToken: cancellationToken));
		}

		public async Task<IReadOnlyList<MfaApprovalRequest>> GetRecentForUserAsync(string userId, int take, CancellationToken cancellationToken = default)
		{
			var sql = _postgres
				? $"SELECT {Columns} FROM {_table} WHERE UserId = @UserId ORDER BY CreatedOnUtc DESC LIMIT @Take"
				: $"SELECT TOP (@Take) {Columns} FROM {_table} WHERE UserId = @UserId ORDER BY CreatedOnUtc DESC";

			await using var connection = _connections.Create();
			await connection.OpenAsync(cancellationToken);
			return (await connection.QueryAsync<MfaApprovalRequest>(new CommandDefinition(sql, new { UserId = userId, Take = Math.Max(1, take) },
				cancellationToken: cancellationToken))).ToList();
		}

		public async Task<int> CountCreatedSinceAsync(string userId, DateTime sinceUtc, CancellationToken cancellationToken = default)
		{
			await using var connection = _connections.Create();
			await connection.OpenAsync(cancellationToken);
			return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
				$"SELECT COUNT(*) FROM {_table} WHERE UserId = @UserId AND CreatedOnUtc > @Since",
				new { UserId = userId, Since = Timestamp(sinceUtc) }, cancellationToken: cancellationToken));
		}

		public async Task<MfaApprovalRequestState?> RecordWrongNumberAsync(string approvalRequestId, DateTime utcNow,
			CancellationToken cancellationToken = default)
		{
			// Both engines evaluate every SET expression against the pre-update row, so the limit check sees the old count.
			var set = $@"Attempts = Attempts + 1, Version = Version + 1,
					State = CASE WHEN Attempts + 1 >= MaxAttempts THEN {Denied} ELSE State END,
					EndReason = CASE WHEN Attempts + 1 >= MaxAttempts THEN {TooManyAttempts} ELSE EndReason END,
					DecidedOnUtc = CASE WHEN Attempts + 1 >= MaxAttempts THEN @Now ELSE DecidedOnUtc END";
			var where = $"MfaApprovalRequestId = @Id AND State = {Pending} AND ExpiresOnUtc > @Now";
			var sql = _postgres
				? $"UPDATE {_table} SET {set} WHERE {where} RETURNING State"
				: $"UPDATE {_table} SET {set} OUTPUT INSERTED.State WHERE {where}";

			await using var connection = _connections.Create();
			await connection.OpenAsync(cancellationToken);
			var state = await connection.QueryFirstOrDefaultAsync<int?>(new CommandDefinition(sql, new { Id = approvalRequestId, Now = Timestamp(utcNow) },
				cancellationToken: cancellationToken));
			return state == null ? null : (MfaApprovalRequestState)state.Value;
		}

		public async Task<bool> TryApproveAsync(string approvalRequestId, string approverSessionId, string approverPasskeyId, DateTime utcNow,
			CancellationToken cancellationToken = default)
		{
			await using var connection = _connections.Create();
			await connection.OpenAsync(cancellationToken);
			return await connection.ExecuteAsync(new CommandDefinition(
				$@"UPDATE {_table} SET State = {Approved}, Version = Version + 1, DecidedOnUtc = @Now, ApproverSessionId = @SessionId,
					ApproverPasskeyId = @PasskeyId
				WHERE MfaApprovalRequestId = @Id AND State = {Pending} AND ExpiresOnUtc > @Now AND Attempts < MaxAttempts",
				new { Id = approvalRequestId, SessionId = approverSessionId, PasskeyId = approverPasskeyId, Now = Timestamp(utcNow) },
				cancellationToken: cancellationToken)) == 1;
		}

		public async Task<bool> TryDenyAsync(string approvalRequestId, MfaApprovalEndReason reason, DateTime utcNow,
			CancellationToken cancellationToken = default)
		{
			await using var connection = _connections.Create();
			await connection.OpenAsync(cancellationToken);
			return await connection.ExecuteAsync(new CommandDefinition(
				$@"UPDATE {_table} SET State = {Denied}, Version = Version + 1, DecidedOnUtc = @Now, EndReason = @Reason
				WHERE MfaApprovalRequestId = @Id AND State = {Pending} AND ExpiresOnUtc > @Now",
				new { Id = approvalRequestId, Reason = (int)reason, Now = Timestamp(utcNow) }, cancellationToken: cancellationToken)) == 1;
		}

		public async Task<bool> TryCancelAsync(string approvalRequestId, MfaApprovalRequesterKind requesterKind, string requesterId, DateTime utcNow,
			CancellationToken cancellationToken = default)
		{
			await using var connection = _connections.Create();
			await connection.OpenAsync(cancellationToken);
			return await connection.ExecuteAsync(new CommandDefinition(
				$@"UPDATE {_table} SET State = {Canceled}, Version = Version + 1, DecidedOnUtc = @Now, EndReason = @Reason
				WHERE MfaApprovalRequestId = @Id AND State = {Pending} AND RequesterKind = @Kind AND RequesterId = @RequesterId",
				new
				{
					Id = approvalRequestId,
					Kind = (int)requesterKind,
					RequesterId = requesterId,
					Reason = (int)MfaApprovalEndReason.CanceledByRequester,
					Now = Timestamp(utcNow)
				}, cancellationToken: cancellationToken)) == 1;
		}

		public async Task<bool> TryConsumeAsync(string approvalRequestId, MfaApprovalRequesterKind requesterKind, string requesterId, DateTime utcNow,
			TimeSpan graceAfterExpiry, CancellationToken cancellationToken = default)
		{
			await using var connection = _connections.Create();
			await connection.OpenAsync(cancellationToken);
			return await connection.ExecuteAsync(new CommandDefinition(
				$@"UPDATE {_table} SET State = {Consumed}, Version = Version + 1, ConsumedOnUtc = @Now
				WHERE MfaApprovalRequestId = @Id AND State = {Approved} AND RequesterKind = @Kind AND RequesterId = @RequesterId
					AND ExpiresOnUtc > @Cutoff",
				new
				{
					Id = approvalRequestId,
					Kind = (int)requesterKind,
					RequesterId = requesterId,
					Now = Timestamp(utcNow),
					Cutoff = Timestamp(utcNow - graceAfterExpiry)
				}, cancellationToken: cancellationToken)) == 1;
		}

		public async Task<int> CancelPendingForUserAsync(string userId, MfaApprovalEndReason reason, DateTime utcNow,
			CancellationToken cancellationToken = default)
		{
			await using var connection = _connections.Create();
			await connection.OpenAsync(cancellationToken);
			return await connection.ExecuteAsync(new CommandDefinition(
				$@"UPDATE {_table} SET State = {Canceled}, Version = Version + 1, DecidedOnUtc = @Now, EndReason = @Reason
				WHERE UserId = @UserId AND State = {Pending}",
				new { UserId = userId, Reason = (int)reason, Now = Timestamp(utcNow) }, cancellationToken: cancellationToken));
		}

		public async Task<int> PurgeCreatedBeforeAsync(DateTime utcCutoff, CancellationToken cancellationToken = default)
		{
			await using var connection = _connections.Create();
			await connection.OpenAsync(cancellationToken);
			return await connection.ExecuteAsync(new CommandDefinition(
				$"DELETE FROM {_table} WHERE CreatedOnUtc < @Cutoff",
				new { Cutoff = Timestamp(utcCutoff) }, cancellationToken: cancellationToken));
		}

		// Npgsql refuses Kind=Utc for "timestamp without time zone"; SQL Server ignores Kind.
		private DateTime Timestamp(DateTime utc) => _postgres ? DateTime.SpecifyKind(utc, DateTimeKind.Unspecified) : utc;
	}
}
