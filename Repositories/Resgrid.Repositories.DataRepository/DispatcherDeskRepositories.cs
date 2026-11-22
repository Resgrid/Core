using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository
{
	// Dispatcher desk repositories (M0324). String keys are citext on PostgreSQL, so every string comparison casts its parameter
	// (CaseInsensitive) to keep the match case-insensitive and indexed, as SQL Server's collation does.

	internal static class DispatcherDeskSql
	{
		/// <summary>datetime2 on SQL Server; unspecified kind for Npgsql timestamp columns.</summary>
		internal static void AddTimestamp(DynamicParameters parameters, string name, DateTime? value)
		{
			if (DataConfig.DatabaseType == DatabaseTypes.Postgres)
				parameters.Add(name, value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Unspecified) : (DateTime?)null);
			else
				parameters.Add(name, value, DbType.DateTime2);
		}
	}

	public class ShiftDispatchSettingsRepository : RmsRepositoryBase<ShiftDispatchSetting>, IShiftDispatchSettingsRepository
	{
		public ShiftDispatchSettingsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<ShiftDispatchSetting> GetByShiftIdAsync(int shiftId)
		{
			return QueryFirstOrDefaultAsync<ShiftDispatchSetting>(
				$"SELECT * FROM {Tbl("ShiftDispatchSettings")} WHERE {Col("ShiftId")} = {P}ShiftId", new { ShiftId = shiftId });
		}

		public Task<IEnumerable<ShiftDispatchSetting>> GetByDepartmentIdAsync(int departmentId)
		{
			return QueryAsync<ShiftDispatchSetting>(
				$"SELECT * FROM {Tbl("ShiftDispatchSettings")} WHERE {Col("DepartmentId")} = {P}DepartmentId", new { DepartmentId = departmentId });
		}

		public Task<int> DeleteByShiftIdAsync(int shiftId, CancellationToken cancellationToken = default)
		{
			return ExecuteAsync($"DELETE FROM {Tbl("ShiftDispatchSettings")} WHERE {Col("ShiftId")} = {P}ShiftId", new { ShiftId = shiftId }, cancellationToken);
		}
	}

	public class ShiftDispatchersRepository : RmsRepositoryBase<ShiftDispatcher>, IShiftDispatchersRepository
	{
		public ShiftDispatchersRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<IEnumerable<ShiftDispatcher>> GetByShiftIdAsync(int shiftId)
		{
			return QueryAsync<ShiftDispatcher>(
				$"SELECT * FROM {Tbl("ShiftDispatchers")} WHERE {Col("ShiftId")} = {P}ShiftId", new { ShiftId = shiftId });
		}

		public Task<IEnumerable<ShiftDispatcher>> GetByDepartmentIdAsync(int departmentId)
		{
			return QueryAsync<ShiftDispatcher>(
				$"SELECT * FROM {Tbl("ShiftDispatchers")} WHERE {Col("DepartmentId")} = {P}DepartmentId", new { DepartmentId = departmentId });
		}

		public Task<IEnumerable<ShiftDispatcher>> GetByUserIdAsync(int departmentId, string userId)
		{
			return QueryAsync<ShiftDispatcher>(
				$"SELECT * FROM {Tbl("ShiftDispatchers")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("UserId")} = {CaseInsensitive("UserId")}",
				new { DepartmentId = departmentId, UserId = userId });
		}

		private string DayTimesSelect =>
			$"SELECT d.{Col("ShiftId")} AS ShiftId, d.{Col("ShiftDayId")} AS ShiftDayId, d.{Col("Day")} AS Day, s.{Col("Name")} AS ShiftName, " +
			$"s.{Col("Color")} AS Color, s.{Col("StartTime")} AS StartTime, s.{Col("EndTime")} AS EndTime, s.{Col("Hours")} AS Hours, " +
			$"s.{Col("AssignmentType")} AS AssignmentType, s.{Col("RequireApproval")} AS RequireApproval " +
			$"FROM {Tbl("ShiftDays")} d INNER JOIN {Tbl("Shifts")} s ON s.{Col("ShiftId")} = d.{Col("ShiftId")} ";

		public Task<IEnumerable<ShiftDayTimesRow>> GetShiftDayTimesAsync(int departmentId, IEnumerable<int> shiftIds, DateTime fromLocalDate, DateTime toLocalDate)
		{
			var ids = InListValue(shiftIds);
			if (ids.Length == 0)
				return Task.FromResult(Enumerable.Empty<ShiftDayTimesRow>());

			var parameters = new DynamicParameters(new { DepartmentId = departmentId, ShiftIds = ids });
			DispatcherDeskSql.AddTimestamp(parameters, "From", fromLocalDate.Date);
			DispatcherDeskSql.AddTimestamp(parameters, "To", toLocalDate.Date.AddDays(1));

			return QueryAsync<ShiftDayTimesRow>(DayTimesSelect +
				$"WHERE s.{Col("DepartmentId")} = {P}DepartmentId AND {InList("ShiftId", "ShiftIds", "d")} " +
				$"AND d.{Col("Day")} >= {P}From AND d.{Col("Day")} < {P}To", parameters);
		}

		public Task<ShiftDayTimesRow> GetShiftDayTimesByIdAsync(int shiftDayId)
		{
			return QueryFirstOrDefaultAsync<ShiftDayTimesRow>(DayTimesSelect + $"WHERE d.{Col("ShiftDayId")} = {P}ShiftDayId", new { ShiftDayId = shiftDayId });
		}

		public Task<int> DeleteByShiftIdAsync(int shiftId, CancellationToken cancellationToken = default)
		{
			return ExecuteAsync($"DELETE FROM {Tbl("ShiftDispatchers")} WHERE {Col("ShiftId")} = {P}ShiftId", new { ShiftId = shiftId }, cancellationToken);
		}
	}

	public class DispatcherBreaksRepository : RmsRepositoryBase<DispatcherBreak>, IDispatcherBreaksRepository
	{
		private static readonly HashSet<string> AlertColumns = new HashSet<string>(StringComparer.Ordinal)
		{
			"ReminderSentOn", "DueAlertSentOn", "ReturnAlertSentOn", "OverdueAlertSentOn"
		};

		public DispatcherBreaksRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<IEnumerable<DispatcherBreak>> GetForDepartmentInRangeAsync(int departmentId, DateTime fromUtc, DateTime toUtc)
		{
			var parameters = new DynamicParameters(new { DepartmentId = departmentId, Active = (int)DispatcherBreakStates.Active });
			DispatcherDeskSql.AddTimestamp(parameters, "From", fromUtc);
			DispatcherDeskSql.AddTimestamp(parameters, "To", toUtc);

			return QueryAsync<DispatcherBreak>(
				$"SELECT * FROM {Tbl("DispatcherBreaks")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND (" +
				$"({Col("ScheduledStartOn")} >= {P}From AND {Col("ScheduledStartOn")} < {P}To) OR " +
				$"({Col("StartedOn")} >= {P}From AND {Col("StartedOn")} < {P}To) OR {Col("State")} = {P}Active)", parameters);
		}

		public Task<IEnumerable<DispatcherBreak>> GetForShiftDayAsync(int shiftDayId)
		{
			return QueryAsync<DispatcherBreak>(
				$"SELECT * FROM {Tbl("DispatcherBreaks")} WHERE {Col("ShiftDayId")} = {P}ShiftDayId", new { ShiftDayId = shiftDayId });
		}

		public Task<IEnumerable<DispatcherBreak>> GetOpenForUserAsync(int departmentId, string userId, DateTime fromUtc, DateTime toUtc)
		{
			var parameters = new DynamicParameters(new
			{
				DepartmentId = departmentId, UserId = userId, Scheduled = (int)DispatcherBreakStates.Scheduled, Active = (int)DispatcherBreakStates.Active
			});
			DispatcherDeskSql.AddTimestamp(parameters, "From", fromUtc);
			DispatcherDeskSql.AddTimestamp(parameters, "To", toUtc);

			return QueryAsync<DispatcherBreak>(
				$"SELECT * FROM {Tbl("DispatcherBreaks")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("UserId")} = {CaseInsensitive("UserId")} " +
				$"AND ({Col("State")} = {P}Active OR ({Col("State")} = {P}Scheduled AND {Col("ScheduledStartOn")} >= {P}From AND {Col("ScheduledStartOn")} < {P}To))",
				parameters);
		}

		public Task<DispatcherBreak> GetActiveForUserAsync(int departmentId, string userId)
		{
			return QueryFirstOrDefaultAsync<DispatcherBreak>(
				$"SELECT * FROM {Tbl("DispatcherBreaks")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("UserId")} = {CaseInsensitive("UserId")} " +
				$"AND {Col("State")} = {P}Active ORDER BY {Col("StartedOn")} DESC",
				new { DepartmentId = departmentId, UserId = userId, Active = (int)DispatcherBreakStates.Active });
		}

		public Task<IEnumerable<DispatcherBreak>> GetForTimersAsync(DateTime scheduledBeforeUtc, DateTime scheduledAfterUtc)
		{
			var parameters = new DynamicParameters(new { Scheduled = (int)DispatcherBreakStates.Scheduled, Active = (int)DispatcherBreakStates.Active });
			DispatcherDeskSql.AddTimestamp(parameters, "Before", scheduledBeforeUtc);
			DispatcherDeskSql.AddTimestamp(parameters, "After", scheduledAfterUtc);

			return QueryAsync<DispatcherBreak>(
				$"SELECT * FROM {Tbl("DispatcherBreaks")} WHERE {Col("State")} = {P}Active OR ({Col("State")} = {P}Scheduled " +
				$"AND {Col("ScheduledStartOn")} < {P}Before AND {Col("ScheduledStartOn")} >= {P}After)", parameters);
		}

		public Task<IEnumerable<DispatcherBreak>> GetOverdueScheduledAsync(DateTime scheduledBeforeUtc, int limit)
		{
			var parameters = new DynamicParameters(new { Scheduled = (int)DispatcherBreakStates.Scheduled, Limit = Math.Max(1, limit) });
			DispatcherDeskSql.AddTimestamp(parameters, "Before", scheduledBeforeUtc);

			var where = $"WHERE {Col("State")} = {P}Scheduled AND {Col("ScheduledStartOn")} < {P}Before ORDER BY {Col("ScheduledStartOn")}";
			return QueryAsync<DispatcherBreak>(IsPostgres
				? $"SELECT * FROM {Tbl("DispatcherBreaks")} {where} LIMIT {P}Limit"
				: $"SELECT TOP ({P}Limit) * FROM {Tbl("DispatcherBreaks")} {where}", parameters);
		}

		/// <summary>The state and plan the caller read: "still in this state, still planned for this start and length".</summary>
		private string ObservedFilter(DynamicParameters parameters, DispatcherBreak observed)
		{
			parameters.Add("ObservedState", observed.State);
			parameters.Add("ObservedDuration", observed.DurationMinutes);

			var filter = $"{Col("State")} = {P}ObservedState AND {Col("DurationMinutes")} = {P}ObservedDuration AND ";
			if (!observed.ScheduledStartOn.HasValue)
				return filter + $"{Col("ScheduledStartOn")} IS NULL";

			DispatcherDeskSql.AddTimestamp(parameters, "ObservedStart", observed.ScheduledStartOn);
			return filter + $"{Col("ScheduledStartOn")} = {P}ObservedStart";
		}

		public Task<bool> TryTransitionAsync(DispatcherBreak updated, int fromState, CancellationToken cancellationToken = default)
		{
			if (updated == null || string.IsNullOrWhiteSpace(updated.DispatcherBreakId))
				return Task.FromResult(false);

			var parameters = UpdateParameters(updated);
			parameters.Add("FromState", fromState);

			return UpdateWhereAsync(parameters, $"{Col("State")} = {P}FromState", false, cancellationToken);
		}

		public Task<bool> TryUpdateObservedAsync(DispatcherBreak updated, DispatcherBreak observed, bool resetPlanAlerts, CancellationToken cancellationToken = default)
		{
			if (updated == null || observed == null || string.IsNullOrWhiteSpace(updated.DispatcherBreakId))
				return Task.FromResult(false);

			var parameters = UpdateParameters(updated);
			return UpdateWhereAsync(parameters, ObservedFilter(parameters, observed), resetPlanAlerts, cancellationToken);
		}

		private async Task<bool> UpdateWhereAsync(DynamicParameters parameters, string condition, bool resetPlanAlerts, CancellationToken cancellationToken)
		{
			var sql = $"UPDATE {Tbl("DispatcherBreaks")} SET {Col("State")} = {P}State, {Col("BreakType")} = {P}BreakType, " +
					  $"{Col("DurationMinutes")} = {P}DurationMinutes, {Col("ScheduledStartOn")} = {P}ScheduledStartOn, {Col("StartedOn")} = {P}StartedOn, " +
					  $"{Col("ExpectedReturnOn")} = {P}ExpectedReturnOn, {Col("EndedOn")} = {P}EndedOn, {Col("EndReason")} = {P}EndReason, " +
					  $"{Col("EndedByUserId")} = {P}EndedByUserId, {Col("Note")} = {P}Note, {Col("UpdatedByUserId")} = {P}UpdatedByUserId, " +
					  $"{Col("UpdatedOn")} = {P}UpdatedOn, {Col("ShiftId")} = {P}ShiftId, {Col("ShiftDayId")} = {P}ShiftDayId" +
					  (resetPlanAlerts ? $", {Col("ReminderSentOn")} = NULL, {Col("DueAlertSentOn")} = NULL" : "") +
					  $" WHERE {Col("DispatcherBreakId")} = {CaseInsensitive("Id")} AND {condition}";

			return await ExecuteAsync(sql, parameters, cancellationToken) == 1;
		}

		private static DynamicParameters UpdateParameters(DispatcherBreak updated)
		{
			var parameters = new DynamicParameters(new
			{
				Id = updated.DispatcherBreakId,
				updated.State,
				updated.BreakType,
				updated.DurationMinutes,
				updated.EndReason,
				updated.EndedByUserId,
				updated.Note,
				updated.UpdatedByUserId,
				updated.ShiftId,
				updated.ShiftDayId
			});
			DispatcherDeskSql.AddTimestamp(parameters, "ScheduledStartOn", updated.ScheduledStartOn);
			DispatcherDeskSql.AddTimestamp(parameters, "StartedOn", updated.StartedOn);
			DispatcherDeskSql.AddTimestamp(parameters, "ExpectedReturnOn", updated.ExpectedReturnOn);
			DispatcherDeskSql.AddTimestamp(parameters, "EndedOn", updated.EndedOn);
			DispatcherDeskSql.AddTimestamp(parameters, "UpdatedOn", updated.UpdatedOn);
			return parameters;
		}

		public async Task<bool> TryMarkAlertAsync(DispatcherBreak observed, string column, DateTime sentOnUtc, CancellationToken cancellationToken = default)
		{
			if (!AlertColumns.Contains(column))
				throw new ArgumentOutOfRangeException(nameof(column));

			if (observed == null || string.IsNullOrWhiteSpace(observed.DispatcherBreakId))
				return false;

			var parameters = new DynamicParameters(new { Id = observed.DispatcherBreakId });
			DispatcherDeskSql.AddTimestamp(parameters, "SentOn", sentOnUtc);

			return await ExecuteAsync(
				$"UPDATE {Tbl("DispatcherBreaks")} SET {Col(column)} = {P}SentOn WHERE {Col("DispatcherBreakId")} = {CaseInsensitive("Id")} AND {Col(column)} IS NULL " +
				$"AND {ObservedFilter(parameters, observed)}",
				parameters, cancellationToken) == 1;
		}

		/// <summary>How long a break start waits for another start in the same department to finish.</summary>
		private const int BreakStartLockTimeoutMs = 10000;

		/// <summary>The first key of the PostgreSQL two-key advisory lock for break starts ("DBRK"); the second is the department id.</summary>
		private const int BreakStartLockSpace = 0x4442524B;

		public async Task LockDepartmentForBreakStartAsync(int departmentId, CancellationToken cancellationToken = default)
		{
			if (UnitOfWork?.Transaction == null)
				throw new InvalidOperationException("Locking a department for a break start needs the unit of work's transaction.");

			var connection = UnitOfWork.Connection;
			var transaction = UnitOfWork.Transaction;

			if (IsPostgres)
			{
				// Transaction-scoped (released at commit or rollback) and polled, so a stuck start cannot hold the next one forever.
				var waited = System.Diagnostics.Stopwatch.StartNew();
				while (true)
				{
					var commandSeconds = Math.Max(1, (int)Math.Ceiling((BreakStartLockTimeoutMs - waited.ElapsedMilliseconds) / 1000.0));
					if (await connection.ExecuteScalarAsync<bool>(new Dapper.CommandDefinition("SELECT pg_try_advisory_xact_lock(@Space, @DepartmentId)",
							new { Space = BreakStartLockSpace, DepartmentId = departmentId }, transaction, commandTimeout: commandSeconds, cancellationToken: cancellationToken)))
						return;

					var remainingMs = BreakStartLockTimeoutMs - waited.ElapsedMilliseconds;
					if (remainingMs <= 0)
						throw new TimeoutException($"Another break start in department {departmentId} is still running.");

					await Task.Delay((int)Math.Min(250, remainingMs), cancellationToken);
				}
			}

			// sp_getapplock answers 0 or 1 when granted and a negative code on timeout, cancellation or deadlock.
			var result = await connection.ExecuteScalarAsync<int>(new Dapper.CommandDefinition(
				"DECLARE @Result int; EXEC @Result = sp_getapplock @Resource = @Resource, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = @TimeoutMs; SELECT @Result;",
				new { Resource = "Resgrid.DispatcherBreakStart.Department." + departmentId.ToString(System.Globalization.CultureInfo.InvariantCulture), TimeoutMs = BreakStartLockTimeoutMs },
				transaction, cancellationToken: cancellationToken));
			if (result < 0)
				throw new TimeoutException($"Another break start in department {departmentId} is still running.");
		}

		public Task<int> PurgeBeforeAsync(DateTime beforeUtc, CancellationToken cancellationToken = default)
		{
			var parameters = new DynamicParameters(new { Scheduled = (int)DispatcherBreakStates.Scheduled, Active = (int)DispatcherBreakStates.Active });
			DispatcherDeskSql.AddTimestamp(parameters, "Before", beforeUtc);

			return ExecuteAsync(
				$"DELETE FROM {Tbl("DispatcherBreaks")} WHERE {Col("State")} <> {P}Active AND {Col("CreatedOn")} < {P}Before " +
				$"AND ({Col("State")} <> {P}Scheduled OR {Col("ScheduledStartOn")} IS NULL OR {Col("ScheduledStartOn")} < {P}Before)", parameters, cancellationToken);
		}
	}

	public class DispatcherPresencesRepository : RmsRepositoryBase<DispatcherPresence>, IDispatcherPresencesRepository
	{
		public DispatcherPresencesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<DispatcherPresence> GetForUserAsync(int departmentId, string userId)
		{
			return QueryFirstOrDefaultAsync<DispatcherPresence>(
				$"SELECT * FROM {Tbl("DispatcherPresences")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("UserId")} = {CaseInsensitive("UserId")}",
				new { DepartmentId = departmentId, UserId = userId });
		}

		public Task<IEnumerable<DispatcherPresence>> GetByDepartmentIdAsync(int departmentId)
		{
			return QueryAsync<DispatcherPresence>(
				$"SELECT * FROM {Tbl("DispatcherPresences")} WHERE {Col("DepartmentId")} = {P}DepartmentId", new { DepartmentId = departmentId });
		}

		public async Task<bool> SetLastOfferedOnAsync(string dispatcherPresenceId, DateTime offeredOnUtc, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(dispatcherPresenceId))
				return false;

			var parameters = new DynamicParameters(new { Id = dispatcherPresenceId });
			DispatcherDeskSql.AddTimestamp(parameters, "OfferedOn", offeredOnUtc);

			return await ExecuteAsync($"UPDATE {Tbl("DispatcherPresences")} SET {Col("LastOfferedOn")} = {P}OfferedOn " +
									  $"WHERE {Col("DispatcherPresenceId")} = {CaseInsensitive("Id")}", parameters, cancellationToken) == 1;
		}

		public Task<IEnumerable<DispatcherPresence>> GetSignedInAsync(CancellationToken cancellationToken = default)
		{
			return QueryAsync<DispatcherPresence>(
				$"SELECT * FROM {Tbl("DispatcherPresences")} WHERE {Col("State")} > {P}Offline", new { Offline = (int)DispatcherPresenceStates.Offline },
				cancellationToken);
		}

		public async Task<bool> TouchAsync(string dispatcherPresenceId, DateTime lastSeenOnUtc, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(dispatcherPresenceId))
				return false;

			var parameters = new DynamicParameters(new { Id = dispatcherPresenceId, Offline = (int)DispatcherPresenceStates.Offline });
			DispatcherDeskSql.AddTimestamp(parameters, "LastSeenOn", lastSeenOnUtc);

			return await ExecuteAsync(
				$"UPDATE {Tbl("DispatcherPresences")} SET {Col("LastSeenOn")} = {P}LastSeenOn " +
				$"WHERE {Col("DispatcherPresenceId")} = {CaseInsensitive("Id")} AND {Col("State")} > {P}Offline", parameters, cancellationToken) > 0;
		}

		public async Task<bool> TrySignOffAsync(string dispatcherPresenceId, DateTime? expectedSignedInOn, DateTime nowUtc, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(dispatcherPresenceId))
				return false;

			var parameters = new DynamicParameters(new { Id = dispatcherPresenceId, Offline = (int)DispatcherPresenceStates.Offline });
			DispatcherDeskSql.AddTimestamp(parameters, "Now", nowUtc);
			if (expectedSignedInOn.HasValue)
				DispatcherDeskSql.AddTimestamp(parameters, "SignedInOn", expectedSignedInOn);

			var session = expectedSignedInOn.HasValue ? $"{Col("SignedInOn")} = {P}SignedInOn" : $"{Col("SignedInOn")} IS NULL";

			return await ExecuteAsync(
				$"UPDATE {Tbl("DispatcherPresences")} SET {Col("State")} = {P}Offline, {Col("StateChangedOn")} = {P}Now, {Col("ActiveBreakId")} = NULL, " +
				$"{Col("AwayReason")} = NULL, {Col("MissedOffers")} = 0 " +
				$"WHERE {Col("DispatcherPresenceId")} = {CaseInsensitive("Id")} AND {Col("State")} > {P}Offline AND {session}", parameters, cancellationToken) > 0;
		}

		public async Task<bool> TrySetDutyAsync(string dispatcherPresenceId, DateTime? expectedSignedInOn, int dutyMode, int? dutyShiftId, int? dutyShiftDayId,
			DateTime? dutyEndsOn, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(dispatcherPresenceId))
				return false;

			var parameters = new DynamicParameters(new
			{
				Id = dispatcherPresenceId,
				Offline = (int)DispatcherPresenceStates.Offline,
				DutyMode = dutyMode,
				DutyShiftId = dutyShiftId,
				DutyShiftDayId = dutyShiftDayId
			});
			DispatcherDeskSql.AddTimestamp(parameters, "DutyEndsOn", dutyEndsOn);
			if (expectedSignedInOn.HasValue)
				DispatcherDeskSql.AddTimestamp(parameters, "SignedInOn", expectedSignedInOn);

			var session = expectedSignedInOn.HasValue ? $"{Col("SignedInOn")} = {P}SignedInOn" : $"{Col("SignedInOn")} IS NULL";

			return await ExecuteAsync(
				$"UPDATE {Tbl("DispatcherPresences")} SET {Col("DutyMode")} = {P}DutyMode, {Col("DutyShiftId")} = {P}DutyShiftId, " +
				$"{Col("DutyShiftDayId")} = {P}DutyShiftDayId, {Col("DutyEndsOn")} = {P}DutyEndsOn " +
				$"WHERE {Col("DispatcherPresenceId")} = {CaseInsensitive("Id")} AND {Col("State")} > {P}Offline AND {session}", parameters, cancellationToken) > 0;
		}

		public async Task<bool> TryInsertAsync(DispatcherPresence presence, CancellationToken cancellationToken = default)
		{
			if (presence == null)
				return false;

			if (string.IsNullOrWhiteSpace(presence.DispatcherPresenceId))
				presence.DispatcherPresenceId = Guid.NewGuid().ToString();

			try
			{
				await InsertAsync(presence, cancellationToken, true);
				return true;
			}
			catch (Exception ex) when (IsUniqueViolation(ex))
			{
				return false;
			}
		}
	}

	public class DispatcherQueueItemsRepository : RmsRepositoryBase<DispatcherQueueItem>, IDispatcherQueueItemsRepository
	{
		private static readonly int[] OpenStates =
		{
			(int)DispatcherQueueItemStates.Queued, (int)DispatcherQueueItemStates.Offered, (int)DispatcherQueueItemStates.Accepted
		};

		public DispatcherQueueItemsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<DispatcherQueueItem> GetItemAsync(string dispatcherQueueItemId)
		{
			return QueryFirstOrDefaultAsync<DispatcherQueueItem>(
				$"SELECT * FROM {Tbl("DispatcherQueueItems")} WHERE {Col("DispatcherQueueItemId")} = {CaseInsensitive("Id")}", new { Id = dispatcherQueueItemId });
		}

		public Task<IEnumerable<DispatcherQueueItem>> GetOpenForDepartmentAsync(int departmentId)
		{
			return QueryAsync<DispatcherQueueItem>(
				$"SELECT * FROM {Tbl("DispatcherQueueItems")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("State")} < {P}Completed " +
				$"ORDER BY {Col("CreatedOn")}", new { DepartmentId = departmentId, Completed = (int)DispatcherQueueItemStates.Completed });
		}

		public Task<IEnumerable<DispatcherQueueItem>> GetOpenForCallAsync(int departmentId, int callId)
		{
			return QueryAsync<DispatcherQueueItem>(
				$"SELECT * FROM {Tbl("DispatcherQueueItems")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("CallId")} = {P}CallId " +
				$"AND {Col("State")} < {P}Completed", new { DepartmentId = departmentId, CallId = callId, Completed = (int)DispatcherQueueItemStates.Completed });
		}

		public async Task<bool> AnyForCallAsync(int departmentId, int callId)
		{
			var count = await QueryFirstOrDefaultAsync<int>(
				$"SELECT COUNT(1) FROM {Tbl("DispatcherQueueItems")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("CallId")} = {P}CallId",
				new { DepartmentId = departmentId, CallId = callId });
			return count > 0;
		}

		public async Task<IEnumerable<int>> GetCallIdsOnDeskAsync(int departmentId, IEnumerable<int> callIds)
		{
			var found = new List<int>();
			// SQL Server expands the list into one parameter per id (2100 at most), so large sweeps are read in chunks.
			foreach (var chunk in InListValue(callIds).Distinct().Chunk(500))
			{
				found.AddRange(await QueryAsync<int>(
					$"SELECT DISTINCT {Col("CallId")} FROM {Tbl("DispatcherQueueItems")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {InList("CallId", "CallIds")}",
					new { DepartmentId = departmentId, CallIds = chunk }));
			}

			return found;
		}

		public async Task<bool> TryInsertAsync(DispatcherQueueItem item, CancellationToken cancellationToken = default)
		{
			if (item == null)
				return false;

			if (string.IsNullOrWhiteSpace(item.DispatcherQueueItemId))
				item.DispatcherQueueItemId = Guid.NewGuid().ToString();

			try
			{
				await InsertAsync(item, cancellationToken, true);
				return true;
			}
			catch (Exception ex) when (IsUniqueViolation(ex))
			{
				// UX_DispatcherQueueItems_OpenCall / _OpenReference: another open item has this call or reference.
				return false;
			}
		}

		public Task<DispatcherQueueItem> GetOpenByConversationKeyAsync(int departmentId, string conversationKey, DateTime activeSinceUtc)
		{
			var parameters = new DynamicParameters(new { DepartmentId = departmentId, Key = conversationKey, Completed = (int)DispatcherQueueItemStates.Completed });
			DispatcherDeskSql.AddTimestamp(parameters, "Since", activeSinceUtc);

			return QueryFirstOrDefaultAsync<DispatcherQueueItem>(
				$"SELECT * FROM {Tbl("DispatcherQueueItems")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("ConversationKey")} = {CaseInsensitive("Key")} " +
				$"AND {Col("State")} < {P}Completed AND {Col("LastActivityOn")} >= {P}Since ORDER BY {Col("LastActivityOn")} DESC", parameters);
		}

		public Task<DispatcherQueueItem> GetOpenByConversationKeyAnyDepartmentAsync(string conversationKey, DateTime activeSinceUtc)
		{
			var parameters = new DynamicParameters(new { Key = conversationKey, Completed = (int)DispatcherQueueItemStates.Completed });
			DispatcherDeskSql.AddTimestamp(parameters, "Since", activeSinceUtc);

			return QueryFirstOrDefaultAsync<DispatcherQueueItem>(
				$"SELECT * FROM {Tbl("DispatcherQueueItems")} WHERE {Col("ConversationKey")} = {CaseInsensitive("Key")} " +
				$"AND {Col("State")} < {P}Completed AND {Col("LastActivityOn")} >= {P}Since ORDER BY {Col("LastActivityOn")} DESC", parameters);
		}

		public Task<DispatcherQueueItem> GetByExternalReferenceAsync(string externalReference)
		{
			return QueryFirstOrDefaultAsync<DispatcherQueueItem>(
				$"SELECT * FROM {Tbl("DispatcherQueueItems")} WHERE {Col("ExternalReference")} = {CaseInsensitive("Ref")} ORDER BY {Col("CreatedOn")} DESC",
				new { Ref = externalReference });
		}

		public async Task<bool> TryUpdateAsync(DispatcherQueueItem item, int expectedVersion, CancellationToken cancellationToken = default)
		{
			if (item == null || string.IsNullOrWhiteSpace(item.DispatcherQueueItemId))
				return false;

			var parameters = new DynamicParameters(new
			{
				Id = item.DispatcherQueueItemId,
				Expected = expectedVersion,
				NewVersion = expectedVersion + 1,
				item.Kind,
				item.State,
				item.Outcome,
				item.CallId,
				item.Priority,
				item.SourceAddress,
				item.SourceName,
				item.TargetAddress,
				item.Subject,
				item.Preview,
				item.ExternalReference,
				item.ConversationKey,
				item.AssignedUserId,
				item.OfferAttempts,
				item.DeclinedUserIds,
				item.CompletedByUserId,
				item.OverflowNote,
				item.Note
			});
			DispatcherDeskSql.AddTimestamp(parameters, "OfferedOn", item.OfferedOn);
			DispatcherDeskSql.AddTimestamp(parameters, "OfferExpiresOn", item.OfferExpiresOn);
			DispatcherDeskSql.AddTimestamp(parameters, "AcceptedOn", item.AcceptedOn);
			DispatcherDeskSql.AddTimestamp(parameters, "CompletedOn", item.CompletedOn);
			DispatcherDeskSql.AddTimestamp(parameters, "DueOn", item.DueOn);
			DispatcherDeskSql.AddTimestamp(parameters, "LastActivityOn", item.LastActivityOn);
			DispatcherDeskSql.AddTimestamp(parameters, "OverflowedOn", item.OverflowedOn);
			DispatcherDeskSql.AddTimestamp(parameters, "EscalatedOn", item.EscalatedOn);

			var sets = new[]
			{
				"Kind", "State", "Outcome", "CallId", "Priority", "SourceAddress", "SourceName", "TargetAddress", "Subject", "Preview",
				"ExternalReference", "ConversationKey", "AssignedUserId", "OfferedOn", "OfferExpiresOn", "OfferAttempts", "DeclinedUserIds",
				"AcceptedOn", "CompletedOn", "CompletedByUserId", "DueOn", "LastActivityOn", "OverflowedOn", "OverflowNote", "EscalatedOn", "Note"
			};

			var sql = $"UPDATE {Tbl("DispatcherQueueItems")} SET " + string.Join(", ", sets.Select(x => $"{Col(x)} = {P}{x}")) +
					  $", {Col("Version")} = {P}NewVersion WHERE {Col("DispatcherQueueItemId")} = {CaseInsensitive("Id")} AND {Col("Version")} = {P}Expected";

			try
			{
				if (await ExecuteAsync(sql, parameters, cancellationToken) != 1)
					return false;
			}
			catch (Exception ex) when (IsUniqueViolation(ex))
			{
				// The write would give a second open item the department's call or reference: it loses like a stale version.
				return false;
			}

			item.Version = expectedVersion + 1;
			return true;
		}

		public Task<IEnumerable<int>> GetDepartmentsWithOpenItemsAsync()
		{
			return QueryAsync<int>(
				$"SELECT DISTINCT {Col("DepartmentId")} FROM {Tbl("DispatcherQueueItems")} WHERE {Col("State")} < {P}Completed",
				new { Completed = (int)DispatcherQueueItemStates.Completed });
		}

		public Task<IEnumerable<DispatcherQueueItem>> GetCreatedInRangeAsync(int departmentId, DateTime fromUtc, DateTime toUtc)
		{
			var parameters = new DynamicParameters(new { DepartmentId = departmentId });
			DispatcherDeskSql.AddTimestamp(parameters, "From", fromUtc);
			DispatcherDeskSql.AddTimestamp(parameters, "To", toUtc);

			return QueryAsync<DispatcherQueueItem>(
				$"SELECT * FROM {Tbl("DispatcherQueueItems")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("CreatedOn")} >= {P}From " +
				$"AND {Col("CreatedOn")} < {P}To ORDER BY {Col("CreatedOn")}", parameters);
		}

		public async Task<int> PurgeClosedBeforeAsync(DateTime beforeUtc, CancellationToken cancellationToken = default)
		{
			var parameters = new DynamicParameters(new { Completed = (int)DispatcherQueueItemStates.Completed });
			DispatcherDeskSql.AddTimestamp(parameters, "Before", beforeUtc);

			var closed = $"SELECT {Col("DispatcherQueueItemId")} FROM {Tbl("DispatcherQueueItems")} WHERE {Col("State")} >= {P}Completed AND {Col("LastActivityOn")} < {P}Before";

			// One unit of work: an item never outlives its history (a cancellation after the first delete rolls all three back).
			var owns = UnitOfWork.Transaction == null;
			await UnitOfWork.CreateOrGetConnectionAsync(cancellationToken);
			try
			{
				await ExecuteAsync($"DELETE FROM {Tbl("DispatcherConversationMessages")} WHERE {Col("DispatcherQueueItemId")} IN ({closed})", parameters, cancellationToken);
				await ExecuteAsync($"DELETE FROM {Tbl("DispatcherQueueEvents")} WHERE {Col("DispatcherQueueItemId")} IN ({closed})", parameters, cancellationToken);

				var purged = await ExecuteAsync(
					$"DELETE FROM {Tbl("DispatcherQueueItems")} WHERE {Col("State")} >= {P}Completed AND {Col("LastActivityOn")} < {P}Before", parameters, cancellationToken);

				if (owns)
					UnitOfWork.CommitChanges();

				return purged;
			}
			catch
			{
				if (owns)
					UnitOfWork.DiscardChanges();
				throw;
			}
		}
	}

	public class DispatcherQueueEventsRepository : RmsRepositoryBase<DispatcherQueueEvent>, IDispatcherQueueEventsRepository
	{
		public DispatcherQueueEventsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<IEnumerable<DispatcherQueueEvent>> GetForItemAsync(string dispatcherQueueItemId)
		{
			return QueryAsync<DispatcherQueueEvent>(
				$"SELECT * FROM {Tbl("DispatcherQueueEvents")} WHERE {Col("DispatcherQueueItemId")} = {CaseInsensitive("Id")} ORDER BY {Col("OccurredOn")}",
				new { Id = dispatcherQueueItemId });
		}

		public Task<IEnumerable<DispatcherQueueEvent>> GetForDepartmentInRangeAsync(int departmentId, DateTime fromUtc, DateTime toUtc)
		{
			var parameters = new DynamicParameters(new { DepartmentId = departmentId });
			DispatcherDeskSql.AddTimestamp(parameters, "From", fromUtc);
			DispatcherDeskSql.AddTimestamp(parameters, "To", toUtc);

			return QueryAsync<DispatcherQueueEvent>(
				$"SELECT * FROM {Tbl("DispatcherQueueEvents")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("OccurredOn")} >= {P}From " +
				$"AND {Col("OccurredOn")} < {P}To ORDER BY {Col("OccurredOn")}", parameters);
		}
	}

	public class DispatcherConversationMessagesRepository : RmsRepositoryBase<DispatcherConversationMessage>, IDispatcherConversationMessagesRepository
	{
		public DispatcherConversationMessagesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<IEnumerable<DispatcherConversationMessage>> GetForItemAsync(string dispatcherQueueItemId)
		{
			return QueryAsync<DispatcherConversationMessage>(
				$"SELECT * FROM {Tbl("DispatcherConversationMessages")} WHERE {Col("DispatcherQueueItemId")} = {CaseInsensitive("Id")} ORDER BY {Col("CreatedOn")}",
				new { Id = dispatcherQueueItemId });
		}

		public Task<DispatcherConversationMessage> GetByProviderMessageIdAsync(string providerMessageId)
		{
			return QueryFirstOrDefaultAsync<DispatcherConversationMessage>(
				$"SELECT * FROM {Tbl("DispatcherConversationMessages")} WHERE {Col("ProviderMessageId")} = {CaseInsensitive("Pid")}",
				new { Pid = providerMessageId });
		}

		public async Task<bool> TryInsertAsync(DispatcherConversationMessage message, CancellationToken cancellationToken = default)
		{
			if (message == null)
				return false;

			if (string.IsNullOrWhiteSpace(message.DispatcherConversationMessageId))
				message.DispatcherConversationMessageId = Guid.NewGuid().ToString();

			try
			{
				await InsertAsync(message, cancellationToken, true);
				return true;
			}
			catch (Exception ex) when (IsUniqueViolation(ex))
			{
				// UX_DispatcherConversationMessages_Provider: another delivery of the same provider message is stored.
				return false;
			}
		}
	}
}
