using System.Collections.Generic;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository
{
	public class DepartmentCertificationTypeRepository : RmsRepositoryBase<DepartmentCertificationType>, IDepartmentCertificationTypeRepository
	{
		public DepartmentCertificationTypeRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		private static string False => IsPostgres ? "FALSE" : "0";
		private static string True => IsPostgres ? "TRUE" : "1";

		public Task<DepartmentCertificationType> GetByCodeAsync(int departmentId, string code) =>
			QueryFirstOrDefaultAsync<DepartmentCertificationType>(
				$"SELECT * FROM {Tbl("DepartmentCertificationTypes")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("Code")} = {P}Code AND {Col("IsDeleted")} = {False}",
				new { DepartmentId = departmentId, Code = code ?? string.Empty });

		public Task<IEnumerable<DepartmentCertificationType>> GetByCodesAsync(int departmentId, IEnumerable<string> codes) =>
			QueryAsync<DepartmentCertificationType>(
				$"SELECT * FROM {Tbl("DepartmentCertificationTypes")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("IsDeleted")} = {False} AND {InList("Code", "Codes")}",
				new { DepartmentId = departmentId, Codes = InListValue(codes) });

		public Task<IEnumerable<DepartmentCertificationType>> GetActiveForDepartmentAsync(int departmentId, int? appliesTo = null) =>
			QueryAsync<DepartmentCertificationType>(
				$"SELECT * FROM {Tbl("DepartmentCertificationTypes")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("IsDeleted")} = {False} AND {Col("IsActive")} = {True}" +
				(appliesTo.HasValue ? $" AND {Col("AppliesTo")} = {P}AppliesTo" : string.Empty) + $" ORDER BY {Col("Category")}, {Col("Type")}",
				new { DepartmentId = departmentId, AppliesTo = appliesTo ?? 0 });
	}
}
