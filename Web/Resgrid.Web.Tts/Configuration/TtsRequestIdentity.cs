using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Resgrid.Config;

namespace Resgrid.Web.Tts.Configuration
{
	public static class TtsRequestIdentity
	{
		public static void ConfigureForwardedHeaders(ForwardedHeadersOptions options)
		{
			options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
			AddKnownNetwork(options, WebConfig.IngressProxyNetwork, WebConfig.IngressProxyNetworkCidr);
		}

		public static string ResolveRateLimitPartitionKey(HttpContext httpContext)
		{
			ArgumentNullException.ThrowIfNull(httpContext);

			var remoteIpAddress = httpContext.Connection.RemoteIpAddress;
			if (remoteIpAddress is null)
			{
				return "unknown";
			}

			return remoteIpAddress.AddressFamily == AddressFamily.InterNetworkV6 && remoteIpAddress.IsIPv4MappedToIPv6
				? remoteIpAddress.MapToIPv4().ToString()
				: remoteIpAddress.ToString();
		}

		private static void AddKnownNetwork(ForwardedHeadersOptions options, string network, int prefixLength)
		{
			if (string.IsNullOrWhiteSpace(network) || !IPAddress.TryParse(network, out var parsedNetwork))
			{
				return;
			}

			options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(parsedNetwork, prefixLength));

			if (parsedNetwork.AddressFamily == AddressFamily.InterNetwork)
			{
				options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(parsedNetwork.MapToIPv6(), prefixLength));
			}
		}
	}
}
