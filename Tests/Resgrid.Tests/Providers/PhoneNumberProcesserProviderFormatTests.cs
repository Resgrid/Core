using FluentAssertions;
using NUnit.Framework;
using Resgrid.Providers.NumberProvider;

namespace Resgrid.Tests.Providers
{
	/// <summary>
	/// The profile save paths store PhoneNumberResult.InternationalNumber, and the inbound SMS/voice
	/// lookup matches that stored value against digits (optionally plus-prefixed). That only works
	/// because this provider emits E.164 with no separators - if it ever returned a spaced or dashed
	/// international format, every newly saved number would stop resolving on inbound messages.
	/// </summary>
	[TestFixture]
	public class PhoneNumberProcesserProviderFormatTests
	{
		private PhoneNumberProcesserProvider _provider;

		[SetUp]
		public void SetUp() => _provider = new PhoneNumberProcesserProvider();

		[TestCase("+12015550123", null)]
		[TestCase("2015550123", "US")]
		[TestCase("(201) 555-0123", "US")]
		[TestCase("201.555.0123", "US")]
		[TestCase("+1 201 555 0123", null)]
		public void Process_returns_e164_without_separators(string input, string region)
		{
			var result = _provider.Process(input, region);

			result.IsValid.Should().BeTrue();
			result.InternationalNumber.Should().Be("+12015550123");
		}

		[TestCase("+61 2 5550 1234", null, "+61255501234")]
		[TestCase("+44 20 7946 0958", null, "+442079460958")]
		public void Process_returns_e164_without_separators_outside_the_us(string input, string region, string expected)
		{
			var result = _provider.Process(input, region);

			result.IsValid.Should().BeTrue();
			result.InternationalNumber.Should().Be(expected);
		}

		// ── Real stored formats ───────────────────────────────────────────────────────
		// Sampled from the production UserProfiles.MobileNumber column. The normalization sweep
		// (Resgrid.Console --NormalizePhoneNumbers) runs each stored value through this provider, so
		// these are the shapes it has to be able to convert.

		[TestCase("(270) 555-0101", "+12705550101")]
		[TestCase("270-555-0102", "+12705550102")]
		[TestCase("270-555-0103", "+12705550103")]
		[TestCase("(574) 555-0104", "+15745550104")]
		[TestCase("(815) 555-0105", "+18155550105")]
		[TestCase("802-555-0106", "+18025550106")]
		[TestCase("970-555-0107", "+19705550107")]
		[TestCase("(812) 555-0108", "+18125550108")]
		[TestCase("(501) 555-0109", "+15015550109")]
		public void Process_converts_punctuated_us_numbers_to_e164(string stored, string expected)
		{
			var result = _provider.Process(stored, "US");

			result.IsValid.Should().BeTrue();
			result.InternationalNumber.Should().Be(expected);
		}

		[TestCase("7135550110", "+17135550110")]
		[TestCase("2315550111", "+12315550111")]
		[TestCase("8125550108", "+18125550108")]
		[TestCase("+17755550112", "+17755550112")]
		public void Process_converts_bare_and_already_canonical_us_numbers_to_e164(string stored, string expected)
		{
			var result = _provider.Process(stored, "US");

			result.IsValid.Should().BeTrue();
			result.InternationalNumber.Should().Be(expected);
		}

		[Test]
		public void Process_maps_the_two_stored_formats_of_one_number_onto_the_same_value()
		{
			// "(812) 555-0108" and "8125550108" sit on different profiles in the sampled data. After the
			// sweep they collide on one number, which is what the verified-first ordering in the profile
			// lookup exists to arbitrate - and what the sweep reports as a collision.
			_provider.Process("(812) 555-0108", "US").InternationalNumber
				.Should().Be(_provider.Process("8125550108", "US").InternationalNumber);
		}

		[TestCase("(043) 555-0118")]
		[TestCase("(041) 555-0119")]
		public void Process_rejects_leading_zero_area_codes_under_the_us_region(string stored)
		{
			// Non-US national formats. The sweep must not rewrite these - it reports them for review
			// instead, because guessing a country code would produce a number that dials elsewhere.
			var result = _provider.Process(stored, "US");

			result.IsValid.Should().BeFalse();
		}
		// ── Shapes recovered from the normalization sweep's skip list ─────────────────
		// Every case below was reported as "does not parse to a valid number" against real stored
		// data. Values here are synthetic stand-ins with the same structure.

		[TestCase("0040722555017", "+40722555017")]      // Romania
		[TestCase("00306945550123", "+306945550123")]    // Greece
		[TestCase("00359877555012", "+359877555012")]    // Bulgaria
		[TestCase("00212607555012", "+212607555012")]    // Morocco
		[TestCase("0044 7400 555012", "+447400555012")]  // spaced, United Kingdom
		public void Process_reads_00_as_the_international_prefix(string stored, string expected)
		{
			// "00" is the international access code across most of the world - the typed equivalent of
			// "+" - and stored numbers routinely use it. Under a US region hint it parses as nothing.
			var result = _provider.Process(stored, "US");

			result.IsValid.Should().BeTrue();
			result.InternationalNumber.Should().Be(expected);
		}

		[TestCase("447400555012", "+447400555012")]
		[TestCase("61255501234", "+61255501234")]
		public void Process_accepts_a_country_code_with_no_plus(string stored, string expected)
		{
			var result = _provider.Process(stored, "US");

			result.IsValid.Should().BeTrue();
			result.InternationalNumber.Should().Be(expected);
		}

		[Test]
		public void Process_ignores_bracket_styles_the_parser_does_not_know()
		{
			// "{201} 555-0123" is a real stored shape; "()" parses and "{}" did not.
			var result = _provider.Process("{201} 555-0123", "US");

			result.IsValid.Should().BeTrue();
			result.InternationalNumber.Should().Be("+12015550123");
		}

		[Test]
		public void Process_ignores_invisible_formatting_characters()
		{
			// Numbers pasted in from other applications carry bidi/format marks that are invisible in
			// every UI but stop the number parsing.
			var result = _provider.Process("+1 201 555 0123‬", "US");

			result.IsValid.Should().BeTrue();
			result.InternationalNumber.Should().Be("+12015550123");
		}

		[Test]
		public void Process_reports_the_region_the_number_actually_belongs_to()
		{
			// Lets the sweep learn a department's country from the numbers that already parse, and
			// reuse it for the national-format ones that cannot be read without it.
			_provider.Process("+447400555012", null).Region.Should().Be("GB");
			_provider.Process("+61255501234", null).Region.Should().Be("AU");
			_provider.Process("+12015550123", null).Region.Should().Be("US");
		}

		[TestCase("07400555012", "GB", "+447400555012")]
		[TestCase("0491570156", "AU", "+61491570156")]
		[TestCase("0272555012", "NZ", "+64272555012")]
		[TestCase("0824555012", "ZA", "+27824555012")]
		[TestCase("0722555017", "RO", "+40722555017")]
		public void Process_reads_a_national_number_once_the_region_is_known(string stored, string region, string expected)
		{
			// The single biggest cause of skipped rows: a perfectly good national number read against
			// the wrong country. Same values under "US" fail outright.
			_provider.Process(stored, "US").IsValid.Should().BeFalse();

			var result = _provider.Process(stored, region);

			result.IsValid.Should().BeTrue();
			result.InternationalNumber.Should().Be(expected);
		}

		[TestCase("jbusby")]
		[TestCase("N/A")]
		[TestCase("Tom ellis")]
		[TestCase("someone@example.com")]
		[TestCase("00000")]
		[TestCase("9999999999")]
		[TestCase("1010101010")]
		[TestCase("705")]
		[TestCase("")]
		[TestCase(null)]
		public void Process_still_rejects_what_is_not_a_number(string stored)
		{
			// The recovery attempts must not turn junk into a number that dials somewhere.
			_provider.Process(stored, "US").IsValid.Should().BeFalse();
		}
	}
}
