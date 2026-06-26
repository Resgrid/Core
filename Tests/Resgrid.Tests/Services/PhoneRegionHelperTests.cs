using FluentAssertions;
using NUnit.Framework;
using Resgrid.Framework;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	public class PhoneRegionHelperTests
	{
		[Test]
		public void ToIso_MapsKnownCountries()
		{
			PhoneRegionHelper.ToIso("South Africa").Should().Be("za");
			PhoneRegionHelper.ToIso("United States").Should().Be("us");
			PhoneRegionHelper.ToIso("United Kingdom").Should().Be("gb");
		}

		[Test]
		public void ToIso_IsCaseAndWhitespaceInsensitive()
		{
			PhoneRegionHelper.ToIso("  south africa  ").Should().Be("za");
		}

		[Test]
		public void ToIso_ReturnsNullForUnknownOrBlank()
		{
			PhoneRegionHelper.ToIso("Atlantis").Should().BeNull();
			PhoneRegionHelper.ToIso("").Should().BeNull();
			PhoneRegionHelper.ToIso(null).Should().BeNull();
		}
	}
}
