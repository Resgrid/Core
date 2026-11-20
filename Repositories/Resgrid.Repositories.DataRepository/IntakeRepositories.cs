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

		public async Task<bool> TryReclaimAsync(string intakeMessageId, int expectedState, CancellationToken cancellationToken = default)
		{
			var updated = await ExecuteAsync(
				$"UPDATE {Tbl(Table)} SET {Col("State")} = {P}Received, {Col("Reason")} = {P}Reason WHERE {Col("IntakeMessageId")} = {P}Id " +
				$"AND {Col("State")} = {P}Expected AND {Col("CallId")} IS NULL",
				new { Id = intakeMessageId, Expected = expectedState, Received = (int)IntakeMessageStates.Received, Reason = "Retried" },
				cancellationToken);

			return updated == 1;
		}

		public async Task<bool> CompleteAsync(string intakeMessageId, int state, int? callId, string reason, DateTime completedOn, CancellationToken cancellationToken = default)
		{
			var updated = await ExecuteAsync(
				$"UPDATE {Tbl(Table)} SET {Col("State")} = {P}State, {Col("CallId")} = {P}CallId, {Col("Reason")} = {P}Reason, {Col("CompletedOn")} = {P}CompletedOn " +
				$"WHERE {Col("IntakeMessageId")} = {P}Id",
				new { Id = intakeMessageId, State = state, CallId = callId, Reason = reason, CompletedOn = DatabaseTimestamp(completedOn) },
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

		public async Task<bool> UpdateProgressAsync(VoiceIntakeSession session, CancellationToken cancellationToken = default)
		{
			if (session == null)
				throw new ArgumentNullException(nameof(session));

			Utf8WriteGuard.Sanitize(session);
			var updated = await ExecuteAsync(
				$"UPDATE {Tbl(Table)} SET {Col("Step")} = {P}Step, {Col("AnswersJson")} = {P}AnswersJson, {Col("Retries")} = {P}Retries, " +
				$"{Col("CallId")} = {P}CallId, {Col("CompletedOn")} = {P}CompletedOn WHERE {Col("VoiceIntakeSessionId")} = {P}Id",
				new
				{
					Id = session.VoiceIntakeSessionId,
					session.Step,
					session.AnswersJson,
					session.Retries,
					session.CallId,
					CompletedOn = session.CompletedOn.HasValue ? DatabaseTimestamp(session.CompletedOn.Value) : (DateTime?)null
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

		public Task<int> PurgeStartedBeforeAsync(DateTime before, CancellationToken cancellationToken = default)
		{
			return ExecuteAsync($"DELETE FROM {Tbl(Table)} WHERE {Col("StartedOn")} < {P}Before", new { Before = DatabaseTimestamp(before) }, cancellationToken);
		}
	}
}
