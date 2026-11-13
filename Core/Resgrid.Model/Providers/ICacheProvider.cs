using System;
using System.Threading.Tasks;

namespace Resgrid.Model.Providers
{
	public interface ICacheProvider
	{
		T Retrieve<T>(string cacheKey, Func<T> fallbackFunction, TimeSpan expiration) where T : class;
		Task<T> RetrieveAsync<T>(string cacheKey, Func<Task<T>> fallbackFunction, TimeSpan expiration);
		void Remove(string cacheKey);
		Task<bool> RemoveAsync(string cacheKey);
		bool IsConnected();
		Task<bool> SetStringAsync(string cacheKey, string value, TimeSpan expiration);
		Task<string> GetStringAsync(string cacheKey);

		/// <summary>
		/// Like <see cref="GetStringAsync"/>, but tells a missing key from a read that never happened: Succeeded is true with
		/// a null Value when the key is not there, and false when the cache is off, disconnected, timed out or failed. For
		/// readers that must not take an outage for "nothing stored" (counters summed into a report).
		/// </summary>
		Task<(bool Succeeded, string Value)> TryGetStringAsync(string cacheKey);
		Task<T> GetAsync<T>(string cacheKey) where T : class;

		/// <summary>
		/// Atomically returns the string stored at the key, or stores <paramref name="valueIfAbsent"/> and
		/// returns it when the key is missing (concurrent callers all get the first value stored). Either
		/// way the key's expiration is reset, so a key in regular use never ages out. Returns null only
		/// when the cache is unavailable — unlike <see cref="GetStringAsync"/>, null never means "absent".
		/// </summary>
		Task<string> GetOrAddStringAsync(string cacheKey, string valueIfAbsent, TimeSpan slidingExpiration);

		/// <summary>
		/// Atomically increments a counter and returns the new value. Sets the expiration on first
		/// increment (when the value becomes 1). Returns 0 when the cache is unavailable.
		/// </summary>
		Task<long> IncrementAsync(string cacheKey, TimeSpan expiration);

		/// <summary>
		/// Atomically adds <paramref name="amount"/> to a counter and returns the new value. Sets the expiration when the
		/// counter has none yet. Returns 0 when the cache is unavailable.
		/// </summary>
		Task<long> IncrementByAsync(string cacheKey, long amount, TimeSpan expiration);
	}
}
