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
	/// <summary>Signatures (S0, M0341). Every read starts at the department.</summary>
	public class SignatureRecordsRepository : RmsRepositoryBase<SignatureRecord>, ISignatureRecordsRepository
	{
		public SignatureRecordsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<List<SignatureRecord>> GetForSubjectAsync(int departmentId, int subjectKind, string subjectId)
		{
			if (string.IsNullOrWhiteSpace(subjectId))
				return new List<SignatureRecord>();

			return (await QueryAsync<SignatureRecord>(
				$"SELECT * FROM {Tbl("SignatureRecords")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("SubjectKind")} = {P}SubjectKind AND {Col("SubjectId")} = {CaseInsensitive("SubjectId")} " +
				$"ORDER BY {Col("SignedOn")}, {Col("SignatureRecordId")}",
				new { DepartmentId = departmentId, SubjectKind = subjectKind, SubjectId = subjectId })).ToList();
		}

		public async Task<List<SignatureRecord>> GetForSubjectsAsync(int departmentId, int subjectKind, IEnumerable<string> subjectIds)
		{
			var ids = InListValue((subjectIds ?? Enumerable.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase));
			if (ids.Length == 0)
				return new List<SignatureRecord>();

			return (await QueryAsync<SignatureRecord>(
				$"SELECT * FROM {Tbl("SignatureRecords")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("SubjectKind")} = {P}SubjectKind AND {CaseInsensitiveInList("SubjectId", "SubjectIds")} " +
				$"ORDER BY {Col("SignedOn")}, {Col("SignatureRecordId")}",
				new { DepartmentId = departmentId, SubjectKind = subjectKind, SubjectIds = ids })).ToList();
		}

		public Task<SignatureRecord> GetByIdForDepartmentAsync(int departmentId, int signatureRecordId)
		{
			return QueryFirstOrDefaultAsync<SignatureRecord>(
				$"SELECT * FROM {Tbl("SignatureRecords")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("SignatureRecordId")} = {P}Id",
				new { DepartmentId = departmentId, Id = signatureRecordId });
		}
	}

	/// <summary>Library categories (P1, M0342).</summary>
	public class ProtocolCategoriesRepository : RmsRepositoryBase<ProtocolCategory>, IProtocolCategoriesRepository
	{
		public ProtocolCategoriesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<List<ProtocolCategory>> GetForDepartmentAsync(int departmentId)
		{
			return (await QueryAsync<ProtocolCategory>(
				$"SELECT * FROM {Tbl("ProtocolCategories")} WHERE {Col("DepartmentId")} = {P}DepartmentId ORDER BY {Col("SortOrder")}, {Col("Name")}",
				new { DepartmentId = departmentId })).ToList();
		}

		public Task<ProtocolCategory> GetByIdForDepartmentAsync(int departmentId, int protocolCategoryId)
		{
			return QueryFirstOrDefaultAsync<ProtocolCategory>(
				$"SELECT * FROM {Tbl("ProtocolCategories")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("ProtocolCategoryId")} = {P}Id",
				new { DepartmentId = departmentId, Id = protocolCategoryId });
		}
	}

	/// <summary>Protocol versions (P1, M0342). Header reads never select ContentJson or ContentHtml.</summary>
	public class ProtocolVersionsRepository : RmsRepositoryBase<ProtocolVersion>, IProtocolVersionsRepository
	{
		private static readonly string[] HeaderColumns =
		{
			"ProtocolVersionId", "DepartmentId", "DispatchProtocolId", "Major", "Minor", "Status", "EffectiveOn", "ExpiresOn", "ContentHash", "ChangeSummary",
			"RequiresAcknowledgment", "Locale", "CreatedOn", "CreatedByUserId", "UpdatedOn", "UpdatedByUserId", "SubmittedOn", "SubmittedByUserId",
			"ApprovedOn", "ApprovedByUserId", "PublishedOn", "PublishedByUserId", "SupersededOn", "RetiredOn", "RowVersion", "ContentKind", "QuizTrainingId"
		};

		public ProtocolVersionsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		private static string Select(bool withContent) => withContent ? "*" : Cols(HeaderColumns);

		public async Task<List<ProtocolVersion>> GetForProtocolAsync(int departmentId, int protocolId, bool withContent = false)
		{
			return (await QueryAsync<ProtocolVersion>(
				$"SELECT {Select(withContent)} FROM {Tbl("ProtocolVersions")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("DispatchProtocolId")} = {P}ProtocolId " +
				$"ORDER BY {Col("ProtocolVersionId")} DESC",
				new { DepartmentId = departmentId, ProtocolId = protocolId })).ToList();
		}

		public Task<ProtocolVersion> GetByIdForDepartmentAsync(int departmentId, int protocolVersionId)
		{
			return QueryFirstOrDefaultAsync<ProtocolVersion>(
				$"SELECT * FROM {Tbl("ProtocolVersions")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("ProtocolVersionId")} = {P}Id",
				new { DepartmentId = departmentId, Id = protocolVersionId });
		}

		public async Task<List<ProtocolVersion>> GetByIdsAsync(int departmentId, IEnumerable<int> protocolVersionIds, bool withContent = false)
		{
			var ids = InListValue((protocolVersionIds ?? Enumerable.Empty<int>()).Where(x => x > 0).Distinct());
			if (ids.Length == 0)
				return new List<ProtocolVersion>();

			return (await QueryAsync<ProtocolVersion>(
				$"SELECT {Select(withContent)} FROM {Tbl("ProtocolVersions")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {InList("ProtocolVersionId", "Ids")}",
				new { DepartmentId = departmentId, Ids = ids })).ToList();
		}

		public async Task<List<ProtocolVersion>> GetHeadersForDepartmentAsync(int departmentId)
		{
			return (await QueryAsync<ProtocolVersion>(
				$"SELECT {Select(false)} FROM {Tbl("ProtocolVersions")} WHERE {Col("DepartmentId")} = {P}DepartmentId ORDER BY {Col("ProtocolVersionId")} DESC",
				new { DepartmentId = departmentId })).ToList();
		}

		public async Task<bool> TryUpdateAsync(ProtocolVersion version, long expectedRowVersion, CancellationToken cancellationToken = default)
		{
			if (version == null || version.ProtocolVersionId <= 0)
				return false;

			var parameters = new DynamicParameters();
			parameters.Add("Id", version.ProtocolVersionId);
			parameters.Add("DepartmentId", version.DepartmentId);
			parameters.Add("Expected", expectedRowVersion);
			parameters.Add("Next", expectedRowVersion + 1);
			parameters.Add("Major", version.Major);
			parameters.Add("Minor", version.Minor);
			parameters.Add("Status", version.Status);
			parameters.Add("EffectiveOn", Stamp(version.EffectiveOn), System.Data.DbType.DateTime2);
			parameters.Add("ExpiresOn", Stamp(version.ExpiresOn), System.Data.DbType.DateTime2);
			parameters.Add("ContentJson", version.ContentJson, System.Data.DbType.String);
			parameters.Add("ContentHtml", version.ContentHtml, System.Data.DbType.String);
			parameters.Add("ContentHash", version.ContentHash, System.Data.DbType.String);
			parameters.Add("ChangeSummary", version.ChangeSummary, System.Data.DbType.String);
			parameters.Add("RequiresAcknowledgment", version.RequiresAcknowledgment);
			parameters.Add("Locale", version.Locale, System.Data.DbType.String);
			parameters.Add("UpdatedOn", Stamp(version.UpdatedOn), System.Data.DbType.DateTime2);
			parameters.Add("UpdatedByUserId", version.UpdatedByUserId, System.Data.DbType.String);
			parameters.Add("SubmittedOn", Stamp(version.SubmittedOn), System.Data.DbType.DateTime2);
			parameters.Add("SubmittedByUserId", version.SubmittedByUserId, System.Data.DbType.String);
			parameters.Add("ApprovedOn", Stamp(version.ApprovedOn), System.Data.DbType.DateTime2);
			parameters.Add("ApprovedByUserId", version.ApprovedByUserId, System.Data.DbType.String);
			parameters.Add("PublishedOn", Stamp(version.PublishedOn), System.Data.DbType.DateTime2);
			parameters.Add("PublishedByUserId", version.PublishedByUserId, System.Data.DbType.String);
			parameters.Add("SupersededOn", Stamp(version.SupersededOn), System.Data.DbType.DateTime2);
			parameters.Add("RetiredOn", Stamp(version.RetiredOn), System.Data.DbType.DateTime2);
			parameters.Add("ContentKind", version.ContentKind);
			parameters.Add("QuizTrainingId", version.QuizTrainingId, System.Data.DbType.Int32);

			var set = string.Join(", ", new[]
			{
				"Major", "Minor", "Status", "EffectiveOn", "ExpiresOn", "ContentJson", "ContentHtml", "ContentHash", "ChangeSummary", "RequiresAcknowledgment", "Locale",
				"UpdatedOn", "UpdatedByUserId", "SubmittedOn", "SubmittedByUserId", "ApprovedOn", "ApprovedByUserId", "PublishedOn", "PublishedByUserId",
				"SupersededOn", "RetiredOn", "ContentKind", "QuizTrainingId"
			}.Select(c => $"{Col(c)} = {P}{c}"));

			var changed = await ExecuteAsync(
				$"UPDATE {Tbl("ProtocolVersions")} SET {set}, {Col("RowVersion")} = {P}Next " +
				$"WHERE {Col("ProtocolVersionId")} = {P}Id AND {Col("DepartmentId")} = {P}DepartmentId AND {Col("RowVersion")} = {P}Expected",
				parameters, cancellationToken);

			if (changed == 1)
				version.RowVersion = expectedRowVersion + 1;

			return changed == 1;
		}

		public async Task<List<ProtocolVersion>> GetDueActivationsAsync(DateTime utcNow, int take)
		{
			return (await QueryAsync<ProtocolVersion>(
				$"SELECT {Select(false)} FROM {Tbl("ProtocolVersions")} WHERE {Col("Status")} = {(int)ProtocolVersionStatuses.Approved} AND {Col("EffectiveOn")} IS NOT NULL " +
				$"AND {Col("EffectiveOn")} <= {P}Now ORDER BY {Col("EffectiveOn")}, {Col("ProtocolVersionId")} {Take()}",
				new { Now = DatabaseTimestamp(utcNow), Take = Math.Max(1, take) })).ToList();
		}

		public async Task<List<ProtocolVersion>> GetDueExpirationsAsync(DateTime utcNow, int take)
		{
			return (await QueryAsync<ProtocolVersion>(
				$"SELECT {Select(false)} FROM {Tbl("ProtocolVersions")} WHERE {Col("Status")} = {(int)ProtocolVersionStatuses.Published} AND {Col("ExpiresOn")} IS NOT NULL " +
				$"AND {Col("ExpiresOn")} <= {P}Now ORDER BY {Col("ExpiresOn")}, {Col("ProtocolVersionId")} {Take()}",
				new { Now = DatabaseTimestamp(utcNow), Take = Math.Max(1, take) })).ToList();
		}

		public async Task<List<ProtocolVersion>> GetPurgeCandidatesAsync(int departmentId, DateTime endedBeforeUtc, int take)
		{
			var hold = IsPostgres ? "false" : "0";
			return (await QueryAsync<ProtocolVersion>(
				$"SELECT {string.Join(", ", HeaderColumns.Select(c => "v." + Col(c)))} FROM {Tbl("ProtocolVersions")} v " +
				$"JOIN {Tbl("DispatchProtocols")} p ON p.{Col("DispatchProtocolId")} = v.{Col("DispatchProtocolId")} " +
				$"WHERE v.{Col("DepartmentId")} = {P}DepartmentId AND p.{Col("DepartmentId")} = {P}DepartmentId " +
				$"AND v.{Col("Status")} IN ({(int)ProtocolVersionStatuses.Superseded}, {(int)ProtocolVersionStatuses.Retired}) " +
				$"AND COALESCE(v.{Col("SupersededOn")}, v.{Col("RetiredOn")}) IS NOT NULL AND COALESCE(v.{Col("SupersededOn")}, v.{Col("RetiredOn")}) < {P}Before " +
				$"AND p.{Col("LegalHold")} = {hold} AND (p.{Col("CurrentVersionId")} IS NULL OR p.{Col("CurrentVersionId")} <> v.{Col("ProtocolVersionId")}) " +
				$"AND NOT EXISTS (SELECT 1 FROM {Tbl("CallProtocols")} c WHERE c.{Col("ProtocolVersionId")} = v.{Col("ProtocolVersionId")}) " +
				$"ORDER BY v.{Col("ProtocolVersionId")} {Take()}",
				new { DepartmentId = departmentId, Before = DatabaseTimestamp(endedBeforeUtc), Take = Math.Max(1, take) })).ToList();
		}

		public async Task<int> PurgeAsync(int departmentId, int protocolVersionId, CancellationToken cancellationToken = default)
		{
			var owns = UnitOfWork.Transaction == null;
			UnitOfWork.CreateOrGetConnection();
			try
			{
				var key = new { DepartmentId = departmentId, Id = protocolVersionId };
				// What hangs off the version goes with it (R-P10: retention, never a user action), under the same guard as the version
				// itself so a version that is not purgeable keeps everything: acknowledgments (P2), the primary file and its text (M0347).
				var purgeable = $"EXISTS (SELECT 1 FROM {Tbl("ProtocolVersions")} pv WHERE pv.{Col("ProtocolVersionId")} = {P}Id AND pv.{Col("DepartmentId")} = {P}DepartmentId " +
					$"AND pv.{Col("Status")} IN ({(int)ProtocolVersionStatuses.Superseded}, {(int)ProtocolVersionStatuses.Retired}) " +
					$"AND NOT EXISTS (SELECT 1 FROM {Tbl("CallProtocols")} c WHERE c.{Col("ProtocolVersionId")} = {P}Id))";
				await ExecuteAsync($"DELETE FROM {Tbl("ProtocolAcknowledgments")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("ProtocolVersionId")} = {P}Id AND {purgeable}", key, cancellationToken);
				await ExecuteAsync($"DELETE FROM {Tbl("ProtocolFileTextPages")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("ProtocolFileTextId")} IN " +
					$"(SELECT t.{Col("ProtocolFileTextId")} FROM {Tbl("ProtocolFileTexts")} t WHERE t.{Col("DepartmentId")} = {P}DepartmentId " +
					$"AND t.{Col("SourceKind")} = {(int)ProtocolFileSourceKinds.VersionFile} AND t.{Col("ProtocolVersionId")} = {P}Id) AND {purgeable}", key, cancellationToken);
				await ExecuteAsync($"DELETE FROM {Tbl("ProtocolFileTexts")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("SourceKind")} = {(int)ProtocolFileSourceKinds.VersionFile} " +
					$"AND {Col("ProtocolVersionId")} = {P}Id AND {purgeable}", key, cancellationToken);
				await ExecuteAsync($"DELETE FROM {Tbl("ProtocolVersionFiles")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("ProtocolVersionId")} = {P}Id AND {purgeable}", key, cancellationToken);
				// P3 (M0362): the version's translations go with it (a schema without the table has none).
				if (await HasTableAsync("ProtocolVersionTranslations", cancellationToken))
					await ExecuteAsync($"DELETE FROM {Tbl("ProtocolVersionTranslations")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("ProtocolVersionId")} = {P}Id AND {purgeable}", key, cancellationToken);
				await ExecuteAsync($"DELETE FROM {Tbl("ProtocolVersionApprovals")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("ProtocolVersionId")} = {P}Id", key, cancellationToken);
				await ExecuteAsync($"DELETE FROM {Tbl("ProtocolComments")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("ProtocolVersionId")} = {P}Id", key, cancellationToken);
				var removed = await ExecuteAsync(
					$"DELETE FROM {Tbl("ProtocolVersions")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("ProtocolVersionId")} = {P}Id " +
					$"AND {Col("Status")} IN ({(int)ProtocolVersionStatuses.Superseded}, {(int)ProtocolVersionStatuses.Retired}) " +
					$"AND NOT EXISTS (SELECT 1 FROM {Tbl("CallProtocols")} c WHERE c.{Col("ProtocolVersionId")} = {P}Id)", key, cancellationToken);
				if (owns) UnitOfWork.CommitChanges();
				return removed;
			}
			catch
			{
				if (owns) UnitOfWork.DiscardChanges();
				throw;
			}
		}

		private static DateTime? Stamp(DateTime? value) => value.HasValue ? DatabaseTimestamp(value.Value) : (DateTime?)null;

		private static string Take() => IsPostgres ? "LIMIT @Take" : "OFFSET 0 ROWS FETCH NEXT @Take ROWS ONLY";
	}

	public class ProtocolVersionApprovalsRepository : RmsRepositoryBase<ProtocolVersionApproval>, IProtocolVersionApprovalsRepository
	{
		public ProtocolVersionApprovalsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<List<ProtocolVersionApproval>> GetForVersionAsync(int departmentId, int protocolVersionId)
		{
			return (await QueryAsync<ProtocolVersionApproval>(
				$"SELECT * FROM {Tbl("ProtocolVersionApprovals")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("ProtocolVersionId")} = {P}Id " +
				$"ORDER BY {Col("Round")}, {Col("StepOrder")}, {Col("ProtocolVersionApprovalId")}",
				new { DepartmentId = departmentId, Id = protocolVersionId })).ToList();
		}

		public async Task<List<ProtocolVersionApproval>> GetForVersionsAsync(int departmentId, IEnumerable<int> protocolVersionIds)
		{
			var ids = InListValue((protocolVersionIds ?? Enumerable.Empty<int>()).Where(x => x > 0).Distinct());
			if (ids.Length == 0)
				return new List<ProtocolVersionApproval>();

			return (await QueryAsync<ProtocolVersionApproval>(
				$"SELECT * FROM {Tbl("ProtocolVersionApprovals")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {InList("ProtocolVersionId", "Ids")} " +
				$"ORDER BY {Col("ProtocolVersionId")}, {Col("Round")}, {Col("StepOrder")}",
				new { DepartmentId = departmentId, Ids = ids })).ToList();
		}

		public async Task<List<ProtocolVersionApproval>> GetPendingForDepartmentAsync(int departmentId)
		{
			return (await QueryAsync<ProtocolVersionApproval>(
				$"SELECT * FROM {Tbl("ProtocolVersionApprovals")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("Decision")} = {(int)ProtocolApprovalDecisions.Pending} " +
				$"ORDER BY {Col("ProtocolVersionId")}, {Col("Round")}, {Col("StepOrder")}",
				new { DepartmentId = departmentId })).ToList();
		}

		public async Task<bool> TryDecideAsync(ProtocolVersionApproval approval, CancellationToken cancellationToken = default)
		{
			if (approval == null || approval.ProtocolVersionApprovalId <= 0)
				return false;

			var parameters = new DynamicParameters();
			parameters.Add("Id", approval.ProtocolVersionApprovalId);
			parameters.Add("DepartmentId", approval.DepartmentId);
			parameters.Add("Decision", approval.Decision);
			parameters.Add("DecidedByUserId", approval.DecidedByUserId, System.Data.DbType.String);
			parameters.Add("Comment", approval.Comment, System.Data.DbType.String);
			parameters.Add("DecidedOn", DatabaseTimestamp(approval.DecidedOn ?? DateTime.UtcNow), System.Data.DbType.DateTime2);
			parameters.Add("SignatureRecordId", approval.SignatureRecordId, System.Data.DbType.Int32);

			return await ExecuteAsync(
				$"UPDATE {Tbl("ProtocolVersionApprovals")} SET {Col("Decision")} = {P}Decision, {Col("DecidedByUserId")} = {P}DecidedByUserId, {Col("Comment")} = {P}Comment, " +
				$"{Col("DecidedOn")} = {P}DecidedOn, {Col("SignatureRecordId")} = {P}SignatureRecordId " +
				$"WHERE {Col("ProtocolVersionApprovalId")} = {P}Id AND {Col("DepartmentId")} = {P}DepartmentId AND {Col("Decision")} = {(int)ProtocolApprovalDecisions.Pending}",
				parameters, cancellationToken) == 1;
		}

		public Task<int> WithdrawPendingAsync(int departmentId, int protocolVersionId, int round, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			return ExecuteAsync(
				$"UPDATE {Tbl("ProtocolVersionApprovals")} SET {Col("Decision")} = {(int)ProtocolApprovalDecisions.Withdrawn}, {Col("DecidedOn")} = {P}Now " +
				$"WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("ProtocolVersionId")} = {P}Id AND {Col("Round")} = {P}Round AND {Col("Decision")} = {(int)ProtocolApprovalDecisions.Pending}",
				new { DepartmentId = departmentId, Id = protocolVersionId, Round = round, Now = DatabaseTimestamp(utcNow) }, cancellationToken);
		}
	}

	public class ProtocolAudiencesRepository : RmsRepositoryBase<ProtocolAudience>, IProtocolAudiencesRepository
	{
		public ProtocolAudiencesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<List<ProtocolAudience>> GetForProtocolAsync(int departmentId, int protocolId)
		{
			return (await QueryAsync<ProtocolAudience>(
				$"SELECT * FROM {Tbl("ProtocolAudiences")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("DispatchProtocolId")} = {P}ProtocolId ORDER BY {Col("ProtocolAudienceId")}",
				new { DepartmentId = departmentId, ProtocolId = protocolId })).ToList();
		}

		public async Task<List<ProtocolAudience>> GetForDepartmentAsync(int departmentId)
		{
			return (await QueryAsync<ProtocolAudience>(
				$"SELECT * FROM {Tbl("ProtocolAudiences")} WHERE {Col("DepartmentId")} = {P}DepartmentId ORDER BY {Col("DispatchProtocolId")}, {Col("ProtocolAudienceId")}",
				new { DepartmentId = departmentId })).ToList();
		}

		public async Task ReplaceForProtocolAsync(int departmentId, int protocolId, IReadOnlyCollection<ProtocolAudience> rows, CancellationToken cancellationToken = default)
		{
			var owns = UnitOfWork.Transaction == null;
			UnitOfWork.CreateOrGetConnection();
			try
			{
				await ExecuteAsync($"DELETE FROM {Tbl("ProtocolAudiences")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("DispatchProtocolId")} = {P}ProtocolId",
					new { DepartmentId = departmentId, ProtocolId = protocolId }, cancellationToken);

				foreach (var row in rows ?? new List<ProtocolAudience>())
				{
					var parameters = new DynamicParameters();
					parameters.Add("DepartmentId", departmentId);
					parameters.Add("ProtocolId", protocolId);
					parameters.Add("Kind", row.Kind);
					parameters.Add("RefId", row.RefId, System.Data.DbType.String);
					await ExecuteAsync($"INSERT INTO {Tbl("ProtocolAudiences")} ({Cols("DepartmentId", "DispatchProtocolId", "Kind", "RefId")}) " +
						$"VALUES ({P}DepartmentId, {P}ProtocolId, {P}Kind, {P}RefId)", parameters, cancellationToken);
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

	public class ProtocolCommentsRepository : RmsRepositoryBase<ProtocolComment>, IProtocolCommentsRepository
	{
		public ProtocolCommentsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<List<ProtocolComment>> GetForProtocolAsync(int departmentId, int protocolId)
		{
			return (await QueryAsync<ProtocolComment>(
				$"SELECT * FROM {Tbl("ProtocolComments")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("DispatchProtocolId")} = {P}ProtocolId ORDER BY {Col("CreatedOn")}, {Col("ProtocolCommentId")}",
				new { DepartmentId = departmentId, ProtocolId = protocolId })).ToList();
		}

		public Task<ProtocolComment> GetByIdForDepartmentAsync(int departmentId, int protocolCommentId)
		{
			return QueryFirstOrDefaultAsync<ProtocolComment>(
				$"SELECT * FROM {Tbl("ProtocolComments")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("ProtocolCommentId")} = {P}Id",
				new { DepartmentId = departmentId, Id = protocolCommentId });
		}

		public Task<int> CountOpenReviewCommentsAsync(int departmentId, int protocolVersionId)
		{
			return ScalarAsync<int>(
				$"SELECT COUNT(*) FROM {Tbl("ProtocolComments")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("ProtocolVersionId")} = {P}Id " +
				$"AND {Col("Kind")} = {(int)ProtocolCommentKinds.ReviewComment} AND {Col("ParentCommentId")} IS NULL AND {Col("ResolvedOn")} IS NULL",
				new { DepartmentId = departmentId, Id = protocolVersionId });
		}
	}

	/// <summary>Library queries on DispatchProtocols (P1): optimistic row versions, review reminders, retention work, usage.</summary>
	public class ProtocolLibraryRepository : RmsRepositoryBase<DispatchProtocol>, IProtocolLibraryRepository
	{
		public ProtocolLibraryRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<bool> TryBumpRowVersionAsync(int departmentId, int protocolId, long expected, CancellationToken cancellationToken = default)
		{
			return await ExecuteAsync(
				$"UPDATE {Tbl("DispatchProtocols")} SET {Col("RowVersion")} = {P}Next WHERE {Col("DispatchProtocolId")} = {P}Id AND {Col("DepartmentId")} = {P}DepartmentId AND {Col("RowVersion")} = {P}Expected",
				new { Id = protocolId, DepartmentId = departmentId, Expected = expected, Next = expected + 1 }, cancellationToken) == 1;
		}

		public async Task<List<ProtocolReviewDue>> GetReviewRemindersDueAsync(DateTime utcNow, int take)
		{
			var notDeleted = IsPostgres ? "false" : "0";
			// The stage due now, computed in SQL so only protocols that owe a reminder come back.
			var stage = $"CASE WHEN {Col("NextReviewOn")} <= {P}Now THEN {(int)ProtocolReviewReminderStages.Overdue} " +
				$"WHEN {Col("NextReviewOn")} <= {P}Seven THEN {(int)ProtocolReviewReminderStages.SevenDays} ELSE {(int)ProtocolReviewReminderStages.ThirtyDays} END";
			return (await QueryAsync<ProtocolReviewDue>(
				$"SELECT {Cols("DispatchProtocolId", "DepartmentId", "Name", "Code", "OwnerUserId", "NextReviewOn", "ReviewReminderStage")} FROM {Tbl("DispatchProtocols")} " +
				$"WHERE {Col("Status")} = {(int)ProtocolStatuses.Published} AND {Col("IsDeleted")} = {notDeleted} AND {Col("NextReviewOn")} IS NOT NULL " +
				$"AND {Col("NextReviewOn")} <= {P}Thirty AND {Col("ReviewReminderStage")} < ({stage}) " +
				$"ORDER BY {Col("NextReviewOn")}, {Col("DispatchProtocolId")} " + (IsPostgres ? "LIMIT @Take" : "OFFSET 0 ROWS FETCH NEXT @Take ROWS ONLY"),
				new
				{
					Now = DatabaseTimestamp(utcNow),
					Seven = DatabaseTimestamp(utcNow.AddDays(7)),
					Thirty = DatabaseTimestamp(utcNow.AddDays(30)),
					Take = Math.Max(1, take)
				})).ToList();
		}

		public async Task<bool> MarkReviewReminderAsync(int departmentId, int protocolId, DateTime nextReviewOn, int stage, CancellationToken cancellationToken = default)
		{
			return await ExecuteAsync(
				$"UPDATE {Tbl("DispatchProtocols")} SET {Col("ReviewReminderStage")} = {P}Stage WHERE {Col("DispatchProtocolId")} = {P}Id AND {Col("DepartmentId")} = {P}DepartmentId " +
				$"AND {Col("NextReviewOn")} = {P}NextReviewOn AND {Col("ReviewReminderStage")} < {P}Stage",
				new { Id = protocolId, DepartmentId = departmentId, Stage = stage, NextReviewOn = DatabaseTimestamp(nextReviewOn) }, cancellationToken) == 1;
		}

		public async Task<List<int>> GetDepartmentsWithRetentionWorkAsync(int take)
		{
			return (await QueryAsync<int>(
				$"SELECT DISTINCT s.{Col("DepartmentId")} FROM {Tbl("DepartmentSettings")} s WHERE s.{Col("SettingType")} = {(int)DepartmentSettingTypes.ProtocolLibrarySettings} " +
				$"AND EXISTS (SELECT 1 FROM {Tbl("ProtocolVersions")} v WHERE v.{Col("DepartmentId")} = s.{Col("DepartmentId")} " +
				$"AND v.{Col("Status")} IN ({(int)ProtocolVersionStatuses.Superseded}, {(int)ProtocolVersionStatuses.Retired})) " +
				$"ORDER BY s.{Col("DepartmentId")} " + (IsPostgres ? "LIMIT @Take" : "OFFSET 0 ROWS FETCH NEXT @Take ROWS ONLY"),
				new { Take = Math.Max(1, take) })).ToList();
		}

		public async Task<List<ProtocolCallUsage>> GetRecentCallsAsync(int departmentId, int protocolId, int take)
		{
			return (await QueryAsync<ProtocolCallUsage>(
				$"SELECT c.{Col("CallId")} AS CallId, c.{Col("Number")} AS Number, c.{Col("Name")} AS Name, c.{Col("LoggedOn")} AS LoggedOn, " +
				$"cp.{Col("ProtocolVersionId")} AS ProtocolVersionId, cp.{Col("RemovedOn")} AS RemovedOn " +
				$"FROM {Tbl("CallProtocols")} cp JOIN {Tbl("Calls")} c ON c.{Col("CallId")} = cp.{Col("CallId")} " +
				$"WHERE c.{Col("DepartmentId")} = {P}DepartmentId AND cp.{Col("DispatchProtocolId")} = {P}ProtocolId " +
				$"ORDER BY c.{Col("LoggedOn")} DESC " + (IsPostgres ? "LIMIT @Take" : "OFFSET 0 ROWS FETCH NEXT @Take ROWS ONLY"),
				new { DepartmentId = departmentId, ProtocolId = protocolId, Take = Math.Max(1, take) })).ToList();
		}

		public async Task<Dictionary<int, int>> GetCallCountsByVersionAsync(int departmentId, int protocolId)
		{
			var rows = await QueryAsync<VersionCount>(
				$"SELECT cp.{Col("ProtocolVersionId")} AS ProtocolVersionId, COUNT(*) AS Calls FROM {Tbl("CallProtocols")} cp JOIN {Tbl("Calls")} c ON c.{Col("CallId")} = cp.{Col("CallId")} " +
				$"WHERE c.{Col("DepartmentId")} = {P}DepartmentId AND cp.{Col("DispatchProtocolId")} = {P}ProtocolId AND cp.{Col("ProtocolVersionId")} IS NOT NULL " +
				$"GROUP BY cp.{Col("ProtocolVersionId")}",
				new { DepartmentId = departmentId, ProtocolId = protocolId });
			return rows.Where(r => r.ProtocolVersionId.HasValue).ToDictionary(r => r.ProtocolVersionId.Value, r => r.Calls);
		}

		private sealed class VersionCount
		{
			public int? ProtocolVersionId { get; set; }
			public int Calls { get; set; }
		}
	}
}
