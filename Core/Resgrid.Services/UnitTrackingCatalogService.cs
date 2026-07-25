using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Model.Tracking;

namespace Resgrid.Services
{
	public class UnitTrackingCatalogService : IUnitTrackingCatalogService
	{
		private static readonly IReadOnlyCollection<UnitTrackingCatalogProfile> Profiles =
			new[]
			{
				new UnitTrackingCatalogProfile
				{
					Key = "generic-https",
					ManufacturerKey = "generic",
					ManufacturerName = "Generic",
					Model = "Resgrid JSON",
					TransportType = UnitTrackingTransportType.NativeHttps,
					ProtocolKey = "resgrid-json",
					PayloadAdapterKey = "resgrid-json-v1",
					CertificationStatus = UnitTrackingCertificationStatus.Certified,
					IdentifierRequired = false,
					IsSelectable = true,
					SupportedAuthModes = new[]
					{
						UnitTrackingAuthMode.Bearer,
						UnitTrackingAuthMode.Basic,
						UnitTrackingAuthMode.CustomHeader,
						UnitTrackingAuthMode.CapabilityPath
					},
					SetupSummary =
						"POST the documented Resgrid JSON position payload to the generated HTTPS endpoint.",
					RetryExpectation =
						"Retry on 429 and 503. Treat 200, 201, 202, and 204 as successful delivery."
				},
				new UnitTrackingCatalogProfile
				{
					Key = "traccar-forwarder",
					ManufacturerKey = "traccar",
					ManufacturerName = "Traccar",
					Model = "Position Forwarding",
					TransportType = UnitTrackingTransportType.ProtocolGateway,
					ProtocolKey = "traccar",
					PayloadAdapterKey = "traccar-json-v1",
					CertificationStatus = UnitTrackingCertificationStatus.Candidate,
					IdentifierRequired = true,
					IsSelectable = false,
					SupportedAuthModes = new[]
					{
						UnitTrackingAuthMode.Bearer,
						UnitTrackingAuthMode.Basic,
						UnitTrackingAuthMode.CustomHeader,
						UnitTrackingAuthMode.CapabilityPath
					},
					SetupSummary =
						"Configure Traccar v6.14.5 JSON position forwarding. Use a per-device capability URL when one Traccar server forwards multiple devices.",
					RetryExpectation =
						"Enable Traccar position-forwarding retries. Resgrid returns 202 after durable queue acceptance and 429 or 503 for retryable failures."
				}
			};

		public Task<IReadOnlyCollection<UnitTrackingCatalogProfile>> GetProfilesAsync(
			CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();
			return Task.FromResult(Profiles);
		}

		public Task<UnitTrackingCatalogProfile> GetProfileAsync(
			string profileKey,
			CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var profile = Profiles.FirstOrDefault(item =>
				string.Equals(item.Key, profileKey?.Trim(), StringComparison.OrdinalIgnoreCase));
			return Task.FromResult(profile);
		}
	}
}
