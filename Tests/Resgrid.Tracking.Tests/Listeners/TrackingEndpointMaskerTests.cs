using System.Net;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.TrackerGateway.Listeners;

namespace Resgrid.Tracking.Tests.Listeners
{
	[TestFixture]
	public class TrackingEndpointMaskerTests
	{
		[Test]
		public void Mask_Ipv4Endpoint_ZerosHostOctet()
		{
			// Arrange
			var endpoint = new IPEndPoint(
				IPAddress.Parse("192.0.2.45"),
				41234);

			// Act
			var masked = TrackingEndpointMasker.Mask(endpoint);

			// Assert
			masked.Should().Be("192.0.2.0:41234");
		}

		[Test]
		public void Mask_Ipv6Endpoint_ZerosHostHalf()
		{
			// Arrange
			var endpoint = new IPEndPoint(
				IPAddress.Parse("2001:db8:1234:5678:90ab:cdef:1234:5678"),
				41234);

			// Act
			var masked = TrackingEndpointMasker.Mask(endpoint);

			// Assert
			masked.Should().Be("[2001:db8:1234:5678::]:41234");
		}

		[Test]
		public void Mask_NonIpEndpoint_DoesNotRenderRawValue()
		{
			// Arrange
			var endpoint = new DnsEndPoint(
				"sensitive-device.example",
				41234);

			// Act
			var masked = TrackingEndpointMasker.Mask(endpoint);

			// Assert
			masked.Should().Be("unknown");
		}
	}
}
