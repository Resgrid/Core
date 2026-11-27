using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Lms;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository
{
	// Training T3 (M0364-M0366): learning requirements, their members, training-backed enforcement, worker 93's clock and learning paths.
	// Portable SQL through the RMS helpers; list parameters use InList (IN on SQL Server, = ANY on PostgreSQL).

	public class LmsRequirementsRepository : LmsRepositoryBase<LmsRequirement>, ILmsRequirementsRepository
	{
		public LmsRequirementsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		private static string True => IsPostgres ? "true" : "1";

		public async Task<List<LmsRequirement>> GetForDepartmentAsync(int departmentId, bool includeDeleted = false)
		{
			return (await QueryAsync<LmsRequirement>(
				$"SELECT * FROM {Tbl("LmsRequirements")} WHERE {Col("DepartmentId")} = {P}DepartmentId" + (includeDeleted ? string.Empty : $" AND {Col("DeletedOn")} IS NULL") +
				$" ORDER BY {Col("Name")}", new { DepartmentId = departmentId })).ToList();
		}

		public Task<LmsRequirement> GetByIdForDepartmentAsync(int departmentId, int lmsRequirementId)
		{
			return QueryFirstOrDefaultAsync<LmsRequirement>(
				$"SELECT * FROM {Tbl("LmsRequirements")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsRequirementId")} = {P}Id",
				new { DepartmentId = departmentId, Id = lmsRequirementId });
		}

		public async Task<bool> HasLiveAsync(int departmentId)
		{
			return await ScalarAsync<int>(
				$"SELECT COUNT(1) FROM {Tbl("LmsRequirements")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("IsActive")} = {True} AND {Col("DeletedOn")} IS NULL",
				new { DepartmentId = departmentId }) > 0;
		}

		public async Task<List<int>> GetDepartmentIdsWithDueWorkAsync()
		{
			return (await QueryAsync<int>(
				$"SELECT {Col("DepartmentId")} FROM {Tbl("LmsRequirements")} WHERE {Col("IsActive")} = {True} AND {Col("DeletedOn")} IS NULL AND {Col("DepartmentId")} > 0 " +
				$"UNION SELECT {Col("DepartmentId")} FROM {Tbl("LmsRequirementEnforcements")} WHERE {Col("UndoneOn")} IS NULL " +
				$"UNION SELECT a.{Col("DepartmentId")} FROM {Tbl("LmsPathAssignments")} a JOIN {Tbl("LmsPaths")} p ON p.{Col("LmsPathId")} = a.{Col("LmsPathId")} " +
				$"WHERE p.{Col("DepartmentId")} > 0 AND a.{Col("CompletedOn")} IS NULL AND a.{Col("RemovedOn")} IS NULL", new { })).Distinct().OrderBy(d => d).ToList();
		}
	}

	public class LmsRequirementAudiencesRepository : LmsRepositoryBase<LmsRequirementAudience>, ILmsRequirementAudiencesRepository
	{
		public LmsRequirementAudiencesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<List<LmsRequirementAudience>> GetForDepartmentAsync(int departmentId)
		{
			return (await QueryAsync<LmsRequirementAudience>(
				$"SELECT * FROM {Tbl("LmsRequirementAudiences")} WHERE {Col("DepartmentId")} = {P}DepartmentId", new { DepartmentId = departmentId })).ToList();
		}

		public async Task ReplaceAsync(int departmentId, int lmsRequirementId, IEnumerable<IContentAudienceRule> rules, CancellationToken cancellationToken = default)
		{
			var owns = UnitOfWork.Transaction == null;
			UnitOfWork.CreateOrGetConnection();
			try
			{
				await ExecuteAsync(
					$"DELETE FROM {Tbl("LmsRequirementAudiences")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsRequirementId")} = {P}Id",
					new { DepartmentId = departmentId, Id = lmsRequirementId }, cancellationToken);
				foreach (var rule in ContentAudienceMatcher.Normalize(rules))
					await base.InsertAsync(new LmsRequirementAudience { DepartmentId = departmentId, LmsRequirementId = lmsRequirementId, Kind = rule.Kind, RefId = rule.RefId },
						cancellationToken, true);
				if (owns) UnitOfWork.CommitChanges();
			}
			catch
			{
				if (owns) UnitOfWork.DiscardChanges();
				throw;
			}
		}
	}

	public class LmsRequirementMembersRepository : LmsRepositoryBase<LmsRequirementMember>, ILmsRequirementMembersRepository
	{
		public LmsRequirementMembersRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<List<LmsRequirementMember>> GetOpenForDepartmentAsync(int departmentId)
		{
			return (await QueryAsync<LmsRequirementMember>(
				$"SELECT * FROM {Tbl("LmsRequirementMembers")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LeftOn")} IS NULL", new { DepartmentId = departmentId })).ToList();
		}

		public async Task<List<LmsRequirementMember>> GetOpenForUserAsync(int departmentId, string userId)
		{
			return (await QueryAsync<LmsRequirementMember>(
				$"SELECT * FROM {Tbl("LmsRequirementMembers")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("UserId")} = {CaseInsensitive("UserId")} AND {Col("LeftOn")} IS NULL",
				new { DepartmentId = departmentId, UserId = userId })).ToList();
		}

		public Task<LmsRequirementMember> GetByIdForDepartmentAsync(int departmentId, int lmsRequirementMemberId)
		{
			return QueryFirstOrDefaultAsync<LmsRequirementMember>(
				$"SELECT * FROM {Tbl("LmsRequirementMembers")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsRequirementMemberId")} = {P}Id",
				new { DepartmentId = departmentId, Id = lmsRequirementMemberId });
		}

		private Task<LmsRequirementMember> GetOpenAsync(int departmentId, int lmsRequirementId, string userId)
		{
			return QueryFirstOrDefaultAsync<LmsRequirementMember>(
				$"SELECT * FROM {Tbl("LmsRequirementMembers")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsRequirementId")} = {P}RequirementId " +
				$"AND {Col("UserId")} = {CaseInsensitive("UserId")} AND {Col("LeftOn")} IS NULL",
				new { DepartmentId = departmentId, RequirementId = lmsRequirementId, UserId = userId });
		}

		public async Task<(LmsRequirementMember Member, bool Created)> InsertOpenAsync(LmsRequirementMember member, CancellationToken cancellationToken = default)
		{
			var existing = await GetOpenAsync(member.DepartmentId, member.LmsRequirementId, member.UserId);
			if (existing != null)
				return (existing, false);

			member.LeftOn = null;
			if (await TryInsertUniqueAsync(member, cancellationToken))
				return (member, true);

			return (await GetOpenAsync(member.DepartmentId, member.LmsRequirementId, member.UserId), false);
		}

		public Task<int> MarkLeftAsync(int departmentId, IEnumerable<int> lmsRequirementMemberIds, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var ids = InListValue(lmsRequirementMemberIds);
			if (ids.Length == 0)
				return Task.FromResult(0);
			return ExecuteAsync(
				$"UPDATE {Tbl("LmsRequirementMembers")} SET {Col("LeftOn")} = {P}Now WHERE {Col("DepartmentId")} = {P}DepartmentId AND {InList("LmsRequirementMemberId", "Ids")} " +
				$"AND {Col("LeftOn")} IS NULL", new { Now = DatabaseTimestamp(utcNow), DepartmentId = departmentId, Ids = ids }, cancellationToken);
		}

		public Task<int> SaveStateAsync(LmsRequirementMember member, CancellationToken cancellationToken = default)
		{
			return ExecuteAsync(
				$"UPDATE {Tbl("LmsRequirementMembers")} SET {Col("State")} = {P}State, {Col("StateChangedOn")} = {P}StateChangedOn, {Col("DueOn")} = {P}DueOn, " +
				$"{Col("LastCompletedOn")} = {P}LastCompletedOn WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsRequirementMemberId")} = {P}Id",
				new
				{
					member.State,
					StateChangedOn = Stamp(member.StateChangedOn),
					DueOn = Stamp(member.DueOn),
					LastCompletedOn = Stamp(member.LastCompletedOn),
					member.DepartmentId,
					Id = member.LmsRequirementMemberId
				}, cancellationToken);
		}

		public Task<int> SaveExemptionAsync(LmsRequirementMember member, CancellationToken cancellationToken = default)
		{
			return ExecuteAsync(
				$"UPDATE {Tbl("LmsRequirementMembers")} SET {Col("ExemptOn")} = {P}ExemptOn, {Col("ExemptUntil")} = {P}ExemptUntil, {Col("ExemptReason")} = {P}ExemptReason, " +
				$"{Col("ExemptByUserId")} = {P}ExemptByUserId, {Col("ExtendedDueOn")} = {P}ExtendedDueOn, {Col("ExtendedByUserId")} = {P}ExtendedByUserId, " +
				$"{Col("ExtensionReason")} = {P}ExtensionReason WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsRequirementMemberId")} = {P}Id",
				new
				{
					ExemptOn = Stamp(member.ExemptOn),
					ExemptUntil = Stamp(member.ExemptUntil),
					member.ExemptReason,
					member.ExemptByUserId,
					ExtendedDueOn = Stamp(member.ExtendedDueOn),
					member.ExtendedByUserId,
					member.ExtensionReason,
					member.DepartmentId,
					Id = member.LmsRequirementMemberId
				}, cancellationToken);
		}

		public async Task<bool> TryClaimNoticeAsync(int departmentId, int lmsRequirementMemberId, bool overdue, DateTime dueOnUtc, CancellationToken cancellationToken = default)
		{
			var column = overdue ? "OverdueNotifiedFor" : "AtRiskNotifiedFor";
			return await ExecuteAsync(
				$"UPDATE {Tbl("LmsRequirementMembers")} SET {Col(column)} = {P}Due WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsRequirementMemberId")} = {P}Id " +
				$"AND ({Col(column)} IS NULL OR {Col(column)} <> {P}Due)",
				new { Due = DatabaseTimestamp(ClaimKey(dueOnUtc)), DepartmentId = departmentId, Id = lmsRequirementMemberId }, cancellationToken) == 1;
		}

		/// <summary>
		/// A due moment as a claim key: whole seconds, so the value written and the value compared are the same on both engines (SQL Server
		/// binds a DateTime as datetime, PostgreSQL keeps microseconds; a due moment shifted by leave or a lapse can carry sub-second ticks).
		/// </summary>
		public static DateTime ClaimKey(DateTime utc) => new DateTime(utc.Ticks - utc.Ticks % TimeSpan.TicksPerSecond, utc.Kind);

		public async Task<bool> TryClaimAssignmentAsync(int departmentId, int lmsRequirementMemberId, DateTime dueOnUtc, CancellationToken cancellationToken = default)
		{
			return await ExecuteAsync(
				$"UPDATE {Tbl("LmsRequirementMembers")} SET {Col("AssignedForDueOn")} = {P}Due WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsRequirementMemberId")} = {P}Id " +
				$"AND ({Col("AssignedForDueOn")} IS NULL OR {Col("AssignedForDueOn")} <> {P}Due)",
				new { Due = DatabaseTimestamp(ClaimKey(dueOnUtc)), DepartmentId = departmentId, Id = lmsRequirementMemberId }, cancellationToken) == 1;
		}
	}

	public class LmsRequirementEnforcementsRepository : LmsRepositoryBase<LmsRequirementEnforcement>, ILmsRequirementEnforcementsRepository
	{
		public LmsRequirementEnforcementsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<List<LmsRequirementEnforcement>> GetOpenForDepartmentAsync(int departmentId)
		{
			return (await QueryAsync<LmsRequirementEnforcement>(
				$"SELECT * FROM {Tbl("LmsRequirementEnforcements")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("UndoneOn")} IS NULL", new { DepartmentId = departmentId })).ToList();
		}

		public async Task<List<LmsRequirementEnforcement>> GetRecentForDepartmentAsync(int departmentId, int take)
		{
			return (await QueryAsync<LmsRequirementEnforcement>(
				$"SELECT * FROM {Tbl("LmsRequirementEnforcements")} WHERE {Col("DepartmentId")} = {P}DepartmentId ORDER BY {Col("ActedOn")} DESC {Take()}",
				new { DepartmentId = departmentId, Take = Math.Clamp(take, 1, 1000) })).ToList();
		}

		public async Task<List<int>> GetDepartmentIdsWithOpenAsync()
		{
			return (await QueryAsync<int>($"SELECT DISTINCT {Col("DepartmentId")} FROM {Tbl("LmsRequirementEnforcements")} WHERE {Col("UndoneOn")} IS NULL", new { })).ToList();
		}

		public Task<bool> TryInsertOpenAsync(LmsRequirementEnforcement enforcement, CancellationToken cancellationToken = default)
		{
			enforcement.UndoneOn = null;
			return TryInsertUniqueAsync(enforcement, cancellationToken);
		}

		public async Task<bool> TryMarkUndoneAsync(int departmentId, int lmsRequirementEnforcementId, LmsEnforcementUndoReasons reason, string restoredRolesJson, DateTime utcNow,
			CancellationToken cancellationToken = default)
		{
			return await ExecuteAsync(
				$"UPDATE {Tbl("LmsRequirementEnforcements")} SET {Col("UndoneOn")} = {P}Now, {Col("UndoReason")} = {P}Reason, {Col("RestoredRolesJson")} = {P}Roles " +
				$"WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsRequirementEnforcementId")} = {P}Id AND {Col("UndoneOn")} IS NULL",
				new { Now = DatabaseTimestamp(utcNow), Reason = (int)reason, Roles = restoredRolesJson, DepartmentId = departmentId, Id = lmsRequirementEnforcementId },
				cancellationToken) == 1;
		}
	}

	public class LmsRequirementDepartmentStatesRepository : LmsRepositoryBase<LmsRequirementDepartmentState>, ILmsRequirementDepartmentStatesRepository
	{
		public LmsRequirementDepartmentStatesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<LmsRequirementDepartmentState> GetForDepartmentAsync(int departmentId)
		{
			return QueryFirstOrDefaultAsync<LmsRequirementDepartmentState>(
				$"SELECT * FROM {Tbl("LmsRequirementDepartmentStates")} WHERE {Col("DepartmentId")} = {P}DepartmentId", new { DepartmentId = departmentId });
		}

		public async Task<bool> TryClaimDayAsync(int departmentId, DateTime localDate, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var parameters = new { DepartmentId = departmentId, Day = DatabaseTimestamp(localDate.Date), Now = DatabaseTimestamp(utcNow) };
			var claim = $"UPDATE {Tbl("LmsRequirementDepartmentStates")} SET {Col("LastSweepLocalDate")} = {P}Day, {Col("LastSweepOn")} = {P}Now " +
				$"WHERE {Col("DepartmentId")} = {P}DepartmentId AND ({Col("LastSweepLocalDate")} IS NULL OR {Col("LastSweepLocalDate")} < {P}Day)";
			if (await ExecuteAsync(claim, parameters, cancellationToken) == 1)
				return true;

			if (await GetForDepartmentAsync(departmentId) != null)
				return false;

			// First pass for the department: the unique index lets exactly one of two racing ticks insert the claimed row.
			return await TryInsertUniqueAsync(new LmsRequirementDepartmentState { DepartmentId = departmentId, LastSweepLocalDate = localDate.Date, LastSweepOn = utcNow },
				cancellationToken);
		}

		public Task ReleaseDayAsync(int departmentId, DateTime localDate, CancellationToken cancellationToken = default)
		{
			return ExecuteAsync(
				$"UPDATE {Tbl("LmsRequirementDepartmentStates")} SET {Col("LastSweepLocalDate")} = NULL WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LastSweepLocalDate")} = {P}Day",
				new { DepartmentId = departmentId, Day = DatabaseTimestamp(localDate.Date) }, cancellationToken);
		}

		public Task StampRefusedAsync(int departmentId, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			return ExecuteAsync(
				$"UPDATE {Tbl("LmsRequirementDepartmentStates")} SET {Col("LastRefusedOn")} = {P}Now WHERE {Col("DepartmentId")} = {P}DepartmentId",
				new { DepartmentId = departmentId, Now = DatabaseTimestamp(utcNow) }, cancellationToken);
		}
	}

	public class LmsPathsRepository : LmsRepositoryBase<LmsPath>, ILmsPathsRepository
	{
		public LmsPathsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		private static string NotDeleted => $"{Col("IsDeleted")} = {(IsPostgres ? "false" : "0")}";

		public async Task<List<LmsPath>> GetVisibleAsync(int departmentId)
		{
			return (await QueryAsync<LmsPath>(
				$"SELECT * FROM {Tbl("LmsPaths")} WHERE {DepartmentOrSystem()} AND {NotDeleted} ORDER BY {Col("DepartmentId")} DESC, {Col("Name")}", new { DepartmentId = departmentId })).ToList();
		}

		public Task<LmsPath> GetVisibleByIdAsync(int departmentId, int lmsPathId)
		{
			return QueryFirstOrDefaultAsync<LmsPath>(
				$"SELECT * FROM {Tbl("LmsPaths")} WHERE {DepartmentOrSystem()} AND {Col("LmsPathId")} = {P}Id", new { DepartmentId = departmentId, Id = lmsPathId });
		}

		public Task<LmsPath> GetBySystemKeyAsync(string systemKey)
		{
			return QueryFirstOrDefaultAsync<LmsPath>(
				$"SELECT * FROM {Tbl("LmsPaths")} WHERE {Col("SystemKey")} = {CaseInsensitive("SystemKey")} AND {Col("DepartmentId")} = 0", new { SystemKey = systemKey });
		}
	}

	public class LmsPathStepsRepository : LmsRepositoryBase<LmsPathStep>, ILmsPathStepsRepository
	{
		public LmsPathStepsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<List<LmsPathStep>> GetForPathsAsync(IEnumerable<int> lmsPathIds)
		{
			var ids = InListValue(lmsPathIds);
			if (ids.Length == 0)
				return new List<LmsPathStep>();
			return (await QueryAsync<LmsPathStep>(
				$"SELECT * FROM {Tbl("LmsPathSteps")} WHERE {InList("LmsPathId", "Ids")} ORDER BY {Col("LmsPathId")}, {Col("SortOrder")}", new { Ids = ids })).ToList();
		}

		public async Task ReplaceAsync(int departmentId, int lmsPathId, IEnumerable<LmsPathStep> steps, CancellationToken cancellationToken = default)
		{
			var owns = UnitOfWork.Transaction == null;
			UnitOfWork.CreateOrGetConnection();
			try
			{
				await ExecuteAsync($"DELETE FROM {Tbl("LmsPathSteps")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsPathId")} = {P}Id",
					new { DepartmentId = departmentId, Id = lmsPathId }, cancellationToken);
				foreach (var step in (steps ?? Enumerable.Empty<LmsPathStep>()).Where(s => s != null).OrderBy(s => s.SortOrder))
				{
					step.LmsPathStepId = 0;
					step.LmsPathId = lmsPathId;
					step.DepartmentId = departmentId;
					await base.InsertAsync(step, cancellationToken, true);
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

	/// <summary>The engine's read projections over LMS enrollments and the T1 ledger (explicit columns only).</summary>
	public class LmsRequirementFactsRepository : LmsRepositoryBase<LmsRequirementMember>, ILmsRequirementFactsRepository
	{
		/// <summary>The ledger's baseline-training completion reference: "training:{trainingId}:{cycle}".</summary>
		public const string TrainingRefPrefix = "training:";

		public LmsRequirementFactsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		private const int Verified = (int)LearningRecordStatuses.Verified;

		public async Task<List<LmsRequirementFact>> GetCourseCompletionsAsync(int departmentId, IEnumerable<int> lmsCourseIds)
		{
			var ids = InListValue(lmsCourseIds);
			if (ids.Length == 0)
				return new List<LmsRequirementFact>();
			return (await QueryAsync<LmsRequirementFact>(
				$"SELECT {Col("UserId")}, {Col("LmsCourseId")} AS {Col("RefId")}, {Col("CompletedOn")}, {Col("LearningRecordId")}, {Col("CompletionMethod")} FROM {Tbl("LmsEnrollments")} " +
				$"WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("Status")} = {(int)LmsEnrollmentStatuses.Completed} AND {Col("CompletedOn")} IS NOT NULL " +
				$"AND {InList("LmsCourseId", "Ids")}", new { DepartmentId = departmentId, Ids = ids })).ToList();
		}

		public async Task<List<LmsRequirementFact>> GetCourseCompletionsWithRecordsAsync(int departmentId, IEnumerable<int> lmsCourseIds)
		{
			var ids = InListValue(lmsCourseIds);
			if (ids.Length == 0)
				return new List<LmsRequirementFact>();
			var reference = Concat("'lms-enrollment:'", IntegerAsText("e." + Col("LmsEnrollmentId"))) + (IsPostgres ? "::citext" : string.Empty);
			return (await QueryAsync<LmsRequirementFact>(
				$"SELECT e.{Col("UserId")}, e.{Col("LmsCourseId")} AS {Col("RefId")}, e.{Col("CompletedOn")}, COALESCE(e.{Col("LearningRecordId")}, lr.{Col("LearningRecordId")}) AS {Col("LearningRecordId")}, " +
				$"e.{Col("CompletionMethod")} FROM {Tbl("LmsEnrollments")} e LEFT JOIN {Tbl("LearningRecords")} lr ON lr.{Col("DepartmentId")} = e.{Col("DepartmentId")} " +
				$"AND lr.{Col("SourceKind")} IN ({(int)LearningRecordSourceKinds.LmsCourseCompletion}, {(int)LearningRecordSourceKinds.AcademyLesson}) AND lr.{Col("SourceRefId")} = ({reference}) " +
				$"AND lr.{Col("Status")} <> {(int)LearningRecordStatuses.Voided} " +
				$"WHERE e.{Col("DepartmentId")} = {P}DepartmentId AND e.{Col("Status")} = {(int)LmsEnrollmentStatuses.Completed} AND e.{Col("CompletedOn")} IS NOT NULL " +
				$"AND {InList("LmsCourseId", "Ids", "e")}", new { DepartmentId = departmentId, Ids = ids })).ToList();
		}

		public async Task<List<LmsRequirementFact>> GetTrainingCompletionsAsync(int departmentId, IEnumerable<int> trainingIds)
		{
			var wanted = new HashSet<int>(trainingIds ?? Enumerable.Empty<int>());
			if (wanted.Count == 0)
				return new List<LmsRequirementFact>();
			var rows = await QueryAsync<LmsRequirementFact>(
				$"SELECT {Col("UserId")}, {Col("SourceRefId")} AS {Col("Code")}, {Col("CompletedOn")}, {Col("LearningRecordId")}, {Col("CompletionMethod")} FROM {Tbl("LearningRecords")} " +
				$"WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("SourceKind")} = {(int)LearningRecordSourceKinds.CourseCompletion} AND {Col("Status")} = {Verified} " +
				$"AND {Col("SourceRefId")} LIKE {P}Prefix", new { DepartmentId = departmentId, Prefix = TrainingRefPrefix + "%" });
			var facts = new List<LmsRequirementFact>();
			foreach (var row in rows)
			{
				var trainingId = ParseTrainingId(row.Code);
				if (trainingId > 0 && wanted.Contains(trainingId))
				{
					row.RefId = trainingId;
					row.Code = null;
					facts.Add(row);
				}
			}

			return facts;
		}

		/// <summary>The training id of a "training:{id}:{cycle}" reference; 0 when it is not one.</summary>
		public static int ParseTrainingId(string sourceRefId)
		{
			if (string.IsNullOrWhiteSpace(sourceRefId) || !sourceRefId.StartsWith(TrainingRefPrefix, StringComparison.OrdinalIgnoreCase))
				return 0;
			var parts = sourceRefId.Split(':');
			return parts.Length >= 2 && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : 0;
		}

		public async Task<List<LmsRequirementFact>> GetCategoryHoursAsync(int departmentId, IEnumerable<int> categoryIds, DateTime fromUtc)
		{
			var ids = InListValue(categoryIds);
			if (ids.Length == 0)
				return new List<LmsRequirementFact>();
			return (await QueryAsync<LmsRequirementFact>(
				$"SELECT {Col("UserId")}, {Col("CategoryId")} AS {Col("RefId")}, {Col("CompletedOn")}, {Col("Hours")} AS {Col("Amount")}, {Col("LearningRecordId")} " +
				$"FROM {Tbl("LearningRecords")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("Status")} = {Verified} AND {InList("CategoryId", "Ids")} " +
				$"AND {Col("CompletedOn")} >= {P}From", new { DepartmentId = departmentId, Ids = ids, From = DatabaseTimestamp(fromUtc) })).ToList();
		}

		public async Task<List<LmsRequirementFact>> GetEvaluationsAsync(int departmentId, IEnumerable<string> codes)
		{
			var wanted = InListValue((codes ?? Enumerable.Empty<string>()).Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim().ToLowerInvariant()).Distinct());
			if (wanted.Length == 0)
				return new List<LmsRequirementFact>();
			return (await QueryAsync<LmsRequirementFact>(
				$"SELECT {Col("UserId")}, {Col("CourseCode")} AS {Col("Code")}, {Col("CompletedOn")}, {Col("LearningRecordId")} FROM {Tbl("LearningRecords")} " +
				$"WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("SourceKind")} = {(int)LearningRecordSourceKinds.SkillEvaluation} AND {Col("Status")} = {Verified} " +
				$"AND LOWER({Col("CourseCode")}) " + (IsPostgres ? $"= ANY({P}Codes)" : $"IN {P}Codes"), new { DepartmentId = departmentId, Codes = wanted })).ToList();
		}
	}
}
