using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Config;
using Resgrid.Llm;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Search.Knowledge
{
	/// <summary>
	/// The per-process store of prepared department corpora, keyed by department and reloaded when the department's index revision moves.
	/// A department's corpus is thousands of chunks (§25.4a item 11), so the least recently used departments are dropped past
	/// <see cref="KnowledgeConfig.CorpusCacheDepartments"/>.
	/// </summary>
	public sealed class KnowledgeCorpusCache
	{
		private sealed class Entry
		{
			public long Revision { get; init; }
			public IReadOnlyList<PreparedKnowledgeChunk> Chunks { get; init; }
			public long LastUsedTicks { get; set; }
		}

		private readonly ConcurrentDictionary<int, Entry> _entries = new ConcurrentDictionary<int, Entry>();

		public async Task<IReadOnlyList<PreparedKnowledgeChunk>> GetAsync(int departmentId, long revision, Func<Task<List<KnowledgeChunk>>> load)
		{
			if (_entries.TryGetValue(departmentId, out var entry) && entry.Revision == revision)
			{
				entry.LastUsedTicks = DateTime.UtcNow.Ticks;
				return entry.Chunks;
			}

			var chunks = (await load() ?? new List<KnowledgeChunk>()).Where(c => c != null && c.DepartmentId == departmentId).Select(PreparedKnowledgeChunk.From).ToList();
			_entries[departmentId] = new Entry { Revision = revision, Chunks = chunks, LastUsedTicks = DateTime.UtcNow.Ticks };

			var limit = Math.Max(2, KnowledgeConfig.CorpusCacheDepartments);
			if (_entries.Count > limit)
				foreach (var stale in _entries.OrderBy(e => e.Value.LastUsedTicks).Take(_entries.Count - limit).Select(e => e.Key).ToList())
					_entries.TryRemove(stale, out _);

			return chunks;
		}

		public void Clear() => _entries.Clear();
	}

	/// <summary>
	/// Brute-force exact retrieval inside Resgrid.Search (decision §25.4a item 11): the reader's department corpus and the Academy system
	/// corpus are filtered by <see cref="KnowledgeAccessFilter"/> and effective dates first, and only the survivors are scored (exact cosine
	/// plus BM25, fused and diversity-capped by <see cref="KnowledgeRanker"/>). No vector service, no approximate index.
	/// </summary>
	public sealed class KnowledgeRetriever : IKnowledgeRetriever
	{
		private readonly IKnowledgeIndexRepository _repository;
		private readonly KnowledgeCorpusCache _cache;

		public KnowledgeRetriever(IKnowledgeIndexRepository repository, KnowledgeCorpusCache cache)
		{
			_repository = repository;
			_cache = cache;
		}

		public async Task<KnowledgeRetrievalResult> RetrieveAsync(KnowledgeRetrievalRequest request, CancellationToken cancellationToken = default)
		{
			var scope = request?.Scope;
			if (scope == null || !scope.IsCurrentMember || scope.DepartmentId <= 0 || string.IsNullOrWhiteSpace(request.Question))
				return new KnowledgeRetrievalResult { Mode = KnowledgeRetrievalModes.KeywordOnly };

			IEnumerable<PreparedKnowledgeChunk> pool;
			var now = DateTime.UtcNow;
			var checkDates = request.Chunks == null;
			if (request.Chunks != null)
			{
				pool = request.Chunks.Where(c => c != null).Select(PreparedKnowledgeChunk.From).ToList();
			}
			else
			{
				var revisions = await _repository.GetRevisionsAsync(new[] { scope.DepartmentId, 0 }, cancellationToken);
				var department = await _cache.GetAsync(scope.DepartmentId, revisions.TryGetValue(scope.DepartmentId, out var r) ? r : 0,
					() => _repository.GetCorpusAsync(scope.DepartmentId, cancellationToken));
				var system = await _cache.GetAsync(0, revisions.TryGetValue(0, out var s) ? s : 0, () => _repository.GetCorpusAsync(0, cancellationToken));
				pool = department.Concat(system);
			}

			// Department, audience and dates first: nothing the reader cannot open is ever scored. Then one language per item: an approved
			// translation in the asker's language replaces its source (round 5F).
			var candidates = KnowledgeLocaleVariants.Choose(pool
				.Where(p => KnowledgeAccessFilter.CanRead(scope, p.Chunk))
				.Where(p => !checkDates || InEffect(p.Chunk, now))
				.Where(p => InScope(p.Chunk, request.OnlyKind, request.OnlyItemId)), p => p.Chunk, request.Locale);

			return KnowledgeRanker.Rank(candidates, request.Question, new KnowledgeRanker.Options
			{
				QueryVector = request.QueryVector,
				QueryModel = request.QueryModel,
				TopK = request.TopK > 0 ? request.TopK : KnowledgeConfig.TopK,
				MaxPerItem = request.MaxPerItem > 0 ? request.MaxPerItem : KnowledgeConfig.MaxChunksPerItem,
				MinCosine = KnowledgeConfig.MinCosine,
				MinCoverage = KnowledgeConfig.MinKeywordCoverage
			});
		}

		public static bool InEffect(KnowledgeChunk chunk, DateTime utcNow) =>
			chunk != null && (!chunk.EffectiveOn.HasValue || chunk.EffectiveOn.Value <= utcNow) && (!chunk.ExpiresOn.HasValue || chunk.ExpiresOn.Value > utcNow);

		/// <summary>An item page's Ask: the item's own chunks and its files' pages.</summary>
		public static bool InScope(KnowledgeChunk chunk, int? onlyKind, string onlyItemId)
		{
			if (!onlyKind.HasValue)
				return true;
			// A3 "How do I…?": the whole Academy corpus (the only kind that may be scoped without an item).
			if (string.IsNullOrEmpty(onlyItemId) && onlyKind.Value == (int)KnowledgeSourceKinds.Academy)
				return chunk.SourceKind == (int)KnowledgeSourceKinds.Academy;
			if (!string.Equals(chunk.ItemId, onlyItemId, StringComparison.Ordinal))
				return false;
			var kind = (KnowledgeSourceKinds)onlyKind.Value;
			var chunkKind = (KnowledgeSourceKinds)chunk.SourceKind;
			return chunkKind == kind ||
				kind == KnowledgeSourceKinds.Protocol && chunkKind == KnowledgeSourceKinds.ProtocolFile ||
				kind == KnowledgeSourceKinds.Training && chunkKind == KnowledgeSourceKinds.TrainingFile;
		}
	}

	/// <summary>
	/// <see cref="IEmbeddingProvider"/> over TEI bge-m3 (KnowledgeConfig). The endpoint must be the exact operator embed route; every call is
	/// bounded by its deadline; any failure is an <see cref="EmbeddingUnavailableException"/>.
	/// </summary>
	public sealed class TeiEmbeddingProvider : IEmbeddingProvider
	{
		public bool IsConfigured => !string.IsNullOrWhiteSpace(KnowledgeConfig.EmbeddingsEndpoint) && !string.IsNullOrWhiteSpace(KnowledgeConfig.EmbeddingsModel) &&
			KnowledgeConfig.EmbeddingDimensions > 0;

		public string ModelId => KnowledgeConfig.EmbeddingsModel;

		public int Dimensions => KnowledgeConfig.EmbeddingDimensions;

		public async Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, TimeSpan deadline, CancellationToken cancellationToken)
		{
			if (texts == null || texts.Count == 0)
				return Array.Empty<float[]>();
			if (!IsConfigured)
				throw new EmbeddingUnavailableException("Embeddings are not configured.");

			Uri endpoint;
			try
			{
				endpoint = OperatorEndpointPolicy.ValidateUri(KnowledgeConfig.EmbeddingsEndpoint, KnowledgeConfig.EmbeddingsAllowPrivateEndpoint, OperatorEndpointPolicy.EmbedPath);
			}
			catch (LlmUnavailableException ex)
			{
				throw new EmbeddingUnavailableException("The embeddings endpoint is not an allowed operator endpoint.", ex);
			}

			using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			bounded.CancelAfter(deadline <= TimeSpan.Zero ? TimeSpan.FromSeconds(1) : deadline);
			var client = new TeiEmbeddingClient(OperatorEndpointPolicy.GetSharedClient(endpoint, KnowledgeConfig.EmbeddingsAllowPrivateEndpoint), endpoint,
				KnowledgeConfig.EmbeddingsApiKey, KnowledgeConfig.EmbeddingDimensions);
			var batch = Math.Clamp(KnowledgeConfig.EmbeddingsBatchSize, 1, 64);
			var vectors = new List<float[]>(texts.Count);
			try
			{
				for (var i = 0; i < texts.Count; i += batch)
					vectors.AddRange(await client.EmbedAsync(texts.Skip(i).Take(batch).Select(t => t ?? string.Empty).ToList(), bounded.Token));
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex) when (ex is LlmUnavailableException || ex is OperationCanceledException || ex is System.Net.Http.HttpRequestException ||
				ex is System.Text.Json.JsonException || ex is System.IO.IOException || ex is OverflowException)
			{
				throw new EmbeddingUnavailableException("The embeddings service did not answer in time or answered with an unexpected shape.", ex);
			}

			return vectors;
		}
	}
}
