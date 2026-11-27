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
	/// <summary>Column-list helpers shared by the Training T1 repositories.</summary>
	internal static class LearningRecordColumns
	{
		/// <summary>Every LearningRecords column except the identity (insert order).</summary>
		public static readonly string[] Insert =
		{
			"DepartmentId", "UserId", "SourceKind", "SourceRefId", "ComponentKey", "SessionKey", "Title", "CourseCode", "CompletedOn", "StartedOn", "EndedOn",
			"Hours", "CategoryId", "MethodOfTraining", "Objectives", "Location", "CeProvider", "CeCourseNumber", "CeHours", "InstructorUserIds", "InstructorNames",
			"Status", "SupersedesId", "VoidReason", "VoidedOn", "VoidedByUserId", "VerifiedByUserId", "VerifiedOn", "ReviewNote", "EvidenceFileId", "IsBackfill",
			"ExternalId", "Notes", "Tags", "PossibleDuplicateOfId", "DuplicateResolvedOn", "DuplicateResolution", "VerificationCode", "CreatedOn", "CreatedByUserId",
			// Round 7B (M0400): a completion recorded by someone else (permission 135)
			"CompletionMethod", "RecordedByUserId", "RecordedOn", "RecordedCompletionId",
			// Round 9B (M0408): a member submission's review state and when it was handed to reviewers
			"ReviewState", "SubmittedOn"
		};

		/// <summary>Columns a later write may change; the idempotency key and the source fields never change (R-T4).</summary>
		public static readonly HashSet<string> Updatable = new HashSet<string>(StringComparer.Ordinal)
		{
			"Status", "Hours", "CategoryId", "VoidReason", "VoidedOn", "VoidedByUserId", "VerifiedByUserId", "VerifiedOn", "ReviewNote", "PossibleDuplicateOfId",
			"DuplicateResolvedOn", "DuplicateResolution", "VerificationCode", "SessionKey",
			// Round 9B: a submission's review, and what its member may correct when they resubmit it (still Pending)
			"ReviewState", "SubmittedOn", "CeHours", "EvidenceFileId"
		};

		public static DynamicParameters Parameters<T>(T entity, IEnumerable<string> columns, Func<DateTime, DateTime> timestamp)
		{
			var parameters = new DynamicParameters();
			foreach (var column in columns)
			{
				var property = typeof(T).GetProperty(column);
				var value = property.GetValue(entity);
				var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

				if (type == typeof(DateTime))
					parameters.Add(column, value == null ? (DateTime?)null : timestamp((DateTime)value), System.Data.DbType.DateTime2);
				else if (type == typeof(string))
					parameters.Add(column, value, System.Data.DbType.String);
				else if (type == typeof(decimal))
					parameters.Add(column, value, System.Data.DbType.Decimal);
				else if (type == typeof(bool))
					parameters.Add(column, value, System.Data.DbType.Boolean);
				else if (type == typeof(int))
					parameters.Add(column, value, System.Data.DbType.Int32);
				else
					parameters.Add(column, value);
			}

			return parameters;
		}
	}

	/// <summary>The learning-record ledger (Training T1, M0344). Every read starts at the department.</summary>
	public class LearningRecordsRepository : RmsRepositoryBase<LearningRecord>, ILearningRecordsRepository
	{
		public const int MaxTake = 5000;

		public LearningRecordsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		private static int Voided => (int)LearningRecordStatuses.Voided;

		public Task<LearningRecord> GetByIdForDepartmentAsync(int departmentId, int learningRecordId) =>
			QueryFirstOrDefaultAsync<LearningRecord>(
				$"SELECT * FROM {Tbl("LearningRecords")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LearningRecordId")} = {P}Id",
				new { DepartmentId = departmentId, Id = learningRecordId });

		public async Task<List<LearningRecord>> GetByIdsForDepartmentAsync(int departmentId, IEnumerable<int> learningRecordIds)
		{
			var ids = InListValue(learningRecordIds).Distinct().ToArray();
			if (ids.Length == 0)
				return new List<LearningRecord>();

			var result = new List<LearningRecord>();
			foreach (var chunk in ids.Chunk(1000))
				result.AddRange(await QueryAsync<LearningRecord>(
					$"SELECT * FROM {Tbl("LearningRecords")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {InList("LearningRecordId", "Ids")}",
					new { DepartmentId = departmentId, Ids = chunk }));
			return result;
		}

		public async Task<List<LearningRecord>> GetBySourceAsync(int departmentId, int sourceKind, string sourceRefId)
		{
			return (await QueryAsync<LearningRecord>(
				$"SELECT * FROM {Tbl("LearningRecords")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("SourceKind")} = {P}Kind AND {Col("SourceRefId")} = {P}Ref " +
				$"ORDER BY {Col("LearningRecordId")}",
				new { DepartmentId = departmentId, Kind = sourceKind, Ref = sourceRefId ?? string.Empty })).ToList();
		}

		public async Task<HashSet<string>> GetExistingSourceRefIdsAsync(int departmentId, int sourceKind, IEnumerable<string> sourceRefIds)
		{
			var result = new HashSet<string>(StringComparer.Ordinal);
			var refs = InListValue(sourceRefIds?.Where(r => !string.IsNullOrEmpty(r))).Distinct(StringComparer.Ordinal).ToArray();
			foreach (var chunk in refs.Chunk(1000))
			{
				var rows = await QueryAsync<string>(
					$"SELECT DISTINCT {Col("SourceRefId")} FROM {Tbl("LearningRecords")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("SourceKind")} = {P}Kind " +
					$"AND {Col("Status")} <> {Voided} AND {InList("SourceRefId", "Refs")}",
					new { DepartmentId = departmentId, Kind = sourceKind, Refs = chunk });
				foreach (var row in rows)
					result.Add(row);
			}

			return result;
		}

		public async Task<List<LearningRecord>> GetBySourceRefIdsAsync(int departmentId, int sourceKind, IEnumerable<string> sourceRefIds)
		{
			var result = new List<LearningRecord>();
			var refs = InListValue(sourceRefIds?.Where(r => !string.IsNullOrEmpty(r))).Distinct(StringComparer.Ordinal).ToArray();
			foreach (var chunk in refs.Chunk(1000))
				result.AddRange(await QueryAsync<LearningRecord>(
					$"SELECT * FROM {Tbl("LearningRecords")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("SourceKind")} = {P}Kind " +
					$"AND {Col("Status")} <> {Voided} AND {InList("SourceRefId", "Refs")}",
					new { DepartmentId = departmentId, Kind = sourceKind, Refs = chunk }));
			return result;
		}

		public async Task<List<LearningRecord>> GetBySessionKeyAsync(int departmentId, string sessionKey)
		{
			if (string.IsNullOrWhiteSpace(sessionKey))
				return new List<LearningRecord>();

			return (await QueryAsync<LearningRecord>(
				$"SELECT * FROM {Tbl("LearningRecords")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("SessionKey")} = {P}SessionKey AND {Col("Status")} <> {Voided}",
				new { DepartmentId = departmentId, SessionKey = sessionKey })).ToList();
		}

		private string Where(LearningRecordQuery query, DynamicParameters parameters)
		{
			var clauses = new List<string> { $"{Col("DepartmentId")} = {P}DepartmentId" };
			parameters.Add("DepartmentId", query.DepartmentId);

			var users = query.UserIds?.Where(u => !string.IsNullOrWhiteSpace(u)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
			if (users != null && users.Length > 0)
			{
				clauses.Add(CaseInsensitiveInList("UserId", "UserIds"));
				parameters.Add("UserIds", users);
			}
			else
			{
				// External learners (T5) are never members: a department-wide query leaves their records out.
				clauses.Add($"{Col("UserId")} NOT LIKE {P}ExternalLearnerPrefix");
				parameters.Add("ExternalLearnerPrefix", LearningRecordQuery.ExternalLearnerUserIdPrefix + "%");
			}

			if (query.From.HasValue)
			{
				clauses.Add($"{Col("CompletedOn")} >= {P}FromOn");
				parameters.Add("FromOn", DatabaseTimestamp(query.From.Value), System.Data.DbType.DateTime2);
			}

			if (query.To.HasValue)
			{
				clauses.Add($"{Col("CompletedOn")} < {P}ToOn");
				parameters.Add("ToOn", DatabaseTimestamp(query.To.Value), System.Data.DbType.DateTime2);
			}

			if (query.UncategorizedOnly)
				clauses.Add($"{Col("CategoryId")} IS NULL");
			else if (query.CategoryIds != null && query.CategoryIds.Count > 0)
			{
				clauses.Add(InList("CategoryId", "CategoryIds"));
				parameters.Add("CategoryIds", query.CategoryIds.Distinct().ToArray());
			}

			if (query.SourceKinds != null && query.SourceKinds.Count > 0)
			{
				clauses.Add(InList("SourceKind", "SourceKinds"));
				parameters.Add("SourceKinds", query.SourceKinds.Distinct().ToArray());
			}

			if (query.Statuses != null && query.Statuses.Count > 0)
			{
				clauses.Add(InList("Status", "Statuses"));
				parameters.Add("Statuses", query.Statuses.Distinct().ToArray());
			}
			else if (!query.IncludeVoided)
				clauses.Add($"{Col("Status")} <> {Voided}");

			if (!string.IsNullOrWhiteSpace(query.Search))
			{
				// LIKE folds case on both engines here: SQL Server's collation, and lower() on PostgreSQL.
				clauses.Add(IsPostgres
					? $"(lower({Col("Title")}) LIKE {P}Search ESCAPE '!' OR lower(COALESCE({Col("CourseCode")}, '')) LIKE {P}Search ESCAPE '!')"
					: $"({Col("Title")} LIKE {P}Search ESCAPE '!' OR {Col("CourseCode")} LIKE {P}Search ESCAPE '!')");
				parameters.Add("Search", "%" + EscapeLike(query.Search.Trim().ToLowerInvariant()) + "%", System.Data.DbType.String);
			}

			if (!string.IsNullOrWhiteSpace(query.CourseCode))
			{
				clauses.Add(IsPostgres ? $"lower({Col("CourseCode")}) = {P}CourseCode" : $"{Col("CourseCode")} = {P}CourseCode");
				parameters.Add("CourseCode", IsPostgres ? query.CourseCode.Trim().ToLowerInvariant() : query.CourseCode.Trim(), System.Data.DbType.String);
			}

			if (query.BackfillOnly)
				clauses.Add($"{Col("IsBackfill")} = {(IsPostgres ? "TRUE" : "1")}");

			if (query.UnresolvedDuplicatesOnly)
				clauses.Add($"{Col("PossibleDuplicateOfId")} IS NOT NULL AND {Col("DuplicateResolvedOn")} IS NULL");

			if (query.ReviewStates != null && query.ReviewStates.Count > 0)
			{
				// Round 9B: a T1 submission written before M0408 has no review state and is waiting for a reviewer.
				var states = query.ReviewStates.Distinct().ToArray();
				clauses.Add(states.Contains((int)TrainingSubmissionReviewStates.AwaitingReview)
					? $"({InList("ReviewState", "ReviewStates")} OR {Col("ReviewState")} IS NULL)"
					: InList("ReviewState", "ReviewStates"));
				parameters.Add("ReviewStates", states);
			}

			return string.Join(" AND ", clauses);
		}

		/// <summary>Escapes LIKE wildcards with '!' (declared with ESCAPE on both engines).</summary>
		private static string EscapeLike(string value) => value.Replace("!", "!!").Replace("%", "!%").Replace("_", "!_").Replace("[", "![");

		public async Task<List<LearningRecord>> QueryAsync(LearningRecordQuery query)
		{
			if (query == null || query.DepartmentId <= 0)
				return new List<LearningRecord>();

			var parameters = new DynamicParameters();
			var where = Where(query, parameters);
			parameters.Add("Skip", Math.Max(0, query.Skip));
			parameters.Add("Take", Math.Clamp(query.Take <= 0 ? MaxTake : query.Take, 1, MaxTake));
			var order = query.OldestSubmittedFirst
				? $"ORDER BY COALESCE({Col("SubmittedOn")}, {Col("CreatedOn")}) ASC, {Col("LearningRecordId")} ASC"
				: $"ORDER BY {Col("CompletedOn")} DESC, {Col("LearningRecordId")} DESC";
			var sql = IsPostgres
				? $"SELECT * FROM {Tbl("LearningRecords")} WHERE {where} {order} LIMIT {P}Take OFFSET {P}Skip"
				: $"SELECT * FROM {Tbl("LearningRecords")} WHERE {where} {order} OFFSET {P}Skip ROWS FETCH NEXT {P}Take ROWS ONLY";
			return (await QueryAsync<LearningRecord>(sql, parameters)).ToList();
		}

		public Task<int> CountAsync(LearningRecordQuery query)
		{
			if (query == null || query.DepartmentId <= 0)
				return Task.FromResult(0);

			var parameters = new DynamicParameters();
			var where = Where(query, parameters);
			return ScalarAsync<int>($"SELECT COUNT(*) FROM {Tbl("LearningRecords")} WHERE {where}", parameters);
		}

		public async Task<List<LearningRecord>> GetForMemberInWindowAsync(int departmentId, string userId, DateTime fromUtc, DateTime toUtc)
		{
			var parameters = new DynamicParameters();
			parameters.Add("DepartmentId", departmentId);
			parameters.Add("UserId", userId, System.Data.DbType.String);
			parameters.Add("FromOn", DatabaseTimestamp(fromUtc), System.Data.DbType.DateTime2);
			parameters.Add("ToOn", DatabaseTimestamp(toUtc), System.Data.DbType.DateTime2);
			return (await QueryAsync<LearningRecord>(
				$"SELECT * FROM {Tbl("LearningRecords")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("UserId")} = {CaseInsensitive("UserId")} " +
				$"AND {Col("Status")} <> {Voided} AND {Col("CompletedOn")} >= {P}FromOn AND {Col("CompletedOn")} < {P}ToOn ORDER BY {Col("CompletedOn")}",
				parameters)).ToList();
		}

		public async Task<(LearningRecord Record, bool Inserted)> InsertIfAbsentAsync(LearningRecord record, CancellationToken cancellationToken = default)
		{
			if (record == null || record.DepartmentId <= 0 || string.IsNullOrWhiteSpace(record.UserId) || string.IsNullOrWhiteSpace(record.SourceRefId))
				throw new ArgumentException("A learning record needs a department, a member and a source.", nameof(record));

			record.ComponentKey ??= string.Empty;
			if (record.CreatedOn == default)
				record.CreatedOn = DateTime.UtcNow;

			var parameters = LearningRecordColumns.Parameters(record, LearningRecordColumns.Insert, DatabaseTimestamp);
			var values = string.Join(", ", LearningRecordColumns.Insert.Select(c => P + c));
			var key = $"{Col("DepartmentId")} = {P}DepartmentId AND {Col("SourceKind")} = {P}SourceKind AND {Col("SourceRefId")} = {P}SourceRefId " +
				$"AND {Col("UserId")} = {CaseInsensitive("UserId")} AND {Col("ComponentKey")} = {P}ComponentKey AND {Col("Status")} <> {Voided}";

			// The idempotency key (UX_LearningRecords_Key, filtered on records that are not voided): an existing record wins and
			// nothing is inserted, so a re-save, a double submit or a racing backfill never duplicates (R-T1).
			var sql = IsPostgres
				? $"INSERT INTO {Tbl("LearningRecords")} ({Cols(LearningRecordColumns.Insert)}) SELECT {values} " +
				  $"WHERE NOT EXISTS (SELECT 1 FROM {Tbl("LearningRecords")} WHERE {key}) RETURNING {Col("LearningRecordId")}"
				: $"INSERT INTO {Tbl("LearningRecords")} ({Cols(LearningRecordColumns.Insert)}) OUTPUT INSERTED.{Col("LearningRecordId")} SELECT {values} " +
				  $"WHERE NOT EXISTS (SELECT 1 FROM {Tbl("LearningRecords")} WITH (UPDLOCK, HOLDLOCK) WHERE {key})";

			int? id;
			try
			{
				id = await ScalarAsync<int?>(sql, parameters, cancellationToken);
			}
			catch (Exception ex) when (IsUniqueViolation(ex))
			{
				id = null;
			}

			if (id.HasValue && id.Value > 0)
			{
				record.LearningRecordId = id.Value;
				return (record, true);
			}

			var existing = await QueryFirstOrDefaultAsync<LearningRecord>(
				$"SELECT * FROM {Tbl("LearningRecords")} WHERE {key}", parameters, cancellationToken);
			return (existing, false);
		}

		public async Task<bool> VoidAsync(int departmentId, int learningRecordId, string reason, string userId, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var parameters = new DynamicParameters();
			parameters.Add("DepartmentId", departmentId);
			parameters.Add("Id", learningRecordId);
			parameters.Add("Reason", Trim(reason, 500), System.Data.DbType.String);
			parameters.Add("UserId", userId, System.Data.DbType.String);
			parameters.Add("Now", DatabaseTimestamp(utcNow), System.Data.DbType.DateTime2);
			return await ExecuteAsync(
				$"UPDATE {Tbl("LearningRecords")} SET {Col("Status")} = {Voided}, {Col("VoidReason")} = {P}Reason, {Col("VoidedOn")} = {P}Now, {Col("VoidedByUserId")} = {P}UserId " +
				$"WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LearningRecordId")} = {P}Id AND {Col("Status")} <> {Voided}",
				parameters, cancellationToken) == 1;
		}

		public Task<int> UpdateColumnsAsync(LearningRecord record, IReadOnlyCollection<string> columns, CancellationToken cancellationToken = default)
		{
			if (record == null || record.LearningRecordId <= 0 || columns == null || columns.Count == 0)
				return Task.FromResult(0);

			var list = columns.Distinct(StringComparer.Ordinal).ToList();
			foreach (var column in list)
				if (!LearningRecordColumns.Updatable.Contains(column))
					throw new ArgumentException("Not an updatable learning record column: " + column, nameof(columns));

			var parameters = LearningRecordColumns.Parameters(record, list, DatabaseTimestamp);
			parameters.Add("LearningRecordIdKey", record.LearningRecordId);
			parameters.Add("DepartmentIdKey", record.DepartmentId);
			var sets = string.Join(", ", list.Select(c => $"{Col(c)} = {P}{c}"));
			return ExecuteAsync($"UPDATE {Tbl("LearningRecords")} SET {sets} WHERE {Col("LearningRecordId")} = {P}LearningRecordIdKey AND {Col("DepartmentId")} = {P}DepartmentIdKey",
				parameters, cancellationToken);
		}

		public async Task<int> SetCategoryAsync(int departmentId, IEnumerable<int> learningRecordIds, int? categoryId, CancellationToken cancellationToken = default)
		{
			var ids = InListValue(learningRecordIds).Distinct().ToArray();
			var count = 0;
			foreach (var chunk in ids.Chunk(1000))
			{
				var parameters = new DynamicParameters();
				parameters.Add("DepartmentId", departmentId);
				parameters.Add("CategoryId", categoryId, System.Data.DbType.Int32);
				parameters.Add("Ids", chunk);
				count += await ExecuteAsync(
					$"UPDATE {Tbl("LearningRecords")} SET {Col("CategoryId")} = {P}CategoryId WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("Status")} <> {Voided} " +
					$"AND {InList("LearningRecordId", "Ids")}", parameters, cancellationToken);
			}

			return count;
		}

		public async Task<List<LearningRecordGroup>> GetUncategorizedGroupsAsync(int departmentId, DateTime? fromUtc, DateTime? toUtc)
		{
			var parameters = new DynamicParameters();
			parameters.Add("DepartmentId", departmentId);
			var window = string.Empty;
			if (fromUtc.HasValue)
			{
				window += $" AND {Col("CompletedOn")} >= {P}FromOn";
				parameters.Add("FromOn", DatabaseTimestamp(fromUtc.Value), System.Data.DbType.DateTime2);
			}

			if (toUtc.HasValue)
			{
				window += $" AND {Col("CompletedOn")} < {P}ToOn";
				parameters.Add("ToOn", DatabaseTimestamp(toUtc.Value), System.Data.DbType.DateTime2);
			}

			var code = $"COALESCE({Col("CourseCode")}, '')";
			return (await QueryAsync<LearningRecordGroup>(
				$"SELECT {Col("Title")} AS Title, {code} AS CourseCode, COUNT(*) AS Count, SUM({Col("Hours")}) AS Hours, MIN({Col("CompletedOn")}) AS FirstOn, " +
				$"MAX({Col("CompletedOn")}) AS LastOn FROM {Tbl("LearningRecords")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("CategoryId")} IS NULL " +
				$"AND {Col("Status")} <> {Voided}{window} GROUP BY {Col("Title")}, {code} ORDER BY COUNT(*) DESC, {Col("Title")}",
				parameters)).ToList();
		}

		public Task<int> CountUnresolvedDuplicatesAsync(int departmentId, DateTime? fromUtc, DateTime? toUtc)
		{
			return CountAsync(new LearningRecordQuery { DepartmentId = departmentId, From = fromUtc, To = toUtc, UnresolvedDuplicatesOnly = true });
		}

		public Task<LearningRecord> GetByVerificationCodeAsync(string verificationCode)
		{
			if (string.IsNullOrWhiteSpace(verificationCode))
				return Task.FromResult<LearningRecord>(null);

			return QueryFirstOrDefaultAsync<LearningRecord>(
				$"SELECT * FROM {Tbl("LearningRecords")} WHERE {Col("VerificationCode")} = {P}Code",
				new { Code = verificationCode.Trim().ToUpperInvariant() });
		}

		public async Task<List<int>> GetDepartmentIdsAwaitingReviewAsync(int take)
		{
			var where = $"{Col("SourceKind")} = {(int)LearningRecordSourceKinds.ExternalSubmission} AND {Col("Status")} = {(int)LearningRecordStatuses.Pending} " +
				$"AND ({Col("ReviewState")} IS NULL OR {Col("ReviewState")} = {(int)TrainingSubmissionReviewStates.AwaitingReview})";
			var sql = IsPostgres
				? $"SELECT DISTINCT {Col("DepartmentId")} FROM {Tbl("LearningRecords")} WHERE {where} ORDER BY {Col("DepartmentId")} LIMIT {P}Take"
				: $"SELECT DISTINCT TOP ({P}Take) {Col("DepartmentId")} FROM {Tbl("LearningRecords")} WHERE {where} ORDER BY {Col("DepartmentId")}";
			return (await QueryAsync<int>(sql, new { Take = Math.Clamp(take, 1, 10000) })).ToList();
		}

		private static string Trim(string value, int max)
		{
			var trimmed = value?.Trim();
			return string.IsNullOrEmpty(trimmed) ? null : trimmed.Length > max ? trimmed.Substring(0, max) : trimmed;
		}
	}

	/// <summary>Evidence files of external submissions.</summary>
	public class LearningRecordFilesRepository : RmsRepositoryBase<LearningRecordFile>, ILearningRecordFilesRepository
	{
		/// <summary>Round 9B: every column except the bytes (lists and exports never read them).</summary>
		private static readonly string[] MetadataColumns =
		{
			"LearningRecordFileId", "DepartmentId", "UserId", "FileName", "ContentType", "UploadedOn", "UploadedByUserId", "LearningRecordId", "SizeBytes", "Sha256",
			"EvidenceKind", "ScanState"
		};

		public LearningRecordFilesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<LearningRecordFile> GetWithDataAsync(int departmentId, int learningRecordFileId) =>
			QueryFirstOrDefaultAsync<LearningRecordFile>(
				$"SELECT * FROM {Tbl("LearningRecordFiles")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LearningRecordFileId")} = {P}Id",
				new { DepartmentId = departmentId, Id = learningRecordFileId });

		public async Task<List<LearningRecordFile>> GetForRecordsAsync(int departmentId, IEnumerable<int> learningRecordIds)
		{
			var ids = InListValue(learningRecordIds).Where(i => i > 0).Distinct().ToArray();
			var result = new List<LearningRecordFile>();
			foreach (var chunk in ids.Chunk(1000))
			{
				// Linked files, and the single evidence file a record names (a path that does not link its file, such as an LMS training request).
				result.AddRange(await QueryAsync<LearningRecordFile>(
					$"SELECT {Cols(MetadataColumns)} FROM {Tbl("LearningRecordFiles")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND ({InList("LearningRecordId", "Ids")} " +
					$"OR {Col("LearningRecordFileId")} IN (SELECT {Col("EvidenceFileId")} FROM {Tbl("LearningRecords")} WHERE {Col("DepartmentId")} = {P}DepartmentId " +
					$"AND {Col("EvidenceFileId")} IS NOT NULL AND {InList("LearningRecordId", "Ids")}))",
					new { DepartmentId = departmentId, Ids = chunk }));
			}

			return result.GroupBy(f => f.LearningRecordFileId).Select(g => g.First()).OrderBy(f => f.UploadedOn).ThenBy(f => f.LearningRecordFileId).ToList();
		}

		public async Task<List<LearningRecordFile>> GetForUserAsync(int departmentId, string userId)
		{
			if (string.IsNullOrWhiteSpace(userId))
				return new List<LearningRecordFile>();

			var parameters = new DynamicParameters();
			parameters.Add("DepartmentId", departmentId);
			parameters.Add("UserId", userId, System.Data.DbType.String);
			return (await QueryAsync<LearningRecordFile>(
				$"SELECT {Cols(MetadataColumns)} FROM {Tbl("LearningRecordFiles")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("UserId")} = {CaseInsensitive("UserId")} " +
				$"ORDER BY {Col("UploadedOn")}, {Col("LearningRecordFileId")}", parameters)).ToList();
		}

		public async Task<int> LinkToRecordAsync(int departmentId, IEnumerable<int> learningRecordFileIds, int learningRecordId, CancellationToken cancellationToken = default)
		{
			var ids = InListValue(learningRecordFileIds).Where(i => i > 0).Distinct().ToArray();
			if (ids.Length == 0 || learningRecordId <= 0)
				return 0;

			var count = 0;
			foreach (var chunk in ids.Chunk(1000))
				count += await ExecuteAsync(
					$"UPDATE {Tbl("LearningRecordFiles")} SET {Col("LearningRecordId")} = {P}RecordId WHERE {Col("DepartmentId")} = {P}DepartmentId " +
					$"AND {Col("LearningRecordId")} IS NULL AND {InList("LearningRecordFileId", "Ids")}",
					new { DepartmentId = departmentId, RecordId = learningRecordId, Ids = chunk }, cancellationToken);
			return count;
		}
	}

	/// <summary>Round 9B: the review history of member submissions (M0409).</summary>
	public class LearningRecordReviewEventsRepository : RmsRepositoryBase<LearningRecordReviewEvent>, ILearningRecordReviewEventsRepository
	{
		public LearningRecordReviewEventsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<List<LearningRecordReviewEvent>> GetForRecordAsync(int departmentId, int learningRecordId) =>
			(await QueryAsync<LearningRecordReviewEvent>(
				$"SELECT * FROM {Tbl("LearningRecordReviewEvents")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LearningRecordId")} = {P}RecordId " +
				$"ORDER BY {Col("CreatedOn")}, {Col("LearningRecordReviewEventId")}",
				new { DepartmentId = departmentId, RecordId = learningRecordId })).ToList();

		public async Task<List<LearningRecordReviewEvent>> GetForUserAsync(int departmentId, string userId)
		{
			if (string.IsNullOrWhiteSpace(userId))
				return new List<LearningRecordReviewEvent>();

			var parameters = new DynamicParameters();
			parameters.Add("DepartmentId", departmentId);
			parameters.Add("UserId", userId, System.Data.DbType.String);
			return (await QueryAsync<LearningRecordReviewEvent>(
				$"SELECT * FROM {Tbl("LearningRecordReviewEvents")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("UserId")} = {CaseInsensitive("UserId")} " +
				$"ORDER BY {Col("CreatedOn")}, {Col("LearningRecordReviewEventId")}", parameters)).ToList();
		}
	}

	/// <summary>Round 9B: the daily reviewer digest claims (M0410), unique per department and department-local day.</summary>
	public class TrainingReviewDigestsRepository : RmsRepositoryBase<TrainingReviewDigest>, ITrainingReviewDigestsRepository
	{
		public TrainingReviewDigestsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<bool> TryClaimAsync(int departmentId, DateTime digestDate, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var parameters = new DynamicParameters();
			parameters.Add("DepartmentId", departmentId);
			parameters.Add("DigestDate", DatabaseTimestamp(digestDate.Date), System.Data.DbType.DateTime2);
			parameters.Add("SentOn", DatabaseTimestamp(utcNow), System.Data.DbType.DateTime2);
			var lockHint = IsPostgres ? string.Empty : " WITH (UPDLOCK, HOLDLOCK)";
			var sql = $"INSERT INTO {Tbl("TrainingReviewDigests")} ({Cols("DepartmentId", "DigestDate", "Reviewers", "Submissions", "SentOn")}) " +
				$"SELECT {P}DepartmentId, {P}DigestDate, 0, 0, {P}SentOn WHERE NOT EXISTS (SELECT 1 FROM {Tbl("TrainingReviewDigests")}{lockHint} " +
				$"WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("DigestDate")} = {P}DigestDate)";
			try
			{
				return await ExecuteAsync(sql, parameters, cancellationToken) == 1;
			}
			catch (Exception ex) when (IsUniqueViolation(ex))
			{
				// The unique (DepartmentId, DigestDate) index: another worker claimed the same day first.
				return false;
			}
		}

		public Task<int> SaveCountsAsync(int departmentId, DateTime digestDate, int reviewers, int submissions, CancellationToken cancellationToken = default)
		{
			var parameters = new DynamicParameters();
			parameters.Add("DepartmentId", departmentId);
			parameters.Add("DigestDate", DatabaseTimestamp(digestDate.Date), System.Data.DbType.DateTime2);
			parameters.Add("Reviewers", reviewers);
			parameters.Add("Submissions", submissions);
			return ExecuteAsync(
				$"UPDATE {Tbl("TrainingReviewDigests")} SET {Col("Reviewers")} = {P}Reviewers, {Col("Submissions")} = {P}Submissions " +
				$"WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("DigestDate")} = {P}DigestDate", parameters, cancellationToken);
		}
	}

	public class TrainingCategoriesRepository : RmsRepositoryBase<TrainingCategory>, ITrainingCategoriesRepository
	{
		public TrainingCategoriesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<List<TrainingCategory>> GetForDepartmentAsync(int departmentId, bool includeDeleted)
		{
			var live = includeDeleted ? string.Empty : $" AND {Col("IsDeleted")} = {(IsPostgres ? "FALSE" : "0")}";
			return (await QueryAsync<TrainingCategory>(
				$"SELECT * FROM {Tbl("TrainingCategories")} WHERE {Col("DepartmentId")} = {P}DepartmentId{live} ORDER BY {Col("SortOrder")}, {Col("Name")}",
				new { DepartmentId = departmentId })).ToList();
		}
	}

	public class CategoryCreditMappingsRepository : RmsRepositoryBase<CategoryCreditMapping>, ICategoryCreditMappingsRepository
	{
		public CategoryCreditMappingsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<List<CategoryCreditMapping>> GetForDepartmentAsync(int departmentId)
		{
			return (await QueryAsync<CategoryCreditMapping>(
				$"SELECT * FROM {Tbl("CategoryCreditMappings")} WHERE {Col("DepartmentId")} = {P}DepartmentId ORDER BY {Col("DepartmentCertificationTypeId")}, {Col("CategoryId")}",
				new { DepartmentId = departmentId })).ToList();
		}

		public async Task<List<CategoryCreditMapping>> GetForCertificationTypeAsync(int departmentId, int departmentCertificationTypeId)
		{
			return (await QueryAsync<CategoryCreditMapping>(
				$"SELECT * FROM {Tbl("CategoryCreditMappings")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("DepartmentCertificationTypeId")} = {P}TypeId",
				new { DepartmentId = departmentId, TypeId = departmentCertificationTypeId })).ToList();
		}
	}

	public class LearningRecordBackfillsRepository : RmsRepositoryBase<LearningRecordBackfill>, ILearningRecordBackfillsRepository
	{
		private static readonly string[] ProgressColumns =
		{
			"State", "StartedOn", "CompletedOn", "ResumeAfter", "CompletionsFound", "TrainingRecordsFound", "RecordsWritten", "RecordsSkipped", "Attempts", "Error"
		};

		public LearningRecordBackfillsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<LearningRecordBackfill> GetLatestForDepartmentAsync(int departmentId)
		{
			var sql = IsPostgres
				? $"SELECT * FROM {Tbl("LearningRecordBackfills")} WHERE {Col("DepartmentId")} = {P}DepartmentId ORDER BY {Col("LearningRecordBackfillId")} DESC LIMIT 1"
				: $"SELECT TOP (1) * FROM {Tbl("LearningRecordBackfills")} WHERE {Col("DepartmentId")} = {P}DepartmentId ORDER BY {Col("LearningRecordBackfillId")} DESC";
			return QueryFirstOrDefaultAsync<LearningRecordBackfill>(sql, new { DepartmentId = departmentId });
		}

		public async Task<List<LearningRecordBackfill>> GetRunnableAsync(DateTime staleBeforeUtc, int take)
		{
			var parameters = new DynamicParameters();
			parameters.Add("Stale", DatabaseTimestamp(staleBeforeUtc), System.Data.DbType.DateTime2);
			parameters.Add("Take", Math.Max(1, take));
			var where = $"{Col("State")} = {(int)LearningRecordBackfillStates.Queued} OR ({Col("State")} = {(int)LearningRecordBackfillStates.Running} AND {Col("StartedOn")} < {P}Stale)";
			var sql = IsPostgres
				? $"SELECT * FROM {Tbl("LearningRecordBackfills")} WHERE {where} ORDER BY {Col("LearningRecordBackfillId")} LIMIT {P}Take"
				: $"SELECT TOP ({P}Take) * FROM {Tbl("LearningRecordBackfills")} WHERE {where} ORDER BY {Col("LearningRecordBackfillId")}";
			return (await QueryAsync<LearningRecordBackfill>(sql, parameters)).ToList();
		}

		public async Task<bool> ClaimAsync(int learningRecordBackfillId, int expectedState, DateTime? expectedStartedOn, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var parameters = new DynamicParameters();
			parameters.Add("Id", learningRecordBackfillId);
			parameters.Add("Expected", expectedState);
			parameters.Add("Now", DatabaseTimestamp(utcNow), System.Data.DbType.DateTime2);
			parameters.Add("Running", (int)LearningRecordBackfillStates.Running);
			// The null case is built in C#: an untyped null parameter in "IS NULL" fails on PostgreSQL (42P08).
			string started;
			if (expectedStartedOn.HasValue)
			{
				started = $"{Col("StartedOn")} = {P}ExpectedStarted";
				parameters.Add("ExpectedStarted", DatabaseTimestamp(expectedStartedOn.Value), System.Data.DbType.DateTime2);
			}
			else
			{
				started = $"{Col("StartedOn")} IS NULL";
			}

			return await ExecuteAsync(
				$"UPDATE {Tbl("LearningRecordBackfills")} SET {Col("State")} = {P}Running, {Col("StartedOn")} = {P}Now, {Col("Attempts")} = {Col("Attempts")} + 1 " +
				$"WHERE {Col("LearningRecordBackfillId")} = {P}Id AND {Col("State")} = {P}Expected AND {started}", parameters, cancellationToken) == 1;
		}

		public Task<int> SaveProgressAsync(LearningRecordBackfill backfill, CancellationToken cancellationToken = default)
		{
			if (backfill == null || backfill.LearningRecordBackfillId <= 0)
				return Task.FromResult(0);

			var parameters = LearningRecordColumns.Parameters(backfill, ProgressColumns, DatabaseTimestamp);
			parameters.Add("IdKey", backfill.LearningRecordBackfillId);
			var sets = string.Join(", ", ProgressColumns.Select(c => $"{Col(c)} = {P}{c}"));
			return ExecuteAsync($"UPDATE {Tbl("LearningRecordBackfills")} SET {sets} WHERE {Col("LearningRecordBackfillId")} = {P}IdKey", parameters, cancellationToken);
		}
	}

	/// <summary>LMS (dark): custom certificate templates.</summary>
	public class LmsCertificateTemplatesRepository : RmsRepositoryBase<LmsCertificateTemplate>, ILmsCertificateTemplatesRepository
	{
		public LmsCertificateTemplatesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<List<LmsCertificateTemplate>> GetForDepartmentAsync(int departmentId)
		{
			return (await QueryAsync<LmsCertificateTemplate>(
				$"SELECT * FROM {Tbl("LmsCertificateTemplates")} WHERE {Col("DepartmentId")} = {P}DepartmentId ORDER BY {Col("Name")}",
				new { DepartmentId = departmentId })).ToList();
		}
	}
}
