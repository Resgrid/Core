using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace Resgrid.Web.Services.Health
{
	/// <summary>
	/// Gates the deep /health/full endpoint. The probes it triggers (SQL, Redis, TTS
	/// microservice) and the dependency detail it returns are for trusted monitoring
	/// only, so callers must present the configured shared key. An unconfigured key
	/// fails closed and disables the endpoint. The shallow /health liveness endpoint
	/// stays public.
	/// </summary>
	public static class FullHealthCheckAccess
	{
		public const string HeaderName = "X-Resgrid-Health-Key";

		public static bool IsAuthorized(HttpRequest request)
		{
			var configuredKey = Config.SystemBehaviorConfig.FullHealthCheckKey;

			if (string.IsNullOrWhiteSpace(configuredKey))
				return false;

			if (request == null || !request.Headers.TryGetValue(HeaderName, out var providedValues))
				return false;

			var providedKey = providedValues.ToString();

			if (string.IsNullOrEmpty(providedKey))
				return false;

			return CryptographicOperations.FixedTimeEquals(
				Encoding.UTF8.GetBytes(providedKey),
				Encoding.UTF8.GetBytes(configuredKey));
		}
	}
}
