using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Providers.GeoLocationProvider;

namespace Resgrid.Tests.Providers
{
	/// <summary>
	/// Geocoding runs through the chain MappingConfig.GeocodingProviders lists (plan §6.4), and Plus Codes decode
	/// locally so they work without Google (D25).
	/// </summary>
	[TestFixture]
	public class GeocodingChainTests
	{
		private string _providers;
		private CultureInfo _culture;

		[SetUp]
		public void SetUp()
		{
			_providers = MappingConfig.GeocodingProviders;
			_culture = CultureInfo.CurrentCulture;
		}

		[TearDown]
		public void TearDown()
		{
			MappingConfig.GeocodingProviders = _providers;
			CultureInfo.CurrentCulture = _culture;
		}

		private static Mock<ICacheProvider> PassThroughCache()
		{
			var cache = new Mock<ICacheProvider>();
			cache.Setup(c => c.RetrieveAsync(It.IsAny<string>(), It.IsAny<Func<Task<string>>>(), It.IsAny<TimeSpan>()))
				.Returns((string _, Func<Task<string>> fallback, TimeSpan __) => fallback());
			return cache;
		}

		private static Mock<IGeocodingProvider> Geocoder(string name, GeocodeMatch forward, string reverse = null, bool configured = true)
		{
			var geocoder = new Mock<IGeocodingProvider>();
			geocoder.SetupGet(g => g.Name).Returns(name);
			geocoder.SetupGet(g => g.IsConfigured).Returns(configured);
			geocoder.Setup(g => g.ForwardAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(forward);
			geocoder.Setup(g => g.ReverseAsync(It.IsAny<double>(), It.IsAny<double>(), It.IsAny<CancellationToken>())).ReturnsAsync(reverse);
			return geocoder;
		}

		[Test]
		public async Task chain_order_follows_config_and_skips_empty_answers()
		{
			MappingConfig.GeocodingProviders = "nominatim,google";
			var google = Geocoder("google", new GeocodeMatch { Latitude = 1, Longitude = 2 });
			var nominatim = Geocoder("nominatim", null);
			var provider = new GeoLocationProvider(PassThroughCache().Object, new[] { google.Object, nominatim.Object });

			var result = await provider.GetLatLonFromAddress("1 Main St");

			result.Should().Be("1,2");
			nominatim.Verify(g => g.ForwardAsync("1 Main St", It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task providers_not_listed_or_not_configured_are_never_called()
		{
			MappingConfig.GeocodingProviders = "nominatim,mapbox";
			var google = Geocoder("google", new GeocodeMatch { Latitude = 1, Longitude = 2 });
			var mapbox = Geocoder("mapbox", new GeocodeMatch { Latitude = 3, Longitude = 4 }, configured: false);
			var nominatim = Geocoder("nominatim", null);
			var provider = new GeoLocationProvider(PassThroughCache().Object, new[] { google.Object, mapbox.Object, nominatim.Object });

			var result = await provider.GetLatLonFromAddress("1 Main St");

			result.Should().BeNull();
			google.Verify(g => g.ForwardAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
			mapbox.Verify(g => g.ForwardAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task coordinates_are_always_invariant_lat_comma_lon()
		{
			// Callers split on "," and double.Parse; a comma-decimal culture must not change the format.
			CultureInfo.CurrentCulture = new CultureInfo("de-DE");
			MappingConfig.GeocodingProviders = "nominatim";
			var nominatim = Geocoder("nominatim", new GeocodeMatch { Latitude = 47.5, Longitude = 8.25 });
			var provider = new GeoLocationProvider(PassThroughCache().Object, new[] { nominatim.Object });

			(await provider.GetLatLonFromAddress("Zurich")).Should().Be("47.5,8.25");
		}

		[Test]
		public async Task reverse_geocoding_walks_the_chain()
		{
			MappingConfig.GeocodingProviders = "mapbox,nominatim";
			var mapbox = Geocoder("mapbox", null, reverse: null);
			var nominatim = Geocoder("nominatim", null, reverse: "1 Main St, Reno");
			var provider = new GeoLocationProvider(PassThroughCache().Object, new[] { mapbox.Object, nominatim.Object });

			(await provider.GetAddressFromLatLong(39.5, -119.8)).Should().Be("1 Main St, Reno");
		}

		// GeocodeAddressAsync: bulk POI imports retry a lookup no geocoder answered, but never one a geocoder answered with
		// "no such address" (the address itself is the problem).

		private static Mock<IGeocodingProvider> Failing(string name)
		{
			var geocoder = Geocoder(name, null);
			geocoder.Setup(g => g.ForwardAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new GeocodingUnavailableException(name, 503));
			return geocoder;
		}

		[Test]
		public async Task classified_lookup_returns_the_first_match()
		{
			MappingConfig.GeocodingProviders = "google,locationiq";
			var provider = new GeoLocationProvider(PassThroughCache().Object, new[] { Failing("google").Object, Geocoder("locationiq", new GeocodeMatch { Latitude = 41.5, Longitude = -93.5 }).Object });

			var result = await provider.GeocodeAddressAsync("1 Main St");

			result.Outcome.Should().Be(GeocodeOutcome.Found);
			result.Latitude.Should().Be(41.5);
			result.Longitude.Should().Be(-93.5);
		}

		[Test]
		public async Task an_answer_without_a_match_is_not_found_even_when_another_geocoder_failed()
		{
			MappingConfig.GeocodingProviders = "google,locationiq";
			var provider = new GeoLocationProvider(PassThroughCache().Object, new[] { Geocoder("google", null).Object, Failing("locationiq").Object });

			(await provider.GeocodeAddressAsync("99999 Imaginary Rd")).Outcome.Should().Be(GeocodeOutcome.NotFound);
		}

		[Test]
		public async Task a_match_at_0_0_or_out_of_range_is_skipped_for_the_next_geocoder_and_never_cached()
		{
			MappingConfig.GeocodingProviders = "google,locationiq,nominatim";
			string cached = null;
			var cache = new Mock<ICacheProvider>();
			cache.Setup(c => c.RetrieveAsync(It.IsAny<string>(), It.IsAny<Func<Task<string>>>(), It.IsAny<TimeSpan>()))
				.Returns(async (string _, Func<Task<string>> fallback, TimeSpan __) => cached = await fallback());
			var provider = new GeoLocationProvider(cache.Object, new[]
			{
				Geocoder("google", new GeocodeMatch { Latitude = 0, Longitude = 0 }).Object,
				Geocoder("locationiq", new GeocodeMatch { Latitude = -93.5, Longitude = 241.5 }).Object,
				Geocoder("nominatim", new GeocodeMatch { Latitude = 41.5, Longitude = -93.5 }).Object
			});

			var result = await provider.GeocodeAddressAsync("1 Main St");

			result.Outcome.Should().Be(GeocodeOutcome.Found);
			result.Latitude.Should().Be(41.5);
			result.Longitude.Should().Be(-93.5);
			cached.Should().Be("41.5,-93.5");
		}

		[Test]
		public async Task only_invalid_matches_are_unavailable_not_a_position()
		{
			MappingConfig.GeocodingProviders = "google";
			var provider = new GeoLocationProvider(PassThroughCache().Object, new[] { Geocoder("google", new GeocodeMatch { Latitude = 0, Longitude = 0 }).Object });

			(await provider.GeocodeAddressAsync("1 Main St")).Outcome.Should().Be(GeocodeOutcome.Unavailable);
		}

		[Test]
		public async Task no_answer_from_any_geocoder_is_unavailable()
		{
			MappingConfig.GeocodingProviders = "google,locationiq";
			var timeout = Geocoder("locationiq", null);
			timeout.Setup(g => g.ForwardAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new TaskCanceledException());
			var provider = new GeoLocationProvider(PassThroughCache().Object, new[] { Failing("google").Object, timeout.Object });

			(await provider.GeocodeAddressAsync("1 Main St")).Outcome.Should().Be(GeocodeOutcome.Unavailable);
		}

		[Test]
		public async Task no_configured_geocoder_is_not_configured()
		{
			MappingConfig.GeocodingProviders = "google";
			var provider = new GeoLocationProvider(PassThroughCache().Object, new[] { Geocoder("google", null, configured: false).Object });

			(await provider.GeocodeAddressAsync("1 Main St")).Outcome.Should().Be(GeocodeOutcome.NotConfigured);
		}

		[Test]
		public async Task a_cached_position_is_found_without_asking_a_geocoder()
		{
			MappingConfig.GeocodingProviders = "google";
			var google = Geocoder("google", null);
			var cache = new Mock<ICacheProvider>();
			cache.Setup(c => c.RetrieveAsync(It.IsAny<string>(), It.IsAny<Func<Task<string>>>(), It.IsAny<TimeSpan>())).ReturnsAsync("41.5,-93.5");
			var provider = new GeoLocationProvider(cache.Object, new[] { google.Object });

			var result = await provider.GeocodeAddressAsync("1 Main St");

			result.Outcome.Should().Be(GeocodeOutcome.Found);
			result.Latitude.Should().Be(41.5);
			google.Verify(g => g.ForwardAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}
	}

	[TestFixture]
	public class OpenLocationCodeTests
	{
		[Test]
		public void decodes_a_full_code()
		{
			OpenLocationCode.TryDecodeFull("8FVC2222+22", out var latitude, out var longitude).Should().BeTrue();

			latitude.Should().BeApproximately(47.0000625, 1e-7);
			longitude.Should().BeApproximately(8.0000625, 1e-7);
		}

		[Test]
		public void decodes_a_full_code_followed_by_text()
		{
			OpenLocationCode.TryDecodeFull("8FVC2222+22 Zurich", out var latitude, out _).Should().BeTrue();

			latitude.Should().BeApproximately(47.0000625, 1e-7);
		}

		[Test]
		public void encodes_a_point()
		{
			OpenLocationCode.Encode(47.0000625, 8.0000625).Should().Be("8FVC2222+22");
		}

		[Test]
		public void recovers_a_short_code_near_the_reference()
		{
			OpenLocationCode.TryRecoverShort("2222+22", 47.3, 8.2, out var latitude, out var longitude).Should().BeTrue();

			latitude.Should().BeApproximately(47.0000625, 1e-7);
			longitude.Should().BeApproximately(8.0000625, 1e-7);
		}

		[TestCase("2222+22")]
		[TestCase("1 Main Street")]
		[TestCase("")]
		public void short_codes_and_addresses_are_not_full_codes(string code)
		{
			OpenLocationCode.TryDecodeFull(code, out _, out _).Should().BeFalse();
		}
	}
}
