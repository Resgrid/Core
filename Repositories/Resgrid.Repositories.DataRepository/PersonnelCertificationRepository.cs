using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading.Tasks;
using Dapper;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;
using System.Linq;
using Resgrid.Repositories.DataRepository.Queries.Certifications;

namespace Resgrid.Repositories.DataRepository
{
	public class PersonnelCertificationRepository : RmsRepositoryBase<PersonnelCertification>, IPersonnelCertificationRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IQueryFactory _queryFactory;
		private readonly IUnitOfWork _unitOfWork;

		public PersonnelCertificationRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_queryFactory = queryFactory;
			_unitOfWork = unitOfWork;
		}

		public async Task<IEnumerable<PersonnelCertification>> GetCertificationsByUserAsync(string userId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<PersonnelCertification>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("UserId", userId);

					var query = _queryFactory.GetQuery<SelectCertsByUserQuery>();

					return await x.QueryAsync<PersonnelCertification>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction);
				});

				DbConnection conn = null;
				if (_unitOfWork?.Connection == null)
				{
					using (conn = _connectionProvider.Create())
					{
						await conn.OpenAsync();

						return await selectFunction(conn);
					}
				}
				else
				{
					conn = _unitOfWork.CreateOrGetConnection();

					return await selectFunction(conn);
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);

				throw;
			}
		}

		private static string False => IsPostgres ? "FALSE" : "0";

		public Task<IEnumerable<PersonnelCertification>> GetForDepartmentAsync(int departmentId, IEnumerable<string> userIds = null)
		{
			var users = InListValue(userIds);
			return QueryAsync<PersonnelCertification>(
				$"SELECT {Cols(CertificationColumns.PersonnelMeta)} FROM {Tbl("PersonnelCertifications")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("IsDeleted")} = {False}" +
				(userIds != null ? $" AND {InList("UserId", "Users")}" : string.Empty) + $" ORDER BY {Col("UserId")}, {Col("ExpiresOn")}",
				new { DepartmentId = departmentId, Users = users });
		}

		public Task<IEnumerable<PersonnelCertification>> GetByTypeIdsAsync(int departmentId, IEnumerable<int> departmentCertificationTypeIds) =>
			QueryAsync<PersonnelCertification>(
				$"SELECT {Cols(CertificationColumns.PersonnelMeta)} FROM {Tbl("PersonnelCertifications")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("IsDeleted")} = {False} AND {InList("DepartmentCertificationTypeId", "TypeIds")} ORDER BY {Col("UserId")}, {Col("ExpiresOn")}",
				new { DepartmentId = departmentId, TypeIds = InListValue(departmentCertificationTypeIds) });

		public Task<IEnumerable<PersonnelCertification>> GetExpiringAsync(int departmentId, DateTime onOrBefore) =>
			QueryAsync<PersonnelCertification>(
				$"SELECT {Cols(CertificationColumns.PersonnelMeta)} FROM {Tbl("PersonnelCertifications")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("IsDeleted")} = {False} AND {Col("ExpiresOn")} IS NOT NULL AND {Col("ExpiresOn")} <= {P}OnOrBefore ORDER BY {Col("ExpiresOn")}",
				new { DepartmentId = departmentId, OnOrBefore = DatabaseTimestamp(onOrBefore) });

		public Task<IEnumerable<int>> GetDepartmentIdsWithTypedRecordsAsync() =>
			QueryAsync<int>($"SELECT DISTINCT {Col("DepartmentId")} FROM {Tbl("PersonnelCertifications")} WHERE {Col("DepartmentCertificationTypeId")} IS NOT NULL AND {Col("IsDeleted")} = {False}", new { });

		public Task<int> CountByTypeIdAsync(int departmentCertificationTypeId) =>
			ScalarAsync<int>($"SELECT COUNT(*) FROM {Tbl("PersonnelCertifications")} WHERE {Col("DepartmentCertificationTypeId")} = {P}TypeId AND {Col("IsDeleted")} = {False}", new { TypeId = departmentCertificationTypeId });
	}
}
