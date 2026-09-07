using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository
{
	/// <summary>Work assignments (RMS-1D, registry M0179). Dapper over RmsRepositoryBase; list parameters go through InList so Postgres binds arrays.</summary>
	public class RmsRecordWorkAssignmentsRepository : RmsRepositoryBase<RmsRecordWorkAssignment>, IRmsRecordWorkAssignmentsRepository
	{
		public RmsRecordWorkAssignmentsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<RmsRecordWorkAssignment> GetByIdForDepartmentAsync(int departmentId, string assignmentId)
		{
			return QueryFirstOrDefaultAsync<RmsRecordWorkAssignment>(
				$"SELECT * FROM {Tbl("RmsRecordWorkAssignments")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsRecordWorkAssignmentId")} = {P}Id",
				new { DepartmentId = departmentId, Id = assignmentId });
		}

		public Task<IEnumerable<RmsRecordWorkAssignment>> GetForRecordAsync(int departmentId, string recordId)
		{
			return QueryAsync<RmsRecordWorkAssignment>(
				$"SELECT * FROM {Tbl("RmsRecordWorkAssignments")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RecordId")} = {P}RecordId AND {Col("DeletedOn")} IS NULL ORDER BY {Col("CreatedOn")}",
				new { DepartmentId = departmentId, RecordId = recordId });
		}

		public Task<IEnumerable<RmsRecordWorkAssignment>> GetOpenForAssigneesAsync(int departmentId, string userId, IEnumerable<int> unitIds, IEnumerable<int> groupIds, IEnumerable<string> roles, int take)
		{
			var units = InListValue(unitIds);
			var groups = InListValue(groupIds);
			var roleList = (roles ?? Enumerable.Empty<string>()).Where(r => !string.IsNullOrWhiteSpace(r)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
			var clauses = new List<string>();
			var parameters = new DynamicParameters();
			parameters.Add("DepartmentId", departmentId);
			parameters.Add("Skip", 0);
			parameters.Add("Take", take <= 0 ? 200 : Math.Min(take, 1000));
			if (!string.IsNullOrWhiteSpace(userId))
			{
				clauses.Add($"({Col("AssigneeKind")} = {(int)RmsWorkAssigneeKind.Person} AND {Col("AssigneeUserId")} = {P}UserId)");
				parameters.Add("UserId", userId);
			}
			if (units.Length > 0)
			{
				clauses.Add($"({Col("AssigneeKind")} = {(int)RmsWorkAssigneeKind.Unit} AND {InList("AssigneeUnitId", "UnitIds")})");
				parameters.Add("UnitIds", units);
			}
			if (groups.Length > 0)
			{
				clauses.Add($"({Col("AssigneeKind")} = {(int)RmsWorkAssigneeKind.Group} AND {InList("AssigneeGroupId", "GroupIds")})");
				parameters.Add("GroupIds", groups);
			}
			if (roleList.Length > 0)
			{
				clauses.Add($"({Col("AssigneeKind")} IN ({(int)RmsWorkAssigneeKind.CommandRole}, {(int)RmsWorkAssigneeKind.DispatchRole}) AND {InList("AssigneeRole", "Roles")})");
				parameters.Add("Roles", roleList);
			}
			if (clauses.Count == 0)
				return Task.FromResult<IEnumerable<RmsRecordWorkAssignment>>(new List<RmsRecordWorkAssignment>());

			return QueryAsync<RmsRecordWorkAssignment>(
				$"SELECT * FROM {Tbl("RmsRecordWorkAssignments")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("DeletedOn")} IS NULL AND {Col("State")} IN ({(int)RmsWorkAssignmentState.Open}, {(int)RmsWorkAssignmentState.Acknowledged}) AND ({string.Join(" OR ", clauses)}) ORDER BY {Col("DueOn")}, {Col("CreatedOn")}, {Col("RmsRecordWorkAssignmentId")} {Paging()}",
				parameters);
		}

		public Task<IEnumerable<RmsRecordWorkAssignment>> GetModifiedSinceAsync(int departmentId, DateTime? since, int take)
		{
			var parameters = new DynamicParameters();
			parameters.Add("DepartmentId", departmentId);
			parameters.Add("Skip", 0);
			parameters.Add("Take", take <= 0 ? 200 : Math.Min(take, 1000));
			var sinceClause = string.Empty;
			if (since.HasValue)
			{
				sinceClause = $" AND {Col("ModifiedOn")} > {P}Since";
				parameters.Add("Since", since.Value);
			}
			return QueryAsync<RmsRecordWorkAssignment>(
				$"SELECT * FROM {Tbl("RmsRecordWorkAssignments")} WHERE {Col("DepartmentId")} = {P}DepartmentId{sinceClause} ORDER BY {Col("ModifiedOn")}, {Col("RmsRecordWorkAssignmentId")} {Paging()}",
				parameters);
		}
	}
}
