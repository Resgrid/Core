using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	public class UnitTrackingCatalogServiceTests
	{
		[Test]
		public async Task GetProfilesAsync_FoundationProfiles_ExposeCertificationAndAdapters()
		{
			// Arrange
			var service = new UnitTrackingCatalogService();

			// Act
			var profiles = await service.GetProfilesAsync();

			// Assert
			profiles.Select(profile => profile.Key)
				.Should().BeEquivalentTo("generic-https", "traccar-forwarder");
			profiles.Single(profile => profile.Key == "generic-https")
				.CertificationStatus.Should().Be(UnitTrackingCertificationStatus.Certified);
			profiles.Single(profile => profile.Key == "traccar-forwarder")
				.CertificationStatus.Should().Be(UnitTrackingCertificationStatus.Candidate);
			profiles.Single(profile => profile.Key == "generic-https")
				.IsSelectable.Should().BeTrue();
			profiles.Single(profile => profile.Key == "traccar-forwarder")
				.IsSelectable.Should().BeFalse();
			profiles.Single(profile => profile.Key == "traccar-forwarder")
				.SupportedAuthModes.Should().Contain(UnitTrackingAuthMode.CapabilityPath);
			profiles.Single(profile => profile.Key == "traccar-forwarder")
				.SetupSummary.Should().Contain("v6.14.5");
			profiles.Should().OnlyContain(profile =>
				!string.IsNullOrWhiteSpace(profile.PayloadAdapterKey) &&
				profile.SupportedAuthModes.Count > 0);
		}

		[Test]
		public async Task GetProfileAsync_KeyComparison_IsCaseInsensitive()
		{
			// Arrange
			var service = new UnitTrackingCatalogService();

			// Act
			var profile = await service.GetProfileAsync(" GENERIC-HTTPS ");

			// Assert
			profile.Should().NotBeNull();
			profile.Key.Should().Be("generic-https");
		}
	}
}
