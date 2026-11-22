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

		public async Task<bool> TryTransitionAsync(DispatcherBreak updated, int fromState, CancellationToken cancellationToken = default)
		{
			if (updated == null || string.IsNullOrWhiteSpace(updated.DispatcherBreakId))
				return false;

			var parameters = new DynamicParameters(new
			{
				Id = updated.DispatcherBreakId,
				FromState = fromState,
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

			var sql = $"UPDATE {Tbl("DispatcherBreaks")} SET {Col("State")} = {P}State, {Col("BreakType")} = {P}BreakType, " +
					  $"{Col("DurationMinutes")} = {P}DurationMinutes, {Col("ScheduledStartOn")} = {P}ScheduledStartOn, {Col("StartedOn")} = {P}StartedOn, " +
					  $"{Col("ExpectedReturnOn")} = {P}ExpectedReturnOn, {Col("EndedOn")} = {P}EndedOn, {Col("EndReason")} = {P}EndReason, " +
					  $"{Col("EndedByUserId")} = {P}EndedByUserId, {Col("Note")} = {P}Note, {Col("UpdatedByUserId")} = {P}UpdatedByUserId, " +
					  $"{Col("UpdatedOn")} = {P}UpdatedOn, {Col("ShiftId")} = {P}ShiftId, {Col("ShiftDayId")} = {P}ShiftDayId " +
					  $"WHERE {Col("DispatcherBreakId")} = {CaseInsensitive("Id")} AND {Col("State")} = {P}FromState";

			return await ExecuteAsync(sql, parameters, cancellationToken) == 1;
		}

		public async Task<bool> TryMarkAlertAsync(string dispatcherBreakId, string column, DateTime sentOnUtc, CancellationToken cancellationToken = default)
		{
			if (!AlertColumns.Contains(column))
				throw new ArgumentOutOfRangeException(nameof(column));

			var parameters = new DynamicParameters(new { Id = dispatcherBreakId });
			DispatcherDeskSql.AddTimestamp(parameters, "SentOn", sentOnUtc);

			return await ExecuteAsync(
				$"UPDATE {Tbl("DispatcherBreaks")} SET {Col(column)} = {P}SentOn WHERE {Col("DispatcherBreakId")} = {CaseInsensitive("Id")} AND {Col(column)} IS NULL",
				parameters, cancellationToken) == 1;
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

		public Task<IEnumerable<DispatcherPresence>> GetSignedInAsync()
		{
			return QueryAsync<DispatcherPresence>(
				$"SELECT * FROM {Tbl("DispatcherPresences")} WHERE {Col("State")} > {P}Offline", new { Offline = (int)DispatcherPresenceStates.Offline });
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

			if (await ExecuteAsync(sql, parameters, cancellationToken) != 1)
				return false;

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

			await ExecuteAsync($"DELETE FROM {Tbl("DispatcherConversationMessages")} WHERE {Col("DispatcherQueueItemId")} IN ({closed})", parameters, cancellationToken);
			await ExecuteAsync($"DELETE FROM {Tbl("DispatcherQueueEvents")} WHERE {Col("DispatcherQueueItemId")} IN ({closed})", parameters, cancellationToken);

			return await ExecuteAsync(
				$"DELETE FROM {Tbl("DispatcherQueueItems")} WHERE {Col("State")} >= {P}Completed AND {Col("LastActivityOn")} < {P}Before", parameters, cancellationToken);
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
	}
}
