using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Providers.Weather
{
	public class WeatherAlertResponseCache
	{
		private static readonly ConcurrentDictionary<string, CacheEntry> _cache = new();

		public static int DefaultCacheMinutes { get; set; } = 10;

		public static bool TryGet(WeatherAlertSourceType sourceType, string areaFilter, out List<WeatherAlert> alerts)
		{
			var key = BuildKey(sourceType, areaFilter);
			if (_cache.TryGetValue(key, out var entry) && entry.ExpiresUtc > DateTime.UtcNow)
			{
				alerts = entry.Alerts;
				return true;
			}
			alerts = null;
			return false;
		}

		public static void Set(WeatherAlertSourceType sourceType, string areaFilter, List<WeatherAlert> alerts, int? cacheMinutes = null)
		{
			var key = BuildKey(sourceType, areaFilter);
			var ttl = cacheMinutes ?? DefaultCacheMinutes;
			_cache[key] = new CacheEntry
			{
				Alerts = alerts,
				ExpiresUtc = DateTime.UtcNow.AddMinutes(ttl)
			};
		}

		public static void Clear()
		{
			_cache.Clear();
		}

		private static string BuildKey(WeatherAlertSourceType sourceType, string areaFilter)
		{
			return $"{sourceType}:{areaFilter ?? ""}".ToLowerInvariant();
		}

		private class CacheEntry
		{
			public List<WeatherAlert> Alerts { get; set; }
			public DateTime ExpiresUtc { get; set; }
		}
	}
}
