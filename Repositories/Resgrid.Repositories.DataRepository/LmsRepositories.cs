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
	/// <summary>
	/// Shared plumbing of the LMS repositories (T2, M0359/M0360): the optimistic-concurrency update (claim the RowVersion bump,
	/// then write the row in the same transaction), the PostgreSQL savepoint an insert that may hit a unique index needs, and the
	/// "department or system" filter (system-published content has DepartmentId 0).
	/// </summary>
	public abstract class LmsRepositoryBase<T> : RmsRepositoryBase<T> where T : class, IEntity
	{
		protected LmsRepositoryBase(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		/// <summary>"(DepartmentId = @DepartmentId OR DepartmentId = 0)": the department's rows and the system publisher's.</summary>
		protected string DepartmentOrSystem(string alias = null)
		{
			var col = alias == null ? Col("DepartmentId") : alias + "." + Col("DepartmentId");
			return $"({col} = {P}DepartmentId OR {col} = 0)";
		}

		protected static DateTime? Stamp(DateTime? value) => value.HasValue ? DatabaseTimestamp(value.Value) : (DateTime?)null;

		protected static string Take() => IsPostgres ? "LIMIT @Take" : "OFFSET 0 ROWS FETCH NEXT @Take ROWS ONLY";

		/// <summary>
		/// Writes <paramref name="entity"/> only when the stored RowVersion is still <paramref name="expected"/>: the bump is claimed
		/// first (a compare-and-set), then every column is written in the same transaction. False when another save got there first.
		/// </summary>
		protected async Task<bool> TryUpdateVersionedAsync(T entity, string table, string idColumn, int id, int departmentId, long expected, Action<long> setRowVersion,
			CancellationToken cancellationToken)
		{
			if (entity == null || id <= 0)
				return false;

			var owns = UnitOfWork.Transaction == null;
			UnitOfWork.CreateOrGetConnection();
			try
			{
				var claimed = await ExecuteAsync(
					$"UPDATE {Tbl(table)} SET {Col("RowVersion")} = {P}Next WHERE {Col(idColumn)} = {P}Id AND {Col("DepartmentId")} = {P}DepartmentId AND {Col("RowVersion")} = {P}Expected",
					new { Next = expected + 1, Id = id, DepartmentId = departmentId, Expected = expected }, cancellationToken);
				if (claimed != 1)
				{
					if (owns) UnitOfWork.DiscardChanges();
					return false;
				}

				setRowVersion(expected + 1);
				await base.UpdateAsync(entity, cancellationToken, true);
				if (owns) UnitOfWork.CommitChanges();
				return true;
			}
			catch
			{
				if (owns) UnitOfWork.DiscardChanges();
				throw;
			}
		}

		/// <summary>
		/// Inserts a row that a unique index may refuse (a concurrent twin). On PostgreSQL inside a transaction the insert runs under
		/// a savepoint, because a failed statement would abort the whole transaction. True when inserted, false on the unique index.
		/// </summary>
		protected async Task<bool> TryInsertUniqueAsync(T entity, CancellationToken cancellationToken)
		{
			var transaction = UnitOfWork?.Transaction;
			var savepoint = IsPostgres && transaction != null;
			try
			{
				if (savepoint) await transaction.SaveAsync("lms_unique", cancellationToken);
				await base.InsertAsync(entity, cancellationToken, true);
				if (savepoint) await transaction.ReleaseAsync("lms_unique", cancellationToken);
				return true;
			}
			catch (Exception ex) when (IsUniqueViolation(ex) || IsUniqueViolation(ex.InnerException))
			{
				if (savepoint) await transaction.RollbackAsync("lms_unique", cancellationToken);
				return false;
			}
		}
	}

	public class LmsCoursesRepository : LmsRepositoryBase<LmsCourse>, ILmsCoursesRepository
	{
		public LmsCoursesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<List<LmsCourse>> GetForDepartmentAsync(int departmentId, bool includeDeleted = false)
		{
			if (departmentId <= 0)
				return new List<LmsCourse>();
			return (await QueryAsync<LmsCourse>(
				$"SELECT * FROM {Tbl("LmsCourses")} WHERE {Col("DepartmentId")} = {P}DepartmentId" + (includeDeleted ? "" : $" AND {Col("IsDeleted")} = {(IsPostgres ? "false" : "0")}") +
				$" ORDER BY {Col("Title")}", new { DepartmentId = departmentId })).ToList();
		}

		public async Task<List<LmsCourse>> GetSystemCoursesAsync()
		{
			return (await QueryAsync<LmsCourse>(
				$"SELECT * FROM {Tbl("LmsCourses")} WHERE {Col("DepartmentId")} = 0 AND {Col("PublisherKind")} = {(int)LmsPublisherKinds.System} ORDER BY {Col("LmsCourseId")}", null)).ToList();
		}

		public Task<LmsCourse> GetVisibleByIdAsync(int departmentId, int lmsCourseId)
		{
			return QueryFirstOrDefaultAsync<LmsCourse>(
				$"SELECT * FROM {Tbl("LmsCourses")} WHERE {DepartmentOrSystem()} AND {Col("LmsCourseId")} = {P}Id", new { DepartmentId = departmentId, Id = lmsCourseId });
		}

		public async Task<List<LmsCourse>> GetVisibleByIdsAsync(int departmentId, IEnumerable<int> lmsCourseIds)
		{
			var ids = InListValue((lmsCourseIds ?? Enumerable.Empty<int>()).Where(x => x > 0).Distinct());
			if (ids.Length == 0)
				return new List<LmsCourse>();
			return (await QueryAsync<LmsCourse>(
				$"SELECT * FROM {Tbl("LmsCourses")} WHERE {DepartmentOrSystem()} AND {InList("LmsCourseId", "Ids")}", new { DepartmentId = departmentId, Ids = ids })).ToList();
		}

		public Task<LmsCourse> GetBySystemKeyAsync(string systemKey)
		{
			return QueryFirstOrDefaultAsync<LmsCourse>(
				$"SELECT * FROM {Tbl("LmsCourses")} WHERE {Col("DepartmentId")} = 0 AND {Col("SystemKey")} = {CaseInsensitive("SystemKey")}", new { SystemKey = systemKey });
		}

		public Task<bool> TryUpdateAsync(LmsCourse course, long expectedRowVersion, CancellationToken cancellationToken = default) =>
			TryUpdateVersionedAsync(course, "LmsCourses", "LmsCourseId", course?.LmsCourseId ?? 0, course?.DepartmentId ?? 0, expectedRowVersion, v => course.RowVersion = v, cancellationToken);
	}

	public class LmsCourseVersionsRepository : LmsRepositoryBase<LmsCourseVersion>, ILmsCourseVersionsRepository
	{
		public LmsCourseVersionsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<List<LmsCourseVersion>> GetForCourseAsync(int departmentId, int lmsCourseId)
		{
			return (await QueryAsync<LmsCourseVersion>(
				$"SELECT * FROM {Tbl("LmsCourseVersions")} WHERE {DepartmentOrSystem()} AND {Col("LmsCourseId")} = {P}CourseId ORDER BY {Col("LmsCourseVersionId")} DESC",
				new { DepartmentId = departmentId, CourseId = lmsCourseId })).ToList();
		}

		public Task<LmsCourseVersion> GetVisibleByIdAsync(int departmentId, int lmsCourseVersionId)
		{
			return QueryFirstOrDefaultAsync<LmsCourseVersion>(
				$"SELECT * FROM {Tbl("LmsCourseVersions")} WHERE {DepartmentOrSystem()} AND {Col("LmsCourseVersionId")} = {P}Id", new { DepartmentId = departmentId, Id = lmsCourseVersionId });
		}

		public async Task<List<LmsCourseVersion>> GetVisibleByIdsAsync(int departmentId, IEnumerable<int> lmsCourseVersionIds)
		{
			var ids = InListValue((lmsCourseVersionIds ?? Enumerable.Empty<int>()).Where(x => x > 0).Distinct());
			if (ids.Length == 0)
				return new List<LmsCourseVersion>();
			return (await QueryAsync<LmsCourseVersion>(
				$"SELECT * FROM {Tbl("LmsCourseVersions")} WHERE {DepartmentOrSystem()} AND {InList("LmsCourseVersionId", "Ids")}", new { DepartmentId = departmentId, Ids = ids })).ToList();
		}

		public Task<LmsCourseVersion> GetAnyByIdAsync(int lmsCourseVersionId)
		{
			return QueryFirstOrDefaultAsync<LmsCourseVersion>(
				$"SELECT * FROM {Tbl("LmsCourseVersions")} WHERE {Col("LmsCourseVersionId")} = {P}Id", new { Id = lmsCourseVersionId });
		}

		public async Task<List<LmsCourseVersion>> GetInReviewAsync(int departmentId)
		{
			return (await QueryAsync<LmsCourseVersion>(
				$"SELECT * FROM {Tbl("LmsCourseVersions")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("Status")} = {(int)ProtocolVersionStatuses.InReview} ORDER BY {Col("SubmittedOn")}",
				new { DepartmentId = departmentId })).ToList();
		}

		public Task<bool> TryUpdateAsync(LmsCourseVersion version, long expectedRowVersion, CancellationToken cancellationToken = default) =>
			TryUpdateVersionedAsync(version, "LmsCourseVersions", "LmsCourseVersionId", version?.LmsCourseVersionId ?? 0, version?.DepartmentId ?? 0, expectedRowVersion,
				v => version.RowVersion = v, cancellationToken);
	}

	public class LmsCourseVersionApprovalsRepository : LmsRepositoryBase<LmsCourseVersionApproval>, ILmsCourseVersionApprovalsRepository
	{
		public LmsCourseVersionApprovalsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<List<LmsCourseVersionApproval>> GetForVersionAsync(int departmentId, int lmsCourseVersionId)
		{
			return (await QueryAsync<LmsCourseVersionApproval>(
				$"SELECT * FROM {Tbl("LmsCourseVersionApprovals")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsCourseVersionId")} = {P}VersionId " +
				$"ORDER BY {Col("Round")}, {Col("StepOrder")}", new { DepartmentId = departmentId, VersionId = lmsCourseVersionId })).ToList();
		}

		public async Task<bool> TryDecideAsync(LmsCourseVersionApproval step, CancellationToken cancellationToken = default)
		{
			if (step == null)
				return false;
			var parameters = new DynamicParameters();
			parameters.Add("Id", step.LmsCourseVersionApprovalId);
			parameters.Add("DepartmentId", step.DepartmentId);
			parameters.Add("Decision", step.Decision);
			parameters.Add("DecidedByUserId", step.DecidedByUserId, System.Data.DbType.String);
			parameters.Add("DecidedOn", Stamp(step.DecidedOn), System.Data.DbType.DateTime2);
			parameters.Add("Comment", step.Comment, System.Data.DbType.String);
			parameters.Add("SignatureRecordId", step.SignatureRecordId, System.Data.DbType.Int32);
			return await ExecuteAsync(
				$"UPDATE {Tbl("LmsCourseVersionApprovals")} SET {Col("Decision")} = {P}Decision, {Col("DecidedByUserId")} = {P}DecidedByUserId, {Col("DecidedOn")} = {P}DecidedOn, " +
				$"{Col("Comment")} = {P}Comment, {Col("SignatureRecordId")} = {P}SignatureRecordId " +
				$"WHERE {Col("LmsCourseVersionApprovalId")} = {P}Id AND {Col("DepartmentId")} = {P}DepartmentId AND {Col("Decision")} = {(int)ProtocolApprovalDecisions.Pending}",
				parameters, cancellationToken) == 1;
		}

		public Task<int> WithdrawPendingAsync(int departmentId, int lmsCourseVersionId, int round, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			return ExecuteAsync(
				$"UPDATE {Tbl("LmsCourseVersionApprovals")} SET {Col("Decision")} = {(int)ProtocolApprovalDecisions.Withdrawn}, {Col("DecidedOn")} = {P}Now " +
				$"WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsCourseVersionId")} = {P}VersionId AND {Col("Round")} = {P}Round AND {Col("Decision")} = {(int)ProtocolApprovalDecisions.Pending}",
				new { DepartmentId = departmentId, VersionId = lmsCourseVersionId, Round = round, Now = DatabaseTimestamp(utcNow) }, cancellationToken);
		}
	}

	public class LmsCourseAudiencesRepository : LmsRepositoryBase<LmsCourseAudience>, ILmsCourseAudiencesRepository
	{
		public LmsCourseAudiencesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<List<LmsCourseAudience>> GetForCourseAsync(int departmentId, int lmsCourseId)
		{
			return (await QueryAsync<LmsCourseAudience>(
				$"SELECT * FROM {Tbl("LmsCourseAudiences")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsCourseId")} = {P}CourseId",
				new { DepartmentId = departmentId, CourseId = lmsCourseId })).ToList();
		}

		public async Task<List<LmsCourseAudience>> GetForDepartmentAsync(int departmentId)
		{
			return (await QueryAsync<LmsCourseAudience>(
				$"SELECT * FROM {Tbl("LmsCourseAudiences")} WHERE {Col("DepartmentId")} = {P}DepartmentId", new { DepartmentId = departmentId })).ToList();
		}

		public async Task ReplaceAsync(int departmentId, int lmsCourseId, IEnumerable<IContentAudienceRule> rules, CancellationToken cancellationToken = default)
		{
			var owns = UnitOfWork.Transaction == null;
			UnitOfWork.CreateOrGetConnection();
			try
			{
				await ExecuteAsync($"DELETE FROM {Tbl("LmsCourseAudiences")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsCourseId")} = {P}CourseId",
					new { DepartmentId = departmentId, CourseId = lmsCourseId }, cancellationToken);
				foreach (var rule in ContentAudienceMatcher.Normalize(rules))
					await base.InsertAsync(new LmsCourseAudience { DepartmentId = departmentId, LmsCourseId = lmsCourseId, Kind = rule.Kind, RefId = rule.RefId }, cancellationToken, true);
				if (owns) UnitOfWork.CommitChanges();
			}
			catch
			{
				if (owns) UnitOfWork.DiscardChanges();
				throw;
			}
		}
	}

	public class LmsCourseOwnersRepository : LmsRepositoryBase<LmsCourseOwner>, ILmsCourseOwnersRepository
	{
		public LmsCourseOwnersRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<List<LmsCourseOwner>> GetForCourseAsync(int departmentId, int lmsCourseId)
		{
			return (await QueryAsync<LmsCourseOwner>(
				$"SELECT * FROM {Tbl("LmsCourseOwners")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsCourseId")} = {P}CourseId",
				new { DepartmentId = departmentId, CourseId = lmsCourseId })).ToList();
		}

		public async Task<List<LmsCourseOwner>> GetForUserAsync(int departmentId, string userId)
		{
			return (await QueryAsync<LmsCourseOwner>(
				$"SELECT * FROM {Tbl("LmsCourseOwners")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("UserId")} = {CaseInsensitive("UserId")}",
				new { DepartmentId = departmentId, UserId = userId })).ToList();
		}
	}

	public class LmsCourseModulesRepository : LmsRepositoryBase<LmsCourseModule>, ILmsCourseModulesRepository
	{
		public LmsCourseModulesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<List<LmsCourseModule>> GetForVersionAsync(int departmentId, int lmsCourseVersionId)
		{
			return (await QueryAsync<LmsCourseModule>(
				$"SELECT * FROM {Tbl("LmsCourseModules")} WHERE {DepartmentOrSystem()} AND {Col("LmsCourseVersionId")} = {P}VersionId ORDER BY {Col("SortOrder")}, {Col("LmsCourseModuleId")}",
				new { DepartmentId = departmentId, VersionId = lmsCourseVersionId })).ToList();
		}

		public Task<LmsCourseModule> GetVisibleByIdAsync(int departmentId, int lmsCourseModuleId)
		{
			return QueryFirstOrDefaultAsync<LmsCourseModule>(
				$"SELECT * FROM {Tbl("LmsCourseModules")} WHERE {DepartmentOrSystem()} AND {Col("LmsCourseModuleId")} = {P}Id", new { DepartmentId = departmentId, Id = lmsCourseModuleId });
		}
	}

	public class LmsLessonsRepository : LmsRepositoryBase<LmsLesson>, ILmsLessonsRepository
	{
		public LmsLessonsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<List<LmsLesson>> GetForVersionAsync(int departmentId, int lmsCourseVersionId)
		{
			return (await QueryAsync<LmsLesson>(
				$"SELECT * FROM {Tbl("LmsLessons")} WHERE {DepartmentOrSystem()} AND {Col("LmsCourseVersionId")} = {P}VersionId ORDER BY {Col("SortOrder")}, {Col("LmsLessonId")}",
				new { DepartmentId = departmentId, VersionId = lmsCourseVersionId })).ToList();
		}

		public Task<LmsLesson> GetVisibleByIdAsync(int departmentId, int lmsLessonId)
		{
			return QueryFirstOrDefaultAsync<LmsLesson>(
				$"SELECT * FROM {Tbl("LmsLessons")} WHERE {DepartmentOrSystem()} AND {Col("LmsLessonId")} = {P}Id", new { DepartmentId = departmentId, Id = lmsLessonId });
		}
	}

	public class LmsQuestionBanksRepository : LmsRepositoryBase<LmsQuestionBank>, ILmsQuestionBanksRepository
	{
		public LmsQuestionBanksRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<List<LmsQuestionBank>> GetForDepartmentAsync(int departmentId)
		{
			return (await QueryAsync<LmsQuestionBank>(
				$"SELECT * FROM {Tbl("LmsQuestionBanks")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("IsDeleted")} = {(IsPostgres ? "false" : "0")} ORDER BY {Col("Name")}",
				new { DepartmentId = departmentId })).ToList();
		}

		public Task<LmsQuestionBank> GetVisibleByIdAsync(int departmentId, int lmsQuestionBankId)
		{
			return QueryFirstOrDefaultAsync<LmsQuestionBank>(
				$"SELECT * FROM {Tbl("LmsQuestionBanks")} WHERE {DepartmentOrSystem()} AND {Col("LmsQuestionBankId")} = {P}Id", new { DepartmentId = departmentId, Id = lmsQuestionBankId });
		}

		public Task<LmsQuestionBank> GetBySystemKeyAsync(string systemKey)
		{
			return QueryFirstOrDefaultAsync<LmsQuestionBank>(
				$"SELECT * FROM {Tbl("LmsQuestionBanks")} WHERE {Col("DepartmentId")} = 0 AND {Col("SystemKey")} = {CaseInsensitive("SystemKey")}", new { SystemKey = systemKey });
		}
	}

	public class LmsQuestionsRepository : LmsRepositoryBase<LmsQuestion>, ILmsQuestionsRepository
	{
		public LmsQuestionsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<List<LmsQuestion>> GetForBankAsync(int departmentId, int lmsQuestionBankId)
		{
			return (await QueryAsync<LmsQuestion>(
				$"SELECT * FROM {Tbl("LmsQuestions")} WHERE {DepartmentOrSystem()} AND {Col("LmsQuestionBankId")} = {P}BankId ORDER BY {Col("SortOrder")}, {Col("LmsQuestionId")}",
				new { DepartmentId = departmentId, BankId = lmsQuestionBankId })).ToList();
		}

		public Task<LmsQuestion> GetVisibleByIdAsync(int departmentId, int lmsQuestionId)
		{
			return QueryFirstOrDefaultAsync<LmsQuestion>(
				$"SELECT * FROM {Tbl("LmsQuestions")} WHERE {DepartmentOrSystem()} AND {Col("LmsQuestionId")} = {P}Id", new { DepartmentId = departmentId, Id = lmsQuestionId });
		}
	}

	public class LmsAssessmentAttemptsRepository : LmsRepositoryBase<LmsAssessmentAttempt>, ILmsAssessmentAttemptsRepository
	{
		public LmsAssessmentAttemptsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<LmsAssessmentAttempt> GetByIdForDepartmentAsync(int departmentId, int lmsAssessmentAttemptId)
		{
			return QueryFirstOrDefaultAsync<LmsAssessmentAttempt>(
				$"SELECT * FROM {Tbl("LmsAssessmentAttempts")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsAssessmentAttemptId")} = {P}Id",
				new { DepartmentId = departmentId, Id = lmsAssessmentAttemptId });
		}

		public async Task<List<LmsAssessmentAttempt>> GetForEnrollmentLessonAsync(int departmentId, int lmsEnrollmentId, int lmsLessonId)
		{
			return (await QueryAsync<LmsAssessmentAttempt>(
				$"SELECT * FROM {Tbl("LmsAssessmentAttempts")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsEnrollmentId")} = {P}EnrollmentId AND {Col("LmsLessonId")} = {P}LessonId " +
				$"ORDER BY {Col("LmsAssessmentAttemptId")} DESC", new { DepartmentId = departmentId, EnrollmentId = lmsEnrollmentId, LessonId = lmsLessonId })).ToList();
		}

		public async Task<List<LmsAssessmentAttempt>> GetForUserLessonsAsync(int departmentId, string userId, IEnumerable<int> lmsLessonIds)
		{
			var ids = InListValue((lmsLessonIds ?? Enumerable.Empty<int>()).Where(x => x > 0).Distinct());
			if (ids.Length == 0 || string.IsNullOrWhiteSpace(userId))
				return new List<LmsAssessmentAttempt>();
			return (await QueryAsync<LmsAssessmentAttempt>(
				$"SELECT * FROM {Tbl("LmsAssessmentAttempts")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("UserId")} = {CaseInsensitive("UserId")} AND {InList("LmsLessonId", "Ids")} " +
				$"ORDER BY {Col("LmsAssessmentAttemptId")} DESC", new { DepartmentId = departmentId, UserId = userId, Ids = ids })).ToList();
		}

		public async Task<List<LmsAssessmentAttempt>> GetForUserWithoutSnapshotAsync(int departmentId, string userId)
		{
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(userId))
				return new List<LmsAssessmentAttempt>();
			return (await QueryAsync<LmsAssessmentAttempt>(
				$"SELECT {Cols("LmsAssessmentAttemptId", "DepartmentId", "LmsCourseVersionId", "LmsLessonId", "LmsEnrollmentId", "UserId", "AttemptNumber", "Status", "StartedOn",
					"DeadlineOn", "SubmittedOn", "Score", "MaxScore", "Percent", "Passed", "IsPreTest", "PassMark", "RevisionHash", "AnswersJson", "ResultsJson", "GradedOn",
					"GradeNotes", "Attested")} " +
				$"FROM {Tbl("LmsAssessmentAttempts")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("UserId")} = {CaseInsensitive("UserId")} " +
				$"ORDER BY {Col("LmsAssessmentAttemptId")} DESC", new { DepartmentId = departmentId, UserId = userId })).ToList();
		}

		public async Task<List<LmsAssessmentAttempt>> GetPendingGradingAsync(int departmentId)
		{
			return (await QueryAsync<LmsAssessmentAttempt>(
				$"SELECT * FROM {Tbl("LmsAssessmentAttempts")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("Status")} = {(int)LmsAttemptStatuses.PendingGrading} ORDER BY {Col("SubmittedOn")}",
				new { DepartmentId = departmentId })).ToList();
		}

		public async Task<List<LmsAssessmentAttempt>> GetFinishedForLessonAsync(int departmentId, int lmsLessonId)
		{
			return (await QueryAsync<LmsAssessmentAttempt>(
				$"SELECT * FROM {Tbl("LmsAssessmentAttempts")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsLessonId")} = {P}LessonId " +
				$"AND {Col("Status")} IN ({(int)LmsAttemptStatuses.Scored}, {(int)LmsAttemptStatuses.Graded})", new { DepartmentId = departmentId, LessonId = lmsLessonId })).ToList();
		}

		public Task<bool> TryUpdateAsync(LmsAssessmentAttempt attempt, long expectedRowVersion, CancellationToken cancellationToken = default) =>
			TryUpdateVersionedAsync(attempt, "LmsAssessmentAttempts", "LmsAssessmentAttemptId", attempt?.LmsAssessmentAttemptId ?? 0, attempt?.DepartmentId ?? 0, expectedRowVersion,
				v => attempt.RowVersion = v, cancellationToken);
	}

	public class LmsEnrollmentsRepository : LmsRepositoryBase<LmsEnrollment>, ILmsEnrollmentsRepository
	{
		public LmsEnrollmentsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		private string Open => $"{Col("IsOpen")} = {(IsPostgres ? "true" : "1")}";

		public Task<LmsEnrollment> GetByIdForDepartmentAsync(int departmentId, int lmsEnrollmentId)
		{
			return QueryFirstOrDefaultAsync<LmsEnrollment>(
				$"SELECT * FROM {Tbl("LmsEnrollments")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsEnrollmentId")} = {P}Id", new { DepartmentId = departmentId, Id = lmsEnrollmentId });
		}

		public Task<LmsEnrollment> GetOpenAsync(int departmentId, string userId, int lmsCourseId)
		{
			return QueryFirstOrDefaultAsync<LmsEnrollment>(
				$"SELECT * FROM {Tbl("LmsEnrollments")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("UserId")} = {CaseInsensitive("UserId")} AND {Col("LmsCourseId")} = {P}CourseId AND {Open}",
				new { DepartmentId = departmentId, UserId = userId, CourseId = lmsCourseId });
		}

		public async Task<List<LmsEnrollment>> GetForUserAsync(int departmentId, string userId)
		{
			return (await QueryAsync<LmsEnrollment>(
				$"SELECT * FROM {Tbl("LmsEnrollments")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("UserId")} = {CaseInsensitive("UserId")} ORDER BY {Col("LmsEnrollmentId")} DESC",
				new { DepartmentId = departmentId, UserId = userId })).ToList();
		}

		public async Task<List<LmsEnrollment>> GetForCourseAsync(int departmentId, int lmsCourseId)
		{
			return (await QueryAsync<LmsEnrollment>(
				$"SELECT * FROM {Tbl("LmsEnrollments")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsCourseId")} = {P}CourseId ORDER BY {Col("LmsEnrollmentId")} DESC",
				new { DepartmentId = departmentId, CourseId = lmsCourseId })).ToList();
		}

		public async Task<List<LmsEnrollment>> GetForCoursesAsync(int departmentId, IEnumerable<int> lmsCourseIds)
		{
			var ids = InListValue((lmsCourseIds ?? Enumerable.Empty<int>()).Where(x => x > 0).Distinct());
			if (ids.Length == 0)
				return new List<LmsEnrollment>();
			return (await QueryAsync<LmsEnrollment>(
				$"SELECT * FROM {Tbl("LmsEnrollments")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {InList("LmsCourseId", "Ids")}", new { DepartmentId = departmentId, Ids = ids })).ToList();
		}

		public async Task<List<LmsEnrollment>> GetOpenForCourseAnyDepartmentAsync(int lmsCourseId)
		{
			return (await QueryAsync<LmsEnrollment>(
				$"SELECT * FROM {Tbl("LmsEnrollments")} WHERE {Col("LmsCourseId")} = {P}CourseId AND {Open}", new { CourseId = lmsCourseId })).ToList();
		}

		public async Task<(LmsEnrollment Enrollment, bool Created)> InsertOpenAsync(LmsEnrollment enrollment, CancellationToken cancellationToken = default)
		{
			var existing = await GetOpenAsync(enrollment.DepartmentId, enrollment.UserId, enrollment.LmsCourseId);
			if (existing != null)
				return (existing, false);

			enrollment.IsOpen = true;
			if (await TryInsertUniqueAsync(enrollment, cancellationToken))
				return (enrollment, true);

			return (await GetOpenAsync(enrollment.DepartmentId, enrollment.UserId, enrollment.LmsCourseId), false);
		}

		public Task<bool> TryUpdateAsync(LmsEnrollment enrollment, long expectedRowVersion, CancellationToken cancellationToken = default) =>
			TryUpdateVersionedAsync(enrollment, "LmsEnrollments", "LmsEnrollmentId", enrollment?.LmsEnrollmentId ?? 0, enrollment?.DepartmentId ?? 0, expectedRowVersion,
				v => enrollment.RowVersion = v, cancellationToken);

		public async Task<bool> HasCompletedVersionAsync(string userId, int lmsCourseVersionId)
		{
			if (string.IsNullOrWhiteSpace(userId) || lmsCourseVersionId <= 0)
				return false;
			return await ScalarAsync<int>(
				$"SELECT COUNT(1) FROM {Tbl("LmsEnrollments")} WHERE {Col("LmsCourseVersionId")} = {P}VersionId AND {Col("UserId")} = {CaseInsensitive("UserId")} " +
				$"AND {Col("Status")} = {(int)LmsEnrollmentStatuses.Completed}", new { VersionId = lmsCourseVersionId, UserId = userId }) > 0;
		}

		public async Task<List<LmsEnrollment>> GetReminderDueAsync(DateTime idleBeforeUtc, int take)
		{
			return (await QueryAsync<LmsEnrollment>(
				$"SELECT * FROM {Tbl("LmsEnrollments")} WHERE {Col("Status")} = {(int)LmsEnrollmentStatuses.InProgress} AND {Open} AND {Col("LastActivityOn")} < {P}IdleBefore " +
				$"AND ({Col("ReminderSentOn")} IS NULL OR {Col("ReminderSentOn")} < {Col("LastActivityOn")}) ORDER BY {Col("LastActivityOn")} {Take()}",
				new { IdleBefore = DatabaseTimestamp(idleBeforeUtc), Take = take })).ToList();
		}

		public async Task<bool> ClaimReminderAsync(int departmentId, int lmsEnrollmentId, DateTime? previousReminderOn, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			// The previous value decides the SQL shape in C#: a null DateTime parameter in "IS NULL" fails on PostgreSQL (42P08).
			var previous = previousReminderOn.HasValue ? $"{Col("ReminderSentOn")} = {P}Previous" : $"{Col("ReminderSentOn")} IS NULL";
			var parameters = new DynamicParameters();
			parameters.Add("DepartmentId", departmentId);
			parameters.Add("Id", lmsEnrollmentId);
			parameters.Add("Now", DatabaseTimestamp(utcNow), System.Data.DbType.DateTime2);
			if (previousReminderOn.HasValue)
				parameters.Add("Previous", DatabaseTimestamp(previousReminderOn.Value), System.Data.DbType.DateTime2);
			return await ExecuteAsync(
				$"UPDATE {Tbl("LmsEnrollments")} SET {Col("ReminderSentOn")} = {P}Now WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsEnrollmentId")} = {P}Id AND {previous}",
				parameters, cancellationToken) == 1;
		}
	}

	public class LmsLessonProgressRepository : LmsRepositoryBase<LmsLessonProgress>, ILmsLessonProgressRepository
	{
		public LmsLessonProgressRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<List<LmsLessonProgress>> GetForEnrollmentAsync(int departmentId, int lmsEnrollmentId)
		{
			return (await QueryAsync<LmsLessonProgress>(
				$"SELECT * FROM {Tbl("LmsLessonProgress")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsEnrollmentId")} = {P}EnrollmentId",
				new { DepartmentId = departmentId, EnrollmentId = lmsEnrollmentId })).ToList();
		}

		public Task<LmsLessonProgress> GetAsync(int departmentId, int lmsEnrollmentId, int lmsLessonId)
		{
			return QueryFirstOrDefaultAsync<LmsLessonProgress>(
				$"SELECT * FROM {Tbl("LmsLessonProgress")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsEnrollmentId")} = {P}EnrollmentId AND {Col("LmsLessonId")} = {P}LessonId",
				new { DepartmentId = departmentId, EnrollmentId = lmsEnrollmentId, LessonId = lmsLessonId });
		}

		/// <summary>The first save of a lesson's progress may race a second tab: the unique index keeps one row.</summary>
		public override async Task<LmsLessonProgress> InsertAsync(LmsLessonProgress entity, CancellationToken cancellationToken, bool firstLevelOnly = false)
		{
			if (await TryInsertUniqueAsync(entity, cancellationToken))
				return entity;
			var stored = await GetAsync(entity.DepartmentId, entity.LmsEnrollmentId, entity.LmsLessonId);
			if (stored == null)
				return entity;
			entity.LmsLessonProgressId = stored.LmsLessonProgressId;
			await base.UpdateAsync(entity, cancellationToken, true);
			return entity;
		}
	}

	public class LmsLessonFilesRepository : LmsRepositoryBase<LmsLessonFile>, ILmsLessonFilesRepository
	{
		public LmsLessonFilesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<LmsLessonFile> GetByIdForDepartmentAsync(int departmentId, int lmsLessonFileId)
		{
			return QueryFirstOrDefaultAsync<LmsLessonFile>(
				$"SELECT * FROM {Tbl("LmsLessonFiles")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsLessonFileId")} = {P}Id", new { DepartmentId = departmentId, Id = lmsLessonFileId });
		}
	}

	public class LmsSurveyResponsesRepository : LmsRepositoryBase<LmsSurveyResponse>, ILmsSurveyResponsesRepository
	{
		public LmsSurveyResponsesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<List<LmsSurveyResponse>> GetForLessonAsync(int departmentId, int lmsLessonId)
		{
			return (await QueryAsync<LmsSurveyResponse>(
				$"SELECT * FROM {Tbl("LmsSurveyResponses")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsLessonId")} = {P}LessonId",
				new { DepartmentId = departmentId, LessonId = lmsLessonId })).ToList();
		}
	}

	public class LmsCrewViewingsRepository : LmsRepositoryBase<LmsCrewViewing>, ILmsCrewViewingsRepository
	{
		public LmsCrewViewingsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<LmsCrewViewing> GetByIdForDepartmentAsync(int departmentId, int lmsCrewViewingId)
		{
			return QueryFirstOrDefaultAsync<LmsCrewViewing>(
				$"SELECT * FROM {Tbl("LmsCrewViewings")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsCrewViewingId")} = {P}Id", new { DepartmentId = departmentId, Id = lmsCrewViewingId });
		}
	}

	public class LmsCrewViewingAttendeesRepository : LmsRepositoryBase<LmsCrewViewingAttendee>, ILmsCrewViewingAttendeesRepository
	{
		public LmsCrewViewingAttendeesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<List<LmsCrewViewingAttendee>> GetForViewingAsync(int departmentId, int lmsCrewViewingId)
		{
			return (await QueryAsync<LmsCrewViewingAttendee>(
				$"SELECT * FROM {Tbl("LmsCrewViewingAttendees")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsCrewViewingId")} = {P}ViewingId ORDER BY {Col("MarkedOn")}",
				new { DepartmentId = departmentId, ViewingId = lmsCrewViewingId })).ToList();
		}

		public async Task<bool> TryInsertAsync(LmsCrewViewingAttendee attendee, CancellationToken cancellationToken = default)
		{
			var existing = await ScalarAsync<int>(
				$"SELECT COUNT(1) FROM {Tbl("LmsCrewViewingAttendees")} WHERE {Col("LmsCrewViewingId")} = {P}ViewingId AND {Col("UserId")} = {CaseInsensitive("UserId")}",
				new { ViewingId = attendee.LmsCrewViewingId, attendee.UserId }, cancellationToken);
			return existing == 0 && await TryInsertUniqueAsync(attendee, cancellationToken);
		}
	}

	public class LmsPathAssignmentsRepository : LmsRepositoryBase<LmsPathAssignment>, ILmsPathAssignmentsRepository
	{
		public LmsPathAssignmentsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<List<LmsPathAssignment>> GetForDepartmentAsync(int departmentId)
		{
			return (await QueryAsync<LmsPathAssignment>(
				$"SELECT * FROM {Tbl("LmsPathAssignments")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RemovedOn")} IS NULL", new { DepartmentId = departmentId })).ToList();
		}

		public async Task<List<LmsPathAssignment>> GetForUserAsync(int departmentId, string userId)
		{
			return (await QueryAsync<LmsPathAssignment>(
				$"SELECT * FROM {Tbl("LmsPathAssignments")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("UserId")} = {CaseInsensitive("UserId")} AND {Col("RemovedOn")} IS NULL",
				new { DepartmentId = departmentId, UserId = userId })).ToList();
		}

		public Task<int> LinkSystemPathAsync(string pathKey, int lmsPathId, CancellationToken cancellationToken = default)
		{
			return ExecuteAsync(
				$"UPDATE {Tbl("LmsPathAssignments")} SET {Col("LmsPathId")} = {P}PathId WHERE {Col("PathKey")} = {CaseInsensitive("PathKey")} AND {Col("LmsPathId")} IS NULL " +
				$"AND {Col("DepartmentId")} > 0", new { PathId = lmsPathId, PathKey = pathKey }, cancellationToken);
		}

		public async Task<bool> TryCompleteAsync(int departmentId, int lmsPathAssignmentId, DateTime completedOnUtc, int? personnelCertificationId, CancellationToken cancellationToken = default)
		{
			return await ExecuteAsync(
				$"UPDATE {Tbl("LmsPathAssignments")} SET {Col("CompletedOn")} = {P}CompletedOn, {Col("PersonnelCertificationId")} = {P}CertificationId " +
				$"WHERE {Col("LmsPathAssignmentId")} = {P}Id AND {Col("DepartmentId")} = {P}DepartmentId AND {Col("CompletedOn")} IS NULL",
				new { CompletedOn = DatabaseTimestamp(completedOnUtc), CertificationId = personnelCertificationId, Id = lmsPathAssignmentId, DepartmentId = departmentId },
				cancellationToken) == 1;
		}
	}

	public class LmsDepartmentStatesRepository : LmsRepositoryBase<LmsDepartmentState>, ILmsDepartmentStatesRepository
	{
		public LmsDepartmentStatesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<LmsDepartmentState> GetForDepartmentAsync(int departmentId)
		{
			return QueryFirstOrDefaultAsync<LmsDepartmentState>(
				$"SELECT * FROM {Tbl("LmsDepartmentStates")} WHERE {Col("DepartmentId")} = {P}DepartmentId", new { DepartmentId = departmentId });
		}

		public async Task StampEntitledAsync(int departmentId, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var parameters = new { DepartmentId = departmentId, Now = DatabaseTimestamp(utcNow) };
			if (IsPostgres)
			{
				await ExecuteAsync(
					$"INSERT INTO {Tbl("LmsDepartmentStates")} ({Cols("DepartmentId", "LastEntitledOn", "UpdatedOn")}) VALUES ({P}DepartmentId, {P}Now, {P}Now) " +
					$"ON CONFLICT ({Col("DepartmentId")}) DO UPDATE SET {Col("LastEntitledOn")} = EXCLUDED.{Col("LastEntitledOn")}, {Col("UpdatedOn")} = EXCLUDED.{Col("UpdatedOn")}",
					parameters, cancellationToken);
				return;
			}

			var updated = await ExecuteAsync(
				$"UPDATE {Tbl("LmsDepartmentStates")} SET {Col("LastEntitledOn")} = {P}Now, {Col("UpdatedOn")} = {P}Now WHERE {Col("DepartmentId")} = {P}DepartmentId", parameters, cancellationToken);
			if (updated == 0 && !await TryInsertUniqueAsync(new LmsDepartmentState { DepartmentId = departmentId, LastEntitledOn = utcNow, UpdatedOn = utcNow }, cancellationToken))
				await ExecuteAsync(
					$"UPDATE {Tbl("LmsDepartmentStates")} SET {Col("LastEntitledOn")} = {P}Now, {Col("UpdatedOn")} = {P}Now WHERE {Col("DepartmentId")} = {P}DepartmentId", parameters, cancellationToken);
		}

		public async Task<List<int>> GetLapsedWithReadyMediaAsync(DateTime entitledBeforeUtc)
		{
			return (await QueryAsync<int>(
				$"SELECT DISTINCT m.{Col("DepartmentId")} FROM {Tbl("LmsMediaAssets")} m JOIN {Tbl("LmsDepartmentStates")} s ON s.{Col("DepartmentId")} = m.{Col("DepartmentId")} " +
				$"WHERE m.{Col("Status")} = {(int)LmsMediaStatuses.Ready} AND s.{Col("LastEntitledOn")} < {P}Before",
				new { Before = DatabaseTimestamp(entitledBeforeUtc) })).ToList();
		}
	}

	public class LmsMediaAssetsRepository : LmsRepositoryBase<LmsMediaAsset>, ILmsMediaAssetsRepository
	{
		public LmsMediaAssetsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<LmsMediaAsset> GetByIdForDepartmentAsync(int departmentId, int lmsMediaAssetId)
		{
			return QueryFirstOrDefaultAsync<LmsMediaAsset>(
				$"SELECT * FROM {Tbl("LmsMediaAssets")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsMediaAssetId")} = {P}Id", new { DepartmentId = departmentId, Id = lmsMediaAssetId });
		}

		public async Task<List<LmsMediaAsset>> GetForDepartmentAsync(int departmentId)
		{
			return (await QueryAsync<LmsMediaAsset>(
				$"SELECT * FROM {Tbl("LmsMediaAssets")} WHERE {Col("DepartmentId")} = {P}DepartmentId ORDER BY {Col("LmsMediaAssetId")} DESC", new { DepartmentId = departmentId })).ToList();
		}

		public async Task<LmsMediaAsset> ClaimNextAsync(DateTime utcNow, DateTime staleClaimBeforeUtc, CancellationToken cancellationToken = default)
		{
			var parameters = new { Now = DatabaseTimestamp(utcNow), Stale = DatabaseTimestamp(staleClaimBeforeUtc) };
			var due = $"({Col("Status")} = {(int)LmsMediaStatuses.Queued} OR ({Col("Status")} = {(int)LmsMediaStatuses.Transcoding} AND {Col("ClaimedOn")} < {P}Stale))";
			var sql = IsPostgres
				? $"UPDATE {Tbl("LmsMediaAssets")} SET {Col("Status")} = {(int)LmsMediaStatuses.Transcoding}, {Col("ClaimedOn")} = {P}Now, {Col("Attempts")} = {Col("Attempts")} + 1 " +
				  $"WHERE {Col("LmsMediaAssetId")} = (SELECT {Col("LmsMediaAssetId")} FROM {Tbl("LmsMediaAssets")} WHERE {due} ORDER BY {Col("CreatedOn")} LIMIT 1 FOR UPDATE SKIP LOCKED) RETURNING *"
				: $"WITH next AS (SELECT TOP (1) * FROM {Tbl("LmsMediaAssets")} WITH (ROWLOCK, READPAST, UPDLOCK) WHERE {due} ORDER BY {Col("CreatedOn")}) " +
				  $"UPDATE next SET {Col("Status")} = {(int)LmsMediaStatuses.Transcoding}, {Col("ClaimedOn")} = {P}Now, {Col("Attempts")} = {Col("Attempts")} + 1 OUTPUT INSERTED.*;";
			return await QueryFirstOrDefaultAsync<LmsMediaAsset>(sql, parameters, cancellationToken);
		}

		public async Task<bool> TryMoveAsync(LmsMediaAsset asset, LmsMediaStatuses from, CancellationToken cancellationToken = default)
		{
			if (asset == null)
				return false;
			var parameters = new DynamicParameters();
			parameters.Add("Id", asset.LmsMediaAssetId);
			parameters.Add("DepartmentId", asset.DepartmentId);
			parameters.Add("From", (int)from);
			parameters.Add("Status", asset.Status);
			parameters.Add("SourceKey", asset.SourceKey, System.Data.DbType.String);
			parameters.Add("SourceBytes", asset.SourceBytes);
			parameters.Add("HlsPrefix", asset.HlsPrefix, System.Data.DbType.String);
			parameters.Add("Mp4Key", asset.Mp4Key, System.Data.DbType.String);
			parameters.Add("OutputBytes", asset.OutputBytes);
			parameters.Add("DurationSeconds", asset.DurationSeconds);
			parameters.Add("LastError", asset.LastError, System.Data.DbType.String);
			parameters.Add("ReadyOn", Stamp(asset.ReadyOn), System.Data.DbType.DateTime2);
			parameters.Add("ArchivedOn", Stamp(asset.ArchivedOn), System.Data.DbType.DateTime2);
			return await ExecuteAsync(
				$"UPDATE {Tbl("LmsMediaAssets")} SET {Col("Status")} = {P}Status, {Col("SourceKey")} = {P}SourceKey, {Col("SourceBytes")} = {P}SourceBytes, {Col("HlsPrefix")} = {P}HlsPrefix, " +
				$"{Col("Mp4Key")} = {P}Mp4Key, {Col("OutputBytes")} = {P}OutputBytes, {Col("DurationSeconds")} = {P}DurationSeconds, {Col("LastError")} = {P}LastError, " +
				$"{Col("ReadyOn")} = {P}ReadyOn, {Col("ArchivedOn")} = {P}ArchivedOn " +
				$"WHERE {Col("LmsMediaAssetId")} = {P}Id AND {Col("DepartmentId")} = {P}DepartmentId AND {Col("Status")} = {P}From", parameters, cancellationToken) == 1;
		}

		public Task<long> GetStoredBytesAsync(int departmentId)
		{
			return ScalarAsync<long>(
				$"SELECT CAST(COALESCE(SUM({Col("SourceBytes")} + {Col("OutputBytes")}), 0) AS bigint) FROM {Tbl("LmsMediaAssets")} WHERE {Col("DepartmentId")} = {P}DepartmentId",
				new { DepartmentId = departmentId });
		}
	}
}
