using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Resgrid.Providers.Workflow.Executors
{
	/// <summary>
	/// Guards against Server-Side Request Forgery (SSRF) by validating that a
	/// target host/URL does not resolve to a private, loopback, link-local, or
	/// cloud-metadata IP address before an outbound network connection is made.
	/// </summary>
	internal static class SsrfGuard
	{
		// Cloud instance metadata IPs that must never be reachable
		private static readonly string[] BlockedAddresses =
		{
			"169.254.169.254", // AWS/Azure/GCP metadata
			"100.100.100.200", // Alibaba Cloud metadata
		};

		/// <summary>
		/// Validates a URL string for SSRF risks.
		/// For HTTP/HTTPS executors also enforces that the scheme is https.
		/// </summary>
		/// <param name="url">Full absolute URL to validate.</param>
		/// <param name="requireHttps">When true, rejects http:// URLs.</param>
		/// <returns>(IsAllowed: true, Reason: null) on success; (false, reason) when blocked.</returns>
		public static async Task<(bool IsAllowed, string Reason)> ValidateUrlAsync(string url, bool requireHttps = true)
		{
			if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
				return (false, $"'{url}' is not a valid absolute URL.");

			if (requireHttps && uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase))
				return (false, "Plain HTTP URLs are not permitted for workflow HTTP actions. Use HTTPS.");

			return await ValidateHostAsync(uri.Host);
		}

		/// <summary>
		/// Validates a raw hostname (e.g. from an FTP/SFTP credential) for SSRF risks.
		/// </summary>
		public static async Task<(bool IsAllowed, string Reason)> ValidateHostAsync(string host)
		{
			if (string.IsNullOrWhiteSpace(host))
				return (false, "Host is empty.");

			// Block by hostname directly
			if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
			    host.Equals("ip6-localhost", StringComparison.OrdinalIgnoreCase) ||
			    host.Equals("ip6-loopback", StringComparison.OrdinalIgnoreCase))
				return (false, $"Connections to '{host}' are not permitted.");

			// Resolve hostname to IP addresses and check each
			IPAddress[] addresses;
			try
			{
				addresses = await Dns.GetHostAddressesAsync(host);
			}
			catch (SocketException)
			{
				// If DNS resolution fails, block the request — don't allow unresolvable hosts.
				return (false, $"Could not resolve hostname '{host}'. Only resolvable, non-private hosts are permitted.");
			}

			foreach (var addr in addresses)
			{
				var reason = GetBlockedReason(addr);
				if (reason != null)
					return (false, $"Host '{host}' resolves to a blocked address ({addr}): {reason}");
			}

			return (true, null);
		}

		private static string GetBlockedReason(IPAddress address)
		{
			// Normalize IPv4-mapped IPv6 addresses (::ffff:10.x.x.x) to IPv4
			if (address.IsIPv4MappedToIPv6)
				address = address.MapToIPv4();

			if (IPAddress.IsLoopback(address))
				return "loopback address";

			// Explicit block list
			if (BlockedAddresses.Contains(address.ToString()))
				return "cloud metadata endpoint";

			if (address.AddressFamily == AddressFamily.InterNetwork)
			{
				var bytes = address.GetAddressBytes();
				// 10.0.0.0/8
				if (bytes[0] == 10)
					return "RFC-1918 private range (10.x.x.x)";
				// 172.16.0.0/12
				if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
					return "RFC-1918 private range (172.16-31.x.x)";
				// 192.168.0.0/16
				if (bytes[0] == 192 && bytes[1] == 168)
					return "RFC-1918 private range (192.168.x.x)";
				// 169.254.0.0/16 (link-local)
				if (bytes[0] == 169 && bytes[1] == 254)
					return "link-local address";
				// 100.64.0.0/10 (CGNAT shared space — often used for cloud internal)
				if (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127)
					return "CGNAT shared address space";
				// 0.0.0.0
				if (bytes[0] == 0)
					return "unspecified address (0.x.x.x)";
			}
			else if (address.AddressFamily == AddressFamily.InterNetworkV6)
			{
				// ::1 loopback already covered by IsLoopback
				// fc00::/7 — unique local
				var bytes = address.GetAddressBytes();
				if ((bytes[0] & 0xFE) == 0xFC)
					return "IPv6 unique-local address (fc00::/7)";
				// fe80::/10 — link-local
				if (bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0x80)
					return "IPv6 link-local address (fe80::/10)";
			}

			return null;
		}
	}
}

