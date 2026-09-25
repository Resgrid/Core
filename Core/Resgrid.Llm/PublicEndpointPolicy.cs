#nullable disable
using System;
using System.Net;
using System.Net.Sockets;

namespace Resgrid.Llm
{
	/// <summary>
	/// SSRF guard for LLM endpoints (system-level ChatbotConfig.CloudNluApiEndpoint and per-department
	/// overrides). Only absolute https URIs whose host is — and resolves to — public addresses are
	/// accepted; loopback, private, link-local and reserved ranges are rejected so a configured
	/// endpoint can never point the server at internal infrastructure.
	/// </summary>
	public static class PublicEndpointPolicy
	{
		public static bool IsValid(string endpoint, out string error)
		{
			error = null;

			if (string.IsNullOrWhiteSpace(endpoint))
			{
				error = "Endpoint is empty.";
				return false;
			}

			if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
			{
				error = "Endpoint must be an absolute URI.";
				return false;
			}

			if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0)
			{
				error = "Endpoint must use the https scheme.";
				return false;
			}

			var host = uri.Host;
			if (host.Length > 2 && host[0] == '[' && host[host.Length - 1] == ']')
				host = host.Substring(1, host.Length - 2);

			if (IPAddress.TryParse(host, out var literal))
				return IsPublicAddress(literal, out error);

			try
			{
				var addresses = Dns.GetHostAddresses(host);
				if (addresses == null || addresses.Length == 0)
				{
					error = "Endpoint host did not resolve to any address.";
					return false;
				}

				foreach (var address in addresses)
				{
					if (!IsPublicAddress(address, out error))
						return false;
				}

				return true;
			}
			catch (Exception)
			{
				error = "Endpoint host could not be resolved.";
				return false;
			}
		}

		private static bool IsPublicAddress(IPAddress address, out string error)
		{
			if (IsBlockedAddress(address))
			{
				error = $"Endpoint host resolves to a loopback/private/link-local/reserved address ({address}).";
				return false;
			}

			error = null;
			return true;
		}

		private static bool IsBlockedAddress(IPAddress address) => !OperatorEndpointPolicy.IsAllowedAddress(address, false, false);
	}
}
