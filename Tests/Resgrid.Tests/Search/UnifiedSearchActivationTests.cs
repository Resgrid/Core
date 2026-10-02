using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Search;

namespace Resgrid.Tests.Search
{
	/// <summary>
	/// Lazy activation on every search (not only while the shared index is missing), the building flag, and the search
	/// page's request shape: date range, ordering, deeper window and excerpts.
	/// </summary>
	public partial class UnifiedSearchServiceTests
	{
		private static SearchIndexState ReadyState() => new SearchIndexState
		{
			IndexName = SearchIndexNames.Global, DepartmentId = 7, State = (int)SearchIndexBuildState.Ready
		};

		[Test]
		public async Task A_department_without_a_state_row_is_activated_even_while_the_shared_index_serves()
		{
			_states.Setup(s => s.GetAsync(SearchIndexNames.Global, 7)).ReturnsAsync((SearchIndexState)null);
			SearchIndexState inserted = null;
			_states.Setup(s => s.InsertIfMissingAsync(It.IsAny<SearchIndexState>(), It.IsAny<CancellationToken>()))
				.Callback((SearchIndexState s, CancellationToken _) => inserted = s).ReturnsAsync(true);

			var result = await _service.SearchAsync(new UnifiedSearchRequest { Text = "one" }, Principal("Call:View"));

			inserted.Should().NotBeNull("once another department had built the shared index, this one used to be skipped forever");
			inserted.State.Should().Be((int)SearchIndexBuildState.RebuildRequested);
			result.IndexBuilding.Should().BeTrue();
			result.Degraded.Should().BeFalse("the shared index itself is serving");
			result.Hits.Should().HaveCount(2);
		}

		[Test]
		public async Task A_ready_department_is_reported_built_and_its_state_is_not_re_read_on_every_keystroke()
		{
			_states.Setup(s => s.GetAsync(SearchIndexNames.Global, 7)).ReturnsAsync(ReadyState());

			var first = await _service.SearchAsync(new UnifiedSearchRequest { Text = "on" }, Principal("Call:View"));
			var second = await _service.SearchAsync(new UnifiedSearchRequest { Text = "one" }, Principal("Call:View"));

			first.IndexBuilding.Should().BeFalse();
			second.IndexBuilding.Should().BeFalse();
			_states.Verify(s => s.GetAsync(SearchIndexNames.Global, 7), Times.Once);
			_states.Verify(s => s.InsertIfMissingAsync(It.IsAny<SearchIndexState>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task A_ready_state_built_at_an_older_generation_is_reported_building()
		{
			var stale = ReadyState();
			stale.Generation = "2.0.0";
			_states.Setup(s => s.GetAsync(SearchIndexNames.Global, 7)).ReturnsAsync(stale);

			var result = await _service.SearchAsync(new UnifiedSearchRequest { Text = "one" }, Principal("Call:View"));

			result.IndexBuilding.Should().BeTrue("after a schema bump the query filters on the new generation until the worker rebuilds");
		}

		[TestCase(SearchIndexBuildState.RebuildRequested)]
		[TestCase(SearchIndexBuildState.Rebuilding)]
		[TestCase(SearchIndexBuildState.Failed)]
		public async Task A_department_that_is_not_ready_is_reported_building_every_time(SearchIndexBuildState state)
		{
			_states.Setup(s => s.GetAsync(SearchIndexNames.Global, 7)).ReturnsAsync(new SearchIndexState { IndexName = SearchIndexNames.Global, DepartmentId = 7, State = (int)state });

			(await _service.SearchAsync(new UnifiedSearchRequest { Text = "one" }, Principal("Call:View"))).IndexBuilding.Should().BeTrue();
			(await _service.SearchAsync(new UnifiedSearchRequest { Text = "one" }, Principal("Call:View"))).IndexBuilding.Should().BeTrue();
			_states.Verify(s => s.GetAsync(SearchIndexNames.Global, 7), Times.Exactly(2), "only Ready is remembered");
		}

		[Test]
		public async Task The_page_request_carries_its_date_range_order_and_deeper_window_to_the_index()
		{
			var from = new DateTime(2026, 1, 1, 6, 0, 0, DateTimeKind.Utc);
			var to = new DateTime(2026, 3, 1, 5, 59, 59, DateTimeKind.Utc);

			await _service.SearchAsync(new UnifiedSearchRequest
			{
				Text = "trouble alarm", FromUtc = from, ToUtc = to, Sort = "NEWEST", MaxCandidates = 100000, IncludeRecords = false,
				EntityTypes = new List<string> { SearchEntityTypes.Call }
			}, Principal("Call:View"));

			_lastQuery.FromUtc.Should().Be(from);
			_lastQuery.ToUtc.Should().Be(to);
			_lastQuery.Sort.Should().Be(SearchSortOrders.Newest);
			_lastQuery.MaxWindow.Should().Be(SearchConfig.MaxPageWindow, "the window is capped by configuration");
			_lastQuery.Take.Should().Be(SearchConfig.MaxPageWindow);
			_lastQuery.Prefix.Should().BeFalse();
		}

		[Test]
		public async Task Without_a_total_authorization_stops_once_the_page_is_full()
		{
			Answer(Hit(SearchEntityTypes.Call, "1"), Hit(SearchEntityTypes.Call, "2"), Hit(SearchEntityTypes.Call, "3"));

			var result = await _service.SearchAsync(new UnifiedSearchRequest { Text = "one", Take = 1, CountTotal = false }, Principal("Call:View"));

			result.Hits.Select(h => h.EntityId).Should().Equal("1");
			result.Total.Should().BeNull();
			_auth.Verify(a => a.CanUserViewCallAsync("u1", It.IsAny<int>()), Times.Once, "the palette shows no total, so the other candidates are never checked");
		}

		[Test]
		public async Task Typeahead_keeps_the_default_window()
		{
			await _service.SearchAsync(new UnifiedSearchRequest { Text = "one", Prefix = true }, Principal("Call:View"));

			_lastQuery.Take.Should().Be(200);
			_lastQuery.MaxWindow.Should().Be(200);
		}

		[Test]
		public async Task Full_text_hits_carry_the_excerpt_around_the_match_and_typeahead_hits_do_not()
		{
			Answer(Hit(SearchEntityTypes.Call, "1"));
			_rows[0].SearchText = "1 Industrial Way Alarm activation ACME Monitoring account 5521. Alarm company: trouble alarm @ Bldg 4, room 12";

			var full = await _service.SearchAsync(new UnifiedSearchRequest { Text = "\"trouble alarm\" bldg" }, Principal("Call:View"));
			var typeahead = await _service.SearchAsync(new UnifiedSearchRequest { Text = "trouble", Prefix = true }, Principal("Call:View"));

			full.Hits.Single().Snippet.Should().Contain("trouble alarm @ Bldg 4");
			typeahead.Hits.Single().Snippet.Should().BeNull();
		}

		[Test]
		public async Task Newest_first_orders_the_combined_page_by_when_each_hit_occurred()
		{
			Answer(Hit(SearchEntityTypes.Call, "1"), Hit(SearchEntityTypes.Call, "2"));
			_rows[0].OccurredOn = new DateTime(2026, 1, 1);
			_rows[1].OccurredOn = new DateTime(2026, 6, 1);

			var result = await _service.SearchAsync(new UnifiedSearchRequest { Text = "one", Sort = SearchSortOrders.Newest }, Principal("Call:View"));

			result.Hits.Select(h => h.EntityId).Should().Equal("2", "1");
		}

		[Test]
		public async Task Searchable_families_follow_claims_and_include_records_only_when_the_caller_can_search_them()
		{
			var plain = await _service.GetSearchableEntityTypesAsync(Principal("Call:View", "Notes:View"));
			plain.Should().BeEquivalentTo(new[] { SearchEntityTypes.Call, SearchEntityTypes.Note, SearchEntityTypes.Deployment, SearchEntityTypes.Poi });

			RecordsAnswer();
			var withRecords = await _service.GetSearchableEntityTypesAsync(Principal("Call:View", "Record:View"));
			withRecords.Should().Contain(SearchEntityTypes.Record);

			_flagOn = false;
			(await _service.GetSearchableEntityTypesAsync(Principal("Call:View"))).Should().BeEmpty();
		}
	}
}
