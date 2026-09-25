using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using CommandDefinition = Dapper.CommandDefinition;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository
{
	/// <summary>Independent commits preserve evidence even when the business transaction rolls back.</summary>
	public sealed class AdpAuditRepository : IAdpAuditRepository
	{
		private readonly IConnectionProvider _connections;
		private readonly string _table;
		private readonly bool _postgres;
		public AdpAuditRepository(IConnectionProvider connections, SqlConfiguration configuration)
		{
			_connections = connections;
			_postgres = DataConfig.DatabaseType == DatabaseTypes.Postgres;
			_table = configuration.SchemaName + (_postgres ? ".adpauditevents" : ".[AdpAuditEvents]");
		}

		private string TailSql => _postgres
			? $"SELECT * FROM {_table} WHERE departmentid=@departmentId ORDER BY sequence DESC LIMIT 1"
			: $"SELECT TOP 1 * FROM {_table} WHERE DepartmentId=@departmentId ORDER BY Sequence DESC";

		public async Task AppendAsync(AdpAuditEvent record, CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(record);
			if (record.DepartmentId <= 0 || string.IsNullOrWhiteSpace(record.Layer) ||
				string.IsNullOrWhiteSpace(record.Operation) || string.IsNullOrWhiteSpace(record.Outcome))
				throw new ArgumentException("ADP audit requires a department, layer, operation and outcome.");
			using var connection = _connections.Create();
			await connection.OpenAsync(cancellationToken);
			for (var attempt = 0; ; attempt++)
			{
				var tail = await connection.QuerySingleOrDefaultAsync<AdpAuditEvent>(new Dapper.CommandDefinition(TailSql,
					new { record.DepartmentId }, cancellationToken: cancellationToken));
				AdpAuditChain.Link(record, tail);
				try
				{
					await connection.ExecuteAsync(new Dapper.CommandDefinition($@"INSERT INTO {_table}
(EventId,DepartmentId,Sequence,Layer,Operation,Outcome,ActorId,CorrelationId,ResourceId,PolicyEpoch,OccurredUtc,PreviousHash,Hash)
VALUES (@EventId,@DepartmentId,@Sequence,@Layer,@Operation,@Outcome,@ActorId,@CorrelationId,@ResourceId,@PolicyEpoch,@OccurredUtc,@PreviousHash,@Hash)",
						record, cancellationToken: cancellationToken));
					return;
				}
				catch (DbException) when (attempt < 31)
				{
					// The unique tenant/sequence index serializes writers across hosts. Retry only a lost race.
					var current = await connection.QuerySingleOrDefaultAsync<AdpAuditEvent>(new Dapper.CommandDefinition(TailSql,
						new { record.DepartmentId }, cancellationToken: cancellationToken));
					if (current == null || current.Sequence < record.Sequence) throw;
					// Concurrent writers for one department all race for the same tail. Back off with jitter so
					// they spread out instead of colliding again on the very next sequence.
					await Task.Delay(Random.Shared.Next(1, 4 << Math.Min(attempt, 5)), cancellationToken);
				}
			}
		}

		public async Task<IReadOnlyList<AdpAuditEvent>> ReadAsync(int departmentId, long afterSequence, int take, CancellationToken cancellationToken = default)
		{
			if (afterSequence < 0 || take < 1) throw new ArgumentOutOfRangeException(nameof(take), "ADP audit pages start at a non-negative sequence and read at least one row.");
			using var connection = _connections.Create();
			await connection.OpenAsync(cancellationToken);
			var sql = _postgres
				? $"SELECT * FROM {_table} WHERE departmentid=@departmentId AND sequence>@afterSequence ORDER BY sequence LIMIT @take"
				: $"SELECT TOP (@take) * FROM {_table} WHERE DepartmentId=@departmentId AND Sequence>@afterSequence ORDER BY Sequence";
			return (await connection.QueryAsync<AdpAuditEvent>(new Dapper.CommandDefinition(sql,
				new { departmentId, afterSequence, take }, cancellationToken: cancellationToken))).ToList();
		}
	}
}
