using FluentAssertions;
using NUnit.Framework;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	public class UnitTrackingEventIdServiceTests
	{
		[Test]
		public void CreateForHttps_SameBindingAndCallerId_IsStableAndBounded()
		{
			var service = new UnitTrackingEventIdService();

			var first = service.CreateForHttps("binding-1", "vendor-record-42");
			var retry = service.CreateForHttps("binding-1", "vendor-record-42");
			var otherBinding = service.CreateForHttps("binding-2", "vendor-record-42");

			first.Should().Be(retry);
			first.Should().MatchRegex("^[0-9a-f]{64}$");
			otherBinding.Should().NotBe(first);
		}
	}
}
