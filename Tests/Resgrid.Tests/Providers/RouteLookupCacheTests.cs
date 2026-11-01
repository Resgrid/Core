using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Providers.GeoLocationProvider;

namespace Resgrid.Tests.Providers
{
	/// <summary>
	/// Route (drive time) lookups: a route found is cached for 30 days, a failed lookup only for a few minutes. A failure
	/// used to be cached for the full 30 days, so one provider error (or a missing key) left every route it touched on
	/// straight-line estimates for a month.
	/// </summary>
	[TestFixture]
	public class RouteLookupCacheTests
	{
		private const string Key = "route:34.05,-118.27:34.06,-118.26";
		private static readonly DateTime Now = new DateTime(2026, 10, 31, 16, 0, 0, DateTimeKind.Utc);

		private FakeCache _cache;
		private int _lookups;

		[SetUp]
		public void SetUp()
		{
			_cache = new FakeCache();
			_lookups = 0;
		}

		private Func<Task<RouteInformation>> Lookup(bool successful, DateTime processedOn) => () =>
		{
			_lookups++;
			return Task.FromResult(new RouteInformation { Successful = successful, ProcessedOn = processedOn, Seconds = successful ? 360 : 0 });
		};

		private Task<RouteInformation> Get(Func<Task<RouteInformation>> lookup) => RouteLookupCache.GetAsync(_cache, Key, lookup, () => Now);

		[Test]
		public async Task A_route_found_is_cached_for_the_success_length()
		{
			var first = await Get(Lookup(true, Now));
			var second = await Get(Lookup(true, Now));

			first.Seconds.Should().Be(360);
			second.Seconds.Should().Be(360);
			_lookups.Should().Be(1);
			_cache.Expirations[Key].Should().Be(RouteLookupCache.SuccessLength);
		}

		[Test]
		public async Task A_recent_failure_is_not_looked_up_again_on_every_request()
		{
			_cache.Items[Key] = new RouteInformation { Successful = false, ProcessedOn = Now.AddMinutes(-3) };

			var route = await Get(Lookup(true, Now));

			route.Successful.Should().BeFalse("a failure younger than the failure length is trusted");
			_lookups.Should().Be(0);
		}

		[Test]
		public async Task A_failure_older_than_the_failure_length_is_looked_up_again()
		{
			_cache.Items[Key] = new RouteInformation { Successful = false, ProcessedOn = Now.AddDays(-2) };

			var route = await Get(Lookup(true, Now));

			route.Successful.Should().BeTrue();
			route.Seconds.Should().Be(360);
			_lookups.Should().Be(1);
			((RouteInformation)_cache.Items[Key]).Successful.Should().BeTrue("the fresh route replaces the stale failure");
		}

		[Test]
		public async Task A_failure_cached_before_lookups_were_stamped_is_looked_up_again()
		{
			// Failures cached by older code have no ProcessedOn (e.g. a REQUEST_DENIED status never set it).
			_cache.Items[Key] = new RouteInformation { Successful = false };

			var route = await Get(Lookup(true, Now));

			route.Successful.Should().BeTrue();
			_lookups.Should().Be(1);
		}

		[Test]
		public async Task A_fresh_failure_is_returned_without_a_second_lookup()
		{
			var route = await Get(Lookup(false, Now));

			route.Successful.Should().BeFalse();
			_lookups.Should().Be(1, "a failure just looked up is not retried straight away");
		}

		[Test]
		public async Task A_stale_failure_that_fails_again_is_kept_with_the_new_time()
		{
			_cache.Items[Key] = new RouteInformation { Successful = false, ProcessedOn = Now.AddHours(-1) };

			var route = await Get(Lookup(false, Now));

			route.Successful.Should().BeFalse();
			route.ProcessedOn.Should().Be(Now);
			_lookups.Should().Be(1);

			// Within the failure length it is trusted again.
			await Get(Lookup(true, Now));
			_lookups.Should().Be(1);
		}

		/// <summary>The cache-aside behaviour of AzureRedisCacheProvider.RetrieveAsync, in memory.</summary>
		private sealed class FakeCache : ICacheProvider
		{
			public Dictionary<string, object> Items { get; } = new Dictionary<string, object>();
			public Dictionary<string, TimeSpan> Expirations { get; } = new Dictionary<string, TimeSpan>();

			public async Task<T> RetrieveAsync<T>(string cacheKey, Func<Task<T>> fallbackFunction, TimeSpan expiration)
			{
				if (Items.TryGetValue(cacheKey, out var value))
					return (T)value;

				var data = await fallbackFunction();
				Items[cacheKey] = data;
				Expirations[cacheKey] = expiration;
				return data;
			}

			public Task<bool> RemoveAsync(string cacheKey) => Task.FromResult(Items.Remove(cacheKey));

			public void Remove(string cacheKey) => Items.Remove(cacheKey);

			public T Retrieve<T>(string cacheKey, Func<T> fallbackFunction, TimeSpan expiration) where T : class => throw new NotSupportedException();

			public bool IsConnected() => true;

			public Task<bool> SetStringAsync(string cacheKey, string value, TimeSpan expiration) => throw new NotSupportedException();

			public Task<string> GetStringAsync(string cacheKey) => throw new NotSupportedException();

			public Task<T> GetAsync<T>(string cacheKey) where T : class => throw new NotSupportedException();

			public Task<string> GetOrAddStringAsync(string cacheKey, string valueIfAbsent, TimeSpan slidingExpiration) => throw new NotSupportedException();

			public Task<long> IncrementAsync(string cacheKey, TimeSpan expiration) => throw new NotSupportedException();
		}
	}
}
