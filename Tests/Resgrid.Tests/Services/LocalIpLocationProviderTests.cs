using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using Resgrid.Config;
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
				var provider = new LocalIpLocationProvider();

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
	}
}
