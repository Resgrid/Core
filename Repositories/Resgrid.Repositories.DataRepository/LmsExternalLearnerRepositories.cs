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
	// Training T5 item 2 (round 6E, M0396–M0397). Portable SQL over RmsRepositoryBase; e-mail and learner ids are citext on PostgreSQL, so
	// their parameters are cast (pg-citext-text-param-equality); lists use InList (postgres-dapper-in-list).

	public class LmsLearnerPortalsRepository : RmsRepositoryBase<LmsLearnerPortal>, ILmsLearnerPortalsRepository
	{
		public LmsLearnerPortalsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<LmsLearnerPortal> GetByDepartmentAsync(int departmentId) =>
			QueryFirstOrDefaultAsync<LmsLearnerPortal>($"SELECT * FROM {Tbl("LmsLearnerPortals")} WHERE {Col("DepartmentId")} = {P}DepartmentId",
				new { DepartmentId = departmentId });

		public Task<LmsLearnerPortal> GetByKeyAsync(string portalKey) =>
			string.IsNullOrWhiteSpace(portalKey) ? Task.FromResult<LmsLearnerPortal>(null) :
			QueryFirstOrDefaultAsync<LmsLearnerPortal>($"SELECT * FROM {Tbl("LmsLearnerPortals")} WHERE {Col("PortalKey")} = {CaseInsensitive("PortalKey")}",
				new { PortalKey = portalKey.Trim() });
	}

	public class LmsExternalLearnersRepository : RmsRepositoryBase<LmsExternalLearner>, ILmsExternalLearnersRepository
	{
		private readonly IQueryFactory _queries;

		public LmsExternalLearnersRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_queries = queryFactory;
		}

		public async Task<bool> TryUpdateAsync(LmsExternalLearner learner, long expectedRowVersion, CancellationToken cancellationToken = default)
		{
			if (learner == null || learner.LmsExternalLearnerId <= 0)
				return false;
			Utf8WriteGuard.Sanitize(learner);
			var previous = learner.RowVersion;
			learner.RowVersion = expectedRowVersion + 1;
			// One statement, a compare-and-set needing no transaction: every column and the bumped version, only while the stored version is
			// still the one this copy was read at.
			var parameters = new DynamicParameters(learner);
			parameters.Add("ExpectedRowVersion", expectedRowVersion);
			var sql = _queries.GetUpdateQuery<Resgrid.Repositories.DataRepository.Queries.Common.UpdateQuery, LmsExternalLearner>(learner) +
				$" AND {Col("DepartmentId")} = {P}DepartmentId AND {Col("RowVersion")} = {P}ExpectedRowVersion";
			if (await ExecuteAsync(sql, parameters, cancellationToken) == 1)
				return true;
			learner.RowVersion = previous;
			return false;
		}

		/// <summary>
		/// Security review follow-up (M0412): every whole-row write of an existing learner is a compare-and-set on RowVersion, so a copy read
		/// before a stamp rotation, a password change or a suspension is refused instead of writing the old values back.
		/// </summary>
		public override async Task<LmsExternalLearner> UpdateAsync(LmsExternalLearner entity, CancellationToken cancellationToken, bool firstLevelOnly = false)
		{
			if (!await TryUpdateAsync(entity, entity?.RowVersion ?? 0, cancellationToken))
				throw new System.Data.DBConcurrencyException("The external learner was changed by another save; read it again.");
			return entity;
		}

		public async Task<string> EnsureUnsubscribeKeyAsync(int departmentId, int lmsExternalLearnerId, string newKey, CancellationToken cancellationToken = default)
		{
			await ExecuteAsync(
				$"UPDATE {Tbl("LmsExternalLearners")} SET {Col("UnsubscribeKey")} = {P}Key, {Col("RowVersion")} = {Col("RowVersion")} + 1 " +
				$"WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsExternalLearnerId")} = {P}Id AND {Col("UnsubscribeKey")} IS NULL",
				new { DepartmentId = departmentId, Id = lmsExternalLearnerId, Key = newKey }, cancellationToken);
			return (await GetByIdForDepartmentAsync(departmentId, lmsExternalLearnerId))?.UnsubscribeKey;
		}

		public Task SetBillingContactAsync(int departmentId, int lmsExternalLearnerId, string billingContactId, DateTime utcNow, CancellationToken cancellationToken = default) =>
			ExecuteAsync(
				$"UPDATE {Tbl("LmsExternalLearners")} SET {Col("BillingContactId")} = {P}Contact, {Col("UpdatedOn")} = {P}Now, {Col("RowVersion")} = {Col("RowVersion")} + 1 " +
				$"WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsExternalLearnerId")} = {P}Id",
				new { DepartmentId = departmentId, Id = lmsExternalLearnerId, Contact = billingContactId, Now = DatabaseTimestamp(utcNow) }, cancellationToken);

		public Task<LmsExternalLearner> GetByIdForDepartmentAsync(int departmentId, int lmsExternalLearnerId) =>
			QueryFirstOrDefaultAsync<LmsExternalLearner>(
				$"SELECT * FROM {Tbl("LmsExternalLearners")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsExternalLearnerId")} = {P}Id",
				new { DepartmentId = departmentId, Id = lmsExternalLearnerId });

		public Task<LmsExternalLearner> GetByLearnerUserIdAsync(int departmentId, string learnerUserId) =>
			string.IsNullOrWhiteSpace(learnerUserId) ? Task.FromResult<LmsExternalLearner>(null) :
			QueryFirstOrDefaultAsync<LmsExternalLearner>(
				$"SELECT * FROM {Tbl("LmsExternalLearners")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LearnerUserId")} = {CaseInsensitive("UserId")}",
				new { DepartmentId = departmentId, UserId = learnerUserId });

		public Task<LmsExternalLearner> GetByEmailAsync(int departmentId, string email) =>
			string.IsNullOrWhiteSpace(email) ? Task.FromResult<LmsExternalLearner>(null) :
			QueryFirstOrDefaultAsync<LmsExternalLearner>(
				$"SELECT * FROM {Tbl("LmsExternalLearners")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("Email")} = {CaseInsensitive("Email")}",
				new { DepartmentId = departmentId, Email = LmsExternalLearnerRules.NormalizeEmail(email) });

		public async Task<List<LmsExternalLearner>> GetForDepartmentAsync(int departmentId) =>
			(await QueryAsync<LmsExternalLearner>(
				$"SELECT * FROM {Tbl("LmsExternalLearners")} WHERE {Col("DepartmentId")} = {P}DepartmentId ORDER BY {Col("LastName")}, {Col("FirstName")}, {Col("LmsExternalLearnerId")}",
				new { DepartmentId = departmentId })).ToList();

		public async Task<List<LmsExternalLearner>> GetByLearnerUserIdsAsync(int departmentId, IEnumerable<string> learnerUserIds)
		{
			var ids = InListValue((learnerUserIds ?? Enumerable.Empty<string>()).Where(LmsExternalLearnerIds.IsExternal).Distinct(StringComparer.OrdinalIgnoreCase));
			if (ids.Length == 0)
				return new List<LmsExternalLearner>();
			return (await QueryAsync<LmsExternalLearner>(
				$"SELECT * FROM {Tbl("LmsExternalLearners")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {CaseInsensitiveInList("LearnerUserId", "Ids")}",
				new { DepartmentId = departmentId, Ids = ids })).ToList();
		}

		// The key is a random token compared exactly: text on PostgreSQL (never cast to citext), and on SQL Server, whose default collation
		// ignores case, the row is checked again here.
		public async Task<LmsExternalLearner> GetByUnsubscribeKeyAsync(int departmentId, string unsubscribeKey)
		{
			if (string.IsNullOrWhiteSpace(unsubscribeKey) || unsubscribeKey.Length > 128)
				return null;
			var key = unsubscribeKey.Trim();
			var rows = await QueryAsync<LmsExternalLearner>(
				$"SELECT * FROM {Tbl("LmsExternalLearners")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("UnsubscribeKey")} = {P}UnsubscribeKey",
				new { DepartmentId = departmentId, UnsubscribeKey = key });
			return rows.FirstOrDefault(r => string.Equals(r.UnsubscribeKey, key, StringComparison.Ordinal));
		}

		public Task<int> CountCountedAsync(int departmentId) =>
			ScalarAsync<int>(
				$"SELECT COUNT(1) FROM {Tbl("LmsExternalLearners")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {InList("Status", "Statuses")}",
				new { DepartmentId = departmentId, Statuses = InListValue(new[] { (int)LmsExternalLearnerStatuses.Invited, (int)LmsExternalLearnerStatuses.Active }) });

		public Task<int> PurgeLearnerDataAsync(int departmentId, string learnerUserId, CancellationToken cancellationToken = default)
		{
			// Only ever an external learner's synthetic id: a member's rows are never touched by this path.
			if (departmentId <= 0 || !LmsExternalLearnerIds.IsExternal(learnerUserId))
				return Task.FromResult(0);

			var parameters = new DynamicParameters();
			parameters.Add("DepartmentId", departmentId);
			parameters.Add("UserId", learnerUserId);
			var mine = $"{Col("DepartmentId")} = {P}DepartmentId AND {Col("UserId")} = {CaseInsensitive("UserId")}";
			// Children before parents (lesson progress references lesson files and enrolments; attempts reference enrolments).
			var statements = new[] { "LmsLessonProgress", "LmsAssessmentAttempts", "LmsLessonFiles", "LmsCrewViewingAttendees", "LmsPathAssignments", "LmsEnrollments",
				"LearningRecordFiles", "LearningRecords" }.Select(t => $"DELETE FROM {Tbl(t)} WHERE {mine}").ToList();
			statements.Add($"UPDATE {Tbl("LmsClassEnrollments")} SET {Col("MemberName")} = NULL, {Col("JobTitle")} = NULL, {Col("Note")} = NULL WHERE {mine}");

			return RunAsync(async connection =>
			{
				var owned = UnitOfWork?.Transaction == null;
				var transaction = UnitOfWork?.Transaction ?? await connection.BeginTransactionAsync(cancellationToken);
				try
				{
					var touched = 0;
					foreach (var statement in statements)
						touched += await connection.ExecuteAsync(new Dapper.CommandDefinition(statement, parameters, transaction, cancellationToken: cancellationToken));
					if (owned)
						await transaction.CommitAsync(cancellationToken);
					return touched;
				}
				catch
				{
					if (owned)
						await transaction.RollbackAsync(CancellationToken.None);
					throw;
				}
				finally
				{
					if (owned)
						await transaction.DisposeAsync();
				}
			}, cancellationToken);
		}
	}

	public class LmsExternalLearnerTokensRepository : RmsRepositoryBase<LmsExternalLearnerToken>, ILmsExternalLearnerTokensRepository
	{
		public LmsExternalLearnerTokensRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<LmsExternalLearnerToken> GetByHashAsync(string tokenHash) =>
			string.IsNullOrWhiteSpace(tokenHash) ? Task.FromResult<LmsExternalLearnerToken>(null) :
			QueryFirstOrDefaultAsync<LmsExternalLearnerToken>($"SELECT * FROM {Tbl("LmsExternalLearnerTokens")} WHERE {Col("TokenHash")} = {P}Hash",
				new { Hash = tokenHash });

		public async Task<bool> ClaimAsync(int lmsExternalLearnerTokenId, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var parameters = new DynamicParameters();
			parameters.Add("Id", lmsExternalLearnerTokenId);
			parameters.Add("Now", DatabaseTimestamp(utcNow), System.Data.DbType.DateTime2);
			return await ExecuteAsync(
				$"UPDATE {Tbl("LmsExternalLearnerTokens")} SET {Col("UsedOn")} = {P}Now WHERE {Col("LmsExternalLearnerTokenId")} = {P}Id AND {Col("UsedOn")} IS NULL AND {Col("ExpiresOn")} > {P}Now",
				parameters, cancellationToken) == 1;
		}

		public Task<int> DeleteForLearnerAsync(int departmentId, int lmsExternalLearnerId, CancellationToken cancellationToken = default) =>
			ExecuteAsync($"DELETE FROM {Tbl("LmsExternalLearnerTokens")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsExternalLearnerId")} = {P}Id",
				new { DepartmentId = departmentId, Id = lmsExternalLearnerId }, cancellationToken);
	}

	public class LmsPublicClassesRepository : RmsRepositoryBase<LmsPublicClass>, ILmsPublicClassesRepository
	{
		public LmsPublicClassesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<LmsPublicClass> GetBySessionAsync(int departmentId, int lmsClassSessionId) =>
			QueryFirstOrDefaultAsync<LmsPublicClass>(
				$"SELECT * FROM {Tbl("LmsPublicClasses")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsClassSessionId")} = {P}SessionId",
				new { DepartmentId = departmentId, SessionId = lmsClassSessionId });

		public async Task<List<LmsPublicClass>> GetForDepartmentAsync(int departmentId) =>
			(await QueryAsync<LmsPublicClass>($"SELECT * FROM {Tbl("LmsPublicClasses")} WHERE {Col("DepartmentId")} = {P}DepartmentId",
				new { DepartmentId = departmentId })).ToList();
	}

	public class LmsClassFeesRepository : RmsRepositoryBase<LmsClassFee>, ILmsClassFeesRepository
	{
		public LmsClassFeesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<List<LmsClassFee>> GetForSessionAsync(int departmentId, int lmsClassSessionId) =>
			(await QueryAsync<LmsClassFee>(
				$"SELECT * FROM {Tbl("LmsClassFees")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsClassSessionId")} = {P}SessionId ORDER BY {Col("LmsClassFeeId")}",
				new { DepartmentId = departmentId, SessionId = lmsClassSessionId })).ToList();

		public Task<LmsClassFee> GetByEnrollmentAsync(int departmentId, int lmsClassEnrollmentId) =>
			QueryFirstOrDefaultAsync<LmsClassFee>(
				$"SELECT * FROM {Tbl("LmsClassFees")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsClassEnrollmentId")} = {P}EnrollmentId",
				new { DepartmentId = departmentId, EnrollmentId = lmsClassEnrollmentId });

		public async Task<List<LmsClassFee>> GetForLearnerAsync(int departmentId, string learnerUserId) =>
			string.IsNullOrWhiteSpace(learnerUserId) ? new List<LmsClassFee>() :
			(await QueryAsync<LmsClassFee>(
				$"SELECT * FROM {Tbl("LmsClassFees")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LearnerUserId")} = {CaseInsensitive("UserId")} ORDER BY {Col("LmsClassFeeId")}",
				new { DepartmentId = departmentId, UserId = learnerUserId })).ToList();

		public Task<int> ClaimAsync(int departmentId, IEnumerable<int> lmsClassFeeIds, string claim, CancellationToken cancellationToken = default)
		{
			var ids = InListValue(lmsClassFeeIds);
			if (ids.Length == 0 || string.IsNullOrWhiteSpace(claim))
				return Task.FromResult(0);
			return ExecuteAsync(
				$"UPDATE {Tbl("LmsClassFees")} SET {Col("InvoiceId")} = {P}Claim WHERE {Col("DepartmentId")} = {P}DepartmentId AND {InList("LmsClassFeeId", "Ids")} " +
				$"AND {Col("InvoiceId")} IS NULL AND {Col("Waived")} = {P}No",
				new { DepartmentId = departmentId, Ids = ids, Claim = claim, No = false }, cancellationToken);
		}

		public Task<int> CompleteClaimAsync(int departmentId, IEnumerable<int> lmsClassFeeIds, string claim, string invoiceId, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var ids = InListValue(lmsClassFeeIds);
			if (ids.Length == 0)
				return Task.FromResult(0);
			var parameters = new DynamicParameters();
			parameters.Add("DepartmentId", departmentId);
			parameters.Add("Ids", ids);
			parameters.Add("Claim", claim);
			parameters.Add("InvoiceId", invoiceId);
			parameters.Add("Now", DatabaseTimestamp(utcNow), System.Data.DbType.DateTime2);
			return ExecuteAsync(
				$"UPDATE {Tbl("LmsClassFees")} SET {Col("InvoiceId")} = {P}InvoiceId, {Col("InvoicedOn")} = {P}Now, {Col("UpdatedOn")} = {P}Now " +
				$"WHERE {Col("DepartmentId")} = {P}DepartmentId AND {InList("LmsClassFeeId", "Ids")} AND {Col("InvoiceId")} = {P}Claim",
				parameters, cancellationToken);
		}

		public Task<int> ReleaseClaimAsync(int departmentId, string claim, CancellationToken cancellationToken = default) =>
			string.IsNullOrWhiteSpace(claim) ? Task.FromResult(0) :
			ExecuteAsync($"UPDATE {Tbl("LmsClassFees")} SET {Col("InvoiceId")} = NULL WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("InvoiceId")} = {P}Claim",
				new { DepartmentId = departmentId, Claim = claim }, cancellationToken);
	}

	public class LmsExternalCourseAccessRepository : RmsRepositoryBase<LmsExternalCourseAccess>, ILmsExternalCourseAccessRepository
	{
		public LmsExternalCourseAccessRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<List<LmsExternalCourseAccess>> GetForDepartmentAsync(int departmentId) =>
			(await QueryAsync<LmsExternalCourseAccess>($"SELECT * FROM {Tbl("LmsExternalCourseAccess")} WHERE {Col("DepartmentId")} = {P}DepartmentId",
				new { DepartmentId = departmentId })).ToList();

		public Task<LmsExternalCourseAccess> GetByCourseAsync(int departmentId, int lmsCourseId) =>
			QueryFirstOrDefaultAsync<LmsExternalCourseAccess>(
				$"SELECT * FROM {Tbl("LmsExternalCourseAccess")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsCourseId")} = {P}CourseId",
				new { DepartmentId = departmentId, CourseId = lmsCourseId });
	}

	/// <summary>Round 7B (M0402): learner e-mail claims. The unique key (department, learner, session, kind, key) makes each notice once only.</summary>
	public class LmsLearnerNoticesRepository : RmsRepositoryBase<LmsLearnerNotice>, ILmsLearnerNoticesRepository
	{
		public LmsLearnerNoticesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<bool> TryClaimAsync(LmsLearnerNotice notice, CancellationToken cancellationToken = default)
		{
			if (notice == null || notice.DepartmentId <= 0 || notice.LmsExternalLearnerId <= 0 || string.IsNullOrWhiteSpace(notice.NoticeKey))
				return false;

			var parameters = new DynamicParameters();
			parameters.Add("DepartmentId", notice.DepartmentId);
			parameters.Add("LearnerId", notice.LmsExternalLearnerId);
			parameters.Add("SessionId", notice.LmsClassSessionId);
			parameters.Add("Kind", notice.Kind);
			parameters.Add("NoticeKey", notice.NoticeKey, System.Data.DbType.String);
			parameters.Add("ClaimedOn", DatabaseTimestamp(notice.ClaimedOn == default ? DateTime.UtcNow : notice.ClaimedOn), System.Data.DbType.DateTime2);
			var columns = Cols("DepartmentId", "LmsExternalLearnerId", "LmsClassSessionId", "Kind", "NoticeKey", "ClaimedOn");
			var exists = $"SELECT 1 FROM {Tbl("LmsLearnerNotices")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsExternalLearnerId")} = {P}LearnerId " +
				$"AND {Col("LmsClassSessionId")} = {P}SessionId AND {Col("Kind")} = {P}Kind AND {Col("NoticeKey")} = {P}NoticeKey";
			var values = $"{P}DepartmentId, {P}LearnerId, {P}SessionId, {P}Kind, {P}NoticeKey, {P}ClaimedOn";
			var sql = IsPostgres
				? $"INSERT INTO {Tbl("LmsLearnerNotices")} ({columns}) SELECT {values} WHERE NOT EXISTS ({exists}) RETURNING {Col("LmsLearnerNoticeId")}"
				: $"INSERT INTO {Tbl("LmsLearnerNotices")} ({columns}) OUTPUT INSERTED.{Col("LmsLearnerNoticeId")} SELECT {values} WHERE NOT EXISTS ({exists.Replace("WHERE", "WITH (UPDLOCK, HOLDLOCK) WHERE")})";
			int? id;
			try
			{
				id = await ScalarAsync<int?>(sql, parameters, cancellationToken);
			}
			catch (Exception ex) when (IsUniqueViolation(ex))
			{
				return false;
			}

			if (!id.HasValue || id.Value <= 0)
				return false;
			notice.LmsLearnerNoticeId = id.Value;
			return true;
		}

		public async Task<bool> MarkAsync(int departmentId, int lmsLearnerNoticeId, DateTime? sentOn, string skipped, CancellationToken cancellationToken = default)
		{
			var parameters = new DynamicParameters();
			parameters.Add("DepartmentId", departmentId);
			parameters.Add("Id", lmsLearnerNoticeId);
			parameters.Add("SentOn", sentOn.HasValue ? DatabaseTimestamp(sentOn.Value) : (DateTime?)null, System.Data.DbType.DateTime2);
			parameters.Add("Skipped", string.IsNullOrWhiteSpace(skipped) ? null : skipped.Length > 100 ? skipped.Substring(0, 100) : skipped, System.Data.DbType.String);
			return await ExecuteAsync(
				$"UPDATE {Tbl("LmsLearnerNotices")} SET {Col("SentOn")} = {P}SentOn, {Col("Skipped")} = {P}Skipped WHERE {Col("DepartmentId")} = {P}DepartmentId " +
				$"AND {Col("LmsLearnerNoticeId")} = {P}Id", parameters, cancellationToken) == 1;
		}

		public async Task<List<LmsLearnerNotice>> GetForLearnerAsync(int departmentId, int lmsExternalLearnerId) =>
			(await QueryAsync<LmsLearnerNotice>(
				$"SELECT * FROM {Tbl("LmsLearnerNotices")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsExternalLearnerId")} = {P}LearnerId " +
				$"ORDER BY {Col("ClaimedOn")}, {Col("LmsLearnerNoticeId")}", new { DepartmentId = departmentId, LearnerId = lmsExternalLearnerId })).ToList();

		public Task<int> DeleteForLearnerAsync(int departmentId, int lmsExternalLearnerId, CancellationToken cancellationToken = default) =>
			ExecuteAsync($"DELETE FROM {Tbl("LmsLearnerNotices")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsExternalLearnerId")} = {P}LearnerId",
				new { DepartmentId = departmentId, LearnerId = lmsExternalLearnerId }, cancellationToken);
	}
}
