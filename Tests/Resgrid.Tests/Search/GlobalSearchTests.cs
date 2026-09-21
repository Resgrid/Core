using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Lucene.Net.Store;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model.Search;
using Resgrid.Model.Services;
using Resgrid.Search;

namespace Resgrid.Tests.Search
{
	/// <summary>
	/// The global index end to end on an in-memory directory (Unified Search plan R4 Phase 2): department isolation,
	/// message scope inside the query, admin-only filtering, prefix typeahead, exact identifiers and health.
	/// </summary>
	[TestFixture]
	public class GlobalSearchTests
	{
		private LuceneGlobalIndexHost _host;
		private LuceneGlobalSearchIndexer _indexer;
		private LuceneGlobalSearchService _search;

		[SetUp]
		public async Task SetUp()
		{
			SearchConfig.Enabled = true;
			_host = new LuceneGlobalIndexHost(new RAMDirectory(), ownsDirectory: true);
			_indexer = new LuceneGlobalSearchIndexer(_host);
			_search = new LuceneGlobalSearchService(_host);

			await _indexer.IndexAsync(new[]
			{
				Projection(1, SearchEntityTypes.Call, "10", "Structure Fire - 123 Main St", keywords: "2026-000123 INC-77", summary: "Residential structure fire", category: "Fire", status: "Active", occurred: new DateTime(2026, 5, 1)),
				Projection(1, SearchEntityTypes.Unit, "5", "Engine 2", keywords: "E2 1FTSW21P", category: "Engine", occurred: new DateTime(2026, 4, 1)),
				Projection(1, SearchEntityTypes.Personnel, "u1", "Jane Doe", keywords: "1042", occurred: new DateTime(2026, 3, 1)),
				Projection(1, SearchEntityTypes.Message, "300", "Shift swap Saturday", owner: "u2", participants: "u1,u3", occurred: new DateTime(2026, 6, 1)),
				Projection(1, SearchEntityTypes.Note, "7", "Engine bay memo", adminOnly: true, occurred: new DateTime(2026, 2, 1)),
				Projection(2, SearchEntityTypes.Call, "11", "Structure Fire - 9 Oak Ave", keywords: "2026-000009", occurred: new DateTime(2026, 5, 2))
			}, "1.25.3");
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
			var one = await _search.SearchAsync(1, new GlobalSearchQuery { Text = "structure fire", ViewerUserId = "u1" });
			var two = await _search.SearchAsync(2, new GlobalSearchQuery { Text = "structure fire", ViewerUserId = "u1" });

			one.Hits.Select(h => h.EntityId).Should().BeEquivalentTo(new[] { "10" });
			two.Hits.Select(h => h.EntityId).Should().BeEquivalentTo(new[] { "11" });
		}

		[Test]
		public async Task Policy_generation_is_filtered_before_matching_or_counting()
		{
			var current = await _search.SearchAsync(1, new GlobalSearchQuery { Text = "fire", ViewerUserId = "u1", Generation = "1.25.3" });
			current.Hits.Should().ContainSingle(h => h.DepartmentId == 1 && h.Generation == "1.25.3" && h.RowVersion == 1);
			var changed = await _search.SearchAsync(1, new GlobalSearchQuery { Text = "fire", ViewerUserId = "u1", Generation = "2.25.4" });
			changed.Hits.Should().BeEmpty();
			changed.Total.Should().Be(0);
		}

		[TestCase(false)]
		[TestCase(true)]
		public async Task Every_entity_family_is_isolated_even_for_the_same_user_in_two_departments(bool prefix)
		{
			foreach (var department in new[] { 1, 2 })
				await _indexer.IndexAsync(SearchEntityTypes.Indexed.Select(type =>
					Projection(department, type, "isolation-" + type, "Canary isolation", owner: "shared-member")), "2.0.0");
			await _indexer.CommitAsync();
			foreach (var department in new[] { 1, 2 })
			{
				var result = await _search.SearchAsync(department, new GlobalSearchQuery { Text = "canary", Prefix = prefix, ViewerUserId = "shared-member", IncludeAdminOnly = true });
				result.Hits.Should().HaveCount(SearchEntityTypes.Indexed.Count).And.OnlyContain(h => h.DepartmentId == department);
				result.Total.Should().Be(SearchEntityTypes.Indexed.Count);
			}
		}

		[Test]
		public async Task An_unbounded_skip_yields_an_empty_page_instead_of_an_overflowed_window()
		{
			var result = await _search.SearchAsync(1, new GlobalSearchQuery { Text = "structure fire", ViewerUserId = "u1", Skip = int.MaxValue, Take = 20 });

			result.Available.Should().BeTrue();
			result.Hits.Should().BeEmpty();
			result.Total.Should().Be(1);
		}

		[Test]
		public async Task Messages_are_visible_only_to_sender_or_recipient()
		{
			var recipient = await _search.SearchAsync(1, new GlobalSearchQuery { Text = "shift swap", ViewerUserId = "u1" });
			var sender = await _search.SearchAsync(1, new GlobalSearchQuery { Text = "shift swap", ViewerUserId = "u2" });
			var stranger = await _search.SearchAsync(1, new GlobalSearchQuery { Text = "shift swap", ViewerUserId = "u9" });

			recipient.Hits.Should().ContainSingle(h => h.EntityType == SearchEntityTypes.Message);
			sender.Hits.Should().ContainSingle(h => h.EntityType == SearchEntityTypes.Message);
			stranger.Hits.Should().BeEmpty();
			stranger.Total.Should().Be(0, "counts must not disclose messages the viewer cannot open");
		}

		[Test]
		public async Task Viewer_scoped_families_return_only_the_viewers_own_rows_and_messages_stay_scoped()
		{
			// Two deployments: u1 is rostered on the first only. The roster is the projection's participant set.
			await _indexer.IndexAsync(new[]
			{
				Projection(1, SearchEntityTypes.Deployment, "dep-1", "Ridge Complex strike team", keywords: "CA-LNU-001234", participants: "u1,u4", occurred: new DateTime(2026, 1, 10)),
				Projection(1, SearchEntityTypes.Deployment, "dep-2", "Ridge Complex water tender", keywords: "CA-LNU-001235", participants: "u4", occurred: new DateTime(2026, 1, 11))
			}, "1.25.3");
			await _indexer.CommitAsync();
			var scoped = new List<string> { SearchEntityTypes.Deployment };

			var rostered = await _search.SearchAsync(1, new GlobalSearchQuery { Text = "ridge complex", ViewerUserId = "u1", ViewerScopedEntityTypes = scoped });
			var stranger = await _search.SearchAsync(1, new GlobalSearchQuery { Text = "ridge complex", ViewerUserId = "u9", ViewerScopedEntityTypes = scoped });
			var claimHolder = await _search.SearchAsync(1, new GlobalSearchQuery { Text = "ridge complex", ViewerUserId = "u9" });
			var messageStillScoped = await _search.SearchAsync(1, new GlobalSearchQuery { Text = "shift swap", ViewerUserId = "u9", ViewerScopedEntityTypes = scoped });

			rostered.Hits.Select(h => h.EntityId).Should().Equal("dep-1");
			rostered.Total.Should().Be(1, "the index counts only the rows the scope admits");
			stranger.Hits.Should().BeEmpty();
			stranger.Total.Should().Be(0);
			claimHolder.Hits.Select(h => h.EntityId).Should().BeEquivalentTo(new[] { "dep-1", "dep-2" }, "without the scope the family is department-wide, as for a claim holder");
			messageStillScoped.Hits.Should().BeEmpty("naming extra families never un-scopes messages");
		}

		[Test]
		public async Task Admin_only_rows_are_hidden_unless_the_viewer_is_an_admin()
		{
			var member = await _search.SearchAsync(1, new GlobalSearchQuery { Text = "engine", ViewerUserId = "u1" });
			var admin = await _search.SearchAsync(1, new GlobalSearchQuery { Text = "engine", ViewerUserId = "u1", IncludeAdminOnly = true });

			member.Hits.Select(h => h.EntityType).Should().BeEquivalentTo(new[] { SearchEntityTypes.Unit });
			admin.Hits.Select(h => h.EntityType).Should().BeEquivalentTo(new[] { SearchEntityTypes.Unit, SearchEntityTypes.Note });
		}

		[Test]
		public async Task Prefix_mode_matches_titles_and_identifiers()
		{
			var eng = await _search.SearchAsync(1, new GlobalSearchQuery { Text = "eng", ViewerUserId = "u1", Prefix = true });
			var number = await _search.SearchAsync(1, new GlobalSearchQuery { Text = "2026-0001", ViewerUserId = "u1", Prefix = true });
			var name = await _search.SearchAsync(1, new GlobalSearchQuery { Text = "jan do", ViewerUserId = "u1", Prefix = true });

			eng.Hits.Select(h => h.EntityId).Should().Contain("5");
			number.Hits.Select(h => h.EntityId).Should().BeEquivalentTo(new[] { "10" });
			name.Hits.Select(h => h.EntityId).Should().BeEquivalentTo(new[] { "u1" });
		}

		[Test]
		public async Task Exact_identifier_ranks_first_and_partial_last_token_still_matches()
		{
			var exact = await _search.SearchAsync(1, new GlobalSearchQuery { Text = "INC-77", ViewerUserId = "u1" });
			var partial = await _search.SearchAsync(1, new GlobalSearchQuery { Text = "structure fi", ViewerUserId = "u1" });

			exact.Hits.First().EntityId.Should().Be("10");
			partial.Hits.Select(h => h.EntityId).Should().Contain("10");
		}

		[Test]
		public async Task Entity_type_filter_and_date_sort_without_text()
		{
			var units = await _search.SearchAsync(1, new GlobalSearchQuery { Text = "e", ViewerUserId = "u1", Prefix = true, EntityTypes = new List<string> { SearchEntityTypes.Unit } });
			var newest = await _search.SearchAsync(1, new GlobalSearchQuery { ViewerUserId = "u1", IncludeAdminOnly = true });

			units.Hits.Should().OnlyContain(h => h.EntityType == SearchEntityTypes.Unit);
			newest.Hits.First().EntityType.Should().Be(SearchEntityTypes.Message, "no text sorts by OccurredOn descending");
		}

		[Test]
		public async Task Query_input_is_escaped_and_field_selectors_never_reach_the_parser()
		{
			var result = await _search.SearchAsync(1, new GlobalSearchQuery { Text = "DepartmentId:2 OR Title:*", ViewerUserId = "u1" });
			result.Available.Should().BeTrue();
			result.Hits.Should().BeEmpty();
		}

		[Test]
		public async Task Disabled_host_reports_unavailable_and_health()
		{
			var health = await _search.GetHealthAsync();
			health.Online.Should().BeTrue();
			health.DocumentCount.Should().Be(6);
			health.IndexName.Should().Be(SearchIndexNames.Global);

			SearchConfig.Enabled = false;
			var result = await _search.SearchAsync(1, new GlobalSearchQuery { Text = "fire" });
			result.Available.Should().BeFalse();
		}

		internal static SearchProjection Projection(int departmentId, string type, string id, string title, string keywords = null, string summary = null,
			string category = null, string status = null, string owner = null, string participants = null, bool adminOnly = false, DateTime? occurred = null)
		{
			return new SearchProjection
			{
				SearchProjectionId = Guid.NewGuid().ToString(),
				DepartmentId = departmentId,
				EntityType = type,
				EntityId = id,
				Title = title,
				Keywords = keywords,
				Summary = summary,
				Category = category,
				Status = status,
				OwnerUserId = owner,
				ParticipantUserIds = participants,
				IsAdminOnly = adminOnly,
				IsActive = true,
				OccurredOn = occurred ?? DateTime.UtcNow,
				Url = "/User/" + type,
				CreatedOn = DateTime.UtcNow,
				ModifiedOn = DateTime.UtcNow,
				RowVersion = 1
			};
		}
	}
}
