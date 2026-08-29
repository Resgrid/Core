using System;
using System.Collections.Generic;
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
	public class DepartmentMemberEmergencyContactRepository : RepositoryBase<DepartmentMemberEmergencyContact>, IDepartmentMemberEmergencyContactRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly IUnitOfWork _unitOfWork;
		private readonly string _table;
		private readonly bool _isPostgres;

		public DepartmentMemberEmergencyContactRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration,
			IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_unitOfWork = unitOfWork;
			_isPostgres = DataConfig.DatabaseType == DatabaseTypes.Postgres;
			_table = _isPostgres
				? $"{sqlConfiguration.SchemaName}.departmentmemberemergencycontacts"
				: $"{sqlConfiguration.SchemaName}.[DepartmentMemberEmergencyContacts]";
		}

		public Task<IEnumerable<DepartmentMemberEmergencyContact>> GetAllByDepartmentAndUserAsync(int departmentId, string userId)
		{
			var sql = _isPostgres
				? $"SELECT * FROM {_table} WHERE departmentid = @DepartmentId AND userid = @UserId AND isdeleted = false ORDER BY isprimary DESC, sortorder ASC, departmentmemberemergencycontactid ASC"
				: $"SELECT * FROM {_table} WHERE [DepartmentId] = @DepartmentId AND [UserId] = @UserId AND [IsDeleted] = 0 ORDER BY [IsPrimary] DESC, [SortOrder] ASC, [DepartmentMemberEmergencyContactId] ASC";

			return WithConnectionAsync(connection => connection.QueryAsync<DepartmentMemberEmergencyContact>(
				sql, new { DepartmentId = departmentId, UserId = userId }, _unitOfWork?.Transaction));
		}

		public Task<int> DeleteAllByDepartmentAndUserAsync(int departmentId, string userId)
		{
			var sql = _isPostgres
				? $"DELETE FROM {_table} WHERE departmentid = @DepartmentId AND userid = @UserId"
				: $"DELETE FROM {_table} WHERE [DepartmentId] = @DepartmentId AND [UserId] = @UserId";

			return WithConnectionAsync(connection => connection.ExecuteAsync(
				sql, new { DepartmentId = departmentId, UserId = userId }, _unitOfWork?.Transaction));
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
