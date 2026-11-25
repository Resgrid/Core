using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Lms;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository
{
	/// <summary>Per-protocol LMS learning settings (LmsProtocolLearnings, M0417): portable SQL, unique per department and protocol.</summary>
	public class LmsProtocolLearningsRepository : LmsRepositoryBase<LmsProtocolLearning>, ILmsProtocolLearningsRepository
	{
		public LmsProtocolLearningsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		private static string True => IsPostgres ? "true" : "1";

		public Task<LmsProtocolLearning> GetForProtocolAsync(int departmentId, int dispatchProtocolId)
		{
			return QueryFirstOrDefaultAsync<LmsProtocolLearning>(
				$"SELECT * FROM {Tbl("LmsProtocolLearnings")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("DispatchProtocolId")} = {P}ProtocolId",
				new { DepartmentId = departmentId, ProtocolId = dispatchProtocolId });
		}

		public async Task<List<LmsProtocolLearning>> GetForDepartmentAsync(int departmentId)
		{
			if (departmentId <= 0)
				return new List<LmsProtocolLearning>();
			return (await QueryAsync<LmsProtocolLearning>(
				$"SELECT * FROM {Tbl("LmsProtocolLearnings")} WHERE {Col("DepartmentId")} = {P}DepartmentId ORDER BY {Col("DispatchProtocolId")}",
				new { DepartmentId = departmentId })).ToList();
		}

		public async Task<List<int>> GetMicroLearningDueDepartmentIdsAsync(DateTime utcNow, int take, CancellationToken cancellationToken = default)
		{
			// Due work only (R-L9): departments with a micro-learning protocol whose weekly clock ran out or was never started.
			return (await QueryAsync<int>(
				$"SELECT DISTINCT l.{Col("DepartmentId")} FROM {Tbl("LmsProtocolLearnings")} l " +
				$"LEFT JOIN {Tbl("LmsMicroLearningRuns")} r ON r.{Col("DepartmentId")} = l.{Col("DepartmentId")} " +
				$"WHERE l.{Col("MicroLearning")} = {True} AND l.{Col("DepartmentId")} > 0 AND (r.{Col("LmsMicroLearningRunId")} IS NULL OR r.{Col("NextRunOn")} <= {P}Now) " +
				$"ORDER BY l.{Col("DepartmentId")} {Take()}",
				new { Now = DatabaseTimestamp(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc)), Take = Math.Clamp(take, 1, 500) }, cancellationToken)).ToList();
		}
	}

	/// <summary>Protocol acknowledgment quiz attempts (LmsProtocolQuizAttempts, M0417), written against their RowVersion.</summary>
	public class LmsProtocolQuizAttemptsRepository : LmsRepositoryBase<LmsProtocolQuizAttempt>, ILmsProtocolQuizAttemptsRepository
	{
		public LmsProtocolQuizAttemptsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		private static LmsProtocolQuizAttempt AsUtc(LmsProtocolQuizAttempt attempt)
		{
			if (attempt != null)
			{
				attempt.StartedOn = DateTime.SpecifyKind(attempt.StartedOn, DateTimeKind.Utc);
				if (attempt.SubmittedOn.HasValue)
					attempt.SubmittedOn = DateTime.SpecifyKind(attempt.SubmittedOn.Value, DateTimeKind.Utc);
			}
			return attempt;
		}

		public async Task<LmsProtocolQuizAttempt> GetByIdForDepartmentAsync(int departmentId, int lmsProtocolQuizAttemptId)
		{
			return AsUtc(await QueryFirstOrDefaultAsync<LmsProtocolQuizAttempt>(
				$"SELECT * FROM {Tbl("LmsProtocolQuizAttempts")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsProtocolQuizAttemptId")} = {P}Id",
				new { DepartmentId = departmentId, Id = lmsProtocolQuizAttemptId }));
		}

		public async Task<List<LmsProtocolQuizAttempt>> GetForUserVersionAsync(int departmentId, string userId, int protocolVersionId)
		{
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(userId))
				return new List<LmsProtocolQuizAttempt>();
			return (await QueryAsync<LmsProtocolQuizAttempt>(
				$"SELECT * FROM {Tbl("LmsProtocolQuizAttempts")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("UserId")} = {CaseInsensitive("UserId")} " +
				$"AND {Col("ProtocolVersionId")} = {P}VersionId ORDER BY {Col("LmsProtocolQuizAttemptId")} DESC",
				new { DepartmentId = departmentId, UserId = userId, VersionId = protocolVersionId })).Select(AsUtc).ToList();
		}

		public async Task<List<LmsProtocolQuizAttempt>> GetForUserAsync(int departmentId, string userId)
		{
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(userId))
				return new List<LmsProtocolQuizAttempt>();
			return (await QueryAsync<LmsProtocolQuizAttempt>(
				$"SELECT * FROM {Tbl("LmsProtocolQuizAttempts")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("UserId")} = {CaseInsensitive("UserId")} " +
				$"ORDER BY {Col("LmsProtocolQuizAttemptId")} DESC",
				new { DepartmentId = departmentId, UserId = userId })).Select(AsUtc).ToList();
		}

		public Task<bool> TryUpdateAsync(LmsProtocolQuizAttempt attempt, long expectedRowVersion, CancellationToken cancellationToken = default) =>
			TryUpdateVersionedAsync(attempt, "LmsProtocolQuizAttempts", "LmsProtocolQuizAttemptId", attempt?.LmsProtocolQuizAttemptId ?? 0, attempt?.DepartmentId ?? 0,
				expectedRowVersion, v => attempt.RowVersion = v, cancellationToken);
	}

	/// <summary>Weekly micro-learning questions and the per-department clock (LmsMicroLearningItems, LmsMicroLearningRuns; M0417).</summary>
	public class LmsMicroLearningRepository : LmsRepositoryBase<LmsMicroLearningItem>, ILmsMicroLearningRepository
	{
		public LmsMicroLearningRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		private static LmsMicroLearningItem AsUtc(LmsMicroLearningItem item)
		{
			if (item != null)
			{
				item.SentOn = DateTime.SpecifyKind(item.SentOn, DateTimeKind.Utc);
				if (item.AnsweredOn.HasValue)
					item.AnsweredOn = DateTime.SpecifyKind(item.AnsweredOn.Value, DateTimeKind.Utc);
			}
			return item;
		}

		private static DateTime Utc(DateTime value) => DatabaseTimestamp(DateTime.SpecifyKind(value, DateTimeKind.Utc));

		public async Task<bool> TryClaimRunAsync(int departmentId, DateTime utcNow, DateTime nextRunUtc, string weekKey, CancellationToken cancellationToken = default)
		{
			if (departmentId <= 0)
				return false;
			var parameters = new { DepartmentId = departmentId, Now = Utc(utcNow), Next = Utc(nextRunUtc), Week = weekKey };

			// A compare-and-set on the clock: only the worker that finds it still due moves it.
			var claimed = await ExecuteAsync(
				$"UPDATE {Tbl("LmsMicroLearningRuns")} SET {Col("NextRunOn")} = {P}Next, {Col("LastRunOn")} = {P}Now, {Col("LastWeekKey")} = {P}Week " +
				$"WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("NextRunOn")} <= {P}Now", parameters, cancellationToken);
			if (claimed == 1)
				return true;

			var exists = await ScalarAsync<int>($"SELECT COUNT(1) FROM {Tbl("LmsMicroLearningRuns")} WHERE {Col("DepartmentId")} = {P}DepartmentId",
				new { DepartmentId = departmentId }, cancellationToken);
			if (exists > 0)
				return false;

			try
			{
				await ExecuteAsync(
					$"INSERT INTO {Tbl("LmsMicroLearningRuns")} ({Cols("DepartmentId", "NextRunOn", "LastRunOn", "LastWeekKey")}) VALUES ({P}DepartmentId, {P}Next, {P}Now, {P}Week)",
					parameters, cancellationToken);
				return true;
			}
			catch (Exception ex) when (IsUniqueViolation(ex))
			{
				// Another worker started the department's clock first.
				return false;
			}
		}

		public async Task<LmsMicroLearningRun> GetRunAsync(int departmentId)
		{
			var run = await QueryFirstOrDefaultAsync<LmsMicroLearningRun>(
				$"SELECT * FROM {Tbl("LmsMicroLearningRuns")} WHERE {Col("DepartmentId")} = {P}DepartmentId", new { DepartmentId = departmentId });
			if (run != null)
			{
				run.NextRunOn = DateTime.SpecifyKind(run.NextRunOn, DateTimeKind.Utc);
				if (run.LastRunOn.HasValue)
					run.LastRunOn = DateTime.SpecifyKind(run.LastRunOn.Value, DateTimeKind.Utc);
			}
			return run;
		}

		public async Task<bool> TryInsertItemAsync(LmsMicroLearningItem item, CancellationToken cancellationToken = default)
		{
			if (item == null || item.DepartmentId <= 0 || string.IsNullOrWhiteSpace(item.UserId) || string.IsNullOrWhiteSpace(item.WeekKey) ||
				item.WeekKey.Length > LmsMicroLearningItem.WeekKeyMaxLength)
				throw new ArgumentException("A micro-learning question needs a department, a member and a week.", nameof(item));

			var columns = Cols("DepartmentId", "UserId", "DispatchProtocolId", "LmsQuestionId", "WeekKey", "SnapshotJson", "SentOn");
			var values = $"{P}DepartmentId, {P}UserId, {P}DispatchProtocolId, {P}LmsQuestionId, {P}WeekKey, {P}SnapshotJson, {P}SentOn";
			var sql = IsPostgres
				? $"INSERT INTO {Tbl("LmsMicroLearningItems")} ({columns}) VALUES ({values}) RETURNING {Col("LmsMicroLearningItemId")}"
				: $"INSERT INTO {Tbl("LmsMicroLearningItems")} ({columns}) OUTPUT INSERTED.{Col("LmsMicroLearningItemId")} VALUES ({values})";
			try
			{
				item.LmsMicroLearningItemId = await ScalarAsync<int>(sql, new
				{
					item.DepartmentId,
					item.UserId,
					item.DispatchProtocolId,
					item.LmsQuestionId,
					item.WeekKey,
					item.SnapshotJson,
					SentOn = Utc(item.SentOn == default ? DateTime.UtcNow : item.SentOn)
				}, cancellationToken);
				return true;
			}
			catch (Exception ex) when (IsUniqueViolation(ex))
			{
				// The member already has this week's question (a re-run, or another worker).
				return false;
			}
		}

		public async Task<LmsMicroLearningItem> GetItemAsync(int departmentId, int lmsMicroLearningItemId)
		{
			return AsUtc(await QueryFirstOrDefaultAsync<LmsMicroLearningItem>(
				$"SELECT * FROM {Tbl("LmsMicroLearningItems")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsMicroLearningItemId")} = {P}Id",
				new { DepartmentId = departmentId, Id = lmsMicroLearningItemId }));
		}

		public async Task<List<LmsMicroLearningItem>> GetForUserAsync(int departmentId, string userId, int take)
		{
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(userId))
				return new List<LmsMicroLearningItem>();
			var sql = $"SELECT * FROM {Tbl("LmsMicroLearningItems")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("UserId")} = {CaseInsensitive("UserId")} " +
				$"ORDER BY {Col("LmsMicroLearningItemId")} DESC" + (take > 0 ? " " + Take() : string.Empty);
			return (await QueryAsync<LmsMicroLearningItem>(sql, new { DepartmentId = departmentId, UserId = userId, Take = Math.Clamp(take, 1, 500) }))
				.Select(AsUtc).ToList();
		}

		public async Task<List<LmsMicroLearningItem>> GetRecentForDepartmentAsync(int departmentId, DateTime sinceUtc)
		{
			if (departmentId <= 0)
				return new List<LmsMicroLearningItem>();
			// Explicit columns: the snapshots are not needed to avoid repeats.
			return (await QueryAsync<LmsMicroLearningItem>(
				$"SELECT {Cols("LmsMicroLearningItemId", "DepartmentId", "UserId", "DispatchProtocolId", "LmsQuestionId", "WeekKey", "SentOn")} FROM {Tbl("LmsMicroLearningItems")} " +
				$"WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("SentOn")} >= {P}Since",
				new { DepartmentId = departmentId, Since = Utc(sinceUtc) })).Select(AsUtc).ToList();
		}

		public async Task<bool> TryAnswerAsync(LmsMicroLearningItem item, CancellationToken cancellationToken = default)
		{
			if (item == null || item.LmsMicroLearningItemId <= 0 || !item.AnsweredOn.HasValue)
				return false;
			return await ExecuteAsync(
				$"UPDATE {Tbl("LmsMicroLearningItems")} SET {Col("AnsweredOn")} = {P}AnsweredOn, {Col("AnswersJson")} = {P}AnswersJson, {Col("Correct")} = {P}Correct " +
				$"WHERE {Col("LmsMicroLearningItemId")} = {P}Id AND {Col("DepartmentId")} = {P}DepartmentId AND {Col("AnsweredOn")} IS NULL",
				new { AnsweredOn = Utc(item.AnsweredOn.Value), item.AnswersJson, item.Correct, Id = item.LmsMicroLearningItemId, item.DepartmentId }, cancellationToken) == 1;
		}
	}
}
