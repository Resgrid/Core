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
	/// The user-level security notice outbox (plan section 6.4). Claims take a lease in one guarded statement (PostgreSQL
	/// <c>FOR UPDATE SKIP LOCKED</c>, SQL Server <c>UPDLOCK, READPAST</c>), and only the lease holder records a result.
	/// </summary>
	public sealed class SecurityNoticeRepository : ISecurityNoticeRepository
	{
		private const int Pending = (int)SecurityNoticeState.Pending;
		private const int Sent = (int)SecurityNoticeState.Sent;
		private const int Failed = (int)SecurityNoticeState.Failed;

		private const string Columns = @"SecurityNoticeId, UserId, Kind, OccurredOnUtc, ClientApplication, InstallationLabel, Region, State, Attempts,
			NextAttemptOnUtc, LeaseOwner, LeaseUntilUtc, SentOnUtc, LastFailure, CreatedOnUtc";

		private readonly IConnectionProvider _connections;
		private readonly bool _postgres;
		private readonly string _table;

		public SecurityNoticeRepository(IConnectionProvider connections, SqlConfiguration configuration)
		{
			_connections = connections;
			_postgres = DataConfig.DatabaseType == DatabaseTypes.Postgres;
			_table = configuration.SchemaName + (_postgres ? ".securitynotices" : ".[SecurityNotices]");
		}

		public async Task InsertAsync(SecurityNotice notice, CancellationToken cancellationToken = default)
		{
			await using var connection = _connections.Create();
			await connection.OpenAsync(cancellationToken);
			await connection.ExecuteAsync(new CommandDefinition(
				$@"INSERT INTO {_table} (SecurityNoticeId, UserId, Kind, OccurredOnUtc, ClientApplication, InstallationLabel, Region, State, Attempts,
					NextAttemptOnUtc, CreatedOnUtc)
				VALUES (@SecurityNoticeId, @UserId, @Kind, @OccurredOnUtc, @ClientApplication, @InstallationLabel, @Region, {Pending}, 0,
					@NextAttemptOnUtc, @CreatedOnUtc)",
				new
				{
					notice.SecurityNoticeId,
					notice.UserId,
					notice.Kind,
					OccurredOnUtc = Timestamp(notice.OccurredOnUtc),
					notice.ClientApplication,
					notice.InstallationLabel,
					notice.Region,
					NextAttemptOnUtc = Timestamp(notice.NextAttemptOnUtc),
					CreatedOnUtc = Timestamp(notice.CreatedOnUtc)
				}, cancellationToken: cancellationToken));
		}

		public async Task<SecurityNotice> GetAsync(string securityNoticeId, CancellationToken cancellationToken = default)
		{
			await using var connection = _connections.Create();
			await connection.OpenAsync(cancellationToken);
			return await connection.QuerySingleOrDefaultAsync<SecurityNotice>(new CommandDefinition(
				$"SELECT {Columns} FROM {_table} WHERE SecurityNoticeId = @Id", new { Id = securityNoticeId }, cancellationToken: cancellationToken));
		}

		public async Task<bool> TryClaimAsync(string securityNoticeId, string owner, DateTime utcNow, DateTime leaseUntilUtc,
			CancellationToken cancellationToken = default)
		{
			await using var connection = _connections.Create();
			await connection.OpenAsync(cancellationToken);
			return await connection.ExecuteAsync(new CommandDefinition(
				$@"UPDATE {_table} SET LeaseOwner = @Owner, LeaseUntilUtc = @Until
				WHERE SecurityNoticeId = @Id AND State = {Pending} AND NextAttemptOnUtc <= @Now AND (LeaseUntilUtc IS NULL OR LeaseUntilUtc < @Now)",
				new { Id = securityNoticeId, Owner = owner, Now = Timestamp(utcNow), Until = Timestamp(leaseUntilUtc) },
				cancellationToken: cancellationToken)) == 1;
		}

		public async Task<IReadOnlyList<SecurityNotice>> ClaimDueAsync(string owner, DateTime utcNow, DateTime leaseUntilUtc, int batchSize,
			CancellationToken cancellationToken = default)
		{
			var due = $"State = {Pending} AND NextAttemptOnUtc <= @Now AND (LeaseUntilUtc IS NULL OR LeaseUntilUtc < @Now)";
			var sql = _postgres
				? $@"UPDATE {_table} SET LeaseOwner = @Owner, LeaseUntilUtc = @Until
					WHERE SecurityNoticeId IN (SELECT SecurityNoticeId FROM {_table} WHERE {due} ORDER BY NextAttemptOnUtc LIMIT @Batch FOR UPDATE SKIP LOCKED)
					RETURNING {Columns}"
				: $@"WITH due AS (SELECT TOP (@Batch) * FROM {_table} WITH (UPDLOCK, READPAST, ROWLOCK) WHERE {due} ORDER BY NextAttemptOnUtc)
					UPDATE due SET LeaseOwner = @Owner, LeaseUntilUtc = @Until
					OUTPUT INSERTED.SecurityNoticeId, INSERTED.UserId, INSERTED.Kind, INSERTED.OccurredOnUtc, INSERTED.ClientApplication,
						INSERTED.InstallationLabel, INSERTED.Region, INSERTED.State, INSERTED.Attempts, INSERTED.NextAttemptOnUtc, INSERTED.LeaseOwner,
						INSERTED.LeaseUntilUtc, INSERTED.SentOnUtc, INSERTED.LastFailure, INSERTED.CreatedOnUtc;";

			await using var connection = _connections.Create();
			await connection.OpenAsync(cancellationToken);
			return (await connection.QueryAsync<SecurityNotice>(new CommandDefinition(sql,
				new { Owner = owner, Now = Timestamp(utcNow), Until = Timestamp(leaseUntilUtc), Batch = Math.Max(1, batchSize) },
				cancellationToken: cancellationToken))).ToList();
		}

		public async Task<bool> MarkSentAsync(string securityNoticeId, string owner, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			await using var connection = _connections.Create();
			await connection.OpenAsync(cancellationToken);
			return await connection.ExecuteAsync(new CommandDefinition(
				$@"UPDATE {_table} SET State = {Sent}, SentOnUtc = @Now, Attempts = Attempts + 1, LeaseOwner = NULL, LeaseUntilUtc = NULL, LastFailure = NULL
				WHERE SecurityNoticeId = @Id AND State = {Pending} AND LeaseOwner = @Owner",
				new { Id = securityNoticeId, Owner = owner, Now = Timestamp(utcNow) }, cancellationToken: cancellationToken)) == 1;
		}

		public async Task<bool> MarkRetryAsync(string securityNoticeId, string owner, DateTime nextAttemptOnUtc, string failure,
			CancellationToken cancellationToken = default)
		{
			await using var connection = _connections.Create();
			await connection.OpenAsync(cancellationToken);
			return await connection.ExecuteAsync(new CommandDefinition(
				$@"UPDATE {_table} SET Attempts = Attempts + 1, NextAttemptOnUtc = @Next, LastFailure = @Failure, LeaseOwner = NULL, LeaseUntilUtc = NULL
				WHERE SecurityNoticeId = @Id AND State = {Pending} AND LeaseOwner = @Owner",
				new { Id = securityNoticeId, Owner = owner, Next = Timestamp(nextAttemptOnUtc), Failure = Limit(failure) },
				cancellationToken: cancellationToken)) == 1;
		}

		public async Task<bool> MarkFailedAsync(string securityNoticeId, string owner, string failure, CancellationToken cancellationToken = default)
		{
			await using var connection = _connections.Create();
			await connection.OpenAsync(cancellationToken);
			return await connection.ExecuteAsync(new CommandDefinition(
				$@"UPDATE {_table} SET State = {Failed}, Attempts = Attempts + 1, LastFailure = @Failure, LeaseOwner = NULL, LeaseUntilUtc = NULL
				WHERE SecurityNoticeId = @Id AND State = {Pending} AND LeaseOwner = @Owner",
				new { Id = securityNoticeId, Owner = owner, Failure = Limit(failure) }, cancellationToken: cancellationToken)) == 1;
		}

		public async Task<bool> ExistsSinceAsync(string userId, SecurityNoticeKind kind, DateTime sinceUtc, CancellationToken cancellationToken = default)
		{
			await using var connection = _connections.Create();
			await connection.OpenAsync(cancellationToken);
			return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
				$"SELECT COUNT(*) FROM {_table} WHERE UserId = @UserId AND Kind = @Kind AND CreatedOnUtc > @Since AND State <> {Failed}",
				new { UserId = userId, Kind = (int)kind, Since = Timestamp(sinceUtc) }, cancellationToken: cancellationToken)) > 0;
		}

		public async Task<int> PurgeFinishedBeforeAsync(DateTime utcCutoff, CancellationToken cancellationToken = default)
		{
			await using var connection = _connections.Create();
			await connection.OpenAsync(cancellationToken);
			return await connection.ExecuteAsync(new CommandDefinition(
				$"DELETE FROM {_table} WHERE State <> {Pending} AND CreatedOnUtc < @Cutoff",
				new { Cutoff = Timestamp(utcCutoff) }, cancellationToken: cancellationToken));
		}

		public async Task<int> DeleteForUserAsync(string userId, CancellationToken cancellationToken = default)
		{
			await using var connection = _connections.Create();
			await connection.OpenAsync(cancellationToken);
			return await connection.ExecuteAsync(new CommandDefinition(
				$"DELETE FROM {_table} WHERE UserId = @UserId", new { UserId = userId }, cancellationToken: cancellationToken));
		}

		private static string Limit(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Length <= 64 ? value : value[..64];

		// Npgsql refuses Kind=Utc for "timestamp without time zone"; SQL Server ignores Kind.
		private DateTime Timestamp(DateTime utc) => _postgres ? DateTime.SpecifyKind(utc, DateTimeKind.Unspecified) : utc;
	}
}
