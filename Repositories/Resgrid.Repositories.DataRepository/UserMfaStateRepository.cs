using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Resgrid.Config;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository
{
	/// <summary>
	/// Compare-and-set MFA factor state (workbook section 8.3). Each call opens its own connection so a consume never
	/// shares an ambient unit of work, and every consume is a single guarded statement that succeeds on exactly one row.
	/// </summary>
	public sealed class UserMfaStateRepository : IUserMfaStateRepository
	{
		// The legacy ";"-joined plaintext recovery codes written by IdentityUserStore before M0243.
		internal const string LegacyRecoveryLoginProvider = "[AspNetUserStore]";
		internal const string LegacyRecoveryTokenName = "RecoveryCodes";

		private readonly IConnectionProvider _connections;
		private readonly bool _postgres;
		private readonly string _totp;
		private readonly string _codes;
		private readonly string _preferences;

		public UserMfaStateRepository(IConnectionProvider connections, SqlConfiguration configuration)
		{
			_connections = connections;
			_postgres = DataConfig.DatabaseType == DatabaseTypes.Postgres;
			_totp = configuration.SchemaName + (_postgres ? ".usertotpstates" : ".[UserTotpStates]");
			_codes = configuration.SchemaName + (_postgres ? ".userrecoverycodes" : ".[UserRecoveryCodes]");
			_preferences = configuration.SchemaName + (_postgres ? ".usermfapreferences" : ".[UserMfaPreferences]");
		}

		public async Task<int?> GetPreferredMethodAsync(string userId, CancellationToken cancellationToken = default)
		{
			await using var connection = _connections.Create();
			await connection.OpenAsync(cancellationToken);
			return await connection.QuerySingleOrDefaultAsync<int?>(new CommandDefinition(
				$"SELECT PreferredMethod FROM {_preferences} WHERE UserId = @UserId",
				new { UserId = userId }, cancellationToken: cancellationToken));
		}

		public async Task SetPreferredMethodAsync(string userId, int method, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			// Last writer wins; this is a display default, not an authorization input.
			var sql = _postgres
				? $@"INSERT INTO {_preferences} (userid, preferredmethod, updatedonutc) VALUES (@UserId, @Method, @Now)
					ON CONFLICT (userid) DO UPDATE SET preferredmethod = EXCLUDED.preferredmethod, updatedonutc = EXCLUDED.updatedonutc"
				: $@"MERGE {_preferences} WITH (HOLDLOCK) AS target
					USING (SELECT @UserId AS UserId) AS source ON target.UserId = source.UserId
					WHEN MATCHED THEN UPDATE SET PreferredMethod = @Method, UpdatedOnUtc = @Now
					WHEN NOT MATCHED THEN INSERT (UserId, PreferredMethod, UpdatedOnUtc) VALUES (@UserId, @Method, @Now);";

			await using var connection = _connections.Create();
			await connection.OpenAsync(cancellationToken);
			await connection.ExecuteAsync(new CommandDefinition(sql,
				new { UserId = userId, Method = method, Now = Timestamp(utcNow) }, cancellationToken: cancellationToken));
		}

		public async Task<bool> TryConsumeTotpTimeStepAsync(string userId, long timeStep, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			// One statement on both engines: insert the first step, or advance only to a strictly newer step.
			// Zero rows changed means this step (or a later one) was already accepted.
			var sql = _postgres
				? $@"INSERT INTO {_totp} AS t (userid, lastacceptedtimestep, lastacceptedonutc) VALUES (@UserId, @TimeStep, @Now)
					ON CONFLICT (userid) DO UPDATE SET lastacceptedtimestep = EXCLUDED.lastacceptedtimestep, lastacceptedonutc = EXCLUDED.lastacceptedonutc
					WHERE t.lastacceptedtimestep < EXCLUDED.lastacceptedtimestep"
				: $@"MERGE {_totp} WITH (HOLDLOCK) AS target
					USING (SELECT @UserId AS UserId) AS source ON target.UserId = source.UserId
					WHEN MATCHED AND target.LastAcceptedTimeStep < @TimeStep THEN
						UPDATE SET LastAcceptedTimeStep = @TimeStep, LastAcceptedOnUtc = @Now
					WHEN NOT MATCHED THEN
						INSERT (UserId, LastAcceptedTimeStep, LastAcceptedOnUtc) VALUES (@UserId, @TimeStep, @Now);";

			await using var connection = _connections.Create();
			await connection.OpenAsync(cancellationToken);
			return await connection.ExecuteAsync(new CommandDefinition(sql,
				new { UserId = userId, TimeStep = timeStep, Now = Timestamp(utcNow) }, cancellationToken: cancellationToken)) == 1;
		}

		public async Task<UserTotpState> GetTotpStateAsync(string userId, CancellationToken cancellationToken = default)
		{
			await using var connection = _connections.Create();
			await connection.OpenAsync(cancellationToken);
			return await connection.QuerySingleOrDefaultAsync<UserTotpState>(new CommandDefinition(
				$"SELECT UserId, LastAcceptedTimeStep, LastAcceptedOnUtc, EnrolledOnUtc, EnrolledInSharedMode, EnrolledClientApplication, EnrolledInstallation FROM {_totp} WHERE UserId = @UserId",
				new { UserId = userId }, cancellationToken: cancellationToken));
		}

		public async Task RecordTotpEnrollmentAsync(string userId, DateTime utcNow, TotpEnrollmentContext context, CancellationToken cancellationToken = default)
		{
			var installation = string.IsNullOrWhiteSpace(context?.Installation) ? null : context.Installation.Trim();
			await using var connection = _connections.Create();
			await connection.OpenAsync(cancellationToken);
			await connection.ExecuteAsync(new CommandDefinition(
				$@"UPDATE {_totp} SET EnrolledOnUtc = @Now, EnrolledInSharedMode = @SharedMode, EnrolledClientApplication = @Client,
					EnrolledInstallation = @Installation WHERE UserId = @UserId",
				new
				{
					UserId = userId, Now = Timestamp(utcNow), SharedMode = context?.SharedMode == true, Client = context?.ClientApplication,
					Installation = installation?.Length > 256 ? installation.Substring(0, 256) : installation
				}, cancellationToken: cancellationToken));
		}

		public async Task<int> CountUnusedRecoveryCodesAsync(string userId, CancellationToken cancellationToken = default)
		{
			await using var connection = _connections.Create();
			await connection.OpenAsync(cancellationToken);
			return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
				$"SELECT COUNT(*) FROM {_codes} WHERE UserId = @UserId AND UsedOnUtc IS NULL",
				new { UserId = userId }, cancellationToken: cancellationToken));
		}

		public async Task<bool> TryRedeemRecoveryCodeAsync(string userId, byte[] codeHash, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			await using var connection = _connections.Create();
			await connection.OpenAsync(cancellationToken);
			return await connection.ExecuteAsync(new CommandDefinition(
				$"UPDATE {_codes} SET UsedOnUtc = @Now WHERE UserId = @UserId AND CodeHash = @CodeHash AND UsedOnUtc IS NULL",
				new { UserId = userId, CodeHash = codeHash, Now = Timestamp(utcNow) }, cancellationToken: cancellationToken)) == 1;
		}

		public async Task ReplaceRecoveryCodesAsync(string userId, IReadOnlyCollection<byte[]> codeHashes, int hashVersion, DateTime utcNow,
			CancellationToken cancellationToken = default)
		{
			await using var connection = _connections.Create();
			await connection.OpenAsync(cancellationToken);
			await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

			// Delete the legacy token FIRST. A concurrent ImportLegacyRecoveryCodesAsync holds that row while it inserts the
			// old codes; waiting on it here means the row delete below also removes whatever it imported, so old codes can
			// never survive a regeneration.
			await DeleteLegacyTokenAsync(connection, transaction, userId, null, cancellationToken);
			await connection.ExecuteAsync(new CommandDefinition(
				$"DELETE FROM {_codes} WHERE UserId = @UserId", new { UserId = userId }, transaction, cancellationToken: cancellationToken));
			await InsertCodesAsync(connection, transaction, userId, codeHashes, hashVersion, utcNow, cancellationToken);

			await transaction.CommitAsync(cancellationToken);
		}

		public async Task<bool> ImportLegacyRecoveryCodesAsync(string userId, string legacyTokenValue, IReadOnlyCollection<byte[]> codeHashes,
			int hashVersion, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			await using var connection = _connections.Create();
			await connection.OpenAsync(cancellationToken);
			await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

			// Compare-and-delete on the exact value read: only one request migrates, and a token that changed since it was
			// read (or was already migrated) is left alone.
			if (await DeleteLegacyTokenAsync(connection, transaction, userId, legacyTokenValue, cancellationToken) != 1)
			{
				await transaction.RollbackAsync(cancellationToken);
				return false;
			}

			await InsertCodesAsync(connection, transaction, userId, codeHashes, hashVersion, utcNow, cancellationToken);
			await transaction.CommitAsync(cancellationToken);
			return true;
		}

		private static Task<int> DeleteLegacyTokenAsync(DbConnection connection, DbTransaction transaction, string userId, string expectedValue,
			CancellationToken cancellationToken)
		{
			// Unqualified, unquoted names resolve to dbo.AspNetUserTokens and public.aspnetusertokens, as in IdentityUserRepository.
			var sql = "DELETE FROM AspNetUserTokens WHERE UserId = @UserId AND LoginProvider = @LoginProvider AND Name = @Name"
				+ (expectedValue == null ? string.Empty : " AND Value = @Value");
			return connection.ExecuteAsync(new CommandDefinition(sql, new
			{
				UserId = userId,
				LoginProvider = LegacyRecoveryLoginProvider,
				Name = LegacyRecoveryTokenName,
				Value = expectedValue
			}, transaction, cancellationToken: cancellationToken));
		}

		private Task InsertCodesAsync(DbConnection connection, DbTransaction transaction, string userId, IReadOnlyCollection<byte[]> codeHashes,
			int hashVersion, DateTime utcNow, CancellationToken cancellationToken)
		{
			if (codeHashes == null || codeHashes.Count == 0)
				return Task.CompletedTask;

			var created = Timestamp(utcNow);
			var rows = codeHashes.Distinct(ByteArrayComparer.Instance).Select(hash => new
			{
				Id = Guid.NewGuid().ToString(),
				UserId = userId,
				CodeHash = hash,
				HashVersion = hashVersion,
				Now = created
			}).ToList();

			return connection.ExecuteAsync(new CommandDefinition(
				$"INSERT INTO {_codes} (UserRecoveryCodeId, UserId, CodeHash, HashVersion, CreatedOnUtc) VALUES (@Id, @UserId, @CodeHash, @HashVersion, @Now)",
				rows, transaction, cancellationToken: cancellationToken));
		}

		// Npgsql refuses Kind=Utc for "timestamp without time zone"; SQL Server ignores Kind.
		private DateTime Timestamp(DateTime utcNow) => _postgres ? DateTime.SpecifyKind(utcNow, DateTimeKind.Unspecified) : utcNow;

		private sealed class ByteArrayComparer : IEqualityComparer<byte[]>
		{
			public static readonly ByteArrayComparer Instance = new();
			public bool Equals(byte[] x, byte[] y) => x.AsSpan().SequenceEqual(y);
			public int GetHashCode(byte[] obj) => obj.Length >= 4 ? BitConverter.ToInt32(obj, 0) : obj.Length;
		}
	}
}
