using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.Knowledge
{
	/// <summary>
	/// The reindex queue the content services write to on publish and retire (ProtocolLibraryService, TrainingService). It depends on the
	/// repository only, so the services that call it never form a construction cycle with the corpus sources; and it never fails its caller.
	/// </summary>
	public sealed class KnowledgeIndexQueue : IKnowledgeIndexQueue
	{
		private readonly IKnowledgeIndexRepository _repository;

		public KnowledgeIndexQueue(IKnowledgeIndexRepository repository)
		{
			_repository = repository;
		}

		public async Task EnqueueAsync(int departmentId, KnowledgeSourceKinds kind, string itemId, CancellationToken cancellationToken = default)
		{
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(itemId) || !Enum.IsDefined(typeof(KnowledgeSourceKinds), kind))
				return;
			try
			{
				await _repository.EnqueueAsync(departmentId, (int)kind, itemId.Trim(), DateTime.UtcNow, cancellationToken);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				// The nightly repair pass re-reads every item, so a lost request only delays the change.
				Logging.LogException(ex, $"Knowledge reindex of {kind} {itemId} could not be queued for department {departmentId}.");
			}
		}
	}

	/// <summary>
	/// The knowledge index (K1, §16.2): per-item reindex from the queue, and the nightly repair pass that re-checks every indexable item,
	/// removes chunks whose item is no longer indexable (drafts never are; retired, excluded, deleted and archived items are removed) and
	/// embeds chunks an outage left without a vector. Only worker 89 calls the embeddings service through here; an outage leaves chunks
	/// keyword-only until the next pass. An unchanged item is never rewritten, and an unchanged chunk keeps its vector.
	/// </summary>
	public class KnowledgeIndexService : IKnowledgeIndexService
	{
		public const int EmbedBatchPerPass = 256;

		/// <summary>Batches of missing vectors one department pass embeds at most (the rest wait for the next due pass).</summary>
		public const int EmbedBatchesPerPass = 20;

		private readonly IKnowledgeIndexRepository _repository;
		private readonly IEnumerable<IKnowledgeCorpusSource> _sources;
		private readonly IEmbeddingProvider _embeddings;
		private readonly IEventAggregator _eventAggregator;

		public KnowledgeIndexService(IKnowledgeIndexRepository repository, IEnumerable<IKnowledgeCorpusSource> sources, IEmbeddingProvider embeddings,
			IEventAggregator eventAggregator)
		{
			_repository = repository;
			_sources = sources ?? Enumerable.Empty<IKnowledgeCorpusSource>();
			_embeddings = embeddings;
			_eventAggregator = eventAggregator;
		}

		private IKnowledgeCorpusSource SourceFor(KnowledgeSourceKinds kind) => _sources.FirstOrDefault(s => s.Kinds != null && s.Kinds.Contains(kind));

		public Task EnqueueAsync(int departmentId, KnowledgeSourceKinds kind, string itemId, CancellationToken cancellationToken = default) =>
			new KnowledgeIndexQueue(_repository).EnqueueAsync(departmentId, kind, itemId, cancellationToken);

		public async Task RequestRebuildAsync(int departmentId, string userId, CancellationToken cancellationToken = default)
		{
			if (departmentId < 0)
				return;
			await _repository.EnqueueAsync(departmentId, KnowledgeIndexRequest.DepartmentRebuild, string.Empty, DateTime.UtcNow, cancellationToken);
			if (!string.IsNullOrWhiteSpace(userId) && departmentId > 0)
				_eventAggregator?.SendMessage<AuditEvent>(new AuditEvent
				{
					DepartmentId = departmentId,
					UserId = userId,
					Type = AuditLogTypes.KnowledgeIndexRebuildRequested,
					Successful = true,
					ServerName = Environment.MachineName
				});
		}

		public Task<List<int>> GetDueDepartmentsAsync(DateTime utcNow, int take, CancellationToken cancellationToken = default) =>
			_repository.GetDueDepartmentsAsync(utcNow, take, cancellationToken);

		public Task<KnowledgeIndexState> GetStateAsync(int departmentId, CancellationToken cancellationToken = default) =>
			_repository.GetStateAsync(departmentId, cancellationToken);

		/// <summary>The next repair pass: the configured UTC hour of the next day, spread over that hour by department.</summary>
		public static DateTime NextRepair(DateTime utcNow, int departmentId)
		{
			var hour = Math.Clamp(KnowledgeConfig.RepairHourUtc, 0, 23);
			var day = utcNow.Date.AddDays(utcNow.Hour < hour ? 0 : 1);
			return DateTime.SpecifyKind(day.AddHours(hour).AddMinutes(Math.Abs(departmentId % 60)), DateTimeKind.Utc);
		}

		public async Task<KnowledgeIndexRunResult> ProcessDepartmentAsync(int departmentId, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var result = new KnowledgeIndexRunResult();
			var state = await _repository.GetStateAsync(departmentId, cancellationToken) ?? new KnowledgeIndexState { DepartmentId = departmentId };
			var requests = await _repository.GetRequestsAsync(departmentId, utcNow, Math.Max(1, KnowledgeConfig.WorkerItemsPerDepartment), cancellationToken) ?? new List<KnowledgeIndexRequest>();

			var rebuild = requests.Any(r => r.SourceKind == KnowledgeIndexRequest.DepartmentRebuild);
			var repairDue = !state.NextRepairOn.HasValue || state.NextRepairOn.Value <= utcNow;
			if (rebuild || repairDue)
			{
				// The whole department: every indexable item re-checked, orphans removed. It covers every queued item too.
				await ReconcileAsync(departmentId, result, cancellationToken);
				await _repository.DeleteRequestsAsync(requests, cancellationToken);
				state.LastRepairOn = utcNow;
				state.NextRepairOn = NextRepair(utcNow, departmentId);
				result.Repairs++;
			}
			else
			{
				var done = new List<KnowledgeIndexRequest>();
				foreach (var request in requests)
				{
					cancellationToken.ThrowIfCancellationRequested();
					try
					{
						if (Enum.IsDefined(typeof(KnowledgeSourceKinds), request.SourceKind))
							await ReindexAsync(departmentId, (KnowledgeSourceKinds)request.SourceKind, request.ItemId, result, cancellationToken);
						done.Add(request);
					}
					catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
					{
						throw;
					}
					catch (Exception ex)
					{
						result.Failures++;
						Logging.LogException(ex, $"Knowledge reindex of {request.SourceKind}:{request.ItemId} failed for department {departmentId}.");
						await _repository.PostponeRequestAsync(request, utcNow.AddMinutes(Math.Min(60, 1 << Math.Min(6, request.Attempts + 1))), cancellationToken);
					}
				}

				await _repository.DeleteRequestsAsync(done, cancellationToken);
			}

			// Chunks an outage or a model change left without a vector of the current model.
			await EmbedMissingAsync(departmentId, result, cancellationToken);

			state.DepartmentId = departmentId;
			state.Active = true;
			state.EmbeddingModel = _embeddings?.IsConfigured == true ? _embeddings.ModelId : state.EmbeddingModel;
			state.LastError = result.Failures > 0 ? $"{result.Failures} item(s) failed; retried with backoff." : (result.EmbeddingsUnavailable ? "Embeddings unavailable; keyword retrieval only until the next pass." : null);
			await _repository.SaveStateAsync(state, cancellationToken);
			return result;
		}

		public async Task DeactivateAsync(int departmentId, CancellationToken cancellationToken = default)
		{
			await _repository.DeleteDepartmentRequestsAsync(departmentId, cancellationToken);
			var state = await _repository.GetStateAsync(departmentId, cancellationToken);
			if (state == null)
				return;
			state.Active = false;
			state.NextRepairOn = null;
			await _repository.SaveStateAsync(state, cancellationToken);
		}

		private async Task ReconcileAsync(int departmentId, KnowledgeIndexRunResult result, CancellationToken cancellationToken)
		{
			var wanted = new HashSet<(int, string)>();
			foreach (var kind in Enum.GetValues(typeof(KnowledgeSourceKinds)).Cast<KnowledgeSourceKinds>())
			{
				var source = SourceFor(kind);
				if (source == null)
					continue;
				foreach (var itemId in await source.ListItemIdsAsync(departmentId, kind, cancellationToken) ?? Array.Empty<string>())
				{
					cancellationToken.ThrowIfCancellationRequested();
					wanted.Add(((int)kind, itemId));
					try
					{
						await ReindexAsync(departmentId, kind, itemId, result, cancellationToken);
					}
					catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
					{
						throw;
					}
					catch (Exception ex)
					{
						result.Failures++;
						Logging.LogException(ex, $"Knowledge repair of {kind}:{itemId} failed for department {departmentId}.");
					}
				}
			}

			foreach (var orphan in (await _repository.GetIndexedItemsAsync(departmentId, cancellationToken) ?? new List<KnowledgeIndexedItem>())
				.Where(i => !wanted.Contains((i.SourceKind, i.ItemId))))
			{
				if (await _repository.DeleteItemAsync(departmentId, orphan.SourceKind, orphan.ItemId, cancellationToken) > 0)
					result.ItemsRemoved++;
			}
		}

		public async Task<int> ReindexItemAsync(int departmentId, KnowledgeSourceKinds kind, string itemId, CancellationToken cancellationToken = default)
		{
			var result = new KnowledgeIndexRunResult();
			return await ReindexAsync(departmentId, kind, itemId, result, cancellationToken);
		}

		private async Task<int> ReindexAsync(int departmentId, KnowledgeSourceKinds kind, string itemId, KnowledgeIndexRunResult result, CancellationToken cancellationToken)
		{
			if (string.IsNullOrWhiteSpace(itemId))
				return 0;

			var source = SourceFor(kind);
			var content = source == null ? null : await source.BuildAsync(departmentId, kind, itemId, cancellationToken);
			if (content == null)
			{
				// Not indexable (any more): draft, retired, excluded, archived, deleted, or a kind no source serves.
				if (await _repository.DeleteItemAsync(departmentId, (int)kind, itemId, cancellationToken) > 0)
					result.ItemsRemoved++;
				return 0;
			}

			var now = DateTime.UtcNow;
			var chunks = (content.Chunks ?? new List<KnowledgeChunk>()).Where(c => c != null && !string.IsNullOrWhiteSpace(c.Text)).ToList();
			for (var i = 0; i < chunks.Count; i++)
			{
				var chunk = chunks[i];
				chunk.DepartmentId = departmentId;
				chunk.SourceKind = (int)kind;
				chunk.ItemId = itemId;
				chunk.Ordinal = i;
				chunk.Title ??= string.Empty;
				chunk.TextHash = KnowledgeChunker.Hash(KnowledgeChunker.EmbeddingInput(chunk));
				chunk.IndexedOn = now;
				chunk.Embedding = null;
				chunk.EmbeddingModel = null;
				chunk.EmbeddedOn = null;
			}

			var existing = await _repository.GetItemChunksAsync(departmentId, (int)kind, itemId, cancellationToken) ?? new List<KnowledgeChunk>();
			if (SameContent(existing, chunks))
				return 0;

			// An unchanged chunk keeps its vector; only new or changed text is embedded.
			var model = _embeddings?.IsConfigured == true ? _embeddings.ModelId : null;
			var vectors = existing.Where(e => e.Embedding != null && model != null && string.Equals(e.EmbeddingModel, model, StringComparison.Ordinal))
				.GroupBy(e => e.TextHash, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
			foreach (var chunk in chunks)
			{
				if (vectors.TryGetValue(chunk.TextHash, out var kept))
				{
					chunk.Embedding = kept.Embedding;
					chunk.EmbeddingModel = kept.EmbeddingModel;
					chunk.EmbeddedOn = kept.EmbeddedOn;
				}
			}

			var missing = chunks.Where(c => c.Embedding == null).ToList();
			if (model != null && missing.Count > 0 && !result.EmbeddingsUnavailable)
				result.ChunksEmbedded += await EmbedAsync(missing, model, result, cancellationToken);

			await _repository.ReplaceItemChunksAsync(departmentId, (int)kind, itemId, chunks, cancellationToken);
			result.ItemsIndexed++;
			return chunks.Count;
		}

		/// <summary>Same chunks in the same order with the same text, source, access and dates: nothing to write.</summary>
		public static bool SameContent(IReadOnlyList<KnowledgeChunk> existing, IReadOnlyList<KnowledgeChunk> fresh)
		{
			if (existing == null || fresh == null || existing.Count != fresh.Count || existing.Count == 0)
				return false;
			var stored = existing.OrderBy(c => c.Ordinal).ToList();
			for (var i = 0; i < stored.Count; i++)
			{
				var a = stored[i];
				var b = fresh[i];
				if (a.Ordinal != b.Ordinal || !string.Equals(a.TextHash, b.TextHash, StringComparison.Ordinal) || !string.Equals(a.Anchor, b.Anchor, StringComparison.Ordinal) ||
					!string.Equals(a.VersionId, b.VersionId, StringComparison.Ordinal) || !string.Equals(a.VersionLabel, b.VersionLabel, StringComparison.Ordinal) ||
					!string.Equals(a.AudienceJson, b.AudienceJson, StringComparison.Ordinal) || a.AccessFlags != b.AccessFlags ||
					!string.Equals(a.Locale, b.Locale, StringComparison.Ordinal) || !SameInstant(a.EffectiveOn, b.EffectiveOn) || !SameInstant(a.ExpiresOn, b.ExpiresOn))
					return false;
			}
			return true;
		}

		// Stored timestamps lose sub-second precision on PostgreSQL; a second's difference is the same instant here.
		private static bool SameInstant(DateTime? a, DateTime? b) =>
			a.HasValue == b.HasValue && (!a.HasValue || Math.Abs((a.Value - b.Value).TotalSeconds) < 1);

		private async Task<int> EmbedAsync(List<KnowledgeChunk> chunks, string model, KnowledgeIndexRunResult result, CancellationToken cancellationToken)
		{
			try
			{
				var vectors = await _embeddings.EmbedAsync(chunks.Select(KnowledgeChunker.EmbeddingInput).ToList(),
					TimeSpan.FromSeconds(Math.Clamp(KnowledgeConfig.EmbeddingsTimeoutSeconds, 1, 300)), cancellationToken);
				if (vectors == null || vectors.Count != chunks.Count)
					throw new EmbeddingUnavailableException("The embeddings service returned the wrong number of vectors.");
				var now = DateTime.UtcNow;
				for (var i = 0; i < chunks.Count; i++)
				{
					chunks[i].Embedding = KnowledgeVectorCodec.Encode(vectors[i]);
					chunks[i].EmbeddingModel = model;
					chunks[i].EmbeddedOn = now;
				}
				return chunks.Count;
			}
			catch (EmbeddingUnavailableException ex)
			{
				// Keyword-only until the next pass; the chunk text is still written.
				result.EmbeddingsUnavailable = true;
				Logging.LogInfo($"Knowledge embeddings unavailable: {ex.Message}");
				return 0;
			}
		}

		private async Task EmbedMissingAsync(int departmentId, KnowledgeIndexRunResult result, CancellationToken cancellationToken)
		{
			if (_embeddings?.IsConfigured != true || result.EmbeddingsUnavailable)
				return;
			var model = _embeddings.ModelId;
			for (var batch = 0; batch < EmbedBatchesPerPass && !result.EmbeddingsUnavailable; batch++)
			{
				var pending = await _repository.GetChunksToEmbedAsync(departmentId, model, EmbedBatchPerPass, cancellationToken) ?? new List<KnowledgeChunk>();
				if (pending.Count == 0)
					return;
				var embedded = await EmbedAsync(pending, model, result, cancellationToken);
				if (embedded == 0)
					return;
				var saved = await _repository.SaveEmbeddingsAsync(departmentId, pending, cancellationToken);
				result.ChunksEmbedded += saved;
				// Nothing stored (every chunk changed meanwhile) or a short batch: nothing more to do this pass.
				if (saved == 0 || pending.Count < EmbedBatchPerPass)
					return;
			}
		}
	}
}
