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
	/// <summary>Pages owed to resources added to live calls (M0321). Every state change is conditional on the row's state and claim.</summary>
	public class CallBroadcastOutboxRepository : RmsRepositoryBase<CallBroadcastOutboxEntry>, ICallBroadcastOutboxRepository
	{
		private const int Pending = (int)CallBroadcastOutboxStates.Pending;
		private const int Sending = (int)CallBroadcastOutboxStates.Sending;

		public CallBroadcastOutboxRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task InsertEntryAsync(CallBroadcastOutboxEntry entry, CancellationToken cancellationToken = default)
		{
			if (entry == null)
				throw new ArgumentNullException(nameof(entry));

			if (string.IsNullOrWhiteSpace(entry.CallBroadcastOutboxId))
				entry.CallBroadcastOutboxId = Guid.NewGuid().ToString();

			var columns = Cols("CallBroadcastOutboxId", "DepartmentId", "CallId", "UserIds", "GroupIds", "UnitIds", "RoleIds", "State", "Attempts",
				"NextAttemptOn", "CreatedByUserId", "CreatedOn");
			var values = $"{P}Id, {P}DepartmentId, {P}CallId, {P}UserIds, {P}GroupIds, {P}UnitIds, {P}RoleIds, {P}State, {P}Attempts, {P}NextAttemptOn, " +
				$"{P}CreatedByUserId, {P}CreatedOn";

			var parameters = new DynamicParameters(new
			{
				Id = entry.CallBroadcastOutboxId,
				entry.DepartmentId,
				entry.CallId,
				entry.UserIds,
				entry.GroupIds,
				entry.UnitIds,
				entry.RoleIds,
				entry.State,
				entry.Attempts,
				entry.CreatedByUserId
			});
			RecurringCallsRepository.AddTimestamp(parameters, "NextAttemptOn", entry.NextAttemptOn);
			RecurringCallsRepository.AddTimestamp(parameters, "CreatedOn", entry.CreatedOn);

			await ExecuteAsync($"INSERT INTO {Tbl("CallBroadcastOutbox")} ({columns}) VALUES ({values})", parameters, cancellationToken);
		}

		public async Task<CallBroadcastOutboxEntry> GetEntryAsync(string callBroadcastOutboxId, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(callBroadcastOutboxId))
				return null;

			return await QueryFirstOrDefaultAsync<CallBroadcastOutboxEntry>(
				$"SELECT * FROM {Tbl("CallBroadcastOutbox")} WHERE {Col("CallBroadcastOutboxId")} = {CaseInsensitive("Id")}",
				new { Id = callBroadcastOutboxId }, cancellationToken);
		}

		public async Task<IEnumerable<string>> GetOpenIdsForCallAsync(int callId, DateTime nowUtc, CancellationToken cancellationToken = default)
		{
			var parameters = new DynamicParameters(new { CallId = callId, Pending, Sending });
			RecurringCallsRepository.AddTimestamp(parameters, "Now", nowUtc);

			return await QueryAsync<string>(
				$"SELECT {Col("CallBroadcastOutboxId")} FROM {Tbl("CallBroadcastOutbox")} WHERE {Col("CallId")} = {P}CallId AND {Open()} " +
				$"ORDER BY {Col("CreatedOn")}", parameters, cancellationToken);
		}

		public async Task<IEnumerable<string>> GetDueIdsAsync(DateTime nowUtc, int take, CancellationToken cancellationToken = default)
		{
			var parameters = new DynamicParameters(new { Pending, Sending, Take = Math.Max(1, take) });
			RecurringCallsRepository.AddTimestamp(parameters, "Now", nowUtc);

			var due = $"(({Col("State")} = {P}Pending AND {Col("NextAttemptOn")} <= {P}Now) OR ({Col("State")} = {P}Sending AND {Col("LeaseExpiresOn")} < {P}Now))";
			var sql = IsPostgres
				? $"SELECT {Col("CallBroadcastOutboxId")} FROM {Tbl("CallBroadcastOutbox")} WHERE {due} ORDER BY {Col("NextAttemptOn")} LIMIT {P}Take"
				: $"SELECT TOP ({P}Take) {Col("CallBroadcastOutboxId")} FROM {Tbl("CallBroadcastOutbox")} WHERE {due} ORDER BY {Col("NextAttemptOn")}";

			return await QueryAsync<string>(sql, parameters, cancellationToken);
		}

		public async Task<bool> TryClaimAsync(string callBroadcastOutboxId, string claimToken, DateTime nowUtc, DateTime leaseExpiresOnUtc, bool ignoreSchedule,
			CancellationToken cancellationToken = default)
		{
			var parameters = new DynamicParameters(new { Id = callBroadcastOutboxId, Token = claimToken, Pending, Sending });
			RecurringCallsRepository.AddTimestamp(parameters, "Now", nowUtc);
			RecurringCallsRepository.AddTimestamp(parameters, "Lease", leaseExpiresOnUtc);

			// Pending (due, unless the edit itself is sending it) or claimed by a sender whose lease ran out (its process stopped).
			var claimable = ignoreSchedule
				? Open()
				: $"(({Col("State")} = {P}Pending AND {Col("NextAttemptOn")} <= {P}Now) OR ({Col("State")} = {P}Sending AND {Col("LeaseExpiresOn")} < {P}Now))";

			var rows = await ExecuteAsync(
				$"UPDATE {Tbl("CallBroadcastOutbox")} SET {Col("State")} = {P}Sending, {Col("ClaimToken")} = {P}Token, {Col("LeaseExpiresOn")} = {P}Lease, " +
				$"{Col("Attempts")} = {Col("Attempts")} + 1 WHERE {Col("CallBroadcastOutboxId")} = {CaseInsensitive("Id")} AND {claimable}",
				parameters, cancellationToken);

			return rows == 1;
		}

		public async Task<bool> CompleteAsync(string callBroadcastOutboxId, string claimToken, CallBroadcastOutboxStates state, DateTime nowUtc, string error,
			CancellationToken cancellationToken = default)
		{
			var parameters = new DynamicParameters(new { Id = callBroadcastOutboxId, Token = claimToken, State = (int)state, Sending, Error = Trim(error) });
			RecurringCallsRepository.AddTimestamp(parameters, "Now", nowUtc);

			var rows = await ExecuteAsync(
				$"UPDATE {Tbl("CallBroadcastOutbox")} SET {Col("State")} = {P}State, {Col("CompletedOn")} = {P}Now, {Col("LastError")} = {P}Error, " +
				$"{Col("LeaseExpiresOn")} = NULL WHERE {Col("CallBroadcastOutboxId")} = {CaseInsensitive("Id")} AND {Col("State")} = {P}Sending " +
				$"AND {Col("ClaimToken")} = {CaseInsensitive("Token")}",
				parameters, cancellationToken);

			return rows == 1;
		}

		public async Task<bool> RetryLaterAsync(string callBroadcastOutboxId, string claimToken, DateTime nextAttemptOnUtc, string error,
			CancellationToken cancellationToken = default)
		{
			var parameters = new DynamicParameters(new { Id = callBroadcastOutboxId, Token = claimToken, Pending, Sending, Error = Trim(error) });
			RecurringCallsRepository.AddTimestamp(parameters, "Next", nextAttemptOnUtc);

			var rows = await ExecuteAsync(
				$"UPDATE {Tbl("CallBroadcastOutbox")} SET {Col("State")} = {P}Pending, {Col("NextAttemptOn")} = {P}Next, {Col("LastError")} = {P}Error, " +
				$"{Col("ClaimToken")} = NULL, {Col("LeaseExpiresOn")} = NULL WHERE {Col("CallBroadcastOutboxId")} = {CaseInsensitive("Id")} " +
				$"AND {Col("State")} = {P}Sending AND {Col("ClaimToken")} = {CaseInsensitive("Token")}",
				parameters, cancellationToken);

			return rows == 1;
		}

		public async Task<int> SupersedePendingForCallAsync(int callId, DateTime nowUtc, CancellationToken cancellationToken = default)
		{
			var parameters = new DynamicParameters(new { CallId = callId, Pending, Superseded = (int)CallBroadcastOutboxStates.Superseded });
			RecurringCallsRepository.AddTimestamp(parameters, "Now", nowUtc);

			return await ExecuteAsync(
				$"UPDATE {Tbl("CallBroadcastOutbox")} SET {Col("State")} = {P}Superseded, {Col("CompletedOn")} = {P}Now " +
				$"WHERE {Col("CallId")} = {P}CallId AND {Col("State")} = {P}Pending",
				parameters, cancellationToken);
		}

		public async Task<int> PurgeCompletedBeforeAsync(DateTime beforeUtc, CancellationToken cancellationToken = default)
		{
			var parameters = new DynamicParameters();
			RecurringCallsRepository.AddTimestamp(parameters, "Before", beforeUtc);

			return await ExecuteAsync(
				$"DELETE FROM {Tbl("CallBroadcastOutbox")} WHERE {Col("CompletedOn")} IS NOT NULL AND {Col("CompletedOn")} < {P}Before",
				parameters, cancellationToken);
		}

		/// <summary>Not sent yet: pending, or claimed by a sender whose lease ran out.</summary>
		private string Open()
		{
			return $"({Col("State")} = {P}Pending OR ({Col("State")} = {P}Sending AND {Col("LeaseExpiresOn")} < {P}Now))";
		}

		private static string Trim(string error)
		{
			return string.IsNullOrEmpty(error) || error.Length <= 1000 ? error : error.Substring(0, 1000);
		}
	}
}
