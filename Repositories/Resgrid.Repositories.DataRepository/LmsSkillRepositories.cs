using System;
using System.Collections.Generic;
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
	// Training T4 part 2 (round 5C, M0381-M0384). Portable SQL over the LMS repository base; member ids are citext on PostgreSQL, so their
	// parameters are cast (pg-citext-text-param-equality); list parameters use InList (postgres-dapper-in-list); list queries never select
	// evidence bytes; nullable timestamps never decide the SQL shape in SQL (pg-untyped-null-param-is-null).

	public class LmsSkillSheetsRepository : LmsRepositoryBase<LmsSkillSheet>, ILmsSkillSheetsRepository
	{
		public LmsSkillSheetsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<List<LmsSkillSheet>> GetForDepartmentAsync(int departmentId, bool includeDeleted = false) =>
			(await QueryAsync<LmsSkillSheet>(
				$"SELECT * FROM {Tbl("LmsSkillSheets")} WHERE {Col("DepartmentId")} = {P}DepartmentId" +
				(includeDeleted ? string.Empty : $" AND {Col("IsDeleted")} = {P}No") +
				$" ORDER BY {Col("Title")}, {Col("LmsSkillSheetId")}",
				new { DepartmentId = departmentId, No = false })).ToList();

		public Task<LmsSkillSheet> GetByIdForDepartmentAsync(int departmentId, int lmsSkillSheetId) =>
			QueryFirstOrDefaultAsync<LmsSkillSheet>(
				$"SELECT * FROM {Tbl("LmsSkillSheets")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsSkillSheetId")} = {P}Id",
				new { DepartmentId = departmentId, Id = lmsSkillSheetId });

		public async Task<List<LmsSkillSheet>> GetByIdsAsync(int departmentId, IEnumerable<int> lmsSkillSheetIds)
		{
			var ids = InListValue((lmsSkillSheetIds ?? Enumerable.Empty<int>()).Where(x => x > 0).Distinct());
			if (ids.Length == 0)
				return new List<LmsSkillSheet>();
			return (await QueryAsync<LmsSkillSheet>(
				$"SELECT * FROM {Tbl("LmsSkillSheets")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {InList("LmsSkillSheetId", "Ids")}",
				new { DepartmentId = departmentId, Ids = ids })).ToList();
		}

		public Task<bool> TryUpdateAsync(LmsSkillSheet sheet, long expectedRowVersion, CancellationToken cancellationToken = default) =>
			TryUpdateVersionedAsync(sheet, "LmsSkillSheets", "LmsSkillSheetId", sheet?.LmsSkillSheetId ?? 0, sheet?.DepartmentId ?? 0, expectedRowVersion, v => sheet.RowVersion = v, cancellationToken);
	}

	public class LmsSkillSheetVersionsRepository : LmsRepositoryBase<LmsSkillSheetVersion>, ILmsSkillSheetVersionsRepository
	{
		public LmsSkillSheetVersionsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<List<LmsSkillSheetVersion>> GetForSheetAsync(int departmentId, int lmsSkillSheetId) =>
			(await QueryAsync<LmsSkillSheetVersion>(
				$"SELECT * FROM {Tbl("LmsSkillSheetVersions")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsSkillSheetId")} = {P}SheetId " +
				$"ORDER BY {Col("Major")} DESC, {Col("Minor")} DESC, {Col("LmsSkillSheetVersionId")} DESC",
				new { DepartmentId = departmentId, SheetId = lmsSkillSheetId })).ToList();

		public Task<LmsSkillSheetVersion> GetByIdForDepartmentAsync(int departmentId, int lmsSkillSheetVersionId) =>
			QueryFirstOrDefaultAsync<LmsSkillSheetVersion>(
				$"SELECT * FROM {Tbl("LmsSkillSheetVersions")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsSkillSheetVersionId")} = {P}Id",
				new { DepartmentId = departmentId, Id = lmsSkillSheetVersionId });

		public Task<bool> TryUpdateAsync(LmsSkillSheetVersion version, long expectedRowVersion, CancellationToken cancellationToken = default) =>
			TryUpdateVersionedAsync(version, "LmsSkillSheetVersions", "LmsSkillSheetVersionId", version?.LmsSkillSheetVersionId ?? 0, version?.DepartmentId ?? 0, expectedRowVersion,
				v => version.RowVersion = v, cancellationToken);
	}

	public class LmsSkillEvaluationsRepository : LmsRepositoryBase<LmsSkillEvaluation>, ILmsSkillEvaluationsRepository
	{
		/// <summary>The list columns (everything but the answers and notes).</summary>
		private static readonly string[] ListColumns =
		{
			"LmsSkillEvaluationId", "DepartmentId", "LmsSkillSheetId", "LmsSkillSheetVersionId", "Title", "Code", "Mode", "Status", "Result", "Score", "CriticalFailed",
			"TimeExceeded", "EvaluatorUserId", "CreatedByUserId", "StartedOn", "SubmittedOn", "CompletedOn", "DurationSeconds", "CallId", "DeploymentId", "Location",
			"LmsTaskBookId", "TaskKey", "ClassSessionRef", "ParentEvaluationId", "IdempotencyKey", "ContentHash", "SignatureRequestId", "NextSignerUserId",
			"ExternalEvaluatorName", "VoidedOn", "VoidedByUserId", "VoidReason", "UpdatedOn", "RowVersion"
		};

		public LmsSkillEvaluationsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		private string List(string alias = null) =>
			string.Join(", ", ListColumns.Select(c => alias == null ? Col(c) : alias + "." + Col(c)));

		public Task<LmsSkillEvaluation> GetByIdForDepartmentAsync(int departmentId, int lmsSkillEvaluationId) =>
			QueryFirstOrDefaultAsync<LmsSkillEvaluation>(
				$"SELECT * FROM {Tbl("LmsSkillEvaluations")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsSkillEvaluationId")} = {P}Id",
				new { DepartmentId = departmentId, Id = lmsSkillEvaluationId });

		public Task<LmsSkillEvaluation> GetByIdempotencyKeyAsync(int departmentId, string idempotencyKey) =>
			string.IsNullOrWhiteSpace(idempotencyKey) ? Task.FromResult<LmsSkillEvaluation>(null) :
			QueryFirstOrDefaultAsync<LmsSkillEvaluation>(
				$"SELECT * FROM {Tbl("LmsSkillEvaluations")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("IdempotencyKey")} = {P}IdempotencyKey",
				new { DepartmentId = departmentId, IdempotencyKey = idempotencyKey.Trim() });

		public async Task<List<LmsSkillEvaluation>> GetForDepartmentAsync(int departmentId, DateTime? fromUtc, DateTime? toUtc, int? lmsSkillSheetId, int? status, int take)
		{
			var where = $"{Col("DepartmentId")} = {P}DepartmentId";
			if (fromUtc.HasValue)
				where += $" AND {Col("StartedOn")} >= {P}FromUtc";
			if (toUtc.HasValue)
				where += $" AND {Col("StartedOn")} < {P}ToUtc";
			if (lmsSkillSheetId.HasValue)
				where += $" AND {Col("LmsSkillSheetId")} = {P}SheetId";
			if (status.HasValue)
				where += $" AND {Col("Status")} = {P}Status";
			return (await QueryAsync<LmsSkillEvaluation>(
				$"SELECT {List()} FROM {Tbl("LmsSkillEvaluations")} WHERE {where} ORDER BY {Col("StartedOn")} DESC, {Col("LmsSkillEvaluationId")} DESC {Take()}",
				new
				{
					DepartmentId = departmentId, FromUtc = fromUtc.HasValue ? DatabaseTimestamp(fromUtc.Value) : DateTime.MinValue,
					ToUtc = toUtc.HasValue ? DatabaseTimestamp(toUtc.Value) : DateTime.MaxValue, SheetId = lmsSkillSheetId ?? 0, Status = status ?? 0, Take = Math.Clamp(take, 1, 2000)
				})).ToList();
		}

		public async Task<List<LmsSkillEvaluation>> GetOpenForEvaluatorAsync(int departmentId, string evaluatorUserId) =>
			string.IsNullOrWhiteSpace(evaluatorUserId) ? new List<LmsSkillEvaluation>() :
			(await QueryAsync<LmsSkillEvaluation>(
				$"SELECT {List()} FROM {Tbl("LmsSkillEvaluations")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("EvaluatorUserId")} = {CaseInsensitive("UserId")} " +
				$"AND {Col("Status")} IN ({(int)LmsSkillEvaluationStatuses.InProgress}, {(int)LmsSkillEvaluationStatuses.AwaitingSignature}) ORDER BY {Col("StartedOn")} DESC",
				new { DepartmentId = departmentId, UserId = evaluatorUserId.Trim() })).ToList();

		public async Task<List<LmsSkillEvaluation>> GetForMemberAsync(int departmentId, string userId, int? lmsSkillSheetId)
		{
			if (string.IsNullOrWhiteSpace(userId))
				return new List<LmsSkillEvaluation>();
			return (await QueryAsync<LmsSkillEvaluation>(
				$"SELECT {List("e")} FROM {Tbl("LmsSkillEvaluations")} e INNER JOIN {Tbl("LmsSkillEvaluationMembers")} m ON m.{Col("LmsSkillEvaluationId")} = e.{Col("LmsSkillEvaluationId")} " +
				$"WHERE e.{Col("DepartmentId")} = {P}DepartmentId AND m.{Col("DepartmentId")} = {P}DepartmentId AND m.{Col("UserId")} = {CaseInsensitive("UserId")} " +
				$"AND m.{Col("State")} = {(int)LmsSkillEvaluationMemberStates.Included}" + (lmsSkillSheetId.HasValue ? $" AND e.{Col("LmsSkillSheetId")} = {P}SheetId" : string.Empty) +
				$" ORDER BY e.{Col("StartedOn")} DESC, e.{Col("LmsSkillEvaluationId")} DESC",
				new { DepartmentId = departmentId, UserId = userId.Trim(), SheetId = lmsSkillSheetId ?? 0 })).ToList();
		}

		public async Task<List<LmsSkillEvaluation>> GetForTaskBookAsync(int departmentId, int lmsTaskBookId) =>
			(await QueryAsync<LmsSkillEvaluation>(
				$"SELECT {List()} FROM {Tbl("LmsSkillEvaluations")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsTaskBookId")} = {P}BookId " +
				$"ORDER BY {Col("StartedOn")}, {Col("LmsSkillEvaluationId")}",
				new { DepartmentId = departmentId, BookId = lmsTaskBookId })).ToList();

		public async Task<List<LmsSkillEvaluation>> GetForClassSessionAsync(int departmentId, string classSessionRef) =>
			string.IsNullOrWhiteSpace(classSessionRef) ? new List<LmsSkillEvaluation>() :
			(await QueryAsync<LmsSkillEvaluation>(
				$"SELECT {List()} FROM {Tbl("LmsSkillEvaluations")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("ClassSessionRef")} = {CaseInsensitive("Ref")} " +
				$"ORDER BY {Col("LmsSkillEvaluationId")}",
				new { DepartmentId = departmentId, Ref = classSessionRef.Trim() })).ToList();

		public Task<LmsSkillEvaluation> GetBySignatureRequestAsync(int departmentId, int signatureRequestId) =>
			QueryFirstOrDefaultAsync<LmsSkillEvaluation>(
				$"SELECT * FROM {Tbl("LmsSkillEvaluations")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("SignatureRequestId")} = {P}RequestId",
				new { DepartmentId = departmentId, RequestId = signatureRequestId });

		public async Task<List<LmsSkillEvaluation>> GetCompletedForReportAsync(int departmentId, DateTime fromUtc, DateTime toUtc, int? lmsSkillSheetId) =>
			(await QueryAsync<LmsSkillEvaluation>(
				$"SELECT * FROM {Tbl("LmsSkillEvaluations")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("Status")} = {(int)LmsSkillEvaluationStatuses.Completed} " +
				$"AND {Col("CompletedOn")} >= {P}FromUtc AND {Col("CompletedOn")} < {P}ToUtc" + (lmsSkillSheetId.HasValue ? $" AND {Col("LmsSkillSheetId")} = {P}SheetId" : string.Empty) +
				$" ORDER BY {Col("CompletedOn")}, {Col("LmsSkillEvaluationId")}",
				new { DepartmentId = departmentId, FromUtc = DatabaseTimestamp(fromUtc), ToUtc = DatabaseTimestamp(toUtc), SheetId = lmsSkillSheetId ?? 0 })).ToList();

		public Task<bool> TryUpdateAsync(LmsSkillEvaluation evaluation, long expectedRowVersion, CancellationToken cancellationToken = default) =>
			TryUpdateVersionedAsync(evaluation, "LmsSkillEvaluations", "LmsSkillEvaluationId", evaluation?.LmsSkillEvaluationId ?? 0, evaluation?.DepartmentId ?? 0, expectedRowVersion,
				v => evaluation.RowVersion = v, cancellationToken);

		/// <summary>Inserts an evaluation an idempotency key may already hold (an offline replay racing the first delivery); false on the unique index.</summary>
		public Task<bool> TryInsertAsync(LmsSkillEvaluation evaluation, CancellationToken cancellationToken = default) => TryInsertUniqueAsync(evaluation, cancellationToken);
	}

	public class LmsSkillEvaluationMembersRepository : LmsRepositoryBase<LmsSkillEvaluationMember>, ILmsSkillEvaluationMembersRepository
	{
		public LmsSkillEvaluationMembersRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<List<LmsSkillEvaluationMember>> GetForEvaluationAsync(int departmentId, int lmsSkillEvaluationId) =>
			(await QueryAsync<LmsSkillEvaluationMember>(
				$"SELECT * FROM {Tbl("LmsSkillEvaluationMembers")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsSkillEvaluationId")} = {P}Id " +
				$"ORDER BY {Col("LmsSkillEvaluationMemberId")}",
				new { DepartmentId = departmentId, Id = lmsSkillEvaluationId })).ToList();

		public async Task<List<LmsSkillEvaluationMember>> GetForEvaluationsAsync(int departmentId, IEnumerable<int> lmsSkillEvaluationIds)
		{
			var ids = InListValue((lmsSkillEvaluationIds ?? Enumerable.Empty<int>()).Where(x => x > 0).Distinct());
			var result = new List<LmsSkillEvaluationMember>();
			foreach (var chunk in ids.Chunk(1000))
				result.AddRange(await QueryAsync<LmsSkillEvaluationMember>(
					$"SELECT * FROM {Tbl("LmsSkillEvaluationMembers")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {InList("LmsSkillEvaluationId", "Ids")} " +
					$"ORDER BY {Col("LmsSkillEvaluationMemberId")}",
					new { DepartmentId = departmentId, Ids = chunk }));
			return result;
		}
	}

	public class LmsSkillEvaluationFilesRepository : LmsRepositoryBase<LmsSkillEvaluationFile>, ILmsSkillEvaluationFilesRepository
	{
		public LmsSkillEvaluationFilesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<List<LmsSkillEvaluationFile>> GetForEvaluationAsync(int departmentId, int lmsSkillEvaluationId) =>
			(await QueryAsync<LmsSkillEvaluationFile>(
				$"SELECT {Cols("LmsSkillEvaluationFileId", "LmsSkillEvaluationId", "DepartmentId", "StepKey", "FileName", "ContentType", "Size", "Sha256", "UploadedByUserId", "CreatedOn")} " +
				$"FROM {Tbl("LmsSkillEvaluationFiles")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsSkillEvaluationId")} = {P}Id ORDER BY {Col("LmsSkillEvaluationFileId")}",
				new { DepartmentId = departmentId, Id = lmsSkillEvaluationId })).ToList();

		public Task<LmsSkillEvaluationFile> GetWithDataAsync(int departmentId, int lmsSkillEvaluationFileId) =>
			QueryFirstOrDefaultAsync<LmsSkillEvaluationFile>(
				$"SELECT * FROM {Tbl("LmsSkillEvaluationFiles")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsSkillEvaluationFileId")} = {P}Id",
				new { DepartmentId = departmentId, Id = lmsSkillEvaluationFileId });
	}

	public class LmsTaskBookDefinitionsRepository : LmsRepositoryBase<LmsTaskBookDefinition>, ILmsTaskBookDefinitionsRepository
	{
		public LmsTaskBookDefinitionsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<List<LmsTaskBookDefinition>> GetForDepartmentAsync(int departmentId, bool includeDeleted = false) =>
			(await QueryAsync<LmsTaskBookDefinition>(
				$"SELECT * FROM {Tbl("LmsTaskBookDefinitions")} WHERE {Col("DepartmentId")} = {P}DepartmentId" +
				(includeDeleted ? string.Empty : $" AND {Col("IsDeleted")} = {P}No") +
				$" ORDER BY {Col("PositionCode")}, {Col("Name")}, {Col("LmsTaskBookDefinitionId")}",
				new { DepartmentId = departmentId, No = false })).ToList();

		public Task<LmsTaskBookDefinition> GetByIdForDepartmentAsync(int departmentId, int lmsTaskBookDefinitionId) =>
			QueryFirstOrDefaultAsync<LmsTaskBookDefinition>(
				$"SELECT * FROM {Tbl("LmsTaskBookDefinitions")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsTaskBookDefinitionId")} = {P}Id",
				new { DepartmentId = departmentId, Id = lmsTaskBookDefinitionId });

		public Task<bool> TryUpdateAsync(LmsTaskBookDefinition definition, long expectedRowVersion, CancellationToken cancellationToken = default) =>
			TryUpdateVersionedAsync(definition, "LmsTaskBookDefinitions", "LmsTaskBookDefinitionId", definition?.LmsTaskBookDefinitionId ?? 0, definition?.DepartmentId ?? 0,
				expectedRowVersion, v => definition.RowVersion = v, cancellationToken);
	}

	public class LmsTaskBooksRepository : LmsRepositoryBase<LmsTaskBook>, ILmsTaskBooksRepository
	{
		public LmsTaskBooksRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		private static string OpenStatuses => $"({(int)LmsTaskBookStatuses.Open}, {(int)LmsTaskBookStatuses.AwaitingCertification})";

		public Task<LmsTaskBook> GetByIdForDepartmentAsync(int departmentId, int lmsTaskBookId) =>
			QueryFirstOrDefaultAsync<LmsTaskBook>(
				$"SELECT * FROM {Tbl("LmsTaskBooks")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsTaskBookId")} = {P}Id",
				new { DepartmentId = departmentId, Id = lmsTaskBookId });

		public async Task<List<LmsTaskBook>> GetForDepartmentAsync(int departmentId, bool openOnly) =>
			(await QueryAsync<LmsTaskBook>(
				$"SELECT * FROM {Tbl("LmsTaskBooks")} WHERE {Col("DepartmentId")} = {P}DepartmentId" + (openOnly ? $" AND {Col("Status")} IN {OpenStatuses}" : string.Empty) +
				$" ORDER BY {Col("InitiatedOn")} DESC, {Col("LmsTaskBookId")} DESC",
				new { DepartmentId = departmentId })).ToList();

		public async Task<List<LmsTaskBook>> GetForMemberAsync(int departmentId, string userId) =>
			string.IsNullOrWhiteSpace(userId) ? new List<LmsTaskBook>() :
			(await QueryAsync<LmsTaskBook>(
				$"SELECT * FROM {Tbl("LmsTaskBooks")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("UserId")} = {CaseInsensitive("UserId")} " +
				$"ORDER BY {Col("InitiatedOn")} DESC, {Col("LmsTaskBookId")} DESC",
				new { DepartmentId = departmentId, UserId = userId.Trim() })).ToList();

		public Task<LmsTaskBook> GetBySignatureRequestAsync(int departmentId, int signatureRequestId) =>
			QueryFirstOrDefaultAsync<LmsTaskBook>(
				$"SELECT * FROM {Tbl("LmsTaskBooks")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("SignatureRequestId")} = {P}RequestId",
				new { DepartmentId = departmentId, RequestId = signatureRequestId });

		public async Task<List<LmsTaskBook>> GetExpiredOpenAsync(DateTime utcNow, int take) =>
			(await QueryAsync<LmsTaskBook>(
				$"SELECT * FROM {Tbl("LmsTaskBooks")} WHERE {Col("Status")} = {(int)LmsTaskBookStatuses.Open} AND {Col("ExpiresOn")} <= {P}Now " +
				$"ORDER BY {Col("ExpiresOn")}, {Col("LmsTaskBookId")} {Take()}",
				new { Now = DatabaseTimestamp(utcNow), Take = Math.Clamp(take, 1, 1000) })).ToList();

		public Task<bool> TryUpdateAsync(LmsTaskBook book, long expectedRowVersion, CancellationToken cancellationToken = default) =>
			TryUpdateVersionedAsync(book, "LmsTaskBooks", "LmsTaskBookId", book?.LmsTaskBookId ?? 0, book?.DepartmentId ?? 0, expectedRowVersion, v => book.RowVersion = v, cancellationToken);
	}

	public class LmsSkillNotificationsRepository : LmsRepositoryBase<LmsSkillNotification>, ILmsSkillNotificationsRepository
	{
		public LmsSkillNotificationsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<List<LmsSkillNotification>> GetDueAsync(DateTime utcNow, DateTime staleClaimBefore, int take) =>
			(await QueryAsync<LmsSkillNotification>(
				$"SELECT * FROM {Tbl("LmsSkillNotifications")} WHERE {Col("SentOn")} IS NULL AND {Col("DueOn")} <= {P}Now " +
				$"AND ({Col("ClaimedOn")} IS NULL OR {Col("ClaimedOn")} < {P}Stale) ORDER BY {Col("DepartmentId")}, {Col("UserId")}, {Col("LmsSkillNotificationId")} {Take()}",
				new { Now = DatabaseTimestamp(utcNow), Stale = DatabaseTimestamp(staleClaimBefore), Take = Math.Clamp(take, 1, 2000) })).ToList();

		public async Task<bool> ClaimAsync(int lmsSkillNotificationId, DateTime utcNow, DateTime staleClaimBefore, CancellationToken cancellationToken = default) =>
			await ExecuteAsync(
				$"UPDATE {Tbl("LmsSkillNotifications")} SET {Col("ClaimedOn")} = {P}Now WHERE {Col("LmsSkillNotificationId")} = {P}Id AND {Col("SentOn")} IS NULL " +
				$"AND ({Col("ClaimedOn")} IS NULL OR {Col("ClaimedOn")} < {P}Stale)",
				new { Id = lmsSkillNotificationId, Now = DatabaseTimestamp(utcNow), Stale = DatabaseTimestamp(staleClaimBefore) }, cancellationToken) == 1;

		public async Task MarkSentAsync(IEnumerable<int> lmsSkillNotificationIds, DateTime utcNow, bool skipped, CancellationToken cancellationToken = default)
		{
			var ids = InListValue((lmsSkillNotificationIds ?? Enumerable.Empty<int>()).Where(x => x > 0).Distinct());
			foreach (var chunk in ids.Chunk(1000))
				await ExecuteAsync(
					$"UPDATE {Tbl("LmsSkillNotifications")} SET {Col("SentOn")} = {P}Now, {Col("Skipped")} = {P}Skipped WHERE {InList("LmsSkillNotificationId", "Ids")}",
					new { Ids = chunk, Now = DatabaseTimestamp(utcNow), Skipped = skipped }, cancellationToken);
		}

		public async Task<List<LmsSkillNotification>> GetForUserAsync(int departmentId, string userId, int take) =>
			string.IsNullOrWhiteSpace(userId) ? new List<LmsSkillNotification>() :
			(await QueryAsync<LmsSkillNotification>(
				$"SELECT * FROM {Tbl("LmsSkillNotifications")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("UserId")} = {CaseInsensitive("UserId")} " +
				$"ORDER BY {Col("CreatedOn")} DESC, {Col("LmsSkillNotificationId")} DESC {Take()}",
				new { DepartmentId = departmentId, UserId = userId.Trim(), Take = Math.Clamp(take, 1, 500) })).ToList();
	}
}
