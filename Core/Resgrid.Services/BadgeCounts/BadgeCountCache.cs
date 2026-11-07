using System;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Providers;

namespace Resgrid.Services.BadgeCounts
{
	/// <summary>Cache-aside for the badge count providers: off when caching is off, never failing a count.</summary>
	internal static class BadgeCountCache
	{
		public static bool IsEnabled(ICacheProvider cacheProvider) => cacheProvider != null && Config.SystemBehaviorConfig.CacheEnabled;

		/// <summary>The department's calls version (rolled by CallsService on every call save, delete and reopen).</summary>
		public static async Task<string> GetCallsVersionAsync(ICacheProvider cacheProvider, int departmentId)
		{
			var version = await cacheProvider.GetStringAsync(BadgeCountCacheKeys.OpenCallsVersion(departmentId));

			return string.IsNullOrWhiteSpace(version) ? "0" : version;
		}

		public static async Task<int> RetrieveAsync(ICacheProvider cacheProvider, string key, Func<Task<int>> count, TimeSpan length)
		{
			if (!IsEnabled(cacheProvider))
				return await count();

			return await cacheProvider.RetrieveAsync(key, count, length);
		}
	}
}
