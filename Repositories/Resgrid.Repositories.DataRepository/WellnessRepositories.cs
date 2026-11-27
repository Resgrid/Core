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
	/// Member exposure and critical-incident entries (§13b WL0, M0360). Portable SQL through <see cref="RmsRepositoryBase{T}"/>; the
	/// member id is citext on PostgreSQL and compared through <c>CaseInsensitive</c>. The worker queries read operational columns only.
	/// </summary>
	public class RmsResponderExposuresRepository : RmsRepositoryBase<RmsResponderExposure>, IRmsResponderExposuresRepository
	{
		public RmsResponderExposuresRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		private string Table => Tbl("RmsResponderExposures");

		public Task<RmsResponderExposure> GetByIdForDepartmentAsync(int departmentId, string exposureId)
		{
			return QueryFirstOrDefaultAsync<RmsResponderExposure>(
				$"SELECT * FROM {Table} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsResponderExposureId")} = {P}Id",
				new { DepartmentId = departmentId, Id = exposureId });
		}

		public Task<IEnumerable<RmsResponderExposure>> GetForUserAsync(int departmentId, string userId)
		{
			return QueryAsync<RmsResponderExposure>(
				$"SELECT * FROM {Table} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("UserId")} = {CaseInsensitive("UserId")} " +
				$"ORDER BY {Col("OccurredOn")} DESC, {Col("RmsResponderExposureId")}",
				new { DepartmentId = departmentId, UserId = userId });
		}

		public Task<IEnumerable<RmsResponderExposure>> GetForDepartmentAsync(int departmentId, DateTime? fromUtc, DateTime? toUtc)
		{
			// The window decides the SQL shape in C#: a null DateTime parameter tested with IS NULL fails on PostgreSQL (42P08).
			var parameters = new DynamicParameters();
			parameters.Add("DepartmentId", departmentId);
			var window = string.Empty;
			if (fromUtc.HasValue)
			{
				window += $" AND {Col("OccurredOn")} >= {P}FromUtc";
				parameters.Add("FromUtc", DatabaseTimestamp(fromUtc.Value));
			}
			if (toUtc.HasValue)
			{
				window += $" AND {Col("OccurredOn")} < {P}ToUtc";
				parameters.Add("ToUtc", DatabaseTimestamp(toUtc.Value));
			}

			return QueryAsync<RmsResponderExposure>(
				$"SELECT * FROM {Table} WHERE {Col("DepartmentId")} = {P}DepartmentId{window} ORDER BY {Col("OccurredOn")} DESC, {Col("RmsResponderExposureId")}",
				parameters);
		}

		public Task<IEnumerable<RmsResponderExposure>> GetForCallAsync(int departmentId, int callId)
		{
			return QueryAsync<RmsResponderExposure>(
				$"SELECT * FROM {Table} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("CallId")} = {P}CallId ORDER BY {Col("CreatedOn")}, {Col("RmsResponderExposureId")}",
				new { DepartmentId = departmentId, CallId = callId });
		}

		public Task<IEnumerable<RmsResponderExposure>> GetFromFlagAsync(int departmentId, string callExposureFlagId)
		{
			return QueryAsync<RmsResponderExposure>(
				$"SELECT * FROM {Table} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("CallExposureFlagId")} = {P}FlagId ORDER BY {Col("CreatedOn")}, {Col("RmsResponderExposureId")}",
				new { DepartmentId = departmentId, FlagId = callExposureFlagId });
		}

		public Task<int> DeleteSuggestionsFromFlagAsync(int departmentId, string callExposureFlagId, CancellationToken cancellationToken = default)
		{
			return ExecuteAsync(
				$"DELETE FROM {Table} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("CallExposureFlagId")} = {P}FlagId AND {Col("Status")} = {(int)ResponderExposureStatuses.Suggested}",
				new { DepartmentId = departmentId, FlagId = callExposureFlagId }, cancellationToken);
		}

		private string DueAnniversary =>
			$"{Col("Status")} = {(int)ResponderExposureStatuses.Confirmed} AND ({Col("RemindMember")} = {P}Yes OR {Col("RemindPeerTeam")} = {P}Yes) " +
			$"AND {Col("NextAnniversaryOn")} IS NOT NULL AND {Col("NextAnniversaryOn")} <= {P}DueBy";

		public Task<IEnumerable<int>> GetDepartmentsWithDueAnniversariesAsync(DateTime dueBy, int take)
		{
			var parameters = new DynamicParameters();
			parameters.Add("Yes", true);
			parameters.Add("DueBy", DatabaseTimestamp(dueBy));
			parameters.Add("Skip", 0);
			parameters.Add("Take", Math.Max(1, take));
			return QueryAsync<int>(
				$"SELECT DISTINCT {Col("DepartmentId")} FROM {Table} WHERE {DueAnniversary} ORDER BY {Col("DepartmentId")} {Paging()}",
				parameters);
		}

		public Task<IEnumerable<RmsResponderExposure>> GetDueAnniversariesAsync(int departmentId, DateTime dueBy, int take)
		{
			var parameters = new DynamicParameters();
			parameters.Add("DepartmentId", departmentId);
			parameters.Add("Yes", true);
			parameters.Add("DueBy", DatabaseTimestamp(dueBy));
			parameters.Add("Skip", 0);
			parameters.Add("Take", Math.Max(1, take));
			return QueryAsync<RmsResponderExposure>(
				$"SELECT * FROM {Table} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {DueAnniversary} ORDER BY {Col("NextAnniversaryOn")}, {Col("RmsResponderExposureId")} {Paging()}",
				parameters);
		}

		public async Task<bool> ClaimAnniversaryAsync(int departmentId, string exposureId, DateTime expected, DateTime next, DateTime sentOn, CancellationToken cancellationToken = default)
		{
			var rows = await ExecuteAsync(
				$"UPDATE {Table} SET {Col("NextAnniversaryOn")} = {P}Next, {Col("LastReminderSentOn")} = {P}SentOn, {Col("RowVersion")} = {Col("RowVersion")} + 1 " +
				$"WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsResponderExposureId")} = {P}Id AND {Col("NextAnniversaryOn")} = {P}Expected",
				new { DepartmentId = departmentId, Id = exposureId, Expected = DatabaseTimestamp(expected), Next = DatabaseTimestamp(next), SentOn = DatabaseTimestamp(sentOn) },
				cancellationToken);
			return rows == 1;
		}

		public Task<IEnumerable<int>> GetDepartmentsWithEntriesBeforeAsync(DateTime cutoff, int take)
		{
			var parameters = new DynamicParameters();
			parameters.Add("Cutoff", DatabaseTimestamp(cutoff));
			parameters.Add("Skip", 0);
			parameters.Add("Take", Math.Max(1, take));
			return QueryAsync<int>(
				$"SELECT DISTINCT {Col("DepartmentId")} FROM {Table} WHERE {Col("OccurredOn")} < {P}Cutoff ORDER BY {Col("DepartmentId")} {Paging()}",
				parameters);
		}

		public Task<IEnumerable<RmsResponderExposure>> GetBeforeAsync(int departmentId, DateTime cutoff, int take)
		{
			var parameters = new DynamicParameters();
			parameters.Add("DepartmentId", departmentId);
			parameters.Add("Cutoff", DatabaseTimestamp(cutoff));
			parameters.Add("Skip", 0);
			parameters.Add("Take", Math.Max(1, take));
			return QueryAsync<RmsResponderExposure>(
				$"SELECT * FROM {Table} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("OccurredOn")} < {P}Cutoff ORDER BY {Col("OccurredOn")}, {Col("RmsResponderExposureId")} {Paging()}",
				parameters);
		}

		public Task<int> DeleteForDepartmentAsync(int departmentId, string exposureId, CancellationToken cancellationToken = default)
		{
			return ExecuteAsync(
				$"DELETE FROM {Table} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsResponderExposureId")} = {P}Id",
				new { DepartmentId = departmentId, Id = exposureId }, cancellationToken);
		}
	}

	/// <summary>Post-call critical-incident and exposure flags (§13b WL0, M0360).</summary>
	public class RmsCallExposureFlagsRepository : RmsRepositoryBase<RmsCallExposureFlag>, IRmsCallExposureFlagsRepository
	{
		public RmsCallExposureFlagsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		private string Table => Tbl("RmsCallExposureFlags");

		public Task<RmsCallExposureFlag> GetByIdForDepartmentAsync(int departmentId, string flagId)
		{
			return QueryFirstOrDefaultAsync<RmsCallExposureFlag>(
				$"SELECT * FROM {Table} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsCallExposureFlagId")} = {P}Id",
				new { DepartmentId = departmentId, Id = flagId });
		}

		public Task<IEnumerable<RmsCallExposureFlag>> GetForCallAsync(int departmentId, int callId)
		{
			return QueryAsync<RmsCallExposureFlag>(
				$"SELECT * FROM {Table} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("CallId")} = {P}CallId ORDER BY {Col("Category")}",
				new { DepartmentId = departmentId, CallId = callId });
		}

		public Task<IEnumerable<RmsCallExposureFlag>> GetRecentForDepartmentAsync(int departmentId, int take)
		{
			var parameters = new DynamicParameters();
			parameters.Add("DepartmentId", departmentId);
			parameters.Add("Skip", 0);
			parameters.Add("Take", Math.Max(1, take));
			return QueryAsync<RmsCallExposureFlag>(
				$"SELECT * FROM {Table} WHERE {Col("DepartmentId")} = {P}DepartmentId ORDER BY {Col("FlaggedOn")} DESC, {Col("RmsCallExposureFlagId")} {Paging()}",
				parameters);
		}

		public Task<IEnumerable<RmsCallExposureFlag>> GetPendingAsync(int take)
		{
			var parameters = new DynamicParameters();
			parameters.Add("Skip", 0);
			parameters.Add("Take", Math.Max(1, take));
			return QueryAsync<RmsCallExposureFlag>(
				$"SELECT * FROM {Table} WHERE {Col("SuggestionsGeneratedOn")} IS NULL AND {Col("ClearedOn")} IS NULL ORDER BY {Col("FlaggedOn")}, {Col("RmsCallExposureFlagId")} {Paging()}",
				parameters);
		}

		public async Task<bool> ClaimGenerationAsync(int departmentId, string flagId, DateTime generatedOn, CancellationToken cancellationToken = default)
		{
			var rows = await ExecuteAsync(
				$"UPDATE {Table} SET {Col("SuggestionsGeneratedOn")} = {P}On, {Col("RowVersion")} = {Col("RowVersion")} + 1 " +
				$"WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsCallExposureFlagId")} = {P}Id AND {Col("SuggestionsGeneratedOn")} IS NULL AND {Col("ClearedOn")} IS NULL",
				new { DepartmentId = departmentId, Id = flagId, On = DatabaseTimestamp(generatedOn) }, cancellationToken);
			return rows == 1;
		}

		public Task<int> SetSuggestionCountAsync(int departmentId, string flagId, int count, CancellationToken cancellationToken = default)
		{
			return ExecuteAsync(
				$"UPDATE {Table} SET {Col("SuggestionCount")} = {P}Count WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsCallExposureFlagId")} = {P}Id",
				new { DepartmentId = departmentId, Id = flagId, Count = count }, cancellationToken);
		}

		public Task<int> ReleaseGenerationAsync(int departmentId, string flagId, CancellationToken cancellationToken = default)
		{
			return ExecuteAsync(
				$"UPDATE {Table} SET {Col("SuggestionsGeneratedOn")} = NULL WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsCallExposureFlagId")} = {P}Id",
				new { DepartmentId = departmentId, Id = flagId }, cancellationToken);
		}
	}

	/// <summary>At-risk screening flags (§13b WL0, M0360).</summary>
	public class RmsResponderScreeningFlagsRepository : RmsRepositoryBase<RmsResponderScreeningFlag>, IRmsResponderScreeningFlagsRepository
	{
		public RmsResponderScreeningFlagsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		private string Table => Tbl("RmsResponderScreeningFlags");

		public Task<RmsResponderScreeningFlag> GetByIdForDepartmentAsync(int departmentId, string flagId)
		{
			return QueryFirstOrDefaultAsync<RmsResponderScreeningFlag>(
				$"SELECT * FROM {Table} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsResponderScreeningFlagId")} = {P}Id",
				new { DepartmentId = departmentId, Id = flagId });
		}

		public Task<RmsResponderScreeningFlag> GetActiveForUserAsync(int departmentId, string userId)
		{
			return QueryFirstOrDefaultAsync<RmsResponderScreeningFlag>(
				$"SELECT * FROM {Table} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("UserId")} = {CaseInsensitive("UserId")} AND {Col("ClearedOn")} IS NULL " +
				$"ORDER BY {Col("FlaggedOn")} DESC",
				new { DepartmentId = departmentId, UserId = userId });
		}

		public Task<IEnumerable<RmsResponderScreeningFlag>> GetActiveForDepartmentAsync(int departmentId)
		{
			return QueryAsync<RmsResponderScreeningFlag>(
				$"SELECT * FROM {Table} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("ClearedOn")} IS NULL ORDER BY {Col("FlaggedOn")} DESC",
				new { DepartmentId = departmentId });
		}

		public Task<IEnumerable<RmsResponderScreeningFlag>> GetForUserAsync(int departmentId, string userId)
		{
			return QueryAsync<RmsResponderScreeningFlag>(
				$"SELECT * FROM {Table} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("UserId")} = {CaseInsensitive("UserId")} ORDER BY {Col("FlaggedOn")} DESC",
				new { DepartmentId = departmentId, UserId = userId });
		}
	}
}
