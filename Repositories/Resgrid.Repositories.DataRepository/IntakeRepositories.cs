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
	/// <summary>Intake channels (M0323). Address is citext on PostgreSQL, compared case-insensitively on both providers.</summary>
	public class IntakeChannelsRepository : RmsRepositoryBase<IntakeChannel>, IIntakeChannelsRepository
	{
		private const string Table = "IntakeChannels";

		public IntakeChannelsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<List<IntakeChannel>> GetLiveForDepartmentAsync(int departmentId)
		{
			var rows = await QueryAsync<IntakeChannel>(
				$"SELECT * FROM {Tbl(Table)} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("IsDeleted")} = {False}",
				new { DepartmentId = departmentId });

			return rows?.ToList() ?? new List<IntakeChannel>();
		}

		public Task<IntakeChannel> GetLiveByAddressAsync(int channelType, string address)
		{
			return QueryFirstOrDefaultAsync<IntakeChannel>(
				$"SELECT * FROM {Tbl(Table)} WHERE {Col("ChannelType")} = {P}ChannelType AND {Col("Address")} = {CaseInsensitive("Address")} AND {Col("IsDeleted")} = {False}",
				new { ChannelType = channelType, Address = address });
		}

		private static string False => IsPostgres ? "false" : "0";
	}

	/// <summary>Intake messages (M0323): the claim insert is one conditional statement so a redelivery never throws.</summary>
	public class IntakeMessagesRepository : RmsRepositoryBase<IntakeMessage>, IIntakeMessagesRepository
	{
		private const string Table = "IntakeMessages";

		private static readonly string[] InsertColumns =
			{ "IntakeMessageId", "DepartmentId", "IntakeChannelId", "ChannelType", "ProviderMessageId", "Sender", "State", "CallId", "Reason", "ReceivedOn", "CompletedOn" };

		public IntakeMessagesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<bool> TryClaimAsync(IntakeMessage message, CancellationToken cancellationToken = default)
		{
			if (message == null)
				throw new ArgumentNullException(nameof(message));

			Utf8WriteGuard.Sanitize(message);
			var values = string.Join(", ", Array.ConvertAll(InsertColumns, c => P + c));

			// Same shape as InboundEmailReceipts: ON CONFLICT on the partial unique index (PostgreSQL), a key-range lock on SQL
			// Server, so the twin of a concurrent redelivery waits and then inserts nothing.
			var sql = IsPostgres
				? $"INSERT INTO {Tbl(Table)} ({Cols(InsertColumns)}) VALUES ({values}) ON CONFLICT ({Col("IntakeChannelId")}, {Col("ProviderMessageId")}) " +
				  $"WHERE {Col("IntakeChannelId")} IS NOT NULL AND {Col("ProviderMessageId")} IS NOT NULL DO NOTHING"
				: $"INSERT INTO {Tbl(Table)} ({Cols(InsertColumns)}) SELECT {values} " +
				  $"WHERE NOT EXISTS (SELECT 1 FROM {Tbl(Table)} WITH (UPDLOCK, HOLDLOCK) WHERE {Col("IntakeChannelId")} = {P}IntakeChannelId " +
				  $"AND {Col("ProviderMessageId")} = {P}ProviderMessageId)";

			var inserted = await ExecuteAsync(sql, new
			{
				message.IntakeMessageId,
				message.DepartmentId,
				message.IntakeChannelId,
				message.ChannelType,
				message.ProviderMessageId,
				message.Sender,
				message.State,
				message.CallId,
				message.Reason,
				ReceivedOn = DatabaseTimestamp(message.ReceivedOn),
				CompletedOn = message.CompletedOn.HasValue ? DatabaseTimestamp(message.CompletedOn.Value) : (DateTime?)null
			}, cancellationToken);

			return inserted == 1;
		}

		public Task<IntakeMessage> GetByProviderMessageIdAsync(string intakeChannelId, string providerMessageId)
		{
			return QueryFirstOrDefaultAsync<IntakeMessage>(
				$"SELECT * FROM {Tbl(Table)} WHERE {Col("IntakeChannelId")} = {P}ChannelId AND {Col("ProviderMessageId")} = {CaseInsensitive("ProviderId")}",
				new { ChannelId = intakeChannelId, ProviderId = providerMessageId });
		}

		public async Task<bool> TryReclaimAsync(string intakeMessageId, int expectedState, DateTime now, DateTime abandonedBefore, CancellationToken cancellationToken = default)
		{
			// A Failed claim changes state on takeover, so a second taker no longer matches it. A Received (abandoned) one keeps its
			// state, so the age is part of the predicate and ReceivedOn moves to now: the second taker's row is no longer old enough.
			var updated = await ExecuteAsync(
				$"UPDATE {Tbl(Table)} SET {Col("State")} = {P}Received, {Col("Reason")} = {P}Reason, {Col("ReceivedOn")} = {P}Now " +
				$"WHERE {Col("IntakeMessageId")} = {P}Id AND {Col("State")} = {P}Expected AND {Col("CallId")} IS NULL " +
				$"AND ({Col("State")} <> {P}Received OR {Col("ReceivedOn")} < {P}AbandonedBefore)",
				new
				{
					Id = intakeMessageId,
					Expected = expectedState,
					Received = (int)IntakeMessageStates.Received,
					Reason = "Retried",
					Now = DatabaseTimestamp(now),
					AbandonedBefore = DatabaseTimestamp(abandonedBefore)
				},
				cancellationToken);

			return updated == 1;
		}

		public async Task<int?> GetUnclaimedIntakeCallIdAsync(int departmentId, string sourceIdentifier, int[] callSources, DateTime loggedSince,
			CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(sourceIdentifier) || callSources == null || callSources.Length == 0)
				return null;

			var parameters = new DynamicParameters();
			parameters.Add("DepartmentId", departmentId);
			parameters.Add("SourceIdentifier", sourceIdentifier);
			parameters.Add("Sources", InListValue(callSources));
			parameters.Add("LoggedSince", DatabaseTimestamp(loggedSince));
			var top = IsPostgres ? string.Empty : "TOP 1 ";
			var limit = IsPostgres ? " LIMIT 1" : string.Empty;
			var ids = await QueryAsync<int>(
				$"SELECT {top}c.{Col("CallId")} FROM {Tbl("Calls")} c WHERE c.{Col("DepartmentId")} = {P}DepartmentId " +
				$"AND c.{Col("SourceIdentifier")} = {CaseInsensitive("SourceIdentifier")} AND {InList("CallSource", "Sources", "c")} " +
				$"AND c.{Col("LoggedOn")} >= {P}LoggedSince AND c.{Col("IsDeleted")} = {False} " +
				$"AND NOT EXISTS (SELECT 1 FROM {Tbl(Table)} m WHERE m.{Col("CallId")} = c.{Col("CallId")}) " +
				$"ORDER BY c.{Col("CallId")} DESC{limit}",
				parameters, cancellationToken);

			var id = ids?.FirstOrDefault() ?? 0;
			return id > 0 ? id : (int?)null;
		}

		private static string False => IsPostgres ? "false" : "0";

		public async Task<bool> CompleteAsync(string intakeMessageId, int state, int? callId, string reason, DateTime completedOn, CancellationToken cancellationToken = default)
		{
			// A call recorded on the claim is never cleared or replaced: a late completion from an attempt that lost its claim (it ran past
			// the abandonment window while a retry took over) must not unlink the call the new owner recorded.
			var updated = await ExecuteAsync(
				$"UPDATE {Tbl(Table)} SET {Col("State")} = {P}State, {Col("CallId")} = {P}CallId, {Col("Reason")} = {P}Reason, {Col("CompletedOn")} = {P}CompletedOn " +
				$"WHERE {Col("IntakeMessageId")} = {P}Id AND ({Col("CallId")} IS NULL OR {Col("CallId")} = {P}CallId)",
				new { Id = intakeMessageId, State = state, CallId = callId, Reason = reason, CompletedOn = DatabaseTimestamp(completedOn) },
				cancellationToken);

			return updated == 1;
		}

		public async Task<bool> ReleaseReclaimAsync(string intakeMessageId, DateTime claimedOn, DateTime receivedOn, CancellationToken cancellationToken = default)
		{
			// Only this caller's takeover still in progress (Received, no call, ReceivedOn still the stamp its takeover wrote) moves back,
			// and only to an earlier time: the row reads as abandoned again, so the next delivery takes it over at once and repeats the
			// saved-call lookup. A newer owner's takeover restamped ReceivedOn, so a stalled attempt's release matches nothing.
			var updated = await ExecuteAsync(
				$"UPDATE {Tbl(Table)} SET {Col("ReceivedOn")} = {P}ReceivedOn, {Col("Reason")} = {P}Reason " +
				$"WHERE {Col("IntakeMessageId")} = {P}Id AND {Col("State")} = {P}Received AND {Col("CallId")} IS NULL " +
				$"AND {Col("ReceivedOn")} = {P}ClaimedOn AND {Col("ReceivedOn")} > {P}ReceivedOn",
				new
				{
					Id = intakeMessageId,
					Received = (int)IntakeMessageStates.Received,
					Reason = "RetryReleased",
					ClaimedOn = DatabaseTimestamp(claimedOn),
					ReceivedOn = DatabaseTimestamp(receivedOn)
				},
				cancellationToken);

			return updated == 1;
		}

		public async Task<int> CountAsync(int departmentId, int channelType, DateTime from, DateTime before, int[] excludedStates)
		{
			var parameters = Window(departmentId, from, before, excludedStates);
			parameters.Add("ChannelType", channelType);
			return await ScalarAsync<int>(
				$"SELECT COUNT(*) FROM {Tbl(Table)} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("ChannelType")} = {P}ChannelType " +
				$"AND {Col("ReceivedOn")} >= {P}FromOn AND {Col("ReceivedOn")} < {P}BeforeOn AND NOT ({InList("State", "Excluded")})",
				parameters);
		}

		public async Task<Dictionary<int, int>> CountByTypeAsync(int departmentId, DateTime from, DateTime before, int[] excludedStates)
		{
			var rows = await QueryAsync<KeyCount<int>>(
				$"SELECT {Col("ChannelType")} AS {Col("Key")}, COUNT(*) AS {Col("Count")} FROM {Tbl(Table)} WHERE {Col("DepartmentId")} = {P}DepartmentId " +
				$"AND {Col("ReceivedOn")} >= {P}FromOn AND {Col("ReceivedOn")} < {P}BeforeOn AND NOT ({InList("State", "Excluded")}) GROUP BY {Col("ChannelType")}",
				Window(departmentId, from, before, excludedStates));

			return (rows ?? Enumerable.Empty<KeyCount<int>>()).ToDictionary(r => r.Key, r => r.Count);
		}

		public async Task<Dictionary<string, int>> CountByChannelAsync(int departmentId, DateTime from, DateTime before, int[] excludedStates)
		{
			var rows = await QueryAsync<KeyCount<string>>(
				$"SELECT {Col("IntakeChannelId")} AS {Col("Key")}, COUNT(*) AS {Col("Count")} FROM {Tbl(Table)} WHERE {Col("DepartmentId")} = {P}DepartmentId " +
				$"AND {Col("IntakeChannelId")} IS NOT NULL AND {Col("ReceivedOn")} >= {P}FromOn AND {Col("ReceivedOn")} < {P}BeforeOn " +
				$"AND NOT ({InList("State", "Excluded")}) GROUP BY {Col("IntakeChannelId")}",
				Window(departmentId, from, before, excludedStates));

			return (rows ?? Enumerable.Empty<KeyCount<string>>()).Where(r => r.Key != null)
				.ToDictionary(r => r.Key, r => r.Count, StringComparer.OrdinalIgnoreCase);
		}

		public async Task<List<IntakeMessage>> GetByCallIdsAsync(int departmentId, IEnumerable<int> callIds)
		{
			var ids = InListValue(callIds?.Distinct());
			if (ids.Length == 0)
				return new List<IntakeMessage>();

			var parameters = new DynamicParameters();
			parameters.Add("DepartmentId", departmentId);
			parameters.Add("CallIds", ids);
			parameters.Add("Created", (int)IntakeMessageStates.CallCreated);
			var rows = await QueryAsync<IntakeMessage>(
				$"SELECT * FROM {Tbl(Table)} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("State")} = {P}Created AND {InList("CallId", "CallIds")}",
				parameters);

			return rows?.ToList() ?? new List<IntakeMessage>();
		}

		public async Task<List<IntakeMessage>> GetCallsCreatedBySenderSinceAsync(int departmentId, string sender, DateTime since, int[] channelTypes)
		{
			if (string.IsNullOrWhiteSpace(sender) || channelTypes == null || channelTypes.Length == 0)
				return new List<IntakeMessage>();

			var parameters = new DynamicParameters();
			parameters.Add("DepartmentId", departmentId);
			parameters.Add("Sender", sender);
			parameters.Add("Created", (int)IntakeMessageStates.CallCreated);
			parameters.Add("Since", DatabaseTimestamp(since));
			parameters.Add("ChannelTypes", InListValue(channelTypes));
			var rows = await QueryAsync<IntakeMessage>(
				$"SELECT * FROM {Tbl(Table)} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("Sender")} = {CaseInsensitive("Sender")} " +
				$"AND {InList("ChannelType", "ChannelTypes")} AND {Col("State")} = {P}Created AND {Col("CallId")} IS NOT NULL AND {Col("ReceivedOn")} >= {P}Since " +
				$"ORDER BY {Col("ReceivedOn")} DESC",
				parameters);

			return rows?.ToList() ?? new List<IntakeMessage>();
		}

		public async Task<bool> SetScreeningAsync(int departmentId, int callId, int screening, int[] replaceable, string userId, DateTime screenedOn,
			CancellationToken cancellationToken = default)
		{
			var parameters = new DynamicParameters();
			parameters.Add("DepartmentId", departmentId);
			parameters.Add("CallId", callId);
			parameters.Add("Created", (int)IntakeMessageStates.CallCreated);
			parameters.Add("Screening", screening);
			parameters.Add("UserId", userId);
			parameters.Add("ScreenedOn", DatabaseTimestamp(screenedOn));
			// An empty IN list is invalid SQL Server; -1 is never an IntakeScreenings value.
			parameters.Add("Replaceable", InListValue(replaceable != null && replaceable.Length > 0 ? replaceable : new[] { -1 }));

			// Unscreened rows (NULL, from before M0326 or never screened) read as None, so "replace None" covers both.
			var current = $"COALESCE({Col("Screening")}, 0)";
			var replaceableMatch = IsPostgres ? $"{current} = ANY({P}Replaceable)" : $"{current} IN {P}Replaceable";
			var updated = await ExecuteAsync(
				$"UPDATE {Tbl(Table)} SET {Col("Screening")} = {P}Screening, {Col("ScreenedOn")} = {P}ScreenedOn, {Col("ScreenedByUserId")} = {P}UserId " +
				$"WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("CallId")} = {P}CallId AND {Col("State")} = {P}Created " +
				$"AND {replaceableMatch} AND {current} <> {P}Screening",
				parameters, cancellationToken);

			return updated > 0;
		}

		public Task<int> PurgeReceivedBeforeAsync(DateTime before, CancellationToken cancellationToken = default)
		{
			return ExecuteAsync($"DELETE FROM {Tbl(Table)} WHERE {Col("ReceivedOn")} < {P}Before", new { Before = DatabaseTimestamp(before) }, cancellationToken);
		}

		private static DynamicParameters Window(int departmentId, DateTime from, DateTime before, int[] excludedStates)
		{
			var parameters = new DynamicParameters();
			parameters.Add("DepartmentId", departmentId);
			parameters.Add("FromOn", DatabaseTimestamp(from));
			parameters.Add("BeforeOn", DatabaseTimestamp(before));
			// An empty IN list is invalid SQL Server; 0 is never an IntakeMessageStates value.
			parameters.Add("Excluded", InListValue(excludedStates != null && excludedStates.Length > 0 ? excludedStates : new[] { 0 }));
			return parameters;
		}

		private sealed class KeyCount<TKey>
		{
			public TKey Key { get; set; }

			public int Count { get; set; }
		}
	}

	/// <summary>
	/// Blocked intake senders (M0326). One standing (not lifted) row per department and sender, enforced by a filtered unique index and
	/// a conditional insert, so two dispatchers dismissing the same sender at once leave one row. Sender is citext on PostgreSQL.
	/// </summary>
	public class IntakeBlockedSendersRepository : RmsRepositoryBase<IntakeBlockedSender>, IIntakeBlockedSendersRepository
	{
		private const string Table = "IntakeBlockedSenders";

		private static readonly string[] InsertColumns =
		{
			"IntakeBlockedSenderId", "DepartmentId", "Sender", "ChannelType", "Reason", "CallId", "BlockedByUserId", "BlockedOn", "ExpiresOn",
			"RemovedByUserId", "RemovedOn"
		};

		public IntakeBlockedSendersRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<IntakeBlockedSender> GetInForceAsync(int departmentId, string sender, DateTime nowUtc)
		{
			return QueryFirstOrDefaultAsync<IntakeBlockedSender>(
				$"SELECT * FROM {Tbl(Table)} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("Sender")} = {CaseInsensitive("Sender")} " +
				$"AND {Col("RemovedOn")} IS NULL AND ({Col("ExpiresOn")} IS NULL OR {Col("ExpiresOn")} > {P}Now)",
				new { DepartmentId = departmentId, Sender = sender, Now = DatabaseTimestamp(nowUtc) });
		}

		public Task<IntakeBlockedSender> GetCurrentAsync(int departmentId, string sender)
		{
			return QueryFirstOrDefaultAsync<IntakeBlockedSender>(
				$"SELECT * FROM {Tbl(Table)} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("Sender")} = {CaseInsensitive("Sender")} AND {Col("RemovedOn")} IS NULL",
				new { DepartmentId = departmentId, Sender = sender });
		}

		public async Task<List<IntakeBlockedSender>> GetCurrentForDepartmentAsync(int departmentId)
		{
			var rows = await QueryAsync<IntakeBlockedSender>(
				$"SELECT * FROM {Tbl(Table)} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RemovedOn")} IS NULL ORDER BY {Col("BlockedOn")} DESC",
				new { DepartmentId = departmentId });

			return rows?.ToList() ?? new List<IntakeBlockedSender>();
		}

		public async Task<bool> TryInsertAsync(IntakeBlockedSender block, CancellationToken cancellationToken = default)
		{
			if (block == null)
				throw new ArgumentNullException(nameof(block));

			Utf8WriteGuard.Sanitize(block);
			var values = string.Join(", ", Array.ConvertAll(InsertColumns, c => P + c));
			var sql = IsPostgres
				? $"INSERT INTO {Tbl(Table)} ({Cols(InsertColumns)}) VALUES ({values}) ON CONFLICT ({Col("DepartmentId")}, {Col("Sender")}) " +
				  $"WHERE {Col("RemovedOn")} IS NULL DO NOTHING"
				: $"INSERT INTO {Tbl(Table)} ({Cols(InsertColumns)}) SELECT {values} " +
				  $"WHERE NOT EXISTS (SELECT 1 FROM {Tbl(Table)} WITH (UPDLOCK, HOLDLOCK) WHERE {Col("DepartmentId")} = {P}DepartmentId " +
				  $"AND {Col("Sender")} = {P}Sender AND {Col("RemovedOn")} IS NULL)";

			var inserted = await ExecuteAsync(sql, new
			{
				block.IntakeBlockedSenderId,
				block.DepartmentId,
				block.Sender,
				block.ChannelType,
				block.Reason,
				block.CallId,
				block.BlockedByUserId,
				BlockedOn = DatabaseTimestamp(block.BlockedOn),
				ExpiresOn = block.ExpiresOn.HasValue ? DatabaseTimestamp(block.ExpiresOn.Value) : (DateTime?)null,
				block.RemovedByUserId,
				RemovedOn = block.RemovedOn.HasValue ? DatabaseTimestamp(block.RemovedOn.Value) : (DateTime?)null
			}, cancellationToken);

			return inserted == 1;
		}

		public async Task<bool> RenewAsync(IntakeBlockedSender block, CancellationToken cancellationToken = default)
		{
			if (block == null)
				throw new ArgumentNullException(nameof(block));

			Utf8WriteGuard.Sanitize(block);
			// The expiry only ever moves later, decided against the stored row in the same statement: a renewal that read the row before
			// another one committed cannot shorten it, and a block that never expires stays so (a null expiry asked for makes it permanent).
			// A permanent renewal is written as NULL outright: PostgreSQL cannot type a parameter that is only tested with IS NULL.
			var expires = Col("ExpiresOn");
			var expiry = block.ExpiresOn.HasValue
				? $"CASE WHEN {expires} IS NULL THEN NULL WHEN {expires} > {P}ExpiresOn THEN {expires} ELSE {P}ExpiresOn END"
				: "NULL";
			var updated = await ExecuteAsync(
				$"UPDATE {Tbl(Table)} SET {Col("ChannelType")} = {P}ChannelType, {Col("Reason")} = {P}Reason, {Col("CallId")} = {P}CallId, " +
				$"{Col("BlockedByUserId")} = {P}BlockedByUserId, {Col("BlockedOn")} = {P}BlockedOn, " +
				$"{expires} = {expiry} " +
				$"WHERE {Col("IntakeBlockedSenderId")} = {P}Id AND {Col("DepartmentId")} = {P}DepartmentId AND {Col("RemovedOn")} IS NULL",
				new
				{
					Id = block.IntakeBlockedSenderId,
					block.DepartmentId,
					block.ChannelType,
					block.Reason,
					block.CallId,
					block.BlockedByUserId,
					BlockedOn = DatabaseTimestamp(block.BlockedOn),
					ExpiresOn = block.ExpiresOn.HasValue ? DatabaseTimestamp(block.ExpiresOn.Value) : (DateTime?)null
				}, cancellationToken);

			return updated == 1;
		}

		public async Task<bool> RemoveAsync(int departmentId, string intakeBlockedSenderId, string userId, DateTime removedOn, CancellationToken cancellationToken = default)
		{
			var updated = await ExecuteAsync(
				$"UPDATE {Tbl(Table)} SET {Col("RemovedOn")} = {P}RemovedOn, {Col("RemovedByUserId")} = {P}UserId " +
				$"WHERE {Col("IntakeBlockedSenderId")} = {P}Id AND {Col("DepartmentId")} = {P}DepartmentId AND {Col("RemovedOn")} IS NULL",
				new { Id = intakeBlockedSenderId, DepartmentId = departmentId, UserId = userId, RemovedOn = DatabaseTimestamp(removedOn) },
				cancellationToken);

			return updated == 1;
		}

		public Task<int> PurgeEndedBeforeAsync(DateTime before, CancellationToken cancellationToken = default)
		{
			return ExecuteAsync(
				$"DELETE FROM {Tbl(Table)} WHERE ({Col("RemovedOn")} IS NOT NULL AND {Col("RemovedOn")} < {P}Before) " +
				$"OR ({Col("RemovedOn")} IS NULL AND {Col("ExpiresOn")} IS NOT NULL AND {Col("ExpiresOn")} < {P}Before)",
				new { Before = DatabaseTimestamp(before) }, cancellationToken);
		}
	}

	/// <summary>Voice intake sessions (M0323), keyed by the carrier's call id.</summary>
	public class VoiceIntakeSessionsRepository : RmsRepositoryBase<VoiceIntakeSession>, IVoiceIntakeSessionsRepository
	{
		private const string Table = "VoiceIntakeSessions";

		private static readonly string[] InsertColumns =
			{ "VoiceIntakeSessionId", "DepartmentId", "ProviderCallId", "FromNumber", "ToNumber", "Step", "AnswersJson", "Retries", "CallId", "StartedOn", "CompletedOn" };

		public VoiceIntakeSessionsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<VoiceIntakeSession> GetByProviderCallIdAsync(string providerCallId)
		{
			return QueryFirstOrDefaultAsync<VoiceIntakeSession>(
				$"SELECT * FROM {Tbl(Table)} WHERE {Col("ProviderCallId")} = {CaseInsensitive("CallSid")}",
				new { CallSid = providerCallId });
		}

		public async Task<bool> TryInsertAsync(VoiceIntakeSession session, CancellationToken cancellationToken = default)
		{
			if (session == null)
				throw new ArgumentNullException(nameof(session));

			Utf8WriteGuard.Sanitize(session);
			var values = string.Join(", ", Array.ConvertAll(InsertColumns, c => P + c));
			var sql = IsPostgres
				? $"INSERT INTO {Tbl(Table)} ({Cols(InsertColumns)}) VALUES ({values}) ON CONFLICT ({Col("ProviderCallId")}) DO NOTHING"
				: $"INSERT INTO {Tbl(Table)} ({Cols(InsertColumns)}) SELECT {values} " +
				  $"WHERE NOT EXISTS (SELECT 1 FROM {Tbl(Table)} WITH (UPDLOCK, HOLDLOCK) WHERE {Col("ProviderCallId")} = {P}ProviderCallId)";

			var inserted = await ExecuteAsync(sql, new
			{
				session.VoiceIntakeSessionId,
				session.DepartmentId,
				session.ProviderCallId,
				session.FromNumber,
				session.ToNumber,
				session.Step,
				session.AnswersJson,
				session.Retries,
				session.CallId,
				StartedOn = DatabaseTimestamp(session.StartedOn),
				CompletedOn = session.CompletedOn.HasValue ? DatabaseTimestamp(session.CompletedOn.Value) : (DateTime?)null
			}, cancellationToken);

			return inserted == 1;
		}

		public async Task<bool> UpdateProgressAsync(VoiceIntakeSession session, int expectedStep, int expectedRetries, CancellationToken cancellationToken = default)
		{
			if (session == null)
				throw new ArgumentNullException(nameof(session));

			// Conditional on the state the caller read, so an overlapping callback's older snapshot never rewinds the session or clears a
			// completed one's call and completion.
			Utf8WriteGuard.Sanitize(session);
			var updated = await ExecuteAsync(
				$"UPDATE {Tbl(Table)} SET {Col("Step")} = {P}Step, {Col("AnswersJson")} = {P}AnswersJson, {Col("Retries")} = {P}Retries, " +
				$"{Col("CallId")} = {P}CallId, {Col("CompletedOn")} = {P}CompletedOn WHERE {Col("VoiceIntakeSessionId")} = {P}Id " +
				$"AND {Col("Step")} = {P}ExpectedStep AND {Col("Retries")} = {P}ExpectedRetries AND {Col("CompletedOn")} IS NULL",
				new
				{
					Id = session.VoiceIntakeSessionId,
					session.Step,
					session.AnswersJson,
					session.Retries,
					session.CallId,
					CompletedOn = session.CompletedOn.HasValue ? DatabaseTimestamp(session.CompletedOn.Value) : (DateTime?)null,
					ExpectedStep = expectedStep,
					ExpectedRetries = expectedRetries
				},
				cancellationToken);

			return updated == 1;
		}

		public async Task<int> CountStartedAsync(int departmentId, DateTime from, DateTime before)
		{
			return await ScalarAsync<int>(
				$"SELECT COUNT(*) FROM {Tbl(Table)} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("StartedOn")} >= {P}FromOn AND {Col("StartedOn")} < {P}BeforeOn",
				new { DepartmentId = departmentId, FromOn = DatabaseTimestamp(from), BeforeOn = DatabaseTimestamp(before) });
		}

		public async Task<int> PurgeStartedBeforeAsync(DateTime before, bool keepCarrierCleanup, CancellationToken cancellationToken = default)
		{
			var parameters = new { Before = DatabaseTimestamp(before) };
			if (!keepCarrierCleanup)
				return await ExecuteAsync($"DELETE FROM {Tbl(Table)} WHERE {Col("StartedOn")} < {P}Before", parameters, cancellationToken);

			// A recording still at the carrier keeps its session (the sid worker 80 deletes it by); its transcript expires all the same.
			var carrierCopyLeft = $"({Col("RecordingSid")} IS NOT NULL AND {Col("RecordingCarrierDeletedOn")} IS NULL)";
			await ExecuteAsync(
				$"UPDATE {Tbl(Table)} SET {Col("AnswersJson")} = NULL WHERE {Col("StartedOn")} < {P}Before AND {carrierCopyLeft} AND {Col("AnswersJson")} IS NOT NULL",
				parameters, cancellationToken);
			return await ExecuteAsync($"DELETE FROM {Tbl(Table)} WHERE {Col("StartedOn")} < {P}Before AND NOT {carrierCopyLeft}", parameters, cancellationToken);
		}

		/// <summary>
		/// An attach lease stamp, whole seconds: the lease is compared for equality, and a sub-second DateTime sent as a SQL Server datetime
		/// parameter is rounded to 1/300 s on the way in, then compared with the datetime2 column at full precision (never equal).
		/// </summary>
		private static DateTime LeaseStamp(DateTime value) => DatabaseTimestamp(new DateTime(value.Ticks - value.Ticks % TimeSpan.TicksPerSecond, value.Kind));

		public async Task<bool> TryBeginRecordingAsync(string providerCallId, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(providerCallId))
				return false;

			var updated = await ExecuteAsync(
				$"UPDATE {Tbl(Table)} SET {Col("RecordingState")} = {P}Requested WHERE {Col("ProviderCallId")} = {CaseInsensitive("CallSid")} " +
				$"AND {Col("RecordingState")} = {P}None",
				new { CallSid = providerCallId.Trim(), Requested = (int)VoiceIntakeRecordingStates.Requested, None = (int)VoiceIntakeRecordingStates.None },
				cancellationToken);

			return updated == 1;
		}

		public async Task<bool> SetRecordingSidAsync(string providerCallId, string recordingSid, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(providerCallId) || string.IsNullOrWhiteSpace(recordingSid))
				return false;

			var updated = await ExecuteAsync(
				$"UPDATE {Tbl(Table)} SET {Col("RecordingSid")} = {P}RecordingSid WHERE {Col("ProviderCallId")} = {CaseInsensitive("CallSid")} AND {Col("RecordingSid")} IS NULL",
				new { CallSid = providerCallId.Trim(), RecordingSid = recordingSid.Trim() },
				cancellationToken);

			return updated == 1;
		}

		public async Task<bool> CompleteRecordingAsync(string providerCallId, string recordingSid, int? durationSeconds, int state, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(providerCallId))
				return false;

			// The sid the callback names wins over none; a stored one (from the start answer) is kept.
			var updated = await ExecuteAsync(
				$"UPDATE {Tbl(Table)} SET {Col("RecordingState")} = {P}State, {Col("RecordingDurationSeconds")} = {P}Duration, " +
				$"{Col("RecordingSid")} = COALESCE({Col("RecordingSid")}, {P}RecordingSid) " +
				$"WHERE {Col("ProviderCallId")} = {CaseInsensitive("CallSid")} AND {Col("RecordingState")} = {P}Requested",
				new
				{
					CallSid = providerCallId.Trim(),
					RecordingSid = string.IsNullOrWhiteSpace(recordingSid) ? null : recordingSid.Trim(),
					Duration = durationSeconds,
					State = state,
					Requested = (int)VoiceIntakeRecordingStates.Requested
				},
				cancellationToken);

			return updated == 1;
		}

		public async Task<bool> TryClaimRecordingAsync(string voiceIntakeSessionId, int expectedState, DateTime claimedOn, DateTime staleBefore,
			CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(voiceIntakeSessionId))
				return false;

			var updated = await ExecuteAsync(
				$"UPDATE {Tbl(Table)} SET {Col("RecordingClaimedOn")} = {P}ClaimedOn, {Col("RecordingAttempts")} = {Col("RecordingAttempts")} + 1 " +
				$"WHERE {Col("VoiceIntakeSessionId")} = {P}Id AND {Col("RecordingState")} = {P}Expected " +
				$"AND ({Col("RecordingClaimedOn")} IS NULL OR {Col("RecordingClaimedOn")} < {P}StaleBefore)",
				new { Id = voiceIntakeSessionId, Expected = expectedState, ClaimedOn = LeaseStamp(claimedOn), StaleBefore = DatabaseTimestamp(staleBefore) },
				cancellationToken);

			return updated == 1;
		}

		public async Task<bool> SetRecordingStateAsync(string voiceIntakeSessionId, int state, int? callAttachmentId, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(voiceIntakeSessionId))
				return false;

			var updated = await ExecuteAsync(
				$"UPDATE {Tbl(Table)} SET {Col("RecordingState")} = {P}State, {Col("RecordingClaimedOn")} = NULL, " +
				$"{Col("RecordingCallAttachmentId")} = COALESCE({P}AttachmentId, {Col("RecordingCallAttachmentId")}) WHERE {Col("VoiceIntakeSessionId")} = {P}Id",
				new { Id = voiceIntakeSessionId, State = state, AttachmentId = callAttachmentId },
				cancellationToken);

			return updated == 1;
		}

		public async Task<bool> SetClaimedRecordingStateAsync(string voiceIntakeSessionId, DateTime claimedOn, int state, int? callAttachmentId,
			CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(voiceIntakeSessionId))
				return false;

			var updated = await ExecuteAsync(
				$"UPDATE {Tbl(Table)} SET {Col("RecordingState")} = {P}State, {Col("RecordingClaimedOn")} = NULL, " +
				$"{Col("RecordingCallAttachmentId")} = COALESCE({P}AttachmentId, {Col("RecordingCallAttachmentId")}) " +
				$"WHERE {Col("VoiceIntakeSessionId")} = {P}Id AND {Col("RecordingClaimedOn")} = {P}ClaimedOn",
				new { Id = voiceIntakeSessionId, State = state, AttachmentId = callAttachmentId, ClaimedOn = LeaseStamp(claimedOn) },
				cancellationToken);

			return updated == 1;
		}

		public async Task<bool> RenewRecordingClaimAsync(string voiceIntakeSessionId, DateTime claimedOn, DateTime renewedOn, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(voiceIntakeSessionId))
				return false;

			var updated = await ExecuteAsync(
				$"UPDATE {Tbl(Table)} SET {Col("RecordingClaimedOn")} = {P}RenewedOn " +
				$"WHERE {Col("VoiceIntakeSessionId")} = {P}Id AND {Col("RecordingClaimedOn")} = {P}ClaimedOn AND {Col("RecordingState")} = {P}Completed",
				new
				{
					Id = voiceIntakeSessionId,
					ClaimedOn = LeaseStamp(claimedOn),
					RenewedOn = LeaseStamp(renewedOn),
					Completed = (int)VoiceIntakeRecordingStates.Completed
				},
				cancellationToken);

			return updated == 1;
		}

		public async Task<bool> TryDiscardUnconfirmedRecordingAsync(string voiceIntakeSessionId, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(voiceIntakeSessionId))
				return false;

			var updated = await ExecuteAsync(
				$"UPDATE {Tbl(Table)} SET {Col("RecordingState")} = {P}Discarded, {Col("RecordingClaimedOn")} = NULL " +
				$"WHERE {Col("VoiceIntakeSessionId")} = {P}Id AND {Col("RecordingState")} = {P}Requested AND {Col("RecordingSid")} IS NULL",
				new { Id = voiceIntakeSessionId, Discarded = (int)VoiceIntakeRecordingStates.Discarded, Requested = (int)VoiceIntakeRecordingStates.Requested },
				cancellationToken);

			return updated == 1;
		}

		public async Task<bool> TryDiscardWithoutCallAsync(string voiceIntakeSessionId, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(voiceIntakeSessionId))
				return false;

			var updated = await ExecuteAsync(
				$"UPDATE {Tbl(Table)} SET {Col("RecordingState")} = {P}Discarded, {Col("RecordingClaimedOn")} = NULL " +
				$"WHERE {Col("VoiceIntakeSessionId")} = {P}Id AND {Col("RecordingState")} = {P}Completed AND {Col("CallId")} IS NULL",
				new { Id = voiceIntakeSessionId, Discarded = (int)VoiceIntakeRecordingStates.Discarded, Completed = (int)VoiceIntakeRecordingStates.Completed },
				cancellationToken);

			return updated == 1;
		}

		public async Task<bool> MarkRecordingCarrierDeletedAsync(string voiceIntakeSessionId, DateTime deletedOn, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(voiceIntakeSessionId))
				return false;

			var updated = await ExecuteAsync(
				$"UPDATE {Tbl(Table)} SET {Col("RecordingCarrierDeletedOn")} = {P}DeletedOn WHERE {Col("VoiceIntakeSessionId")} = {P}Id AND {Col("RecordingCarrierDeletedOn")} IS NULL",
				new { Id = voiceIntakeSessionId, DeletedOn = DatabaseTimestamp(deletedOn) },
				cancellationToken);

			return updated == 1;
		}

		public async Task<List<VoiceIntakeSession>> GetRecordingWorkAsync(DateTime purgeBefore, bool includeCarrierCopies, int max, CancellationToken cancellationToken = default)
		{
			var take = Math.Clamp(max, 1, 1000);
			var top = IsPostgres ? string.Empty : $"TOP ({take}) ";
			var limit = IsPostgres ? $" LIMIT {take}" : string.Empty;
			var carrierCopies = includeCarrierCopies
				? $" OR ({Col("RecordingSid")} IS NOT NULL AND {Col("RecordingCarrierDeletedOn")} IS NULL AND " +
				  $"({Col("RecordingState")} IN ({P}Attached, {P}Discarded) OR ({Col("RecordingState")} = {P}Failed AND {Col("StartedOn")} < {P}PurgeBefore)))"
				: string.Empty;

			// Recording columns plus what the attach needs; never the answers (transcripts). Recordings still to attach come first: carrier
			// copies whose deletes keep failing are kept (the purge holds their sessions) and must never fill every batch ahead of them.
			var attachFirst = $"CASE WHEN {Col("RecordingState")} IN ({(int)VoiceIntakeRecordingStates.Requested}, {(int)VoiceIntakeRecordingStates.Completed}) THEN 0 ELSE 1 END";
			var rows = await QueryAsync<VoiceIntakeSession>(
				$"SELECT {top}{Cols("VoiceIntakeSessionId", "DepartmentId", "ProviderCallId", "FromNumber", "ToNumber", "Step", "CallId", "StartedOn", "CompletedOn", "RecordingState", "RecordingSid", "RecordingDurationSeconds", "RecordingAttempts", "RecordingClaimedOn", "RecordingCallAttachmentId", "RecordingCarrierDeletedOn")} " +
				$"FROM {Tbl(Table)} WHERE {Col("RecordingState")} IN ({P}Requested, {P}Completed){carrierCopies} " +
				$"ORDER BY {attachFirst}, {Col("StartedOn")}{limit}",
				new
				{
					Requested = (int)VoiceIntakeRecordingStates.Requested,
					Completed = (int)VoiceIntakeRecordingStates.Completed,
					Attached = (int)VoiceIntakeRecordingStates.Attached,
					Discarded = (int)VoiceIntakeRecordingStates.Discarded,
					Failed = (int)VoiceIntakeRecordingStates.Failed,
					PurgeBefore = DatabaseTimestamp(purgeBefore)
				},
				cancellationToken);

			return rows?.ToList() ?? new List<VoiceIntakeSession>();
		}
	}
}
