using System.Security.Cryptography;
using System.Text;

namespace Resgrid.Web.Tts.Health
{
	/// <summary>
	/// Gates the deep /health/full endpoint. Its probes run a real Piper/ffmpeg
	/// synthesis plus Redis and S3 round-trips, and this service is internet-reachable
	/// (Twilio fetches playback audio from it), so callers must present the same
	/// shared monitoring key the API's /health/full uses
	/// (SystemBehaviorConfig.FullHealthCheckKey, X-Resgrid-Health-Key header).
	/// An unconfigured key fails closed and disables the endpoint. The shallow
	/// /health probe endpoint stays public.
	/// </summary>
	public static class TtsHealthCheckAccess
	{
		public const string HeaderName = "X-Resgrid-Health-Key";

		public static bool IsAuthorized(HttpRequest request)
		{
			var configuredKey = Resgrid.Config.SystemBehaviorConfig.FullHealthCheckKey;

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
