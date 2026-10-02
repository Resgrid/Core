using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model.Search;
using Resgrid.Web.Helpers;

namespace Resgrid.Tests.Search
{
	/// <summary>The excerpt and highlight helpers behind the search page: phrases first, literal matching, encoded output.</summary>
	[TestFixture]
	public class SearchSnippetsTests
	{
		[Test]
		public void Terms_list_phrases_before_their_words_and_drop_single_characters()
		{
			var terms = SearchSnippets.Terms("\"trouble alarm\" bldg x, room");

			terms.First().Should().Be("trouble alarm");
			terms.Should().Contain(new[] { "trouble", "alarm", "bldg", "room" });
			terms.Should().NotContain("x");
		}

		[Test]
		public void The_excerpt_centres_on_the_phrase_and_marks_cuts_with_an_ellipsis()
		{
			var text = string.Join(" ", Enumerable.Repeat("Routine alarm check logged.", 20)) + " Alarm company: trouble alarm @ Bldg 4, room 12. " +
				string.Join(" ", Enumerable.Repeat("Crew returned to quarters.", 20));

			var snippet = SearchSnippets.Build(text, SearchSnippets.Terms("\"trouble alarm\""), 120);

			snippet.Should().Contain("trouble alarm @ Bldg 4");
			snippet.Should().StartWith("…").And.EndWith("…");
			snippet.Length.Should().BeLessThanOrEqualTo(122);
		}

		[Test]
		public void A_short_text_comes_back_whole_and_a_miss_comes_back_null()
		{
			SearchSnippets.Build("Trouble alarm, Bldg 4", new[] { "bldg" }).Should().Be("Trouble alarm, Bldg 4");
			SearchSnippets.Build("Medical call", new[] { "alarm" }).Should().BeNull();
			SearchSnippets.Build(null, new[] { "alarm" }).Should().BeNull();
		}

		[Test]
		public void Highlighting_encodes_the_text_and_marks_only_the_terms()
		{
			var html = SearchHighlighter.Highlight("<script>x</script> Trouble ALARM & more", new[] { "trouble alarm", "alarm" }).ToString();

			html.Should().Contain("&lt;script&gt;");
			html.Should().Contain("<mark>Trouble ALARM</mark>", "the phrase is marked whole, case-insensitively");
			html.Should().Contain("&amp; more");
			html.Should().NotContain("<script>");
		}

		[Test]
		public void Highlighting_treats_terms_as_literal_text()
		{
			var html = SearchHighlighter.Highlight("cost (est.) $100", new[] { "(est.)", "$100" }).ToString();

			html.Should().Be("cost <mark>(est.)</mark> <mark>$100</mark>");
		}
	}
}
