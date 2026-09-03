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
		private readonly string _schema;

		public DepartmentMemberSensitiveDataRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration,
			IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_unitOfWork = unitOfWork;
			_isPostgres = DataConfig.DatabaseType == DatabaseTypes.Postgres;
			_schema = sqlConfiguration.SchemaName;
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
			// Every table is schema-qualified: _table already carries the configured schema, so
			// leaving the joins bare would only work while the connection's default schema happens
			// to match.
			//
			// "Outstanding" is the ABSENCE of the relocation marker, not an empty target column: a
			// member who cleared their department identification number has an empty target and must
			// not be swept forever. A member with no row at all is outstanding by definition.
			return WithConnectionAsync(async connection =>
			{
				// QA may already have run the original M0141 contract while production deliberately
				// retains this column during the relocation window. Detect the deployed shape before
				// composing the query; merely guarding a missing column inside SQL still fails when the
				// database parses that column reference.
				var columnExistsSql = _isPostgres
					? @"SELECT COUNT(*) FROM information_schema.columns
WHERE table_schema = @SchemaName AND table_name = 'userprofiles' AND column_name = 'identificationnumber'"
					: @"SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_SCHEMA = @SchemaName AND TABLE_NAME = 'UserProfiles' AND COLUMN_NAME = 'IdentificationNumber'";

				var legacyIdentificationNumberExists = await connection.ExecuteScalarAsync<long>(columnExistsSql,
					new { SchemaName = _schema.Trim('[', ']') }, _unitOfWork?.Transaction) > 0;

				var legacyIdentificationNumberPredicate = legacyIdentificationNumberExists
					? _isPostgres
						? " OR (up.identificationnumber IS NOT NULL AND btrim(up.identificationnumber) <> '')"
						: " OR (up.[IdentificationNumber] IS NOT NULL AND LTRIM(RTRIM(up.[IdentificationNumber])) <> '')"
					: string.Empty;

				var sql = _isPostgres
				? $@"SELECT DISTINCT dm.departmentid
FROM {_schema}.departmentmembers dm
INNER JOIN {_schema}.userprofiles up ON up.userid = dm.userid
LEFT JOIN {_table} s ON s.departmentid = dm.departmentid AND s.userid = dm.userid
WHERE dm.isdeleted = false
  AND s.legacyprofilerelocatedon IS NULL
  AND (up.homeaddressid IS NOT NULL OR up.mailingaddressid IS NOT NULL{legacyIdentificationNumberPredicate})"
				: $@"SELECT DISTINCT dm.[DepartmentId]
FROM {_schema}.[DepartmentMembers] dm
INNER JOIN {_schema}.[UserProfiles] up ON up.[UserId] = dm.[UserId]
LEFT JOIN {_table} s ON s.[DepartmentId] = dm.[DepartmentId] AND s.[UserId] = dm.[UserId]
WHERE dm.[IsDeleted] = 0
  AND s.[LegacyProfileRelocatedOn] IS NULL
  AND (up.[HomeAddressId] IS NOT NULL OR up.[MailingAddressId] IS NOT NULL{legacyIdentificationNumberPredicate})";

				return await connection.QueryAsync<int>(sql, null, _unitOfWork?.Transaction);
			});
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
