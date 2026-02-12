using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Resgrid.Web.Mcp.Infrastructure
{
	/// <summary>
	/// Response caching service for MCP server
	/// </summary>
	public interface IResponseCache
	{
		Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiration = null);
		void Remove(string key);
		void Clear();
	}

	public sealed class ResponseCache : IResponseCache
	{
		private readonly IMemoryCache _cache;
		private readonly ILogger<ResponseCache> _logger;
		private static readonly TimeSpan DefaultExpiration = TimeSpan.FromMinutes(5);

		public ResponseCache(IMemoryCache cache, ILogger<ResponseCache> logger)
		{
			_cache = cache;
			_logger = logger;
		}

		public async Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiration = null)
		{
			if (_cache.TryGetValue(key, out T cachedValue))
			{
				_logger.LogDebug("Cache hit for key: {Key}", key);
				return cachedValue;
			}

			_logger.LogDebug("Cache miss for key: {Key}", key);
			var value = await factory();

			var cacheOptions = new MemoryCacheEntryOptions
			{
				AbsoluteExpirationRelativeToNow = expiration ?? DefaultExpiration
			};

			_cache.Set(key, value, cacheOptions);
			return value;
		}

		public void Remove(string key)
		{
			_cache.Remove(key);
			_logger.LogDebug("Removed cache key: {Key}", key);
		}

		public void Clear()
		{
			if (_cache is MemoryCache memoryCache)
			{
				memoryCache.Compact(1.0);
				_logger.LogInformation("Cache cleared");
			}
		}
	}
}

