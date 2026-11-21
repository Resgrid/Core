using System;
using System.Collections.Generic;
using System.Data;
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
	/// <summary>Enhanced AI destination advice (AiDestinationAdvices, M0323).</summary>
	public class AiDestinationAdvicesRepository : RmsRepositoryBase<AiDestinationAdvice>, IAiDestinationAdvicesRepository
	{
		public AiDestinationAdvicesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		private static string Columns => Cols("AiDestinationAdviceId", "DepartmentId", "CallId", "CurrentPoiId", "RecommendedPoiId", "RequiredCapabilityIds",
			"Rationale", "Confidence", "IsChange", "ReviewTrigger", "State", "InputDigest", "PromptVersion", "ModelRevision", "CreatedOn", "ResolvedByUserId", "ResolvedOn");

		// The newest row matching the filter, in the dialect's form.
		private string Newest(string where) => IsPostgres
			? $"SELECT {Columns} FROM {Tbl("AiDestinationAdvices")} WHERE {where} ORDER BY {Col("CreatedOn")} DESC LIMIT 1"
			: $"SELECT TOP 1 {Columns} FROM {Tbl("AiDestinationAdvices")} WHERE {where} ORDER BY {Col("CreatedOn")} DESC";

		public async Task<AiDestinationAdvice> GetForDepartmentAsync(int departmentId, string adviceId, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(adviceId) || adviceId.Length > 36)
				return null;

			return Utc(await QueryFirstOrDefaultAsync<AiDestinationAdvice>(
				$"SELECT {Columns} FROM {Tbl("AiDestinationAdvices")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("AiDestinationAdviceId")} = {P}Id",
				new { DepartmentId = departmentId, Id = adviceId.Trim() }, cancellationToken));
		}

		public async Task<AiDestinationAdvice> GetOpenForCallAsync(int departmentId, int callId, CancellationToken cancellationToken = default)
		{
			return Utc(await QueryFirstOrDefaultAsync<AiDestinationAdvice>(
				Newest($"{Col("DepartmentId")} = {P}DepartmentId AND {Col("CallId")} = {P}CallId AND {Col("State")} = {P}State"),
				new { DepartmentId = departmentId, CallId = callId, State = (int)AiDestinationAdviceStates.Open }, cancellationToken));
		}

		public async Task<AiDestinationAdvice> GetLatestForCallAsync(int departmentId, int callId, CancellationToken cancellationToken = default)
		{
			return Utc(await QueryFirstOrDefaultAsync<AiDestinationAdvice>(
				Newest($"{Col("DepartmentId")} = {P}DepartmentId AND {Col("CallId")} = {P}CallId"),
				new { DepartmentId = departmentId, CallId = callId }, cancellationToken));
		}

		public Task<int> SupersedeOpenAsync(int departmentId, int callId, string exceptAdviceId, DateTime nowUtc, CancellationToken cancellationToken = default)
		{
			var parameters = new DynamicParameters(new
			{
				DepartmentId = departmentId, CallId = callId, Open = (int)AiDestinationAdviceStates.Open, Superseded = (int)AiDestinationAdviceStates.Superseded,
				Except = exceptAdviceId ?? string.Empty
			});
			AiDestinationReviewsRepository.AddTimestamp(parameters, "Now", nowUtc);

			// Only rows OLDER than the kept advice (CreatedOn, then id): two publishers that each insert and then supersede can never
			// supersede each other, so whatever the interleaving the newest advice stays the call's one open row. The kept row's
			// CreatedOn is read in SQL, so no parameter precision can differ from what is stored. No kept id: every open row.
			var kept = $"(SELECT k.{Col("CreatedOn")} FROM {Tbl("AiDestinationAdvices")} k WHERE k.{Col("AiDestinationAdviceId")} = {P}Except)";
			return ExecuteAsync(
				$"UPDATE {Tbl("AiDestinationAdvices")} SET {Col("State")} = {P}Superseded, {Col("ResolvedOn")} = {P}Now " +
				$"WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("CallId")} = {P}CallId AND {Col("State")} = {P}Open AND {Col("AiDestinationAdviceId")} <> {P}Except " +
				$"AND ({P}Except = '' OR {Col("CreatedOn")} < {kept} OR ({Col("CreatedOn")} = {kept} AND {Col("AiDestinationAdviceId")} < {P}Except))",
				parameters, cancellationToken);
		}

		public async Task<bool> TryTransitionAsync(int departmentId, string adviceId, AiDestinationAdviceStates fromState, AiDestinationAdviceStates toState, string userId,
			DateTime? resolvedOnUtc, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(adviceId))
				return false;

			var parameters = new DynamicParameters(new { DepartmentId = departmentId, Id = adviceId.Trim(), From = (int)fromState, To = (int)toState });
			parameters.Add("UserId", userId, DbType.String);
			if (resolvedOnUtc.HasValue)
				AiDestinationReviewsRepository.AddTimestamp(parameters, "ResolvedOn", resolvedOnUtc.Value);

			var resolvedOn = resolvedOnUtc.HasValue ? $"{P}ResolvedOn" : "NULL";
			return await ExecuteAsync(
				$"UPDATE {Tbl("AiDestinationAdvices")} SET {Col("State")} = {P}To, {Col("ResolvedByUserId")} = {P}UserId, {Col("ResolvedOn")} = {resolvedOn} " +
				$"WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("AiDestinationAdviceId")} = {P}Id AND {Col("State")} = {P}From",
				parameters, cancellationToken) == 1;
		}

		// Both databases hand back zone-less values; every timestamp here is written as UTC.
		private static AiDestinationAdvice Utc(AiDestinationAdvice advice)
		{
			if (advice == null)
				return null;

			advice.CreatedOn = DateTime.SpecifyKind(advice.CreatedOn, DateTimeKind.Utc);
			if (advice.ResolvedOn.HasValue)
				advice.ResolvedOn = DateTime.SpecifyKind(advice.ResolvedOn.Value, DateTimeKind.Utc);

			return advice;
		}
	}

	/// <summary>Destination review requests (AiDestinationReviews, M0323): one waiting row per call (filtered unique index).</summary>
	public class AiDestinationReviewsRepository : RmsRepositoryBase<AiDestinationReview>, IAiDestinationReviewsRepository
	{
		public AiDestinationReviewsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		private static string Columns => Cols("AiDestinationReviewId", "DepartmentId", "CallId", "ReviewTrigger", "RequestedOn", "ClaimedOn", "ProcessedOn", "Outcome");

		internal static void AddTimestamp(DynamicParameters parameters, string name, DateTime value)
		{
			if (IsPostgres)
				parameters.Add(name, DateTime.SpecifyKind(value, DateTimeKind.Unspecified));
			else
				parameters.Add(name, value, DbType.DateTime2);
		}

		public async Task QueueAsync(int departmentId, int callId, AiDestinationReviewTriggers trigger, DateTime nowUtc, CancellationToken cancellationToken = default)
		{
			if (departmentId <= 0 || callId <= 0)
				return;

			// Fold into the waiting row; else insert one unless one appeared meanwhile. Two triggers inserting at the same instant are
			// settled without an error: SQL Server serializes the existence check (UPDLOCK, HOLDLOCK) and PostgreSQL skips the losing
			// insert on the filtered unique index (ON CONFLICT DO NOTHING), so the loser inserts nothing and folds into the winner's
			// row on the second round. A duplicate-key error is still caught as a last resort, but no longer expected (it would be
			// logged by the repository base on the way out).
			var existsLock = IsPostgres ? string.Empty : " WITH (UPDLOCK, HOLDLOCK)";
			var onConflict = IsPostgres ? " ON CONFLICT DO NOTHING" : string.Empty;
			for (var attempt = 0; attempt < 2; attempt++)
			{
				if (await FoldAsync(departmentId, callId, trigger, nowUtc, cancellationToken))
					return;

				var parameters = new DynamicParameters(new { Id = Guid.NewGuid().ToString("D"), DepartmentId = departmentId, CallId = callId, Trigger = (int)trigger });
				AddTimestamp(parameters, "Now", nowUtc);
				try
				{
					if (await ExecuteAsync(
							$"INSERT INTO {Tbl("AiDestinationReviews")} ({Cols("AiDestinationReviewId", "DepartmentId", "CallId", "ReviewTrigger", "RequestedOn")}) " +
							$"SELECT {P}Id, {P}DepartmentId, {P}CallId, {P}Trigger, {P}Now WHERE NOT EXISTS (SELECT 1 FROM {Tbl("AiDestinationReviews")}{existsLock} " +
							$"WHERE {Col("CallId")} = {P}CallId AND {Col("ProcessedOn")} IS NULL){onConflict}", parameters, cancellationToken) == 1)
						return;
				}
				catch (Exception ex) when (IsUniqueViolation(ex))
				{
					// Another trigger inserted the waiting row first; fold into it.
				}
			}
		}

		private async Task<bool> FoldAsync(int departmentId, int callId, AiDestinationReviewTriggers trigger, DateTime nowUtc, CancellationToken cancellationToken)
		{
			var parameters = new DynamicParameters(new { DepartmentId = departmentId, CallId = callId, Trigger = (int)trigger });
			AddTimestamp(parameters, "Now", nowUtc);

			// A waiting row keeps its due time; a claimed one moves it, so the pass in flight leaves the row for another run.
			return await ExecuteAsync(
				$"UPDATE {Tbl("AiDestinationReviews")} SET {Col("ReviewTrigger")} = {P}Trigger, " +
				$"{Col("RequestedOn")} = CASE WHEN {Col("ClaimedOn")} IS NULL THEN {Col("RequestedOn")} ELSE {P}Now END " +
				$"WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("CallId")} = {P}CallId AND {Col("ProcessedOn")} IS NULL",
				parameters, cancellationToken) > 0;
		}

		public async Task<List<AiDestinationReview>> GetDueAsync(DateTime dueBeforeUtc, DateTime leaseCutoffUtc, int take, CancellationToken cancellationToken = default)
		{
			take = Math.Clamp(take, 1, 500);
			var parameters = new DynamicParameters(new { Take = take });
			AddTimestamp(parameters, "DueBefore", dueBeforeUtc);
			AddTimestamp(parameters, "LeaseCutoff", leaseCutoffUtc);

			var where = $"{Col("ProcessedOn")} IS NULL AND {Col("RequestedOn")} <= {P}DueBefore AND ({Col("ClaimedOn")} IS NULL OR {Col("ClaimedOn")} < {P}LeaseCutoff)";
			var sql = IsPostgres
				? $"SELECT {Columns} FROM {Tbl("AiDestinationReviews")} WHERE {where} ORDER BY {Col("RequestedOn")} LIMIT {P}Take"
				: $"SELECT TOP ({P}Take) {Columns} FROM {Tbl("AiDestinationReviews")} WHERE {where} ORDER BY {Col("RequestedOn")}";

			var rows = (await QueryAsync<AiDestinationReview>(sql, parameters, cancellationToken)).ToList();
			foreach (var row in rows)
			{
				row.RequestedOn = DateTime.SpecifyKind(row.RequestedOn, DateTimeKind.Utc);
				if (row.ClaimedOn.HasValue)
					row.ClaimedOn = DateTime.SpecifyKind(row.ClaimedOn.Value, DateTimeKind.Utc);
			}

			return rows;
		}

		public async Task<bool> TryClaimAsync(string reviewId, DateTime claimedOnUtc, DateTime leaseCutoffUtc, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(reviewId))
				return false;

			var parameters = new DynamicParameters(new { Id = reviewId });
			AddTimestamp(parameters, "ClaimedOn", claimedOnUtc);
			AddTimestamp(parameters, "LeaseCutoff", leaseCutoffUtc);

			return await ExecuteAsync(
				$"UPDATE {Tbl("AiDestinationReviews")} SET {Col("ClaimedOn")} = {P}ClaimedOn WHERE {Col("AiDestinationReviewId")} = {P}Id " +
				$"AND {Col("ProcessedOn")} IS NULL AND ({Col("ClaimedOn")} IS NULL OR {Col("ClaimedOn")} < {P}LeaseCutoff)",
				parameters, cancellationToken) == 1;
		}

		public async Task<bool> CompleteAsync(string reviewId, DateTime claimedOnUtc, string outcome, DateTime processedOnUtc, CancellationToken cancellationToken = default)
		{
			var parameters = new DynamicParameters(new { Id = reviewId });
			parameters.Add("Outcome", Truncate(outcome, 200), DbType.String);
			AddTimestamp(parameters, "ClaimedOn", claimedOnUtc);
			AddTimestamp(parameters, "ProcessedOn", processedOnUtc);

			// Only while nothing asked for a newer review since the claim (RequestedOn would have moved past it).
			if (await ExecuteAsync(
					$"UPDATE {Tbl("AiDestinationReviews")} SET {Col("ProcessedOn")} = {P}ProcessedOn, {Col("Outcome")} = {P}Outcome " +
					$"WHERE {Col("AiDestinationReviewId")} = {P}Id AND {Col("ClaimedOn")} = {P}ClaimedOn AND {Col("RequestedOn")} <= {P}ClaimedOn",
					parameters, cancellationToken) == 1)
				return true;

			await ReleaseAsync(reviewId, claimedOnUtc, cancellationToken);
			return false;
		}

		public async Task ReleaseAsync(string reviewId, DateTime claimedOnUtc, CancellationToken cancellationToken = default)
		{
			var parameters = new DynamicParameters(new { Id = reviewId });
			AddTimestamp(parameters, "ClaimedOn", claimedOnUtc);

			await ExecuteAsync(
				$"UPDATE {Tbl("AiDestinationReviews")} SET {Col("ClaimedOn")} = NULL WHERE {Col("AiDestinationReviewId")} = {P}Id " +
				$"AND {Col("ClaimedOn")} = {P}ClaimedOn AND {Col("ProcessedOn")} IS NULL",
				parameters, cancellationToken);
		}

		public Task<int> DeleteProcessedBeforeAsync(DateTime cutoffUtc, CancellationToken cancellationToken = default)
		{
			var parameters = new DynamicParameters();
			AddTimestamp(parameters, "Cutoff", cutoffUtc);

			return ExecuteAsync(
				$"DELETE FROM {Tbl("AiDestinationReviews")} WHERE {Col("ProcessedOn")} IS NOT NULL AND {Col("ProcessedOn")} < {P}Cutoff",
				parameters, cancellationToken);
		}

		private static string Truncate(string value, int max) => string.IsNullOrEmpty(value) || value.Length <= max ? value : value.Substring(0, max);
	}
}
