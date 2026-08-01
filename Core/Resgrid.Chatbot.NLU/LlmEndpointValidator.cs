using System;
using System.Net;
using System.Net.Sockets;

namespace Resgrid.Chatbot.NLU
{
	/// <summary>
	/// SSRF guard for LLM endpoints (system-level ChatbotConfig.CloudNluApiEndpoint and per-department
	/// overrides). Only absolute https URIs whose host is — and resolves to — public addresses are
	/// accepted; loopback, private, link-local and reserved ranges are rejected so a configured
	/// endpoint can never point the server at internal infrastructure.
	/// </summary>
	public static class LlmEndpointValidator
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

			if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
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

		private static bool IsBlockedAddress(IPAddress address)
		{
			if (address.AddressFamily == AddressFamily.InterNetwork)
			{
				var bytes = address.GetAddressBytes();

				if (bytes[0] == 0)
					return true; // 0.0.0.0/8 (incl. 0.0.0.0)

				if (bytes[0] == 10)
					return true; // 10.0.0.0/8

				if (bytes[0] == 127)
					return true; // 127.0.0.0/8 (loopback)

				if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
					return true; // 172.16.0.0/12

				if (bytes[0] == 192 && bytes[1] == 168)
					return true; // 192.168.0.0/16

				if (bytes[0] == 169 && bytes[1] == 254)
					return true; // 169.254.0.0/16 (link-local)

				return false;
			}

			if (address.AddressFamily == AddressFamily.InterNetworkV6)
			{
				if (IPAddress.IPv6Loopback.Equals(address))
					return true; // ::1

				var bytes = address.GetAddressBytes();

				if ((bytes[0] & 0xFE) == 0xFC)
					return true; // fc00::/7 (unique local)

				if (bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0x80)
					return true; // fe80::/10 (link-local)

				return false;
			}

			return true;
		}
	}
}
