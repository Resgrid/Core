using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services.Knowledge;
using Resgrid.Workers.Framework.Logic;

namespace Resgrid.Tests.Knowledge
{
	/// <summary>
	/// K1 index: per-item reindex from the queue, the nightly repair pass (orphans removed, missing vectors embedded), unchanged items never
	/// rewritten, an embeddings outage leaving chunks keyword-only, and worker 89 starting from due work with one gate call per department.
	/// </summary>
	[TestFixture]
	public class KnowledgeIndexTests
	{
		private const int DepartmentId = 7;
		private const string Model = "BAAI/bge-m3";

		private sealed class FakeIndex : IKnowledgeIndexRepository
		{
			public readonly Dictionary<(int, int, string), List<KnowledgeChunk>> Items = new Dictionary<(int, int, string), List<KnowledgeChunk>>();
			public readonly List<KnowledgeIndexRequest> Requests = new List<KnowledgeIndexRequest>();
			public readonly Dictionary<int, KnowledgeIndexState> States = new Dictionary<int, KnowledgeIndexState>();
			public int Replacements;
			public int NextId = 1;

			public Task<List<KnowledgeChunk>> GetCorpusAsync(int departmentId, CancellationToken cancellationToken = default) =>
				Task.FromResult(Items.Where(i => i.Key.Item1 == departmentId).SelectMany(i => i.Value).ToList());

			public Task<List<KnowledgeChunk>> GetItemChunksAsync(int departmentId, int sourceKind, string itemId, CancellationToken cancellationToken = default) =>
				Task.FromResult(Items.TryGetValue((departmentId, sourceKind, itemId), out var list) ? list.ToList() : new List<KnowledgeChunk>());

			public Task<List<KnowledgeIndexedItem>> GetIndexedItemsAsync(int departmentId, CancellationToken cancellationToken = default) =>
				Task.FromResult(Items.Where(i => i.Key.Item1 == departmentId && i.Value.Count > 0).Select(i => new KnowledgeIndexedItem { SourceKind = i.Key.Item2, ItemId = i.Key.Item3 }).ToList());

			public Task ReplaceItemChunksAsync(int departmentId, int sourceKind, string itemId, IReadOnlyList<KnowledgeChunk> chunks, CancellationToken cancellationToken = default)
			{
				Replacements++;
				foreach (var c in chunks)
					c.KnowledgeChunkId = NextId++;
				Items[(departmentId, sourceKind, itemId)] = chunks.ToList();
				return Task.CompletedTask;
			}

			public Task<int> DeleteItemAsync(int departmentId, int sourceKind, string itemId, CancellationToken cancellationToken = default) =>
				Task.FromResult(Items.Remove((departmentId, sourceKind, itemId)) ? 1 : 0);

			public Task<List<KnowledgeChunk>> GetChunksToEmbedAsync(int departmentId, string model, int take, CancellationToken cancellationToken = default) =>
				Task.FromResult(Items.Where(i => i.Key.Item1 == departmentId).SelectMany(i => i.Value).Where(c => c.Embedding == null || c.EmbeddingModel != model).Take(take).ToList());

			public Task<int> SaveEmbeddingsAsync(int departmentId, IReadOnlyList<KnowledgeChunk> chunks, CancellationToken cancellationToken = default) =>
				Task.FromResult(chunks.Count(c => c.Embedding != null));

			public Task<Dictionary<int, long>> GetRevisionsAsync(IReadOnlyCollection<int> departmentIds, CancellationToken cancellationToken = default) =>
				Task.FromResult(departmentIds.ToDictionary(d => d, _ => 0L));

			public Task EnqueueAsync(int departmentId, int sourceKind, string itemId, DateTime utcNow, CancellationToken cancellationToken = default)
			{
				var existing = Requests.FirstOrDefault(r => r.DepartmentId == departmentId && r.SourceKind == sourceKind && r.ItemId == itemId);
				if (existing != null)
				{
					existing.RequestedOn = utcNow;
					existing.Revision++;
				}
				else
					Requests.Add(new KnowledgeIndexRequest { KnowledgeIndexRequestId = Requests.Count + 1, DepartmentId = departmentId, SourceKind = sourceKind, ItemId = itemId, RequestedOn = utcNow });
				return Task.CompletedTask;
			}

			public Task<List<int>> GetDueDepartmentsAsync(DateTime utcNow, int take, CancellationToken cancellationToken = default) =>
				Task.FromResult(Requests.Select(r => r.DepartmentId).Distinct().Take(take).ToList());

			public Task<List<KnowledgeIndexRequest>> GetRequestsAsync(int departmentId, DateTime utcNow, int take, CancellationToken cancellationToken = default) =>
				Task.FromResult(Requests.Where(r => r.DepartmentId == departmentId && r.RequestedOn <= utcNow).Take(take).ToList());

			public Task DeleteRequestsAsync(IReadOnlyCollection<KnowledgeIndexRequest> requests, CancellationToken cancellationToken = default)
			{
				foreach (var r in requests)
					Requests.Remove(r);
				return Task.CompletedTask;
			}

			public Task PostponeRequestAsync(KnowledgeIndexRequest request, DateTime untilUtc, CancellationToken cancellationToken = default)
			{
				request.RequestedOn = untilUtc;
				request.Attempts++;
				return Task.CompletedTask;
			}

			public Task DeleteDepartmentRequestsAsync(int departmentId, CancellationToken cancellationToken = default)
			{
				Requests.RemoveAll(r => r.DepartmentId == departmentId);
				return Task.CompletedTask;
			}

			public Task<KnowledgeIndexState> GetStateAsync(int departmentId, CancellationToken cancellationToken = default) =>
				Task.FromResult(States.TryGetValue(departmentId, out var s) ? s : null);

			public Task SaveStateAsync(KnowledgeIndexState state, CancellationToken cancellationToken = default)
			{
				States[state.DepartmentId] = state;
				return Task.CompletedTask;
			}

			public Task<List<DispatchProtocol>> GetProtocolHeadersAsync(int departmentId, CancellationToken cancellationToken = default) => Task.FromResult(new List<DispatchProtocol>());

			public Task<List<KnowledgeDocumentHeader>> GetDocumentHeadersAsync(int departmentId, CancellationToken cancellationToken = default) => Task.FromResult(new List<KnowledgeDocumentHeader>());
		}

		/// <summary>A source whose items and text the test controls.</summary>
		private sealed class FakeSource : IKnowledgeCorpusSource
		{
			public readonly Dictionary<string, string[]> Texts = new Dictionary<string, string[]>();
			public IReadOnlyCollection<KnowledgeSourceKinds> Kinds => new[] { KnowledgeSourceKinds.Protocol };

			public Task<IReadOnlyList<string>> ListItemIdsAsync(int departmentId, KnowledgeSourceKinds kind, CancellationToken cancellationToken = default) =>
				Task.FromResult<IReadOnlyList<string>>(Texts.Keys.ToList());

			public Task<KnowledgeItemContent> BuildAsync(int departmentId, KnowledgeSourceKinds kind, string itemId, CancellationToken cancellationToken = default) =>
				Task.FromResult(Texts.TryGetValue(itemId, out var texts)
					? new KnowledgeItemContent
					{
						DepartmentId = departmentId, Kind = kind, ItemId = itemId,
						Chunks = texts.Select((t, i) => new KnowledgeChunk { Title = "P" + itemId, Anchor = "p-" + i, Text = t, VersionId = "1" }).ToList()
					}
					: null);

			public Task AddReadableAsync(KnowledgeReader reader, KnowledgeReaderScope scope, CancellationToken cancellationToken = default) => Task.CompletedTask;
		}

		private FakeIndex _index;
		private FakeSource _source;
		private Mock<IEmbeddingProvider> _embeddings;
		private KnowledgeIndexService _service;

		[SetUp]
		public void SetUp()
		{
			_index = new FakeIndex();
			_source = new FakeSource();
			_embeddings = new Mock<IEmbeddingProvider>();
			_embeddings.SetupGet(e => e.IsConfigured).Returns(true);
			_embeddings.SetupGet(e => e.ModelId).Returns(Model);
			_embeddings.Setup(e => e.EmbedAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((IReadOnlyList<string> texts, TimeSpan _, CancellationToken __) => texts.Select(t => new[] { (float)t.Length, 1f }).ToList());
			_service = new KnowledgeIndexService(_index, new IKnowledgeCorpusSource[] { _source }, _embeddings.Object, Mock.Of<IEventAggregator>());
		}

		[Test]
		public async Task Reindexing_an_item_writes_its_chunks_with_hashes_and_vectors_and_an_unchanged_item_is_not_rewritten()
		{
			_source.Texts["1"] = new[] { "Stage uphill.", "Wear gloves." };

			(await _service.ReindexItemAsync(DepartmentId, KnowledgeSourceKinds.Protocol, "1")).Should().Be(2);
			var chunks = _index.Items[(DepartmentId, 0, "1")];
			chunks.Select(c => c.Ordinal).Should().Equal(0, 1);
			chunks.Should().OnlyContain(c => c.TextHash.Length == 64 && c.Embedding != null && c.EmbeddingModel == Model && c.DepartmentId == DepartmentId);

			(await _service.ReindexItemAsync(DepartmentId, KnowledgeSourceKinds.Protocol, "1")).Should().Be(0);
			_index.Replacements.Should().Be(1);
			_embeddings.Verify(e => e.EmbedAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task A_changed_item_keeps_the_vectors_of_its_unchanged_chunks()
		{
			_source.Texts["1"] = new[] { "Stage uphill.", "Wear gloves." };
			await _service.ReindexItemAsync(DepartmentId, KnowledgeSourceKinds.Protocol, "1");
			_source.Texts["1"] = new[] { "Stage uphill.", "Wear gloves and a mask." };

			await _service.ReindexItemAsync(DepartmentId, KnowledgeSourceKinds.Protocol, "1");

			_embeddings.Verify(e => e.EmbedAsync(It.Is<IReadOnlyList<string>>(t => t.Count == 1 && t[0].Contains("mask")), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task An_item_that_is_no_longer_indexable_is_removed()
		{
			_source.Texts["1"] = new[] { "Stage uphill." };
			await _service.ReindexItemAsync(DepartmentId, KnowledgeSourceKinds.Protocol, "1");

			// Retired, excluded from AI or deleted: the source no longer builds it.
			_source.Texts.Remove("1");
			await _service.ReindexItemAsync(DepartmentId, KnowledgeSourceKinds.Protocol, "1");

			_index.Items.ContainsKey((DepartmentId, 0, "1")).Should().BeFalse();
		}

		[Test]
		public async Task A_kind_no_source_serves_is_never_indexed()
		{
			_index.Items[(DepartmentId, (int)KnowledgeSourceKinds.Course, "9")] = new List<KnowledgeChunk> { new KnowledgeChunk { Text = "x" } };

			(await _service.ReindexItemAsync(DepartmentId, KnowledgeSourceKinds.Course, "9")).Should().Be(0);

			_index.Items.ContainsKey((DepartmentId, (int)KnowledgeSourceKinds.Course, "9")).Should().BeFalse();
		}

		[Test]
		public async Task An_embeddings_outage_still_writes_the_text_keyword_only_and_the_repair_pass_embeds_it_later()
		{
			_source.Texts["1"] = new[] { "Stage uphill." };
			_embeddings.Setup(e => e.EmbedAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
				.ThrowsAsync(new EmbeddingUnavailableException("down"));
			await _index.EnqueueAsync(DepartmentId, 0, "1", DateTime.UtcNow.AddMinutes(-1));
			_index.States[DepartmentId] = new KnowledgeIndexState { DepartmentId = DepartmentId, Active = true, NextRepairOn = DateTime.UtcNow.AddDays(1) };

			var first = await _service.ProcessDepartmentAsync(DepartmentId, DateTime.UtcNow);

			first.EmbeddingsUnavailable.Should().BeTrue();
			_index.Items[(DepartmentId, 0, "1")].Single().Embedding.Should().BeNull();
			_index.States[DepartmentId].LastError.Should().NotBeNullOrEmpty();

			_embeddings.Setup(e => e.EmbedAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((IReadOnlyList<string> texts, TimeSpan _, CancellationToken __) => texts.Select(t => new[] { 1f, 2f }).ToList());
			var second = await _service.ProcessDepartmentAsync(DepartmentId, DateTime.UtcNow);

			second.ChunksEmbedded.Should().Be(1);
			_index.Items[(DepartmentId, 0, "1")].Single().Embedding.Should().NotBeNull();
		}

		[Test]
		public async Task The_queue_rebuilds_only_the_requested_items_until_the_repair_pass_is_due()
		{
			_source.Texts["1"] = new[] { "One." };
			_source.Texts["2"] = new[] { "Two." };
			_index.States[DepartmentId] = new KnowledgeIndexState { DepartmentId = DepartmentId, Active = true, NextRepairOn = DateTime.UtcNow.AddHours(5) };
			await _index.EnqueueAsync(DepartmentId, 0, "2", DateTime.UtcNow.AddMinutes(-1));

			var result = await _service.ProcessDepartmentAsync(DepartmentId, DateTime.UtcNow);

			result.Repairs.Should().Be(0);
			_index.Items.Keys.Should().BeEquivalentTo(new[] { (DepartmentId, 0, "2") });
			_index.Requests.Should().BeEmpty();
		}

		[Test]
		public async Task The_repair_pass_reconciles_the_department_removes_orphans_and_schedules_the_next_night()
		{
			_source.Texts["1"] = new[] { "One." };
			_index.Items[(DepartmentId, 0, "old")] = new List<KnowledgeChunk> { new KnowledgeChunk { Text = "Retired text." } };
			var now = new DateTime(2026, 11, 25, 14, 0, 0, DateTimeKind.Utc);

			var result = await _service.ProcessDepartmentAsync(DepartmentId, now);

			result.Repairs.Should().Be(1);
			_index.Items.Keys.Should().BeEquivalentTo(new[] { (DepartmentId, 0, "1") });
			var state = _index.States[DepartmentId];
			state.Active.Should().BeTrue();
			state.LastRepairOn.Should().Be(now);
			state.NextRepairOn.Should().Be(KnowledgeIndexService.NextRepair(now, DepartmentId));
			state.NextRepairOn.Value.Should().BeAfter(now).And.BeBefore(now.AddDays(1).AddHours(2));
		}

		[Test]
		public async Task A_whole_department_rebuild_request_runs_the_reconcile_and_clears_the_queue()
		{
			_source.Texts["1"] = new[] { "One." };
			_index.States[DepartmentId] = new KnowledgeIndexState { DepartmentId = DepartmentId, Active = true, NextRepairOn = DateTime.UtcNow.AddHours(5) };
			await _service.RequestRebuildAsync(DepartmentId, "admin");
			_index.Requests.Single().SourceKind.Should().Be(KnowledgeIndexRequest.DepartmentRebuild);

			var result = await _service.ProcessDepartmentAsync(DepartmentId, DateTime.UtcNow.AddMinutes(1));

			result.Repairs.Should().Be(1);
			_index.Items.Should().ContainKey((DepartmentId, 0, "1"));
			_index.Requests.Should().BeEmpty();
		}

		[Test]
		public async Task Deactivating_drops_the_queue_and_stops_the_repair_pass()
		{
			_index.States[DepartmentId] = new KnowledgeIndexState { DepartmentId = DepartmentId, Active = true, NextRepairOn = DateTime.UtcNow };
			await _index.EnqueueAsync(DepartmentId, 0, "1", DateTime.UtcNow);

			await _service.DeactivateAsync(DepartmentId);

			_index.Requests.Should().BeEmpty();
			_index.States[DepartmentId].Active.Should().BeFalse();
			_index.States[DepartmentId].NextRepairOn.Should().BeNull();
		}

		[Test]
		public async Task The_queue_writer_never_fails_its_caller()
		{
			var repository = new Mock<IKnowledgeIndexRepository>();
			repository.Setup(r => r.EnqueueAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
				.ThrowsAsync(new InvalidOperationException("database down"));
			var queue = new KnowledgeIndexQueue(repository.Object);

			Func<Task> enqueue = () => queue.EnqueueAsync(DepartmentId, KnowledgeSourceKinds.Protocol, "1");

			await enqueue.Should().NotThrowAsync();
		}

		#region Worker 89

		[Test]
		public async Task Worker_89_starts_from_due_departments_and_asks_the_gate_once_per_department()
		{
			var index = new Mock<IKnowledgeIndexService>();
			index.Setup(i => i.GetStateAsync(0, It.IsAny<CancellationToken>())).ReturnsAsync(new KnowledgeIndexState { DepartmentId = 0 });
			index.Setup(i => i.GetDueDepartmentsAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<int> { 3, 0, 3, 5 });
			index.Setup(i => i.ProcessDepartmentAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(new KnowledgeIndexRunResult { ItemsIndexed = 1 });
			var asked = new List<int>();
			Task<bool> Gate(int departmentId) { asked.Add(departmentId); return Task.FromResult(departmentId == 3); }

			var summary = await KnowledgeIndexLogic.Process(index.Object, Gate, DateTime.UtcNow);

			asked.Should().Equal(3, 5);
			summary.GateChecks.Should().Be(2);
			summary.NotEntitled.Should().Be(1);
			index.Verify(i => i.ProcessDepartmentAsync(3, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
			index.Verify(i => i.ProcessDepartmentAsync(0, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once, "the Academy system corpus needs no entitlement");
			index.Verify(i => i.ProcessDepartmentAsync(5, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
			index.Verify(i => i.DeactivateAsync(5, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task Worker_89_with_no_due_work_touches_no_department_and_queues_the_system_corpus_once()
		{
			var index = new Mock<IKnowledgeIndexService>();
			index.Setup(i => i.GetStateAsync(0, It.IsAny<CancellationToken>())).ReturnsAsync((KnowledgeIndexState)null);
			index.Setup(i => i.GetDueDepartmentsAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<int>());
			var gateCalls = 0;

			var summary = await KnowledgeIndexLogic.Process(index.Object, _ => { gateCalls++; return Task.FromResult(true); }, DateTime.UtcNow);

			gateCalls.Should().Be(0);
			summary.Departments.Should().Be(0);
			index.Verify(i => i.RequestRebuildAsync(0, null, It.IsAny<CancellationToken>()), Times.Once);
			index.Verify(i => i.ProcessDepartmentAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Worker_89_keeps_going_when_one_department_fails()
		{
			var index = new Mock<IKnowledgeIndexService>();
			index.Setup(i => i.GetStateAsync(0, It.IsAny<CancellationToken>())).ReturnsAsync(new KnowledgeIndexState());
			index.Setup(i => i.GetDueDepartmentsAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<int> { 3, 4 });
			index.Setup(i => i.ProcessDepartmentAsync(3, It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("boom"));
			index.Setup(i => i.ProcessDepartmentAsync(4, It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(new KnowledgeIndexRunResult());

			var summary = await KnowledgeIndexLogic.Process(index.Object, _ => Task.FromResult(true), DateTime.UtcNow);

			summary.Failures.Should().Be(1);
			summary.Departments.Should().Be(1);
		}

		#endregion Worker 89
	}
}
