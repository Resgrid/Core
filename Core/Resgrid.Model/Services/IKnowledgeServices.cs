using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Queues one item for reindexing (publish, retire, audience or AI-flag change). Never fails the caller: a lost request is picked up
	/// by worker 89's nightly repair pass.
	/// </summary>
	public interface IKnowledgeIndexQueue
	{
		Task EnqueueAsync(int departmentId, KnowledgeSourceKinds kind, string itemId, CancellationToken cancellationToken = default);
	}

	/// <summary>
	/// One part of the knowledge corpus (§16.1): which items of its kinds may be indexed, their chunks, and which of them a reader may
	/// open right now. A kind no source claims is never readable (fail closed). LMS course lessons (T2) arrive as another source.
	/// </summary>
	public interface IKnowledgeCorpusSource
	{
		IReadOnlyCollection<KnowledgeSourceKinds> Kinds { get; }

		/// <summary>The department's items of <paramref name="kind"/> that may be indexed now (published, in effect, not excluded).</summary>
		Task<IReadOnlyList<string>> ListItemIdsAsync(int departmentId, KnowledgeSourceKinds kind, CancellationToken cancellationToken = default);

		/// <summary>The item's chunks, or null when it must not be indexed (draft, retired, excluded from AI, licensed, removed).</summary>
		Task<KnowledgeItemContent> BuildAsync(int departmentId, KnowledgeSourceKinds kind, string itemId, CancellationToken cancellationToken = default);

		/// <summary>Adds the items of its kinds this reader may open right now to <paramref name="scope"/> (the source's own read rule).</summary>
		Task AddReadableAsync(KnowledgeReader reader, KnowledgeReaderScope scope, CancellationToken cancellationToken = default);
	}

	/// <summary>
	/// Extracted file text by page (protocol and training attachments, documents). The extraction itself and its page store belong to
	/// the protocols wave-2 work (bulk upload + file text extraction); until it is registered the default source returns no pages.
	/// </summary>
	public interface IKnowledgePageTextSource
	{
		Task<IReadOnlyList<KnowledgePageText>> GetPagesAsync(int departmentId, KnowledgeSourceKinds kind, string itemId, string versionId,
			CancellationToken cancellationToken = default);
	}

	/// <summary>A question to score against a reader's visible corpus.</summary>
	public sealed class KnowledgeRetrievalRequest
	{
		public KnowledgeReaderScope Scope { get; set; }

		public string Question { get; set; }

		/// <summary>The question's embedding; null scores by keywords only.</summary>
		public float[] QueryVector { get; set; }

		public string QueryModel { get; set; }

		public int TopK { get; set; } = 6;

		/// <summary>The per-source diversity cap: most chunks taken from one item.</summary>
		public int MaxPerItem { get; set; } = 2;

		/// <summary>Only chunks of this item (a protocol or training page's Ask).</summary>
		public int? OnlyKind { get; set; }

		public string OnlyItemId { get; set; }

		/// <summary>Score these chunks instead of the stored corpus (a call's cited version, built at request time). Still access-filtered.</summary>
		public IReadOnlyList<KnowledgeChunk> Chunks { get; set; }

		/// <summary>The asker's locale: an item with an approved translation in that language is answered from it (round 5F).</summary>
		public string Locale { get; set; }
	}

	public sealed class KnowledgeScoredChunk
	{
		public KnowledgeChunk Chunk { get; set; }

		/// <summary>Exact cosine with the question's vector; null when either has no vector of the same model.</summary>
		public double? Cosine { get; set; }

		/// <summary>BM25 over the reader's visible candidates.</summary>
		public double Keyword { get; set; }

		/// <summary>Share of the question's distinct terms the chunk contains (0–1).</summary>
		public double Coverage { get; set; }

		/// <summary>The fused, re-ranked score the order is taken from.</summary>
		public double Score { get; set; }
	}

	public sealed class KnowledgeRetrievalResult
	{
		/// <summary>The top chunks, diversity-capped, best first.</summary>
		public List<KnowledgeScoredChunk> Selected { get; set; } = new List<KnowledgeScoredChunk>();

		/// <summary>Whether the best chunk passes the relevance floor (cosine or keyword coverage); otherwise the answer is "not covered".</summary>
		public bool Adequate { get; set; }

		/// <summary><see cref="KnowledgeRetrievalModes"/>.</summary>
		public string Mode { get; set; }

		/// <summary>How many chunks survived the department and audience filter (all of them were scored, none other).</summary>
		public int Candidates { get; set; }
	}

	/// <summary>Brute-force exact retrieval inside Resgrid.Search (§25.4a item 11): filter by department and audience first, then score.</summary>
	public interface IKnowledgeRetriever
	{
		Task<KnowledgeRetrievalResult> RetrieveAsync(KnowledgeRetrievalRequest request, CancellationToken cancellationToken = default);
	}

	/// <summary>What one worker 89 department pass did.</summary>
	public sealed class KnowledgeIndexRunResult
	{
		public int ItemsIndexed { get; set; }
		public int ItemsRemoved { get; set; }
		public int ChunksEmbedded { get; set; }
		public int Repairs { get; set; }
		public bool EmbeddingsUnavailable { get; set; }
		public int Failures { get; set; }
	}

	/// <summary>
	/// The department knowledge index (K1): per-item reindex on publish and retire through the queue, and worker 89's nightly repair pass
	/// (every indexable item re-checked, orphans removed, chunks missing an embedding embedded). Embedding calls run only here, on the worker.
	/// </summary>
	public interface IKnowledgeIndexService : IKnowledgeIndexQueue
	{
		/// <summary>Queues a whole-department rebuild (first use, settings change, an administrator's request).</summary>
		Task RequestRebuildAsync(int departmentId, string userId, CancellationToken cancellationToken = default);

		/// <summary>Departments with queued requests or a due repair pass (never every department).</summary>
		Task<List<int>> GetDueDepartmentsAsync(DateTime utcNow, int take, CancellationToken cancellationToken = default);

		/// <summary>Runs one department's queued requests, and its repair pass when due. The caller has already checked the entitlement.</summary>
		Task<KnowledgeIndexRunResult> ProcessDepartmentAsync(int departmentId, DateTime utcNow, CancellationToken cancellationToken = default);

		/// <summary>The department is no longer entitled: its queued requests are dropped and its index stops being repaired.</summary>
		Task DeactivateAsync(int departmentId, CancellationToken cancellationToken = default);

		/// <summary>Rebuilds one item from its source (or removes it when it is no longer indexable). Returns the chunks written.</summary>
		Task<int> ReindexItemAsync(int departmentId, KnowledgeSourceKinds kind, string itemId, CancellationToken cancellationToken = default);

		Task<KnowledgeIndexState> GetStateAsync(int departmentId, CancellationToken cancellationToken = default);
	}

	/// <summary>
	/// Department knowledge answers with citations (§16.2–16.3), gated by <c>Ai.Knowledge</c> through the Enhanced AI entitlement. Answers
	/// only from retrieved sources; every claim cites item, version, anchor and effective date; "not covered" with the nearest items when
	/// nothing adequate is found; no model-computed clinical numbers. Each exchange is logged to AiGenerations.
	/// </summary>
	public interface IAiKnowledgeService
	{
		/// <summary>Whether the department may use knowledge answers right now (flags, add-on window, module switch); fails closed.</summary>
		Task<bool> IsEntitledAsync(int departmentId);

		Task<KnowledgeStatus> GetStatusAsync(KnowledgeReader reader, CancellationToken cancellationToken = default);

		Task<KnowledgeAnswer> AskAsync(KnowledgeAskRequest request, CancellationToken cancellationToken = default);

		/// <summary>Thumbs up or down on one of the reader's own exchanges; a thumbs-down becomes a knowledge gap.</summary>
		Task<bool> FeedbackAsync(KnowledgeReader reader, string exchangeId, bool helpful, int surface, CancellationToken cancellationToken = default);

		Task<KnowledgeSettings> GetSettingsAsync(int departmentId);

		Task<KnowledgeSettings> SaveSettingsAsync(int departmentId, KnowledgeSettings settings, string userId, CancellationToken cancellationToken = default);
	}

	/// <summary>
	/// K3: builds one version of an item for one reader at request time ("Ask this course" over the course version the learner takes). The
	/// source decides, with its own gates (the LMS for courses), whether the reader may use it; a kind no version source claims is refused.
	/// </summary>
	public interface IKnowledgeVersionContentSource
	{
		IReadOnlyCollection<KnowledgeSourceKinds> VersionKinds { get; }

		/// <summary>The chunks of the version (<paramref name="versionId"/> null: the reader's own), or a refusal with its outcome.</summary>
		Task<KnowledgeVersionContent> LoadForReaderAsync(KnowledgeReader reader, KnowledgeSourceKinds kind, string itemId, string versionId,
			CancellationToken cancellationToken = default);
	}

	/// <summary>
	/// K3: a call's operational fields for a call-context answer (<see cref="KnowledgeCallContext"/>, an allow-list): the type, the priority's
	/// name, the determinant and the location's point-of-interest type. Never patient or protected data.
	/// </summary>
	public interface IKnowledgeCallContextService
	{
		Task<KnowledgeCallContext> BuildAsync(Call call, CancellationToken cancellationToken = default);
	}

	/// <summary>
	/// The dispatch determinant a call carries (P4, built by the dispatch engine stream). Until P4 registers its reader the default returns
	/// null and call-context answers go without a determinant.
	/// </summary>
	public interface IKnowledgeCallDeterminantSource
	{
		Task<string> GetDeterminantAsync(int departmentId, int callId, CancellationToken cancellationToken = default);
	}

	/// <summary>
	/// Scenario practice from a protocol version (K3 tutor, §13a "scenario practice from protocols"): a short scenario and questions written
	/// only from the version's text, and the member's free-text answers scored against the cited content only (no clinical number the
	/// protocol does not hold, K1's number-in-source rule). Practice only: nothing is written to the transcript, a certification or a quiz.
	/// Gated by Enhanced AI with Ai.Knowledge only (round 7A, decision 2026-11-25: protocols live outside the LMS, so practice never needs it;
	/// "Ask this course" still needs both), fails closed. Every exchange is logged to AiGenerations.
	/// </summary>
	public interface IKnowledgePracticeService
	{
		Task<KnowledgePracticeStatus> GetStatusAsync(KnowledgeReader reader, CancellationToken cancellationToken = default);

		/// <summary>A scenario from the protocol's version in force (the reader must be able to open the protocol).</summary>
		Task<KnowledgePracticeScenario> StartAsync(KnowledgeReader reader, int protocolId, CancellationToken cancellationToken = default);

		/// <summary>Scores the reader's answers to one of their own scenarios (<paramref name="exchangeId"/>).</summary>
		Task<KnowledgePracticeResult> ScoreAsync(KnowledgeReader reader, string exchangeId, IReadOnlyList<KnowledgePracticeAnswer> answers,
			CancellationToken cancellationToken = default);
	}

	/// <summary>The knowledge gaps report (§16.4) for training officers and protocol owners.</summary>
	public interface IKnowledgeGapsService
	{
		/// <summary>Department admins, protocol authors and approvers, and members who create or assign trainings.</summary>
		Task<bool> CanSeeGapsAsync(int departmentId, string userId);

		Task<List<KnowledgeGap>> GetGapsAsync(int departmentId, int? status, int skip, int take, CancellationToken cancellationToken = default);

		Task<int> CountAsync(int departmentId, int? status, CancellationToken cancellationToken = default);

		Task<KnowledgeGap> GetGapAsync(int departmentId, int knowledgeGapId, CancellationToken cancellationToken = default);

		Task<KnowledgeGap> RecordAsync(KnowledgeGap gap, CancellationToken cancellationToken = default);

		Task<bool> SetStatusAsync(int departmentId, int knowledgeGapId, KnowledgeGapStatuses status, string userId, string note, CancellationToken cancellationToken = default);
	}
}
