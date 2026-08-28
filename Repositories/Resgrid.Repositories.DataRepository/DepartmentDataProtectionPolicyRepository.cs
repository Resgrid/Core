using System;
using System.Data.Common;
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
	public class DepartmentDataProtectionPolicyRepository : RepositoryBase<DepartmentDataProtectionPolicy>, IDepartmentDataProtectionPolicyRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly IUnitOfWork _unitOfWork;
		private readonly string _table;
		private readonly bool _isPostgres;

		public DepartmentDataProtectionPolicyRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration,
			IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_unitOfWork = unitOfWork;
			_isPostgres = DataConfig.DatabaseType == DatabaseTypes.Postgres;
			_table = _isPostgres
				? $"{sqlConfiguration.SchemaName}.departmentdataprotectionpolicies"
				: $"{sqlConfiguration.SchemaName}.[DepartmentDataProtectionPolicies]";
		}

		public Task<DepartmentDataProtectionPolicy> GetByDepartmentIdAsync(int departmentId)
		{
			var sql = _isPostgres
				? $"SELECT * FROM {_table} WHERE departmentid = @DepartmentId"
				: $"SELECT * FROM {_table} WHERE [DepartmentId] = @DepartmentId";
			return WithConnectionAsync(connection => connection.QueryFirstOrDefaultAsync<DepartmentDataProtectionPolicy>(
				sql, new { DepartmentId = departmentId }, _unitOfWork?.Transaction));
		}

		public Task<int> TryTransitionStateAsync(int departmentId, DepartmentDataProtectionState expectedState,
			DepartmentDataProtectionState newState, int? activeMigrationKind, string updatedByUserId,
			CancellationToken cancellationToken)
		{
			var sql = _isPostgres
				? $"UPDATE {_table} SET state = @NewState, activemigrationkind = @ActiveMigrationKind, updatedon = @UtcNow, updatedbyuserid = @UpdatedByUserId WHERE departmentid = @DepartmentId AND state = @ExpectedState"
				: $"UPDATE {_table} SET [State] = @NewState, [ActiveMigrationKind] = @ActiveMigrationKind, [UpdatedOn] = @UtcNow, [UpdatedByUserId] = @UpdatedByUserId WHERE [DepartmentId] = @DepartmentId AND [State] = @ExpectedState";

			return WithConnectionAsync(connection => connection.ExecuteAsync(new Dapper.CommandDefinition(sql, new
			{
				DepartmentId = departmentId,
				ExpectedState = (int)expectedState,
				NewState = (int)newState,
				ActiveMigrationKind = activeMigrationKind,
				UtcNow = DateTime.UtcNow,
				UpdatedByUserId = updatedByUserId
			}, _unitOfWork?.Transaction, cancellationToken: cancellationToken)));
		}

		public async Task<long> IncrementPolicyEpochAsync(int departmentId, string updatedByUserId, CancellationToken cancellationToken)
		{
			var sql = _isPostgres
				? $"UPDATE {_table} SET policyepoch = policyepoch + 1, updatedon = @UtcNow, updatedbyuserid = @UpdatedByUserId WHERE departmentid = @DepartmentId RETURNING policyepoch"
				: $"UPDATE {_table} SET [PolicyEpoch] = [PolicyEpoch] + 1, [UpdatedOn] = @UtcNow, [UpdatedByUserId] = @UpdatedByUserId OUTPUT INSERTED.[PolicyEpoch] WHERE [DepartmentId] = @DepartmentId";

			var epoch = await WithConnectionAsync(connection => connection.QueryFirstOrDefaultAsync<long?>(
				new Dapper.CommandDefinition(sql, new
				{
					DepartmentId = departmentId,
					UtcNow = DateTime.UtcNow,
					UpdatedByUserId = updatedByUserId
				}, _unitOfWork?.Transaction, cancellationToken: cancellationToken)));

			return epoch ?? 0;
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
