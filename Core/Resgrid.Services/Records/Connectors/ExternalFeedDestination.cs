using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Config;
using Resgrid.Framework;

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

		/// <summary>
		/// False for anything that is not a routable public address (<see cref="PublicNetworkAddress"/>, shared with KML
		/// NetworkLinks), unless the local-development switch is on. The switch is the connectors' own: it never opens KML.
		/// </summary>
		public static bool IsAddressAllowed(IPAddress address)
		{
			if (address == null)
				return false;
			if (RecordsConnectorConfig.AllowPrivateNetworkFeeds)
				return true;

			return PublicNetworkAddress.IsPublic(address);
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
