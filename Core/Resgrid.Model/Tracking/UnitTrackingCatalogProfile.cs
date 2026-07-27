using System;
using System.Collections.Generic;

namespace Resgrid.Model.Tracking
{
	public sealed class UnitTrackingCatalogProfile
	{
		public string Key { get; set; }
		public string ManufacturerKey { get; set; }
		public string ManufacturerName { get; set; }
		public string Model { get; set; }
		public UnitTrackingTransportType TransportType { get; set; }
		public string ProtocolKey { get; set; }
		public string PayloadAdapterKey { get; set; }
		public string DecoderVariant { get; set; }
		public IReadOnlyCollection<string> SupportedTransports { get; set; } =
			Array.Empty<string>();
		public IReadOnlyCollection<string> CertifiedTransports { get; set; } =
			Array.Empty<string>();
		public UnitTrackingCertificationStatus CertificationStatus { get; set; }
		public string ProtocolDocumentVersion { get; set; }
		public string IoMapKey { get; set; }
		public bool IdentifierRequired { get; set; }
		public bool IsSelectable { get; set; }
		public IReadOnlyCollection<UnitTrackingAuthMode> SupportedAuthModes { get; set; } =
			Array.Empty<UnitTrackingAuthMode>();
		public string SetupSummary { get; set; }
		public string RetryExpectation { get; set; }
	}
}
