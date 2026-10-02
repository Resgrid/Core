using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Resgrid.Web.Broker.Services;

namespace Resgrid.Web.Broker.Middleware
{
	/// <summary>
	/// Authenticates the calling host (passkey plan section 8.5), under network isolation and, later, mTLS. A host
	/// presents its credential id in X-Resgrid-Broker-Client and its key in X-Resgrid-Broker-Key; the matched
	/// credential travels with the request so the operation service can enforce its lanes. Without a client id, only
	/// the legacy shared key is recognized, and only while it is still accepted; every such use is logged with the
	/// calling host. Nothing configured refuses everything (503); a wrong or unknown credential is 401 with no detail.
	/// /health is exempt for the k8s probes.
	/// </summary>
	public class BrokerCredentialMiddleware
	{
		public const string ClientHeader = "X-Resgrid-Broker-Client";
		public const string KeyHeader = "X-Resgrid-Broker-Key";

		/// <summary>Informational only (never trusted): the calling process, so legacy-key logs can name the host.</summary>
		public const string HostHeader = "X-Resgrid-Broker-Host";

		/// <summary>HttpContext.Items key for the authenticated <see cref="BrokerCredential"/>.</summary>
		public const string CredentialItemKey = "Resgrid.BrokerCredential";

		private readonly RequestDelegate _next;

		public BrokerCredentialMiddleware(RequestDelegate next)
		{
			_next = next;
		}

		public async Task InvokeAsync(HttpContext context, BrokerCredentialRegistry registry)
		{
			if (context.Request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase))
			{
				await _next(context);
				return;
			}

			if (!registry.HasCredentials && !BrokerCredentialRegistry.LegacyKeyAccepted)
			{
				context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
				return;
			}

			var clientId = context.Request.Headers[ClientHeader].ToString().Trim();
			var key = context.Request.Headers[KeyHeader].ToString();

			BrokerCredential credential;
			if (clientId.Length > 0)
			{
				credential = registry.Authenticate(clientId, key);
				if (credential == null)
					Framework.Logging.LogError($"Protected Data Broker refused credential '{Sanitize(clientId)}' from {RemoteHost(context)}: unknown id or wrong key.");
			}
			else
			{
				credential = BrokerCredentialRegistry.IsLegacyKey(key) ? BrokerCredential.Legacy() : null;
				if (credential != null)
					Framework.Logging.LogInfo($"Protected Data Broker legacy shared key used by {RemoteHost(context)} " +
						$"(host '{Sanitize(context.Request.Headers[HostHeader].ToString())}', {context.Request.Method} {context.Request.Path}). " +
						"Issue this host its own credential; the shared key is retired at the end of the migration window.");
			}

			if (credential == null)
			{
				context.Response.StatusCode = StatusCodes.Status401Unauthorized;
				return;
			}

			context.Items[CredentialItemKey] = credential;
			await _next(context);
		}

		private static string RemoteHost(HttpContext context) => context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

		// Header values are caller-controlled: keep log lines to a short, printable token.
		private static string Sanitize(string value)
		{
			if (string.IsNullOrEmpty(value))
				return "-";
			var chars = value.Length > 64 ? value[..64].ToCharArray() : value.ToCharArray();
			for (var i = 0; i < chars.Length; i++)
				if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '-' && chars[i] != '.' && chars[i] != '_')
					chars[i] = '_';
			return new string(chars);
		}
	}
}
