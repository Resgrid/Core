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
	/// BBP / infectious-disease exposure cases (§13b WL1, M0394). Portable SQL through <see cref="RmsRepositoryBase{T}"/>; member ids are
	/// citext on PostgreSQL and compared through <c>CaseInsensitive</c>. The worker queries read operational columns only.
	/// </summary>
	public class RmsExposureIncidentsRepository : RmsRepositoryBase<RmsExposureIncident>, IRmsExposureIncidentsRepository
	{
		public RmsExposureIncidentsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		private string Table => Tbl("RmsExposureIncidents");

		private static string Closed => $"{(int)ExposureIncidentStages.Closed}, {(int)ExposureIncidentStages.Withdrawn}";

		public Task<RmsExposureIncident> GetByIdForDepartmentAsync(int departmentId, string incidentId)
		{
			return QueryFirstOrDefaultAsync<RmsExposureIncident>(
				$"SELECT * FROM {Table} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsExposureIncidentId")} = {P}Id",
				new { DepartmentId = departmentId, Id = incidentId });
		}

		public Task<IEnumerable<RmsExposureIncident>> GetForUserAsync(int departmentId, string userId)
		{
			return QueryAsync<RmsExposureIncident>(
				$"SELECT * FROM {Table} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("UserId")} = {CaseInsensitive("UserId")} " +
				$"ORDER BY {Col("OccurredOn")} DESC, {Col("RmsExposureIncidentId")}",
				new { DepartmentId = departmentId, UserId = userId });
		}

		public Task<IEnumerable<RmsExposureIncident>> GetForDepartmentAsync(int departmentId, bool includeClosed)
		{
			var open = includeClosed ? string.Empty : $" AND {Col("Stage")} NOT IN ({Closed})";
			return QueryAsync<RmsExposureIncident>(
				$"SELECT * FROM {Table} WHERE {Col("DepartmentId")} = {P}DepartmentId{open} ORDER BY {Col("OccurredOn")} DESC, {Col("RmsExposureIncidentId")}",
				new { DepartmentId = departmentId });
		}

		public Task<IEnumerable<RmsExposureIncident>> GetForExposureAsync(int departmentId, string responderExposureId)
		{
			return QueryAsync<RmsExposureIncident>(
				$"SELECT * FROM {Table} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("ResponderExposureId")} = {P}ExposureId ORDER BY {Col("CreatedOn")}",
				new { DepartmentId = departmentId, ExposureId = responderExposureId });
		}

		public Task<IEnumerable<RmsExposureIncident>> GetOverdueNotificationsAsync(DateTime utcNow, int take)
		{
			var parameters = new DynamicParameters();
			parameters.Add("Now", DatabaseTimestamp(utcNow));
			parameters.Add("Skip", 0);
			parameters.Add("Take", Math.Max(1, take));
			return QueryAsync<RmsExposureIncident>(
				$"SELECT * FROM {Table} WHERE {Col("Stage")} = {(int)ExposureIncidentStages.NotificationRequested} AND {Col("NotificationDueOn")} IS NOT NULL " +
				$"AND {Col("NotificationDueOn")} <= {P}Now AND {Col("NotificationOverdueNoticeOn")} IS NULL " +
				$"ORDER BY {Col("NotificationDueOn")}, {Col("RmsExposureIncidentId")} {Paging()}",
				parameters);
		}

		public async Task<bool> ClaimOverdueNoticeAsync(int departmentId, string incidentId, DateTime sentOn, CancellationToken cancellationToken = default)
		{
			var rows = await ExecuteAsync(
				$"UPDATE {Table} SET {Col("NotificationOverdueNoticeOn")} = {P}SentOn, {Col("RowVersion")} = {Col("RowVersion")} + 1 " +
				$"WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsExposureIncidentId")} = {P}Id AND {Col("NotificationOverdueNoticeOn")} IS NULL " +
				$"AND {Col("Stage")} = {(int)ExposureIncidentStages.NotificationRequested}",
				new { DepartmentId = departmentId, Id = incidentId, SentOn = DatabaseTimestamp(sentOn) }, cancellationToken);
			return rows == 1;
		}

		public Task<IEnumerable<int>> GetDepartmentsWithCasesBeforeAsync(DateTime cutoff, int take)
		{
			var parameters = new DynamicParameters();
			parameters.Add("Cutoff", DatabaseTimestamp(cutoff));
			parameters.Add("Skip", 0);
			parameters.Add("Take", Math.Max(1, take));
			return QueryAsync<int>(
				$"SELECT DISTINCT {Col("DepartmentId")} FROM {Table} WHERE {Col("OccurredOn")} < {P}Cutoff ORDER BY {Col("DepartmentId")} {Paging()}",
				parameters);
		}

		public Task<IEnumerable<RmsExposureIncident>> GetBeforeAsync(int departmentId, DateTime cutoff, int take)
		{
			var parameters = new DynamicParameters();
			parameters.Add("DepartmentId", departmentId);
			parameters.Add("Cutoff", DatabaseTimestamp(cutoff));
			parameters.Add("Skip", 0);
			parameters.Add("Take", Math.Max(1, take));
			return QueryAsync<RmsExposureIncident>(
				$"SELECT * FROM {Table} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("OccurredOn")} < {P}Cutoff ORDER BY {Col("OccurredOn")}, {Col("RmsExposureIncidentId")} {Paging()}",
				parameters);
		}

		public Task<int> DeleteForDepartmentAsync(int departmentId, string incidentId, CancellationToken cancellationToken = default)
		{
			return ExecuteAsync(
				$"DELETE FROM {Table} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsExposureIncidentId")} = {P}Id",
				new { DepartmentId = departmentId, Id = incidentId }, cancellationToken);
		}
	}

	/// <summary>Member injuries with the OSHA 300/301 facts (§13b WL1, M0395).</summary>
	public class RmsMemberInjuriesRepository : RmsRepositoryBase<RmsMemberInjury>, IRmsMemberInjuriesRepository
	{
		public RmsMemberInjuriesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		private string Table => Tbl("RmsMemberInjuries");

		public Task<RmsMemberInjury> GetByIdForDepartmentAsync(int departmentId, string injuryId)
		{
			return QueryFirstOrDefaultAsync<RmsMemberInjury>(
				$"SELECT * FROM {Table} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsMemberInjuryId")} = {P}Id",
				new { DepartmentId = departmentId, Id = injuryId });
		}

		public Task<IEnumerable<RmsMemberInjury>> GetForUserAsync(int departmentId, string userId)
		{
			return QueryAsync<RmsMemberInjury>(
				$"SELECT * FROM {Table} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("UserId")} = {CaseInsensitive("UserId")} " +
				$"ORDER BY {Col("OccurredOn")} DESC, {Col("RmsMemberInjuryId")}",
				new { DepartmentId = departmentId, UserId = userId });
		}

		public Task<IEnumerable<RmsMemberInjury>> GetOpenForDepartmentAsync(int departmentId)
		{
			return QueryAsync<RmsMemberInjury>(
				$"SELECT * FROM {Table} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("State")} = {(int)MemberInjuryStates.Open} " +
				$"ORDER BY {Col("OccurredOn")}, {Col("RmsMemberInjuryId")}",
				new { DepartmentId = departmentId });
		}

		public Task<IEnumerable<RmsMemberInjury>> GetForYearAsync(int departmentId, int year)
		{
			return QueryAsync<RmsMemberInjury>(
				$"SELECT * FROM {Table} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("OshaYear")} = {P}Year ORDER BY {Col("OshaCaseNumber")}, {Col("OccurredOn")}",
				new { DepartmentId = departmentId, Year = year });
		}

		public async Task<int> GetMaxCaseNumberAsync(int departmentId, int year)
		{
			var max = await QueryFirstOrDefaultAsync<int?>(
				$"SELECT MAX({Col("OshaCaseNumber")}) FROM {Table} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("OshaYear")} = {P}Year",
				new { DepartmentId = departmentId, Year = year });
			return max ?? 0;
		}

		public Task<IEnumerable<RmsMemberInjury>> GetDueReturnReviewsAsync(DateTime utcNow, int take)
		{
			var parameters = new DynamicParameters();
			parameters.Add("Now", DatabaseTimestamp(utcNow));
			parameters.Add("Skip", 0);
			parameters.Add("Take", Math.Max(1, take));
			return QueryAsync<RmsMemberInjury>(
				$"SELECT * FROM {Table} WHERE {Col("State")} = {(int)MemberInjuryStates.Open} AND {Col("ExpectedReturnOn")} IS NOT NULL AND {Col("ExpectedReturnOn")} <= {P}Now " +
				$"AND ({Col("ReturnReviewNoticeOn")} IS NULL OR {Col("ReturnReviewNoticeOn")} < {Col("ExpectedReturnOn")}) " +
				$"ORDER BY {Col("ExpectedReturnOn")}, {Col("RmsMemberInjuryId")} {Paging()}",
				parameters);
		}

		public async Task<bool> ClaimReturnReviewAsync(int departmentId, string injuryId, DateTime? expected, DateTime sentOn, CancellationToken cancellationToken = default)
		{
			// The expected value decides the SQL shape in C#: a null DateTime parameter tested with IS NULL fails on PostgreSQL (42P08).
			var parameters = new DynamicParameters();
			parameters.Add("DepartmentId", departmentId);
			parameters.Add("Id", injuryId);
			parameters.Add("SentOn", DatabaseTimestamp(sentOn));
			var guard = $"{Col("ReturnReviewNoticeOn")} IS NULL";
			if (expected.HasValue)
			{
				guard = $"{Col("ReturnReviewNoticeOn")} = {P}Expected";
				parameters.Add("Expected", DatabaseTimestamp(expected.Value));
			}

			var rows = await ExecuteAsync(
				$"UPDATE {Table} SET {Col("ReturnReviewNoticeOn")} = {P}SentOn, {Col("RowVersion")} = {Col("RowVersion")} + 1 " +
				$"WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsMemberInjuryId")} = {P}Id AND {guard}",
				parameters, cancellationToken);
			return rows == 1;
		}

		public Task<IEnumerable<int>> GetDepartmentsWithInjuriesBeforeYearAsync(int beforeYear, int take)
		{
			var parameters = new DynamicParameters();
			parameters.Add("Year", beforeYear);
			parameters.Add("Skip", 0);
			parameters.Add("Take", Math.Max(1, take));
			return QueryAsync<int>(
				$"SELECT DISTINCT {Col("DepartmentId")} FROM {Table} WHERE {Col("OshaYear")} < {P}Year ORDER BY {Col("DepartmentId")} {Paging()}",
				parameters);
		}

		public Task<IEnumerable<RmsMemberInjury>> GetBeforeYearAsync(int departmentId, int beforeYear, int take)
		{
			var parameters = new DynamicParameters();
			parameters.Add("DepartmentId", departmentId);
			parameters.Add("Year", beforeYear);
			parameters.Add("Skip", 0);
			parameters.Add("Take", Math.Max(1, take));
			return QueryAsync<RmsMemberInjury>(
				$"SELECT * FROM {Table} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("OshaYear")} < {P}Year ORDER BY {Col("OshaYear")}, {Col("RmsMemberInjuryId")} {Paging()}",
				parameters);
		}

		public Task<int> DeleteForDepartmentAsync(int departmentId, string injuryId, CancellationToken cancellationToken = default)
		{
			return ExecuteAsync(
				$"DELETE FROM {Table} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsMemberInjuryId")} = {P}Id",
				new { DepartmentId = departmentId, Id = injuryId }, cancellationToken);
		}
	}

	/// <summary>The duty status history of injuries (§13b WL1, M0395).</summary>
	public class RmsInjuryDutyPeriodsRepository : RmsRepositoryBase<RmsInjuryDutyPeriod>, IRmsInjuryDutyPeriodsRepository
	{
		public RmsInjuryDutyPeriodsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		private string Table => Tbl("RmsInjuryDutyPeriods");

		public Task<IEnumerable<RmsInjuryDutyPeriod>> GetForInjuryAsync(int departmentId, string injuryId)
		{
			return QueryAsync<RmsInjuryDutyPeriod>(
				$"SELECT * FROM {Table} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("InjuryId")} = {P}InjuryId ORDER BY {Col("StartsOn")}, {Col("CreatedOn")}",
				new { DepartmentId = departmentId, InjuryId = injuryId });
		}

		public Task<IEnumerable<RmsInjuryDutyPeriod>> GetForInjuriesAsync(int departmentId, IEnumerable<string> injuryIds)
		{
			var ids = InListValue(injuryIds);
			if (ids.Length == 0)
				return Task.FromResult<IEnumerable<RmsInjuryDutyPeriod>>(new List<RmsInjuryDutyPeriod>());
			return QueryAsync<RmsInjuryDutyPeriod>(
				$"SELECT * FROM {Table} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {InList("InjuryId", "Ids")} ORDER BY {Col("InjuryId")}, {Col("StartsOn")}, {Col("CreatedOn")}",
				new { DepartmentId = departmentId, Ids = ids });
		}

		public Task<int> DeleteForInjuryAsync(int departmentId, string injuryId, CancellationToken cancellationToken = default)
		{
			return ExecuteAsync(
				$"DELETE FROM {Table} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("InjuryId")} = {P}InjuryId",
				new { DepartmentId = departmentId, InjuryId = injuryId }, cancellationToken);
		}
	}

	/// <summary>NFPA 1582 medical clearances (§13b WL1, M0396).</summary>
	public class RmsMedicalClearancesRepository : RmsRepositoryBase<RmsMedicalClearance>, IRmsMedicalClearancesRepository
	{
		public RmsMedicalClearancesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		private string Table => Tbl("RmsMedicalClearances");

		public Task<RmsMedicalClearance> GetByIdForDepartmentAsync(int departmentId, string clearanceId)
		{
			return QueryFirstOrDefaultAsync<RmsMedicalClearance>(
				$"SELECT * FROM {Table} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsMedicalClearanceId")} = {P}Id",
				new { DepartmentId = departmentId, Id = clearanceId });
		}

		public Task<IEnumerable<RmsMedicalClearance>> GetForUserAsync(int departmentId, string userId)
		{
			return QueryAsync<RmsMedicalClearance>(
				$"SELECT * FROM {Table} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("UserId")} = {CaseInsensitive("UserId")} " +
				$"ORDER BY {Col("EvaluatedOn")} DESC, {Col("CreatedOn")} DESC",
				new { DepartmentId = departmentId, UserId = userId });
		}

		public Task<IEnumerable<RmsMedicalClearance>> GetActiveForDepartmentAsync(int departmentId)
		{
			return QueryAsync<RmsMedicalClearance>(
				$"SELECT * FROM {Table} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("VoidedOn")} IS NULL ORDER BY {Col("UserId")}, {Col("EvaluatedOn")} DESC",
				new { DepartmentId = departmentId });
		}

		public Task<IEnumerable<int>> GetDepartmentsWithClearancesBeforeAsync(DateTime cutoff, int take)
		{
			var parameters = new DynamicParameters();
			parameters.Add("Cutoff", DatabaseTimestamp(cutoff));
			parameters.Add("Skip", 0);
			parameters.Add("Take", Math.Max(1, take));
			return QueryAsync<int>(
				$"SELECT DISTINCT {Col("DepartmentId")} FROM {Table} WHERE {Col("EvaluatedOn")} < {P}Cutoff ORDER BY {Col("DepartmentId")} {Paging()}",
				parameters);
		}

		public Task<IEnumerable<RmsMedicalClearance>> GetBeforeAsync(int departmentId, DateTime cutoff, int take)
		{
			var parameters = new DynamicParameters();
			parameters.Add("DepartmentId", departmentId);
			parameters.Add("Cutoff", DatabaseTimestamp(cutoff));
			parameters.Add("Skip", 0);
			parameters.Add("Take", Math.Max(1, take));
			return QueryAsync<RmsMedicalClearance>(
				$"SELECT * FROM {Table} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("EvaluatedOn")} < {P}Cutoff ORDER BY {Col("EvaluatedOn")}, {Col("RmsMedicalClearanceId")} {Paging()}",
				parameters);
		}

		public Task<int> DeleteForDepartmentAsync(int departmentId, string clearanceId, CancellationToken cancellationToken = default)
		{
			return ExecuteAsync(
				$"DELETE FROM {Table} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsMedicalClearanceId")} = {P}Id",
				new { DepartmentId = departmentId, Id = clearanceId }, cancellationToken);
		}
	}
}
