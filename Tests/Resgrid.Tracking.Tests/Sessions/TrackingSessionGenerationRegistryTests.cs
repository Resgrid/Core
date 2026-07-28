using System.Threading;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.TrackerGateway.Sessions;

namespace Resgrid.Tracking.Tests.Sessions
{
	[TestFixture]
	public class TrackingSessionGenerationRegistryTests
	{
		[Test]
		public void Activate_NewerSession_CancelsAndSupersedesOlderGeneration()
		{
			// Arrange
			var registry = new TrackingSessionGenerationRegistry();
			using var firstCancellation = new CancellationTokenSource();
			using var secondCancellation = new CancellationTokenSource();
			using var first = registry.Activate(
				"device-1",
				firstCancellation);

			// Act
			using var second = registry.Activate(
				"device-1",
				secondCancellation);

			// Assert
			firstCancellation.IsCancellationRequested.Should().BeTrue();
			secondCancellation.IsCancellationRequested.Should().BeFalse();
			first.IsCurrent.Should().BeFalse();
			second.IsCurrent.Should().BeTrue();
			second.Generation.Should().BeGreaterThan(first.Generation);
			registry.ActiveCount.Should().Be(1);
		}

		[Test]
		public void Dispose_StaleLease_DoesNotRemoveCurrentGeneration()
		{
			// Arrange
			var registry = new TrackingSessionGenerationRegistry();
			using var firstCancellation = new CancellationTokenSource();
			using var secondCancellation = new CancellationTokenSource();
			var first = registry.Activate(
				"device-1",
				firstCancellation);
			var second = registry.Activate(
				"device-1",
				secondCancellation);

			// Act
			first.Dispose();

			// Assert
			second.IsCurrent.Should().BeTrue();
			registry.ActiveCount.Should().Be(1);

			second.Dispose();
			registry.ActiveCount.Should().Be(0);
		}
	}
}
