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
	/// <summary>
	/// Dispatch case reviews (Protocols P5 QA, M0391): portable SQL, every statement scoped to the department. The one-time transitions (send,
	/// acknowledge, reply) are conditional single statements; the candidate query reads short columns of CallProtocols and Calls only.
	/// </summary>
	public class DispatchCaseReviewsRepository : RmsRepositoryBase<DispatchCaseReview>, IDispatchCaseReviewsRepository
	{
		private const string Table = "DispatchCaseReviews";

		public DispatchCaseReviewsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		private static string True => IsPostgres ? "true" : "1";

		private static string Top(string take) => IsPostgres ? string.Empty : $"TOP ({take}) ";

		private static string Limit(string take) => IsPostgres ? $" LIMIT {take}" : string.Empty;

		private static DateTime Utc(DateTime value) => DatabaseTimestamp(DateTime.SpecifyKind(value, DateTimeKind.Utc));

		private static DateTime? Utc(DateTime? value) => value.HasValue ? Utc(value.Value) : (DateTime?)null;

		private static DispatchCaseReview AsUtc(DispatchCaseReview review)
		{
			if (review == null)
				return null;
			review.CallLoggedOn = DateTime.SpecifyKind(review.CallLoggedOn, DateTimeKind.Utc);
			review.SampledOn = DateTime.SpecifyKind(review.SampledOn, DateTimeKind.Utc);
			review.CreatedOn = DateTime.SpecifyKind(review.CreatedOn, DateTimeKind.Utc);
			review.ModifiedOn = DateTime.SpecifyKind(review.ModifiedOn, DateTimeKind.Utc);
			if (review.SentOn.HasValue) review.SentOn = DateTime.SpecifyKind(review.SentOn.Value, DateTimeKind.Utc);
			if (review.AcknowledgedOn.HasValue) review.AcknowledgedOn = DateTime.SpecifyKind(review.AcknowledgedOn.Value, DateTimeKind.Utc);
			if (review.DispatcherCommentOn.HasValue) review.DispatcherCommentOn = DateTime.SpecifyKind(review.DispatcherCommentOn.Value, DateTimeKind.Utc);
			return review;
		}

		private static string SentStatuses => $"({(int)DispatchCaseReviewStatuses.Sent}, {(int)DispatchCaseReviewStatuses.Acknowledged})";

		private static string OpenStatuses => $"({(int)DispatchCaseReviewStatuses.Queued}, {(int)DispatchCaseReviewStatuses.InReview})";

		public async Task<DispatchCaseReview> GetByIdForDepartmentAsync(int departmentId, int dispatchCaseReviewId)
		{
			if (departmentId <= 0 || dispatchCaseReviewId <= 0)
				return null;
			return AsUtc(await QueryFirstOrDefaultAsync<DispatchCaseReview>(
				$"SELECT * FROM {Tbl(Table)} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("DispatchCaseReviewId")} = {P}Id",
				new { DepartmentId = departmentId, Id = dispatchCaseReviewId }));
		}

		public async Task<List<DispatchCaseReview>> GetOpenAsync(int departmentId, int take)
		{
			if (departmentId <= 0)
				return new List<DispatchCaseReview>();
			return (await QueryAsync<DispatchCaseReview>(
				$"SELECT {Top("@Take")}* FROM {Tbl(Table)} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("Status")} IN {OpenStatuses} " +
				$"ORDER BY {Col("SampledOn")}, {Col("DispatchCaseReviewId")}{Limit("@Take")}",
				new { DepartmentId = departmentId, Take = Math.Clamp(take, 1, 1000) })).Select(AsUtc).ToList();
		}

		public async Task<List<DispatchCaseReview>> GetSentForDispatcherAsync(int departmentId, string dispatcherUserId, int take)
		{
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(dispatcherUserId))
				return new List<DispatchCaseReview>();
			return (await QueryAsync<DispatchCaseReview>(
				$"SELECT {Top("@Take")}* FROM {Tbl(Table)} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("DispatcherUserId")} = {CaseInsensitive("UserId")} " +
				$"AND {Col("Status")} IN {SentStatuses} ORDER BY {Col("SentOn")} DESC, {Col("DispatchCaseReviewId")} DESC{Limit("@Take")}",
				new { DepartmentId = departmentId, UserId = dispatcherUserId, Take = Math.Clamp(take, 1, 1000) })).Select(AsUtc).ToList();
		}

		public async Task<List<DispatchCaseReview>> GetSentForReportAsync(int departmentId, DateTime fromUtc, DateTime toUtc, int? dispatchProtocolId, int? protocolVersionId,
			string dispatcherUserId, int take)
		{
			if (departmentId <= 0)
				return new List<DispatchCaseReview>();
			var parameters = new DynamicParameters();
			parameters.Add("DepartmentId", departmentId);
			parameters.Add("From", Utc(fromUtc));
			parameters.Add("To", Utc(toUtc));
			parameters.Add("Take", Math.Clamp(take, 1, 20000));
			var where = $"{Col("DepartmentId")} = {P}DepartmentId AND {Col("Status")} IN {SentStatuses} AND {Col("CallLoggedOn")} >= {P}From AND {Col("CallLoggedOn")} < {P}To";
			if (dispatchProtocolId.HasValue)
			{
				where += $" AND {Col("DispatchProtocolId")} = {P}ProtocolId";
				parameters.Add("ProtocolId", dispatchProtocolId.Value);
			}
			if (protocolVersionId.HasValue)
			{
				where += $" AND {Col("ProtocolVersionId")} = {P}VersionId";
				parameters.Add("VersionId", protocolVersionId.Value);
			}
			if (!string.IsNullOrWhiteSpace(dispatcherUserId))
			{
				where += $" AND {Col("DispatcherUserId")} = {CaseInsensitive("UserId")}";
				parameters.Add("UserId", dispatcherUserId, System.Data.DbType.String);
			}

			return (await QueryAsync<DispatchCaseReview>(
				$"SELECT {Top("@Take")}* FROM {Tbl(Table)} WHERE {where} ORDER BY {Col("CallLoggedOn")} DESC, {Col("DispatchCaseReviewId")} DESC{Limit("@Take")}",
				parameters)).Select(AsUtc).ToList();
		}

		public async Task<Dictionary<int, int>> GetStatusCountsAsync(int departmentId)
		{
			if (departmentId <= 0)
				return new Dictionary<int, int>();
			var rows = await QueryAsync<StatusCount>(
				$"SELECT {Col("Status")} AS {Col("Status")}, COUNT(*) AS {Col("Total")} FROM {Tbl(Table)} WHERE {Col("DepartmentId")} = {P}DepartmentId GROUP BY {Col("Status")}",
				new { DepartmentId = departmentId });
			return rows.ToDictionary(r => r.Status, r => r.Total);
		}

		public async Task<List<DispatchCaseCandidate>> GetCandidatesAsync(int departmentId, DispatchCaseCandidateFilter filter, int take)
		{
			if (departmentId <= 0 || filter == null)
				return new List<DispatchCaseCandidate>();

			var parameters = new DynamicParameters();
			parameters.Add("DepartmentId", departmentId);
			parameters.Add("From", Utc(filter.FromUtc));
			parameters.Add("To", Utc(filter.ToUtc));
			parameters.Add("Take", Math.Clamp(take, 1, DispatchCaseSampling.MaxCandidates));

			var where = new List<string>
			{
				$"c.{Col("DepartmentId")} = {P}DepartmentId",
				$"c.{Col("IsDeleted")} = {(IsPostgres ? "false" : "0")}",
				$"cp.{Col("RemovedOn")} IS NULL",
				$"cp.{Col("CaseJson")} IS NOT NULL",
				$"c.{Col("LoggedOn")} >= {P}From",
				$"c.{Col("LoggedOn")} < {P}To"
			};
			if (filter.DispatchProtocolId.HasValue)
			{
				where.Add($"cp.{Col("DispatchProtocolId")} = {P}ProtocolId");
				parameters.Add("ProtocolId", filter.DispatchProtocolId.Value);
			}
			if (filter.ProtocolVersionId.HasValue)
			{
				where.Add($"cp.{Col("ProtocolVersionId")} = {P}VersionId");
				parameters.Add("VersionId", filter.ProtocolVersionId.Value);
			}
			var determinant = ProtocolDecisionGraphs.NormalizeDeterminant(filter.Determinant);
			if (determinant != null)
			{
				// A prefix match ("09-E" finds 09-E-01); the column compares case-insensitively on both engines (nvarchar CI, citext).
				where.Add($"cp.{Col("Determinant")} LIKE {P}Determinant ESCAPE '!'");
				parameters.Add("Determinant", determinant.Replace("!", "!!").Replace("%", "!%").Replace("_", "!_").Replace("[", "![") + "%", System.Data.DbType.String);
			}
			if (!string.IsNullOrWhiteSpace(filter.DispatcherUserId))
			{
				where.Add($"(cp.{Col("AttachedByUserId")} = {CaseInsensitive("Dispatcher")} OR (cp.{Col("AttachedByUserId")} IS NULL AND c.{Col("ReportingUserId")} = {CaseInsensitive("Dispatcher")}))");
				parameters.Add("Dispatcher", filter.DispatcherUserId.Trim(), System.Data.DbType.String);
			}

			var sql =
				$"SELECT {Top("@Take")}cp.{Col("CallProtocolId")} AS {Col("CallProtocolId")}, cp.{Col("CallId")} AS {Col("CallId")}, cp.{Col("DispatchProtocolId")} AS {Col("DispatchProtocolId")}, " +
				$"cp.{Col("ProtocolVersionId")} AS {Col("ProtocolVersionId")}, cp.{Col("Determinant")} AS {Col("Determinant")}, cp.{Col("AttachedByUserId")} AS {Col("AttachedByUserId")}, " +
				$"c.{Col("ReportingUserId")} AS {Col("ReportingUserId")}, c.{Col("LoggedOn")} AS {Col("LoggedOn")}, c.{Col("Number")} AS {Col("CallNumber")}, c.{Col("Name")} AS {Col("CallName")}, " +
				$"CASE WHEN cp.{Col("OverrideReason")} IS NULL THEN 0 ELSE 1 END AS {Col("OverriddenFlag")}, " +
				$"CASE WHEN EXISTS (SELECT 1 FROM {Tbl(Table)} r WHERE r.{Col("DepartmentId")} = {P}DepartmentId AND r.{Col("CallProtocolId")} = cp.{Col("CallProtocolId")}) THEN 1 ELSE 0 END AS {Col("ReviewedFlag")} " +
				$"FROM {Tbl("CallProtocols")} cp JOIN {Tbl("Calls")} c ON c.{Col("CallId")} = cp.{Col("CallId")} " +
				$"WHERE {string.Join(" AND ", where)} ORDER BY c.{Col("LoggedOn")} DESC, cp.{Col("CallProtocolId")} DESC{Limit("@Take")}";

			var rows = await QueryAsync<CandidateRow>(sql, parameters);
			return rows.Select(r => new DispatchCaseCandidate
			{
				CallProtocolId = r.CallProtocolId,
				CallId = r.CallId,
				DispatchProtocolId = r.DispatchProtocolId,
				ProtocolVersionId = r.ProtocolVersionId,
				Determinant = r.Determinant,
				AttachedByUserId = r.AttachedByUserId,
				ReportingUserId = r.ReportingUserId,
				LoggedOn = DateTime.SpecifyKind(r.LoggedOn, DateTimeKind.Utc),
				CallNumber = r.CallNumber,
				CallName = r.CallName,
				Overridden = r.OverriddenFlag != 0,
				AlreadyReviewed = r.ReviewedFlag != 0
			}).ToList();
		}

		public async Task<Dictionary<int, string>> GetCaseJsonAsync(int departmentId, IReadOnlyCollection<int> callProtocolIds)
		{
			var ids = (callProtocolIds ?? Array.Empty<int>()).Where(i => i > 0).Distinct().ToArray();
			if (departmentId <= 0 || ids.Length == 0)
				return new Dictionary<int, string>();

			var result = new Dictionary<int, string>();
			foreach (var chunk in ids.Chunk(500))
			{
				var rows = await QueryAsync<CaseRow>(
					$"SELECT cp.{Col("CallProtocolId")} AS {Col("CallProtocolId")}, cp.{Col("CaseJson")} AS {Col("CaseJson")} FROM {Tbl("CallProtocols")} cp " +
					$"JOIN {Tbl("Calls")} c ON c.{Col("CallId")} = cp.{Col("CallId")} WHERE c.{Col("DepartmentId")} = {P}DepartmentId AND {InList("CallProtocolId", "Ids", "cp")}",
					new { DepartmentId = departmentId, Ids = InListValue(chunk) });
				foreach (var row in rows)
					result[row.CallProtocolId] = row.CaseJson;
			}
			return result;
		}

		public async Task<bool> TryInsertAsync(DispatchCaseReview review, CancellationToken cancellationToken = default)
		{
			if (review == null || review.DepartmentId <= 0)
				return false;

			var transaction = UnitOfWork?.Transaction;
			var savepoint = IsPostgres && transaction != null;
			try
			{
				if (savepoint) await transaction.SaveAsync("case_review_unique", cancellationToken);
				await base.InsertAsync(review, cancellationToken, true);
				if (savepoint) await transaction.ReleaseAsync("case_review_unique", cancellationToken);
				return true;
			}
			catch (Exception ex) when (IsUniqueViolation(ex) || IsUniqueViolation(ex.InnerException))
			{
				if (savepoint) await transaction.RollbackAsync("case_review_unique", cancellationToken);
				return false;
			}
		}

		public async Task<bool> TrySaveScoresAsync(DispatchCaseReview review, long expectedRowVersion, CancellationToken cancellationToken = default)
		{
			if (review == null || review.DepartmentId <= 0 || review.DispatchCaseReviewId <= 0)
				return false;

			var parameters = new DynamicParameters();
			parameters.Add("DepartmentId", review.DepartmentId);
			parameters.Add("Id", review.DispatchCaseReviewId);
			parameters.Add("Expected", expectedRowVersion);
			parameters.Add("Next", expectedRowVersion + 1);
			parameters.Add("Status", review.Status);
			parameters.Add("Findings", review.FindingsJson, System.Data.DbType.String);
			parameters.Add("Items", review.ItemsJson, System.Data.DbType.String);
			parameters.Add("Score", review.Score, System.Data.DbType.Int32);
			parameters.Add("QuestionCount", review.QuestionCount);
			parameters.Add("QuestionPoints", review.QuestionPoints);
			parameters.Add("InstructionCount", review.InstructionCount);
			parameters.Add("InstructionPoints", review.InstructionPoints);
			parameters.Add("Assigned", review.AssignedToUserId, System.Data.DbType.String);
			parameters.Add("Reviewer", review.ReviewerUserId, System.Data.DbType.String);
			parameters.Add("Comments", review.ReviewerComments, System.Data.DbType.String);
			parameters.Add("Modified", Utc(review.ModifiedOn));

			// SentOn is written only when sending: a null timestamp parameter is never tested in SQL (PostgreSQL 42P08).
			var sent = review.SentOn.HasValue ? $", {Col("SentOn")} = {P}SentOn" : string.Empty;
			if (review.SentOn.HasValue)
				parameters.Add("SentOn", Utc(review.SentOn.Value));

			var changed = await ExecuteAsync(
				$"UPDATE {Tbl(Table)} SET {Col("Status")} = {P}Status, {Col("FindingsJson")} = {P}Findings, {Col("ItemsJson")} = {P}Items, {Col("Score")} = {P}Score, " +
				$"{Col("QuestionCount")} = {P}QuestionCount, {Col("QuestionPoints")} = {P}QuestionPoints, {Col("InstructionCount")} = {P}InstructionCount, " +
				$"{Col("InstructionPoints")} = {P}InstructionPoints, {Col("AssignedToUserId")} = {P}Assigned, {Col("ReviewerUserId")} = {P}Reviewer, " +
				$"{Col("ReviewerComments")} = {P}Comments, {Col("ModifiedOn")} = {P}Modified, {Col("RowVersion")} = {P}Next{sent} " +
				$"WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("DispatchCaseReviewId")} = {P}Id AND {Col("RowVersion")} = {P}Expected AND {Col("Status")} IN {OpenStatuses}",
				parameters, cancellationToken);
			if (changed != 1)
				return false;

			review.RowVersion = expectedRowVersion + 1;
			return true;
		}

		public async Task<bool> TryAssignAsync(int departmentId, int dispatchCaseReviewId, string assigneeUserId, long expectedRowVersion, DateTime nowUtc,
			CancellationToken cancellationToken = default)
		{
			if (departmentId <= 0 || dispatchCaseReviewId <= 0)
				return false;

			var parameters = new DynamicParameters();
			parameters.Add("DepartmentId", departmentId);
			parameters.Add("Id", dispatchCaseReviewId);
			parameters.Add("Assignee", assigneeUserId, System.Data.DbType.String);
			parameters.Add("Expected", expectedRowVersion);
			parameters.Add("Now", Utc(nowUtc));
			var changed = await ExecuteAsync(
				$"UPDATE {Tbl(Table)} SET {Col("AssignedToUserId")} = {P}Assignee, {Col("ModifiedOn")} = {P}Now, {Col("RowVersion")} = {Col("RowVersion")} + 1 " +
				$"WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("DispatchCaseReviewId")} = {P}Id AND {Col("RowVersion")} = {P}Expected AND {Col("Status")} IN {OpenStatuses}",
				parameters, cancellationToken);
			return changed == 1;
		}

		public async Task<bool> TryAcknowledgeAsync(int departmentId, int dispatchCaseReviewId, string dispatcherUserId, string comment, DateTime nowUtc,
			CancellationToken cancellationToken = default)
		{
			if (departmentId <= 0 || dispatchCaseReviewId <= 0 || string.IsNullOrWhiteSpace(dispatcherUserId))
				return false;

			var parameters = new DynamicParameters();
			parameters.Add("DepartmentId", departmentId);
			parameters.Add("Id", dispatchCaseReviewId);
			parameters.Add("UserId", dispatcherUserId, System.Data.DbType.String);
			parameters.Add("Now", Utc(nowUtc));
			var reply = string.Empty;
			if (!string.IsNullOrWhiteSpace(comment))
			{
				// The comment becomes the one reply only when there is none yet; an existing reply is kept.
				reply = $", {Col("DispatcherComment")} = COALESCE({Col("DispatcherComment")}, {P}Comment), " +
				        $"{Col("DispatcherCommentOn")} = CASE WHEN {Col("DispatcherComment")} IS NULL THEN {P}Now ELSE {Col("DispatcherCommentOn")} END";
				parameters.Add("Comment", comment, System.Data.DbType.String);
			}

			var changed = await ExecuteAsync(
				$"UPDATE {Tbl(Table)} SET {Col("Status")} = {(int)DispatchCaseReviewStatuses.Acknowledged}, {Col("AcknowledgedOn")} = {P}Now, {Col("ModifiedOn")} = {P}Now, " +
				$"{Col("RowVersion")} = {Col("RowVersion")} + 1{reply} " +
				$"WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("DispatchCaseReviewId")} = {P}Id AND {Col("Status")} = {(int)DispatchCaseReviewStatuses.Sent} " +
				$"AND {Col("DispatcherUserId")} = {CaseInsensitive("UserId")}",
				parameters, cancellationToken);
			return changed == 1;
		}

		public async Task<bool> TryCommentAsync(int departmentId, int dispatchCaseReviewId, string dispatcherUserId, string comment, DateTime nowUtc,
			CancellationToken cancellationToken = default)
		{
			if (departmentId <= 0 || dispatchCaseReviewId <= 0 || string.IsNullOrWhiteSpace(dispatcherUserId) || string.IsNullOrWhiteSpace(comment))
				return false;

			var parameters = new DynamicParameters();
			parameters.Add("DepartmentId", departmentId);
			parameters.Add("Id", dispatchCaseReviewId);
			parameters.Add("UserId", dispatcherUserId, System.Data.DbType.String);
			parameters.Add("Comment", comment, System.Data.DbType.String);
			parameters.Add("Now", Utc(nowUtc));
			var changed = await ExecuteAsync(
				$"UPDATE {Tbl(Table)} SET {Col("DispatcherComment")} = {P}Comment, {Col("DispatcherCommentOn")} = {P}Now, {Col("ModifiedOn")} = {P}Now, " +
				$"{Col("RowVersion")} = {Col("RowVersion")} + 1 " +
				$"WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("DispatchCaseReviewId")} = {P}Id AND {Col("Status")} IN {SentStatuses} " +
				$"AND {Col("DispatcherComment")} IS NULL AND {Col("DispatcherUserId")} = {CaseInsensitive("UserId")}",
				parameters, cancellationToken);
			return changed == 1;
		}

		public async Task<bool> TryDeleteOpenAsync(int departmentId, int dispatchCaseReviewId, CancellationToken cancellationToken = default)
		{
			if (departmentId <= 0 || dispatchCaseReviewId <= 0)
				return false;
			var changed = await ExecuteAsync(
				$"DELETE FROM {Tbl(Table)} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("DispatchCaseReviewId")} = {P}Id AND {Col("Status")} IN {OpenStatuses}",
				new { DepartmentId = departmentId, Id = dispatchCaseReviewId }, cancellationToken);
			return changed == 1;
		}

		private sealed class StatusCount
		{
			public int Status { get; set; }
			public int Total { get; set; }
		}

		private sealed class CaseRow
		{
			public int CallProtocolId { get; set; }
			public string CaseJson { get; set; }
		}

		private sealed class CandidateRow
		{
			public int CallProtocolId { get; set; }
			public int CallId { get; set; }
			public int DispatchProtocolId { get; set; }
			public int? ProtocolVersionId { get; set; }
			public string Determinant { get; set; }
			public string AttachedByUserId { get; set; }
			public string ReportingUserId { get; set; }
			public DateTime LoggedOn { get; set; }
			public string CallNumber { get; set; }
			public string CallName { get; set; }
			public int OverriddenFlag { get; set; }
			public int ReviewedFlag { get; set; }
		}
	}
}
