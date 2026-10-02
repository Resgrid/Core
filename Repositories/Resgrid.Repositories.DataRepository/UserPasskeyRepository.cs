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
	/// Registered passkeys (workbook section 8.3): every change is one guarded statement on its own connection whose
	/// success is the rows it changed. Registration inserts only when the RP does not already hold the credential id
	/// (PostgreSQL ON CONFLICT DO NOTHING; SQL Server MERGE with HOLDLOCK), so two nodes can never attach one credential to
	/// two users. A use is recorded only if the signature counter is still the one the assertion was verified against.
	/// </summary>
	public sealed class UserPasskeyRepository : IUserPasskeyRepository
	{
		private const string Columns = @"UserPasskeyId, UserId, ClientApplication, RpId, CredentialId, CredentialIdHash, PublicKey, Algorithm,
			UserHandle, SignCount, IsBackupEligible, IsBackedUp, Transports, Aaguid, AttestationFormat, DisplayName, CreatedOnUtc,
			RegistrationPlatform, RegistrationInstallation, RegistrationUserAgentFamily, RegistrationAttachment, RegisteredInSharedMode,
			LastUsedOnUtc, LastUsedClientApplication, LastUsedInstallation, LastUsedInSharedMode, ApprovalEnabled, RevokedOnUtc,
			RevocationReason, RevokedByUserId, StateVersion";

		private const string InsertValues = @"@UserPasskeyId, @UserId, @ClientApplication, @RpId, @CredentialId, @CredentialIdHash, @PublicKey,
			@Algorithm, @UserHandle, @SignCount, @IsBackupEligible, @IsBackedUp, @Transports, @Aaguid, @AttestationFormat, @DisplayName,
			@CreatedOnUtc, @RegistrationPlatform, @RegistrationInstallation, @RegistrationUserAgentFamily, @RegistrationAttachment,
			@RegisteredInSharedMode, NULL, NULL, NULL, @False, @False, NULL, NULL, NULL, 1";

		private readonly IConnectionProvider _connections;
		private readonly bool _postgres;
		private readonly string _table;

		public UserPasskeyRepository(IConnectionProvider connections, SqlConfiguration configuration)
		{
			_connections = connections;
			_postgres = DataConfig.DatabaseType == DatabaseTypes.Postgres;
			_table = configuration.SchemaName + (_postgres ? ".userpasskeys" : ".[UserPasskeys]");
		}

		public async Task<bool> TryInsertAsync(UserPasskey passkey, CancellationToken cancellationToken = default)
		{
			var sql = _postgres
				? $@"INSERT INTO {_table} ({Columns}) VALUES ({InsertValues})
					ON CONFLICT (rpid, credentialidhash) DO NOTHING"
				: $@"MERGE {_table} WITH (HOLDLOCK) AS target
					USING (SELECT @RpId AS RpId, @CredentialIdHash AS CredentialIdHash) AS source
						ON target.RpId = source.RpId AND target.CredentialIdHash = source.CredentialIdHash
					WHEN NOT MATCHED THEN
						INSERT ({Columns}) VALUES ({InsertValues});";

			await using var connection = _connections.Create();
			await connection.OpenAsync(cancellationToken);
			return await connection.ExecuteAsync(new CommandDefinition(sql, new
			{
				passkey.UserPasskeyId,
				passkey.UserId,
				passkey.ClientApplication,
				passkey.RpId,
				passkey.CredentialId,
				passkey.CredentialIdHash,
				passkey.PublicKey,
				passkey.Algorithm,
				passkey.UserHandle,
				passkey.SignCount,
				passkey.IsBackupEligible,
				passkey.IsBackedUp,
				passkey.Transports,
				passkey.Aaguid,
				passkey.AttestationFormat,
				passkey.DisplayName,
				CreatedOnUtc = Timestamp(passkey.CreatedOnUtc),
				passkey.RegistrationPlatform,
				passkey.RegistrationInstallation,
				passkey.RegistrationUserAgentFamily,
				passkey.RegistrationAttachment,
				passkey.RegisteredInSharedMode,
				False = false
			}, cancellationToken: cancellationToken)) == 1;
		}

		public async Task<UserPasskey> GetAsync(string userPasskeyId, CancellationToken cancellationToken = default)
		{
			await using var connection = _connections.Create();
			await connection.OpenAsync(cancellationToken);
			return await connection.QuerySingleOrDefaultAsync<UserPasskey>(new CommandDefinition(
				$"SELECT {Columns} FROM {_table} WHERE UserPasskeyId = @Id",
				new { Id = userPasskeyId }, cancellationToken: cancellationToken));
		}

		public async Task<UserPasskey> GetActiveByCredentialAsync(string rpId, byte[] credentialIdHash, CancellationToken cancellationToken = default)
		{
			await using var connection = _connections.Create();
			await connection.OpenAsync(cancellationToken);
			return await connection.QuerySingleOrDefaultAsync<UserPasskey>(new CommandDefinition(
				$"SELECT {Columns} FROM {_table} WHERE RpId = @RpId AND CredentialIdHash = @Hash AND RevokedOnUtc IS NULL",
				new { RpId = rpId, Hash = credentialIdHash }, cancellationToken: cancellationToken));
		}

		public async Task<IReadOnlyList<UserPasskey>> GetActiveForUserAsync(string userId, CancellationToken cancellationToken = default)
		{
			await using var connection = _connections.Create();
			await connection.OpenAsync(cancellationToken);
			var rows = await connection.QueryAsync<UserPasskey>(new CommandDefinition(
				$"SELECT {Columns} FROM {_table} WHERE UserId = @UserId AND RevokedOnUtc IS NULL ORDER BY ClientApplication, CreatedOnUtc",
				new { UserId = userId }, cancellationToken: cancellationToken));
			return rows.ToList();
		}

		public async Task<int> CountActiveForUserAsync(string userId, CancellationToken cancellationToken = default)
		{
			await using var connection = _connections.Create();
			await connection.OpenAsync(cancellationToken);
			return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
				$"SELECT COUNT(*) FROM {_table} WHERE UserId = @UserId AND RevokedOnUtc IS NULL",
				new { UserId = userId }, cancellationToken: cancellationToken));
		}

		public async Task<byte[]> GetUserHandleAsync(string userId, string rpId, CancellationToken cancellationToken = default)
		{
			var sql = _postgres
				? $"SELECT UserHandle FROM {_table} WHERE UserId = @UserId AND RpId = @RpId ORDER BY CreatedOnUtc LIMIT 1"
				: $"SELECT TOP 1 UserHandle FROM {_table} WHERE UserId = @UserId AND RpId = @RpId ORDER BY CreatedOnUtc";

			await using var connection = _connections.Create();
			await connection.OpenAsync(cancellationToken);
			return await connection.QuerySingleOrDefaultAsync<byte[]>(new CommandDefinition(sql,
				new { UserId = userId, RpId = rpId }, cancellationToken: cancellationToken));
		}

		public async Task<bool> TryRenameAsync(string userPasskeyId, string userId, string displayName, CancellationToken cancellationToken = default)
		{
			await using var connection = _connections.Create();
			await connection.OpenAsync(cancellationToken);
			return await connection.ExecuteAsync(new CommandDefinition(
				$@"UPDATE {_table} SET DisplayName = @DisplayName
				WHERE UserPasskeyId = @Id AND UserId = @UserId AND RevokedOnUtc IS NULL",
				new { Id = userPasskeyId, UserId = userId, DisplayName = displayName }, cancellationToken: cancellationToken)) == 1;
		}

		public async Task<bool> TryRevokeAsync(string userPasskeyId, string userId, PasskeyRevocationReason reason, string actorUserId, DateTime utcNow,
			CancellationToken cancellationToken = default)
		{
			await using var connection = _connections.Create();
			await connection.OpenAsync(cancellationToken);
			return await connection.ExecuteAsync(new CommandDefinition(
				$@"UPDATE {_table} SET RevokedOnUtc = @Now, RevocationReason = @Reason, RevokedByUserId = @Actor,
					ApprovalEnabled = @False, StateVersion = StateVersion + 1
				WHERE UserPasskeyId = @Id AND UserId = @UserId AND RevokedOnUtc IS NULL",
				new { Id = userPasskeyId, UserId = userId, Reason = (int)reason, Actor = actorUserId, Now = Timestamp(utcNow), False = false },
				cancellationToken: cancellationToken)) == 1;
		}

		public async Task<IReadOnlyList<string>> RevokeAllForClientAsync(string userId, int clientApplication, PasskeyRevocationReason reason,
			string actorUserId, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			const string Set = @"SET RevokedOnUtc = @Now, RevocationReason = @Reason, RevokedByUserId = @Actor,
					ApprovalEnabled = @False, StateVersion = StateVersion + 1";
			const string Where = "WHERE UserId = @UserId AND ClientApplication = @Client AND RevokedOnUtc IS NULL";

			// One statement reports exactly the rows it revoked, so the caller retires evidence for those and no others.
			var sql = _postgres
				? $"UPDATE {_table} {Set} {Where} RETURNING UserPasskeyId"
				: $"UPDATE {_table} {Set} OUTPUT inserted.UserPasskeyId {Where}";

			await using var connection = _connections.Create();
			await connection.OpenAsync(cancellationToken);
			var ids = await connection.QueryAsync<string>(new CommandDefinition(sql,
				new { UserId = userId, Client = clientApplication, Reason = (int)reason, Actor = actorUserId, Now = Timestamp(utcNow), False = false },
				cancellationToken: cancellationToken));
			return ids.ToList();
		}

		public async Task<bool> TryRecordUseAsync(string userPasskeyId, long expectedSignCount, long newSignCount, bool isBackedUp,
			int clientApplication, string installation, bool sharedMode, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			await using var connection = _connections.Create();
			await connection.OpenAsync(cancellationToken);
			return await connection.ExecuteAsync(new CommandDefinition(
				$@"UPDATE {_table} SET SignCount = @NewSignCount, IsBackedUp = @IsBackedUp, LastUsedOnUtc = @Now,
					LastUsedClientApplication = @Client, LastUsedInstallation = @Installation, LastUsedInSharedMode = @SharedMode
				WHERE UserPasskeyId = @Id AND SignCount = @ExpectedSignCount AND RevokedOnUtc IS NULL",
				new
				{
					Id = userPasskeyId,
					ExpectedSignCount = expectedSignCount,
					NewSignCount = newSignCount,
					IsBackedUp = isBackedUp,
					Client = clientApplication,
					Installation = installation,
					SharedMode = sharedMode,
					Now = Timestamp(utcNow)
				}, cancellationToken: cancellationToken)) == 1;
		}

		public async Task<bool> TrySetApprovalEnabledAsync(string userPasskeyId, string userId, bool enabled, CancellationToken cancellationToken = default)
		{
			// Changing what a credential may approve advances its state version, so anything derived from the old
			// setting can be told apart (plan section 7.9).
			await using var connection = _connections.Create();
			await connection.OpenAsync(cancellationToken);
			return await connection.ExecuteAsync(new CommandDefinition(
				$@"UPDATE {_table} SET ApprovalEnabled = @Enabled, StateVersion = StateVersion + 1
				WHERE UserPasskeyId = @Id AND UserId = @UserId AND RevokedOnUtc IS NULL",
				new { Id = userPasskeyId, UserId = userId, Enabled = enabled }, cancellationToken: cancellationToken)) == 1;
		}

		// Npgsql refuses Kind=Utc for "timestamp without time zone"; SQL Server ignores Kind.
		private DateTime Timestamp(DateTime utc) => _postgres ? DateTime.SpecifyKind(utc, DateTimeKind.Unspecified) : utc;
	}
}
