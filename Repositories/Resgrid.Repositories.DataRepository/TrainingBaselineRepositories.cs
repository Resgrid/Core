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
	/// <summary>Quiz attempts (R-T7). Every read starts at the department.</summary>
	public class TrainingQuizAttemptsRepository : RmsRepositoryBase<TrainingQuizAttempt>, ITrainingQuizAttemptsRepository
	{
		public TrainingQuizAttemptsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<List<TrainingQuizAttempt>> GetForTrainingAsync(int departmentId, int trainingId)
		{
			return (await QueryAsync<TrainingQuizAttempt>(
				$"SELECT * FROM {Tbl("TrainingQuizAttempts")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("TrainingId")} = {P}TrainingId ORDER BY {Col("SubmittedOn")} DESC, {Col("TrainingQuizAttemptId")} DESC",
				new { DepartmentId = departmentId, TrainingId = trainingId })).ToList();
		}

		public async Task<List<TrainingQuizAttempt>> GetForMemberAsync(int departmentId, int trainingId, string userId)
		{
			return (await QueryAsync<TrainingQuizAttempt>(
				$"SELECT * FROM {Tbl("TrainingQuizAttempts")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("TrainingId")} = {P}TrainingId AND {Col("UserId")} = {CaseInsensitive("UserId")} " +
				$"ORDER BY {Col("AttemptNumber")} DESC",
				new { DepartmentId = departmentId, TrainingId = trainingId, UserId = userId })).ToList();
		}

		public async Task<List<TrainingQuizAttempt>> GetAllForMemberAsync(int departmentId, string userId)
		{
			return (await QueryAsync<TrainingQuizAttempt>(
				$"SELECT * FROM {Tbl("TrainingQuizAttempts")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("UserId")} = {CaseInsensitive("UserId")} ORDER BY {Col("SubmittedOn")} DESC",
				new { DepartmentId = departmentId, UserId = userId })).ToList();
		}

		public Task<int> GetLastAttemptNumberAsync(int departmentId, int trainingId, string userId)
		{
			return ScalarAsync<int>(
				$"SELECT COALESCE(MAX({Col("AttemptNumber")}), 0) FROM {Tbl("TrainingQuizAttempts")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("TrainingId")} = {P}TrainingId AND {Col("UserId")} = {CaseInsensitive("UserId")}",
				new { DepartmentId = departmentId, TrainingId = trainingId, UserId = userId });
		}
	}

	/// <summary>Dynamic assignment rules of trainings (T-D20).</summary>
	public class TrainingAudiencesRepository : RmsRepositoryBase<TrainingAudience>, ITrainingAudiencesRepository
	{
		public TrainingAudiencesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<List<TrainingAudience>> GetForTrainingAsync(int departmentId, int trainingId)
		{
			return (await QueryAsync<TrainingAudience>(
				$"SELECT * FROM {Tbl("TrainingAudiences")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("TrainingId")} = {P}TrainingId ORDER BY {Col("TrainingAudienceId")}",
				new { DepartmentId = departmentId, TrainingId = trainingId })).ToList();
		}

		public async Task<List<TrainingAudience>> GetForOpenTrainingsAsync(int departmentId)
		{
			var closed = IsPostgres ? "false" : "0";
			return (await QueryAsync<TrainingAudience>(
				$"SELECT a.* FROM {Tbl("TrainingAudiences")} a JOIN {Tbl("Trainings")} t ON t.{Col("TrainingId")} = a.{Col("TrainingId")} " +
				$"WHERE a.{Col("DepartmentId")} = {P}DepartmentId AND t.{Col("DepartmentId")} = {P}DepartmentId AND t.{Col("ArchivedOn")} IS NULL AND t.{Col("ClosedToNewAssignees")} = {closed} " +
				$"ORDER BY a.{Col("TrainingId")}, a.{Col("TrainingAudienceId")}",
				new { DepartmentId = departmentId })).ToList();
		}

		public async Task ReplaceForTrainingAsync(int departmentId, int trainingId, IReadOnlyCollection<TrainingAudience> rows, CancellationToken cancellationToken = default)
		{
			var owns = UnitOfWork.Transaction == null;
			UnitOfWork.CreateOrGetConnection();
			try
			{
				await ExecuteAsync($"DELETE FROM {Tbl("TrainingAudiences")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("TrainingId")} = {P}TrainingId",
					new { DepartmentId = departmentId, TrainingId = trainingId }, cancellationToken);

				foreach (var row in rows ?? new List<TrainingAudience>())
				{
					var parameters = new DynamicParameters();
					parameters.Add("DepartmentId", departmentId);
					parameters.Add("TrainingId", trainingId);
					parameters.Add("Kind", row.Kind);
					parameters.Add("RefId", row.RefId, System.Data.DbType.String);
					parameters.Add("CreatedOn", DatabaseTimestamp(row.CreatedOn == default ? DateTime.UtcNow : row.CreatedOn), System.Data.DbType.DateTime2);
					parameters.Add("CreatedByUserId", row.CreatedByUserId, System.Data.DbType.String);
					await ExecuteAsync($"INSERT INTO {Tbl("TrainingAudiences")} ({Cols("DepartmentId", "TrainingId", "Kind", "RefId", "CreatedOn", "CreatedByUserId")}) " +
						$"VALUES ({P}DepartmentId, {P}TrainingId, {P}Kind, {P}RefId, {P}CreatedOn, {P}CreatedByUserId)", parameters, cancellationToken);
				}

				if (owns) UnitOfWork.CommitChanges();
			}
			catch
			{
				if (owns) UnitOfWork.DiscardChanges();
				throw;
			}
		}
	}

	/// <summary>Completions moved off live assignment rows.</summary>
	public class TrainingCompletionHistoriesRepository : RmsRepositoryBase<TrainingCompletionHistory>, ITrainingCompletionHistoriesRepository
	{
		public TrainingCompletionHistoriesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<List<TrainingCompletionHistory>> GetForTrainingAsync(int departmentId, int trainingId)
		{
			return (await QueryAsync<TrainingCompletionHistory>(
				$"SELECT * FROM {Tbl("TrainingCompletionHistories")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("TrainingId")} = {P}TrainingId ORDER BY {Col("RecordedOn")} DESC",
				new { DepartmentId = departmentId, TrainingId = trainingId })).ToList();
		}

		public async Task<List<TrainingCompletionHistory>> GetForMemberAsync(int departmentId, string userId)
		{
			return (await QueryAsync<TrainingCompletionHistory>(
				$"SELECT * FROM {Tbl("TrainingCompletionHistories")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("UserId")} = {CaseInsensitive("UserId")} ORDER BY {Col("RecordedOn")} DESC",
				new { DepartmentId = departmentId, UserId = userId })).ToList();
		}
	}

	/// <summary>"Flag this question" feedback.</summary>
	public class TrainingQuestionFlagsRepository : RmsRepositoryBase<TrainingQuestionFlag>, ITrainingQuestionFlagsRepository
	{
		public TrainingQuestionFlagsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<List<TrainingQuestionFlag>> GetForTrainingAsync(int departmentId, int trainingId, bool includeResolved)
		{
			var open = includeResolved ? string.Empty : $" AND {Col("ResolvedOn")} IS NULL";
			return (await QueryAsync<TrainingQuestionFlag>(
				$"SELECT * FROM {Tbl("TrainingQuestionFlags")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("TrainingId")} = {P}TrainingId{open} ORDER BY {Col("CreatedOn")} DESC",
				new { DepartmentId = departmentId, TrainingId = trainingId })).ToList();
		}

		public async Task<bool> ResolveAsync(int departmentId, int trainingQuestionFlagId, string userId, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var parameters = new DynamicParameters();
			parameters.Add("DepartmentId", departmentId);
			parameters.Add("Id", trainingQuestionFlagId);
			parameters.Add("UserId", userId, System.Data.DbType.String);
			parameters.Add("Now", DatabaseTimestamp(utcNow), System.Data.DbType.DateTime2);
			return await ExecuteAsync($"UPDATE {Tbl("TrainingQuestionFlags")} SET {Col("ResolvedOn")} = {P}Now, {Col("ResolvedByUserId")} = {P}UserId " +
				$"WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("TrainingQuestionFlagId")} = {P}Id AND {Col("ResolvedOn")} IS NULL", parameters, cancellationToken) == 1;
		}

		public Task<int> DeleteForQuestionsAsync(int departmentId, int trainingId, IEnumerable<int> questionIds, CancellationToken cancellationToken = default)
		{
			var ids = InListValue(questionIds);
			if (ids.Length == 0)
				return Task.FromResult(0);

			return ExecuteAsync($"DELETE FROM {Tbl("TrainingQuestionFlags")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("TrainingId")} = {P}TrainingId AND {InList("TrainingQuestionId", "Ids")}",
				new { DepartmentId = departmentId, TrainingId = trainingId, Ids = ids }, cancellationToken);
		}
	}

	/// <summary>The membership-change queue worker 83 drains.</summary>
	public class ContentAudienceChangesRepository : RmsRepositoryBase<ContentAudienceChange>, IContentAudienceChangesRepository
	{
		public ContentAudienceChangesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<List<ContentAudienceChange>> GetPendingAsync(int take)
		{
			var sql = IsPostgres
				? $"SELECT * FROM {Tbl("ContentAudienceChanges")} WHERE {Col("ProcessedOn")} IS NULL ORDER BY {Col("ContentAudienceChangeId")} LIMIT {P}Take"
				: $"SELECT TOP ({P}Take) * FROM {Tbl("ContentAudienceChanges")} WHERE {Col("ProcessedOn")} IS NULL ORDER BY {Col("ContentAudienceChangeId")}";
			return (await QueryAsync<ContentAudienceChange>(sql, new { Take = Math.Max(1, take) })).ToList();
		}

		public Task<int> MarkProcessedAsync(IEnumerable<int> ids, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var list = InListValue(ids);
			if (list.Length == 0)
				return Task.FromResult(0);

			var parameters = new DynamicParameters();
			parameters.Add("Ids", list);
			parameters.Add("Now", DatabaseTimestamp(utcNow), System.Data.DbType.DateTime2);
			return ExecuteAsync($"UPDATE {Tbl("ContentAudienceChanges")} SET {Col("ProcessedOn")} = {P}Now WHERE {InList("ContentAudienceChangeId", "Ids")}", parameters, cancellationToken);
		}

		public Task<int> PurgeProcessedAsync(DateTime processedBeforeUtc, CancellationToken cancellationToken = default)
		{
			var parameters = new DynamicParameters();
			parameters.Add("Before", DatabaseTimestamp(processedBeforeUtc), System.Data.DbType.DateTime2);
			return ExecuteAsync($"DELETE FROM {Tbl("ContentAudienceChanges")} WHERE {Col("ProcessedOn")} IS NOT NULL AND {Col("ProcessedOn")} < {P}Before", parameters, cancellationToken);
		}
	}

	/// <summary>Member leave of absence periods (R-C2).</summary>
	public class MemberLeavePeriodsRepository : RmsRepositoryBase<MemberLeavePeriod>, IMemberLeavePeriodsRepository
	{
		public MemberLeavePeriodsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<List<MemberLeavePeriod>> GetForDepartmentAsync(int departmentId)
		{
			return (await QueryAsync<MemberLeavePeriod>(
				$"SELECT * FROM {Tbl("MemberLeavePeriods")} WHERE {Col("DepartmentId")} = {P}DepartmentId ORDER BY {Col("StartsOn")} DESC",
				new { DepartmentId = departmentId })).ToList();
		}

		public async Task<List<MemberLeavePeriod>> GetForMemberAsync(int departmentId, string userId)
		{
			return (await QueryAsync<MemberLeavePeriod>(
				$"SELECT * FROM {Tbl("MemberLeavePeriods")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("UserId")} = {CaseInsensitive("UserId")} ORDER BY {Col("StartsOn")}",
				new { DepartmentId = departmentId, UserId = userId })).ToList();
		}

		public async Task<bool> DeleteForDepartmentAsync(int departmentId, int memberLeavePeriodId, CancellationToken cancellationToken = default)
		{
			return await ExecuteAsync($"DELETE FROM {Tbl("MemberLeavePeriods")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("MemberLeavePeriodId")} = {P}Id",
				new { DepartmentId = departmentId, Id = memberLeavePeriodId }, cancellationToken) == 1;
		}
	}

	/// <summary>Items members pinned to My Training.</summary>
	public class MemberPinnedItemsRepository : RmsRepositoryBase<MemberPinnedItem>, IMemberPinnedItemsRepository
	{
		public MemberPinnedItemsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<List<MemberPinnedItem>> GetForMemberAsync(int departmentId, string userId)
		{
			return (await QueryAsync<MemberPinnedItem>(
				$"SELECT * FROM {Tbl("MemberPinnedItems")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("UserId")} = {CaseInsensitive("UserId")}",
				new { DepartmentId = departmentId, UserId = userId })).ToList();
		}

		public Task<int> DeleteForMemberAsync(int departmentId, string userId, int kind, string refId, CancellationToken cancellationToken = default)
		{
			return ExecuteAsync($"DELETE FROM {Tbl("MemberPinnedItems")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("UserId")} = {CaseInsensitive("UserId")} " +
				$"AND {Col("Kind")} = {P}Kind AND {Col("RefId")} = {CaseInsensitive("RefId")}",
				new { DepartmentId = departmentId, UserId = userId, Kind = kind, RefId = refId }, cancellationToken);
		}
	}
}
