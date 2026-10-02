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
	/// Restricted login MFA transactions (workbook section 8.3): every state change is one guarded UPDATE whose success is
	/// exactly one row, on its own connection, so a transaction completes once and its completion code redeems once
	/// however many nodes race for it.
	/// </summary>
	public sealed class MfaLoginTransactionRepository : IMfaLoginTransactionRepository
	{
		private const int Pending = (int)MfaLoginTransactionState.Pending;
		private const int Completed = (int)MfaLoginTransactionState.Completed;
		private const int Redeemed = (int)MfaLoginTransactionState.Redeemed;
		private const int Exhausted = (int)MfaLoginTransactionState.Exhausted;

		private const string Columns = @"MfaLoginTransactionId, SecretHash, UserId, DepartmentId, ClientApplication, ClientId, FirstFactorMethod,
			FirstFactorVerifiedOnUtc, DepartmentSsoConfigId, AuthenticationGeneration, MfaPolicyVersion, Scopes, CreatedOnUtc, ExpiresOnUtc,
			Attempts, MaxAttempts, State, CompletionMethod, CompletionFactorReference, CompletionVerifiedOnUtc, IsRecovery, CompletionCodeHash,
			CompletionExpiresOnUtc, RedeemedOnUtc, SharedMode, InstallationLabel";

		private readonly IConnectionProvider _connections;
		private readonly bool _postgres;
		private readonly string _table;

		public MfaLoginTransactionRepository(IConnectionProvider connections, SqlConfiguration configuration)
		{
			_connections = connections;
			_postgres = DataConfig.DatabaseType == DatabaseTypes.Postgres;
			_table = configuration.SchemaName + (_postgres ? ".mfalogintransactions" : ".[MfaLoginTransactions]");
		}

		public async Task InsertAsync(MfaLoginTransaction transaction, CancellationToken cancellationToken = default)
		{
			await using var connection = _connections.Create();
			await connection.OpenAsync(cancellationToken);
			await connection.ExecuteAsync(new CommandDefinition(
				$@"INSERT INTO {_table} (MfaLoginTransactionId, SecretHash, UserId, DepartmentId, ClientApplication, ClientId, FirstFactorMethod,
					FirstFactorVerifiedOnUtc, DepartmentSsoConfigId, AuthenticationGeneration, MfaPolicyVersion, Scopes, CreatedOnUtc, ExpiresOnUtc,
					Attempts, MaxAttempts, State, IsRecovery, SharedMode, InstallationLabel)
				VALUES (@MfaLoginTransactionId, @SecretHash, @UserId, @DepartmentId, @ClientApplication, @ClientId, @FirstFactorMethod,
					@FirstFactorVerifiedOnUtc, @DepartmentSsoConfigId, @AuthenticationGeneration, @MfaPolicyVersion, @Scopes, @CreatedOnUtc, @ExpiresOnUtc,
					0, @MaxAttempts, {Pending}, @False, @SharedMode, @InstallationLabel)",
				new
				{
					transaction.MfaLoginTransactionId,
					transaction.SecretHash,
					transaction.UserId,
					transaction.DepartmentId,
					transaction.ClientApplication,
					transaction.ClientId,
					transaction.FirstFactorMethod,
					FirstFactorVerifiedOnUtc = Timestamp(transaction.FirstFactorVerifiedOnUtc),
					transaction.DepartmentSsoConfigId,
					transaction.AuthenticationGeneration,
					transaction.MfaPolicyVersion,
					transaction.Scopes,
					CreatedOnUtc = Timestamp(transaction.CreatedOnUtc),
					ExpiresOnUtc = Timestamp(transaction.ExpiresOnUtc),
					transaction.MaxAttempts,
					False = false,
					transaction.SharedMode,
					transaction.InstallationLabel
				}, cancellationToken: cancellationToken));
		}

		public async Task<MfaLoginTransaction> GetBySecretHashAsync(byte[] secretHash, CancellationToken cancellationToken = default)
		{
			await using var connection = _connections.Create();
			await connection.OpenAsync(cancellationToken);
			return await connection.QuerySingleOrDefaultAsync<MfaLoginTransaction>(new CommandDefinition(
				$"SELECT {Columns} FROM {_table} WHERE SecretHash = @Hash",
				new { Hash = secretHash }, cancellationToken: cancellationToken));
		}

		public async Task RecordFailedAttemptAsync(string transactionId, CancellationToken cancellationToken = default)
		{
			// Both engines evaluate every SET expression against the pre-update row, so the limit check sees the old count.
			await using var connection = _connections.Create();
			await connection.OpenAsync(cancellationToken);
			await connection.ExecuteAsync(new CommandDefinition(
				$@"UPDATE {_table} SET Attempts = Attempts + 1,
					State = CASE WHEN Attempts + 1 >= MaxAttempts THEN {Exhausted} ELSE State END
				WHERE MfaLoginTransactionId = @Id AND State = {Pending}",
				new { Id = transactionId }, cancellationToken: cancellationToken));
		}

		public async Task<bool> TryCompleteAsync(string transactionId, int? method, string factorReference, DateTime? verifiedOnUtc, bool isRecovery,
			byte[] completionCodeHash, DateTime completionExpiresOnUtc, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			await using var connection = _connections.Create();
			await connection.OpenAsync(cancellationToken);
			return await connection.ExecuteAsync(new CommandDefinition(
				$@"UPDATE {_table} SET State = {Completed}, CompletionMethod = @Method, CompletionFactorReference = @FactorReference,
					CompletionVerifiedOnUtc = @VerifiedOn, IsRecovery = @IsRecovery, CompletionCodeHash = @CodeHash, CompletionExpiresOnUtc = @CodeExpires
				WHERE MfaLoginTransactionId = @Id AND State = {Pending} AND ExpiresOnUtc > @Now AND Attempts < MaxAttempts",
				new
				{
					Id = transactionId,
					Method = method,
					FactorReference = factorReference,
					VerifiedOn = verifiedOnUtc == null ? (DateTime?)null : Timestamp(verifiedOnUtc.Value),
					IsRecovery = isRecovery,
					CodeHash = completionCodeHash,
					CodeExpires = Timestamp(completionExpiresOnUtc),
					Now = Timestamp(utcNow)
				}, cancellationToken: cancellationToken)) == 1;
		}

		public async Task<bool> TryRedeemAsync(string transactionId, byte[] completionCodeHash, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			await using var connection = _connections.Create();
			await connection.OpenAsync(cancellationToken);
			return await connection.ExecuteAsync(new CommandDefinition(
				$@"UPDATE {_table} SET State = {Redeemed}, RedeemedOnUtc = @Now
				WHERE MfaLoginTransactionId = @Id AND State = {Completed} AND CompletionCodeHash = @CodeHash AND CompletionExpiresOnUtc > @Now",
				new { Id = transactionId, CodeHash = completionCodeHash, Now = Timestamp(utcNow) }, cancellationToken: cancellationToken)) == 1;
		}

		public async Task<bool> TryAbandonAsync(string transactionId, CancellationToken cancellationToken = default)
		{
			await using var connection = _connections.Create();
			await connection.OpenAsync(cancellationToken);
			return await connection.ExecuteAsync(new CommandDefinition(
				$"UPDATE {_table} SET State = {Exhausted} WHERE MfaLoginTransactionId = @Id AND State IN ({Pending}, {Completed})",
				new { Id = transactionId }, cancellationToken: cancellationToken)) == 1;
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
