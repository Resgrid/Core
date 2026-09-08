using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Config;

namespace Resgrid.Services.Records.Connectors
{
	/// <summary>
	/// Where a connector is allowed to send a request (RMS plan section 4.1). A connector is configured by a
	/// department administrator and fetched by a worker, so its URL is an outbound request made from inside the
	/// deployment on a person's say-so. Left unchecked that is a way to read whatever the worker can reach —
	/// metadata endpoints, an internal admin port, another tenant's service — with the answer handed back in the
	/// run log. So a destination is checked twice: when it is saved, and again against the resolved addresses
	/// immediately before each connection, because a name that answered publicly once can answer 127.0.0.1 next.
	/// </summary>
	public static class ExternalFeedDestination
	{
		/// <summary>Refuses the URL as a connector feed root, saying why. Does not resolve names.</summary>
		public static void RequireAllowedUrl(Uri uri)
		{
			if (uri == null) throw new ArgumentNullException(nameof(uri));
			if (!IsHostAllowed(uri.Host))
				throw new ArgumentException($"'{uri.Host}' is not an approved feed host for this installation.");
			// A literal address never gets a DNS check, so it is settled here.
			if (IPAddress.TryParse(uri.Host.Trim('[', ']'), out var literal) && !IsAddressAllowed(literal))
				throw new ArgumentException($"'{uri.Host}' is an internal address; a connector reads external ordering systems only.");
		}

		/// <summary>
		/// The same check against what the name resolves to now. Every returned address has to pass: a name that
		/// answers with one public and one internal address is refused rather than raced.
		/// </summary>
		public static async Task RequireAllowedDestinationAsync(Uri uri, CancellationToken cancellationToken = default)
		{
			RequireAllowedUrl(uri);
			var host = uri.Host.Trim('[', ']');
			if (IPAddress.TryParse(host, out _))
				return;
			if (RecordsConnectorConfig.AllowPrivateNetworkFeeds)
				return;

			IPAddress[] addresses;
			try
			{
				addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
			}
			catch (Exception ex) when (ex is SocketException || ex is ArgumentException)
			{
				throw new InvalidOperationException($"The feed host '{uri.Host}' could not be resolved.");
			}

			if (addresses == null || addresses.Length == 0)
				throw new InvalidOperationException($"The feed host '{uri.Host}' could not be resolved.");
			if (addresses.Any(address => !IsAddressAllowed(address)))
				throw new InvalidOperationException($"The feed host '{uri.Host}' resolves to an internal address; a connector reads external ordering systems only.");
		}

		/// <summary>Empty allowlist means any host; a leading dot matches the domain and everything under it.</summary>
		public static bool IsHostAllowed(string host)
		{
			var allowed = Hosts();
			if (allowed.Count == 0)
				return true;
			if (string.IsNullOrWhiteSpace(host))
				return false;
			host = host.Trim().TrimEnd('.');
			return allowed.Any(entry => entry.StartsWith(".", StringComparison.Ordinal)
				? host.EndsWith(entry, StringComparison.OrdinalIgnoreCase) || string.Equals(host, entry.Substring(1), StringComparison.OrdinalIgnoreCase)
				: string.Equals(host, entry, StringComparison.OrdinalIgnoreCase));
		}

		/// <summary>False for anything that is not a routable public address, unless the local-development switch is on.</summary>
		public static bool IsAddressAllowed(IPAddress address)
		{
			if (address == null)
				return false;
			if (RecordsConnectorConfig.AllowPrivateNetworkFeeds)
				return true;
			if (address.IsIPv4MappedToIPv6)
				address = address.MapToIPv4();
			if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
				return false;

			if (address.AddressFamily == AddressFamily.InterNetwork)
			{
				var b = address.GetAddressBytes();
				if (b[0] == 0) return false;                                     // 0.0.0.0/8, "this network"
				if (b[0] == 10) return false;                                    // 10.0.0.0/8
				if (b[0] == 127) return false;                                   // loopback
				if (b[0] == 169 && b[1] == 254) return false;                    // link-local, including the cloud metadata address
				if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return false;        // 172.16.0.0/12
				if (b[0] == 192 && b[1] == 168) return false;                    // 192.168.0.0/16
				if (b[0] == 192 && b[1] == 0 && b[2] == 0) return false;          // IETF protocol assignments
				if (b[0] == 100 && b[1] >= 64 && b[1] <= 127) return false;       // carrier-grade NAT
				if (b[0] >= 224) return false;                                   // multicast and reserved
				return true;
			}

			if (address.AddressFamily == AddressFamily.InterNetworkV6)
			{
				if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast || address.IsIPv6UniqueLocal)
					return false;
				return true;
			}

			return false;
		}

		private static List<string> Hosts()
		{
			var configured = RecordsConnectorConfig.AllowedFeedHosts;
			if (string.IsNullOrWhiteSpace(configured))
				return new List<string>();
			return configured.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
				.Select(entry => entry.Trim().TrimEnd('.'))
				.Where(entry => entry.Length > 0)
				.ToList();
		}
	}
}
