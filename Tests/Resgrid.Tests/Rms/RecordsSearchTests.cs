using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Lucene.Net.Store;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Search;

namespace Resgrid.Tests.Rms
{
	/// <summary>
	/// The RMS-owned records index (plan section 5.10) end to end on an in-memory directory: department isolation,
	/// group scope inside the query, filters, text on safe fields only, narrative gating, deletes and health.
	/// </summary>
	[TestFixture]
	public class RecordsSearchTests
	{
		private LuceneRecordsIndexHost _host;
		private LuceneRecordsIndexer _indexer;
		private LuceneRecordsSearchService _search;
		private Mock<IRmsSearchWriteFence> _fence;

		[SetUp]
		public async Task SetUp()
		{
			SearchConfig.Enabled = true;
			_host = new LuceneRecordsIndexHost(new RAMDirectory(), ownsDirectory: true);
			_fence = new Mock<IRmsSearchWriteFence>();
			_fence.Setup(f => f.WithLiveSourceAsync(It.IsAny<RecordsSearchDocumentSource>(), It.IsAny<Func<RecordsSearchDocumentSource, int>>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((RecordsSearchDocumentSource source, Func<RecordsSearchDocumentSource, int> write, CancellationToken ct) => write(source));
			_indexer = new LuceneRecordsIndexer(_host, _fence.Object);
			_search = new LuceneRecordsSearchService(_host);

			await _indexer.IndexAsync(new[]
			{
				Source(Projection(1, "rec-1", "TRN-2026-0001", "Pump operations", RmsRecordState.Finalized, "a1", groups: "11", participants: "p1", occurred: new DateTime(2026, 3, 1), definition: RmsDefinitionKeys.Training, callId: 5, searchText: "Pump Ops PO-101"), narrative: "Hydrant flow test at station"),
				Source(Projection(1, "rec-2", "RUN-2026-0007", "Kitchen fire", RmsRecordState.Draft, "a2", groups: "12", participants: "", occurred: new DateTime(2026, 5, 1), definition: RmsDefinitionKeys.Run, callId: 9)),
				Source(Projection(1, "rec-3", null, "Ladder drill", RmsRecordState.Draft, "a3", groups: "12", participants: "", occurred: new DateTime(2025, 12, 1), definition: RmsDefinitionKeys.Training, draft: "D-7Q2MX")),
				Source(Projection(2, "rec-9", "TRN-2026-0001", "Pump operations", RmsRecordState.Finalized, "z1", groups: "11", participants: "", occurred: new DateTime(2026, 4, 1), definition: RmsDefinitionKeys.Training))
			});
			await _indexer.CommitAsync();
		}

		[TearDown]
		public void TearDown()
		{
			_host.Dispose();
			SearchConfig.Enabled = false;
		}

		[Test]
		public async Task Department_filter_is_always_injected()
		{
			var one = await _search.SearchAsync(1, new RecordsSearchRequest { Text = "pump" });
			var two = await _search.SearchAsync(2, new RecordsSearchRequest { Text = "pump" });

			one.Hits.Select(h => h.SourceId).Should().Equal("rec-1");
			two.Hits.Select(h => h.SourceId).Should().Equal("rec-9");

			var hostile = await _search.SearchAsync(1, new RecordsSearchRequest { Text = "DepartmentId:2 OR *:* OR SourceId:rec-9" });
			hostile.Hits.Should().OnlyContain(h => h.SourceId != "rec-9", "request text can never widen the department clause");
		}

		[Test]
		public async Task Group_scope_resolves_inside_the_query_with_the_always_visible_cases()
		{
			(await _search.SearchAsync(1, new RecordsSearchRequest { VisibleGroupIds = new List<int> { 11 }, ViewerUserId = "nobody" })).Hits.Select(h => h.SourceId).Should().Equal("rec-1");
			(await _search.SearchAsync(1, new RecordsSearchRequest { VisibleGroupIds = new List<int> { 11 }, ViewerUserId = "a2" })).Hits.Select(h => h.SourceId).Should().BeEquivalentTo(new[] { "rec-1", "rec-2" }, "the author always sees their own record");
			(await _search.SearchAsync(1, new RecordsSearchRequest { VisibleGroupIds = new List<int>(), ViewerUserId = "p1" })).Hits.Select(h => h.SourceId).Should().Equal(new[] { "rec-1" }, "a named participant always sees the record");
			(await _search.SearchAsync(1, new RecordsSearchRequest { VisibleGroupIds = new List<int>(), ViewerUserId = null })).Hits.Should().BeEmpty();
			(await _search.SearchAsync(1, new RecordsSearchRequest { VisibleGroupIds = null })).Hits.Should().HaveCount(3, "null means unrestricted");
		}

		[Test]
		public async Task Filters_and_default_ordering()
		{
			(await _search.SearchAsync(1, new RecordsSearchRequest { States = new List<int> { (int)RmsRecordState.Draft } })).Hits.Select(h => h.SourceId).Should().Equal("rec-2", "rec-3");
			(await _search.SearchAsync(1, new RecordsSearchRequest { Year = 2025 })).Hits.Select(h => h.SourceId).Should().Equal("rec-3");
			(await _search.SearchAsync(1, new RecordsSearchRequest { DefinitionKey = RmsDefinitionKeys.Training })).Hits.Select(h => h.SourceId).Should().Equal("rec-1", "rec-3");
			(await _search.SearchAsync(1, new RecordsSearchRequest { CallId = 9 })).Hits.Select(h => h.SourceId).Should().Equal("rec-2");

			var all = await _search.SearchAsync(1, new RecordsSearchRequest());
			all.Hits.Select(h => h.SourceId).Should().Equal(new[] { "rec-2", "rec-1", "rec-3" }, "newest occurrence first when there is no text");
			all.Total.Should().Be(3);

			var page = await _search.SearchAsync(1, new RecordsSearchRequest { Skip = 1, Take = 1 });
			page.Hits.Select(h => h.SourceId).Should().Equal("rec-1");
			page.Total.Should().Be(3);
		}

		[Test]
		public async Task Text_matches_record_numbers_summaries_call_numbers_and_safe_search_text()
		{
			(await _search.SearchAsync(1, new RecordsSearchRequest { Text = "TRN-2026-0001" })).Hits.Select(h => h.SourceId).Should().Equal("rec-1");
			(await _search.SearchAsync(1, new RecordsSearchRequest { Text = "trn-2026-0001" })).Hits.Select(h => h.SourceId).Should().Equal("rec-1");
			(await _search.SearchAsync(1, new RecordsSearchRequest { Text = "kitchen" })).Hits.Select(h => h.SourceId).Should().Equal("rec-2");
			(await _search.SearchAsync(1, new RecordsSearchRequest { Text = "D-7Q2MX" })).Hits.Select(h => h.SourceId).Should().Equal("rec-3");
			(await _search.SearchAsync(1, new RecordsSearchRequest { Text = "PO-101" })).Hits.Select(h => h.SourceId).Should().Equal("rec-1");
			(await _search.SearchAsync(1, new RecordsSearchRequest { Text = "C-2026-0009" })).Hits.Select(h => h.SourceId).Should().Equal("rec-2");
			(await _search.SearchAsync(1, new RecordsSearchRequest { Text = "pump kitchen" })).Hits.Should().BeEmpty("terms are ANDed");
			(await _search.SearchAsync(1, new RecordsSearchRequest { Text = "((\"unbalanced" })).Available.Should().BeTrue("hostile syntax is escaped, never thrown");
		}

		[Test]
		public async Task Narrative_is_searchable_only_when_supplied_and_withdrawn_on_reindex_without_it()
		{
			(await _search.SearchAsync(1, new RecordsSearchRequest { Text = "hydrant" })).Hits.Select(h => h.SourceId).Should().Equal("rec-1");

			await _indexer.IndexAsync(new[] { Source(Projection(1, "rec-1", "TRN-2026-0001", "Pump operations", RmsRecordState.Finalized, "a1", groups: "11", participants: "p1", occurred: new DateTime(2026, 3, 1), definition: RmsDefinitionKeys.Training, callId: 5)) });
			await _indexer.CommitAsync();

			(await _search.SearchAsync(1, new RecordsSearchRequest { Text = "hydrant" })).Hits.Should().BeEmpty("the degrade path drops narrative on rebuild");
			(await _search.SearchAsync(1, new RecordsSearchRequest { Text = "pump" })).Hits.Select(h => h.SourceId).Should().Equal(new[] { "rec-1" }, "upsert by key never duplicates");
			(await _indexer.CountDocumentsAsync(1)).Should().Be(3);
		}

		[Test]
		public async Task Deletes_by_key_by_soft_deleted_projection_and_by_department()
		{
			await _indexer.DeleteAsync(1, (int)RmsSearchSourceType.Record, "rec-3");
			var gone = Projection(1, "rec-2", "RUN-2026-0007", "Kitchen fire", RmsRecordState.Draft, "a2", groups: "12", participants: "", occurred: new DateTime(2026, 5, 1), definition: RmsDefinitionKeys.Run);
			gone.DeletedOn = DateTime.UtcNow;
			await _indexer.IndexAsync(new[] { Source(gone) });
			await _indexer.CommitAsync();
			(await _indexer.CountDocumentsAsync(1)).Should().Be(1);

			await _indexer.DeleteDepartmentAsync(1);
			await _indexer.CommitAsync();
			(await _indexer.CountDocumentsAsync(1)).Should().Be(0);
			(await _indexer.CountDocumentsAsync(2)).Should().Be(1, "other departments are untouched");
		}

		[Test]
		public async Task Health_and_availability_follow_the_master_switch()
		{
			var health = await _search.GetHealthAsync();
			health.Enabled.Should().BeTrue();
			health.Online.Should().BeTrue();
			health.DocumentCount.Should().Be(4);
			_search.IsAvailable.Should().BeTrue();

			SearchConfig.Enabled = false;
			_search.IsAvailable.Should().BeFalse();
			(await _search.SearchAsync(1, new RecordsSearchRequest { Text = "pump" })).Available.Should().BeFalse();
			(await _search.GetHealthAsync()).Online.Should().BeFalse();
		}

		[Test]
		public void Document_builder_never_indexes_restricted_or_unknown_fields()
		{
			var doc = RecordsSearchDocumentBuilder.Build(Source(Projection(1, "rec-x", "COR-2026-0001", "Coroner case", RmsRecordState.Finalized, "a1", groups: "11", participants: "p1,p2", occurred: new DateTime(2026, 1, 1), definition: RmsDefinitionKeys.Coroner)));
			var names = doc.Fields.Select(f => f.Name).Distinct().ToList();

			names.Should().Contain(new[] { RecordsIndexFields.Key, RecordsIndexFields.DepartmentId, RecordsIndexFields.GroupScopeIds, RecordsIndexFields.ParticipantUserIds, RecordsIndexFields.OccurredOnSort });
			names.Should().NotContain(RecordsIndexFields.Narrative);
			names.Should().NotContain(n => n.ToLowerInvariant().Contains("body") || n.ToLowerInvariant().Contains("case"));
			doc.Fields.Count(f => f.Name == RecordsIndexFields.ParticipantUserIds).Should().Be(2);
		}

		[Test, NonParallelizable]
		public void Constructing_the_configured_host_does_not_touch_the_search_volume()
		{
			// The host is a container singleton, so any I/O in its constructor makes every process that composes a
			// container require the shared volume. On a host where the configured root is not writable — a Linux
			// container or CI runner with no /data — that turns into a container activation failure at startup.
			var probe = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "rms-index-probe-" + Guid.NewGuid().ToString("N"));
			var previous = SearchConfig.IndexPath;
			SearchConfig.IndexPath = probe;

			try
			{
				using (new LuceneRecordsIndexHost()) { }

				System.IO.Directory.Exists(probe).Should().BeFalse("the directory opens on first use, not on construction");
			}
			finally
			{
				SearchConfig.IndexPath = previous;
				if (System.IO.Directory.Exists(probe)) System.IO.Directory.Delete(probe, true);
			}
		}

		private static RecordsSearchDocumentSource Source(RmsRecordSearchProjection projection, string narrative = null)
		{
			return new RecordsSearchDocumentSource { Projection = projection, Narrative = narrative, Generation = "1.0.0" };
		}

		private static RmsRecordSearchProjection Projection(int departmentId, string id, string number, string summary, RmsRecordState state, string author,
			string groups, string participants, DateTime occurred, string definition, int? callId = null, string draft = null, string searchText = null)
		{
			return new RmsRecordSearchProjection
			{
				RmsRecordSearchProjectionId = id,
				DepartmentId = departmentId,
				SourceType = (int)RmsSearchSourceType.Record,
				SourceId = id,
				RecordKind = (int)RmsRecordKind.Operational,
				RecordNumber = number,
				DraftReference = draft ?? "D-" + id.ToUpperInvariant(),
				DefinitionKey = definition,
				DefinitionVersion = 1,
				RecordType = (int)RmsDefinitionKeys.LockedTypes[definition],
				State = (int)state,
				OccurredOn = occurred,
				RecordCreatedOn = occurred,
				AuthorUserId = author,
				OwnerUserId = author,
				ParticipantUserIds = participants,
				GroupScopeIds = groups,
				CallId = callId,
				CallNumber = callId.HasValue ? "C-2026-000" + callId.Value : null,
				DisplaySummary = summary,
				SearchText = searchText ?? summary,
				ModifiedOn = occurred
			};
		}
	}
}
