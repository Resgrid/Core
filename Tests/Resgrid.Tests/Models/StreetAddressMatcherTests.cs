using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;

namespace Resgrid.Tests.Models
{
	[TestFixture]
	public class StreetAddressMatcherTests
	{
		[TestCase("110 S Main St", "110 South Main")]
		[TestCase("110 S Main St", "110 South Main Street")]
		[TestCase("110 South Main", "110 South Main Street")]
		[TestCase("110 S Main St, Springfield, IL 62701, USA", "110 South Main Street")]
		[TestCase("110 s. main st.", "110 SOUTH MAIN STREET")]
		[TestCase("Joe's Diner, 110 S Main St", "110 South Main")]
		[TestCase("Joes Diner 110 S Main St", "110 South Main")]
		[TestCase("110 Main St Apt 4", "110 Main Street")]
		[TestCase("110 Main St #4", "110 Main St")]
		[TestCase("110 Main St, Suite 200, Springfield", "110 Main St Springfield")]
		[TestCase("200 First Street", "200 1st St")]
		[TestCase("45 Spring Lake Dr", "45 Spring Lake Drive")]
		[TestCase("45 Spring Lake", "45 Spring Lake Dr")]
		[TestCase("110 North Ave", "110 North Avenue")]
		[TestCase("110 N Ave", "110 North Avenue")]
		[TestCase("110 E North St", "110 East North Street")]
		[TestCase("10 St Louis Ave", "10 Saint Louis Avenue")]
		[TestCase("5000 Highway 50", "5000 Hwy 50")]
		[TestCase("123 County Road 12", "123 CR 12")]
		[TestCase("123 US Highway 50 E", "123 US-50 E")]
		[TestCase("88 State Route 9", "88 SR 9")]
		[TestCase("Main St & 1st Ave", "1st Avenue and Main Street")]
		[TestCase("Main St / 1st Ave", "Main & First")]
		[TestCase("110 Main St N", "110 Main Street North")]
		[TestCase("1200 Peachtree St NE, Atlanta, GA 30309", "1200 peachtree street northeast")]
		[TestCase("110 Café Rd", "110 Cafe Road")]
		[TestCase("110 1/2 Main St", "110 1/2 Main Street")]
		public void Same_place_spelled_differently_is_the_same(string a, string b)
		{
			StreetAddressMatcher.Compare(a, b).Should().Be(StreetAddressMatch.Same, $"'{a}' and '{b}' name the same place");
			StreetAddressMatcher.Compare(b, a).Should().Be(StreetAddressMatch.Same, "the comparison is symmetric");
		}

		[TestCase("110 Main St", "110 S Main St")]
		[TestCase("110 S Main St", "110 South Main Springfield")]
		[TestCase("110 Main St, Springfield", "110 Main St, Shelbyville")]
		public void One_side_leaving_something_out_is_similar(string a, string b)
		{
			StreetAddressMatcher.Compare(a, b).Should().Be(StreetAddressMatch.Similar);
			StreetAddressMatcher.Compare(b, a).Should().Be(StreetAddressMatch.Similar);
		}

		[TestCase("110 N Main St", "110 S Main St")]
		[TestCase("110 Main St", "112 Main St")]
		[TestCase("110 Main St", "110 Main Ave")]
		[TestCase("110 Main St, 62701", "110 Main St, 62702")]
		[TestCase("110 Oak", "110 Oak Hill Rd")]
		[TestCase("110 Main St", "110 Elm St")]
		[TestCase("Main St & 1st Ave", "Main St & 2nd Ave")]
		[TestCase("Main St & 1st Ave", "110 Main St")]
		[TestCase("5000 Highway 50", "5000 Highway 51")]
		[TestCase("Walmart", "Walmart")]
		[TestCase("", "110 Main St")]
		public void Different_places_do_not_match(string a, string b)
		{
			StreetAddressMatcher.Compare(a, b).Should().Be(StreetAddressMatch.None, $"'{a}' and '{b}' are different places or not addresses");
		}

		[Test]
		public void Parses_geocoder_output_into_parts()
		{
			var parsed = StreetAddressParser.Parse("110 S Main St, Apt 4, Springfield, IL 62701-1234, USA");

			parsed.HouseNumber.Should().Be("110");
			parsed.PreDirectional.Should().Be("S");
			parsed.StreetName.Should().Be("MAIN");
			parsed.SuffixType.Should().Be("ST");
			parsed.Unit.Should().Be("APT 4");
			parsed.Locality.Should().Be("SPRINGFIELD");
			parsed.PostalCode.Should().Be("62701");
			parsed.IndexKey.Should().Be("110|MAIN");
		}

		[Test]
		public void Florida_and_a_zip_code_are_not_a_floor()
		{
			var parsed = StreetAddressParser.Parse("110 Main St Miami FL 33101");

			parsed.Unit.Should().BeNull();
			parsed.PostalCode.Should().Be("33101");
			parsed.Locality.Should().Be("MIAMI");
		}

		[Test]
		public void Structured_locality_and_postal_code_win()
		{
			var parsed = StreetAddressParser.Parse("110 Main St", "Springfield", "62701");

			parsed.Locality.Should().Be("SPRINGFIELD");
			parsed.PostalCode.Should().Be("62701");
		}

		[Test]
		public void Index_keys_agree_across_spellings_so_the_lookup_finds_the_row()
		{
			var keys = new[] { "110 S Main St", "110 South Main", "110 Main Street, Springfield" };

			foreach (var key in keys)
				StreetAddressParser.Parse(key).IndexKey.Should().Be("110|MAIN");

			StreetAddressParser.Parse("Main St & 1st Ave").IndexKey.Should().Be("X|1ST|MAIN");
			StreetAddressParser.Parse("1st Avenue and Main Street").IndexKey.Should().Be("X|1ST|MAIN");
			StreetAddressParser.Parse("Walmart").IndexKey.Should().BeNull("a place name without a number is matched by proximity only");
		}

		[Test]
		public void Canonical_form_round_trips()
		{
			var parsed = StreetAddressParser.Parse("1200 Peachtree St NE, Suite 5, Atlanta, GA 30309");
			var restored = ParsedStreetAddress.FromCanonical(parsed.ToCanonical());

			restored.Should().BeEquivalentTo(parsed);
			StreetAddressMatcher.Compare(restored, parsed).Should().Be(StreetAddressMatch.Same);
			ParsedStreetAddress.FromCanonical("0|1|2").Should().BeNull("a different version is rebuilt, not guessed at");
		}

		[Test]
		public void Canadian_postal_codes_are_read()
		{
			StreetAddressParser.Parse("55 King St W, Toronto, ON M5K 1A1").PostalCode.Should().Be("M5K1A1");
			StreetAddressMatcher.Compare("55 King St W, Toronto, ON M5K 1A1", "55 King Street West").Should().Be(StreetAddressMatch.Same);
		}
	}
}
