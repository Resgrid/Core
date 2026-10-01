using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Resgrid.Model.Providers;

namespace Resgrid.Tests.Security.Live
{
	/// <summary>
	/// The cache the sign-in endpoints use (attempt counters, the SAML relay), in memory: counters and strings behave as the
	/// Redis provider's do, and the cache-aside calls always run their fallback.
	/// </summary>
	internal sealed class InMemoryCacheProvider : ICacheProvider
	{
		private readonly ConcurrentDictionary<string, (string Value, DateTime ExpiresOn)> _strings = new();
		private readonly ConcurrentDictionary<string, (long Value, DateTime ExpiresOn)> _counters = new();

		public T Retrieve<T>(string cacheKey, Func<T> fallbackFunction, TimeSpan expiration) where T : class => fallbackFunction();

		public Task<T> RetrieveAsync<T>(string cacheKey, Func<Task<T>> fallbackFunction, TimeSpan expiration) => fallbackFunction();

		public void Remove(string cacheKey)
		{
			_strings.TryRemove(cacheKey, out _);
			_counters.TryRemove(cacheKey, out _);
		}

		public Task<bool> RemoveAsync(string cacheKey)
		{
			var removed = _strings.TryRemove(cacheKey, out _) | _counters.TryRemove(cacheKey, out _);
			return Task.FromResult(removed);
		}

		public bool IsConnected() => true;

		public Task<bool> SetStringAsync(string cacheKey, string value, TimeSpan expiration)
		{
			_strings[cacheKey] = (value, DateTime.UtcNow.Add(expiration));
			return Task.FromResult(true);
		}

		public Task<string> GetStringAsync(string cacheKey) =>
			Task.FromResult(_strings.TryGetValue(cacheKey, out var entry) && entry.ExpiresOn > DateTime.UtcNow ? entry.Value : null);

		public Task<T> GetAsync<T>(string cacheKey) where T : class => Task.FromResult<T>(null);

		public Task<string> GetOrAddStringAsync(string cacheKey, string valueIfAbsent, TimeSpan slidingExpiration)
		{
			var now = DateTime.UtcNow;
			var entry = _strings.AddOrUpdate(cacheKey, (valueIfAbsent, now.Add(slidingExpiration)),
				(_, existing) => (existing.ExpiresOn > now ? existing.Value : valueIfAbsent, now.Add(slidingExpiration)));
			return Task.FromResult(entry.Value);
		}

		public Task<long> IncrementAsync(string cacheKey, TimeSpan expiration)
		{
			var now = DateTime.UtcNow;
			var entry = _counters.AddOrUpdate(cacheKey, (1, now.Add(expiration)),
				(_, existing) => existing.ExpiresOn > now ? (existing.Value + 1, existing.ExpiresOn) : (1, now.Add(expiration)));
			return Task.FromResult(entry.Value);
		}
	}
}
