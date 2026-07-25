using System.Net;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Web.Services.ApplicationCore.UnitTracking;

namespace Resgrid.Tests.Web.Services
{
	[TestFixture]
	public class UnitTrackingNetworkPolicyTests
	{
		[TestCase("10.10.2.15", "10.10.0.0/16", true)]
		[TestCase("10.11.2.15", "10.10.0.0/16", false)]
		[TestCase("203.0.113.9", "192.0.2.0/24, 203.0.113.9/32", true)]
		[TestCase("2001:db8::42", "2001:db8::/32", true)]
		public void IsAllowed_EnforcesConfiguredCidrs(string address, string ranges, bool expected)
		{
			UnitTrackingNetworkPolicy.IsAllowed(IPAddress.Parse(address), ranges)
				.Should().Be(expected);
		}

		[Test]
		public void IsAllowed_InvalidConfiguredCidr_FailsClosed()
		{
			UnitTrackingNetworkPolicy.IsAllowed(
					IPAddress.Parse("203.0.113.9"),
					"not-a-cidr")
				.Should().BeFalse();
		}
	}
}
