using System;
using System.Data.Common;
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
	public class DepartmentProtectedDataEgressPolicyRepository : RepositoryBase<DepartmentProtectedDataEgressPolicy>, IDepartmentProtectedDataEgressPolicyRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly IUnitOfWork _unitOfWork;
		private readonly string _table;
		private readonly bool _isPostgres;

		public DepartmentProtectedDataEgressPolicyRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration,
			IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_unitOfWork = unitOfWork;
			_isPostgres = DataConfig.DatabaseType == DatabaseTypes.Postgres;
			_table = _isPostgres
				? $"{sqlConfiguration.SchemaName}.departmentprotecteddataegresspolicies"
				: $"{sqlConfiguration.SchemaName}.[DepartmentProtectedDataEgressPolicies]";
		}

		public Task<DepartmentProtectedDataEgressPolicy> GetByDepartmentIdAsync(int departmentId)
		{
			var sql = _isPostgres
				? $"SELECT * FROM {_table} WHERE departmentid = @DepartmentId"
				: $"SELECT * FROM {_table} WHERE [DepartmentId] = @DepartmentId";
			return WithConnectionAsync(connection => connection.QueryFirstOrDefaultAsync<DepartmentProtectedDataEgressPolicy>(
				sql, new { DepartmentId = departmentId }, _unitOfWork?.Transaction));
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
