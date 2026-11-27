using System;
using System.Collections.Generic;
using System.Data.Common;
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
	// Training T4 part 1 (round 5B, M0378–M0380). Portable SQL over RmsRepositoryBase; member ids are citext on PostgreSQL, so their
	// parameters are cast (pg-citext-text-param-equality); list parameters use InList (postgres-dapper-in-list); nullable timestamps never
	// decide the SQL shape through "IS NULL" on a parameter (pg-untyped-null-param-is-null).

	public class LmsClassSessionsRepository : RmsRepositoryBase<LmsClassSession>, ILmsClassSessionsRepository
	{
		public LmsClassSessionsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<LmsClassSession> GetByIdForDepartmentAsync(int departmentId, int lmsClassSessionId) =>
			QueryFirstOrDefaultAsync<LmsClassSession>(
				$"SELECT * FROM {Tbl("LmsClassSessions")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsClassSessionId")} = {P}Id",
				new { DepartmentId = departmentId, Id = lmsClassSessionId });

		public async Task<List<LmsClassSession>> GetForDepartmentAsync(int departmentId, DateTime? fromUtc, DateTime? toUtc, bool includeDeleted = false)
		{
			var parameters = new DynamicParameters();
			parameters.Add("DepartmentId", departmentId);
			parameters.Add("No", false);
			var where = $"{Col("DepartmentId")} = {P}DepartmentId";
			if (!includeDeleted)
				where += $" AND {Col("IsDeleted")} = {P}No";
			if (fromUtc.HasValue)
			{
				where += $" AND {Col("EndsOn")} >= {P}FromUtc";
				parameters.Add("FromUtc", DatabaseTimestamp(fromUtc.Value), System.Data.DbType.DateTime2);
			}
			if (toUtc.HasValue)
			{
				where += $" AND {Col("StartsOn")} < {P}ToUtc";
				parameters.Add("ToUtc", DatabaseTimestamp(toUtc.Value), System.Data.DbType.DateTime2);
			}

			return (await QueryAsync<LmsClassSession>(
				$"SELECT * FROM {Tbl("LmsClassSessions")} WHERE {where} ORDER BY {Col("StartsOn")}, {Col("LmsClassSessionId")}", parameters)).ToList();
		}

		public async Task<List<LmsClassSession>> GetBySeriesAsync(int departmentId, string seriesKey) =>
			string.IsNullOrWhiteSpace(seriesKey) ? new List<LmsClassSession>() :
			(await QueryAsync<LmsClassSession>(
				$"SELECT * FROM {Tbl("LmsClassSessions")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("SeriesKey")} = {CaseInsensitive("SeriesKey")} " +
				$"AND {Col("IsDeleted")} = {P}No ORDER BY {Col("SeriesIndex")}, {Col("StartsOn")}, {Col("LmsClassSessionId")}",
				new { DepartmentId = departmentId, SeriesKey = seriesKey, No = false })).ToList();

		public Task<LmsClassSession> GetByCalendarItemAsync(int departmentId, int calendarItemId) =>
			QueryFirstOrDefaultAsync<LmsClassSession>(
				$"SELECT * FROM {Tbl("LmsClassSessions")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("CalendarItemId")} = {P}CalendarItemId " +
				$"AND {Col("IsDeleted")} = {P}No ORDER BY {Col("LmsClassSessionId")}",
				new { DepartmentId = departmentId, CalendarItemId = calendarItemId, No = false });

		public async Task<List<LmsClassSession>> GetReminderCandidatesAsync(DateTime utcNow, DateTime horizonUtc, int take)
		{
			var parameters = new DynamicParameters();
			parameters.Add("Now", DatabaseTimestamp(utcNow), System.Data.DbType.DateTime2);
			parameters.Add("Horizon", DatabaseTimestamp(horizonUtc), System.Data.DbType.DateTime2);
			parameters.Add("No", false);
			parameters.Add("Scheduled", (int)LmsClassSessionStatuses.Scheduled);
			parameters.Add("Enrolled", (int)LmsClassEnrollmentStatuses.Enrolled);
			var limit = Math.Clamp(take, 1, 5000);
			var sql = $"SELECT {(IsPostgres ? string.Empty : $"TOP ({limit}) ")}s.* FROM {Tbl("LmsClassSessions")} s WHERE s.{Col("IsDeleted")} = {P}No " +
				$"AND s.{Col("Status")} = {P}Scheduled AND s.{Col("ReminderHours")} > 0 AND s.{Col("StartsOn")} > {P}Now AND s.{Col("StartsOn")} <= {P}Horizon " +
				$"AND EXISTS (SELECT 1 FROM {Tbl("LmsClassEnrollments")} e WHERE e.{Col("LmsClassSessionId")} = s.{Col("LmsClassSessionId")} " +
				$"AND e.{Col("DepartmentId")} = s.{Col("DepartmentId")} AND e.{Col("Status")} = {P}Enrolled AND e.{Col("ReminderSentOn")} IS NULL) " +
				$"ORDER BY s.{Col("StartsOn")}, s.{Col("LmsClassSessionId")}" + (IsPostgres ? $" LIMIT {limit}" : string.Empty);
			return (await QueryAsync<LmsClassSession>(sql, parameters)).ToList();
		}

		public async Task<List<LmsClassSession>> GetWaitlistCandidatesAsync(DateTime utcNow, int take)
		{
			var parameters = new DynamicParameters();
			parameters.Add("Now", DatabaseTimestamp(utcNow), System.Data.DbType.DateTime2);
			parameters.Add("No", false);
			parameters.Add("Yes", true);
			parameters.Add("Scheduled", (int)LmsClassSessionStatuses.Scheduled);
			parameters.Add("Waitlisted", (int)LmsClassEnrollmentStatuses.Waitlisted);
			var limit = Math.Clamp(take, 1, 5000);
			var sql = $"SELECT {(IsPostgres ? string.Empty : $"TOP ({limit}) ")}s.* FROM {Tbl("LmsClassSessions")} s WHERE s.{Col("IsDeleted")} = {P}No " +
				$"AND s.{Col("Status")} = {P}Scheduled AND s.{Col("WaitlistEnabled")} = {P}Yes AND s.{Col("StartsOn")} > {P}Now " +
				$"AND EXISTS (SELECT 1 FROM {Tbl("LmsClassEnrollments")} e WHERE e.{Col("LmsClassSessionId")} = s.{Col("LmsClassSessionId")} " +
				$"AND e.{Col("DepartmentId")} = s.{Col("DepartmentId")} AND e.{Col("Status")} = {P}Waitlisted) " +
				$"ORDER BY s.{Col("StartsOn")}, s.{Col("LmsClassSessionId")}" + (IsPostgres ? $" LIMIT {limit}" : string.Empty);
			return (await QueryAsync<LmsClassSession>(sql, parameters)).ToList();
		}

		public async Task<List<LmsClassSession>> GetPurgeCandidatesAsync(DateTime utcNow, DateTime deletedBeforeUtc, int take)
		{
			var parameters = new DynamicParameters();
			parameters.Add("Now", DatabaseTimestamp(utcNow), System.Data.DbType.DateTime2);
			parameters.Add("DeletedBefore", DatabaseTimestamp(deletedBeforeUtc), System.Data.DbType.DateTime2);
			parameters.Add("Yes", true);
			var limit = Math.Clamp(take, 1, 5000);
			var sql = $"SELECT {(IsPostgres ? string.Empty : $"TOP ({limit}) ")}* FROM {Tbl("LmsClassSessions")} WHERE {Col("IsDeleted")} = {P}Yes " +
				$"AND {Col("DeletedOn")} IS NOT NULL AND {Col("DeletedOn")} < {P}DeletedBefore AND ({Col("RetainUntil")} IS NULL OR {Col("RetainUntil")} <= {P}Now) " +
				$"ORDER BY {Col("DeletedOn")}, {Col("LmsClassSessionId")}" + (IsPostgres ? $" LIMIT {limit}" : string.Empty);
			return (await QueryAsync<LmsClassSession>(sql, parameters)).ToList();
		}

		public Task<bool> PurgeAsync(int departmentId, int lmsClassSessionId, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var parameters = new DynamicParameters();
			parameters.Add("DepartmentId", departmentId);
			parameters.Add("Id", lmsClassSessionId);
			parameters.Add("Now", DatabaseTimestamp(utcNow), System.Data.DbType.DateTime2);
			parameters.Add("Yes", true);

			var expired = $"{Col("IsDeleted")} = {P}Yes AND ({Col("RetainUntil")} IS NULL OR {Col("RetainUntil")} <= {P}Now)";
			var guard = $"EXISTS (SELECT 1 FROM {Tbl("LmsClassSessions")} s WHERE s.{Col("LmsClassSessionId")} = {P}Id AND s.{Col("DepartmentId")} = {P}DepartmentId " +
				$"AND s.{Col("IsDeleted")} = {P}Yes AND (s.{Col("RetainUntil")} IS NULL OR s.{Col("RetainUntil")} <= {P}Now))";
			var children = new[] { "LmsClassResourceBookings", "LmsClassCostLines", "LmsClassInstructors", "LmsClassEnrollments" }
				.Select(t => $"DELETE FROM {Tbl(t)} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsClassSessionId")} = {P}Id AND {guard}").ToList();
			var session = $"DELETE FROM {Tbl("LmsClassSessions")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsClassSessionId")} = {P}Id AND {expired}";

			return RunAsync(async connection =>
			{
				var owned = UnitOfWork?.Transaction == null;
				var transaction = UnitOfWork?.Transaction ?? await connection.BeginTransactionAsync(cancellationToken);
				try
				{
					foreach (var statement in children)
						await connection.ExecuteAsync(new Dapper.CommandDefinition(statement, parameters, transaction, cancellationToken: cancellationToken));
					var removed = await connection.ExecuteAsync(new Dapper.CommandDefinition(session, parameters, transaction, cancellationToken: cancellationToken));
					if (owned)
						await transaction.CommitAsync(cancellationToken);
					return removed == 1;
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

	public class LmsClassEnrollmentsRepository : RmsRepositoryBase<LmsClassEnrollment>, ILmsClassEnrollmentsRepository
	{
		public LmsClassEnrollmentsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<LmsClassEnrollment> GetByIdForDepartmentAsync(int departmentId, int lmsClassEnrollmentId) =>
			QueryFirstOrDefaultAsync<LmsClassEnrollment>(
				$"SELECT * FROM {Tbl("LmsClassEnrollments")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsClassEnrollmentId")} = {P}Id",
				new { DepartmentId = departmentId, Id = lmsClassEnrollmentId });

		public async Task<List<LmsClassEnrollment>> GetForSessionAsync(int departmentId, int lmsClassSessionId) =>
			(await QueryAsync<LmsClassEnrollment>(
				$"SELECT * FROM {Tbl("LmsClassEnrollments")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsClassSessionId")} = {P}SessionId " +
				$"ORDER BY {Col("EnrolledOn")}, {Col("LmsClassEnrollmentId")}",
				new { DepartmentId = departmentId, SessionId = lmsClassSessionId })).ToList();

		public async Task<List<LmsClassEnrollment>> GetForSessionsAsync(int departmentId, IEnumerable<int> lmsClassSessionIds)
		{
			var ids = InListValue((lmsClassSessionIds ?? Enumerable.Empty<int>()).Where(i => i > 0).Distinct());
			var result = new List<LmsClassEnrollment>();
			foreach (var chunk in ids.Chunk(1000))
				result.AddRange(await QueryAsync<LmsClassEnrollment>(
					$"SELECT * FROM {Tbl("LmsClassEnrollments")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {InList("LmsClassSessionId", "Ids")} " +
					$"ORDER BY {Col("LmsClassSessionId")}, {Col("EnrolledOn")}, {Col("LmsClassEnrollmentId")}",
					new { DepartmentId = departmentId, Ids = chunk }));
			return result;
		}

		public Task<LmsClassEnrollment> GetForMemberAsync(int departmentId, int lmsClassSessionId, string userId) =>
			QueryFirstOrDefaultAsync<LmsClassEnrollment>(
				$"SELECT * FROM {Tbl("LmsClassEnrollments")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsClassSessionId")} = {P}SessionId " +
				$"AND {Col("UserId")} = {CaseInsensitive("UserId")}",
				new { DepartmentId = departmentId, SessionId = lmsClassSessionId, UserId = userId });

		public async Task<List<LmsClassEnrollment>> GetForUserAsync(int departmentId, string userId) =>
			(await QueryAsync<LmsClassEnrollment>(
				$"SELECT * FROM {Tbl("LmsClassEnrollments")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("UserId")} = {CaseInsensitive("UserId")} " +
				$"ORDER BY {Col("EnrolledOn")} DESC, {Col("LmsClassEnrollmentId")} DESC",
				new { DepartmentId = departmentId, UserId = userId })).ToList();

		public async Task<bool> ClaimReminderAsync(int departmentId, int lmsClassEnrollmentId, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var parameters = new DynamicParameters();
			parameters.Add("DepartmentId", departmentId);
			parameters.Add("Id", lmsClassEnrollmentId);
			parameters.Add("Now", DatabaseTimestamp(utcNow), System.Data.DbType.DateTime2);
			return await ExecuteAsync(
				$"UPDATE {Tbl("LmsClassEnrollments")} SET {Col("ReminderSentOn")} = {P}Now WHERE {Col("DepartmentId")} = {P}DepartmentId " +
				$"AND {Col("LmsClassEnrollmentId")} = {P}Id AND {Col("ReminderSentOn")} IS NULL", parameters, cancellationToken) == 1;
		}

		public async Task<bool> PromoteAsync(int departmentId, int lmsClassEnrollmentId, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var parameters = new DynamicParameters();
			parameters.Add("DepartmentId", departmentId);
			parameters.Add("Id", lmsClassEnrollmentId);
			parameters.Add("Now", DatabaseTimestamp(utcNow), System.Data.DbType.DateTime2);
			parameters.Add("Enrolled", (int)LmsClassEnrollmentStatuses.Enrolled);
			parameters.Add("Waitlisted", (int)LmsClassEnrollmentStatuses.Waitlisted);
			return await ExecuteAsync(
				$"UPDATE {Tbl("LmsClassEnrollments")} SET {Col("Status")} = {P}Enrolled, {Col("WaitlistPosition")} = NULL, {Col("PromotedOn")} = {P}Now, " +
				$"{Col("UpdatedOn")} = {P}Now WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsClassEnrollmentId")} = {P}Id AND {Col("Status")} = {P}Waitlisted",
				parameters, cancellationToken) == 1;
		}

		public Task<int> ResetRemindersAsync(int departmentId, int lmsClassSessionId, CancellationToken cancellationToken = default) =>
			ExecuteAsync($"UPDATE {Tbl("LmsClassEnrollments")} SET {Col("ReminderSentOn")} = NULL WHERE {Col("DepartmentId")} = {P}DepartmentId " +
				$"AND {Col("LmsClassSessionId")} = {P}SessionId AND {Col("ReminderSentOn")} IS NOT NULL",
				new { DepartmentId = departmentId, SessionId = lmsClassSessionId }, cancellationToken);
	}

	public class LmsClassInstructorsRepository : RmsRepositoryBase<LmsClassInstructor>, ILmsClassInstructorsRepository
	{
		public LmsClassInstructorsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<LmsClassInstructor> GetByIdForDepartmentAsync(int departmentId, int lmsClassInstructorId) =>
			QueryFirstOrDefaultAsync<LmsClassInstructor>(
				$"SELECT * FROM {Tbl("LmsClassInstructors")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsClassInstructorId")} = {P}Id",
				new { DepartmentId = departmentId, Id = lmsClassInstructorId });

		public async Task<List<LmsClassInstructor>> GetForSessionAsync(int departmentId, int lmsClassSessionId) =>
			(await QueryAsync<LmsClassInstructor>(
				$"SELECT * FROM {Tbl("LmsClassInstructors")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsClassSessionId")} = {P}SessionId " +
				$"ORDER BY {Col("SortOrder")}, {Col("LmsClassInstructorId")}",
				new { DepartmentId = departmentId, SessionId = lmsClassSessionId })).ToList();

		public async Task<List<LmsClassInstructor>> GetForSessionsAsync(int departmentId, IEnumerable<int> lmsClassSessionIds)
		{
			var ids = InListValue((lmsClassSessionIds ?? Enumerable.Empty<int>()).Where(i => i > 0).Distinct());
			var result = new List<LmsClassInstructor>();
			foreach (var chunk in ids.Chunk(1000))
				result.AddRange(await QueryAsync<LmsClassInstructor>(
					$"SELECT * FROM {Tbl("LmsClassInstructors")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {InList("LmsClassSessionId", "Ids")} " +
					$"ORDER BY {Col("LmsClassSessionId")}, {Col("SortOrder")}, {Col("LmsClassInstructorId")}",
					new { DepartmentId = departmentId, Ids = chunk }));
			return result;
		}

		public async Task<List<LmsClassInstructor>> GetForUserAsync(int departmentId, string userId) =>
			(await QueryAsync<LmsClassInstructor>(
				$"SELECT * FROM {Tbl("LmsClassInstructors")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("UserId")} = {CaseInsensitive("UserId")} " +
				$"ORDER BY {Col("LmsClassSessionId")} DESC, {Col("LmsClassInstructorId")}",
				new { DepartmentId = departmentId, UserId = userId })).ToList();
	}

	public class LmsClassCostLinesRepository : RmsRepositoryBase<LmsClassCostLine>, ILmsClassCostLinesRepository
	{
		public LmsClassCostLinesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<LmsClassCostLine> GetByIdForDepartmentAsync(int departmentId, int lmsClassCostLineId) =>
			QueryFirstOrDefaultAsync<LmsClassCostLine>(
				$"SELECT * FROM {Tbl("LmsClassCostLines")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsClassCostLineId")} = {P}Id",
				new { DepartmentId = departmentId, Id = lmsClassCostLineId });

		public async Task<List<LmsClassCostLine>> GetForSessionsAsync(int departmentId, IEnumerable<int> lmsClassSessionIds)
		{
			var ids = InListValue((lmsClassSessionIds ?? Enumerable.Empty<int>()).Where(i => i > 0).Distinct());
			var result = new List<LmsClassCostLine>();
			foreach (var chunk in ids.Chunk(1000))
				result.AddRange(await QueryAsync<LmsClassCostLine>(
					$"SELECT * FROM {Tbl("LmsClassCostLines")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {InList("LmsClassSessionId", "Ids")} " +
					$"ORDER BY {Col("LmsClassSessionId")}, {Col("LmsClassCostLineId")}",
					new { DepartmentId = departmentId, Ids = chunk }));
			return result;
		}
	}

	public class LmsTrainingResourcesRepository : RmsRepositoryBase<LmsTrainingResource>, ILmsTrainingResourcesRepository
	{
		public LmsTrainingResourcesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<LmsTrainingResource> GetByIdForDepartmentAsync(int departmentId, int lmsTrainingResourceId) =>
			QueryFirstOrDefaultAsync<LmsTrainingResource>(
				$"SELECT * FROM {Tbl("LmsTrainingResources")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsTrainingResourceId")} = {P}Id",
				new { DepartmentId = departmentId, Id = lmsTrainingResourceId });

		public async Task<List<LmsTrainingResource>> GetForDepartmentAsync(int departmentId, bool includeDeleted = false) =>
			(await QueryAsync<LmsTrainingResource>(
				$"SELECT * FROM {Tbl("LmsTrainingResources")} WHERE {Col("DepartmentId")} = {P}DepartmentId" +
				(includeDeleted ? string.Empty : $" AND {Col("IsDeleted")} = {P}No") + $" ORDER BY {Col("Name")}, {Col("LmsTrainingResourceId")}",
				new { DepartmentId = departmentId, No = false })).ToList();
	}

	public class LmsClassResourceBookingsRepository : RmsRepositoryBase<LmsClassResourceBooking>, ILmsClassResourceBookingsRepository
	{
		public LmsClassResourceBookingsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<LmsClassResourceBooking> GetByIdForDepartmentAsync(int departmentId, int lmsClassResourceBookingId) =>
			QueryFirstOrDefaultAsync<LmsClassResourceBooking>(
				$"SELECT * FROM {Tbl("LmsClassResourceBookings")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsClassResourceBookingId")} = {P}Id",
				new { DepartmentId = departmentId, Id = lmsClassResourceBookingId });

		public async Task<List<LmsClassResourceBooking>> GetForSessionAsync(int departmentId, int lmsClassSessionId) =>
			(await QueryAsync<LmsClassResourceBooking>(
				$"SELECT * FROM {Tbl("LmsClassResourceBookings")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsClassSessionId")} = {P}SessionId " +
				$"ORDER BY {Col("LmsClassResourceBookingId")}",
				new { DepartmentId = departmentId, SessionId = lmsClassSessionId })).ToList();

		public async Task<List<LmsClassResourceBooking>> GetForResourceAsync(int departmentId, int lmsTrainingResourceId, DateTime fromUtc, DateTime toUtc)
		{
			var parameters = new DynamicParameters();
			parameters.Add("DepartmentId", departmentId);
			parameters.Add("ResourceId", lmsTrainingResourceId);
			parameters.Add("FromUtc", DatabaseTimestamp(fromUtc), System.Data.DbType.DateTime2);
			parameters.Add("ToUtc", DatabaseTimestamp(toUtc), System.Data.DbType.DateTime2);
			return (await QueryAsync<LmsClassResourceBooking>(
				$"SELECT * FROM {Tbl("LmsClassResourceBookings")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsTrainingResourceId")} = {P}ResourceId " +
				$"AND {Col("StartsOn")} < {P}ToUtc AND {Col("EndsOn")} > {P}FromUtc ORDER BY {Col("StartsOn")}, {Col("LmsClassResourceBookingId")}", parameters)).ToList();
		}

		public async Task<bool> HasOverlapAsync(int departmentId, int lmsTrainingResourceId, DateTime startUtc, DateTime endUtc, int exceptSessionId)
		{
			var parameters = new DynamicParameters();
			parameters.Add("DepartmentId", departmentId);
			parameters.Add("ResourceId", lmsTrainingResourceId);
			parameters.Add("StartUtc", DatabaseTimestamp(startUtc), System.Data.DbType.DateTime2);
			parameters.Add("EndUtc", DatabaseTimestamp(endUtc), System.Data.DbType.DateTime2);
			parameters.Add("Except", exceptSessionId);
			return await ScalarAsync<int>(
				$"SELECT COUNT(*) FROM {Tbl("LmsClassResourceBookings")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsTrainingResourceId")} = {P}ResourceId " +
				$"AND {Col("LmsClassSessionId")} <> {P}Except AND {Col("StartsOn")} < {P}EndUtc AND {Col("EndsOn")} > {P}StartUtc", parameters) > 0;
		}

		public Task<int> DeleteForSessionAsync(int departmentId, int lmsClassSessionId, CancellationToken cancellationToken = default) =>
			ExecuteAsync($"DELETE FROM {Tbl("LmsClassResourceBookings")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsClassSessionId")} = {P}SessionId",
				new { DepartmentId = departmentId, SessionId = lmsClassSessionId }, cancellationToken);
	}

	public class LmsLessonPlansRepository : RmsRepositoryBase<LmsLessonPlan>, ILmsLessonPlansRepository
	{
		public LmsLessonPlansRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<LmsLessonPlan> GetByIdForDepartmentAsync(int departmentId, int lmsLessonPlanId) =>
			QueryFirstOrDefaultAsync<LmsLessonPlan>(
				$"SELECT * FROM {Tbl("LmsLessonPlans")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsLessonPlanId")} = {P}Id",
				new { DepartmentId = departmentId, Id = lmsLessonPlanId });

		public async Task<List<LmsLessonPlan>> GetForDepartmentAsync(int departmentId, bool includeDeleted = false) =>
			(await QueryAsync<LmsLessonPlan>(
				$"SELECT * FROM {Tbl("LmsLessonPlans")} WHERE {Col("DepartmentId")} = {P}DepartmentId" +
				(includeDeleted ? string.Empty : $" AND {Col("IsDeleted")} = {P}No") + $" ORDER BY {Col("Title")}, {Col("LmsLessonPlanId")}",
				new { DepartmentId = departmentId, No = false })).ToList();
	}

	public class LmsLessonPlanVersionsRepository : RmsRepositoryBase<LmsLessonPlanVersion>, ILmsLessonPlanVersionsRepository
	{
		public LmsLessonPlanVersionsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<LmsLessonPlanVersion> GetByIdForDepartmentAsync(int departmentId, int lmsLessonPlanVersionId) =>
			QueryFirstOrDefaultAsync<LmsLessonPlanVersion>(
				$"SELECT * FROM {Tbl("LmsLessonPlanVersions")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsLessonPlanVersionId")} = {P}Id",
				new { DepartmentId = departmentId, Id = lmsLessonPlanVersionId });

		public async Task<List<LmsLessonPlanVersion>> GetForPlanAsync(int departmentId, int lmsLessonPlanId) =>
			(await QueryAsync<LmsLessonPlanVersion>(
				$"SELECT * FROM {Tbl("LmsLessonPlanVersions")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsLessonPlanId")} = {P}PlanId " +
				$"ORDER BY {Col("LmsLessonPlanVersionId")} DESC",
				new { DepartmentId = departmentId, PlanId = lmsLessonPlanId })).ToList();

		public async Task<bool> MoveAsync(int departmentId, int lmsLessonPlanVersionId, int fromStatus, int toStatus, CancellationToken cancellationToken = default) =>
			await ExecuteAsync(
				$"UPDATE {Tbl("LmsLessonPlanVersions")} SET {Col("Status")} = {P}ToStatus WHERE {Col("DepartmentId")} = {P}DepartmentId " +
				$"AND {Col("LmsLessonPlanVersionId")} = {P}Id AND {Col("Status")} = {P}FromStatus",
				new { DepartmentId = departmentId, Id = lmsLessonPlanVersionId, FromStatus = fromStatus, ToStatus = toStatus }, cancellationToken) == 1;
	}
}
