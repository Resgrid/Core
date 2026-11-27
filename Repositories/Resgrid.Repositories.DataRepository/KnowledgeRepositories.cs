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
	/// The knowledge index store (K1, M0353), portable SQL for SQL Server and PostgreSQL. Every read starts at the department; a write to a
	/// department's chunks bumps its revision (and refreshes its counts) in the same transaction, so a reader's cached corpus reloads.
	/// </summary>
	public class KnowledgeIndexRepository : RmsRepositoryBase<KnowledgeChunk>, IKnowledgeIndexRepository
	{
		private static readonly string[] ChunkColumns =
		{
			"DepartmentId", "SourceKind", "ItemId", "VersionId", "VersionLabel", "Ordinal", "Anchor", "HeadingPath", "Title", "Text", "TextHash", "Locale",
			"EffectiveOn", "ExpiresOn", "AudienceJson", "AccessFlags", "Embedding", "EmbeddingModel", "EmbeddedOn", "IndexedOn"
		};

		public KnowledgeIndexRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		private static DateTime? Stamp(DateTime? value) => value.HasValue ? DatabaseTimestamp(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)) : (DateTime?)null;

		private static DateTime? Utc(DateTime? value) => value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : (DateTime?)null;

		private static List<KnowledgeChunk> AsUtc(IEnumerable<KnowledgeChunk> chunks)
		{
			var list = (chunks ?? Enumerable.Empty<KnowledgeChunk>()).ToList();
			foreach (var chunk in list)
			{
				chunk.EffectiveOn = Utc(chunk.EffectiveOn);
				chunk.ExpiresOn = Utc(chunk.ExpiresOn);
				chunk.EmbeddedOn = Utc(chunk.EmbeddedOn);
				chunk.IndexedOn = DateTime.SpecifyKind(chunk.IndexedOn, DateTimeKind.Utc);
			}
			return list;
		}

		private async Task InTransactionAsync(Func<Task> action, CancellationToken cancellationToken)
		{
			var owns = UnitOfWork.Transaction == null;
			await UnitOfWork.CreateOrGetConnectionAsync(cancellationToken);
			try
			{
				await action();
				if (owns)
					UnitOfWork.CommitChanges();
			}
			catch
			{
				if (owns)
					UnitOfWork.DiscardChanges();
				throw;
			}
		}

		public async Task<List<KnowledgeChunk>> GetCorpusAsync(int departmentId, CancellationToken cancellationToken = default)
		{
			return AsUtc(await QueryAsync<KnowledgeChunk>(
				$"SELECT * FROM {Tbl("KnowledgeChunks")} WHERE {Col("DepartmentId")} = {P}DepartmentId ORDER BY {Col("SourceKind")}, {Col("ItemId")}, {Col("Ordinal")}, {Col("KnowledgeChunkId")}",
				new { DepartmentId = departmentId }, cancellationToken));
		}

		public async Task<List<KnowledgeChunk>> GetItemChunksAsync(int departmentId, int sourceKind, string itemId, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(itemId))
				return new List<KnowledgeChunk>();

			return AsUtc(await QueryAsync<KnowledgeChunk>(
				$"SELECT * FROM {Tbl("KnowledgeChunks")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("SourceKind")} = {P}SourceKind AND {Col("ItemId")} = {P}ItemId " +
				$"ORDER BY {Col("Ordinal")}, {Col("KnowledgeChunkId")}",
				new { DepartmentId = departmentId, SourceKind = sourceKind, ItemId = itemId }, cancellationToken));
		}

		public async Task<List<KnowledgeIndexedItem>> GetIndexedItemsAsync(int departmentId, CancellationToken cancellationToken = default)
		{
			return (await QueryAsync<KnowledgeIndexedItem>(
				$"SELECT {Col("SourceKind")}, {Col("ItemId")}, MAX({Col("VersionId")}) AS {Col("VersionId")} FROM {Tbl("KnowledgeChunks")} WHERE {Col("DepartmentId")} = {P}DepartmentId " +
				$"GROUP BY {Col("SourceKind")}, {Col("ItemId")} ORDER BY {Col("SourceKind")}, {Col("ItemId")}",
				new { DepartmentId = departmentId }, cancellationToken)).ToList();
		}

		public Task ReplaceItemChunksAsync(int departmentId, int sourceKind, string itemId, IReadOnlyList<KnowledgeChunk> chunks, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(itemId))
				throw new ArgumentException("An item id is required.", nameof(itemId));
			if ((chunks ?? Array.Empty<KnowledgeChunk>()).Any(c => c == null || c.DepartmentId != departmentId || c.SourceKind != sourceKind || !string.Equals(c.ItemId, itemId, StringComparison.Ordinal)))
				throw new ArgumentException("Every chunk must belong to the item being replaced.", nameof(chunks));

			return InTransactionAsync(async () =>
			{
				await ExecuteAsync(
					$"DELETE FROM {Tbl("KnowledgeChunks")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("SourceKind")} = {P}SourceKind AND {Col("ItemId")} = {P}ItemId",
					new { DepartmentId = departmentId, SourceKind = sourceKind, ItemId = itemId }, cancellationToken);

				var insert = $"INSERT INTO {Tbl("KnowledgeChunks")} ({Cols(ChunkColumns)}) VALUES ({string.Join(", ", ChunkColumns.Select(c => P + c))})";
				foreach (var chunk in chunks ?? Array.Empty<KnowledgeChunk>())
				{
					await ExecuteAsync(insert, new
					{
						chunk.DepartmentId,
						chunk.SourceKind,
						chunk.ItemId,
						chunk.VersionId,
						chunk.VersionLabel,
						chunk.Ordinal,
						chunk.Anchor,
						chunk.HeadingPath,
						Title = chunk.Title ?? string.Empty,
						Text = chunk.Text ?? string.Empty,
						TextHash = chunk.TextHash ?? string.Empty,
						chunk.Locale,
						EffectiveOn = Stamp(chunk.EffectiveOn),
						ExpiresOn = Stamp(chunk.ExpiresOn),
						chunk.AudienceJson,
						chunk.AccessFlags,
						chunk.Embedding,
						chunk.EmbeddingModel,
						EmbeddedOn = Stamp(chunk.EmbeddedOn),
						IndexedOn = DatabaseTimestamp(DateTime.SpecifyKind(chunk.IndexedOn == default ? DateTime.UtcNow : chunk.IndexedOn, DateTimeKind.Utc))
					}, cancellationToken);
				}

				await BumpRevisionAsync(departmentId, cancellationToken);
			}, cancellationToken);
		}

		public async Task<int> DeleteItemAsync(int departmentId, int sourceKind, string itemId, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(itemId))
				return 0;

			var deleted = 0;
			await InTransactionAsync(async () =>
			{
				deleted = await ExecuteAsync(
					$"DELETE FROM {Tbl("KnowledgeChunks")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("SourceKind")} = {P}SourceKind AND {Col("ItemId")} = {P}ItemId",
					new { DepartmentId = departmentId, SourceKind = sourceKind, ItemId = itemId }, cancellationToken);
				if (deleted > 0)
					await BumpRevisionAsync(departmentId, cancellationToken);
			}, cancellationToken);
			return deleted;
		}

		public async Task<List<KnowledgeChunk>> GetChunksToEmbedAsync(int departmentId, string model, int take, CancellationToken cancellationToken = default)
		{
			return AsUtc(await QueryAsync<KnowledgeChunk>(
				$"SELECT * FROM {Tbl("KnowledgeChunks")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND ({Col("Embedding")} IS NULL OR {Col("EmbeddingModel")} IS NULL OR {Col("EmbeddingModel")} <> {P}Model) " +
				$"ORDER BY {Col("KnowledgeChunkId")} {Paging()}",
				new { DepartmentId = departmentId, Model = model ?? string.Empty, Skip = 0, Take = Math.Clamp(take, 1, 1000) }, cancellationToken));
		}

		public async Task<int> SaveEmbeddingsAsync(int departmentId, IReadOnlyList<KnowledgeChunk> chunks, CancellationToken cancellationToken = default)
		{
			var list = (chunks ?? Array.Empty<KnowledgeChunk>()).Where(c => c != null && c.DepartmentId == departmentId && c.Embedding != null).ToList();
			if (list.Count == 0)
				return 0;

			var saved = 0;
			await InTransactionAsync(async () =>
			{
				foreach (var chunk in list)
				{
					// Matched on the text hash too: a chunk rewritten since it was read keeps waiting for its own embedding.
					saved += await ExecuteAsync(
						$"UPDATE {Tbl("KnowledgeChunks")} SET {Col("Embedding")} = {P}Embedding, {Col("EmbeddingModel")} = {P}EmbeddingModel, {Col("EmbeddedOn")} = {P}EmbeddedOn " +
						$"WHERE {Col("KnowledgeChunkId")} = {P}KnowledgeChunkId AND {Col("DepartmentId")} = {P}DepartmentId AND {Col("TextHash")} = {P}TextHash",
						new
						{
							chunk.Embedding,
							chunk.EmbeddingModel,
							EmbeddedOn = Stamp(chunk.EmbeddedOn ?? DateTime.UtcNow),
							chunk.KnowledgeChunkId,
							DepartmentId = departmentId,
							TextHash = chunk.TextHash ?? string.Empty
						}, cancellationToken);
				}

				if (saved > 0)
					await BumpRevisionAsync(departmentId, cancellationToken);
			}, cancellationToken);
			return saved;
		}

		public async Task<Dictionary<int, long>> GetRevisionsAsync(IReadOnlyCollection<int> departmentIds, CancellationToken cancellationToken = default)
		{
			var ids = InListValue((departmentIds ?? Array.Empty<int>()).Distinct());
			var result = ids.ToDictionary(id => id, _ => 0L);
			if (ids.Length == 0)
				return result;

			foreach (var row in await QueryAsync<KnowledgeIndexState>(
				$"SELECT {Cols("DepartmentId", "Revision")} FROM {Tbl("KnowledgeIndexStates")} WHERE {InList("DepartmentId", "Ids")}", new { Ids = ids }, cancellationToken))
				result[row.DepartmentId] = row.Revision;
			return result;
		}

		public async Task EnqueueAsync(int departmentId, int sourceKind, string itemId, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var args = new { DepartmentId = departmentId, SourceKind = sourceKind, ItemId = itemId ?? string.Empty, Now = DatabaseTimestamp(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc)) };
			if (IsPostgres)
			{
				// ON CONFLICT never raises, so a caller's open transaction is not aborted by a repeat request.
				await ExecuteAsync(
					$"INSERT INTO {Tbl("KnowledgeIndexRequests")} AS r ({Cols("DepartmentId", "SourceKind", "ItemId", "RequestedOn", "Attempts", "Revision")}) VALUES ({P}DepartmentId, {P}SourceKind, {P}ItemId, {P}Now, 0, 0) " +
					$"ON CONFLICT ({Cols("DepartmentId", "SourceKind", "ItemId")}) DO UPDATE SET {Col("RequestedOn")} = EXCLUDED.{Col("RequestedOn")}, {Col("Attempts")} = 0, {Col("Revision")} = r.{Col("Revision")} + 1",
					args, cancellationToken);
				return;
			}

			try
			{
				await ExecuteAsync(
					$"UPDATE {Tbl("KnowledgeIndexRequests")} WITH (UPDLOCK, HOLDLOCK) SET {Col("RequestedOn")} = {P}Now, {Col("Attempts")} = 0, {Col("Revision")} = {Col("Revision")} + 1 " +
					$"WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("SourceKind")} = {P}SourceKind AND {Col("ItemId")} = {P}ItemId; " +
					$"IF @@ROWCOUNT = 0 INSERT INTO {Tbl("KnowledgeIndexRequests")} ({Cols("DepartmentId", "SourceKind", "ItemId", "RequestedOn", "Attempts", "Revision")}) VALUES ({P}DepartmentId, {P}SourceKind, {P}ItemId, {P}Now, 0, 0);",
					args, cancellationToken);
			}
			catch (Exception ex) when (IsUniqueViolation(ex))
			{
				// Another request for the same item won the insert: it is queued either way.
			}
		}

		public async Task<List<int>> GetDueDepartmentsAsync(DateTime utcNow, int take, CancellationToken cancellationToken = default)
		{
			var sql =
				$"SELECT d.{Col("DepartmentId")} FROM (" +
				$"SELECT {Col("DepartmentId")}, MIN({Col("RequestedOn")}) AS {Col("DueOn")} FROM {Tbl("KnowledgeIndexRequests")} WHERE {Col("RequestedOn")} <= {P}Now GROUP BY {Col("DepartmentId")} " +
				$"UNION ALL " +
				$"SELECT {Col("DepartmentId")}, {Col("NextRepairOn")} AS {Col("DueOn")} FROM {Tbl("KnowledgeIndexStates")} WHERE {Col("Active")} = {P}True AND {Col("NextRepairOn")} IS NOT NULL AND {Col("NextRepairOn")} <= {P}Now" +
				$") d GROUP BY d.{Col("DepartmentId")} ORDER BY MIN(d.{Col("DueOn")}), d.{Col("DepartmentId")} {Paging()}";
			return (await QueryAsync<int>(sql, new { Now = DatabaseTimestamp(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc)), True = true, Skip = 0, Take = Math.Clamp(take, 1, 200) }, cancellationToken)).ToList();
		}

		public async Task<List<KnowledgeIndexRequest>> GetRequestsAsync(int departmentId, DateTime utcNow, int take, CancellationToken cancellationToken = default)
		{
			var rows = (await QueryAsync<KnowledgeIndexRequest>(
				$"SELECT * FROM {Tbl("KnowledgeIndexRequests")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RequestedOn")} <= {P}Now " +
				$"ORDER BY {Col("RequestedOn")}, {Col("KnowledgeIndexRequestId")} {Paging()}",
				new { DepartmentId = departmentId, Now = DatabaseTimestamp(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc)), Skip = 0, Take = Math.Clamp(take, 1, 1000) }, cancellationToken)).ToList();
			return rows;
		}

		public async Task DeleteRequestsAsync(IReadOnlyCollection<KnowledgeIndexRequest> requests, CancellationToken cancellationToken = default)
		{
			foreach (var request in requests ?? Array.Empty<KnowledgeIndexRequest>())
			{
				if (request == null)
					continue;
				await ExecuteAsync(
					$"DELETE FROM {Tbl("KnowledgeIndexRequests")} WHERE {Col("KnowledgeIndexRequestId")} = {P}Id AND {Col("DepartmentId")} = {P}DepartmentId AND {Col("Revision")} = {P}Revision",
					new { Id = request.KnowledgeIndexRequestId, request.DepartmentId, request.Revision }, cancellationToken);
			}
		}

		public Task PostponeRequestAsync(KnowledgeIndexRequest request, DateTime untilUtc, CancellationToken cancellationToken = default)
		{
			if (request == null)
				return Task.CompletedTask;
			return ExecuteAsync(
				$"UPDATE {Tbl("KnowledgeIndexRequests")} SET {Col("RequestedOn")} = {P}Until, {Col("Attempts")} = {Col("Attempts")} + 1 " +
				$"WHERE {Col("KnowledgeIndexRequestId")} = {P}Id AND {Col("DepartmentId")} = {P}DepartmentId AND {Col("Revision")} = {P}Revision",
				new { Id = request.KnowledgeIndexRequestId, request.DepartmentId, request.Revision, Until = DatabaseTimestamp(DateTime.SpecifyKind(untilUtc, DateTimeKind.Utc)) }, cancellationToken);
		}

		public Task DeleteDepartmentRequestsAsync(int departmentId, CancellationToken cancellationToken = default)
		{
			return ExecuteAsync($"DELETE FROM {Tbl("KnowledgeIndexRequests")} WHERE {Col("DepartmentId")} = {P}DepartmentId", new { DepartmentId = departmentId }, cancellationToken);
		}

		public async Task<KnowledgeIndexState> GetStateAsync(int departmentId, CancellationToken cancellationToken = default)
		{
			var state = await QueryFirstOrDefaultAsync<KnowledgeIndexState>(
				$"SELECT * FROM {Tbl("KnowledgeIndexStates")} WHERE {Col("DepartmentId")} = {P}DepartmentId", new { DepartmentId = departmentId }, cancellationToken);
			if (state != null)
			{
				state.NextRepairOn = Utc(state.NextRepairOn);
				state.LastRepairOn = Utc(state.LastRepairOn);
				state.LastIndexedOn = Utc(state.LastIndexedOn);
			}
			return state;
		}

		public async Task SaveStateAsync(KnowledgeIndexState state, CancellationToken cancellationToken = default)
		{
			if (state == null)
				return;

			await EnsureStateAsync(state.DepartmentId, cancellationToken);
			await ExecuteAsync(
				$"UPDATE {Tbl("KnowledgeIndexStates")} SET {Col("Active")} = {P}Active, {Col("NextRepairOn")} = {P}NextRepairOn, {Col("LastRepairOn")} = {P}LastRepairOn, " +
				$"{Col("EmbeddingModel")} = {P}EmbeddingModel, {Col("LastError")} = {P}LastError WHERE {Col("DepartmentId")} = {P}DepartmentId",
				new
				{
					state.DepartmentId,
					state.Active,
					NextRepairOn = Stamp(state.NextRepairOn),
					LastRepairOn = Stamp(state.LastRepairOn),
					state.EmbeddingModel,
					LastError = string.IsNullOrEmpty(state.LastError) || state.LastError.Length <= 500 ? state.LastError : state.LastError.Substring(0, 500)
				}, cancellationToken);
		}

		private Task EnsureStateAsync(int departmentId, CancellationToken cancellationToken)
		{
			var args = new { DepartmentId = departmentId, True = true };
			if (IsPostgres)
				return ExecuteAsync(
					$"INSERT INTO {Tbl("KnowledgeIndexStates")} ({Cols("DepartmentId", "Revision", "Active", "ChunkCount", "EmbeddedCount")}) VALUES ({P}DepartmentId, 0, {P}True, 0, 0) " +
					$"ON CONFLICT ({Col("DepartmentId")}) DO NOTHING", args, cancellationToken);

			return ExecuteAsync(
				$"IF NOT EXISTS (SELECT 1 FROM {Tbl("KnowledgeIndexStates")} WITH (UPDLOCK, HOLDLOCK) WHERE {Col("DepartmentId")} = {P}DepartmentId) " +
				$"INSERT INTO {Tbl("KnowledgeIndexStates")} ({Cols("DepartmentId", "Revision", "Active", "ChunkCount", "EmbeddedCount")}) VALUES ({P}DepartmentId, 0, {P}True, 0, 0);",
				args, cancellationToken);
		}

		private async Task BumpRevisionAsync(int departmentId, CancellationToken cancellationToken)
		{
			await EnsureStateAsync(departmentId, cancellationToken);
			await ExecuteAsync(
				$"UPDATE {Tbl("KnowledgeIndexStates")} SET {Col("Revision")} = {Col("Revision")} + 1, {Col("LastIndexedOn")} = {P}Now, " +
				$"{Col("ChunkCount")} = (SELECT COUNT(*) FROM {Tbl("KnowledgeChunks")} c WHERE c.{Col("DepartmentId")} = {P}DepartmentId), " +
				$"{Col("EmbeddedCount")} = (SELECT COUNT(*) FROM {Tbl("KnowledgeChunks")} c WHERE c.{Col("DepartmentId")} = {P}DepartmentId AND c.{Col("Embedding")} IS NOT NULL) " +
				$"WHERE {Col("DepartmentId")} = {P}DepartmentId",
				new { DepartmentId = departmentId, Now = DatabaseTimestamp(DateTime.UtcNow) }, cancellationToken);
		}

		public async Task<List<DispatchProtocol>> GetProtocolHeadersAsync(int departmentId, CancellationToken cancellationToken = default)
		{
			return (await QueryAsync<DispatchProtocol>(
				$"SELECT {Cols("DispatchProtocolId", "DepartmentId", "Name", "Code", "Number", "Kind", "Status", "CurrentVersionId", "IsDeleted", "IsDisabled", "ExcludeFromAi", "IsLicensedContent")} " +
				$"FROM {Tbl("DispatchProtocols")} WHERE {Col("DepartmentId")} = {P}DepartmentId ORDER BY {Col("DispatchProtocolId")}",
				new { DepartmentId = departmentId }, cancellationToken)).ToList();
		}

		public async Task<List<KnowledgeDocumentHeader>> GetDocumentHeadersAsync(int departmentId, CancellationToken cancellationToken = default)
		{
			var rows = (await QueryAsync<KnowledgeDocumentHeader>(
				$"SELECT {Cols("DocumentId", "DepartmentId", "Name", "Category", "Description", "Filename", "AdminsOnly", "IsProtected", "AddedOn", "RemoveOn")} " +
				$"FROM {Tbl("Documents")} WHERE {Col("DepartmentId")} = {P}DepartmentId ORDER BY {Col("DocumentId")}",
				new { DepartmentId = departmentId }, cancellationToken)).ToList();
			foreach (var row in rows)
			{
				row.AddedOn = DateTime.SpecifyKind(row.AddedOn, DateTimeKind.Utc);
				row.RemoveOn = Utc(row.RemoveOn);
			}
			return rows;
		}
	}

	/// <summary>The knowledge gaps report (K1, M0354). Every read starts at the department.</summary>
	public class KnowledgeGapsRepository : RmsRepositoryBase<KnowledgeGap>, IKnowledgeGapsRepository
	{
		private static readonly string[] InsertColumns =
		{
			"DepartmentId", "UserId", "AskerRole", "Question", "Reason", "Surface", "Status", "AskedOn", "NearestSourcesJson", "ContextKind", "ContextItemId", "AiGenerationId"
		};

		public KnowledgeGapsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		private static KnowledgeGap AsUtc(KnowledgeGap gap)
		{
			if (gap != null)
			{
				gap.AskedOn = DateTime.SpecifyKind(gap.AskedOn, DateTimeKind.Utc);
				gap.ResolvedOn = gap.ResolvedOn.HasValue ? DateTime.SpecifyKind(gap.ResolvedOn.Value, DateTimeKind.Utc) : (DateTime?)null;
			}
			return gap;
		}

		public async Task<KnowledgeGap> InsertAsync(KnowledgeGap gap, CancellationToken cancellationToken = default)
		{
			if (gap == null || gap.DepartmentId <= 0)
				throw new ArgumentException("A department gap is required.", nameof(gap));

			var columns = Cols(InsertColumns);
			var values = string.Join(", ", InsertColumns.Select(c => P + c));
			var sql = IsPostgres
				? $"INSERT INTO {Tbl("KnowledgeGaps")} ({columns}) VALUES ({values}) RETURNING {Col("KnowledgeGapId")}"
				: $"INSERT INTO {Tbl("KnowledgeGaps")} ({columns}) OUTPUT INSERTED.{Col("KnowledgeGapId")} VALUES ({values})";

			gap.KnowledgeGapId = await ScalarAsync<int>(sql, new
			{
				gap.DepartmentId,
				gap.UserId,
				gap.AskerRole,
				gap.Question,
				gap.Reason,
				gap.Surface,
				gap.Status,
				AskedOn = DatabaseTimestamp(DateTime.SpecifyKind(gap.AskedOn == default ? DateTime.UtcNow : gap.AskedOn, DateTimeKind.Utc)),
				gap.NearestSourcesJson,
				gap.ContextKind,
				gap.ContextItemId,
				gap.AiGenerationId
			}, cancellationToken);
			return gap;
		}

		public async Task<List<KnowledgeGap>> GetForDepartmentAsync(int departmentId, int? status, int skip, int take, CancellationToken cancellationToken = default)
		{
			var filter = status.HasValue ? $" AND {Col("Status")} = {P}Status" : string.Empty;
			return (await QueryAsync<KnowledgeGap>(
				$"SELECT * FROM {Tbl("KnowledgeGaps")} WHERE {Col("DepartmentId")} = {P}DepartmentId{filter} ORDER BY {Col("AskedOn")} DESC, {Col("KnowledgeGapId")} DESC {Paging()}",
				new { DepartmentId = departmentId, Status = status ?? 0, Skip = Math.Max(0, skip), Take = Math.Clamp(take, 1, 500) }, cancellationToken))
				.Select(AsUtc).ToList();
		}

		public Task<int> CountAsync(int departmentId, int? status, CancellationToken cancellationToken = default)
		{
			var filter = status.HasValue ? $" AND {Col("Status")} = {P}Status" : string.Empty;
			return ScalarAsync<int>($"SELECT COUNT(*) FROM {Tbl("KnowledgeGaps")} WHERE {Col("DepartmentId")} = {P}DepartmentId{filter}",
				new { DepartmentId = departmentId, Status = status ?? 0 }, cancellationToken);
		}

		public async Task<KnowledgeGap> GetByIdAsync(int departmentId, int knowledgeGapId, CancellationToken cancellationToken = default)
		{
			return AsUtc(await QueryFirstOrDefaultAsync<KnowledgeGap>(
				$"SELECT * FROM {Tbl("KnowledgeGaps")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("KnowledgeGapId")} = {P}Id",
				new { DepartmentId = departmentId, Id = knowledgeGapId }, cancellationToken));
		}

		public async Task<KnowledgeGap> GetByExchangeAsync(int departmentId, string aiGenerationId, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(aiGenerationId))
				return null;
			return AsUtc(await QueryFirstOrDefaultAsync<KnowledgeGap>(
				$"SELECT * FROM {Tbl("KnowledgeGaps")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("AiGenerationId")} = {P}Id",
				new { DepartmentId = departmentId, Id = aiGenerationId }, cancellationToken));
		}

		public Task InsertGenerationAsync(Resgrid.Model.AdminAssist.AiGenerationRow row, CancellationToken cancellationToken = default)
		{
			if (row == null || row.DepartmentId <= 0 || string.IsNullOrWhiteSpace(row.UserId) || row.Content == null || row.Content.Length > 65536 ||
				!Guid.TryParseExact(row.Id, "D", out _) || !Guid.TryParseExact(row.ConversationId, "D", out _) ||
				(row.IsProtected ? !ProtectedDataEnvelope.HasEnvelopePrefix(row.Content) : !row.Content.StartsWith("enc2:", StringComparison.Ordinal)))
				throw new ArgumentException("Invalid knowledge exchange write.");

			return ExecuteAsync($"INSERT INTO {Tbl("AiGenerations")} ({Cols("Id", "ConversationId", "DepartmentId", "UserId", "Revision", "CreatedOnUtc", "Content", "IsProtected", "ProtectedCatalogVersion", "PromptVersion", "ModelRevision", "RuntimeDigest", "RequestDigest", "InputTokens", "OutputTokens", "Outcome")}) " +
				$"VALUES ({P}Id,{P}ConversationId,{P}DepartmentId,{P}UserId,{P}Revision,{P}CreatedOnUtc,{P}Content,{P}IsProtected,{P}ProtectedCatalogVersion,{P}PromptVersion,{P}ModelRevision,{P}RuntimeDigest,{P}RequestDigest,{P}InputTokens,{P}OutputTokens,{P}Outcome)",
				new
				{
					row.Id,
					row.ConversationId,
					row.DepartmentId,
					row.UserId,
					row.Revision,
					CreatedOnUtc = DatabaseTimestamp(DateTime.SpecifyKind(row.CreatedOnUtc, DateTimeKind.Utc)),
					row.Content,
					row.IsProtected,
					row.ProtectedCatalogVersion,
					PromptVersion = row.PromptVersion ?? string.Empty,
					ModelRevision = row.ModelRevision ?? string.Empty,
					RuntimeDigest = row.RuntimeDigest ?? string.Empty,
					RequestDigest = row.RequestDigest ?? string.Empty,
					row.InputTokens,
					row.OutputTokens,
					row.Outcome
				}, cancellationToken);
		}

		public async Task<Resgrid.Model.AdminAssist.AiGenerationRow> GetGenerationAsync(int departmentId, string userId, string id, CancellationToken cancellationToken = default)
		{
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(userId) || !Guid.TryParseExact(id, "D", out _))
				return null;
			var row = await QueryFirstOrDefaultAsync<Resgrid.Model.AdminAssist.AiGenerationRow>(
				$"SELECT * FROM {Tbl("AiGenerations")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("UserId")} = {P}UserId AND {Col("Id")} = {P}Id",
				new { DepartmentId = departmentId, UserId = userId, Id = id }, cancellationToken);
			if (row != null)
				row.CreatedOnUtc = DateTime.SpecifyKind(row.CreatedOnUtc, DateTimeKind.Utc);
			return row;
		}

		public async Task<bool> UpdateStatusAsync(KnowledgeGap gap, CancellationToken cancellationToken = default)
		{
			if (gap == null)
				return false;
			return await ExecuteAsync(
				$"UPDATE {Tbl("KnowledgeGaps")} SET {Col("Status")} = {P}Status, {Col("ResolvedOn")} = {P}ResolvedOn, {Col("ResolvedByUserId")} = {P}ResolvedByUserId, " +
				$"{Col("ResolutionNote")} = {P}ResolutionNote WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("KnowledgeGapId")} = {P}Id",
				new
				{
					gap.Status,
					ResolvedOn = gap.ResolvedOn.HasValue ? DatabaseTimestamp(DateTime.SpecifyKind(gap.ResolvedOn.Value, DateTimeKind.Utc)) : (DateTime?)null,
					gap.ResolvedByUserId,
					gap.ResolutionNote,
					gap.DepartmentId,
					Id = gap.KnowledgeGapId
				}, cancellationToken) == 1;
		}
	}
}
