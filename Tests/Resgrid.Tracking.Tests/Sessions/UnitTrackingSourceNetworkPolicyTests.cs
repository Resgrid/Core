using System.Net;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model.Tracking;

namespace Resgrid.Tracking.Tests.Sessions
{
	[TestFixture]
	public class UnitTrackingSourceNetworkPolicyTests
	{
		[TestCase("10.20.30.40", "10.0.0.0/8", true)]
		[TestCase("192.168.1.10", "10.0.0.0/8", false)]
		[TestCase("2001:db8::10", "2001:db8::/32", true)]
		public void IsAllowed_AddressAndCanonicalRanges_ReturnsExpected(
			string address,
			string ranges,
			bool expected)
		{
			// Act
			var allowed = UnitTrackingSourceNetworkPolicy.IsAllowed(
				IPAddress.Parse(address),
				ranges);

			// Assert
			allowed.Should().Be(expected);
		}

		[Test]
		public void IsAllowed_MappedIpv4Address_MatchesIpv4Range()
		{
			// Arrange
			var mappedAddress = IPAddress.Parse(
				"::ffff:10.20.30.40");

			// Act
			var allowed = UnitTrackingSourceNetworkPolicy.IsAllowed(
				mappedAddress,
				"10.0.0.0/8");

			// Assert
			allowed.Should().BeTrue();
		}
	}
}
