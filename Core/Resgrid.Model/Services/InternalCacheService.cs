using System;
using System.Collections.Concurrent;

namespace Resgrid.Model.Services
{
	public interface IInternalCacheService
	{
		/// <summary>
		/// Adds the given data to the cache (with a key based on the given type), if it does not exist.
		/// Updates the data in the cache (with a key based on the given type), if it does exists.
		/// </summary>
		void AddOrUpdateCache<T>(string key, T value, TimeSpan cacheLength);

		/// <summary>
		/// Gets cached data base on the given type.
		/// </summary>
		T GetCachedData<T>(string key, TimeSpan cacheLength);

		/// <summary>
		/// Gets cached data base on the given type, if the key is not found, the given func is executed to get the data.
		/// </summary>
		T GetCachedData<T>(string key, TimeSpan cacheLength, Func<T> getData);

		bool Invalidate(string key);

		/// <summary>
		/// Resets all cached data, by setting it to null.
		/// </summary>
		void ResetCache(ConcurrentDictionary<string, Tuple<DateTime, object>> cache = null);
	}
}