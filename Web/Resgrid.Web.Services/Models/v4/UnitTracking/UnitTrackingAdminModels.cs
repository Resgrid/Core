using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Resgrid.Web.Services.Models.v4.UnitTracking
{
	public sealed class UnitTrackingCatalogProfileData
	{
		public string Key { get; set; }
		public string ManufacturerKey { get; set; }
		public string ManufacturerName { get; set; }
		public string Model { get; set; }
		public int TransportType { get; set; }
		public string TransportTypeName { get; set; }
		public string ProtocolKey { get; set; }
		public string PayloadAdapterKey { get; set; }
		public int CertificationStatus { get; set; }
		public string CertificationStatusName { get; set; }
		public bool IdentifierRequired { get; set; }
		public bool IsSelectable { get; set; }
		public IReadOnlyCollection<int> SupportedAuthModes { get; set; } = Array.Empty<int>();
		public string SetupSummary { get; set; }
		public string RetryExpectation { get; set; }
	}

	public sealed class UnitTrackingDeviceData
	{
		public string UnitTrackingDeviceId { get; set; }
		public int UnitId { get; set; }
		public string DisplayName { get; set; }
		public string ManufacturerKey { get; set; }
		public string ModelKey { get; set; }
		public int TransportType { get; set; }
		public string TransportTypeName { get; set; }
		public string ProtocolKey { get; set; }
		public string PayloadAdapterKey { get; set; }
		public string DeviceIdentifier { get; set; }
		public string SecondaryIdentifier { get; set; }
		public bool IdentifierMasked { get; set; }
		public bool IsEnabled { get; set; }
		public int SourcePriority { get; set; }
		public string AllowedSourceCidrs { get; set; }
		public DateTime? LastSeenOn { get; set; }
		public DateTime? LastPositionOn { get; set; }
		public DateTime? LastReceivedOn { get; set; }
		public int Status { get; set; }
		public string StatusName { get; set; }
		public string LastErrorCode { get; set; }
		public string FirmwareVersion { get; set; }
		public DateTime CreatedOn { get; set; }
		public DateTime? UpdatedOn { get; set; }
		public IReadOnlyCollection<UnitTrackingCredentialData> Credentials { get; set; } =
			Array.Empty<UnitTrackingCredentialData>();
	}

	public sealed class UnitTrackingCredentialData
	{
		public string UnitTrackingCredentialId { get; set; }
		public int AuthMode { get; set; }
		public string AuthModeName { get; set; }
		public string HeaderName { get; set; }
		public string BasicUsername { get; set; }
		public string KeyPrefix { get; set; }
		public DateTime ValidFrom { get; set; }
		public DateTime? ExpiresOn { get; set; }
		public DateTime? RevokedOn { get; set; }
		public DateTime? LastUsedOn { get; set; }
		public DateTime CreatedOn { get; set; }
	}

	public sealed class UnitTrackingCredentialProvisionData
	{
		public UnitTrackingCredentialData Credential { get; set; }
		public string Token { get; set; }
		public string EndpointUrl { get; set; }
		public string HeaderName { get; set; }
		public string HeaderValue { get; set; }
		public string BasicUsername { get; set; }
	}

	public sealed class CreateUnitTrackingDeviceInput
	{
		[Required]
		[MaxLength(64)]
		public string ProfileKey { get; set; }

		[MaxLength(200)]
		public string DisplayName { get; set; }

		[MaxLength(128)]
		public string DeviceIdentifier { get; set; }

		[MaxLength(128)]
		public string SecondaryIdentifier { get; set; }

		public int SourcePriority { get; set; } = 100;

		[MaxLength(2048)]
		public string AllowedSourceCidrs { get; set; }

		[MaxLength(128)]
		public string FirmwareVersion { get; set; }
	}

	public sealed class UpdateUnitTrackingDeviceInput
	{
		[Required]
		[MaxLength(64)]
		public string ProfileKey { get; set; }

		[MaxLength(200)]
		public string DisplayName { get; set; }

		[MaxLength(128)]
		public string DeviceIdentifier { get; set; }

		[MaxLength(128)]
		public string SecondaryIdentifier { get; set; }

		public bool IsEnabled { get; set; } = true;
		public int SourcePriority { get; set; } = 100;

		[MaxLength(2048)]
		public string AllowedSourceCidrs { get; set; }

		[MaxLength(128)]
		public string FirmwareVersion { get; set; }
	}

	public sealed class CreateUnitTrackingCredentialInput
	{
		[Range(1, 4)]
		public int AuthMode { get; set; }

		[MaxLength(128)]
		public string HeaderName { get; set; }

		[MaxLength(128)]
		public string BasicUsername { get; set; }
	}

	public sealed class RotateUnitTrackingCredentialInput
	{
		[Range(0, 168)]
		public int? OverlapHours { get; set; }
	}

	public sealed class RebindUnitTrackingDeviceInput
	{
		[Range(1, int.MaxValue)]
		public int UnitId { get; set; }
	}
}
