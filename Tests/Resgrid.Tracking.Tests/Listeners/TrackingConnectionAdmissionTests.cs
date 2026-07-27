using System.Net;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.TrackerGateway.Listeners;

namespace Resgrid.Tracking.Tests.Listeners
{
	[TestFixture]
	public class TrackingConnectionAdmissionTests
	{
		[Test]
		public void TryAcquire_GlobalLimitReached_RejectsAdditionalConnection()
		{
			// Arrange
			var admission = new TrackingConnectionAdmission(
				maximumConnections: 2,
				maximumConnectionsPerIp: 2);

			// Act
			var firstAccepted = admission.TryAcquire(
				IPAddress.Parse("192.0.2.1"),
				out var firstLease);
			var secondAccepted = admission.TryAcquire(
				IPAddress.Parse("192.0.2.2"),
				out var secondLease);
			var thirdAccepted = admission.TryAcquire(
				IPAddress.Parse("192.0.2.3"),
				out var thirdLease);

			// Assert
			firstAccepted.Should().BeTrue();
			secondAccepted.Should().BeTrue();
			thirdAccepted.Should().BeFalse();
			thirdLease.Should().BeNull();
			admission.CurrentConnections.Should().Be(2);

			firstLease.Dispose();
			secondLease.Dispose();
		}

		[Test]
		public void TryAcquire_Ipv4AndMappedIpv6Address_EnforcesOnePerIpLimit()
		{
			// Arrange
			var admission = new TrackingConnectionAdmission(
				maximumConnections: 2,
				maximumConnectionsPerIp: 1);
			var ipv4 = IPAddress.Parse("192.0.2.10");
			var mappedIpv6 = ipv4.MapToIPv6();

			// Act
			var firstAccepted = admission.TryAcquire(
				ipv4,
				out var firstLease);
			var mappedAccepted = admission.TryAcquire(
				mappedIpv6,
				out var mappedLease);

			// Assert
			firstAccepted.Should().BeTrue();
			mappedAccepted.Should().BeFalse();
			mappedLease.Should().BeNull();

			firstLease.Dispose();
		}

		[Test]
		public void Dispose_ActiveLease_ReleasesAdmissionCapacityOnce()
		{
			// Arrange
			var admission = new TrackingConnectionAdmission(
				maximumConnections: 1,
				maximumConnectionsPerIp: 1);
			admission.TryAcquire(
				IPAddress.Loopback,
				out var firstLease);

			// Act
			firstLease.Dispose();
			firstLease.Dispose();
			var acceptedAgain = admission.TryAcquire(
				IPAddress.Loopback,
				out var secondLease);

			// Assert
			acceptedAgain.Should().BeTrue();
			admission.CurrentConnections.Should().Be(1);

			secondLease.Dispose();
			admission.CurrentConnections.Should().Be(0);
		}
	}
}
