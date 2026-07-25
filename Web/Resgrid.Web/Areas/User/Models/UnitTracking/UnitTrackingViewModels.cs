using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Resgrid.Model;
using Resgrid.Model.Tracking;

namespace Resgrid.Web.Areas.User.Models.UnitTracking
{
	public sealed class UnitTrackingIndexView
	{
		public Unit Unit { get; set; }
		public bool CanManage { get; set; }
		public IReadOnlyCollection<UnitTrackingDeviceStatusView> Devices { get; set; } =
			Array.Empty<UnitTrackingDeviceStatusView>();
	}

	public sealed class UnitTrackingDeviceStatusView
	{
		public UnitTrackingDevice Device { get; set; }
		public UnitTrackingDeviceStatus Status { get; set; }
		public string DisplayIdentifier { get; set; }
		public IReadOnlyCollection<UnitTrackingCredential> Credentials { get; set; } =
			Array.Empty<UnitTrackingCredential>();
	}

	public sealed class UnitTrackingEditorView
	{
		public Unit Unit { get; set; }
		public string UnitTrackingDeviceId { get; set; }
		public bool IsEdit { get; set; }
		public IReadOnlyCollection<UnitTrackingCatalogProfile> Profiles { get; set; } =
			Array.Empty<UnitTrackingCatalogProfile>();

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

	public sealed class UnitTrackingDetailsView
	{
		public Unit Unit { get; set; }
		public UnitTrackingDevice Device { get; set; }
		public UnitTrackingCatalogProfile Profile { get; set; }
		public UnitTrackingDeviceStatus Status { get; set; }
		public string DisplayIdentifier { get; set; }
		public bool CanManage { get; set; }
		public bool CanPreviewJson { get; set; }
		public IReadOnlyCollection<UnitTrackingAuthMode> SupportedAuthModes { get; set; } =
			Array.Empty<UnitTrackingAuthMode>();
		public IReadOnlyCollection<UnitTrackingCredential> Credentials { get; set; } =
			Array.Empty<UnitTrackingCredential>();
		public CreateUnitTrackingCredentialView CredentialInput { get; set; } =
			new();
	}

	public sealed class CreateUnitTrackingCredentialView
	{
		[Required]
		public string UnitTrackingDeviceId { get; set; }

		[Range(1, 4)]
		public int AuthMode { get; set; } = (int)UnitTrackingAuthMode.Bearer;

		[MaxLength(128)]
		public string HeaderName { get; set; }

		[MaxLength(128)]
		public string BasicUsername { get; set; }
	}

	public sealed class RotateUnitTrackingCredentialView
	{
		[Required]
		public string UnitTrackingDeviceId { get; set; }

		[Required]
		public string UnitTrackingCredentialId { get; set; }

		[Range(0, 168)]
		public int OverlapHours { get; set; } = 24;
	}

	public sealed class UnitTrackingCredentialDisplayView
	{
		public Unit Unit { get; set; }
		public UnitTrackingDevice Device { get; set; }
		public UnitTrackingCatalogProfile Profile { get; set; }
		public UnitTrackingCredentialProvisionResult Provisioning { get; set; }
	}

	public sealed class PreviewUnitTrackingJsonView
	{
		[Required]
		public string UnitTrackingDeviceId { get; set; }

		[Required]
		public string JsonPayload { get; set; }
	}
}
