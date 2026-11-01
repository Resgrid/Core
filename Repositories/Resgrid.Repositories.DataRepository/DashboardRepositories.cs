using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository
{
	/// <summary>Members' saved dashboard boards (M0307).</summary>
	public class UserDashboardsRepository : RmsRepositoryBase<UserDashboard>, IUserDashboardsRepository
	{
		public UserDashboardsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		private string UserIdParameter => IsPostgres ? $"{P}UserId::citext" : $"{P}UserId";

		public async Task<List<UserDashboard>> GetForUserAsync(int departmentId, string userId)
		{
			var rows = await QueryAsync<UserDashboard>(
				$"SELECT * FROM {Tbl("UserDashboards")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("UserId")} = {UserIdParameter} ORDER BY {Col("SortOrder")}",
				new { DepartmentId = departmentId, UserId = userId });

			return rows.ToList();
		}

		public Task<UserDashboard> GetByIdForUserAsync(string userDashboardId, int departmentId, string userId)
		{
			return QueryFirstOrDefaultAsync<UserDashboard>(
				$"SELECT * FROM {Tbl("UserDashboards")} WHERE {Col("UserDashboardId")} = {P}UserDashboardId AND {Col("DepartmentId")} = {P}DepartmentId AND {Col("UserId")} = {UserIdParameter}",
				new { UserDashboardId = userDashboardId, DepartmentId = departmentId, UserId = userId });
		}

		public async Task<bool> UpdateWithRowVersionAsync(UserDashboard board, int expectedRowVersion, CancellationToken cancellationToken)
		{
			var affected = await ExecuteAsync(
				$"UPDATE {Tbl("UserDashboards")} SET {Col("LayoutJson")} = {P}LayoutJson, {Col("SchemaVersion")} = {P}SchemaVersion, {Col("Name")} = {P}Name, " +
				$"{Col("TemplateId")} = {P}TemplateId, {Col("UseClassic")} = {P}UseClassic, {Col("UpdatedOn")} = {P}UpdatedOn, {Col("RowVersion")} = {P}NextRowVersion " +
				$"WHERE {Col("UserDashboardId")} = {P}UserDashboardId AND {Col("DepartmentId")} = {P}DepartmentId AND {Col("RowVersion")} = {P}ExpectedRowVersion",
				new
				{
					board.LayoutJson,
					board.SchemaVersion,
					board.Name,
					board.TemplateId,
					board.UseClassic,
					UpdatedOn = DatabaseTimestamp(board.UpdatedOn),
					NextRowVersion = expectedRowVersion + 1,
					board.UserDashboardId,
					board.DepartmentId,
					ExpectedRowVersion = expectedRowVersion
				}, cancellationToken);

			if (affected == 1)
				board.RowVersion = expectedRowVersion + 1;

			return affected == 1;
		}

		public Task<int> SetUseClassicAsync(int departmentId, string userId, bool useClassic, CancellationToken cancellationToken)
		{
			return ExecuteAsync(
				$"UPDATE {Tbl("UserDashboards")} SET {Col("UseClassic")} = {P}UseClassic, {Col("UpdatedOn")} = {P}UpdatedOn " +
				$"WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("UserId")} = {UserIdParameter}",
				new { UseClassic = useClassic, UpdatedOn = DatabaseTimestamp(DateTime.UtcNow), DepartmentId = departmentId, UserId = userId }, cancellationToken);
		}

		public Task<int> DeleteForUserAsync(int departmentId, string userId, CancellationToken cancellationToken)
		{
			return ExecuteAsync(
				$"DELETE FROM {Tbl("UserDashboards")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("UserId")} = {UserIdParameter}",
				new { DepartmentId = departmentId, UserId = userId }, cancellationToken);
		}
	}

	/// <summary>Department dashboard templates (M0307).</summary>
	public class DepartmentDashboardTemplatesRepository : RmsRepositoryBase<DepartmentDashboardTemplate>, IDepartmentDashboardTemplatesRepository
	{
		public DepartmentDashboardTemplatesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<List<DepartmentDashboardTemplate>> GetForDepartmentAsync(int departmentId)
		{
			var rows = await QueryAsync<DepartmentDashboardTemplate>(
				$"SELECT * FROM {Tbl("DepartmentDashboardTemplates")} WHERE {Col("DepartmentId")} = {P}DepartmentId ORDER BY {Col("Priority")}, {Col("Name")}",
				new { DepartmentId = departmentId });

			return rows.ToList();
		}

		public Task<DepartmentDashboardTemplate> GetByIdForDepartmentAsync(string departmentDashboardTemplateId, int departmentId)
		{
			return QueryFirstOrDefaultAsync<DepartmentDashboardTemplate>(
				$"SELECT * FROM {Tbl("DepartmentDashboardTemplates")} WHERE {Col("DepartmentDashboardTemplateId")} = {P}TemplateId AND {Col("DepartmentId")} = {P}DepartmentId",
				new { TemplateId = departmentDashboardTemplateId, DepartmentId = departmentId });
		}

		public Task<int> ClearDefaultAsync(int departmentId, string exceptTemplateId, CancellationToken cancellationToken)
		{
			return ExecuteAsync(
				$"UPDATE {Tbl("DepartmentDashboardTemplates")} SET {Col("IsDefault")} = {P}NotDefault " +
				$"WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("IsDefault")} = {P}IsDefault AND {Col("DepartmentDashboardTemplateId")} <> {P}ExceptId",
				new { NotDefault = false, IsDefault = true, DepartmentId = departmentId, ExceptId = exceptTemplateId ?? string.Empty }, cancellationToken);
		}
	}
}
