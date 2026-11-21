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
	/// Intake SMS conversations (M0325). ParticipantAddress and AssignedUserId are citext on PostgreSQL and are compared
	/// case-insensitively on both providers; the ids are varchar and compared exactly. Every state change is one conditional statement.
	/// </summary>
	public class IntakeConversationsRepository : RmsRepositoryBase<IntakeConversation>, IIntakeConversationsRepository
	{
		private const string Table = "IntakeConversations";
		private const int Open = (int)IntakeConversationStates.Open;

		private static readonly string[] InsertColumns =
		{
			"IntakeConversationId", "DepartmentId", "IntakeChannelId", "CallId", "ChannelType", "ParticipantAddress", "ParticipantName", "InboundAddress",
			"ReplyFromAddress", "State", "Handler", "AssignedUserId", "AssignedOn", "AssistantTurns", "OptedOut", "DeskItemId", "StartedOn", "LastInboundOn",
			"LastActivityOn", "ClosedOn", "CloseReason"
		};

		public IntakeConversationsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<bool> TryInsertOpenAsync(IntakeConversation conversation, CancellationToken cancellationToken = default)
		{
			if (conversation == null)
				throw new ArgumentNullException(nameof(conversation));

			Utf8WriteGuard.Sanitize(conversation);
			var values = string.Join(", ", Array.ConvertAll(InsertColumns, c => P + c));

			// The partial unique index (one open thread per department, transport and participant) is the arbiter: ON CONFLICT on
			// PostgreSQL, a key-range lock on SQL Server, so a concurrent twin waits and then inserts nothing instead of raising.
			var sql = IsPostgres
				? $"INSERT INTO {Tbl(Table)} ({Cols(InsertColumns)}) VALUES ({values}) ON CONFLICT ({Col("DepartmentId")}, {Col("ChannelType")}, {Col("ParticipantAddress")}) " +
				  $"WHERE {Col("State")} = {Open} DO NOTHING"
				: $"INSERT INTO {Tbl(Table)} ({Cols(InsertColumns)}) SELECT {values} " +
				  $"WHERE NOT EXISTS (SELECT 1 FROM {Tbl(Table)} WITH (UPDLOCK, HOLDLOCK) WHERE {Col("DepartmentId")} = {P}DepartmentId " +
				  $"AND {Col("ChannelType")} = {P}ChannelType AND {Col("ParticipantAddress")} = {P}ParticipantAddress AND {Col("State")} = {Open})";

			var inserted = await ExecuteAsync(sql, new
			{
				conversation.IntakeConversationId,
				conversation.DepartmentId,
				conversation.IntakeChannelId,
				conversation.CallId,
				conversation.ChannelType,
				conversation.ParticipantAddress,
				conversation.ParticipantName,
				conversation.InboundAddress,
				conversation.ReplyFromAddress,
				conversation.State,
				conversation.Handler,
				conversation.AssignedUserId,
				AssignedOn = Stamp(conversation.AssignedOn),
				conversation.AssistantTurns,
				conversation.OptedOut,
				conversation.DeskItemId,
				StartedOn = DatabaseTimestamp(conversation.StartedOn),
				LastInboundOn = Stamp(conversation.LastInboundOn),
				LastActivityOn = DatabaseTimestamp(conversation.LastActivityOn),
				ClosedOn = Stamp(conversation.ClosedOn),
				conversation.CloseReason
			}, cancellationToken);

			return inserted == 1;
		}

		public Task<IntakeConversation> GetOpenForParticipantAsync(int departmentId, int channelType, string participantAddress)
		{
			return QueryFirstOrDefaultAsync<IntakeConversation>(
				$"SELECT * FROM {Tbl(Table)} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("ChannelType")} = {P}ChannelType " +
				$"AND {Col("ParticipantAddress")} = {CaseInsensitive("Participant")} AND {Col("State")} = {Open}",
				new { DepartmentId = departmentId, ChannelType = channelType, Participant = participantAddress });
		}

		public Task<IntakeConversation> GetOpenForReplyAsync(int channelType, string participantAddress, DateTime activeSince)
		{
			var where = $"WHERE {Col("ChannelType")} = {P}ChannelType AND {Col("ParticipantAddress")} = {CaseInsensitive("Participant")} " +
				$"AND {Col("State")} = {Open} AND {Col("LastActivityOn")} >= {P}Since ORDER BY {Col("LastActivityOn")} DESC";
			var sql = IsPostgres
				? $"SELECT * FROM {Tbl(Table)} {where} LIMIT 1"
				: $"SELECT TOP (1) * FROM {Tbl(Table)} {where}";

			return QueryFirstOrDefaultAsync<IntakeConversation>(sql,
				new { ChannelType = channelType, Participant = participantAddress, Since = DatabaseTimestamp(activeSince) });
		}

		public Task<IntakeConversation> GetLatestForCallAsync(int departmentId, int callId)
		{
			var where = $"WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("CallId")} = {P}CallId ORDER BY {Col("State")} ASC, {Col("StartedOn")} DESC";
			var sql = IsPostgres
				? $"SELECT * FROM {Tbl(Table)} {where} LIMIT 1"
				: $"SELECT TOP (1) * FROM {Tbl(Table)} {where}";

			return QueryFirstOrDefaultAsync<IntakeConversation>(sql, new { DepartmentId = departmentId, CallId = callId });
		}

		public async Task<List<IntakeConversation>> GetOpenIdleAsync(DateTime idleBefore, int take)
		{
			take = Math.Clamp(take, 1, 1000);
			var where = $"WHERE {Col("State")} = {Open} AND {Col("LastActivityOn")} < {P}Before ORDER BY {Col("LastActivityOn")} ASC";
			var sql = IsPostgres
				? $"SELECT * FROM {Tbl(Table)} {where} LIMIT {take}"
				: $"SELECT TOP ({take}) * FROM {Tbl(Table)} {where}";

			var rows = await QueryAsync<IntakeConversation>(sql, new { Before = DatabaseTimestamp(idleBefore) });
			return rows?.ToList() ?? new List<IntakeConversation>();
		}

		public async Task<List<IntakeConversation>> GetOpenWithEndedCallAsync(int[] openCallStates, int take)
		{
			take = Math.Clamp(take, 1, 1000);
			var parameters = new DynamicParameters();
			parameters.Add("OpenStates", InListValue(openCallStates != null && openCallStates.Length > 0 ? openCallStates : new[] { (int)CallStates.Active }));
			parameters.Add("Deleted", true);

			// No foreign key (M0325): a call purged with its department or by hand simply has no row.
			var from = $"FROM {Tbl(Table)} c LEFT JOIN {Tbl("Calls")} k ON k.{Col("CallId")} = c.{Col("CallId")} AND k.{Col("DepartmentId")} = c.{Col("DepartmentId")} " +
				$"WHERE c.{Col("State")} = {Open} AND c.{Col("CallId")} IS NOT NULL AND (k.{Col("CallId")} IS NULL OR k.{Col("IsDeleted")} = {P}Deleted " +
				$"OR NOT ({InList("State", "OpenStates", "k")})) ORDER BY c.{Col("LastActivityOn")} ASC";
			var sql = IsPostgres
				? $"SELECT c.* {from} LIMIT {take}"
				: $"SELECT TOP ({take}) c.* {from}";

			var rows = await QueryAsync<IntakeConversation>(sql, parameters);
			return rows?.ToList() ?? new List<IntakeConversation>();
		}

		public async Task<bool> TryConnectAsync(string conversationId, int departmentId, string userId, string expectedAssignedUserId, DateTime assignedOn,
			CancellationToken cancellationToken = default)
		{
			var expected = string.IsNullOrWhiteSpace(expectedAssignedUserId)
				? $"{Col("AssignedUserId")} IS NULL"
				: $"{Col("AssignedUserId")} = {CaseInsensitive("Expected")}";

			var updated = await ExecuteAsync(
				$"UPDATE {Tbl(Table)} SET {Col("Handler")} = {P}Handler, {Col("AssignedUserId")} = {P}UserId, {Col("AssignedOn")} = {P}AssignedOn, " +
				$"{Col("LastActivityOn")} = {P}AssignedOn WHERE {Col("IntakeConversationId")} = {P}Id AND {Col("DepartmentId")} = {P}DepartmentId " +
				$"AND {Col("State")} = {Open} AND {Col("OptedOut")} = {P}NotOptedOut AND {expected}",
				new
				{
					Id = conversationId, DepartmentId = departmentId, UserId = userId, Expected = expectedAssignedUserId, NotOptedOut = false,
					Handler = (int)IntakeConversationHandlers.Dispatcher, AssignedOn = DatabaseTimestamp(assignedOn)
				},
				cancellationToken);

			return updated == 1;
		}

		public async Task<bool> TryReleaseAsync(string conversationId, int departmentId, string userId, int handler, CancellationToken cancellationToken = default)
		{
			var updated = await ExecuteAsync(
				$"UPDATE {Tbl(Table)} SET {Col("Handler")} = {P}Handler, {Col("AssignedUserId")} = NULL, {Col("AssignedOn")} = NULL " +
				$"WHERE {Col("IntakeConversationId")} = {P}Id AND {Col("DepartmentId")} = {P}DepartmentId AND {Col("State")} = {Open} " +
				$"AND {Col("AssignedUserId")} = {CaseInsensitive("UserId")}",
				new { Id = conversationId, DepartmentId = departmentId, UserId = userId, Handler = handler },
				cancellationToken);

			return updated == 1;
		}

		public async Task<bool> TrySetHandlerAsync(string conversationId, int departmentId, int expectedHandler, int handler, bool exhaustAssistantTurns, int maxAssistantTurns,
			CancellationToken cancellationToken = default)
		{
			var turns = exhaustAssistantTurns
				? $", {Col("AssistantTurns")} = CASE WHEN {Col("AssistantTurns")} < {P}MaxTurns THEN {P}MaxTurns ELSE {Col("AssistantTurns")} END"
				: string.Empty;

			var updated = await ExecuteAsync(
				$"UPDATE {Tbl(Table)} SET {Col("Handler")} = {P}Handler{turns} WHERE {Col("IntakeConversationId")} = {P}Id AND {Col("DepartmentId")} = {P}DepartmentId " +
				$"AND {Col("State")} = {Open} AND {Col("Handler")} = {P}Expected",
				new { Id = conversationId, DepartmentId = departmentId, Expected = expectedHandler, Handler = handler, MaxTurns = Math.Max(0, maxAssistantTurns) },
				cancellationToken);

			return updated == 1;
		}

		public async Task<bool> TryClaimAssistantTurnAsync(string conversationId, int departmentId, int expectedTurns, CancellationToken cancellationToken = default)
		{
			var updated = await ExecuteAsync(
				$"UPDATE {Tbl(Table)} SET {Col("AssistantTurns")} = {Col("AssistantTurns")} + 1 WHERE {Col("IntakeConversationId")} = {P}Id " +
				$"AND {Col("DepartmentId")} = {P}DepartmentId AND {Col("State")} = {Open} AND {Col("Handler")} = {P}Assistant AND {Col("AssistantTurns")} = {P}Expected",
				new { Id = conversationId, DepartmentId = departmentId, Expected = expectedTurns, Assistant = (int)IntakeConversationHandlers.Assistant },
				cancellationToken);

			return updated == 1;
		}

		public async Task<bool> TryCloseAsync(string conversationId, int departmentId, string reason, bool optedOut, DateTime closedOn, CancellationToken cancellationToken = default)
		{
			var optOut = optedOut ? $", {Col("OptedOut")} = {P}OptedOut" : string.Empty;
			var updated = await ExecuteAsync(
				$"UPDATE {Tbl(Table)} SET {Col("State")} = {P}Closed, {Col("ClosedOn")} = {P}ClosedOn, {Col("CloseReason")} = {P}Reason{optOut} " +
				$"WHERE {Col("IntakeConversationId")} = {P}Id AND {Col("DepartmentId")} = {P}DepartmentId AND {Col("State")} = {Open}",
				new
				{
					Id = conversationId, DepartmentId = departmentId, Closed = (int)IntakeConversationStates.Closed, ClosedOn = DatabaseTimestamp(closedOn),
					Reason = reason == null ? null : reason.Length <= 200 ? reason : reason.Substring(0, 200), OptedOut = true
				},
				cancellationToken);

			return updated == 1;
		}

		public async Task<bool> TryCloseIdleAsync(string conversationId, int departmentId, string reason, DateTime idleBefore, DateTime closedOn,
			CancellationToken cancellationToken = default)
		{
			// A reply is recorded (its message row) before the handler touches the thread's activity: a message row from after the cutoff keeps the
			// thread open even while LastActivityOn is still the old value.
			var updated = await ExecuteAsync(
				$"UPDATE {Tbl(Table)} SET {Col("State")} = {P}Closed, {Col("ClosedOn")} = {P}ClosedOn, {Col("CloseReason")} = {P}Reason " +
				$"WHERE {Col("IntakeConversationId")} = {P}Id AND {Col("DepartmentId")} = {P}DepartmentId AND {Col("State")} = {Open} " +
				$"AND {Col("LastActivityOn")} < {P}IdleBefore " +
				$"AND NOT EXISTS (SELECT 1 FROM {Tbl("IntakeConversationMessages")} m WHERE m.{Col("IntakeConversationId")} = {P}Id " +
				$"AND m.{Col("Direction")} = {P}Inbound AND m.{Col("CreatedOn")} >= {P}IdleBefore)",
				new
				{
					Id = conversationId, DepartmentId = departmentId, Closed = (int)IntakeConversationStates.Closed, ClosedOn = DatabaseTimestamp(closedOn),
					Reason = reason == null ? null : reason.Length <= 200 ? reason : reason.Substring(0, 200), IdleBefore = DatabaseTimestamp(idleBefore),
					Inbound = (int)IntakeConversationMessageDirections.Inbound
				},
				cancellationToken);

			return updated == 1;
		}

		public Task TouchAsync(string conversationId, int departmentId, DateTime lastActivityOn, DateTime? lastInboundOn, CancellationToken cancellationToken = default)
		{
			var inbound = lastInboundOn.HasValue
				? $", {Col("LastInboundOn")} = CASE WHEN {Col("LastInboundOn")} IS NULL OR {Col("LastInboundOn")} < {P}Inbound THEN {P}Inbound ELSE {Col("LastInboundOn")} END"
				: string.Empty;

			return ExecuteAsync(
				$"UPDATE {Tbl(Table)} SET {Col("LastActivityOn")} = CASE WHEN {Col("LastActivityOn")} < {P}Activity THEN {P}Activity ELSE {Col("LastActivityOn")} END{inbound} " +
				$"WHERE {Col("IntakeConversationId")} = {P}Id AND {Col("DepartmentId")} = {P}DepartmentId",
				new { Id = conversationId, DepartmentId = departmentId, Activity = DatabaseTimestamp(lastActivityOn), Inbound = Stamp(lastInboundOn) },
				cancellationToken);
		}

		public async Task<bool> SetDeskItemAsync(string conversationId, int departmentId, string deskItemId, CancellationToken cancellationToken = default)
		{
			var updated = await ExecuteAsync(
				$"UPDATE {Tbl(Table)} SET {Col("DeskItemId")} = {P}DeskItemId WHERE {Col("IntakeConversationId")} = {P}Id AND {Col("DepartmentId")} = {P}DepartmentId",
				new { Id = conversationId, DepartmentId = departmentId, DeskItemId = deskItemId },
				cancellationToken);

			return updated == 1;
		}

		private static DateTime? Stamp(DateTime? value) => value.HasValue ? DatabaseTimestamp(value.Value) : (DateTime?)null;
	}

	/// <summary>Intake conversation delivery log (M0325): an inbound provider retry is one row (conditional insert, never raises).</summary>
	public class IntakeConversationMessagesRepository : RmsRepositoryBase<IntakeConversationMessage>, IIntakeConversationMessagesRepository
	{
		private const string Table = "IntakeConversationMessages";

		private static readonly string[] InsertColumns =
		{
			"IntakeConversationMessageId", "IntakeConversationId", "DepartmentId", "Direction", "Author", "AuthorUserId", "CallNoteId", "ProviderMessageId",
			"Status", "Error", "CreatedOn", "SentOn"
		};

		public IntakeConversationMessagesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<bool> TryInsertInboundAsync(IntakeConversationMessage message, CancellationToken cancellationToken = default)
		{
			if (message == null)
				throw new ArgumentNullException(nameof(message));

			if (string.IsNullOrWhiteSpace(message.ProviderMessageId))
			{
				await InsertMessageAsync(message, cancellationToken);
				return true;
			}

			Utf8WriteGuard.Sanitize(message);
			var values = string.Join(", ", Array.ConvertAll(InsertColumns, c => P + c));
			var inbound = (int)IntakeConversationMessageDirections.Inbound;
			var sql = IsPostgres
				? $"INSERT INTO {Tbl(Table)} ({Cols(InsertColumns)}) VALUES ({values}) ON CONFLICT ({Col("IntakeConversationId")}, {Col("ProviderMessageId")}) " +
				  $"WHERE {Col("ProviderMessageId")} IS NOT NULL AND {Col("Direction")} = {inbound} DO NOTHING"
				: $"INSERT INTO {Tbl(Table)} ({Cols(InsertColumns)}) SELECT {values} " +
				  $"WHERE NOT EXISTS (SELECT 1 FROM {Tbl(Table)} WITH (UPDLOCK, HOLDLOCK) WHERE {Col("IntakeConversationId")} = {P}IntakeConversationId " +
				  $"AND {Col("ProviderMessageId")} = {P}ProviderMessageId AND {Col("Direction")} = {inbound})";

			return await ExecuteAsync(sql, Parameters(message), cancellationToken) == 1;
		}

		public async Task InsertMessageAsync(IntakeConversationMessage message, CancellationToken cancellationToken = default)
		{
			if (message == null)
				throw new ArgumentNullException(nameof(message));

			Utf8WriteGuard.Sanitize(message);
			var values = string.Join(", ", Array.ConvertAll(InsertColumns, c => P + c));
			await ExecuteAsync($"INSERT INTO {Tbl(Table)} ({Cols(InsertColumns)}) VALUES ({values})", Parameters(message), cancellationToken);
		}

		public async Task<List<IntakeConversationMessage>> GetForConversationAsync(int departmentId, string conversationId)
		{
			var rows = await QueryAsync<IntakeConversationMessage>(
				$"SELECT * FROM {Tbl(Table)} WHERE {Col("IntakeConversationId")} = {P}Id AND {Col("DepartmentId")} = {P}DepartmentId " +
				$"ORDER BY {Col("CreatedOn")} ASC, {Col("IntakeConversationMessageId")} ASC",
				new { Id = conversationId, DepartmentId = departmentId });

			return rows?.ToList() ?? new List<IntakeConversationMessage>();
		}

		public async Task<bool> UpdateDeliveryAsync(string messageId, int departmentId, int status, string providerMessageId, string error, DateTime? sentOn,
			CancellationToken cancellationToken = default)
		{
			var updated = await ExecuteAsync(
				$"UPDATE {Tbl(Table)} SET {Col("Status")} = {P}Status, {Col("ProviderMessageId")} = {P}ProviderId, {Col("Error")} = {P}Error, {Col("SentOn")} = {P}SentOn " +
				$"WHERE {Col("IntakeConversationMessageId")} = {P}Id AND {Col("DepartmentId")} = {P}DepartmentId",
				new
				{
					Id = messageId, DepartmentId = departmentId, Status = status, ProviderId = Cap(providerMessageId, 256), Error = Cap(error, 1000),
					SentOn = sentOn.HasValue ? DatabaseTimestamp(sentOn.Value) : (DateTime?)null
				},
				cancellationToken);

			return updated == 1;
		}

		public async Task<bool> SetCallNoteAsync(string messageId, int departmentId, int callNoteId, CancellationToken cancellationToken = default)
		{
			var updated = await ExecuteAsync(
				$"UPDATE {Tbl(Table)} SET {Col("CallNoteId")} = {P}CallNoteId WHERE {Col("IntakeConversationMessageId")} = {P}Id AND {Col("DepartmentId")} = {P}DepartmentId",
				new { Id = messageId, DepartmentId = departmentId, CallNoteId = callNoteId },
				cancellationToken);

			return updated == 1;
		}

		private static object Parameters(IntakeConversationMessage message) => new
		{
			message.IntakeConversationMessageId,
			message.IntakeConversationId,
			message.DepartmentId,
			message.Direction,
			message.Author,
			message.AuthorUserId,
			message.CallNoteId,
			ProviderMessageId = Cap(message.ProviderMessageId, 256),
			message.Status,
			Error = Cap(message.Error, 1000),
			CreatedOn = DatabaseTimestamp(message.CreatedOn),
			SentOn = message.SentOn.HasValue ? DatabaseTimestamp(message.SentOn.Value) : (DateTime?)null
		};

		private static string Cap(string value, int max) => value == null ? null : value.Length <= max ? value : value.Substring(0, max);
	}
}
