using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository
{
	/// <summary>
	/// Append-only store for the per-department Protected Workflow hash chain. The only write is AppendAsync, which
	/// links the record to the current tail and inserts it; the unique (DepartmentId, ChainSequence) index turns a
	/// concurrent append into a failed insert, which re-links against the new tail and retries. There is no update
	/// or delete path in application code.
	/// </summary>
	public class ProtectedWorkflowDisclosureRepository : RepositoryBase<ProtectedWorkflowDisclosure>, IProtectedWorkflowDisclosureRepository
	{
		private const int MaxAppendAttempts = 8;

		private readonly IConnectionProvider _connectionProvider;
		private readonly IUnitOfWork _unitOfWork;
		private readonly string _table;
		private readonly bool _isPostgres;

		public ProtectedWorkflowDisclosureRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration,
			IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_unitOfWork = unitOfWork;
			_isPostgres = DataConfig.DatabaseType == DatabaseTypes.Postgres;
			_table = _isPostgres
				? $"{sqlConfiguration.SchemaName}.protectedworkflowdisclosures"
				: $"{sqlConfiguration.SchemaName}.[ProtectedWorkflowDisclosures]";
		}

		public async Task<ProtectedWorkflowDisclosure> AppendAsync(ProtectedWorkflowDisclosure record, CancellationToken cancellationToken = default)
		{
			if (record == null)
				throw new ArgumentNullException(nameof(record));

			if (string.IsNullOrWhiteSpace(record.ProtectedWorkflowDisclosureId))
				record.ProtectedWorkflowDisclosureId = Guid.NewGuid().ToString();

			// Sanitize BEFORE hashing: InsertAsync sanitizes too, and the stored bytes must be the hashed bytes.
			Utf8WriteGuard.Sanitize(record);

			for (var attempt = 1; ; attempt++)
			{
				cancellationToken.ThrowIfCancellationRequested();
				var tail = await GetLatestForDepartmentAsync(record.DepartmentId);
				ProtectedWorkflowDisclosureChain.Link(record, tail);
				// Hashed at millisecond precision already; sent as an unzoned timestamp so no session time zone can shift
				// the stored value (a UTC-kinded DateTime is converted by PostgreSQL on its way into a timestamp column).
				record.OccurredOn = DateTime.SpecifyKind(record.OccurredOn, DateTimeKind.Unspecified);

				try
				{
					await InsertAsync(record, cancellationToken);
					return record;
				}
				catch (DbException) when (attempt < MaxAppendAttempts)
				{
					// Only a lost race for the sequence number is retried; anything else is a real failure.
					var current = await GetLatestForDepartmentAsync(record.DepartmentId);
					if (current == null || current.ChainSequence < record.ChainSequence)
						throw;
				}
			}
		}

		public Task<ProtectedWorkflowDisclosure> GetLatestForDepartmentAsync(int departmentId)
		{
			var sql = _isPostgres
				? $"SELECT * FROM {_table} WHERE departmentid = @DepartmentId ORDER BY chainsequence DESC LIMIT 1"
				: $"SELECT TOP 1 * FROM {_table} WHERE [DepartmentId] = @DepartmentId ORDER BY [ChainSequence] DESC";
			return WithConnectionAsync(c => c.QueryFirstOrDefaultAsync<ProtectedWorkflowDisclosure>(sql, new { DepartmentId = departmentId }, _unitOfWork?.Transaction));
		}

		public Task<IEnumerable<ProtectedWorkflowDisclosure>> GetForDepartmentAsync(int departmentId, ProtectedWorkflowDisclosureFilter filter)
		{
			filter ??= new ProtectedWorkflowDisclosureFilter();
			var max = Math.Clamp(filter.MaxRows, 1, 50000);
			var parameters = new DynamicParameters();
			parameters.Add("DepartmentId", departmentId);

			string Column(string name) => _isPostgres ? name.ToLowerInvariant() : $"[{name}]";

			var where = new StringBuilder($"{Column("DepartmentId")} = @DepartmentId");
			if (!string.IsNullOrWhiteSpace(filter.WorkflowId))
			{
				where.Append($" AND {Column("WorkflowId")} = @WorkflowId");
				parameters.Add("WorkflowId", filter.WorkflowId.Trim());
			}
			if (!string.IsNullOrWhiteSpace(filter.EntityId))
			{
				where.Append($" AND {Column("EntityId")} = @EntityId");
				parameters.Add("EntityId", filter.EntityId.Trim());
			}
			if (!string.IsNullOrWhiteSpace(filter.RecordType))
			{
				where.Append($" AND {Column("RecordType")} = @RecordType");
				parameters.Add("RecordType", filter.RecordType.Trim());
			}
			if (filter.FromUtc.HasValue)
			{
				where.Append($" AND {Column("OccurredOn")} >= @FromUtc");
				parameters.Add("FromUtc", filter.FromUtc.Value);
			}
			if (filter.ToUtc.HasValue)
			{
				where.Append($" AND {Column("OccurredOn")} <= @ToUtc");
				parameters.Add("ToUtc", filter.ToUtc.Value);
			}

			var sql = _isPostgres
				? $"SELECT * FROM {_table} WHERE {where} ORDER BY chainsequence DESC LIMIT {max}"
				: $"SELECT TOP {max} * FROM {_table} WHERE {where} ORDER BY [ChainSequence] DESC";
			return WithConnectionAsync(c => c.QueryAsync<ProtectedWorkflowDisclosure>(sql, parameters, _unitOfWork?.Transaction));
		}

		public Task<IEnumerable<ProtectedWorkflowDisclosure>> GetChainForDepartmentAsync(int departmentId)
		{
			var sql = _isPostgres
				? $"SELECT * FROM {_table} WHERE departmentid = @DepartmentId ORDER BY chainsequence ASC"
				: $"SELECT * FROM {_table} WHERE [DepartmentId] = @DepartmentId ORDER BY [ChainSequence] ASC";
			return WithConnectionAsync(c => c.QueryAsync<ProtectedWorkflowDisclosure>(sql, new { DepartmentId = departmentId }, _unitOfWork?.Transaction));
		}

		private async Task<TResult> WithConnectionAsync<TResult>(Func<DbConnection, Task<TResult>> operation)
		{
			if (_unitOfWork?.Connection != null)
				return await operation(_unitOfWork.CreateOrGetConnection());

			using var connection = _connectionProvider.Create();
			await connection.OpenAsync();
			return await operation(connection);
		}
	}
}
