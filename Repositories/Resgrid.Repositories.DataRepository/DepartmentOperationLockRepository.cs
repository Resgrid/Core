using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository
{
	public class DepartmentOperationLockRepository : RepositoryBase<DepartmentOperationLock>, IDepartmentOperationLockRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly IUnitOfWork _unitOfWork;
		private readonly string _table;
		private readonly bool _isPostgres;

		public DepartmentOperationLockRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration,
			IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_unitOfWork = unitOfWork;
			_isPostgres = DataConfig.DatabaseType == DatabaseTypes.Postgres;
			_table = _isPostgres
				? $"{sqlConfiguration.SchemaName}.departmentoperationlocks"
				: $"{sqlConfiguration.SchemaName}.[DepartmentOperationLocks]";
		}

		public Task<DepartmentOperationLock> GetActiveByDepartmentIdAsync(int departmentId)
		{
			var sql = _isPostgres
				? $"SELECT * FROM {_table} WHERE departmentid = @DepartmentId AND releasedutc IS NULL"
				: $"SELECT * FROM {_table} WHERE [DepartmentId] = @DepartmentId AND [ReleasedUtc] IS NULL";
			return WithConnectionAsync(connection => connection.QueryFirstOrDefaultAsync<DepartmentOperationLock>(
				sql, new { DepartmentId = departmentId }, _unitOfWork?.Transaction));
		}

		public async Task<IReadOnlyList<DepartmentOperationLock>> GetAllActiveAsync()
		{
			var sql = _isPostgres
				? $"SELECT * FROM {_table} WHERE releasedutc IS NULL ORDER BY appliedutc"
				: $"SELECT * FROM {_table} WHERE [ReleasedUtc] IS NULL ORDER BY [AppliedUtc]";
			var locks = await WithConnectionAsync(connection => connection.QueryAsync<DepartmentOperationLock>(
				sql, null, _unitOfWork?.Transaction));
			return locks.ToList();
		}

		/// <summary>
		/// The INSERT is guarded twice: a NOT EXISTS predicate loses gracefully in the common case, and
		/// the filtered/partial unique index on (DepartmentId) WHERE ReleasedUtc IS NULL closes the race
		/// two concurrent acquirers could otherwise win under snapshot isolation. On PostgreSQL the
		/// index race is absorbed in-statement with ON CONFLICT DO NOTHING — an exception there would
		/// poison an enclosing transaction (25P02) and make the recovery re-read impossible. On SQL
		/// Server a unique-index violation is mapped to false by re-reading the active lock rather than
		/// by parsing provider-specific error codes.
		/// </summary>
		public async Task<bool> TryAcquireAsync(DepartmentOperationLock departmentLock, CancellationToken cancellationToken)
		{
			if (departmentLock == null)
				throw new ArgumentNullException(nameof(departmentLock));

			Utf8WriteGuard.Sanitize(departmentLock);

			var sql = _isPostgres
				? $@"INSERT INTO {_table} (departmentid, locktype, reason, correlationid, appliedutc, appliedbyidentity, heartbeatutc, expiresutc, projectedendutc)
					SELECT @DepartmentId, @LockType, @Reason, @CorrelationId, @AppliedUtc, @AppliedByIdentity, @HeartbeatUtc, @ExpiresUtc, @ProjectedEndUtc
					WHERE NOT EXISTS (SELECT 1 FROM {_table} WHERE departmentid = @DepartmentId AND releasedutc IS NULL)
					ON CONFLICT (departmentid) WHERE releasedutc IS NULL DO NOTHING
					RETURNING departmentoperationlockid"
				: $@"INSERT INTO {_table} ([DepartmentId], [LockType], [Reason], [CorrelationId], [AppliedUtc], [AppliedByIdentity], [HeartbeatUtc], [ExpiresUtc], [ProjectedEndUtc])
					OUTPUT INSERTED.[DepartmentOperationLockId]
					SELECT @DepartmentId, @LockType, @Reason, @CorrelationId, @AppliedUtc, @AppliedByIdentity, @HeartbeatUtc, @ExpiresUtc, @ProjectedEndUtc
					WHERE NOT EXISTS (SELECT 1 FROM {_table} WHERE [DepartmentId] = @DepartmentId AND [ReleasedUtc] IS NULL)";

			try
			{
				var id = await WithConnectionAsync(connection => connection.QueryFirstOrDefaultAsync<int?>(
					new Dapper.CommandDefinition(sql, new
					{
						departmentLock.DepartmentId,
						departmentLock.LockType,
						departmentLock.Reason,
						departmentLock.CorrelationId,
						departmentLock.AppliedUtc,
						departmentLock.AppliedByIdentity,
						departmentLock.HeartbeatUtc,
						departmentLock.ExpiresUtc,
						departmentLock.ProjectedEndUtc
					}, _unitOfWork?.Transaction, cancellationToken: cancellationToken)));

				if (id == null)
					return false;

				departmentLock.DepartmentOperationLockId = id.Value;
				return true;
			}
			catch (DbException) when (!_isPostgres)
			{
				// SQL Server only: a concurrent acquirer beat this one through the unique index. If an
				// active lock now exists this is the expected lost race; anything else is a real fault.
				// PostgreSQL never takes this path — ON CONFLICT absorbs the race in-statement, and a
				// re-read inside a now-failed transaction would only raise 25P02.
				var active = await GetActiveByDepartmentIdAsync(departmentLock.DepartmentId);
				if (active != null)
					return false;

				throw;
			}
		}

		public Task<int> HeartbeatAsync(int departmentOperationLockId, DateTime heartbeatUtc, DateTime? newExpiresUtc,
			CancellationToken cancellationToken)
		{
			var sql = _isPostgres
				? $"UPDATE {_table} SET heartbeatutc = @HeartbeatUtc, expiresutc = COALESCE(@NewExpiresUtc, expiresutc) WHERE departmentoperationlockid = @Id AND releasedutc IS NULL"
				: $"UPDATE {_table} SET [HeartbeatUtc] = @HeartbeatUtc, [ExpiresUtc] = COALESCE(@NewExpiresUtc, [ExpiresUtc]) WHERE [DepartmentOperationLockId] = @Id AND [ReleasedUtc] IS NULL";

			return WithConnectionAsync(connection => connection.ExecuteAsync(new Dapper.CommandDefinition(sql, new
			{
				Id = departmentOperationLockId,
				HeartbeatUtc = heartbeatUtc,
				NewExpiresUtc = newExpiresUtc
			}, _unitOfWork?.Transaction, cancellationToken: cancellationToken)));
		}

		public Task<int> ReleaseAsync(int departmentOperationLockId, DepartmentOperationLockReleaseKind kind,
			string releasedBy, DateTime releasedUtc, CancellationToken cancellationToken)
		{
			var sql = _isPostgres
				? $"UPDATE {_table} SET releasedutc = @ReleasedUtc, releasedby = @ReleasedBy, releasekind = @ReleaseKind WHERE departmentoperationlockid = @Id AND releasedutc IS NULL"
				: $"UPDATE {_table} SET [ReleasedUtc] = @ReleasedUtc, [ReleasedBy] = @ReleasedBy, [ReleaseKind] = @ReleaseKind WHERE [DepartmentOperationLockId] = @Id AND [ReleasedUtc] IS NULL";

			return WithConnectionAsync(connection => connection.ExecuteAsync(new Dapper.CommandDefinition(sql, new
			{
				Id = departmentOperationLockId,
				ReleasedUtc = releasedUtc,
				ReleasedBy = releasedBy,
				ReleaseKind = (int)kind
			}, _unitOfWork?.Transaction, cancellationToken: cancellationToken)));
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
