using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository
{
	/// <summary>
	/// Dispatch re-notification rows (M0311). Every write that advances a row is conditional on it still being pending (and, for
	/// an attempt, on its attempt count), so two overlapping sweeps can never send the same re-notification twice.
	/// </summary>
	public class DispatchRenotificationsRepository : RmsRepositoryBase<DispatchRenotification>, IDispatchRenotificationsRepository
	{
		private const int Pending = (int)DispatchRenotifyStatuses.Pending;
		private const int Acknowledged = (int)DispatchRenotifyStatuses.Acknowledged;
		private const int Exhausted = (int)DispatchRenotifyStatuses.Exhausted;
		private const int Cancelled = (int)DispatchRenotifyStatuses.Cancelled;

		public DispatchRenotificationsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		private string Table => Tbl("DispatchRenotifications");

		private static DateTime? NullableTimestamp(DateTime? value) => value.HasValue ? DatabaseTimestamp(value.Value) : (DateTime?)null;

		private static readonly string[] Columns =
		{
			"DispatchRenotificationId", "DepartmentId", "CallId", "RecipientType", "RecipientId", "SourceType", "SourceId", "Status",
			"Attempts", "AcknowledgeAfter", "NextNotifyOn", "LastNotifiedOn", "AcknowledgedOn", "CompletedOn", "CreatedOn"
		};

		public async Task RegisterAsync(IEnumerable<DispatchRenotification> rows, CancellationToken cancellationToken = default)
		{
			var parameters = (rows ?? Enumerable.Empty<DispatchRenotification>()).Where(r => r != null).Select(r => new
			{
				DispatchRenotificationId = string.IsNullOrWhiteSpace(r.DispatchRenotificationId) ? Guid.NewGuid().ToString() : r.DispatchRenotificationId,
				r.DepartmentId,
				r.CallId,
				r.RecipientType,
				r.RecipientId,
				r.SourceType,
				r.SourceId,
				r.Status,
				r.Attempts,
				AcknowledgeAfter = DatabaseTimestamp(r.AcknowledgeAfter),
				NextNotifyOn = NullableTimestamp(r.NextNotifyOn),
				CreatedOn = DatabaseTimestamp(r.CreatedOn)
			}).ToList();

			if (parameters.Count == 0)
				return;

			string sql;
			if (IsPostgres)
			{
				sql = $@"INSERT INTO {Table} AS t (dispatchrenotificationid, departmentid, callid, recipienttype, recipientid, sourcetype, sourceid,
						status, attempts, acknowledgeafter, nextnotifyon, createdon)
					VALUES ({P}DispatchRenotificationId, {P}DepartmentId, {P}CallId, {P}RecipientType, {P}RecipientId, {P}SourceType, {P}SourceId,
						{P}Status, {P}Attempts, {P}AcknowledgeAfter, {P}NextNotifyOn, {P}CreatedOn)
					ON CONFLICT (callid, recipienttype, recipientid) DO UPDATE SET status = EXCLUDED.status, attempts = EXCLUDED.attempts,
						nextnotifyon = EXCLUDED.nextnotifyon, sourcetype = EXCLUDED.sourcetype, sourceid = EXCLUDED.sourceid,
						lastnotifiedon = NULL, acknowledgedon = NULL, completedon = NULL
					WHERE t.status IN ({Exhausted}, {Cancelled})";
			}
			else
			{
				sql = $@"MERGE {Table} WITH (HOLDLOCK) AS t
					USING (SELECT {P}CallId AS CallId, {P}RecipientType AS RecipientType, {P}RecipientId AS RecipientId) AS s
						ON t.[CallId] = s.CallId AND t.[RecipientType] = s.RecipientType AND t.[RecipientId] = s.RecipientId
					WHEN MATCHED AND t.[Status] IN ({Exhausted}, {Cancelled}) THEN UPDATE SET [Status] = {P}Status, [Attempts] = {P}Attempts,
						[NextNotifyOn] = {P}NextNotifyOn, [SourceType] = {P}SourceType, [SourceId] = {P}SourceId,
						[LastNotifiedOn] = NULL, [AcknowledgedOn] = NULL, [CompletedOn] = NULL
					WHEN NOT MATCHED THEN INSERT ([DispatchRenotificationId], [DepartmentId], [CallId], [RecipientType], [RecipientId], [SourceType], [SourceId],
							[Status], [Attempts], [AcknowledgeAfter], [NextNotifyOn], [CreatedOn])
						VALUES ({P}DispatchRenotificationId, {P}DepartmentId, {P}CallId, {P}RecipientType, {P}RecipientId, {P}SourceType, {P}SourceId,
							{P}Status, {P}Attempts, {P}AcknowledgeAfter, {P}NextNotifyOn, {P}CreatedOn);";
			}

			await ExecuteAsync(sql, parameters, cancellationToken);
		}

		public async Task<List<DispatchRenotification>> GetDueAsync(DateTime nowUtc, int take, CancellationToken cancellationToken = default)
		{
			if (take <= 0)
				return new List<DispatchRenotification>();

			var top = IsPostgres ? string.Empty : $"TOP ({P}Take) ";
			var limit = IsPostgres ? $" LIMIT {P}Take" : string.Empty;
			var sql = $@"SELECT {top}{Cols(Columns)} FROM {Table}
				WHERE {Col("Status")} = {Pending} AND {Col("NextNotifyOn")} IS NOT NULL AND {Col("NextNotifyOn")} <= {P}Now
				ORDER BY {Col("NextNotifyOn")}{limit}";

			return (await QueryAsync<DispatchRenotification>(sql, new { Now = DatabaseTimestamp(nowUtc), Take = take }, cancellationToken)).ToList();
		}

		public async Task<bool> TryClaimAttemptAsync(string dispatchRenotificationId, int expectedAttempts, int newAttempts, DateTime? nextNotifyOn, DateTime nowUtc,
			CancellationToken cancellationToken = default)
		{
			var finished = !nextNotifyOn.HasValue;
			var sql = $@"UPDATE {Table} SET {Col("Attempts")} = {P}NewAttempts, {Col("LastNotifiedOn")} = {P}Now, {Col("NextNotifyOn")} = {P}NextNotifyOn,
					{Col("Status")} = {P}Status, {Col("CompletedOn")} = {P}CompletedOn
				WHERE {Col("DispatchRenotificationId")} = {CaseInsensitive("Id")} AND {Col("Status")} = {Pending} AND {Col("Attempts")} = {P}ExpectedAttempts";

			var changed = await ExecuteAsync(sql, new
			{
				Id = dispatchRenotificationId,
				NewAttempts = newAttempts,
				ExpectedAttempts = expectedAttempts,
				Now = DatabaseTimestamp(nowUtc),
				NextNotifyOn = NullableTimestamp(nextNotifyOn),
				Status = finished ? Exhausted : Pending,
				CompletedOn = finished ? DatabaseTimestamp(nowUtc) : (DateTime?)null
			}, cancellationToken);

			return changed == 1;
		}

		public async Task<bool> MarkAcknowledgedAsync(string dispatchRenotificationId, DateTime acknowledgedOnUtc, DateTime nowUtc, CancellationToken cancellationToken = default)
		{
			var sql = $@"UPDATE {Table} SET {Col("Status")} = {Acknowledged}, {Col("AcknowledgedOn")} = {P}AcknowledgedOn, {Col("CompletedOn")} = {P}Now,
					{Col("NextNotifyOn")} = NULL
				WHERE {Col("DispatchRenotificationId")} = {CaseInsensitive("Id")} AND {Col("Status")} = {Pending}";

			var changed = await ExecuteAsync(sql, new
			{
				Id = dispatchRenotificationId,
				AcknowledgedOn = DatabaseTimestamp(acknowledgedOnUtc),
				Now = DatabaseTimestamp(nowUtc)
			}, cancellationToken);

			return changed == 1;
		}

		public async Task<int> CancelAsync(IEnumerable<string> dispatchRenotificationIds, DateTime nowUtc, CancellationToken cancellationToken = default)
		{
			var ids = InListValue(dispatchRenotificationIds?.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct());
			if (ids.Length == 0)
				return 0;

			var sql = $@"UPDATE {Table} SET {Col("Status")} = {Cancelled}, {Col("CompletedOn")} = {P}Now, {Col("NextNotifyOn")} = NULL
				WHERE {CaseInsensitiveInList("DispatchRenotificationId", "Ids")} AND {Col("Status")} = {Pending}";

			return await ExecuteAsync(sql, new { Ids = ids, Now = DatabaseTimestamp(nowUtc) }, cancellationToken);
		}

		public async Task<int> PurgeCompletedAsync(DateTime cutoffUtc, int take, CancellationToken cancellationToken = default)
		{
			if (take <= 0)
				return 0;

			var finished = $"{Col("Status")} <> {Pending} AND {Col("CompletedOn")} IS NOT NULL AND {Col("CompletedOn")} < {P}Cutoff";
			var sql = IsPostgres
				? $@"DELETE FROM {Table} WHERE dispatchrenotificationid IN
						(SELECT dispatchrenotificationid FROM {Table} WHERE {finished} LIMIT {P}Take)"
				: $"DELETE TOP ({P}Take) FROM {Table} WHERE {finished}";

			return await ExecuteAsync(sql, new { Cutoff = DatabaseTimestamp(cutoffUtc), Take = take }, cancellationToken);
		}
	}
}
