using System;
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
	/// Single-use WebAuthn challenges (workbook section 8.3): every state change is one guarded UPDATE whose success is
	/// exactly one row, on its own connection, so two nodes can never both spend one challenge.
	/// </summary>
	public sealed class AuthenticationChallengeRepository : IAuthenticationChallengeRepository
	{
		private const int Pending = (int)AuthenticationChallengeState.Pending;
		private const int Consumed = (int)AuthenticationChallengeState.Consumed;
		private const int Exhausted = (int)AuthenticationChallengeState.Exhausted;
		private const int Canceled = (int)AuthenticationChallengeState.Canceled;

		private readonly IConnectionProvider _connections;
		private readonly bool _postgres;
		private readonly string _table;

		public AuthenticationChallengeRepository(IConnectionProvider connections, SqlConfiguration configuration)
		{
			_connections = connections;
			_postgres = DataConfig.DatabaseType == DatabaseTypes.Postgres;
			_table = configuration.SchemaName + (_postgres ? ".authenticationchallenges" : ".[AuthenticationChallenges]");
		}

		public async Task InsertAsync(AuthenticationChallenge challenge, CancellationToken cancellationToken = default)
		{
			await using var connection = _connections.Create();
			await connection.OpenAsync(cancellationToken);
			await connection.ExecuteAsync(new CommandDefinition(
				$@"INSERT INTO {_table} (AuthenticationChallengeId, UserId, Purpose, ClientApplication, RpId, ParentKind, ParentId,
					DepartmentId, AuthenticationGeneration, LockVersion, OptionsJson, CreatedOnUtc, ExpiresOnUtc, Attempts, MaxAttempts, State)
				VALUES (@AuthenticationChallengeId, @UserId, @Purpose, @ClientApplication, @RpId, @ParentKind, @ParentId,
					@DepartmentId, @AuthenticationGeneration, @LockVersion, @OptionsJson, @CreatedOnUtc, @ExpiresOnUtc, 0, @MaxAttempts, {Pending})",
				new
				{
					challenge.AuthenticationChallengeId,
					challenge.UserId,
					challenge.Purpose,
					challenge.ClientApplication,
					challenge.RpId,
					challenge.ParentKind,
					challenge.ParentId,
					challenge.DepartmentId,
					challenge.AuthenticationGeneration,
					challenge.LockVersion,
					challenge.OptionsJson,
					CreatedOnUtc = Timestamp(challenge.CreatedOnUtc),
					ExpiresOnUtc = Timestamp(challenge.ExpiresOnUtc),
					challenge.MaxAttempts
				}, cancellationToken: cancellationToken));
		}

		public async Task<AuthenticationChallenge> GetAsync(string challengeId, CancellationToken cancellationToken = default)
		{
			await using var connection = _connections.Create();
			await connection.OpenAsync(cancellationToken);
			return await connection.QuerySingleOrDefaultAsync<AuthenticationChallenge>(new CommandDefinition(
				$@"SELECT AuthenticationChallengeId, UserId, Purpose, ClientApplication, RpId, ParentKind, ParentId, DepartmentId,
					AuthenticationGeneration, LockVersion, OptionsJson, CreatedOnUtc, ExpiresOnUtc, Attempts, MaxAttempts, State, ConsumedOnUtc
				FROM {_table} WHERE AuthenticationChallengeId = @Id",
				new { Id = challengeId }, cancellationToken: cancellationToken));
		}

		public async Task<int> CountPendingForUserAsync(string userId, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			await using var connection = _connections.Create();
			await connection.OpenAsync(cancellationToken);
			return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
				$"SELECT COUNT(*) FROM {_table} WHERE UserId = @UserId AND State = {Pending} AND ExpiresOnUtc > @Now",
				new { UserId = userId, Now = Timestamp(utcNow) }, cancellationToken: cancellationToken));
		}

		public async Task<bool> TryConsumeAsync(string challengeId, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			await using var connection = _connections.Create();
			await connection.OpenAsync(cancellationToken);
			return await connection.ExecuteAsync(new CommandDefinition(
				$@"UPDATE {_table} SET State = {Consumed}, ConsumedOnUtc = @Now
				WHERE AuthenticationChallengeId = @Id AND State = {Pending} AND ExpiresOnUtc > @Now AND Attempts < MaxAttempts",
				new { Id = challengeId, Now = Timestamp(utcNow) }, cancellationToken: cancellationToken)) == 1;
		}

		public async Task RecordFailedAttemptAsync(string challengeId, CancellationToken cancellationToken = default)
		{
			// Both engines evaluate every SET expression against the pre-update row, so the limit check sees the old count.
			await using var connection = _connections.Create();
			await connection.OpenAsync(cancellationToken);
			await connection.ExecuteAsync(new CommandDefinition(
				$@"UPDATE {_table} SET Attempts = Attempts + 1,
					State = CASE WHEN Attempts + 1 >= MaxAttempts THEN {Exhausted} ELSE State END
				WHERE AuthenticationChallengeId = @Id AND State = {Pending}",
				new { Id = challengeId }, cancellationToken: cancellationToken));
		}

		public async Task<int> CancelPendingForUserAsync(string userId, CancellationToken cancellationToken = default)
		{
			await using var connection = _connections.Create();
			await connection.OpenAsync(cancellationToken);
			return await connection.ExecuteAsync(new CommandDefinition(
				$"UPDATE {_table} SET State = {Canceled} WHERE UserId = @UserId AND State = {Pending}",
				new { UserId = userId }, cancellationToken: cancellationToken));
		}

		public async Task<int> PurgeExpiredBeforeAsync(DateTime utcCutoff, CancellationToken cancellationToken = default)
		{
			await using var connection = _connections.Create();
			await connection.OpenAsync(cancellationToken);
			return await connection.ExecuteAsync(new CommandDefinition(
				$"DELETE FROM {_table} WHERE ExpiresOnUtc < @Cutoff",
				new { Cutoff = Timestamp(utcCutoff) }, cancellationToken: cancellationToken));
		}

		// Npgsql refuses Kind=Utc for "timestamp without time zone"; SQL Server ignores Kind.
		private DateTime Timestamp(DateTime utc) => _postgres ? DateTime.SpecifyKind(utc, DateTimeKind.Unspecified) : utc;
	}
}
