using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using ProxyNetwork = Microsoft.AspNetCore.HttpOverrides.IPNetwork;

namespace Resgrid.Web.Helpers
{
	/// <summary>
	/// Decides which proxies may set the caller's address through X-Forwarded-For. Shared by every ASP.NET host so the
	/// trust boundary cannot drift between them: whatever ForwardedHeadersMiddleware accepts becomes
	/// HttpContext.Connection.RemoteIpAddress, which audit logs, session tracking and per-IP limits all record.
	/// </summary>
	public static class ForwardedHeadersSetup
	{
		private const int Ipv4MappedPrefixBits = 96;

		/// <summary>
		/// Trusts X-Forwarded-For and X-Forwarded-Proto only from the configured proxy network. An IPv4 network is also
		/// added in IPv4-mapped IPv6 form, because Kestrel on a dual-stack socket reports IPv4 peers as ::ffff:a.b.c.d.
		/// The mapped form's prefix length is offset by the 96 bits of ::ffff:0:0/96: keeping the IPv4 length there would
		/// describe ::/n, which contains every IPv4-mapped address and so trusts any IPv4 client as a proxy.
		/// A network or prefix that does not parse trusts nothing beyond the framework's loopback defaults.
		/// </summary>
		public static void Configure(ForwardedHeadersOptions options, string proxyNetwork, int proxyNetworkCidr)
		{
			options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

			if (string.IsNullOrWhiteSpace(proxyNetwork) || !IPAddress.TryParse(proxyNetwork, out var network))
				return;

			if (network.IsIPv4MappedToIPv6)
			{
				// Configured in mapped form (::ffff:a.b.c.d): accept the IPv4 prefix length or the full IPv6 one.
				network = network.MapToIPv4();
				if (proxyNetworkCidr > 32)
					proxyNetworkCidr -= Ipv4MappedPrefixBits;
			}

			if (network.AddressFamily == AddressFamily.InterNetwork)
			{
				if (proxyNetworkCidr < 0 || proxyNetworkCidr > 32)
					return;

				options.KnownNetworks.Add(new ProxyNetwork(network, proxyNetworkCidr));
				options.KnownNetworks.Add(new ProxyNetwork(network.MapToIPv6(), Ipv4MappedPrefixBits + proxyNetworkCidr));
			}
			else if (network.AddressFamily == AddressFamily.InterNetworkV6)
			{
				if (proxyNetworkCidr < 0 || proxyNetworkCidr > 128)
					return;

				options.KnownNetworks.Add(new ProxyNetwork(network, proxyNetworkCidr));
			}
		}
	}
}
