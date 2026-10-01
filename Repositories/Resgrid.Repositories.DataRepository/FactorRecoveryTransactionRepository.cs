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
	/// Restricted factor recovery transactions (plan section 5.4; workbook section 8.3): every state change is one guarded
	/// UPDATE whose success is exactly one row, on its own connection.
	/// </summary>
	public sealed class FactorRecoveryTransactionRepository : IFactorRecoveryTransactionRepository
	{
		private const int Pending = (int)FactorRecoveryState.Pending;
		private const int Completed = (int)FactorRecoveryState.Completed;
		private const int Canceled = (int)FactorRecoveryState.Canceled;
		private const int Exhausted = (int)FactorRecoveryState.Exhausted;

		private const string Columns = @"FactorRecoveryTransactionId, SecretHash, UserId, ClientApplication, FirstFactorMethod, FirstFactorVerifiedOnUtc,
			DepartmentSsoConfigId, DepartmentId, AuthenticationGeneration, CreatedOnUtc, ExpiresOnUtc, Attempts, MaxAttempts, State, CompletedOnUtc";

		private readonly IConnectionProvider _connections;
		private readonly bool _postgres;
		private readonly string _table;

		public FactorRecoveryTransactionRepository(IConnectionProvider connections, SqlConfiguration configuration)
		{
			_connections = connections;
			_postgres = DataConfig.DatabaseType == DatabaseTypes.Postgres;
			_table = configuration.SchemaName + (_postgres ? ".factorrecoverytransactions" : ".[FactorRecoveryTransactions]");
		}

		public async Task InsertAsync(FactorRecoveryTransaction transaction, CancellationToken cancellationToken = default)
		{
			await using var connection = _connections.Create();
			await connection.OpenAsync(cancellationToken);
			await connection.ExecuteAsync(new CommandDefinition(
				$@"INSERT INTO {_table} (FactorRecoveryTransactionId, SecretHash, UserId, ClientApplication, FirstFactorMethod, FirstFactorVerifiedOnUtc,
					DepartmentSsoConfigId, DepartmentId, AuthenticationGeneration, CreatedOnUtc, ExpiresOnUtc, Attempts, MaxAttempts, State)
				VALUES (@FactorRecoveryTransactionId, @SecretHash, @UserId, @ClientApplication, @FirstFactorMethod, @FirstFactorVerifiedOnUtc,
					@DepartmentSsoConfigId, @DepartmentId, @AuthenticationGeneration, @CreatedOnUtc, @ExpiresOnUtc, 0, @MaxAttempts, {Pending})",
				new
				{
					transaction.FactorRecoveryTransactionId,
					transaction.SecretHash,
					transaction.UserId,
					transaction.ClientApplication,
					transaction.FirstFactorMethod,
					FirstFactorVerifiedOnUtc = Timestamp(transaction.FirstFactorVerifiedOnUtc),
					transaction.DepartmentSsoConfigId,
					transaction.DepartmentId,
					transaction.AuthenticationGeneration,
					CreatedOnUtc = Timestamp(transaction.CreatedOnUtc),
					ExpiresOnUtc = Timestamp(transaction.ExpiresOnUtc),
					transaction.MaxAttempts
				}, cancellationToken: cancellationToken));
		}

		public async Task<FactorRecoveryTransaction> GetBySecretHashAsync(byte[] secretHash, CancellationToken cancellationToken = default)
		{
			await using var connection = _connections.Create();
			await connection.OpenAsync(cancellationToken);
			return await connection.QuerySingleOrDefaultAsync<FactorRecoveryTransaction>(new CommandDefinition(
				$"SELECT {Columns} FROM {_table} WHERE SecretHash = @Hash", new { Hash = secretHash }, cancellationToken: cancellationToken));
		}

		public async Task RecordFailedAttemptAsync(string transactionId, CancellationToken cancellationToken = default)
		{
			// Both engines evaluate every SET expression against the pre-update row, so the limit check sees the old count.
			await using var connection = _connections.Create();
			await connection.OpenAsync(cancellationToken);
			await connection.ExecuteAsync(new CommandDefinition(
				$@"UPDATE {_table} SET Attempts = Attempts + 1, State = CASE WHEN Attempts + 1 >= MaxAttempts THEN {Exhausted} ELSE State END
				WHERE FactorRecoveryTransactionId = @Id AND State = {Pending}",
				new { Id = transactionId }, cancellationToken: cancellationToken));
		}

		public async Task<bool> TryCompleteAsync(string transactionId, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			await using var connection = _connections.Create();
			await connection.OpenAsync(cancellationToken);
			return await connection.ExecuteAsync(new CommandDefinition(
				$@"UPDATE {_table} SET State = {Completed}, CompletedOnUtc = @Now
				WHERE FactorRecoveryTransactionId = @Id AND State = {Pending} AND ExpiresOnUtc > @Now AND Attempts < MaxAttempts",
				new { Id = transactionId, Now = Timestamp(utcNow) }, cancellationToken: cancellationToken)) == 1;
		}

		public async Task<bool> TryCancelAsync(string transactionId, CancellationToken cancellationToken = default)
		{
			await using var connection = _connections.Create();
			await connection.OpenAsync(cancellationToken);
			return await connection.ExecuteAsync(new CommandDefinition(
				$"UPDATE {_table} SET State = {Canceled} WHERE FactorRecoveryTransactionId = @Id AND State = {Pending}",
				new { Id = transactionId }, cancellationToken: cancellationToken)) == 1;
		}

		public async Task<int> PurgeExpiredBeforeAsync(DateTime utcCutoff, CancellationToken cancellationToken = default)
		{
			await using var connection = _connections.Create();
			await connection.OpenAsync(cancellationToken);
			return await connection.ExecuteAsync(new CommandDefinition(
				$"DELETE FROM {_table} WHERE ExpiresOnUtc < @Cutoff", new { Cutoff = Timestamp(utcCutoff) }, cancellationToken: cancellationToken));
		}

		public async Task<int> DeleteForUserAsync(string userId, CancellationToken cancellationToken = default)
		{
			await using var connection = _connections.Create();
			await connection.OpenAsync(cancellationToken);
			return await connection.ExecuteAsync(new CommandDefinition(
				$"DELETE FROM {_table} WHERE UserId = @UserId", new { UserId = userId }, cancellationToken: cancellationToken));
		}

		// Npgsql refuses Kind=Utc for "timestamp without time zone"; SQL Server ignores Kind.
		private DateTime Timestamp(DateTime utc) => _postgres ? DateTime.SpecifyKind(utc, DateTimeKind.Unspecified) : utc;
	}
}
