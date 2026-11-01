using System;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Providers;

namespace Resgrid.Providers.GeoLocationProvider
{
	/// <summary>
	/// How route (drive time) lookups use the cache. A route that was found is kept for <see cref="SuccessLength"/>. A
	/// failed lookup (missing or rejected API key, quota, provider outage, no route) is trusted only for
	/// <see cref="FailureLength"/>, then looked up again. ICacheProvider.RetrieveAsync stores whatever the lookup
	/// returned for the whole expiration, so a failure older than that is dropped and retried here instead of being
	/// read back for a month: one bad minute at the provider used to cost every route it touched its drive times
	/// (straight-line estimates on the nearest board and run card recommendations) for 30 days.
	/// </summary>
	public static class RouteLookupCache
	{
		public static readonly TimeSpan SuccessLength = TimeSpan.FromDays(30);

		/// <summary>Long enough that a broken key is not asked again on every board refresh, short enough to recover quickly.</summary>
		public static readonly TimeSpan FailureLength = TimeSpan.FromMinutes(10);

		/// <param name="lookup">Asks the provider; must stamp <see cref="RouteInformation.ProcessedOn"/> with the time it ran.</param>
		/// <param name="utcNow">The clock; tests pass their own.</param>
		public static async Task<RouteInformation> GetAsync(ICacheProvider cache, string cacheKey, Func<Task<RouteInformation>> lookup, Func<DateTime> utcNow = null)
		{
			var now = utcNow ?? (() => DateTime.UtcNow);

			var route = await cache.RetrieveAsync(cacheKey, lookup, SuccessLength);
			if (route == null || route.Successful || now() - route.ProcessedOn < FailureLength)
				return route;

			// A failure older than FailureLength, or one cached before lookups were stamped (ProcessedOn unset): forget it
			// and ask the provider again, once.
			await cache.RemoveAsync(cacheKey);

			return await cache.RetrieveAsync(cacheKey, lookup, SuccessLength);
		}
	}
}
