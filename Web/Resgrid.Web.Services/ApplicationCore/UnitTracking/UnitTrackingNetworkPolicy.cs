using System;
using System.Linq;
using System.Net;

namespace Resgrid.Web.Services.ApplicationCore.UnitTracking
{
	public static class UnitTrackingNetworkPolicy
	{
		public static bool IsAllowed(IPAddress remoteAddress, string allowedSourceCidrs)
		{
			if (string.IsNullOrWhiteSpace(allowedSourceCidrs))
				return true;
			if (remoteAddress == null)
				return false;

			var ranges = allowedSourceCidrs
				.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
			return ranges.Length > 0 && ranges.Any(range => Contains(range, remoteAddress));
		}

		private static bool Contains(string range, IPAddress candidate)
		{
			var parts = range.Split('/', 2, StringSplitOptions.TrimEntries);
			if (!IPAddress.TryParse(parts[0], out var network))
				return false;

			var candidateAddress = NormalizeFamily(candidate, network.AddressFamily);
			if (candidateAddress == null)
				return false;

			var networkBytes = network.GetAddressBytes();
			var candidateBytes = candidateAddress.GetAddressBytes();
			var maximumPrefix = networkBytes.Length * 8;
			var prefix = maximumPrefix;
			if (parts.Length == 2 &&
			    (!int.TryParse(parts[1], out prefix) || prefix < 0 || prefix > maximumPrefix))
				return false;

			var fullBytes = prefix / 8;
			var remainingBits = prefix % 8;
			for (var index = 0; index < fullBytes; index++)
			{
				if (networkBytes[index] != candidateBytes[index])
					return false;
			}

			if (remainingBits == 0)
				return true;

			var mask = (byte)(0xff << (8 - remainingBits));
			return (networkBytes[fullBytes] & mask) == (candidateBytes[fullBytes] & mask);
		}

		private static IPAddress NormalizeFamily(
			IPAddress address,
			System.Net.Sockets.AddressFamily targetFamily)
		{
			if (address.AddressFamily == targetFamily)
				return address;

			if (targetFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
			    address.IsIPv4MappedToIPv6)
				return address.MapToIPv4();

			if (targetFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 &&
			    address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
				return address.MapToIPv6();

			return null;
		}
	}
}
