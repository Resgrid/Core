using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Providers.GeoLocationProvider;

namespace Resgrid.Tests.Providers
{
	/// <summary>
	/// Geocode and route results are cached in Redis for 30 days and shared by every web replica and worker, so the
	/// key for a given input has to be the same in every process. The keys used to come from string.GetHashCode,
	/// which .NET randomizes per process: the cache never hit across processes and, at 32 bits, two addresses could
	/// share a key. The pinned values below were computed outside .NET (Python hashlib.sha256 over the normalized
	/// input, first 32 hex chars), so a key that matches them is the same in any process.
	/// </summary>
	[TestFixture]
	public class GeoLocationCacheKeysTests
	{
		[Test]
		public void ForwardGeocode_should_match_the_pinned_sha256_key()
		{
			GeoLocationCacheKeys.ForwardGeocode("123 Main St, Springfield, IL")
				.Should().Be("FGeo2_395f588c4d955af5c43a7e4400da765a");
		}

		[Test]
		public void ReverseGeocode_should_match_the_pinned_sha256_key()
		{
			GeoLocationCacheKeys.ReverseGeocode(47.5, -122.25)
				.Should().Be("RGeo2_1a92dd69fc23e08022eac5fcd742e37a");
		}

		[Test]
		public void Route_by_coordinates_should_match_the_pinned_sha256_key()
		{
			GeoLocationCacheKeys.Route(47.5, -122.25, 47.6, -122.3)
				.Should().Be("Route2_06d31261e25e823647f1988a750fef4e");
		}

		[Test]
		public void Route_by_text_should_match_the_pinned_sha256_key()
		{
			GeoLocationCacheKeys.Route("123 Main St", "47.6,-122.3")
				.Should().Be("Route2_aea1c2e6424562a8bde7f472233a4717");
		}

		[Test]
		public void ReverseW3W_should_match_the_pinned_sha256_key()
		{
			GeoLocationCacheKeys.ReverseW3W(47.5, -122.25)
				.Should().Be("RW3W2_1a92dd69fc23e08022eac5fcd742e37a");
		}

		[Test]
		public void Keys_should_be_the_same_on_every_call()
		{
			var address = "1600 Pennsylvania Ave NW, Washington, DC 20500";

			Enumerable.Range(0, 5).Select(_ => GeoLocationCacheKeys.ForwardGeocode(address)).Distinct().Should().ContainSingle();
			Enumerable.Range(0, 5).Select(_ => GeoLocationCacheKeys.ReverseGeocode(38.8977, -77.0365)).Distinct().Should().ContainSingle();
			Enumerable.Range(0, 5).Select(_ => GeoLocationCacheKeys.Route(38.8977, -77.0365, 38.8899, -77.0091)).Distinct().Should().ContainSingle();
			Enumerable.Range(0, 5).Select(_ => GeoLocationCacheKeys.Route(address, "38.8899,-77.0091")).Distinct().Should().ContainSingle();
		}

		[Test]
		public void Keys_should_not_depend_on_the_current_culture()
		{
			var invariant = Capture();
			var original = CultureInfo.CurrentCulture;

			try
			{
				// de-DE formats 47.5 as "47,5"; the key must not change with the request's culture.
				CultureInfo.CurrentCulture = new CultureInfo("de-DE");
				Capture().Should().Equal(invariant);
			}
			finally
			{
				CultureInfo.CurrentCulture = original;
			}

			static string[] Capture() => new[]
			{
				GeoLocationCacheKeys.ReverseGeocode(47.5, -122.25),
				GeoLocationCacheKeys.Route(47.5, -122.25, 47.6, -122.3),
				GeoLocationCacheKeys.ReverseW3W(47.5, -122.25)
			};
		}

		[Test]
		public void ForwardGeocode_should_ignore_case_and_whitespace_differences()
		{
			var key = GeoLocationCacheKeys.ForwardGeocode("123 Main St, Springfield, IL");

			GeoLocationCacheKeys.ForwardGeocode("  123 MAIN st,   Springfield,\tIL \r\n").Should().Be(key);
		}

		[Test]
		public void ForwardGeocode_should_differ_for_different_addresses()
		{
			GeoLocationCacheKeys.ForwardGeocode("123 Main St, Springfield, IL")
				.Should().NotBe(GeoLocationCacheKeys.ForwardGeocode("124 Main St, Springfield, IL"));
		}

		[Test]
		public void ForwardGeocode_should_not_collide_across_many_addresses()
		{
			var keys = Enumerable.Range(1, 20000)
				.Select(n => GeoLocationCacheKeys.ForwardGeocode($"{n} Main St, Springfield, IL"))
				.ToList();

			keys.Distinct().Should().HaveCount(keys.Count);
		}

		[Test]
		public void ReverseGeocode_should_differ_for_different_coordinates()
		{
			var key = GeoLocationCacheKeys.ReverseGeocode(47.5, -122.25);

			GeoLocationCacheKeys.ReverseGeocode(47.5, -122.26).Should().NotBe(key);
			GeoLocationCacheKeys.ReverseGeocode(-122.25, 47.5).Should().NotBe(key);
		}

		[Test]
		public void ReverseGeocode_should_treat_negative_zero_as_zero()
		{
			GeoLocationCacheKeys.ReverseGeocode(-0.0, 10).Should().Be(GeoLocationCacheKeys.ReverseGeocode(0.0, 10));
		}

		[Test]
		public void Route_should_differ_by_direction()
		{
			GeoLocationCacheKeys.Route(47.5, -122.25, 47.6, -122.3)
				.Should().NotBe(GeoLocationCacheKeys.Route(47.6, -122.3, 47.5, -122.25));

			GeoLocationCacheKeys.Route("Station 1", "123 Main St")
				.Should().NotBe(GeoLocationCacheKeys.Route("123 Main St", "Station 1"));
		}

		[Test]
		public void Route_by_text_should_not_shift_words_between_start_and_end()
		{
			GeoLocationCacheKeys.Route("123 Main", "St")
				.Should().NotBe(GeoLocationCacheKeys.Route("123", "Main St"));
		}

		[Test]
		public void Route_overloads_should_not_share_a_key_for_the_same_points()
		{
			// The two GetRoute overloads cache different payloads, so the same points must not land on one key.
			GeoLocationCacheKeys.Route(47.5, -122.25, 47.6, -122.3)
				.Should().NotBe(GeoLocationCacheKeys.Route("47.5,-122.25", "47.6,-122.3"));
		}

		[Test]
		public void Reverse_lookups_should_not_share_a_key_for_the_same_point()
		{
			GeoLocationCacheKeys.ReverseGeocode(47.5, -122.25)
				.Should().NotBe(GeoLocationCacheKeys.ReverseW3W(47.5, -122.25));
		}
	}

	[TestFixture]
	public class GeoLocationProviderCacheKeyTests
	{
		[Test]
		public async Task GetLatLonFromAddress_should_use_the_same_key_from_separate_instances()
		{
			var keys = new List<string>();
			var cache = new Mock<ICacheProvider>();
			cache.Setup(c => c.RetrieveAsync(It.IsAny<string>(), It.IsAny<Func<Task<string>>>(), It.IsAny<TimeSpan>()))
				.Callback<string, Func<Task<string>>, TimeSpan>((key, _, _) => keys.Add(key))
				.ReturnsAsync("39.7817,-89.6501");

			var first = await new GeoLocationProvider(cache.Object).GetLatLonFromAddress("123 Main St, Springfield, IL");
			var second = await new GeoLocationProvider(cache.Object).GetLatLonFromAddress("123 Main St, Springfield, IL");

			first.Should().Be("39.7817,-89.6501");
			second.Should().Be(first);
			keys.Should().Equal("FGeo2_395f588c4d955af5c43a7e4400da765a", "FGeo2_395f588c4d955af5c43a7e4400da765a");
		}

		[Test]
		public async Task GetAddressFromLatLong_should_use_the_same_key_from_separate_instances()
		{
			var keys = new List<string>();
			var cache = new Mock<ICacheProvider>();
			cache.Setup(c => c.RetrieveAsync(It.IsAny<string>(), It.IsAny<Func<Task<string>>>(), It.IsAny<TimeSpan>()))
				.Callback<string, Func<Task<string>>, TimeSpan>((key, _, _) => keys.Add(key))
				.ReturnsAsync("123 Main St");

			await new GeoLocationProvider(cache.Object).GetAddressFromLatLong(47.5, -122.25);
			await new GeoLocationProvider(cache.Object).GetAproxAddressFromLatLong(47.5, -122.25);

			keys.Should().Equal("RGeo2_1a92dd69fc23e08022eac5fcd742e37a", "RGeo2_1a92dd69fc23e08022eac5fcd742e37a");
		}

		[Test]
		public async Task GetRoute_should_use_the_same_key_from_separate_instances()
		{
			var keys = new List<string>();
			var cache = new Mock<ICacheProvider>();
			cache.Setup(c => c.RetrieveAsync(It.IsAny<string>(), It.IsAny<Func<Task<RouteInformation>>>(), It.IsAny<TimeSpan>()))
				.Callback<string, Func<Task<RouteInformation>>, TimeSpan>((key, _, _) => keys.Add(key))
				.ReturnsAsync(new RouteInformation { Successful = true });

			await new GeoLocationProvider(cache.Object).GetRoute(47.5, -122.25, 47.6, -122.3);
			await new GeoLocationProvider(cache.Object).GetRoute(47.5, -122.25, 47.6, -122.3);
			await new GeoLocationProvider(cache.Object).GetRoute("123 Main St", "47.6,-122.3");
			await new GeoLocationProvider(cache.Object).GetRoute("123 Main St", "47.6,-122.3");

			keys.Should().Equal(
				"Route2_06d31261e25e823647f1988a750fef4e", "Route2_06d31261e25e823647f1988a750fef4e",
				"Route2_aea1c2e6424562a8bde7f472233a4717", "Route2_aea1c2e6424562a8bde7f472233a4717");
		}

		[Test]
		public async Task GetW3WFromCoordinates_should_use_the_same_key_from_separate_instances()
		{
			var keys = new List<string>();
			var cache = new Mock<ICacheProvider>();
			cache.Setup(c => c.RetrieveAsync(It.IsAny<string>(), It.IsAny<Func<Task<string>>>(), It.IsAny<TimeSpan>()))
				.Callback<string, Func<Task<string>>, TimeSpan>((key, _, _) => keys.Add(key))
				.ReturnsAsync("index.home.raft");

			var coordinates = new Coordinates { Latitude = 47.5, Longitude = -122.25 };
			await new GeoLocationProvider(cache.Object).GetW3WFromCoordinates(coordinates);
			await new GeoLocationProvider(cache.Object).GetW3WFromCoordinates(coordinates);

			keys.Should().Equal("RW3W2_1a92dd69fc23e08022eac5fcd742e37a", "RW3W2_1a92dd69fc23e08022eac5fcd742e37a");
		}

		[TestCase(null)]
		[TestCase("")]
		[TestCase("   ")]
		public async Task GetLatLonFromAddress_should_return_null_for_a_blank_address_without_a_lookup(string address)
		{
			var cache = new Mock<ICacheProvider>(MockBehavior.Strict);

			var result = await new GeoLocationProvider(cache.Object).GetLatLonFromAddress(address);

			result.Should().BeNull();
		}
	}
}
