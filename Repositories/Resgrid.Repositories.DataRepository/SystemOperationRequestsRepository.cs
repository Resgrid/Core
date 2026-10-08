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
	/// <summary>System operation requests (M0267).</summary>
	public class SystemOperationRequestsRepository : RmsRepositoryBase<SystemOperationRequest>, ISystemOperationRequestsRepository
	{
		private const string Table = "SystemOperationRequests";
		private const int ClaimAttempts = 5;

		public SystemOperationRequestsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<List<SystemOperationRequest>> GetRecentAsync(int take)
		{
			var sql = IsPostgres
				? $"SELECT * FROM {Tbl(Table)} ORDER BY {Col("RequestedOn")} DESC LIMIT {P}Take"
				: $"SELECT TOP ({P}Take) * FROM {Tbl(Table)} ORDER BY {Col("RequestedOn")} DESC";

			var rows = await QueryAsync<SystemOperationRequest>(sql, new { Take = Math.Max(1, take) });

			return rows?.ToList() ?? new List<SystemOperationRequest>();
		}

		public Task<SystemOperationRequest> GetPendingAsync(int operationType, int? targetDepartmentId)
		{
			// A null parameter compared with IS NULL has no type for Npgsql to bind, so the target filter is chosen here.
			var target = targetDepartmentId.HasValue
				? $"{Col("TargetDepartmentId")} = {P}TargetDepartmentId"
				: $"{Col("TargetDepartmentId")} IS NULL";

			return QueryFirstOrDefaultAsync<SystemOperationRequest>(
				$"SELECT * FROM {Tbl(Table)} WHERE {Col("OperationType")} = {P}OperationType AND {Col("Status")} = {P}Pending AND {target} " +
				$"ORDER BY {Col("RequestedOn")}",
				new { OperationType = operationType, Pending = (int)SystemOperationStatuses.Pending, TargetDepartmentId = targetDepartmentId ?? 0 });
		}

		public async Task<SystemOperationRequest> ClaimNextPendingAsync(string workerName, DateTime now, CancellationToken cancellationToken = default)
		{
			var oldestPending = IsPostgres
				? $"SELECT {Col("SystemOperationRequestId")} FROM {Tbl(Table)} WHERE {Col("Status")} = {P}Pending ORDER BY {Col("RequestedOn")}, {Col("SystemOperationRequestId")} LIMIT 1"
				: $"SELECT TOP 1 {Col("SystemOperationRequestId")} FROM {Tbl(Table)} WHERE {Col("Status")} = {P}Pending ORDER BY {Col("RequestedOn")}, {Col("SystemOperationRequestId")}";

			// Another worker can claim the candidate between the read and the conditional update; then take the next one.
			for (var attempt = 0; attempt < ClaimAttempts; attempt++)
			{
				var candidateId = await QueryFirstOrDefaultAsync<string>(oldestPending, new { Pending = (int)SystemOperationStatuses.Pending }, cancellationToken);

				if (string.IsNullOrWhiteSpace(candidateId))
					return null;

				var claimed = await ExecuteAsync(
					$"UPDATE {Tbl(Table)} SET {Col("Status")} = {P}Running, {Col("StartedOn")} = {P}Now, {Col("HeartbeatOn")} = {P}Now, " +
					$"{Col("WorkerName")} = {P}WorkerName, {Col("Progress")} = NULL " +
					$"WHERE {Col("SystemOperationRequestId")} = {P}Id AND {Col("Status")} = {P}Pending",
					new
					{
						Id = candidateId,
						Running = (int)SystemOperationStatuses.Running,
						Pending = (int)SystemOperationStatuses.Pending,
						Now = DatabaseTimestamp(now),
						WorkerName = Truncate(workerName, 256)
					},
					cancellationToken);

				if (claimed == 1)
					return await QueryFirstOrDefaultAsync<SystemOperationRequest>(
						$"SELECT * FROM {Tbl(Table)} WHERE {Col("SystemOperationRequestId")} = {P}Id",
						new { Id = candidateId },
						cancellationToken);
			}

			return null;
		}

		public async Task<bool> HeartbeatAsync(string requestId, string progress, DateTime now, CancellationToken cancellationToken = default)
		{
			var setProgress = progress == null ? "" : $", {Col("Progress")} = {P}Progress";

			var updated = await ExecuteAsync(
				$"UPDATE {Tbl(Table)} SET {Col("HeartbeatOn")} = {P}Now{setProgress} " +
				$"WHERE {Col("SystemOperationRequestId")} = {P}Id AND {Col("Status")} = {P}Running",
				new
				{
					Id = requestId,
					Now = DatabaseTimestamp(now),
					Progress = Truncate(progress, SystemOperationRequest.ProgressMaxLength),
					Running = (int)SystemOperationStatuses.Running
				},
				cancellationToken);

			return updated == 1;
		}

		public async Task<bool> FinishAsync(string requestId, int status, string result, DateTime now, CancellationToken cancellationToken = default)
		{
			var updated = await ExecuteAsync(
				$"UPDATE {Tbl(Table)} SET {Col("Status")} = {P}Status, {Col("CompletedOn")} = {P}Now, {Col("HeartbeatOn")} = {P}Now, {Col("Result")} = {P}Result " +
				$"WHERE {Col("SystemOperationRequestId")} = {P}Id AND {Col("Status")} = {P}Running",
				new
				{
					Id = requestId,
					Status = status,
					Now = DatabaseTimestamp(now),
					Result = Truncate(result, SystemOperationRequest.ResultMaxLength),
					Running = (int)SystemOperationStatuses.Running
				},
				cancellationToken);

			return updated == 1;
		}

		public async Task<bool> CancelPendingAsync(string requestId, string cancelledBy, DateTime now, CancellationToken cancellationToken = default)
		{
			var updated = await ExecuteAsync(
				$"UPDATE {Tbl(Table)} SET {Col("Status")} = {P}Cancelled, {Col("CompletedOn")} = {P}Now, {Col("CancelledBy")} = {P}CancelledBy " +
				$"WHERE {Col("SystemOperationRequestId")} = {P}Id AND {Col("Status")} = {P}Pending",
				new
				{
					Id = requestId,
					Cancelled = (int)SystemOperationStatuses.Cancelled,
					Now = DatabaseTimestamp(now),
					CancelledBy = Truncate(cancelledBy, 256),
					Pending = (int)SystemOperationStatuses.Pending
				},
				cancellationToken);

			return updated == 1;
		}

		public Task<int> FailAbandonedAsync(DateTime heartbeatBefore, string result, DateTime now, CancellationToken cancellationToken = default)
		{
			return ExecuteAsync(
				$"UPDATE {Tbl(Table)} SET {Col("Status")} = {P}Failed, {Col("CompletedOn")} = {P}Now, {Col("Result")} = {P}Result " +
				$"WHERE {Col("Status")} = {P}Running AND COALESCE({Col("HeartbeatOn")}, {Col("StartedOn")}, {Col("RequestedOn")}) < {P}Cutoff",
				new
				{
					Failed = (int)SystemOperationStatuses.Failed,
					Now = DatabaseTimestamp(now),
					Result = Truncate(result, SystemOperationRequest.ResultMaxLength),
					Running = (int)SystemOperationStatuses.Running,
					Cutoff = DatabaseTimestamp(heartbeatBefore)
				},
				cancellationToken);
		}

		private static string Truncate(string value, int length) =>
			string.IsNullOrEmpty(value) || value.Length <= length ? value : value.Substring(0, length);
	}
}
