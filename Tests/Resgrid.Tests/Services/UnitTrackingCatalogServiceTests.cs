using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Tracking;
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
				.Should().BeEquivalentTo(
					"generic-https",
					"traccar-forwarder",
					"teltonika-fmc920",
					"teltonika-fmm920",
					"teltonika-fmc130",
					"teltonika-fmm130",
					"teltonika-fmc003",
					"queclink-gv57mg",
					"queclink-gv350mg",
					"queclink-gv500ma",
					"jimi-vl103m",
					"jimi-jm-vl03",
					"teltonika-fmm003",
					"teltonika-fmc125",
					"teltonika-fmm125",
					"teltonika-fmc150",
					"teltonika-fmm150",
					"teltonika-fmc230",
					"teltonika-fmm230",
					"jimi-jm-vl01",
					"jimi-jm-vl02",
					"jimi-jm-vl04");
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
				!string.IsNullOrWhiteSpace(profile.ProtocolKey) &&
				!string.IsNullOrWhiteSpace(profile.DecoderVariant) &&
				!string.IsNullOrWhiteSpace(profile.ProtocolDocumentVersion) &&
				profile.SupportedTransports.Count > 0);
			profiles.Select(profile => profile.Key)
				.Should().OnlyHaveUniqueItems();
		}

		[Test]
		public async Task GetProfilesAsync_TeltonikaWaveOne_RemainsCandidateWithDataDrivenIoMap()
		{
			// Arrange
			var service = new UnitTrackingCatalogService();
			var waveOneKeys = new[]
			{
				"teltonika-fmc920",
				"teltonika-fmm920",
				"teltonika-fmc130",
				"teltonika-fmm130",
				"teltonika-fmc003"
			};

			// Act
			var profiles = await service.GetProfilesAsync();
			var teltonika = profiles
				.Where(profile => waveOneKeys.Contains(
					profile.Key,
					StringComparer.Ordinal))
				.ToList();

			// Assert
			teltonika.Should().HaveCount(5);
			teltonika.Select(profile => profile.Model)
				.Should().BeEquivalentTo(
					"FMC920",
					"FMM920",
					"FMC130",
					"FMM130",
					"FMC003");
			teltonika.Should().OnlyContain(profile =>
				profile.TransportType ==
				UnitTrackingTransportType.NativeTcpUdp &&
				profile.ProtocolKey == "teltonika-codec8" &&
				profile.DecoderVariant == "codec8-or-8e" &&
				profile.IoMapKey ==
				"teltonika-fmb-03-29-common" &&
				profile.CertificationStatus ==
				UnitTrackingCertificationStatus.Candidate &&
				!profile.IsSelectable &&
				profile.CertifiedTransports.Count == 0 &&
				profile.SupportedAuthModes.Count == 0);
			teltonika.Should().OnlyContain(profile =>
				profile.SupportedTransports
					.OrderBy(transport => transport)
					.SequenceEqual(
						new[] { "Tcp", "Udp" }));
			var ioMap = UnitTrackingCatalog.GetIoMap(
				"teltonika-fmb-03-29-common");
			ioMap.Should().NotBeNull();
			ioMap.Mappings.Select(mapping => mapping.AvlId)
				.Should().BeEquivalentTo(
					new[] { 66, 182, 239, 240 });
		}

		[Test]
		public async Task GetProfilesAsync_WaveTwo_RemainsUnselectableWithoutUnverifiedMappings()
		{
			// Arrange
			var service = new UnitTrackingCatalogService();
			var waveTwoKeys = new[]
			{
				"teltonika-fmm003",
				"teltonika-fmc125",
				"teltonika-fmm125",
				"teltonika-fmc150",
				"teltonika-fmm150",
				"teltonika-fmc230",
				"teltonika-fmm230",
				"jimi-jm-vl01",
				"jimi-jm-vl02",
				"jimi-jm-vl04"
			};

			// Act
			var profiles = await service.GetProfilesAsync();
			var waveTwo = profiles
				.Where(profile => waveTwoKeys.Contains(
					profile.Key,
					StringComparer.Ordinal))
				.ToList();

			// Assert
			waveTwo.Should().HaveCount(waveTwoKeys.Length);
			waveTwo.Select(profile => profile.Key)
				.Should().BeEquivalentTo(waveTwoKeys);
			waveTwo.Should().OnlyContain(profile =>
				profile.CertificationStatus ==
				UnitTrackingCertificationStatus.Candidate &&
				!profile.IsSelectable &&
				profile.CertifiedTransports.Count == 0 &&
				profile.SupportedAuthModes.Count == 0 &&
				string.IsNullOrWhiteSpace(profile.IoMapKey) &&
				profile.ProtocolDocumentVersion.Contains(
					"certification pending",
					StringComparison.Ordinal));
			waveTwo.Where(profile =>
					profile.ManufacturerKey == "teltonika")
				.Should().OnlyContain(profile =>
					profile.ProtocolKey == "teltonika-codec8" &&
					profile.DecoderVariant == "codec8-or-8e" &&
					profile.SupportedTransports
						.OrderBy(transport => transport)
						.SequenceEqual(
							new[] { "Tcp", "Udp" }));
			waveTwo.Where(profile =>
					profile.ManufacturerKey == "jimi")
				.Should().OnlyContain(profile =>
					profile.ProtocolKey == "gt06" &&
					profile.DecoderVariant.EndsWith(
						"-unverified",
						StringComparison.Ordinal) &&
					profile.SupportedTransports.SequenceEqual(
						new[] { "Tcp" }));
		}

		[Test]
		public async Task GetProfilesAsync_QueclinkAndJimiWaveOne_RemainTcpCandidates()
		{
			// Arrange
			var service = new UnitTrackingCatalogService();

			// Act
			var profiles = await service.GetProfilesAsync();
			var candidates = profiles
				.Where(profile =>
					profile.Key == "queclink-gv57mg" ||
					profile.Key == "queclink-gv350mg" ||
					profile.Key == "queclink-gv500ma" ||
					profile.Key == "jimi-vl103m" ||
					profile.Key == "jimi-jm-vl03")
				.ToList();

			// Assert
			candidates.Select(profile => profile.Key)
				.Should().BeEquivalentTo(
					"queclink-gv57mg",
					"queclink-gv350mg",
					"queclink-gv500ma",
					"jimi-vl103m",
					"jimi-jm-vl03");
			candidates.Should().OnlyContain(profile =>
				profile.TransportType ==
				UnitTrackingTransportType.NativeTcpUdp &&
				profile.CertificationStatus ==
				UnitTrackingCertificationStatus.Candidate &&
				!profile.IsSelectable &&
				profile.CertifiedTransports.Count == 0 &&
				profile.SupportedAuthModes.Count == 0 &&
				profile.SupportedTransports.SequenceEqual(
					new[] { "Tcp" }));
			candidates.Where(profile =>
					profile.ManufacturerKey == "queclink")
				.Should().OnlyContain(profile =>
					profile.ProtocolKey == "queclink-attrack" &&
					profile.DecoderVariant ==
					"gl200-text-bounded");
			candidates.Where(profile =>
					profile.ManufacturerKey == "jimi")
				.Should().OnlyContain(profile =>
					profile.ProtocolKey == "gt06" &&
					profile.DecoderVariant.StartsWith(
						"gt06-",
						StringComparison.Ordinal));
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
