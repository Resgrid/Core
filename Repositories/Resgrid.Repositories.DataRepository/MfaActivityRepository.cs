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
	/// <inheritdoc cref="IMfaActivityRepository"/>
	public sealed class MfaActivityRepository : IMfaActivityRepository
	{
		private const string Columns = @"MfaActivityId, UserId, OccurredOnUtc, Method, Purpose, Successful, ClientApplication, InstallationLabel, SharedMode,
			DepartmentId, SessionId, ApproverSessionId, ReportedOnUtc";

		private readonly IConnectionProvider _connections;
		private readonly bool _postgres;
		private readonly string _table;

		public MfaActivityRepository(IConnectionProvider connections, SqlConfiguration configuration)
		{
			_connections = connections;
			_postgres = DataConfig.DatabaseType == DatabaseTypes.Postgres;
			_table = configuration.SchemaName + (_postgres ? ".usermfaactivity" : ".[UserMfaActivity]");
		}

		public async Task InsertAsync(MfaActivity activity, CancellationToken cancellationToken = default)
		{
			await using var connection = _connections.Create();
			await connection.OpenAsync(cancellationToken);
			await connection.ExecuteAsync(new CommandDefinition(
				$@"INSERT INTO {_table} (MfaActivityId, UserId, OccurredOnUtc, Method, Purpose, Successful, ClientApplication, InstallationLabel, SharedMode,
					DepartmentId, SessionId, ApproverSessionId)
				VALUES (@MfaActivityId, @UserId, @OccurredOnUtc, @Method, @Purpose, @Successful, @ClientApplication, @InstallationLabel, @SharedMode,
					@DepartmentId, @SessionId, @ApproverSessionId)",
				new
				{
					activity.MfaActivityId,
					activity.UserId,
					OccurredOnUtc = Timestamp(activity.OccurredOnUtc),
					activity.Method,
					activity.Purpose,
					activity.Successful,
					activity.ClientApplication,
					activity.InstallationLabel,
					activity.SharedMode,
					activity.DepartmentId,
					activity.SessionId,
					activity.ApproverSessionId
				}, cancellationToken: cancellationToken));
		}

		public async Task<int> CountDeniedSinceAsync(string userId, DateTime sinceUtc, CancellationToken cancellationToken = default)
		{
			await using var connection = _connections.Create();
			await connection.OpenAsync(cancellationToken);
			return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
				$"SELECT COUNT(*) FROM {_table} WHERE UserId = @UserId AND Successful = @False AND OccurredOnUtc >= @Since",
				new { UserId = userId, Since = Timestamp(sinceUtc), False = false }, cancellationToken: cancellationToken));
		}

		public async Task<IReadOnlyList<MfaActivity>> GetRecentAsync(string userId, DateTime sinceUtc, int take, CancellationToken cancellationToken = default)
		{
			var sql = _postgres
				? $"SELECT {Columns} FROM {_table} WHERE UserId = @UserId AND OccurredOnUtc >= @Since ORDER BY OccurredOnUtc DESC LIMIT @Take"
				: $"SELECT TOP (@Take) {Columns} FROM {_table} WHERE UserId = @UserId AND OccurredOnUtc >= @Since ORDER BY OccurredOnUtc DESC";
			await using var connection = _connections.Create();
			await connection.OpenAsync(cancellationToken);
			return (await connection.QueryAsync<MfaActivity>(new CommandDefinition(sql,
				new { UserId = userId, Since = Timestamp(sinceUtc), Take = Math.Max(1, take) }, cancellationToken: cancellationToken))).ToList();
		}

		public async Task<MfaActivity> GetAsync(string mfaActivityId, CancellationToken cancellationToken = default)
		{
			await using var connection = _connections.Create();
			await connection.OpenAsync(cancellationToken);
			return await connection.QuerySingleOrDefaultAsync<MfaActivity>(new CommandDefinition(
				$"SELECT {Columns} FROM {_table} WHERE MfaActivityId = @Id", new { Id = mfaActivityId }, cancellationToken: cancellationToken));
		}

		public async Task<bool> TryMarkReportedAsync(string mfaActivityId, string userId, DateTime reportedOnUtc, CancellationToken cancellationToken = default)
		{
			await using var connection = _connections.Create();
			await connection.OpenAsync(cancellationToken);
			return await connection.ExecuteAsync(new CommandDefinition(
				$"UPDATE {_table} SET ReportedOnUtc = @Now WHERE MfaActivityId = @Id AND UserId = @UserId AND ReportedOnUtc IS NULL",
				new { Id = mfaActivityId, UserId = userId, Now = Timestamp(reportedOnUtc) }, cancellationToken: cancellationToken)) == 1;
		}

		public async Task<int> PurgeBeforeAsync(DateTime utcCutoff, CancellationToken cancellationToken = default)
		{
			await using var connection = _connections.Create();
			await connection.OpenAsync(cancellationToken);
			return await connection.ExecuteAsync(new CommandDefinition(
				$"DELETE FROM {_table} WHERE OccurredOnUtc < @Cutoff", new { Cutoff = Timestamp(utcCutoff) }, cancellationToken: cancellationToken));
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
