using System;
using System.Security.Cryptography;
using System.Text;

namespace Resgrid.Services
{
	internal static class UnitTrackingCacheKeys
	{
		public static string Endpoint(string deviceId) =>
			$"UnitTracking:Endpoint:{Digest(deviceId)}";

		public static string Credential(string secretHash) =>
			$"UnitTracking:Credential:{secretHash?.ToLowerInvariant()}";

		public static string DeviceCredentials(string deviceId) =>
			$"UnitTracking:DeviceCredentials:{Digest(deviceId)}";

		public static string ProtocolIdentifier(string protocolKey, string deviceIdentifier) =>
			$"UnitTracking:Protocol:{Digest($"{protocolKey}|{deviceIdentifier}")}";

		private static string Digest(string value)
		{
			if (string.IsNullOrWhiteSpace(value))
				throw new ArgumentNullException(nameof(value));

			return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
				.ToLowerInvariant();
		}
	}
}
