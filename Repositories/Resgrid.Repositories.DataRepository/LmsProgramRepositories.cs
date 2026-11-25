using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Resgrid.Model;
using Resgrid.Model.Lms;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository
{
	// Training T3 part 2 (round 4C, M0387–M0390). Portable SQL over RmsRepositoryBase; member ids are citext on PostgreSQL, so their
	// parameters are cast (pg-citext-text-param-equality); list parameters use InList (postgres-dapper-in-list).

	public class LmsTrainingPlansRepository : RmsRepositoryBase<LmsTrainingPlan>, ILmsTrainingPlansRepository
	{
		public LmsTrainingPlansRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<List<LmsTrainingPlan>> GetForDepartmentAsync(int departmentId, bool includeDeleted = false) =>
			(await QueryAsync<LmsTrainingPlan>(
				$"SELECT * FROM {Tbl("LmsTrainingPlans")} WHERE {Col("DepartmentId")} = {P}DepartmentId" +
				(includeDeleted ? string.Empty : $" AND {Col("IsDeleted")} = {P}No") +
				$" ORDER BY {Col("Year")} DESC, {Col("Name")}, {Col("LmsTrainingPlanId")}",
				new { DepartmentId = departmentId, No = false })).ToList();

		public Task<LmsTrainingPlan> GetByIdForDepartmentAsync(int departmentId, int lmsTrainingPlanId) =>
			QueryFirstOrDefaultAsync<LmsTrainingPlan>(
				$"SELECT * FROM {Tbl("LmsTrainingPlans")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsTrainingPlanId")} = {P}Id",
				new { DepartmentId = departmentId, Id = lmsTrainingPlanId });
	}

	public class LmsTrainingPlanItemsRepository : RmsRepositoryBase<LmsTrainingPlanItem>, ILmsTrainingPlanItemsRepository
	{
		public LmsTrainingPlanItemsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<List<LmsTrainingPlanItem>> GetForPlanAsync(int departmentId, int lmsTrainingPlanId) =>
			(await QueryAsync<LmsTrainingPlanItem>(
				$"SELECT * FROM {Tbl("LmsTrainingPlanItems")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsTrainingPlanId")} = {P}PlanId " +
				$"ORDER BY {Col("Month")}, {Col("PlannedOn")}, {Col("LmsTrainingPlanItemId")}",
				new { DepartmentId = departmentId, PlanId = lmsTrainingPlanId })).ToList();

		public Task<LmsTrainingPlanItem> GetByIdForDepartmentAsync(int departmentId, int lmsTrainingPlanItemId) =>
			QueryFirstOrDefaultAsync<LmsTrainingPlanItem>(
				$"SELECT * FROM {Tbl("LmsTrainingPlanItems")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsTrainingPlanItemId")} = {P}Id",
				new { DepartmentId = departmentId, Id = lmsTrainingPlanItemId });
	}

	public class LmsCompetencyFrameworksRepository : RmsRepositoryBase<LmsCompetencyFramework>, ILmsCompetencyFrameworksRepository
	{
		public LmsCompetencyFrameworksRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<List<LmsCompetencyFramework>> GetForDepartmentAsync(int departmentId, bool includeDeleted = false) =>
			(await QueryAsync<LmsCompetencyFramework>(
				$"SELECT * FROM {Tbl("LmsCompetencyFrameworks")} WHERE {Col("DepartmentId")} = {P}DepartmentId" +
				(includeDeleted ? string.Empty : $" AND {Col("IsDeleted")} = {P}No") +
				$" ORDER BY {Col("Name")}, {Col("LmsCompetencyFrameworkId")}",
				new { DepartmentId = departmentId, No = false })).ToList();

		public Task<LmsCompetencyFramework> GetByIdForDepartmentAsync(int departmentId, int lmsCompetencyFrameworkId) =>
			QueryFirstOrDefaultAsync<LmsCompetencyFramework>(
				$"SELECT * FROM {Tbl("LmsCompetencyFrameworks")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsCompetencyFrameworkId")} = {P}Id",
				new { DepartmentId = departmentId, Id = lmsCompetencyFrameworkId });

		public Task<LmsCompetencyFramework> GetByCaseDocumentIdAsync(int departmentId, string caseDocumentId) =>
			QueryFirstOrDefaultAsync<LmsCompetencyFramework>(
				$"SELECT * FROM {Tbl("LmsCompetencyFrameworks")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("CaseDocumentId")} = {CaseInsensitive("CaseId")} " +
				$"AND {Col("IsDeleted")} = {P}No ORDER BY {Col("LmsCompetencyFrameworkId")}",
				new { DepartmentId = departmentId, CaseId = caseDocumentId, No = false });
	}

	public class LmsCompetencyItemsRepository : RmsRepositoryBase<LmsCompetencyItem>, ILmsCompetencyItemsRepository
	{
		public LmsCompetencyItemsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<List<LmsCompetencyItem>> GetForFrameworkAsync(int departmentId, int lmsCompetencyFrameworkId, bool includeDeleted = false) =>
			(await QueryAsync<LmsCompetencyItem>(
				$"SELECT * FROM {Tbl("LmsCompetencyItems")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsCompetencyFrameworkId")} = {P}FrameworkId" +
				(includeDeleted ? string.Empty : $" AND {Col("IsDeleted")} = {P}No") +
				$" ORDER BY {Col("SortOrder")}, {Col("LmsCompetencyItemId")}",
				new { DepartmentId = departmentId, FrameworkId = lmsCompetencyFrameworkId, No = false })).ToList();

		public Task<LmsCompetencyItem> GetByIdForDepartmentAsync(int departmentId, int lmsCompetencyItemId) =>
			QueryFirstOrDefaultAsync<LmsCompetencyItem>(
				$"SELECT * FROM {Tbl("LmsCompetencyItems")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsCompetencyItemId")} = {P}Id",
				new { DepartmentId = departmentId, Id = lmsCompetencyItemId });

		public Task SetParentAsync(int departmentId, int lmsCompetencyItemId, int? parentItemId, CancellationToken cancellationToken = default) =>
			ExecuteAsync(
				$"UPDATE {Tbl("LmsCompetencyItems")} SET {Col("ParentItemId")} = {P}ParentId WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsCompetencyItemId")} = {P}Id",
				new { DepartmentId = departmentId, Id = lmsCompetencyItemId, ParentId = parentItemId }, cancellationToken);
	}

	public class LmsCompetencyAlignmentsRepository : RmsRepositoryBase<LmsCompetencyAlignment>, ILmsCompetencyAlignmentsRepository
	{
		private static readonly string[] Columns = { "DepartmentId", "LmsCompetencyItemId", "SubjectKind", "SubjectId", "CreatedOn", "CreatedByUserId" };

		public LmsCompetencyAlignmentsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<List<LmsCompetencyAlignment>> GetForDepartmentAsync(int departmentId) =>
			(await QueryAsync<LmsCompetencyAlignment>(
				$"SELECT * FROM {Tbl("LmsCompetencyAlignments")} WHERE {Col("DepartmentId")} = {P}DepartmentId ORDER BY {Col("LmsCompetencyAlignmentId")}",
				new { DepartmentId = departmentId })).ToList();

		public async Task<List<LmsCompetencyAlignment>> GetForItemsAsync(int departmentId, IEnumerable<int> lmsCompetencyItemIds)
		{
			var ids = InListValue((lmsCompetencyItemIds ?? Enumerable.Empty<int>()).Distinct());
			var result = new List<LmsCompetencyAlignment>();
			if (ids.Length == 0)
				return result;
			foreach (var chunk in ids.Chunk(1000))
				result.AddRange(await QueryAsync<LmsCompetencyAlignment>(
					$"SELECT * FROM {Tbl("LmsCompetencyAlignments")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {InList("LmsCompetencyItemId", "Ids")} " +
					$"ORDER BY {Col("LmsCompetencyAlignmentId")}",
					new { DepartmentId = departmentId, Ids = chunk }));
			return result;
		}

		public async Task<List<LmsCompetencyAlignment>> GetForSubjectAsync(int departmentId, int subjectKind, string subjectId) =>
			(await QueryAsync<LmsCompetencyAlignment>(
				$"SELECT * FROM {Tbl("LmsCompetencyAlignments")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("SubjectKind")} = {P}Kind " +
				$"AND {Col("SubjectId")} = {CaseInsensitive("SubjectId")} ORDER BY {Col("LmsCompetencyAlignmentId")}",
				new { DepartmentId = departmentId, Kind = subjectKind, SubjectId = subjectId })).ToList();

		public async Task<bool> AddAsync(LmsCompetencyAlignment alignment, CancellationToken cancellationToken = default)
		{
			if (alignment == null || alignment.DepartmentId <= 0 || string.IsNullOrWhiteSpace(alignment.SubjectId))
				return false;

			var parameters = new DynamicParameters();
			parameters.Add("DepartmentId", alignment.DepartmentId);
			parameters.Add("LmsCompetencyItemId", alignment.LmsCompetencyItemId);
			parameters.Add("SubjectKind", alignment.SubjectKind);
			parameters.Add("SubjectId", alignment.SubjectId);
			parameters.Add("CreatedOn", DatabaseTimestamp(alignment.CreatedOn), System.Data.DbType.DateTime2);
			parameters.Add("CreatedByUserId", alignment.CreatedByUserId);

			var values = string.Join(", ", Columns.Select(c => P + c));
			var sql = IsPostgres
				? $"INSERT INTO {Tbl("LmsCompetencyAlignments")} ({Cols(Columns)}) VALUES ({values}) " +
				  $"ON CONFLICT ({Cols("DepartmentId", "LmsCompetencyItemId", "SubjectKind", "SubjectId")}) DO NOTHING"
				: $"INSERT INTO {Tbl("LmsCompetencyAlignments")} ({Cols(Columns)}) SELECT {values} WHERE NOT EXISTS (SELECT 1 FROM {Tbl("LmsCompetencyAlignments")} " +
				  $"WITH (UPDLOCK, HOLDLOCK) WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsCompetencyItemId")} = {P}LmsCompetencyItemId " +
				  $"AND {Col("SubjectKind")} = {P}SubjectKind AND {Col("SubjectId")} = {P}SubjectId)";
			try
			{
				return await ExecuteAsync(sql, parameters, cancellationToken) == 1;
			}
			catch (Exception ex) when (IsUniqueViolation(ex))
			{
				return false;
			}
		}

		public Task<int> RemoveAsync(int departmentId, int lmsCompetencyAlignmentId, CancellationToken cancellationToken = default) =>
			ExecuteAsync($"DELETE FROM {Tbl("LmsCompetencyAlignments")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsCompetencyAlignmentId")} = {P}Id",
				new { DepartmentId = departmentId, Id = lmsCompetencyAlignmentId }, cancellationToken);
	}

	public class LmsTrainingRequestsRepository : RmsRepositoryBase<LmsTrainingRequest>, ILmsTrainingRequestsRepository
	{
		public LmsTrainingRequestsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<LmsTrainingRequest> GetByIdForDepartmentAsync(int departmentId, int lmsTrainingRequestId) =>
			QueryFirstOrDefaultAsync<LmsTrainingRequest>(
				$"SELECT * FROM {Tbl("LmsTrainingRequests")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsTrainingRequestId")} = {P}Id",
				new { DepartmentId = departmentId, Id = lmsTrainingRequestId });

		public async Task<List<LmsTrainingRequest>> GetForUserAsync(int departmentId, string userId) =>
			(await QueryAsync<LmsTrainingRequest>(
				$"SELECT * FROM {Tbl("LmsTrainingRequests")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("UserId")} = {CaseInsensitive("UserId")} " +
				$"ORDER BY {Col("SubmittedOn")} DESC, {Col("LmsTrainingRequestId")} DESC",
				new { DepartmentId = departmentId, UserId = userId })).ToList();

		public async Task<List<LmsTrainingRequest>> GetForDepartmentAsync(int departmentId, DateTime? fromUtc, DateTime? toUtc, int take = 2000)
		{
			var parameters = new DynamicParameters();
			parameters.Add("DepartmentId", departmentId);
			var where = $"{Col("DepartmentId")} = {P}DepartmentId";
			if (fromUtc.HasValue)
			{
				where += $" AND {Col("SubmittedOn")} >= {P}FromUtc";
				parameters.Add("FromUtc", DatabaseTimestamp(fromUtc.Value), System.Data.DbType.DateTime2);
			}
			if (toUtc.HasValue)
			{
				where += $" AND {Col("SubmittedOn")} < {P}ToUtc";
				parameters.Add("ToUtc", DatabaseTimestamp(toUtc.Value), System.Data.DbType.DateTime2);
			}

			var limit = Math.Clamp(take, 1, 5000);
			var sql = IsPostgres
				? $"SELECT * FROM {Tbl("LmsTrainingRequests")} WHERE {where} ORDER BY {Col("SubmittedOn")} DESC, {Col("LmsTrainingRequestId")} DESC LIMIT {limit}"
				: $"SELECT TOP ({limit}) * FROM {Tbl("LmsTrainingRequests")} WHERE {where} ORDER BY {Col("SubmittedOn")} DESC, {Col("LmsTrainingRequestId")} DESC";
			return (await QueryAsync<LmsTrainingRequest>(sql, parameters)).ToList();
		}

		public async Task<List<LmsTrainingRequest>> GetOpenAsync(int departmentId) =>
			(await QueryAsync<LmsTrainingRequest>(
				$"SELECT * FROM {Tbl("LmsTrainingRequests")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("Status")} = {P}Status " +
				$"ORDER BY {Col("SubmittedOn")}, {Col("LmsTrainingRequestId")}",
				new { DepartmentId = departmentId, Status = (int)LmsTrainingRequestStatuses.Submitted })).ToList();

		public async Task<bool> TryTransitionAsync(LmsTrainingRequest request, int expectedStatus, int? expectedStep, CancellationToken cancellationToken = default)
		{
			if (request == null || request.LmsTrainingRequestId <= 0)
				return false;

			var parameters = new DynamicParameters();
			parameters.Add("DepartmentId", request.DepartmentId);
			parameters.Add("Id", request.LmsTrainingRequestId);
			parameters.Add("Status", request.Status);
			parameters.Add("CurrentStep", request.CurrentStep, System.Data.DbType.Int32);
			parameters.Add("DecidedOn", request.DecidedOn.HasValue ? DatabaseTimestamp(request.DecidedOn.Value) : (DateTime?)null, System.Data.DbType.DateTime2);
			parameters.Add("DecidedByUserId", request.DecidedByUserId, System.Data.DbType.String);
			parameters.Add("LearningRecordId", request.LearningRecordId, System.Data.DbType.Int32);
			parameters.Add("CompletedOn", request.CompletedOn.HasValue ? DatabaseTimestamp(request.CompletedOn.Value) : (DateTime?)null, System.Data.DbType.DateTime2);
			parameters.Add("ActualCost", request.ActualCost, System.Data.DbType.Decimal);
			parameters.Add("Hours", request.Hours);
			parameters.Add("UpdatedOn", DatabaseTimestamp(request.UpdatedOn ?? DateTime.UtcNow), System.Data.DbType.DateTime2);
			parameters.Add("ExpectedStatus", expectedStatus);

			// The expected step decides the SQL shape in C#: an untyped null in "IS NULL" fails on PostgreSQL (pg-untyped-null-param-is-null).
			var stepFilter = expectedStep.HasValue ? $"{Col("CurrentStep")} = {P}ExpectedStep" : $"{Col("CurrentStep")} IS NULL";
			if (expectedStep.HasValue)
				parameters.Add("ExpectedStep", expectedStep.Value);

			var sql = $"UPDATE {Tbl("LmsTrainingRequests")} SET {Col("Status")} = {P}Status, {Col("CurrentStep")} = {P}CurrentStep, {Col("DecidedOn")} = {P}DecidedOn, " +
				$"{Col("DecidedByUserId")} = {P}DecidedByUserId, {Col("LearningRecordId")} = {P}LearningRecordId, {Col("CompletedOn")} = {P}CompletedOn, " +
				$"{Col("ActualCost")} = {P}ActualCost, {Col("Hours")} = {P}Hours, {Col("UpdatedOn")} = {P}UpdatedOn " +
				$"WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsTrainingRequestId")} = {P}Id AND {Col("Status")} = {P}ExpectedStatus AND {stepFilter}";
			return await ExecuteAsync(sql, parameters, cancellationToken) == 1;
		}
	}

	public class LmsTrainingRequestDecisionsRepository : RmsRepositoryBase<LmsTrainingRequestDecision>, ILmsTrainingRequestDecisionsRepository
	{
		public LmsTrainingRequestDecisionsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<List<LmsTrainingRequestDecision>> GetForRequestAsync(int departmentId, int lmsTrainingRequestId) =>
			(await QueryAsync<LmsTrainingRequestDecision>(
				$"SELECT * FROM {Tbl("LmsTrainingRequestDecisions")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsTrainingRequestId")} = {P}Id " +
				$"ORDER BY {Col("DecidedOn")}, {Col("LmsTrainingRequestDecisionId")}",
				new { DepartmentId = departmentId, Id = lmsTrainingRequestId })).ToList();
	}

	public class LmsReportFoldersRepository : RmsRepositoryBase<LmsReportFolder>, ILmsReportFoldersRepository
	{
		public LmsReportFoldersRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<List<LmsReportFolder>> GetForDepartmentAsync(int departmentId) =>
			(await QueryAsync<LmsReportFolder>(
				$"SELECT * FROM {Tbl("LmsReportFolders")} WHERE {Col("DepartmentId")} = {P}DepartmentId ORDER BY {Col("SortOrder")}, {Col("Name")}, {Col("LmsReportFolderId")}",
				new { DepartmentId = departmentId })).ToList();

		public Task<LmsReportFolder> GetByIdForDepartmentAsync(int departmentId, int lmsReportFolderId) =>
			QueryFirstOrDefaultAsync<LmsReportFolder>(
				$"SELECT * FROM {Tbl("LmsReportFolders")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsReportFolderId")} = {P}Id",
				new { DepartmentId = departmentId, Id = lmsReportFolderId });
	}

	public class LmsReportDefinitionsRepository : RmsRepositoryBase<LmsReportDefinition>, ILmsReportDefinitionsRepository
	{
		public LmsReportDefinitionsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<List<LmsReportDefinition>> GetForDepartmentAsync(int departmentId) =>
			(await QueryAsync<LmsReportDefinition>(
				$"SELECT * FROM {Tbl("LmsReportDefinitions")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("IsDeleted")} = {P}No " +
				$"ORDER BY {Col("Name")}, {Col("LmsReportDefinitionId")}",
				new { DepartmentId = departmentId, No = false })).ToList();

		public Task<LmsReportDefinition> GetByIdForDepartmentAsync(int departmentId, int lmsReportDefinitionId) =>
			QueryFirstOrDefaultAsync<LmsReportDefinition>(
				$"SELECT * FROM {Tbl("LmsReportDefinitions")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsReportDefinitionId")} = {P}Id",
				new { DepartmentId = departmentId, Id = lmsReportDefinitionId });

		public async Task<List<LmsReportDefinition>> GetDueAsync(DateTime utcNow, int take)
		{
			var limit = Math.Clamp(take, 1, 1000);
			var where = $"{Col("IsDeleted")} = {P}No AND {Col("Frequency")} > 0 AND {Col("NextRunUtc")} IS NOT NULL AND {Col("NextRunUtc")} <= {P}Now";
			var sql = IsPostgres
				? $"SELECT * FROM {Tbl("LmsReportDefinitions")} WHERE {where} ORDER BY {Col("NextRunUtc")}, {Col("LmsReportDefinitionId")} LIMIT {limit}"
				: $"SELECT TOP ({limit}) * FROM {Tbl("LmsReportDefinitions")} WHERE {where} ORDER BY {Col("NextRunUtc")}, {Col("LmsReportDefinitionId")}";
			var parameters = new DynamicParameters();
			parameters.Add("No", false);
			parameters.Add("Now", DatabaseTimestamp(utcNow), System.Data.DbType.DateTime2);
			return (await QueryAsync<LmsReportDefinition>(sql, parameters)).ToList();
		}

		public async Task<bool> TryClaimRunAsync(int lmsReportDefinitionId, DateTime expectedNextRunUtc, DateTime? nextRunUtc, DateTime ranOnUtc, CancellationToken cancellationToken = default)
		{
			var parameters = new DynamicParameters();
			parameters.Add("Id", lmsReportDefinitionId);
			parameters.Add("Expected", DatabaseTimestamp(expectedNextRunUtc), System.Data.DbType.DateTime2);
			parameters.Add("Next", nextRunUtc.HasValue ? DatabaseTimestamp(nextRunUtc.Value) : (DateTime?)null, System.Data.DbType.DateTime2);
			parameters.Add("RanOn", DatabaseTimestamp(ranOnUtc), System.Data.DbType.DateTime2);
			parameters.Add("No", false);
			return await ExecuteAsync(
				$"UPDATE {Tbl("LmsReportDefinitions")} SET {Col("NextRunUtc")} = {P}Next, {Col("LastRunUtc")} = {P}RanOn " +
				$"WHERE {Col("LmsReportDefinitionId")} = {P}Id AND {Col("NextRunUtc")} = {P}Expected AND {Col("IsDeleted")} = {P}No",
				parameters, cancellationToken) == 1;
		}

		public Task SetLastRunStatusAsync(int lmsReportDefinitionId, int status, CancellationToken cancellationToken = default) =>
			ExecuteAsync($"UPDATE {Tbl("LmsReportDefinitions")} SET {Col("LastRunStatus")} = {P}Status WHERE {Col("LmsReportDefinitionId")} = {P}Id",
				new { Id = lmsReportDefinitionId, Status = status }, cancellationToken);

		public Task ClearFolderAsync(int departmentId, int lmsReportFolderId, CancellationToken cancellationToken = default) =>
			ExecuteAsync($"UPDATE {Tbl("LmsReportDefinitions")} SET {Col("LmsReportFolderId")} = NULL WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsReportFolderId")} = {P}FolderId",
				new { DepartmentId = departmentId, FolderId = lmsReportFolderId }, cancellationToken);
	}

	public class LmsReportRunsRepository : RmsRepositoryBase<LmsReportRun>, ILmsReportRunsRepository
	{
		public LmsReportRunsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<List<LmsReportRun>> GetForDefinitionAsync(int departmentId, int lmsReportDefinitionId, int take)
		{
			var limit = Math.Clamp(take, 1, 500);
			var where = $"{Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsReportDefinitionId")} = {P}Id";
			var sql = IsPostgres
				? $"SELECT * FROM {Tbl("LmsReportRuns")} WHERE {where} ORDER BY {Col("RanOn")} DESC, {Col("LmsReportRunId")} DESC LIMIT {limit}"
				: $"SELECT TOP ({limit}) * FROM {Tbl("LmsReportRuns")} WHERE {where} ORDER BY {Col("RanOn")} DESC, {Col("LmsReportRunId")} DESC";
			return (await QueryAsync<LmsReportRun>(sql, new { DepartmentId = departmentId, Id = lmsReportDefinitionId })).ToList();
		}
	}

	public class LmsCertificationCycleRulesRepository : RmsRepositoryBase<LmsCertificationCycleRule>, ILmsCertificationCycleRulesRepository
	{
		public LmsCertificationCycleRulesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<List<LmsCertificationCycleRule>> GetForDepartmentAsync(int departmentId) =>
			(await QueryAsync<LmsCertificationCycleRule>(
				$"SELECT * FROM {Tbl("LmsCertificationCycleRules")} WHERE {Col("DepartmentId")} = {P}DepartmentId ORDER BY {Col("LmsCertificationCycleRuleId")}",
				new { DepartmentId = departmentId })).ToList();

		public async Task<List<int>> GetDepartmentIdsAsync() =>
			(await QueryAsync<int>($"SELECT DISTINCT {Col("DepartmentId")} FROM {Tbl("LmsCertificationCycleRules")}", null)).OrderBy(id => id).ToList();
	}

	public class LmsCertificationAlertsRepository : RmsRepositoryBase<LmsCertificationAlert>, ILmsCertificationAlertsRepository
	{
		private static readonly string[] Columns = { "DepartmentId", "PersonnelCertificationId", "Kind", "AlertKey", "SentOn", "Recipients" };

		public LmsCertificationAlertsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<bool> TryClaimAsync(LmsCertificationAlert alert, CancellationToken cancellationToken = default)
		{
			if (alert == null || alert.DepartmentId <= 0 || alert.PersonnelCertificationId <= 0 || string.IsNullOrWhiteSpace(alert.AlertKey))
				return false;

			var parameters = new DynamicParameters();
			parameters.Add("DepartmentId", alert.DepartmentId);
			parameters.Add("PersonnelCertificationId", alert.PersonnelCertificationId);
			parameters.Add("Kind", alert.Kind);
			parameters.Add("AlertKey", alert.AlertKey);
			parameters.Add("SentOn", DatabaseTimestamp(alert.SentOn), System.Data.DbType.DateTime2);
			parameters.Add("Recipients", alert.Recipients);

			var values = string.Join(", ", Columns.Select(c => P + c));
			var sql = IsPostgres
				? $"INSERT INTO {Tbl("LmsCertificationAlerts")} ({Cols(Columns)}) VALUES ({values}) ON CONFLICT ({Cols("PersonnelCertificationId", "Kind", "AlertKey")}) DO NOTHING " +
				  $"RETURNING {Col("LmsCertificationAlertId")}"
				: $"INSERT INTO {Tbl("LmsCertificationAlerts")} ({Cols(Columns)}) OUTPUT INSERTED.{Col("LmsCertificationAlertId")} SELECT {values} WHERE NOT EXISTS " +
				  $"(SELECT 1 FROM {Tbl("LmsCertificationAlerts")} WITH (UPDLOCK, HOLDLOCK) WHERE {Col("PersonnelCertificationId")} = {P}PersonnelCertificationId " +
				  $"AND {Col("Kind")} = {P}Kind AND {Col("AlertKey")} = {P}AlertKey)";
			try
			{
				var ids = (await QueryAsync<int>(sql, parameters, cancellationToken)).ToList();
				if (ids.Count == 0)
					return false;
				alert.LmsCertificationAlertId = ids[0];
				return true;
			}
			catch (Exception ex) when (IsUniqueViolation(ex))
			{
				return false;
			}
		}

		public Task SetRecipientsAsync(int lmsCertificationAlertId, int recipients, CancellationToken cancellationToken = default) =>
			ExecuteAsync($"UPDATE {Tbl("LmsCertificationAlerts")} SET {Col("Recipients")} = {P}Recipients WHERE {Col("LmsCertificationAlertId")} = {P}Id",
				new { Id = lmsCertificationAlertId, Recipients = recipients }, cancellationToken);
	}

	public class LmsProgramStatesRepository : RmsRepositoryBase<LmsProgramState>, ILmsProgramStatesRepository
	{
		public LmsProgramStatesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<LmsProgramState> GetForDepartmentAsync(int departmentId) =>
			QueryFirstOrDefaultAsync<LmsProgramState>($"SELECT * FROM {Tbl("LmsProgramStates")} WHERE {Col("DepartmentId")} = {P}DepartmentId", new { DepartmentId = departmentId });

		/// <summary>Inserts the department's row when it has none (a concurrent insert is absorbed).</summary>
		private async Task EnsureRowAsync(int departmentId, CancellationToken cancellationToken)
		{
			var parameters = new DynamicParameters();
			parameters.Add("DepartmentId", departmentId);
			parameters.Add("Now", DatabaseTimestamp(DateTime.UtcNow), System.Data.DbType.DateTime2);
			var sql = IsPostgres
				? $"INSERT INTO {Tbl("LmsProgramStates")} ({Cols("DepartmentId", "UpdatedOn")}) VALUES ({P}DepartmentId, {P}Now) ON CONFLICT ({Col("DepartmentId")}) DO NOTHING"
				: $"INSERT INTO {Tbl("LmsProgramStates")} ({Cols("DepartmentId", "UpdatedOn")}) SELECT {P}DepartmentId, {P}Now WHERE NOT EXISTS " +
				  $"(SELECT 1 FROM {Tbl("LmsProgramStates")} WITH (UPDLOCK, HOLDLOCK) WHERE {Col("DepartmentId")} = {P}DepartmentId)";
			try
			{
				await ExecuteAsync(sql, parameters, cancellationToken);
			}
			catch (Exception ex) when (IsUniqueViolation(ex))
			{
				// Another worker inserted it first.
			}
		}

		public async Task<bool> TryClaimCycleSweepAsync(int departmentId, DateTime localDate, CancellationToken cancellationToken = default)
		{
			await EnsureRowAsync(departmentId, cancellationToken);
			var parameters = new DynamicParameters();
			parameters.Add("DepartmentId", departmentId);
			parameters.Add("Day", DatabaseTimestamp(localDate.Date), System.Data.DbType.DateTime2);
			parameters.Add("Now", DatabaseTimestamp(DateTime.UtcNow), System.Data.DbType.DateTime2);
			return await ExecuteAsync(
				$"UPDATE {Tbl("LmsProgramStates")} SET {Col("CycleSweepLocalDate")} = {P}Day, {Col("UpdatedOn")} = {P}Now WHERE {Col("DepartmentId")} = {P}DepartmentId " +
				$"AND ({Col("CycleSweepLocalDate")} IS NULL OR {Col("CycleSweepLocalDate")} < {P}Day)", parameters, cancellationToken) == 1;
		}

		public Task ReleaseCycleSweepAsync(int departmentId, DateTime localDate, CancellationToken cancellationToken = default)
		{
			var parameters = new DynamicParameters();
			parameters.Add("DepartmentId", departmentId);
			parameters.Add("Day", DatabaseTimestamp(localDate.Date), System.Data.DbType.DateTime2);
			parameters.Add("Previous", DatabaseTimestamp(localDate.Date.AddDays(-1)), System.Data.DbType.DateTime2);
			return ExecuteAsync(
				$"UPDATE {Tbl("LmsProgramStates")} SET {Col("CycleSweepLocalDate")} = {P}Previous WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("CycleSweepLocalDate")} = {P}Day",
				parameters, cancellationToken);
		}

		public async Task SetFamiliarizationScannedThroughAsync(int departmentId, DateTime scannedThroughUtc, CancellationToken cancellationToken = default)
		{
			await EnsureRowAsync(departmentId, cancellationToken);
			var parameters = new DynamicParameters();
			parameters.Add("DepartmentId", departmentId);
			parameters.Add("Through", DatabaseTimestamp(scannedThroughUtc), System.Data.DbType.DateTime2);
			parameters.Add("Now", DatabaseTimestamp(DateTime.UtcNow), System.Data.DbType.DateTime2);
			await ExecuteAsync(
				$"UPDATE {Tbl("LmsProgramStates")} SET {Col("FamiliarizationScannedThrough")} = {P}Through, {Col("UpdatedOn")} = {P}Now WHERE {Col("DepartmentId")} = {P}DepartmentId",
				parameters, cancellationToken);
		}

		public async Task<List<int>> GetDepartmentsWithProgramSettingsAsync() =>
			(await QueryAsync<int>(
				$"SELECT DISTINCT {Col("DepartmentId")} FROM {Tbl("DepartmentSettings")} WHERE {Col("SettingType")} = {P}Type",
				new { Type = (int)DepartmentSettingTypes.LmsProgramSettings })).OrderBy(id => id).ToList();
	}

	/// <summary>
	/// Records visits for building-familiarization credit: explicit plaintext columns only (ids, member ids, dates, numbers and states);
	/// protected text (notes, names, addresses, answers) is never selected.
	/// </summary>
	public class LmsFamiliarizationSourceRepository : RmsRepositoryBase<LmsProgramState>, ILmsFamiliarizationSourceRepository
	{
		public LmsFamiliarizationSourceRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		private sealed class InspectionRow
		{
			public string RmsInspectionId { get; set; }
			public string InspectionNumber { get; set; }
			public int State { get; set; }
			public DateTime? StartedOn { get; set; }
			public DateTime? CompletedOn { get; set; }
			public string InspectorUserId { get; set; }
			public DateTime ModifiedOn { get; set; }
			public DateTime? DeletedOn { get; set; }
		}

		private sealed class ReviewRow
		{
			public string Id { get; set; }
			public string Number { get; set; }
			public DateTime? LastReviewedOn { get; set; }
			public string ReviewedByUserId { get; set; }
		}

		private string Top(int take) => IsPostgres ? string.Empty : $"TOP ({Math.Clamp(take, 1, 5000)}) ";
		private string Limit(int take) => IsPostgres ? $" LIMIT {Math.Clamp(take, 1, 5000)}" : string.Empty;

		private static DynamicParameters Window(int departmentId, DateTime sinceUtc, DateTime untilUtc)
		{
			var parameters = new DynamicParameters();
			parameters.Add("DepartmentId", departmentId);
			parameters.Add("Since", DatabaseTimestamp(sinceUtc), System.Data.DbType.DateTime2);
			parameters.Add("Until", DatabaseTimestamp(untilUtc), System.Data.DbType.DateTime2);
			return parameters;
		}

		public async Task<List<LmsFamiliarizationVisit>> GetInspectionsChangedAsync(int departmentId, DateTime sinceUtc, DateTime untilUtc, int take)
		{
			var rows = await QueryAsync<InspectionRow>(
				$"SELECT {Top(take)}{Cols("RmsInspectionId", "InspectionNumber", "State", "StartedOn", "CompletedOn", "InspectorUserId", "ModifiedOn", "DeletedOn")} " +
				$"FROM {Tbl("RmsInspections")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("ModifiedOn")} > {P}Since AND {Col("ModifiedOn")} <= {P}Until " +
				$"ORDER BY {Col("ModifiedOn")}, {Col("RmsInspectionId")}{Limit(take)}", Window(departmentId, sinceUtc, untilUtc));

			var done = new[] { (int)RmsInspectionState.Completed, (int)RmsInspectionState.ReinspectionRequired, (int)RmsInspectionState.Closed };
			return rows.Select(r => new LmsFamiliarizationVisit
			{
				Kind = "Inspection",
				SourceRefId = "insp:" + r.RmsInspectionId,
				UserId = r.InspectorUserId,
				CompletedOn = r.CompletedOn ?? r.ModifiedOn,
				StartedOn = r.StartedOn,
				Reference = r.InspectionNumber,
				ModifiedOn = r.ModifiedOn,
				Withdrawn = r.DeletedOn.HasValue || !done.Contains(r.State) || !r.CompletedOn.HasValue || string.IsNullOrWhiteSpace(r.InspectorUserId)
			}).ToList();
		}

		public async Task<List<LmsFamiliarizationVisit>> GetOccupancyReviewsAsync(int departmentId, DateTime sinceUtc, DateTime untilUtc, int take)
		{
			var rows = await QueryAsync<ReviewRow>(
				$"SELECT {Top(take)}{Col("RmsOccupancyId")} AS {Col("Id")}, {Col("OccupancyNumber")} AS {Col("Number")}, {Cols("LastReviewedOn", "ReviewedByUserId")} " +
				$"FROM {Tbl("RmsOccupancies")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LastReviewedOn")} > {P}Since AND {Col("LastReviewedOn")} <= {P}Until " +
				$"AND {Col("ReviewedByUserId")} IS NOT NULL AND {Col("DeletedOn")} IS NULL ORDER BY {Col("LastReviewedOn")}, {Col("RmsOccupancyId")}{Limit(take)}",
				Window(departmentId, sinceUtc, untilUtc));
			return rows.Where(r => r.LastReviewedOn.HasValue).Select(r => new LmsFamiliarizationVisit
			{
				Kind = "OccupancyReview",
				SourceRefId = "occ-review:" + r.Id + ":" + r.LastReviewedOn.Value.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture),
				UserId = r.ReviewedByUserId,
				CompletedOn = r.LastReviewedOn.Value,
				Reference = r.Number,
				ModifiedOn = r.LastReviewedOn.Value
			}).ToList();
		}

		public async Task<List<LmsFamiliarizationVisit>> GetPreplanReviewsAsync(int departmentId, DateTime sinceUtc, DateTime untilUtc, int take)
		{
			var rows = await QueryAsync<ReviewRow>(
				$"SELECT {Top(take)}{Col("ContactPreplanId")} AS {Col("Id")}, {Cols("LastReviewedOn", "ReviewedByUserId")} " +
				$"FROM {Tbl("ContactPreplans")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LastReviewedOn")} > {P}Since AND {Col("LastReviewedOn")} <= {P}Until " +
				$"AND {Col("ReviewedByUserId")} IS NOT NULL AND {Col("IsDeleted")} = {P}No ORDER BY {Col("LastReviewedOn")}, {Col("ContactPreplanId")}{Limit(take)}",
				new DynamicParameters(new { DepartmentId = departmentId, Since = DatabaseTimestamp(sinceUtc), Until = DatabaseTimestamp(untilUtc), No = false }));
			return rows.Where(r => r.LastReviewedOn.HasValue).Select(r => new LmsFamiliarizationVisit
			{
				Kind = "PreplanReview",
				SourceRefId = "preplan-review:" + r.Id + ":" + r.LastReviewedOn.Value.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture),
				UserId = r.ReviewedByUserId,
				CompletedOn = r.LastReviewedOn.Value,
				ModifiedOn = r.LastReviewedOn.Value
			}).ToList();
		}
	}
}
