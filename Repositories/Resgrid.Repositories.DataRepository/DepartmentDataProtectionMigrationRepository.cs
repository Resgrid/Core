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
	public class DepartmentDataProtectionMigrationRepository : RepositoryBase<DepartmentDataProtectionMigration>, IDepartmentDataProtectionMigrationRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly IUnitOfWork _unitOfWork;
		private readonly string _table;
		private readonly bool _isPostgres;

		public DepartmentDataProtectionMigrationRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration,
			IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_unitOfWork = unitOfWork;
			_isPostgres = DataConfig.DatabaseType == DatabaseTypes.Postgres;
			_table = _isPostgres
				? $"{sqlConfiguration.SchemaName}.departmentdataprotectionmigrations"
				: $"{sqlConfiguration.SchemaName}.[DepartmentDataProtectionMigrations]";
		}

		public async Task<IReadOnlyList<DepartmentDataProtectionMigration>> GetActiveByDepartmentIdAsync(int departmentId,
			DepartmentDataProtectionMigrationKind kind)
		{
			var sql = _isPostgres
				? $"SELECT * FROM {_table} WHERE departmentid = @DepartmentId AND kind = @Kind AND completedon IS NULL ORDER BY targettable"
				: $"SELECT * FROM {_table} WHERE [DepartmentId] = @DepartmentId AND [Kind] = @Kind AND [CompletedOn] IS NULL ORDER BY [TargetTable]";
			var rows = await WithConnectionAsync(connection => connection.QueryAsync<DepartmentDataProtectionMigration>(
				sql, new { DepartmentId = departmentId, Kind = (int)kind }, _unitOfWork?.Transaction));
			return rows.ToList();
		}

		public Task<DepartmentDataProtectionMigration> GetActiveByDepartmentAndTableAsync(int departmentId,
			DepartmentDataProtectionMigrationKind kind, string targetTable)
		{
			var sql = _isPostgres
				? $"SELECT * FROM {_table} WHERE departmentid = @DepartmentId AND kind = @Kind AND targettable = @TargetTable AND completedon IS NULL"
				: $"SELECT * FROM {_table} WHERE [DepartmentId] = @DepartmentId AND [Kind] = @Kind AND [TargetTable] = @TargetTable AND [CompletedOn] IS NULL";
			return WithConnectionAsync(connection => connection.QueryFirstOrDefaultAsync<DepartmentDataProtectionMigration>(
				sql, new { DepartmentId = departmentId, Kind = (int)kind, TargetTable = targetTable }, _unitOfWork?.Transaction));
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
