using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>An item the department's index holds chunks for.</summary>
	public sealed class KnowledgeIndexedItem
	{
		public int SourceKind { get; set; }
		public string ItemId { get; set; }
		public string VersionId { get; set; }
	}

	/// <summary>A document's metadata for the knowledge corpus: never its bytes.</summary>
	public sealed class KnowledgeDocumentHeader
	{
		public int DocumentId { get; set; }
		public int DepartmentId { get; set; }
		public string Name { get; set; }
		public string Category { get; set; }
		public string Description { get; set; }
		public string Filename { get; set; }
		public bool AdminsOnly { get; set; }
		public bool IsProtected { get; set; }
		public DateTime AddedOn { get; set; }
		public DateTime? RemoveOn { get; set; }
	}

	/// <summary>
	/// The knowledge index store (K1, M0368): chunks with their embeddings, the per-item reindex queue and the per-department state.
	/// Every read starts at the department; list reads never load another department's rows. Writes to a department's chunks bump its
	/// revision in the same transaction.
	/// </summary>
	public interface IKnowledgeIndexRepository
	{
		/// <summary>Every chunk of one department (or 0, the system corpus), with its embedding: the corpus a question is scored over.</summary>
		Task<List<KnowledgeChunk>> GetCorpusAsync(int departmentId, CancellationToken cancellationToken = default);

		/// <summary>One item's chunks with their embeddings (so an unchanged chunk keeps its vector).</summary>
		Task<List<KnowledgeChunk>> GetItemChunksAsync(int departmentId, int sourceKind, string itemId, CancellationToken cancellationToken = default);

		Task<List<KnowledgeIndexedItem>> GetIndexedItemsAsync(int departmentId, CancellationToken cancellationToken = default);

		/// <summary>Replaces the item's chunks in one transaction and bumps the department's revision.</summary>
		Task ReplaceItemChunksAsync(int departmentId, int sourceKind, string itemId, IReadOnlyList<KnowledgeChunk> chunks, CancellationToken cancellationToken = default);

		/// <summary>Removes the item's chunks (excluded, retired, deleted) and bumps the revision when any were removed.</summary>
		Task<int> DeleteItemAsync(int departmentId, int sourceKind, string itemId, CancellationToken cancellationToken = default);

		/// <summary>Chunks without an embedding of <paramref name="model"/> (an outage left them keyword-only, or the model changed).</summary>
		Task<List<KnowledgeChunk>> GetChunksToEmbedAsync(int departmentId, string model, int take, CancellationToken cancellationToken = default);

		/// <summary>Stores embeddings on existing chunks (matched by id and text hash) and bumps the revision.</summary>
		Task<int> SaveEmbeddingsAsync(int departmentId, IReadOnlyList<KnowledgeChunk> chunks, CancellationToken cancellationToken = default);

		/// <summary>The revisions of these departments' indexes (missing = 0).</summary>
		Task<Dictionary<int, long>> GetRevisionsAsync(IReadOnlyCollection<int> departmentIds, CancellationToken cancellationToken = default);

		/// <summary>Queues an item (or <see cref="KnowledgeIndexRequest.DepartmentRebuild"/>) once; asking again moves its RequestedOn and bumps its Revision.</summary>
		Task EnqueueAsync(int departmentId, int sourceKind, string itemId, DateTime utcNow, CancellationToken cancellationToken = default);

		/// <summary>
		/// Departments with due work only, never a sweep: queued requests, or an active index whose repair pass is due. Oldest work first.
		/// </summary>
		Task<List<int>> GetDueDepartmentsAsync(DateTime utcNow, int take, CancellationToken cancellationToken = default);

		/// <summary>The department's requests that are due (a failed one waits out its backoff), oldest first.</summary>
		Task<List<KnowledgeIndexRequest>> GetRequestsAsync(int departmentId, DateTime utcNow, int take, CancellationToken cancellationToken = default);

		/// <summary>
		/// Removes processed requests; a request asked for again while it was being processed (its Revision moved) stays queued.
		/// </summary>
		Task DeleteRequestsAsync(IReadOnlyCollection<KnowledgeIndexRequest> requests, CancellationToken cancellationToken = default);

		/// <summary>A request that failed waits until <paramref name="untilUtc"/> before it is tried again.</summary>
		Task PostponeRequestAsync(KnowledgeIndexRequest request, DateTime untilUtc, CancellationToken cancellationToken = default);

		Task DeleteDepartmentRequestsAsync(int departmentId, CancellationToken cancellationToken = default);

		Task<KnowledgeIndexState> GetStateAsync(int departmentId, CancellationToken cancellationToken = default);

		/// <summary>Inserts or updates the state (the revision is not moved here).</summary>
		Task SaveStateAsync(KnowledgeIndexState state, CancellationToken cancellationToken = default);

		/// <summary>The department's protocols as the library lists them (no content, no triggers): identity, status and the AI flags.</summary>
		Task<List<DispatchProtocol>> GetProtocolHeadersAsync(int departmentId, CancellationToken cancellationToken = default);

		Task<List<KnowledgeDocumentHeader>> GetDocumentHeadersAsync(int departmentId, CancellationToken cancellationToken = default);
	}

	/// <summary>The knowledge gaps report (§16.4, M0369).</summary>
	public interface IKnowledgeGapsRepository
	{
		Task<KnowledgeGap> InsertAsync(KnowledgeGap gap, CancellationToken cancellationToken = default);

		Task<List<KnowledgeGap>> GetForDepartmentAsync(int departmentId, int? status, int skip, int take, CancellationToken cancellationToken = default);

		Task<int> CountAsync(int departmentId, int? status, CancellationToken cancellationToken = default);

		Task<KnowledgeGap> GetByIdAsync(int departmentId, int knowledgeGapId, CancellationToken cancellationToken = default);

		/// <summary>An exchange's gap, if one was recorded (a second thumbs-down does not add another).</summary>
		Task<KnowledgeGap> GetByExchangeAsync(int departmentId, string aiGenerationId, CancellationToken cancellationToken = default);

		Task<bool> UpdateStatusAsync(KnowledgeGap gap, CancellationToken cancellationToken = default);

		/// <summary>
		/// Logs one knowledge exchange to AiGenerations (the Enhanced AI audit table) as a single-turn conversation of its own: content
		/// already encrypted by the conversation protection, prompt version, model revision and digest, tokens and outcome.
		/// </summary>
		Task InsertGenerationAsync(Resgrid.Model.AdminAssist.AiGenerationRow row, CancellationToken cancellationToken = default);

		/// <summary>One of the user's own logged exchanges.</summary>
		Task<Resgrid.Model.AdminAssist.AiGenerationRow> GetGenerationAsync(int departmentId, string userId, string id, CancellationToken cancellationToken = default);
	}
}
