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
	/// Full-text search over the global index: call notes in the full text, quoted phrases, the mid-word stub over the full
	/// text, date ranges, date orderings and the deeper window the search page uses for a narrowed question.
	/// </summary>
	[TestFixture]
	public class GlobalSearchFullTextTests
	{
		private const string Generation = "3.0.0";
		private LuceneGlobalIndexHost _host;
		private LuceneGlobalSearchIndexer _indexer;
		private LuceneGlobalSearchService _search;
		private int _maxResults;
		private int _maxPageWindow;

		[SetUp]
		public async Task SetUp()
		{
			SearchConfig.Enabled = true;
			_maxResults = SearchConfig.MaxResults;
			_maxPageWindow = SearchConfig.MaxPageWindow;
			_host = new LuceneGlobalIndexHost(new RAMDirectory(), ownsDirectory: true);
			_indexer = new LuceneGlobalSearchIndexer(_host);
			_search = new LuceneGlobalSearchService(_host);

			await _indexer.IndexAsync(new[]
			{
				Call("101", "Alarm - Commercial", "ACME Monitoring reports trouble alarm @ Bldg 4, room 12. Keyholder en route.", new DateTime(2026, 3, 1)),
				Call("102", "Alarm - Commercial", "ACME Monitoring: trouble alarm at Bldg 4 room 12 again, second time this week.", new DateTime(2026, 4, 1)),
				Call("103", "Alarm - Commercial", "Fire alarm activation Bldg 7. Alarm company reports trouble on the panel only.", new DateTime(2026, 5, 1)),
				Call("104", "Medical", "Patient fell in room 12 of the clinic.", new DateTime(2026, 6, 1))
			}, Generation);
			await _indexer.CommitAsync();
		}

		[TearDown]
		public void TearDown()
		{
			_host.Dispose();
			SearchConfig.Enabled = false;
			SearchConfig.MaxResults = _maxResults;
			SearchConfig.MaxPageWindow = _maxPageWindow;
		}

		private static SearchProjection Call(string id, string title, string notes, DateTime occurred)
		{
			var p = GlobalSearchTests.Projection(1, SearchEntityTypes.Call, id, title, keywords: "2026-000" + id, summary: title, occurred: occurred);
			p.SearchText = notes;
			return p;
		}

		private Task<GlobalSearchResult> Find(string text, Action<GlobalSearchQuery> configure = null)
		{
			var query = new GlobalSearchQuery { Text = text, ViewerUserId = "u1", Generation = Generation, Take = 50 };
			configure?.Invoke(query);
			return _search.SearchAsync(1, query);
		}

		[Test]
		public async Task Words_found_only_in_the_notes_match_the_call()
		{
			var result = await Find("keyholder");

			result.Hits.Select(h => h.EntityId).Should().Equal("101");
		}

		[Test]
		public async Task Every_word_must_match_somewhere()
		{
			var result = await Find("trouble alarm bldg 4 room 12");

			result.Hits.Select(h => h.EntityId).Should().BeEquivalentTo(new[] { "101", "102" });
		}

		[Test]
		public async Task A_quoted_phrase_matches_the_words_in_order_only()
		{
			var phrase = await Find("\"trouble alarm\"");
			var loose = await Find("trouble alarm");

			phrase.Hits.Select(h => h.EntityId).Should().BeEquivalentTo(new[] { "101", "102" }, "call 103 has both words but not as a phrase");
			loose.Hits.Select(h => h.EntityId).Should().Contain("103");
		}

		[Test]
		public async Task Phrases_and_words_combine_and_punctuation_in_the_text_does_not_break_a_phrase()
		{
			var result = await Find("\"trouble alarm\" \"bldg 4\" \"room 12\"");

			result.Hits.Select(h => h.EntityId).Should().BeEquivalentTo(new[] { "101", "102" }, "'@ Bldg 4, room 12' and 'at Bldg 4 room 12' both hold the phrases");
			result.Total.Should().Be(2);
		}

		[Test]
		public async Task An_unmatched_quote_is_ignored_rather_than_failing_the_query()
		{
			var result = await Find("\"trouble alarm bldg");

			result.Available.Should().BeTrue();
			result.Hits.Select(h => h.EntityId).Should().BeEquivalentTo(new[] { "101", "102", "103" }, "the stray quote is dropped and the words match loosely");
		}

		[Test]
		public async Task A_partial_last_word_matches_the_notes_once_it_is_long_enough()
		{
			var stub = await Find("acme monit");
			var tooShort = await Find("acme mo");

			stub.Hits.Select(h => h.EntityId).Should().BeEquivalentTo(new[] { "101", "102" });
			tooShort.Hits.Should().BeEmpty("a two-letter stub is only expanded against titles and identifiers");
		}

		[Test]
		public async Task The_date_range_bounds_when_the_hit_occurred()
		{
			var march = await Find("alarm", q => { q.FromUtc = new DateTime(2026, 3, 1); q.ToUtc = new DateTime(2026, 3, 31, 23, 59, 59); });
			var fromApril = await Find("alarm", q => q.FromUtc = new DateTime(2026, 4, 1));
			var untilMarch = await Find("alarm", q => q.ToUtc = new DateTime(2026, 3, 15));

			march.Hits.Select(h => h.EntityId).Should().Equal("101");
			fromApril.Hits.Select(h => h.EntityId).Should().BeEquivalentTo(new[] { "102", "103" });
			untilMarch.Hits.Select(h => h.EntityId).Should().Equal("101");
			march.Total.Should().Be(1, "the range is applied before counting");
		}

		[Test]
		public async Task Newest_and_oldest_order_by_when_the_hit_occurred()
		{
			var newest = await Find("alarm", q => q.Sort = SearchSortOrders.Newest);
			var oldest = await Find("alarm", q => q.Sort = SearchSortOrders.Oldest);

			newest.Hits.Select(h => h.EntityId).Should().Equal("103", "102", "101");
			oldest.Hits.Select(h => h.EntityId).Should().Equal("101", "102", "103");
		}

		[Test]
		public async Task A_deeper_window_is_honoured_up_to_the_page_window_cap()
		{
			SearchConfig.MaxResults = 2;
			SearchConfig.MaxPageWindow = 3;

			var typeahead = await Find("alarm", q => q.Take = 10);
			var page = await Find("alarm", q => { q.Take = 10; q.MaxWindow = 10; });

			typeahead.Hits.Should().HaveCount(2);
			typeahead.Truncated.Should().BeTrue();
			page.Hits.Should().HaveCount(3, "the page window cap (3) wins over the requested 10");
			page.Truncated.Should().BeFalse("all three matches fit the window");
		}

		[Test]
		public async Task Indexed_row_versions_report_what_the_index_holds()
		{
			var indexed = Call("105", "Alarm", "Indexed once", new DateTime(2026, 7, 1));
			indexed.RowVersion = 7;
			await _indexer.IndexAsync(new[] { indexed }, Generation);
			await _indexer.CommitAsync();
			var missing = Call("106", "Alarm", "Never indexed", new DateTime(2026, 7, 2));

			var versions = await _indexer.GetIndexedRowVersionsAsync(1, new[] { indexed, missing });

			versions.Should().ContainKey(indexed.SearchProjectionId).WhoseValue.Should().Be(7);
			versions.Should().NotContainKey(missing.SearchProjectionId);
		}

		[Test]
		public void Clauses_keep_quoted_phrases_together_and_drop_stop_words()
		{
			var clauses = LuceneGlobalSearchService.ParseClauses("\"trouble alarm\" the bldg-4");

			clauses.Should().HaveCount(2);
			clauses[0].Quoted.Should().BeTrue();
			clauses[0].Tokens.Select(t => t.Text).Should().Equal("trouble", "alarm");
			clauses[1].Quoted.Should().BeFalse();
			clauses[1].Tokens.Select(t => t.Text).Should().Equal("bldg", "4");
		}
	}
}
