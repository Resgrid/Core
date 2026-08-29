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
	public class DepartmentMemberSensitiveDataRepository : RepositoryBase<DepartmentMemberSensitiveData>, IDepartmentMemberSensitiveDataRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly IUnitOfWork _unitOfWork;
		private readonly string _table;
		private readonly bool _isPostgres;

		public DepartmentMemberSensitiveDataRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration,
			IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_unitOfWork = unitOfWork;
			_isPostgres = DataConfig.DatabaseType == DatabaseTypes.Postgres;
			_table = _isPostgres
				? $"{sqlConfiguration.SchemaName}.departmentmembersensitivedata"
				: $"{sqlConfiguration.SchemaName}.[DepartmentMemberSensitiveData]";
		}

		public Task<DepartmentMemberSensitiveData> GetByDepartmentAndUserAsync(int departmentId, string userId)
		{
			var sql = _isPostgres
				? $"SELECT * FROM {_table} WHERE departmentid = @DepartmentId AND userid = @UserId"
				: $"SELECT * FROM {_table} WHERE [DepartmentId] = @DepartmentId AND [UserId] = @UserId";
			return WithConnectionAsync(connection => connection.QueryFirstOrDefaultAsync<DepartmentMemberSensitiveData>(
				sql, new { DepartmentId = departmentId, UserId = userId }, _unitOfWork?.Transaction));
		}

		public Task<IEnumerable<DepartmentMemberSensitiveData>> GetAllByDepartmentIdAsync(int departmentId)
		{
			var sql = _isPostgres
				? $"SELECT * FROM {_table} WHERE departmentid = @DepartmentId"
				: $"SELECT * FROM {_table} WHERE [DepartmentId] = @DepartmentId";

			return WithConnectionAsync(connection => connection.QueryAsync<DepartmentMemberSensitiveData>(
				sql, new { DepartmentId = departmentId }, _unitOfWork?.Transaction));
		}

		public Task<IEnumerable<int>> GetDepartmentIdsWithOutstandingLegacyProfileDataAsync()
		{
			// "Outstanding" is the ABSENCE of the relocation marker, not an empty target column: a
			// member who cleared their department identification number has an empty target and must
			// not be swept forever. A member with no row at all is outstanding by definition.
			var sql = _isPostgres
				? $@"SELECT DISTINCT dm.departmentid
FROM departmentmembers dm
INNER JOIN userprofiles up ON up.userid = dm.userid
LEFT JOIN {_table} s ON s.departmentid = dm.departmentid AND s.userid = dm.userid
WHERE dm.isdeleted = false
  AND s.legacyprofilerelocatedon IS NULL
  AND (up.homeaddressid IS NOT NULL OR up.mailingaddressid IS NOT NULL
       OR (up.identificationnumber IS NOT NULL AND btrim(up.identificationnumber) <> ''))"
				: $@"SELECT DISTINCT dm.[DepartmentId]
FROM [DepartmentMembers] dm
INNER JOIN [UserProfiles] up ON up.[UserId] = dm.[UserId]
LEFT JOIN {_table} s ON s.[DepartmentId] = dm.[DepartmentId] AND s.[UserId] = dm.[UserId]
WHERE dm.[IsDeleted] = 0
  AND s.[LegacyProfileRelocatedOn] IS NULL
  AND (up.[HomeAddressId] IS NOT NULL OR up.[MailingAddressId] IS NOT NULL
       OR (up.[IdentificationNumber] IS NOT NULL AND LTRIM(RTRIM(up.[IdentificationNumber])) <> ''))";

			return WithConnectionAsync(connection => connection.QueryAsync<int>(sql, null, _unitOfWork?.Transaction));
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
