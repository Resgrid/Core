using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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

	/// <summary>
	/// Field Records rollout telemetry (RMS-1D, registry M0180). Counts and coded outcomes only; the table is
	/// operational and is pruned by window, so nothing here is a record of what anybody wrote.
	/// </summary>
	public class RmsFieldRolloutEventsRepository : RmsRepositoryBase<RmsFieldRolloutEvent>, IRmsFieldRolloutEventsRepository
	{
		public RmsFieldRolloutEventsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<IEnumerable<RmsFieldRolloutEvent>> GetForWindowAsync(int departmentId, DateTime sinceUtc, int take)
		{
			var parameters = new DynamicParameters();
			parameters.Add("DepartmentId", departmentId);
			parameters.Add("Since", sinceUtc);
			parameters.Add("Skip", 0);
			parameters.Add("Take", take <= 0 ? 20000 : Math.Min(take, 200000));
			return QueryAsync<RmsFieldRolloutEvent>(
				$"SELECT * FROM {Tbl("RmsFieldRolloutEvents")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("OccurredOn")} >= {P}Since ORDER BY {Col("OccurredOn")}, {Col("RmsFieldRolloutEventId")} {Paging()}",
				parameters);
		}

		public async Task<int> InsertBatchAsync(IEnumerable<RmsFieldRolloutEvent> events, CancellationToken cancellationToken = default)
		{
			var rows = (events ?? Enumerable.Empty<RmsFieldRolloutEvent>()).ToList();
			if (rows.Count == 0)
				return 0;

			var sql = $@"INSERT INTO {Tbl("RmsFieldRolloutEvents")} ({Cols("RmsFieldRolloutEventId", "DepartmentId", "OriginClient", "AppVersion", "ClientCapability", "EventType", "Outcome", "DefinitionKey", "DefinitionVersion", "RecordId", "UserId", "DurationMs", "ItemCount", "OccurredOn", "RecordedOn")})
				VALUES ({P}RmsFieldRolloutEventId, {P}DepartmentId, {P}OriginClient, {P}AppVersion, {P}ClientCapability, {P}EventType, {P}Outcome, {P}DefinitionKey, {P}DefinitionVersion, {P}RecordId, {P}UserId, {P}DurationMs, {P}ItemCount, {P}OccurredOn, {P}RecordedOn)";
			// Dapper expands the list into one multi-statement command; the whole batch lands in a single round trip.
			return await ExecuteAsync(sql, rows, cancellationToken);
		}

		public Task<int> DeleteOlderThanAsync(int departmentId, DateTime cutoffUtc, CancellationToken cancellationToken = default)
		{
			return ExecuteAsync(
				$"DELETE FROM {Tbl("RmsFieldRolloutEvents")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("OccurredOn")} < {P}Cutoff",
				new { DepartmentId = departmentId, Cutoff = cutoffUtc }, cancellationToken);
		}
	}
}
