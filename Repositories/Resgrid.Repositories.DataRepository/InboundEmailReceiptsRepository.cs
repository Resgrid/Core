using System;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository
{
	/// <summary>Inbound email receipts (M0271).</summary>
	public class InboundEmailReceiptsRepository : RmsRepositoryBase<InboundEmailReceipt>, IInboundEmailReceiptsRepository
	{
		private const string Table = "InboundEmailReceipts";

		private static readonly string[] InsertColumns =
			{ "InboundEmailReceiptId", "ProviderMessageId", "Kind", "State", "PendingSteps", "Deliveries", "ReceivedOn", "ClaimedOn" };

		public InboundEmailReceiptsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<bool> TryInsertAsync(InboundEmailReceipt receipt, CancellationToken cancellationToken = default)
		{
			var values = string.Join(", ", Array.ConvertAll(InsertColumns, c => P + c));

			// A unique violation would be logged as an error by RunAsync on every redelivery, so the collision is resolved in the
			// statement instead: ON CONFLICT on PostgreSQL, a key-range lock on SQL Server (UPDLOCK makes a concurrent twin wait
			// for this insert, HOLDLOCK keeps the absent key locked until it lands).
			var sql = IsPostgres
				? $"INSERT INTO {Tbl(Table)} ({Cols(InsertColumns)}) VALUES ({values}) ON CONFLICT ({Col("ProviderMessageId")}) DO NOTHING"
				: $"INSERT INTO {Tbl(Table)} ({Cols(InsertColumns)}) SELECT {values} " +
				  $"WHERE NOT EXISTS (SELECT 1 FROM {Tbl(Table)} WITH (UPDLOCK, HOLDLOCK) WHERE {Col("ProviderMessageId")} = {P}ProviderMessageId)";

			var inserted = await ExecuteAsync(sql, new
			{
				receipt.InboundEmailReceiptId,
				receipt.ProviderMessageId,
				receipt.Kind,
				receipt.State,
				receipt.PendingSteps,
				receipt.Deliveries,
				ReceivedOn = DatabaseTimestamp(receipt.ReceivedOn),
				ClaimedOn = receipt.ClaimedOn.HasValue ? DatabaseTimestamp(receipt.ClaimedOn.Value) : (DateTime?)null
			}, cancellationToken);

			return inserted == 1;
		}

		public Task<InboundEmailReceipt> GetByProviderMessageIdAsync(string providerMessageId, CancellationToken cancellationToken = default)
		{
			return QueryFirstOrDefaultAsync<InboundEmailReceipt>(
				$"SELECT * FROM {Tbl(Table)} WHERE {Col("ProviderMessageId")} = {CaseInsensitive("ProviderMessageId")}",
				new { ProviderMessageId = providerMessageId },
				cancellationToken);
		}

		public async Task<bool> TryReclaimAsync(string receiptId, int state, DateTime now, DateTime leaseCutoff, CancellationToken cancellationToken = default)
		{
			var updated = await ExecuteAsync(
				$"UPDATE {Tbl(Table)} SET {Col("ClaimedOn")} = {P}Now, {Col("Deliveries")} = {Col("Deliveries")} + 1 " +
				$"WHERE {Col("InboundEmailReceiptId")} = {CaseInsensitive("Id")} AND {Col("State")} = {P}State " +
				$"AND ({Col("ClaimedOn")} IS NULL OR {Col("ClaimedOn")} < {P}Cutoff)",
				new { Id = receiptId, State = state, Now = DatabaseTimestamp(now), Cutoff = DatabaseTimestamp(leaseCutoff) },
				cancellationToken);

			return updated == 1;
		}

		public async Task<bool> RecordDeliveryAsync(string receiptId, CancellationToken cancellationToken = default)
		{
			var updated = await ExecuteAsync(
				$"UPDATE {Tbl(Table)} SET {Col("Deliveries")} = {Col("Deliveries")} + 1 WHERE {Col("InboundEmailReceiptId")} = {CaseInsensitive("Id")}",
				new { Id = receiptId },
				cancellationToken);

			return updated == 1;
		}

		public async Task<bool> MarkSavedAsync(string receiptId, int entityId, int pendingSteps, DateTime now, CancellationToken cancellationToken = default)
		{
			var updated = await ExecuteAsync(
				$"UPDATE {Tbl(Table)} SET {Col("State")} = {P}Saved, {Col("EntityId")} = {P}EntityId, {Col("PendingSteps")} = {P}PendingSteps, " +
				$"{Col("SavedOn")} = {P}Now, {Col("ClaimedOn")} = {P}Now " +
				$"WHERE {Col("InboundEmailReceiptId")} = {CaseInsensitive("Id")} AND {Col("State")} = {P}Processing",
				new
				{
					Id = receiptId,
					EntityId = entityId,
					PendingSteps = pendingSteps,
					Now = DatabaseTimestamp(now),
					Saved = (int)InboundEmailReceiptStates.Saved,
					Processing = (int)InboundEmailReceiptStates.Processing
				},
				cancellationToken);

			return updated == 1;
		}

		public async Task<bool> CompleteStepsAsync(string receiptId, int steps, CancellationToken cancellationToken = default)
		{
			var updated = await ExecuteAsync(
				$"UPDATE {Tbl(Table)} SET {Col("PendingSteps")} = {Col("PendingSteps")} & ~{P}Steps " +
				$"WHERE {Col("InboundEmailReceiptId")} = {CaseInsensitive("Id")} AND {Col("State")} = {P}Saved",
				new { Id = receiptId, Steps = steps, Saved = (int)InboundEmailReceiptStates.Saved },
				cancellationToken);

			return updated == 1;
		}

		public async Task<bool> MarkCompletedAsync(string receiptId, DateTime now, CancellationToken cancellationToken = default)
		{
			var updated = await ExecuteAsync(
				$"UPDATE {Tbl(Table)} SET {Col("State")} = {P}Completed, {Col("PendingSteps")} = 0, {Col("CompletedOn")} = {P}Now, {Col("ClaimedOn")} = NULL " +
				$"WHERE {Col("InboundEmailReceiptId")} = {CaseInsensitive("Id")} AND {Col("State")} <> {P}Completed",
				new { Id = receiptId, Now = DatabaseTimestamp(now), Completed = (int)InboundEmailReceiptStates.Completed },
				cancellationToken);

			return updated == 1;
		}

		public async Task<bool> ReleaseAsync(string receiptId, CancellationToken cancellationToken = default)
		{
			var updated = await ExecuteAsync(
				$"UPDATE {Tbl(Table)} SET {Col("ClaimedOn")} = NULL " +
				$"WHERE {Col("InboundEmailReceiptId")} = {CaseInsensitive("Id")} AND {Col("State")} <> {P}Completed",
				new { Id = receiptId, Completed = (int)InboundEmailReceiptStates.Completed },
				cancellationToken);

			return updated == 1;
		}

		public Task<int> PurgeReceivedBeforeAsync(DateTime before, CancellationToken cancellationToken = default)
		{
			return ExecuteAsync(
				$"DELETE FROM {Tbl(Table)} WHERE {Col("ReceivedOn")} < {P}Before",
				new { Before = DatabaseTimestamp(before) },
				cancellationToken);
		}
	}
}
