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
	}
}
