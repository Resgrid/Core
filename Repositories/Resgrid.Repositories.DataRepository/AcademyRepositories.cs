using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Academy;
using Resgrid.Model.Lms;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository
{
	/// <summary>A3 guidance remembered per member (AcademyGuidanceStates, M0375): portable SQL, unique per department, member and key.</summary>
	public class AcademyGuidanceStatesRepository : RmsRepositoryBase<AcademyGuidanceState>, IAcademyGuidanceStatesRepository
	{
		public AcademyGuidanceStatesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		private static AcademyGuidanceState AsUtc(AcademyGuidanceState state)
		{
			if (state != null)
				state.UpdatedOn = DateTime.SpecifyKind(state.UpdatedOn, DateTimeKind.Utc);
			return state;
		}

		public async Task<List<AcademyGuidanceState>> GetForUserAsync(int departmentId, string userId, CancellationToken cancellationToken = default)
		{
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(userId))
				return new List<AcademyGuidanceState>();
			return (await QueryAsync<AcademyGuidanceState>(
				$"SELECT {Cols("AcademyGuidanceStateId", "DepartmentId", "UserId", "GuidanceKey", "State", "Revision", "TasksDone", "UpdatedOn")} FROM {Tbl("AcademyGuidanceStates")} " +
				$"WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("UserId")} = {CaseInsensitive("UserId")}",
				new { DepartmentId = departmentId, UserId = userId }, cancellationToken)).Select(AsUtc).ToList();
		}

		public async Task<AcademyGuidanceState> UpsertAsync(AcademyGuidanceState state, CancellationToken cancellationToken = default)
		{
			if (state == null || state.DepartmentId <= 0 || string.IsNullOrWhiteSpace(state.UserId) || string.IsNullOrWhiteSpace(state.GuidanceKey) ||
				state.GuidanceKey.Length > AcademyGuidanceState.GuidanceKeyMaxLength || (state.TasksDone?.Length ?? 0) > AcademyGuidanceState.TasksDoneMaxLength)
				throw new ArgumentException("A member's guidance state needs a department, a member and a key.", nameof(state));

			var parameters = new
			{
				state.DepartmentId,
				state.UserId,
				state.GuidanceKey,
				state.State,
				state.Revision,
				state.TasksDone,
				UpdatedOn = DatabaseTimestamp(DateTime.SpecifyKind(state.UpdatedOn == default ? DateTime.UtcNow : state.UpdatedOn, DateTimeKind.Utc))
			};
			var update = $"UPDATE {Tbl("AcademyGuidanceStates")} SET {Col("State")} = {P}State, {Col("Revision")} = {P}Revision, {Col("TasksDone")} = {P}TasksDone, " +
				$"{Col("UpdatedOn")} = {P}UpdatedOn WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("UserId")} = {CaseInsensitive("UserId")} AND {Col("GuidanceKey")} = {P}GuidanceKey";

			if (await ExecuteAsync(update, parameters, cancellationToken) == 0)
			{
				var columns = Cols("DepartmentId", "UserId", "GuidanceKey", "State", "Revision", "TasksDone", "UpdatedOn");
				var values = $"{P}DepartmentId, {P}UserId, {P}GuidanceKey, {P}State, {P}Revision, {P}TasksDone, {P}UpdatedOn";
				var insert = IsPostgres
					? $"INSERT INTO {Tbl("AcademyGuidanceStates")} ({columns}) VALUES ({values}) RETURNING {Col("AcademyGuidanceStateId")}"
					: $"INSERT INTO {Tbl("AcademyGuidanceStates")} ({columns}) OUTPUT INSERTED.{Col("AcademyGuidanceStateId")} VALUES ({values})";
				try
				{
					state.AcademyGuidanceStateId = await ScalarAsync<int>(insert, parameters, cancellationToken);
				}
				catch (Exception ex) when (IsUniqueViolation(ex))
				{
					// Another request inserted the same key first: this one's state wins as an update.
					await ExecuteAsync(update, parameters, cancellationToken);
				}
			}

			return state;
		}
	}

	/// <summary>Major Academy lesson revisions waiting for worker 88 (AcademyLessonUpdates, M0375); system-level, unique per course and revision.</summary>
	public class AcademyLessonUpdatesRepository : RmsRepositoryBase<AcademyLessonUpdate>, IAcademyLessonUpdatesRepository
	{
		public AcademyLessonUpdatesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		private static AcademyLessonUpdate AsUtc(AcademyLessonUpdate update)
		{
			if (update != null)
			{
				update.CreatedOn = DateTime.SpecifyKind(update.CreatedOn, DateTimeKind.Utc);
				update.CompletedOn = update.CompletedOn.HasValue ? DateTime.SpecifyKind(update.CompletedOn.Value, DateTimeKind.Utc) : (DateTime?)null;
			}
			return update;
		}

		public async Task<bool> InsertIfNewAsync(AcademyLessonUpdate update, CancellationToken cancellationToken = default)
		{
			if (update == null || string.IsNullOrWhiteSpace(update.SystemKey) || update.LmsCourseId <= 0 || update.Revision < 1)
				throw new ArgumentException("An Academy lesson update needs a course and a revision.", nameof(update));

			var exists = await ScalarAsync<int>($"SELECT COUNT(*) FROM {Tbl("AcademyLessonUpdates")} WHERE {Col("LmsCourseId")} = {P}LmsCourseId AND {Col("Revision")} = {P}Revision",
				new { update.LmsCourseId, update.Revision }, cancellationToken);
			if (exists > 0)
				return false;

			var columns = Cols("SystemKey", "LmsCourseId", "LmsCourseVersionId", "Revision", "Policy", "ReassignDueDays", "CreatedOn");
			var values = $"{P}SystemKey, {P}LmsCourseId, {P}LmsCourseVersionId, {P}Revision, {P}Policy, {P}ReassignDueDays, {P}CreatedOn";
			var sql = IsPostgres
				? $"INSERT INTO {Tbl("AcademyLessonUpdates")} ({columns}) VALUES ({values}) RETURNING {Col("AcademyLessonUpdateId")}"
				: $"INSERT INTO {Tbl("AcademyLessonUpdates")} ({columns}) OUTPUT INSERTED.{Col("AcademyLessonUpdateId")} VALUES ({values})";
			try
			{
				update.AcademyLessonUpdateId = await ScalarAsync<int>(sql, new
				{
					update.SystemKey,
					update.LmsCourseId,
					update.LmsCourseVersionId,
					update.Revision,
					update.Policy,
					update.ReassignDueDays,
					CreatedOn = DatabaseTimestamp(DateTime.SpecifyKind(update.CreatedOn == default ? DateTime.UtcNow : update.CreatedOn, DateTimeKind.Utc))
				}, cancellationToken);
				return true;
			}
			catch (Exception ex) when (IsUniqueViolation(ex))
			{
				return false;
			}
		}

		public async Task<List<AcademyLessonUpdate>> GetPendingAsync(int take, CancellationToken cancellationToken = default)
		{
			return (await QueryAsync<AcademyLessonUpdate>(
				$"SELECT * FROM {Tbl("AcademyLessonUpdates")} WHERE {Col("CompletedOn")} IS NULL ORDER BY {Col("AcademyLessonUpdateId")} {Paging()}",
				new { Skip = 0, Take = Math.Clamp(take, 1, 100) }, cancellationToken)).Select(AsUtc).ToList();
		}

		public async Task<bool> SaveProgressAsync(AcademyLessonUpdate update, CancellationToken cancellationToken = default)
		{
			if (update == null || update.AcademyLessonUpdateId <= 0)
				return false;
			return await ExecuteAsync(
				$"UPDATE {Tbl("AcademyLessonUpdates")} SET {Col("AppliedThroughDepartmentId")} = {P}AppliedThroughDepartmentId, {Col("CompletedOn")} = {P}CompletedOn " +
				$"WHERE {Col("AcademyLessonUpdateId")} = {P}Id",
				new
				{
					update.AppliedThroughDepartmentId,
					CompletedOn = update.CompletedOn.HasValue ? DatabaseTimestamp(DateTime.SpecifyKind(update.CompletedOn.Value, DateTimeKind.Utc)) : (DateTime?)null,
					Id = update.AcademyLessonUpdateId
				}, cancellationToken) == 1;
		}

		/// <summary>
		/// A live requirement that covers the lesson: a Course requirement on it, or a Path requirement whose path (department or system) has a
		/// course step for it. <c>r</c> is the LmsRequirements alias.
		/// </summary>
		private string RequiresLesson =>
			$"r.{Col("IsActive")} = {(IsPostgres ? "true" : "1")} AND r.{Col("DeletedOn")} IS NULL AND (" +
			$"(r.{Col("Kind")} = {(int)LmsRequirementKinds.Course} AND r.{Col("TargetRefId")} = {P}LmsCourseId) OR " +
			$"(r.{Col("Kind")} = {(int)LmsRequirementKinds.Path} AND EXISTS (SELECT 1 FROM {Tbl("LmsPathSteps")} s WHERE s.{Col("LmsPathId")} = r.{Col("TargetRefId")} " +
			$"AND s.{Col("Kind")} = {(int)LmsPathStepKinds.Course} AND s.{Col("TargetRefId")} = {P}LmsCourseId)))";

		public async Task<List<int>> GetAssigningDepartmentIdsAsync(int lmsCourseId, int afterDepartmentId, int take, CancellationToken cancellationToken = default)
		{
			// Round 5F: the departments that require the lesson (A2 release rule), read from T3's requirements, not from who once assigned it.
			return (await QueryAsync<int>(
				$"SELECT DISTINCT r.{Col("DepartmentId")} FROM {Tbl("LmsRequirements")} r WHERE r.{Col("DepartmentId")} > {P}After AND r.{Col("DepartmentId")} > 0 " +
				$"AND {RequiresLesson} ORDER BY r.{Col("DepartmentId")} {Paging()}",
				new { LmsCourseId = lmsCourseId, After = Math.Max(0, afterDepartmentId), Skip = 0, Take = Math.Clamp(take, 1, 500) },
				cancellationToken)).ToList();
		}

		public async Task<List<AcademyLessonRequirement>> GetRequirementsOnLessonAsync(int departmentId, int lmsCourseId, CancellationToken cancellationToken = default)
		{
			if (departmentId <= 0)
				return new List<AcademyLessonRequirement>();
			return (await QueryAsync<AcademyLessonRequirement>(
				$"SELECT r.{Col("LmsRequirementId")}, r.{Col("DepartmentId")}, r.{Col("AcademyUpdatePolicy")}, r.{Col("DueDays")} FROM {Tbl("LmsRequirements")} r " +
				$"WHERE r.{Col("DepartmentId")} = {P}DepartmentId AND {RequiresLesson} ORDER BY r.{Col("LmsRequirementId")}",
				new { DepartmentId = departmentId, LmsCourseId = lmsCourseId }, cancellationToken)).ToList();
		}
	}
}
