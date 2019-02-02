// Original From: http://www.roelvanlisdonk.nl/?p=3354

using System;
using System.Collections.Concurrent;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class InternalCacheService : IInternalCacheService
	{
		/// <summary>
		/// Holds all cached data (thread safe).
		/// It is declared as static readonly, so it will be initialised only once per appdomain and will never be null.
		/// In this case it is not a problem that is always created (lazy loading is not needed here).
		/// Using a static readonly variable ensure thread safety.
		/// </summary>
		private static readonly ConcurrentDictionary<string, Tuple<DateTime, object>> _cache = new ConcurrentDictionary<string, Tuple<DateTime, object>>();

		/// <summary>
		/// Constructor
		/// </summary>
		public InternalCacheService()
			: this(null)
		{
		}

		/// <summary>
		/// Added for testability.
		/// Uses the ResetCache function to reset the cache, when needed.
		/// </summary>
		public InternalCacheService(ConcurrentDictionary<string, Tuple<DateTime, object>> cache)
		{
			if (cache != null)
			{
				ResetCache(cache);
			}
		}

		/// <summary>
		/// Adds the given data to the cache (with a key based on the given type), if it does not exist.
		/// Updates the data in the cache (with a key based on the given type), if it does exists.
		/// </summary>
		public void AddOrUpdateCache<T>(string key, T value, TimeSpan cacheLength)
		{
			var cacheItem = new Tuple<DateTime, object>(DateTime.UtcNow, value);
			object updatedValue = _cache.AddOrUpdate(key, cacheItem, (x, existingVal) =>
			{
				existingVal = cacheItem;

				return existingVal;
			});
		}

		/// <summary>
		/// Gets cached data base on the given type.
		/// </summary>
		public T GetCachedData<T>(string key, TimeSpan cacheLength)
		{
			Tuple<DateTime, object> result;
			bool succeeded = _cache.TryGetValue(key, out result);

			if (succeeded && result != null && result.Item2 != null)
			{
				if (result.Item1.Add(cacheLength) < DateTime.UtcNow)
				{
					Invalidate(key);
				}
				else
				{
					return (T) result.Item2;
				}
			}

			return default(T);
		}

		/// <summary>
		/// Gets cached data base on the given type, if the key is not found, the given func is executed with the given parameter to get the data and this data will be stored in the cache.
		/// </summary>
		public T GetCachedData<T>(string key, TimeSpan cacheLength, Func<T> getData)
		{
			Tuple<DateTime, object> result;
			bool succeeded = _cache.TryGetValue(key, out result);

			if (succeeded && result != null && result.Item2 != null)
			{
				if (result.Item1.Add(cacheLength) < DateTime.UtcNow)
				{
					Invalidate(key);
				}
				else
				{
					return (T)result.Item2;
				}
			}

			return getData();
		}

		public bool Invalidate(string key)
		{
			Tuple<DateTime, object> result;
			bool succeeded = _cache.TryRemove(key, out result);

			return succeeded;
		}

		/// <summary>
		/// This function is added for testability and return the total count of cached items.
		/// </summary>
		/// <returns></returns>
		public int Count()
		{
			return _cache.Count;
		}

		/// <summary>
		/// Resets all cached data, by clearing the current cache or using the given cache, when this given cache is not null.
		/// </summary>
		public void ResetCache(ConcurrentDictionary<string, Tuple<DateTime, object>> cache = null)
		{
			// Clear current cache.
			_cache.Clear();

			if (cache != null)
			{
				foreach (var item in cache)
				{
					bool succeeded = _cache.TryAdd(item.Key, item.Value);
					if (!succeeded)
					{
						throw new ApplicationException(string.Format("Could not add item with key [{0}] and value [{1}]", item.Key,
							item.Value));
					}
				}
			}
		}
	}
}