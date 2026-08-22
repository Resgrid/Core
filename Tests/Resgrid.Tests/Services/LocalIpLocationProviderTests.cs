using System;
using System.IO;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model.Providers;
using Resgrid.Model.Security;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	[NonParallelizable]
	public class LocalIpLocationProviderTests
	{
		[Test]
		public async Task longest_matching_local_cidr_provides_only_coarse_location()
		{
			var originalPath = SessionSecurityConfig.IpLocationDatabasePath;
			var path = Path.GetTempFileName();
			try
			{
				await File.WriteAllTextAsync(path, """
				[
				  { "network": "203.0.0.0/16", "country": "US", "region": "Broad" },
				  { "network": "203.0.113.0/24", "country": "US", "region": "California", "city": "Example City" }
				]
				""");
				SessionSecurityConfig.IpLocationDatabasePath = path;
				var provider = new LocalIpLocationProvider(PassThroughCache().Object);

				var result = await provider.GetApproximateLocationAsync("203.0.113.9");

				Assert.That(result.Country, Is.EqualTo("US"));
				Assert.That(result.Region, Is.EqualTo("California"));
				Assert.That(result.City, Is.EqualTo("Example City"));
			}
			finally
			{
				SessionSecurityConfig.IpLocationDatabasePath = originalPath;
				File.Delete(path);
			}
		}

		[Test]
		public async Task repeat_lookups_are_served_through_the_shared_cache_provider()
		{
			var originalPath = SessionSecurityConfig.IpLocationDatabasePath;
			var originalCacheEnabled = SystemBehaviorConfig.CacheEnabled;
			var path = Path.GetTempFileName();
			try
			{
				await File.WriteAllTextAsync(path, """
				[ { "network": "203.0.113.0/24", "country": "US", "region": "California" } ]
				""");
				SessionSecurityConfig.IpLocationDatabasePath = path;
				SystemBehaviorConfig.CacheEnabled = true;

				var cache = PassThroughCache();
				var provider = new LocalIpLocationProvider(cache.Object);

				await provider.GetApproximateLocationAsync("203.0.113.9");
				await provider.GetApproximateLocationAsync("203.0.113.9");

				// Both lookups go through the cache-aside path rather than a process-local dictionary,
				// and both derive the same key from the rule-set stamp and the address.
				cache.Verify(x => x.RetrieveAsync(It.IsAny<string>(),
					It.IsAny<Func<Task<IpLocationResult>>>(), It.IsAny<TimeSpan>()), Times.Exactly(2));
			}
			finally
			{
				SessionSecurityConfig.IpLocationDatabasePath = originalPath;
				SystemBehaviorConfig.CacheEnabled = originalCacheEnabled;
				File.Delete(path);
			}
		}

		/// <summary>A cache that always misses, so the fallback runs and the real rule matching is exercised.</summary>
		private static Mock<ICacheProvider> PassThroughCache()
		{
			var cache = new Mock<ICacheProvider>();
			cache.Setup(x => x.RetrieveAsync(It.IsAny<string>(),
					It.IsAny<Func<Task<IpLocationResult>>>(), It.IsAny<TimeSpan>()))
				.Returns<string, Func<Task<IpLocationResult>>, TimeSpan>((_, fallback, _) => fallback());
			return cache;
		}
	}
}
