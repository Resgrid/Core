using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Providers.NumberProvider;

namespace Resgrid.Tests.Providers
{
	/// <summary>
	/// The Twilio AvailablePhoneNumbers queries a dedicated number search becomes (plan §8 D13). Only the query objects are built; Twilio
	/// is never called.
	/// </summary>
	[TestFixture]
	public class DedicatedNumberSearchOptionsTests
	{
		[Test]
		public void A_united_states_search_maps_every_filter_onto_the_local_and_mobile_queries()
		{
			var search = new DedicatedNumberSearch
			{
				CountryCode = "us", Region = "nv", Locality = " Reno ", PostalCode = "89501", AreaCode = "775", Contains = "55",
				NearLatitude = 39.5296, NearLongitude = -119.8138, DistanceMiles = 50
			};

			var local = TwilioProvider.LocalSearchOptions(search, 20);
			local.PathCountryCode.Should().Be("US");
			local.AreaCode.Should().Be(775);
			local.Contains.Should().Be("55");
			local.InRegion.Should().Be("NV");
			local.InLocality.Should().Be("Reno");
			local.InPostalCode.Should().Be("89501");
			local.NearLatLong.Should().Be("39.5296,-119.8138");
			local.Distance.Should().Be(50);
			local.SmsEnabled.Should().BeTrue("a dedicated number takes texts");
			local.VoiceEnabled.Should().BeTrue("and calls");
			local.Limit.Should().Be(20);

			var mobile = TwilioProvider.MobileSearchOptions(search, 20);
			mobile.PathCountryCode.Should().Be("US");
			mobile.AreaCode.Should().Be(775);
			mobile.InRegion.Should().Be("NV");
			mobile.InLocality.Should().Be("Reno");
			mobile.InPostalCode.Should().Be("89501");
			mobile.NearLatLong.Should().Be("39.5296,-119.8138");
			mobile.Distance.Should().Be(50);
			mobile.SmsEnabled.Should().BeTrue();
			mobile.VoiceEnabled.Should().BeTrue();
		}

		[Test]
		public void A_near_point_without_a_radius_searches_25_miles_and_a_radius_is_capped_at_500()
		{
			TwilioProvider.LocalSearchOptions(new DedicatedNumberSearch { CountryCode = "CA", NearLatitude = 45.42, NearLongitude = -75.69 }, 20).Distance.Should().Be(25);
			TwilioProvider.LocalSearchOptions(new DedicatedNumberSearch { CountryCode = "CA", NearLatitude = 45.42, NearLongitude = -75.69, DistanceMiles = 9000 }, 20)
				.Distance.Should().Be(500);
			TwilioProvider.LocalSearchOptions(new DedicatedNumberSearch { CountryCode = "CA", DistanceMiles = 50 }, 20).Distance.Should().BeNull("no near point, no radius");
		}

		[Test]
		public void Outside_the_united_states_and_canada_only_the_city_and_digits_are_sent()
		{
			var options = TwilioProvider.LocalSearchOptions(new DedicatedNumberSearch
			{
				CountryCode = "gb", Region = "London", Locality = "London", PostalCode = "SW1A 1AA", AreaCode = "20", NearLatitude = 51.5, NearLongitude = -0.12
			}, 20);

			options.PathCountryCode.Should().Be("GB");
			options.InLocality.Should().Be("London");
			options.AreaCode.Should().BeNull("Twilio's area code filter is US/Canada only");
			options.Contains.Should().Be("20", "the area code digits narrow the pattern instead");
			options.InRegion.Should().BeNull();
			options.InPostalCode.Should().BeNull();
			options.NearLatLong.Should().BeNull();
			options.Distance.Should().BeNull();
		}

		[Test]
		public void Blank_filters_are_left_out_of_the_query()
		{
			var options = TwilioProvider.LocalSearchOptions(new DedicatedNumberSearch { CountryCode = "US", Region = " ", Locality = "", PostalCode = null }, 5);

			options.InRegion.Should().BeNull();
			options.InLocality.Should().BeNull();
			options.InPostalCode.Should().BeNull();
			options.AreaCode.Should().BeNull();
			options.Contains.Should().BeNull();
			options.Limit.Should().Be(5);
		}
	}
}
