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
	/// <summary>Recurring call schedules (M0319) and the calendar reads of scheduled calls.</summary>
	public class RecurringCallsRepository : RmsRepositoryBase<RecurringCall>, IRecurringCallsRepository
	{
		public RecurringCallsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<IEnumerable<RecurringCall>> GetActiveAsync()
		{
			return QueryAsync<RecurringCall>(
				$"SELECT * FROM {Tbl("RecurringCalls")} WHERE {Col("IsActive")} = {P}Yes AND {Col("IsDeleted")} = {P}No",
				new { Yes = true, No = false });
		}

		public Task<IEnumerable<RecurringCall>> GetPausedWithResumeDateAsync()
		{
			return QueryAsync<RecurringCall>(
				$"SELECT * FROM {Tbl("RecurringCalls")} WHERE {Col("IsActive")} = {P}No AND {Col("IsDeleted")} = {P}No AND {Col("ResumeOn")} IS NOT NULL",
				new { No = false });
		}

		public Task<IEnumerable<RecurringCall>> GetForDepartmentAsync(int departmentId)
		{
			return QueryAsync<RecurringCall>(
				$"SELECT * FROM {Tbl("RecurringCalls")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("IsDeleted")} = {P}No",
				new { DepartmentId = departmentId, No = false });
		}

		public Task<IEnumerable<Call>> GetScheduledCallsInRangeAsync(int departmentId, DateTime fromUtc, DateTime toUtc)
		{
			// A waiting scheduled call is active (0) and not sent; a pending call (8) has no dispatch time of its own. Calls a
			// schedule created stay on the calendar after they go out.
			var parameters = new DynamicParameters(new { DepartmentId = departmentId, No = false });
			AddTimestamp(parameters, "From", fromUtc);
			AddTimestamp(parameters, "To", toUtc);

			return QueryAsync<Call>(
				$"SELECT * FROM {Tbl("Calls")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("IsDeleted")} = {P}No " +
				$"AND {Col("DispatchOn")} IS NOT NULL AND {Col("DispatchOn")} >= {P}From AND {Col("DispatchOn")} < {P}To " +
				$"AND ({Col("RecurringCallId")} IS NOT NULL OR ({Col("State")} = 0 AND ({Col("HasBeenDispatched")} IS NULL OR {Col("HasBeenDispatched")} = {P}No)))",
				parameters);
		}

		public Task<IEnumerable<Call>> GetCallsForScheduleAsync(int departmentId, int recurringCallId, DateTime fromUtc)
		{
			var parameters = new DynamicParameters(new { DepartmentId = departmentId, RecurringCallId = recurringCallId, No = false });
			AddTimestamp(parameters, "From", fromUtc);

			return QueryAsync<Call>(
				$"SELECT * FROM {Tbl("Calls")} WHERE {Col("RecurringCallId")} = {P}RecurringCallId AND {Col("DepartmentId")} = {P}DepartmentId " +
				$"AND {Col("IsDeleted")} = {P}No AND {Col("DispatchOn")} >= {P}From",
				parameters);
		}

		public async Task<Call> GetCallForOccurrenceAsync(int recurringCallId, DateTime occurrenceUtc, DateTime loggedSinceUtc)
		{
			var parameters = new DynamicParameters(new { RecurringCallId = recurringCallId, No = false });
			AddTimestamp(parameters, "OccurrenceOn", occurrenceUtc);
			AddTimestamp(parameters, "LoggedSince", loggedSinceUtc);

			var calls = await QueryAsync<Call>(
				$"SELECT * FROM {Tbl("Calls")} WHERE {Col("RecurringCallId")} = {P}RecurringCallId AND {Col("DispatchOn")} = {P}OccurrenceOn " +
				$"AND {Col("LoggedOn")} >= {P}LoggedSince AND {Col("IsDeleted")} = {P}No",
				parameters);

			return calls?.OrderBy(x => x.CallId).FirstOrDefault();
		}

		/// <summary>datetime2 on SQL Server (a datetime parameter compared with a datetime2 column is widened and can miss); unspecified kind for Npgsql.</summary>
		internal static void AddTimestamp(DynamicParameters parameters, string name, DateTime value)
		{
			if (IsPostgres)
				parameters.Add(name, DateTime.SpecifyKind(value, DateTimeKind.Unspecified));
			else
				parameters.Add(name, value, DbType.DateTime2);
		}
	}

	/// <summary>Occurrences of recurring call schedules (M0319).</summary>
	public class RecurringCallOccurrencesRepository : RmsRepositoryBase<RecurringCallOccurrence>, IRecurringCallOccurrencesRepository
	{
		public RecurringCallOccurrencesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<bool> TryClaimAsync(RecurringCallOccurrence occurrence, CancellationToken cancellationToken = default)
		{
			return InsertClaimAsync(occurrence, null, cancellationToken);
		}

		public Task<bool> TryClaimWhileRunningAsync(RecurringCallOccurrence occurrence, DateTime scheduleRevisionUtc, CancellationToken cancellationToken = default)
		{
			return InsertClaimAsync(occurrence, scheduleRevisionUtc, cancellationToken);
		}

		private async Task<bool> InsertClaimAsync(RecurringCallOccurrence occurrence, DateTime? scheduleRevisionUtc, CancellationToken cancellationToken)
		{
			if (occurrence == null)
				return false;

			if (string.IsNullOrWhiteSpace(occurrence.RecurringCallOccurrenceId))
				occurrence.RecurringCallOccurrenceId = Guid.NewGuid().ToString();

			var parameters = new DynamicParameters(new
			{
				Id = occurrence.RecurringCallOccurrenceId,
				occurrence.RecurringCallId,
				occurrence.DepartmentId,
				occurrence.State,
				occurrence.CallId,
				occurrence.UserId
			});
			RecurringCallsRepository.AddTimestamp(parameters, "OccurrenceOn", occurrence.OccurrenceOn);
			RecurringCallsRepository.AddTimestamp(parameters, "UpdatedOn", occurrence.UpdatedOn);

			var running = scheduleRevisionUtc.HasValue ? " AND " + ScheduleRuns(parameters, scheduleRevisionUtc.Value) : string.Empty;

			// The unique (RecurringCallId, OccurrenceOn) index decides between racing claimers: one insert per statement, so
			// a duplicate only ever fails its own row (and, on PostgreSQL, nothing else, as no transaction is held here).
			var sql = IsPostgres
				? $"INSERT INTO {Tbl("RecurringCallOccurrences")} ({Cols("RecurringCallOccurrenceId", "RecurringCallId", "DepartmentId", "OccurrenceOn", "State", "CallId", "UserId", "UpdatedOn")}) " +
				  $"SELECT {P}Id, {P}RecurringCallId, {P}DepartmentId, {P}OccurrenceOn, {P}State, {P}CallId, {P}UserId, {P}UpdatedOn WHERE 1 = 1{running} " +
				  $"ON CONFLICT ({Cols("RecurringCallId", "OccurrenceOn")}) DO NOTHING"
				: $"INSERT INTO {Tbl("RecurringCallOccurrences")} ({Cols("RecurringCallOccurrenceId", "RecurringCallId", "DepartmentId", "OccurrenceOn", "State", "CallId", "UserId", "UpdatedOn")}) " +
				  $"SELECT {P}Id, {P}RecurringCallId, {P}DepartmentId, {P}OccurrenceOn, {P}State, {P}CallId, {P}UserId, {P}UpdatedOn " +
				  $"WHERE NOT EXISTS (SELECT 1 FROM {Tbl("RecurringCallOccurrences")} WITH (UPDLOCK, HOLDLOCK) WHERE {Col("RecurringCallId")} = {P}RecurringCallId AND {Col("OccurrenceOn")} = {P}OccurrenceOn){running}";

			try
			{
				return await ExecuteAsync(sql, parameters, cancellationToken) == 1;
			}
			catch (Exception ex) when (IsUniqueViolation(ex))
			{
				return false;
			}
		}

		/// <summary>
		/// The occurrence's schedule (<paramref name="scheduleId"/>, @RecurringCallId by default) is active, not deleted and still the
		/// revision read (its last-change time, to the second either way: a schedule just saved can come back rounded). The schedule row
		/// is locked for the check (UPDLOCK on SQL Server, FOR SHARE on PostgreSQL), so a pause or change waits for this statement or is
		/// seen by it.
		/// </summary>
		private string ScheduleRuns(DynamicParameters parameters, DateTime scheduleRevisionUtc, string scheduleId = null)
		{
			scheduleId ??= $"{P}RecurringCallId";
			parameters.Add("ScheduleActive", true);
			parameters.Add("ScheduleDeleted", false);
			RecurringCallsRepository.AddTimestamp(parameters, "RevisionFrom", scheduleRevisionUtc.AddSeconds(-1));
			RecurringCallsRepository.AddTimestamp(parameters, "RevisionTo", scheduleRevisionUtc.AddSeconds(1));

			var revision = $"COALESCE(s.{Col("UpdatedOn")}, s.{Col("CreatedOn")})";
			return IsPostgres
				? $"EXISTS (SELECT 1 FROM {Tbl("RecurringCalls")} s WHERE s.{Col("RecurringCallId")} = {scheduleId} AND s.{Col("IsActive")} = {P}ScheduleActive " +
				  $"AND s.{Col("IsDeleted")} = {P}ScheduleDeleted AND {revision} > {P}RevisionFrom AND {revision} < {P}RevisionTo FOR SHARE)"
				: $"EXISTS (SELECT 1 FROM {Tbl("RecurringCalls")} s WITH (UPDLOCK, HOLDLOCK) WHERE s.{Col("RecurringCallId")} = {scheduleId} " +
				  $"AND s.{Col("IsActive")} = {P}ScheduleActive AND s.{Col("IsDeleted")} = {P}ScheduleDeleted AND {revision} > {P}RevisionFrom AND {revision} < {P}RevisionTo)";
		}

		public Task<IEnumerable<RecurringCallOccurrence>> GetForScheduleInRangeAsync(int recurringCallId, DateTime fromUtc, DateTime toUtc)
		{
			var parameters = new DynamicParameters(new { RecurringCallId = recurringCallId });
			RecurringCallsRepository.AddTimestamp(parameters, "From", fromUtc);
			RecurringCallsRepository.AddTimestamp(parameters, "To", toUtc);

			return QueryAsync<RecurringCallOccurrence>(
				$"SELECT * FROM {Tbl("RecurringCallOccurrences")} WHERE {Col("RecurringCallId")} = {P}RecurringCallId " +
				$"AND {Col("OccurrenceOn")} >= {P}From AND {Col("OccurrenceOn")} < {P}To",
				parameters);
		}

		public Task<IEnumerable<RecurringCallOccurrence>> GetForDepartmentInRangeAsync(int departmentId, DateTime fromUtc, DateTime toUtc)
		{
			var parameters = new DynamicParameters(new { DepartmentId = departmentId });
			RecurringCallsRepository.AddTimestamp(parameters, "From", fromUtc);
			RecurringCallsRepository.AddTimestamp(parameters, "To", toUtc);

			return QueryAsync<RecurringCallOccurrence>(
				$"SELECT * FROM {Tbl("RecurringCallOccurrences")} WHERE {Col("DepartmentId")} = {P}DepartmentId " +
				$"AND {Col("OccurrenceOn")} >= {P}From AND {Col("OccurrenceOn")} < {P}To",
				parameters);
		}

		public async Task<RecurringCallOccurrence> GetByOccurrenceAsync(int recurringCallId, DateTime occurrenceUtc)
		{
			var parameters = new DynamicParameters(new { RecurringCallId = recurringCallId });
			RecurringCallsRepository.AddTimestamp(parameters, "OccurrenceOn", occurrenceUtc);

			var rows = await QueryAsync<RecurringCallOccurrence>(
				$"SELECT * FROM {Tbl("RecurringCallOccurrences")} WHERE {Col("RecurringCallId")} = {P}RecurringCallId AND {Col("OccurrenceOn")} = {P}OccurrenceOn",
				parameters);

			return rows?.FirstOrDefault();
		}

		public async Task<RecurringCallOccurrence> GetByCallAsync(int recurringCallId, int callId)
		{
			var rows = await QueryAsync<RecurringCallOccurrence>(
				$"SELECT * FROM {Tbl("RecurringCallOccurrences")} WHERE {Col("RecurringCallId")} = {P}RecurringCallId AND {Col("CallId")} = {P}CallId",
				new { RecurringCallId = recurringCallId, CallId = callId });

			return rows?.FirstOrDefault();
		}

		public Task<IEnumerable<RecurringCallOccurrence>> GetStaleClaimsAsync(DateTime claimedBeforeUtc)
		{
			var parameters = new DynamicParameters(new { State = (int)RecurringCallOccurrenceStates.Creating });
			RecurringCallsRepository.AddTimestamp(parameters, "Before", claimedBeforeUtc);

			return QueryAsync<RecurringCallOccurrence>(
				$"SELECT * FROM {Tbl("RecurringCallOccurrences")} WHERE {Col("State")} = {P}State AND {Col("UpdatedOn")} < {P}Before",
				parameters);
		}

		public async Task<bool> DeleteIfStateAsync(string recurringCallOccurrenceId, int expectedState, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(recurringCallOccurrenceId))
				return false;

			return await ExecuteAsync(
				$"DELETE FROM {Tbl("RecurringCallOccurrences")} WHERE {Col("RecurringCallOccurrenceId")} = {CaseInsensitive("Id")} AND {Col("State")} = {P}State",
				new { Id = recurringCallOccurrenceId, State = expectedState }, cancellationToken) == 1;
		}

		public async Task<bool> TryTransitionStateAsync(string recurringCallOccurrenceId, int fromState, int toState, string userId, DateTime updatedOnUtc,
			CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(recurringCallOccurrenceId))
				return false;

			var parameters = new DynamicParameters(new { Id = recurringCallOccurrenceId, From = fromState, To = toState, UserId = userId });
			RecurringCallsRepository.AddTimestamp(parameters, "UpdatedOn", updatedOnUtc);

			return await ExecuteAsync(
				$"UPDATE {Tbl("RecurringCallOccurrences")} SET {Col("State")} = {P}To, {Col("UserId")} = {P}UserId, {Col("UpdatedOn")} = {P}UpdatedOn " +
				$"WHERE {Col("RecurringCallOccurrenceId")} = {CaseInsensitive("Id")} AND {Col("State")} = {P}From",
				parameters, cancellationToken) == 1;
		}

		public async Task<bool> TryCompleteClaimAsync(string recurringCallOccurrenceId, int callId, DateTime claimedOnUtc, DateTime updatedOnUtc,
			CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(recurringCallOccurrenceId))
				return false;

			var parameters = ClaimParameters(recurringCallOccurrenceId, claimedOnUtc);
			parameters.Add("CallId", callId);
			parameters.Add("Created", (int)RecurringCallOccurrenceStates.Created);
			RecurringCallsRepository.AddTimestamp(parameters, "UpdatedOn", updatedOnUtc);

			return await ExecuteAsync(
				$"UPDATE {Tbl("RecurringCallOccurrences")} SET {Col("State")} = {P}Created, {Col("CallId")} = {P}CallId, {Col("UpdatedOn")} = {P}UpdatedOn " +
				$"WHERE {ThisClaim()}",
				parameters, cancellationToken) == 1;
		}

		public async Task<bool> TryRenewClaimAsync(string recurringCallOccurrenceId, DateTime claimedOnUtc, DateTime renewedOnUtc, DateTime? scheduleRevisionUtc,
			CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(recurringCallOccurrenceId))
				return false;

			var parameters = ClaimParameters(recurringCallOccurrenceId, claimedOnUtc);
			RecurringCallsRepository.AddTimestamp(parameters, "RenewedOn", renewedOnUtc);

			// The schedule is the claim row's own.
			var running = scheduleRevisionUtc.HasValue
				? " AND " + ScheduleRuns(parameters, scheduleRevisionUtc.Value, $"{Tbl("RecurringCallOccurrences")}.{Col("RecurringCallId")}")
				: string.Empty;

			return await ExecuteAsync(
				$"UPDATE {Tbl("RecurringCallOccurrences")} SET {Col("UpdatedOn")} = {P}RenewedOn WHERE {ThisClaim()}{running}",
				parameters, cancellationToken) == 1;
		}

		public async Task<bool> DeleteClaimAsync(string recurringCallOccurrenceId, DateTime claimedOnUtc, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(recurringCallOccurrenceId))
				return false;

			return await ExecuteAsync($"DELETE FROM {Tbl("RecurringCallOccurrences")} WHERE {ThisClaim()}",
				ClaimParameters(recurringCallOccurrenceId, claimedOnUtc), cancellationToken) == 1;
		}

		public async Task<bool> TryReturnClaimAsync(string recurringCallOccurrenceId, DateTime claimedOnUtc, int toState, string userId, DateTime updatedOnUtc,
			CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(recurringCallOccurrenceId))
				return false;

			var parameters = ClaimParameters(recurringCallOccurrenceId, claimedOnUtc);
			parameters.Add("To", toState);
			parameters.Add("UserId", userId);
			RecurringCallsRepository.AddTimestamp(parameters, "UpdatedOn", updatedOnUtc);

			return await ExecuteAsync(
				$"UPDATE {Tbl("RecurringCallOccurrences")} SET {Col("State")} = {P}To, {Col("UserId")} = {P}UserId, {Col("UpdatedOn")} = {P}UpdatedOn WHERE {ThisClaim()}",
				parameters, cancellationToken) == 1;
		}

		/// <summary>The claim this caller took: the row, still creating, with the claim's own timestamp (its generation).</summary>
		private string ThisClaim()
		{
			return $"{Col("RecurringCallOccurrenceId")} = {CaseInsensitive("Id")} AND {Col("State")} = {P}Creating AND {Col("UpdatedOn")} = {P}ClaimedOn";
		}

		private static DynamicParameters ClaimParameters(string recurringCallOccurrenceId, DateTime claimedOnUtc)
		{
			var parameters = new DynamicParameters(new { Id = recurringCallOccurrenceId, Creating = (int)RecurringCallOccurrenceStates.Creating });
			RecurringCallsRepository.AddTimestamp(parameters, "ClaimedOn", claimedOnUtc);
			return parameters;
		}
	}

	/// <summary>Recurring call alerts (M0320).</summary>
	public class RecurringCallAlertsRepository : RmsRepositoryBase<RecurringCallAlert>, IRecurringCallAlertsRepository
	{
		public RecurringCallAlertsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<bool> TryRaiseAsync(RecurringCallAlert alert, CancellationToken cancellationToken = default)
		{
			if (alert == null)
				return false;

			if (string.IsNullOrWhiteSpace(alert.RecurringCallAlertId))
				alert.RecurringCallAlertId = Guid.NewGuid().ToString();

			// The unique (RecurringCallId, OccurrenceOn, Kind) index over open alerts (filtered on ResolvedOn IS NULL) decides between
			// two passes raising the same alert; a closed one keeps its history without blocking a new raise.
			var columns = Cols("RecurringCallAlertId", "DepartmentId", "RecurringCallId", "OccurrenceOn", "Kind", "CallId", "DetailJson", "CreatedOn");
			var values = $"{P}Id, {P}DepartmentId, {P}RecurringCallId, {P}OccurrenceOn, {P}Kind, {P}CallId, {P}DetailJson, {P}CreatedOn";
			var sql = IsPostgres
				? $"INSERT INTO {Tbl("RecurringCallAlerts")} ({columns}) VALUES ({values}) " +
				  $"ON CONFLICT ({Cols("RecurringCallId", "OccurrenceOn", "Kind")}) WHERE {Col("ResolvedOn")} IS NULL DO NOTHING"
				: $"INSERT INTO {Tbl("RecurringCallAlerts")} ({columns}) SELECT {values} " +
				  $"WHERE NOT EXISTS (SELECT 1 FROM {Tbl("RecurringCallAlerts")} WITH (UPDLOCK, HOLDLOCK) WHERE {Col("RecurringCallId")} = {P}RecurringCallId " +
				  $"AND {Col("OccurrenceOn")} = {P}OccurrenceOn AND {Col("Kind")} = {P}Kind AND {Col("ResolvedOn")} IS NULL)";

			var parameters = new DynamicParameters(new
			{
				Id = alert.RecurringCallAlertId,
				alert.DepartmentId,
				alert.RecurringCallId,
				alert.Kind,
				alert.CallId,
				alert.DetailJson
			});
			RecurringCallsRepository.AddTimestamp(parameters, "OccurrenceOn", alert.OccurrenceOn);
			RecurringCallsRepository.AddTimestamp(parameters, "CreatedOn", alert.CreatedOn);

			try
			{
				return await ExecuteAsync(sql, parameters, cancellationToken) == 1;
			}
			catch (Exception ex) when (IsUniqueViolation(ex))
			{
				return false;
			}
		}

		public async Task<bool> AnyForOccurrenceAsync(int recurringCallId, DateTime occurrenceUtc, int kind)
		{
			var parameters = new DynamicParameters(new { RecurringCallId = recurringCallId, Kind = kind });
			RecurringCallsRepository.AddTimestamp(parameters, "OccurrenceOn", occurrenceUtc);

			var rows = await QueryAsync<RecurringCallAlert>(
				$"SELECT * FROM {Tbl("RecurringCallAlerts")} WHERE {Col("RecurringCallId")} = {P}RecurringCallId AND {Col("OccurrenceOn")} = {P}OccurrenceOn " +
				$"AND {Col("Kind")} = {P}Kind",
				parameters);

			return rows?.Any() == true;
		}

		public Task<IEnumerable<RecurringCallAlert>> GetOpenForDepartmentAsync(int departmentId)
		{
			return QueryAsync<RecurringCallAlert>(
				$"SELECT * FROM {Tbl("RecurringCallAlerts")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("ResolvedOn")} IS NULL",
				new { DepartmentId = departmentId });
		}

		public Task<IEnumerable<RecurringCallAlert>> GetForDepartmentSinceAsync(int departmentId, DateTime sinceUtc)
		{
			var parameters = new DynamicParameters(new { DepartmentId = departmentId });
			RecurringCallsRepository.AddTimestamp(parameters, "Since", sinceUtc);

			return QueryAsync<RecurringCallAlert>(
				$"SELECT * FROM {Tbl("RecurringCallAlerts")} WHERE {Col("DepartmentId")} = {P}DepartmentId " +
				$"AND ({Col("ResolvedOn")} IS NULL OR {Col("CreatedOn")} >= {P}Since)",
				parameters);
		}

		public Task<IEnumerable<RecurringCallAlert>> GetAllOpenAsync()
		{
			return QueryAsync<RecurringCallAlert>($"SELECT * FROM {Tbl("RecurringCallAlerts")} WHERE {Col("ResolvedOn")} IS NULL", new { });
		}

		public Task<IEnumerable<RecurringCallAlert>> GetOpenForOccurrenceAsync(int recurringCallId, DateTime occurrenceUtc)
		{
			var parameters = new DynamicParameters(new { RecurringCallId = recurringCallId });
			RecurringCallsRepository.AddTimestamp(parameters, "OccurrenceOn", occurrenceUtc);

			return QueryAsync<RecurringCallAlert>(
				$"SELECT * FROM {Tbl("RecurringCallAlerts")} WHERE {Col("RecurringCallId")} = {P}RecurringCallId AND {Col("OccurrenceOn")} = {P}OccurrenceOn " +
				$"AND {Col("ResolvedOn")} IS NULL",
				parameters);
		}

		public Task<IEnumerable<RecurringCallAlert>> GetOpenForScheduleAsync(int recurringCallId)
		{
			return QueryAsync<RecurringCallAlert>(
				$"SELECT * FROM {Tbl("RecurringCallAlerts")} WHERE {Col("RecurringCallId")} = {P}RecurringCallId AND {Col("ResolvedOn")} IS NULL",
				new { RecurringCallId = recurringCallId });
		}

		public async Task<bool> TryResolveAsync(string recurringCallAlertId, int resolution, string userId, string note, DateTime resolvedOnUtc,
			CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(recurringCallAlertId))
				return false;

			var parameters = new DynamicParameters(new { Id = recurringCallAlertId, Resolution = resolution, UserId = userId, Note = note });
			RecurringCallsRepository.AddTimestamp(parameters, "ResolvedOn", resolvedOnUtc);

			return await ExecuteAsync(
				$"UPDATE {Tbl("RecurringCallAlerts")} SET {Col("ResolvedOn")} = {P}ResolvedOn, {Col("ResolvedByUserId")} = {P}UserId, " +
				$"{Col("Resolution")} = {P}Resolution, {Col("ResolutionNote")} = {P}Note " +
				$"WHERE {Col("RecurringCallAlertId")} = {CaseInsensitive("Id")} AND {Col("ResolvedOn")} IS NULL",
				parameters, cancellationToken) == 1;
		}

		public async Task<bool> TryAcknowledgeAsync(string recurringCallAlertId, string userId, DateTime acknowledgedOnUtc, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(recurringCallAlertId))
				return false;

			var parameters = new DynamicParameters(new { Id = recurringCallAlertId, UserId = userId });
			RecurringCallsRepository.AddTimestamp(parameters, "AcknowledgedOn", acknowledgedOnUtc);

			return await ExecuteAsync(
				$"UPDATE {Tbl("RecurringCallAlerts")} SET {Col("AcknowledgedOn")} = {P}AcknowledgedOn, {Col("AcknowledgedByUserId")} = {P}UserId " +
				$"WHERE {Col("RecurringCallAlertId")} = {CaseInsensitive("Id")} AND {Col("ResolvedOn")} IS NULL AND {Col("AcknowledgedOn")} IS NULL",
				parameters, cancellationToken) == 1;
		}

		public async Task<bool> TryClaimNotificationAsync(string recurringCallAlertId, DateTime claimedOnUtc, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(recurringCallAlertId))
				return false;

			var parameters = new DynamicParameters(new { Id = recurringCallAlertId });
			RecurringCallsRepository.AddTimestamp(parameters, "ClaimedOn", claimedOnUtc);

			return await ExecuteAsync(
				$"UPDATE {Tbl("RecurringCallAlerts")} SET {Col("NotifiedOn")} = {P}ClaimedOn WHERE {Col("RecurringCallAlertId")} = {CaseInsensitive("Id")} " +
				$"AND {Col("ResolvedOn")} IS NULL AND {Col("NotifiedOn")} IS NULL",
				parameters, cancellationToken) == 1;
		}

		public async Task<bool> ReleaseNotificationClaimAsync(string recurringCallAlertId, DateTime claimedOnUtc, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(recurringCallAlertId))
				return false;

			var parameters = new DynamicParameters(new { Id = recurringCallAlertId });
			RecurringCallsRepository.AddTimestamp(parameters, "ClaimedOn", claimedOnUtc);

			return await ExecuteAsync(
				$"UPDATE {Tbl("RecurringCallAlerts")} SET {Col("NotifiedOn")} = NULL WHERE {Col("RecurringCallAlertId")} = {CaseInsensitive("Id")} " +
				$"AND {Col("NotifiedOn")} = {P}ClaimedOn",
				parameters, cancellationToken) == 1;
		}

		public async Task MarkNotifiedAsync(string recurringCallAlertId, DateTime notifiedOnUtc, CancellationToken cancellationToken = default)
		{
			var parameters = new DynamicParameters(new { Id = recurringCallAlertId });
			RecurringCallsRepository.AddTimestamp(parameters, "NotifiedOn", notifiedOnUtc);

			await ExecuteAsync(
				$"UPDATE {Tbl("RecurringCallAlerts")} SET {Col("NotifiedOn")} = {P}NotifiedOn WHERE {Col("RecurringCallAlertId")} = {CaseInsensitive("Id")}",
				parameters, cancellationToken);
		}

		public async Task SetCallAsync(string recurringCallAlertId, int callId, CancellationToken cancellationToken = default)
		{
			await ExecuteAsync(
				$"UPDATE {Tbl("RecurringCallAlerts")} SET {Col("CallId")} = {P}CallId WHERE {Col("RecurringCallAlertId")} = {CaseInsensitive("Id")}",
				new { Id = recurringCallAlertId, CallId = callId }, cancellationToken);
		}

		public async Task<int> PurgeResolvedBeforeAsync(DateTime resolvedBeforeUtc, CancellationToken cancellationToken = default)
		{
			var parameters = new DynamicParameters();
			RecurringCallsRepository.AddTimestamp(parameters, "Before", resolvedBeforeUtc);

			return await ExecuteAsync(
				$"DELETE FROM {Tbl("RecurringCallAlerts")} WHERE {Col("ResolvedOn")} IS NOT NULL AND {Col("ResolvedOn")} < {P}Before",
				parameters, cancellationToken);
		}
	}
}
