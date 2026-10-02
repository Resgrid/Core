using System;
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
	/// Shared broker replay record (passkey workbook sections 6.2 and 8.3): one insert-if-absent per key, on its own
	/// connection, whose success is exactly one row. PostgreSQL uses ON CONFLICT DO NOTHING; SQL Server uses MERGE with
	/// HOLDLOCK so two replicas racing on one key cannot both insert.
	/// </summary>
	public sealed class BrokerReplayRepository : IBrokerReplayRepository
	{
		private readonly IConnectionProvider _connections;
		private readonly bool _postgres;
		private readonly string _table;

		public BrokerReplayRepository(IConnectionProvider connections, SqlConfiguration configuration)
		{
			_connections = connections;
			_postgres = DataConfig.DatabaseType == DatabaseTypes.Postgres;
			_table = configuration.SchemaName + (_postgres ? ".brokerreplaykeys" : ".[BrokerReplayKeys]");
		}

		public async Task<bool> TryClaimAsync(string replayKey, BrokerReplayKind kind, DateTime expiresOnUtc, DateTime utcNow,
			CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(replayKey) || replayKey.Length > 64)
				throw new ArgumentException("A replay key is a SHA-256 digest.", nameof(replayKey));

			var sql = _postgres
				? $@"INSERT INTO {_table} (replaykey, kind, createdonutc, expiresonutc) VALUES (@Key, @Kind, @Now, @Expires)
					ON CONFLICT (replaykey) DO NOTHING"
				: $@"MERGE {_table} WITH (HOLDLOCK) AS target
					USING (SELECT @Key AS ReplayKey) AS source ON target.ReplayKey = source.ReplayKey
					WHEN NOT MATCHED THEN
						INSERT (ReplayKey, Kind, CreatedOnUtc, ExpiresOnUtc) VALUES (@Key, @Kind, @Now, @Expires);";

			await using var connection = _connections.Create();
			await connection.OpenAsync(cancellationToken);
			return await connection.ExecuteAsync(new CommandDefinition(sql, new
			{
				Key = new DbString { Value = replayKey, IsAnsi = true, IsFixedLength = false, Length = 64 },
				Kind = (int)kind,
				Now = Timestamp(utcNow),
				Expires = Timestamp(expiresOnUtc)
			}, cancellationToken: cancellationToken)) == 1;
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
