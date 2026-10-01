using Microsoft.Extensions.Localization;
using Resgrid.Model;

namespace Resgrid.Web.Helpers
{
	/// <summary>
	/// Display text for the hardware tracking enums on the UnitTracking pages. Unknown uses the Common
	/// resource; a value without a label falls back to its enum name rather than rendering blank.
	/// </summary>
	public static class UnitTrackingDisplayHelper
	{
		public static string GetLocalizedAuthMode(UnitTrackingAuthMode mode, IStringLocalizer<Resgrid.Localization.Areas.User.Units.Units> localizer, IStringLocalizer<Resgrid.Localization.Common> commonLocalizer)
		{
			switch (mode)
			{
				case UnitTrackingAuthMode.Unknown:
					return commonLocalizer["Unknown"].Value;
				case UnitTrackingAuthMode.Bearer:
					return localizer["AuthModeBearer"].Value;
				case UnitTrackingAuthMode.Basic:
					return localizer["AuthModeBasic"].Value;
				case UnitTrackingAuthMode.CustomHeader:
					return localizer["AuthModeCustomHeader"].Value;
				case UnitTrackingAuthMode.CapabilityPath:
					return localizer["AuthModeCapabilityPath"].Value;
				default:
					return mode.ToString();
			}
		}

		public static string GetLocalizedTransport(UnitTrackingTransportType transport, IStringLocalizer<Resgrid.Localization.Areas.User.Units.Units> localizer, IStringLocalizer<Resgrid.Localization.Common> commonLocalizer)
		{
			switch (transport)
			{
				case UnitTrackingTransportType.Unknown:
					return commonLocalizer["Unknown"].Value;
				case UnitTrackingTransportType.NativeHttps:
					return localizer["TransportNativeHttps"].Value;
				case UnitTrackingTransportType.ManagedHttpsJson:
					return localizer["TransportManagedHttpsJson"].Value;
				case UnitTrackingTransportType.NativeTcpUdp:
					return localizer["TransportNativeTcpUdp"].Value;
				case UnitTrackingTransportType.ProtocolGateway:
					return localizer["TransportProtocolGateway"].Value;
				default:
					return transport.ToString();
			}
		}

		public static string GetLocalizedDeviceStatus(UnitTrackingDeviceStatus status, IStringLocalizer<Resgrid.Localization.Areas.User.Units.Units> localizer)
		{
			switch (status)
			{
				case UnitTrackingDeviceStatus.NeverSeen:
					return localizer["DeviceStatusNeverSeen"].Value;
				case UnitTrackingDeviceStatus.Online:
					return localizer["DeviceStatusOnline"].Value;
				case UnitTrackingDeviceStatus.Stale:
					return localizer["DeviceStatusStale"].Value;
				case UnitTrackingDeviceStatus.Error:
					return localizer["DeviceStatusError"].Value;
				case UnitTrackingDeviceStatus.Disabled:
					return localizer["DeviceStatusDisabled"].Value;
				default:
					return status.ToString();
			}
		}

		public static string GetLocalizedCertificationStatus(UnitTrackingCertificationStatus status, IStringLocalizer<Resgrid.Localization.Areas.User.Units.Units> localizer, IStringLocalizer<Resgrid.Localization.Common> commonLocalizer)
		{
			switch (status)
			{
				case UnitTrackingCertificationStatus.Unknown:
					return commonLocalizer["Unknown"].Value;
				case UnitTrackingCertificationStatus.Candidate:
					return localizer["CertificationCandidate"].Value;
				case UnitTrackingCertificationStatus.FixtureVerified:
					return localizer["CertificationFixtureVerified"].Value;
				case UnitTrackingCertificationStatus.HardwareVerified:
					return localizer["CertificationHardwareVerified"].Value;
				case UnitTrackingCertificationStatus.Certified:
					return localizer["CertificationCertified"].Value;
				case UnitTrackingCertificationStatus.Deprecated:
					return localizer["CertificationDeprecated"].Value;
				default:
					return status.ToString();
			}
		}
	}
}
