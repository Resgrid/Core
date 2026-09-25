using System.Net.Sockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Resgrid.Config;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Tts.Configuration
{
	public static class TtsRequestIdentity
	{
		public static void ConfigureForwardedHeaders(ForwardedHeadersOptions options)
		{
			ForwardedHeadersSetup.Configure(options, WebConfig.IngressProxyNetwork, WebConfig.IngressProxyNetworkCidr);
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
	}
}
