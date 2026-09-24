using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
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
	public class WorkflowProtectedReleaseRepository : RepositoryBase<WorkflowProtectedRelease>, IWorkflowProtectedReleaseRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly IUnitOfWork _unitOfWork;
		private readonly string _table;
		private readonly bool _isPostgres;

		public WorkflowProtectedReleaseRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration,
			IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_unitOfWork = unitOfWork;
			_isPostgres = DataConfig.DatabaseType == DatabaseTypes.Postgres;
			_table = _isPostgres
				? $"{sqlConfiguration.SchemaName}.workflowprotectedreleases"
				: $"{sqlConfiguration.SchemaName}.[WorkflowProtectedReleases]";
		}

		public Task<WorkflowProtectedRelease> GetLatestByWorkflowIdAsync(string workflowId)
		{
			var sql = _isPostgres
				? $"SELECT * FROM {_table} WHERE workflowid = @WorkflowId ORDER BY createdon DESC LIMIT 1"
				: $"SELECT TOP 1 * FROM {_table} WHERE [WorkflowId] = @WorkflowId ORDER BY [CreatedOn] DESC";
			return WithConnectionAsync(c => c.QueryFirstOrDefaultAsync<WorkflowProtectedRelease>(sql, new { WorkflowId = workflowId }, _unitOfWork?.Transaction));
		}

		public Task<IEnumerable<WorkflowProtectedRelease>> GetAllByWorkflowIdAsync(string workflowId)
		{
			var sql = _isPostgres
				? $"SELECT * FROM {_table} WHERE workflowid = @WorkflowId ORDER BY createdon DESC"
				: $"SELECT * FROM {_table} WHERE [WorkflowId] = @WorkflowId ORDER BY [CreatedOn] DESC";
			return WithConnectionAsync(c => c.QueryAsync<WorkflowProtectedRelease>(sql, new { WorkflowId = workflowId }, _unitOfWork?.Transaction));
		}

		public Task<IEnumerable<WorkflowProtectedRelease>> GetAllByDepartmentIdAsync(int departmentId)
		{
			var sql = _isPostgres
				? $"SELECT * FROM {_table} WHERE departmentid = @DepartmentId ORDER BY createdon DESC"
				: $"SELECT * FROM {_table} WHERE [DepartmentId] = @DepartmentId ORDER BY [CreatedOn] DESC";
			return WithConnectionAsync(c => c.QueryAsync<WorkflowProtectedRelease>(sql, new { DepartmentId = departmentId }, _unitOfWork?.Transaction));
		}

		public Task<IEnumerable<WorkflowProtectedRelease>> GetAllByCredentialIdAsync(string workflowCredentialId)
		{
			var sql = _isPostgres
				? $"SELECT * FROM {_table} WHERE workflowcredentialid = @WorkflowCredentialId"
				: $"SELECT * FROM {_table} WHERE [WorkflowCredentialId] = @WorkflowCredentialId";
			return WithConnectionAsync(c => c.QueryAsync<WorkflowProtectedRelease>(sql, new { WorkflowCredentialId = workflowCredentialId }, _unitOfWork?.Transaction));
		}

		public Task<IEnumerable<WorkflowProtectedRelease>> GetAllByStatesAsync(IEnumerable<ProtectedReleaseState> states)
		{
			// Enum values only, never caller text, so the literal list is safe on both engines.
			var list = string.Join(",", (states ?? Enumerable.Empty<ProtectedReleaseState>()).Select(s => ((int)s).ToString()).Distinct());
			if (list.Length == 0)
				return Task.FromResult(Enumerable.Empty<WorkflowProtectedRelease>());

			var sql = _isPostgres
				? $"SELECT * FROM {_table} WHERE state IN ({list})"
				: $"SELECT * FROM {_table} WHERE [State] IN ({list})";
			return WithConnectionAsync(c => c.QueryAsync<WorkflowProtectedRelease>(sql, null, _unitOfWork?.Transaction));
		}

		private static readonly string[] MutableColumns =
		{
			"State", "SuspendedReason", "AllowedFieldIds", "DestinationScheme", "DestinationHost", "TokenHost", "WorkflowCredentialId",
			"AuthMethod", "AllowsRestricted", "RestrictedAckVersion", "RestrictedAckByUserId", "AllowsPart2", "Part2AckVersion", "Part2AckByUserId",
			"ConfigFingerprint", "RecipientType", "RecipientName", "Purpose", "AckVersion", "RequestedByUserId", "RequestedOn",
			"ApprovedByUserId", "ApprovedOn", "ExpiresOn", "ExpiryNoticeSentDays", "RevokedByUserId", "RevokedOn", "UpdatedOn"
		};

		public async Task<bool> TryUpdateAsync(WorkflowProtectedRelease release, CancellationToken cancellationToken = default)
		{
			if (release == null)
				throw new ArgumentNullException(nameof(release));

			Utf8WriteGuard.Sanitize(release);
			string Column(string name) => _isPostgres ? name.ToLowerInvariant() : $"[{name}]";
			var assignments = string.Join(", ", MutableColumns.Select(c => $"{Column(c)} = @{c}"));
			var sql = $"UPDATE {_table} SET {assignments}, {Column("Version")} = {Column("Version")} + 1 " +
				$"WHERE {Column("WorkflowProtectedReleaseId")} = @WorkflowProtectedReleaseId AND {Column("Version")} = @Version";

			var rows = await WithConnectionAsync(c => c.ExecuteAsync(new Dapper.CommandDefinition(sql, release, _unitOfWork?.Transaction, cancellationToken: cancellationToken)));
			if (rows != 1)
				return false;

			release.Version++;
			return true;
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
