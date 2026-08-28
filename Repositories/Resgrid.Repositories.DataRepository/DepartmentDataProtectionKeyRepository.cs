using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
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
	public class DepartmentDataProtectionKeyRepository : RepositoryBase<DepartmentDataProtectionKey>, IDepartmentDataProtectionKeyRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly IUnitOfWork _unitOfWork;
		private readonly string _table;
		private readonly bool _isPostgres;

		public DepartmentDataProtectionKeyRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration,
			IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_unitOfWork = unitOfWork;
			_isPostgres = DataConfig.DatabaseType == DatabaseTypes.Postgres;
			_table = _isPostgres
				? $"{sqlConfiguration.SchemaName}.departmentdataprotectionkeys"
				: $"{sqlConfiguration.SchemaName}.[DepartmentDataProtectionKeys]";
		}

		public Task<DepartmentDataProtectionKey> GetActiveByDepartmentIdAsync(int departmentId)
		{
			var sql = _isPostgres
				? $"SELECT * FROM {_table} WHERE departmentid = @DepartmentId AND status = @Status ORDER BY version DESC LIMIT 1"
				: $"SELECT TOP 1 * FROM {_table} WHERE [DepartmentId] = @DepartmentId AND [Status] = @Status ORDER BY [Version] DESC";
			return WithConnectionAsync(connection => connection.QueryFirstOrDefaultAsync<DepartmentDataProtectionKey>(
				sql, new { DepartmentId = departmentId, Status = (int)DepartmentDataProtectionKeyStatus.Active }, _unitOfWork?.Transaction));
		}

		public Task<DepartmentDataProtectionKey> GetByDepartmentAndVersionAsync(int departmentId, int version)
		{
			var sql = _isPostgres
				? $"SELECT * FROM {_table} WHERE departmentid = @DepartmentId AND version = @Version"
				: $"SELECT * FROM {_table} WHERE [DepartmentId] = @DepartmentId AND [Version] = @Version";
			return WithConnectionAsync(connection => connection.QueryFirstOrDefaultAsync<DepartmentDataProtectionKey>(
				sql, new { DepartmentId = departmentId, Version = version }, _unitOfWork?.Transaction));
		}

		public async Task<IReadOnlyList<DepartmentDataProtectionKey>> GetAllVersionsByDepartmentIdAsync(int departmentId)
		{
			var sql = _isPostgres
				? $"SELECT * FROM {_table} WHERE departmentid = @DepartmentId ORDER BY version DESC"
				: $"SELECT * FROM {_table} WHERE [DepartmentId] = @DepartmentId ORDER BY [Version] DESC";
			var keys = await WithConnectionAsync(connection => connection.QueryAsync<DepartmentDataProtectionKey>(
				sql, new { DepartmentId = departmentId }, _unitOfWork?.Transaction));
			return keys.ToList();
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
