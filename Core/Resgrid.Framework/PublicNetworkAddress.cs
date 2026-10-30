using System.Net;
using System.Net.Sockets;

namespace Resgrid.Framework
{
	/// <summary>
	/// Whether an address is a routable public one, for requests the server makes to a URL someone else supplied (records
	/// connector feeds, KML NetworkLinks), so they can't reach metadata endpoints or internal services. Callers that may be
	/// switched to allow private networks (ExternalFeedDestination's AllowPrivateNetworkFeeds) keep that switch themselves.
	/// </summary>
	public static class PublicNetworkAddress
	{
		// 64:ff9b::/96, the well-known NAT64 prefix (RFC 6052): the last four bytes are the IPv4 address the gateway connects to.
		private static readonly byte[] WellKnownNat64Prefix = { 0x00, 0x64, 0xff, 0x9b, 0, 0, 0, 0, 0, 0, 0, 0 };

		// 64:ff9b:1::/48, local-use NAT64 (RFC 8215): where the IPv4 address sits depends on the network's prefix length.
		private static readonly byte[] LocalUseNat64Prefix = { 0x00, 0x64, 0xff, 0x9b, 0x00, 0x01 };

		/// <summary>
		/// False for anything that is not a routable public address: loopback, private, link-local (the cloud metadata
		/// address), carrier-grade NAT, benchmarking, multicast and reserved ranges, and an IPv6 address that only carries one
		/// of them (IPv4-mapped, or NAT64, which a NAT64 gateway would connect to).
		/// </summary>
		public static bool IsPublic(IPAddress address)
		{
			if (address == null)
				return false;
			if (address.IsIPv4MappedToIPv6)
				address = address.MapToIPv4();
			if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
				return false;

			if (address.AddressFamily == AddressFamily.InterNetwork)
				return IsPublicIPv4(address.GetAddressBytes());

			if (address.AddressFamily == AddressFamily.InterNetworkV6)
			{
				if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast || address.IsIPv6UniqueLocal)
					return false;

				var b = address.GetAddressBytes();
				if (StartsWith(b, WellKnownNat64Prefix))
					return IsPublicIPv4(new[] { b[12], b[13], b[14], b[15] });
				if (StartsWith(b, LocalUseNat64Prefix))
					return false;

				return true;
			}

			return false;
		}

		private static bool IsPublicIPv4(byte[] b)
		{
			if (b[0] == 0) return false;                                     // 0.0.0.0/8, "this network"
			if (b[0] == 10) return false;                                    // 10.0.0.0/8
			if (b[0] == 127) return false;                                   // loopback
			if (b[0] == 169 && b[1] == 254) return false;                    // link-local, including the cloud metadata address
			if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return false;        // 172.16.0.0/12
			if (b[0] == 192 && b[1] == 168) return false;                    // 192.168.0.0/16
			if (b[0] == 192 && b[1] == 0 && b[2] == 0) return false;          // IETF protocol assignments
			if (b[0] == 100 && b[1] >= 64 && b[1] <= 127) return false;       // carrier-grade NAT
			if (b[0] == 198 && (b[1] == 18 || b[1] == 19)) return false;      // 198.18.0.0/15, benchmarking (and proxies' fake-IP ranges)
			if (b[0] >= 224) return false;                                   // multicast and reserved
			return true;
		}

		private static bool StartsWith(byte[] bytes, byte[] prefix)
		{
			for (var i = 0; i < prefix.Length; i++)
			{
				if (bytes[i] != prefix[i])
					return false;
			}

			return true;
		}
	}
}
