using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Search;

namespace Resgrid.Tests.Search
{
	/// <summary>
	/// IdentifierSearchKeys: department-formatted numbers (records, calls, invoices, bids...) read the same however they are
	/// typed, and are found by any run of their parts.
	/// </summary>
	[TestFixture]
	public class IdentifierSearchKeysTests
	{
		[TestCase("RUN26-0001", "run-26-1")]
		[TestCase("run-26-1", "run-26-1")]
		[TestCase("RUN 26 0001", "run-26-1")]
		[TestCase("2026.0042", "2026-42")]
		[TestCase("TRN_2026_0007", "trn-2026-7")]
		[TestCase("FD/2026/07/0003", "fd-2026-7-3")]
		[TestCase("#1042", "1042")]
		[TestCase("0000", "0")]
		[TestCase("WO-2027-000001", "wo-2027-1")]
		public void Canonical_sets_the_formatting_aside(string text, string expected)
		{
			IdentifierSearchKeys.Canonical(text).Should().Be(expected);
		}

		[TestCase(null)]
		[TestCase("")]
		[TestCase("  - . / ")]
		public void Canonical_is_null_without_letters_or_digits(string text)
		{
			IdentifierSearchKeys.Canonical(text).Should().BeNull();
		}

		[Test]
		public void A_number_is_keyed_by_every_run_of_consecutive_parts()
		{
			IdentifierSearchKeys.For("EZKT27-0001").Should().BeEquivalentTo("ezkt-27-1", "ezkt", "27", "1", "ezkt-27", "27-1");
		}

		[Test]
		public void Every_way_of_typing_a_number_or_a_part_of_it_hits_one_of_its_keys()
		{
			var keys = IdentifierSearchKeys.For("RUN-2026-0153").ToList();

			foreach (var typed in new[] { "RUN-2026-0153", "run-2026-153", "RUN 2026 153", "2026-153", "0153", "153", "RUN", "run-2026" })
				keys.Should().Contain(IdentifierSearchKeys.Canonical(typed), "\"{0}\" names part of the number", typed);
			keys.Should().NotContain(IdentifierSearchKeys.Canonical("2026-0154"));
		}

		[Test]
		public void Long_numbers_keep_their_whole_key_and_bounded_part_runs()
		{
			var number = string.Join("-", Enumerable.Range(1, 12).Select(i => "A" + i));
			var keys = IdentifierSearchKeys.For(number).ToList();

			keys.Should().Contain(IdentifierSearchKeys.Canonical(number));
			keys.Where(k => k != IdentifierSearchKeys.Canonical(number)).Should().OnlyContain(k => k.Split(new[] { '-' }).Length <= IdentifierSearchKeys.MaxPartsPerKey);
		}

		[TestCase("WO26-0007", "wo26-0007", true)]
		[TestCase("WO26-0007", "WO-26-0007", true)]
		[TestCase("WO26-0007", "26 0007", true)]
		[TestCase("WO26-0007", "0007", true)]
		[TestCase("WO26-0007", "26-7", true)]
		[TestCase("WO26-0017", "26-7", true)]
		[TestCase("WO26-0007", "0017", false)]
		[TestCase("WO26-0007", "7-26", false)]
		[TestCase("INV2026.0042", "inv-2026-42", true)]
		[TestCase("BID_26_0007", "bid 26 0007", true)]
		[TestCase("OCC_2026_0001", "occx2026", false)]
		[TestCase("WO26-0007", " - ", false)]
		[TestCase(null, "7", false)]
		public void List_filters_match_the_searched_runs_in_order(string number, string search, bool expected)
		{
			IdentifierSearchKeys.ContainsParts(number, search).Should().Be(expected);
		}

		[Test]
		public void Typed_parts_keep_zero_padding_for_the_sql_pattern()
		{
			IdentifierSearchKeys.TypedParts("WO-26_0007").Should().Equal("wo", "26", "0007");
			IdentifierSearchKeys.TypedParts("50%_[x]").Should().Equal(new[] { "50", "x" }, "LIKE wildcards never reach the pattern");
		}

		[Test]
		public void A_filter_search_is_trimmed_capped_and_dropped_when_it_names_nothing()
		{
			IdentifierSearchKeys.FilterSearch("  WO26-0007 ").Should().Be("WO26-0007");
			IdentifierSearchKeys.FilterSearch(" -/ ").Should().BeNull();
			IdentifierSearchKeys.FilterSearch(null).Should().BeNull();
			IdentifierSearchKeys.FilterSearch(new string('7', 80)).Should().HaveLength(IdentifierSearchKeys.MaxFilterLength);
		}

		[Test]
		public void Global_keywords_key_only_the_ones_holding_a_digit()
		{
			GlobalSearchDocumentBuilder.NumberKeys("Jane Doe INV2026.0042 jane@example.com").Should()
				.BeEquivalentTo("inv-2026-42", "inv", "2026", "42", "inv-2026", "2026-42");
		}
	}
}
