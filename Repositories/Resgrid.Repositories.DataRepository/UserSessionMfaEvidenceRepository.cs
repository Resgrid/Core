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
	/// <summary>Server-side MFA evidence per session (passkey plan section 5.3). Own connection per call.</summary>
	public sealed class UserSessionMfaEvidenceRepository : IUserSessionMfaEvidenceRepository
	{
		private readonly IConnectionProvider _connections;
		private readonly bool _postgres;
		private readonly string _table;

		public UserSessionMfaEvidenceRepository(IConnectionProvider connections, SqlConfiguration configuration)
		{
			_connections = connections;
			_postgres = DataConfig.DatabaseType == DatabaseTypes.Postgres;
			_table = configuration.SchemaName + (_postgres ? ".usersessionmfaevidence" : ".[UserSessionMfaEvidence]");
		}

		public async Task InsertAsync(MfaEvidence evidence, CancellationToken cancellationToken = default)
		{
			await using var connection = _connections.Create();
			await connection.OpenAsync(cancellationToken);
			await connection.ExecuteAsync(new CommandDefinition(
				$@"INSERT INTO {_table} (MfaEvidenceId, UserId, SessionKey, ClientApplication, Kind, Method, Purpose, DepartmentId,
					VerifiedOnUtc, ExpiresOnUtc, AuthenticationGeneration, FactorReference)
				VALUES (@MfaEvidenceId, @UserId, @SessionKey, @ClientApplication, @Kind, @Method, @Purpose, @DepartmentId,
					@VerifiedOnUtc, @ExpiresOnUtc, @AuthenticationGeneration, @FactorReference)",
				new
				{
					evidence.MfaEvidenceId,
					evidence.UserId,
					evidence.SessionKey,
					evidence.ClientApplication,
					evidence.Kind,
					evidence.Method,
					evidence.Purpose,
					evidence.DepartmentId,
					VerifiedOnUtc = Timestamp(evidence.VerifiedOnUtc),
					ExpiresOnUtc = Timestamp(evidence.ExpiresOnUtc),
					evidence.AuthenticationGeneration,
					evidence.FactorReference
				}, cancellationToken: cancellationToken));
		}

		public Task<MfaEvidence> GetLatestAsync(string userId, string sessionKey, MfaEvidenceKind kind, long authenticationGeneration,
			DateTime utcNow, CancellationToken cancellationToken = default) =>
			QueryLatestAsync(userId, sessionKey, kind, null, authenticationGeneration, utcNow, cancellationToken);

		public Task<MfaEvidence> GetLatestForPurposeAsync(string userId, string sessionKey, MfaEvidenceKind kind, MfaEvidencePurpose purpose,
			long authenticationGeneration, DateTime utcNow, CancellationToken cancellationToken = default) =>
			QueryLatestAsync(userId, sessionKey, kind, purpose, authenticationGeneration, utcNow, cancellationToken);

		private async Task<MfaEvidence> QueryLatestAsync(string userId, string sessionKey, MfaEvidenceKind kind, MfaEvidencePurpose? purpose,
			long authenticationGeneration, DateTime utcNow, CancellationToken cancellationToken)
		{
			const string columns = @"MfaEvidenceId, UserId, SessionKey, ClientApplication, Kind, Method, Purpose, DepartmentId,
				VerifiedOnUtc, ExpiresOnUtc, AuthenticationGeneration, FactorReference, RevokedOnUtc";
			var filter = @"WHERE UserId = @UserId AND SessionKey = @SessionKey AND Kind = @Kind
				AND AuthenticationGeneration = @Generation AND RevokedOnUtc IS NULL AND ExpiresOnUtc > @Now"
				+ (purpose == null ? string.Empty : " AND Purpose = @Purpose");
			var sql = _postgres
				? $"SELECT {columns} FROM {_table} {filter} ORDER BY VerifiedOnUtc DESC LIMIT 1"
				: $"SELECT TOP (1) {columns} FROM {_table} {filter} ORDER BY VerifiedOnUtc DESC";

			await using var connection = _connections.Create();
			await connection.OpenAsync(cancellationToken);
			return await connection.QuerySingleOrDefaultAsync<MfaEvidence>(new CommandDefinition(sql, new
			{
				UserId = userId,
				SessionKey = sessionKey,
				Kind = (int)kind,
				Purpose = (int)(purpose ?? 0),
				Generation = authenticationGeneration,
				Now = Timestamp(utcNow)
			}, cancellationToken: cancellationToken));
		}

		public async Task<int> RevokeForUserAsync(string userId, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			await using var connection = _connections.Create();
			await connection.OpenAsync(cancellationToken);
			return await connection.ExecuteAsync(new CommandDefinition(
				$"UPDATE {_table} SET RevokedOnUtc = @Now WHERE UserId = @UserId AND RevokedOnUtc IS NULL",
				new { UserId = userId, Now = Timestamp(utcNow) }, cancellationToken: cancellationToken));
		}

		public async Task<int> RevokeForFactorAsync(string userId, string factorReference, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			await using var connection = _connections.Create();
			await connection.OpenAsync(cancellationToken);
			return await connection.ExecuteAsync(new CommandDefinition(
				$"UPDATE {_table} SET RevokedOnUtc = @Now WHERE UserId = @UserId AND FactorReference = @FactorReference AND RevokedOnUtc IS NULL",
				new { UserId = userId, FactorReference = factorReference, Now = Timestamp(utcNow) }, cancellationToken: cancellationToken));
		}

		public async Task<int> RevokeByFactorReferenceAsync(string factorReference, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			await using var connection = _connections.Create();
			await connection.OpenAsync(cancellationToken);
			return await connection.ExecuteAsync(new CommandDefinition(
				$"UPDATE {_table} SET RevokedOnUtc = @Now WHERE FactorReference = @FactorReference AND RevokedOnUtc IS NULL",
				new { FactorReference = factorReference, Now = Timestamp(utcNow) }, cancellationToken: cancellationToken));
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
