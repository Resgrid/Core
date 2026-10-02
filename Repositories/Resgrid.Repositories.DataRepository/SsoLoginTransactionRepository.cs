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
	/// Brokered SSO transactions (workbook section 8.3): every state change is one guarded UPDATE whose success is exactly
	/// one row, on its own connection, so an IdP result is accepted once and its one-time code redeemed once.
	/// </summary>
	public sealed class SsoLoginTransactionRepository : ISsoLoginTransactionRepository
	{
		private const int Pending = (int)SsoLoginTransactionState.Pending;
		private const int Authenticated = (int)SsoLoginTransactionState.Authenticated;
		private const int Redeemed = (int)SsoLoginTransactionState.Redeemed;
		private const int Failed = (int)SsoLoginTransactionState.Failed;

		private const string Columns = @"SsoLoginTransactionId, StateHash, Purpose, DepartmentId, DepartmentSsoConfigId, ProviderType, ClientApplication,
			Platform, ReturnTarget, ClientState, CodeChallenge, NonceHash, EncryptedIdpCodeVerifier, SamlRequestId, SessionId, ExpectedUserId,
			AuthenticationGeneration, CreatedOnUtc, ExpiresOnUtc, State, UserId, AuthenticatedOnUtc, CodeHash, CodeExpiresOnUtc, RedeemedOnUtc,
			FailureCode, Operation, LoginTransactionId, FederatedMappingVersion, FederatedMfaValue, SharedInstallation";

		private readonly IConnectionProvider _connections;
		private readonly bool _postgres;
		private readonly string _table;

		public SsoLoginTransactionRepository(IConnectionProvider connections, SqlConfiguration configuration)
		{
			_connections = connections;
			_postgres = DataConfig.DatabaseType == DatabaseTypes.Postgres;
			_table = configuration.SchemaName + (_postgres ? ".ssologintransactions" : ".[SsoLoginTransactions]");
		}

		public async Task InsertAsync(SsoLoginTransaction transaction, CancellationToken cancellationToken = default)
		{
			await using var connection = _connections.Create();
			await connection.OpenAsync(cancellationToken);
			await connection.ExecuteAsync(new CommandDefinition(
				$@"INSERT INTO {_table} (SsoLoginTransactionId, StateHash, Purpose, DepartmentId, DepartmentSsoConfigId, ProviderType, ClientApplication,
					Platform, ReturnTarget, ClientState, CodeChallenge, NonceHash, EncryptedIdpCodeVerifier, SamlRequestId, SessionId, ExpectedUserId,
					AuthenticationGeneration, CreatedOnUtc, ExpiresOnUtc, State, Operation, LoginTransactionId, FederatedMappingVersion, SharedInstallation)
				VALUES (@SsoLoginTransactionId, @StateHash, @Purpose, @DepartmentId, @DepartmentSsoConfigId, @ProviderType, @ClientApplication,
					@Platform, @ReturnTarget, @ClientState, @CodeChallenge, @NonceHash, @EncryptedIdpCodeVerifier, @SamlRequestId, @SessionId, @ExpectedUserId,
					@AuthenticationGeneration, @CreatedOnUtc, @ExpiresOnUtc, {Pending}, @Operation, @LoginTransactionId, @FederatedMappingVersion,
					@SharedInstallation)",
				new
				{
					transaction.SsoLoginTransactionId,
					transaction.StateHash,
					transaction.Purpose,
					transaction.DepartmentId,
					transaction.DepartmentSsoConfigId,
					transaction.ProviderType,
					transaction.ClientApplication,
					transaction.Platform,
					transaction.ReturnTarget,
					transaction.ClientState,
					transaction.CodeChallenge,
					transaction.NonceHash,
					transaction.EncryptedIdpCodeVerifier,
					transaction.SamlRequestId,
					transaction.SessionId,
					transaction.ExpectedUserId,
					transaction.AuthenticationGeneration,
					CreatedOnUtc = Timestamp(transaction.CreatedOnUtc),
					ExpiresOnUtc = Timestamp(transaction.ExpiresOnUtc),
					transaction.Operation,
					transaction.LoginTransactionId,
					transaction.FederatedMappingVersion,
					transaction.SharedInstallation
				}, cancellationToken: cancellationToken));
		}

		public async Task<SsoLoginTransaction> GetAsync(string transactionId, CancellationToken cancellationToken = default)
		{
			await using var connection = _connections.Create();
			await connection.OpenAsync(cancellationToken);
			return await connection.QuerySingleOrDefaultAsync<SsoLoginTransaction>(new CommandDefinition(
				$"SELECT {Columns} FROM {_table} WHERE SsoLoginTransactionId = @Id",
				new { Id = transactionId }, cancellationToken: cancellationToken));
		}

		public async Task<SsoLoginTransaction> GetByStateHashAsync(byte[] stateHash, CancellationToken cancellationToken = default)
		{
			await using var connection = _connections.Create();
			await connection.OpenAsync(cancellationToken);
			return await connection.QuerySingleOrDefaultAsync<SsoLoginTransaction>(new CommandDefinition(
				$"SELECT {Columns} FROM {_table} WHERE StateHash = @Hash",
				new { Hash = stateHash }, cancellationToken: cancellationToken));
		}

		public async Task<bool> TryAuthenticateAsync(string transactionId, string userId, DateTime authenticatedOnUtc, string federatedMfaValue,
			byte[] codeHash, DateTime codeExpiresOnUtc, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			await using var connection = _connections.Create();
			await connection.OpenAsync(cancellationToken);
			return await connection.ExecuteAsync(new CommandDefinition(
				$@"UPDATE {_table} SET State = {Authenticated}, UserId = @UserId, AuthenticatedOnUtc = @AuthenticatedOn, CodeHash = @CodeHash,
					CodeExpiresOnUtc = @CodeExpires, FederatedMfaValue = @FederatedMfaValue
				WHERE SsoLoginTransactionId = @Id AND State = {Pending} AND ExpiresOnUtc > @Now",
				new
				{
					Id = transactionId,
					UserId = userId,
					FederatedMfaValue = federatedMfaValue,
					AuthenticatedOn = Timestamp(authenticatedOnUtc),
					CodeHash = codeHash,
					CodeExpires = Timestamp(codeExpiresOnUtc),
					Now = Timestamp(utcNow)
				}, cancellationToken: cancellationToken)) == 1;
		}

		public async Task<bool> TryFailAsync(string transactionId, string failureCode, CancellationToken cancellationToken = default)
		{
			await using var connection = _connections.Create();
			await connection.OpenAsync(cancellationToken);
			return await connection.ExecuteAsync(new CommandDefinition(
				$"UPDATE {_table} SET State = {Failed}, FailureCode = @FailureCode WHERE SsoLoginTransactionId = @Id AND State = {Pending}",
				new { Id = transactionId, FailureCode = failureCode }, cancellationToken: cancellationToken)) == 1;
		}

		public async Task<bool> TryRedeemAsync(string transactionId, byte[] codeHash, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			await using var connection = _connections.Create();
			await connection.OpenAsync(cancellationToken);
			return await connection.ExecuteAsync(new CommandDefinition(
				$@"UPDATE {_table} SET State = {Redeemed}, RedeemedOnUtc = @Now
				WHERE SsoLoginTransactionId = @Id AND State = {Authenticated} AND CodeHash = @CodeHash AND CodeExpiresOnUtc > @Now",
				new { Id = transactionId, CodeHash = codeHash, Now = Timestamp(utcNow) }, cancellationToken: cancellationToken)) == 1;
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
