using System;
using System.Linq;
using Resgrid.Config;

namespace Resgrid.Providers.NumberProvider
{
	/// <summary>
	/// Where a dedicated department number (Dedicated Number add-on) sends its traffic. URLs are built from
	/// <see cref="SystemBehaviorConfig.ResgridApiBaseUrl"/> when a number is bought, never from the NumberProviderConfig fields that were
	/// composed from the default base URL at type initialization. Methods match what Web.Services' TwilioController accepts.
	/// </summary>
	public static class DedicatedNumberCarrierSettings
	{
		public const string Provider = "Twilio";

		/// <summary>TwilioController.IncomingMessage accepts GET and POST and reads Twilio's form body on POST.</summary>
		public const string SmsMethod = "POST";

		/// <summary>TwilioController.InboundVoice is [HttpGet] only and binds its request from the query string.</summary>
		public const string VoiceMethod = "GET";

		/// <summary>
		/// Twilio webhook connection overrides on the SMS URL: retry a 5xx answer, a failed connection and a read timeout (Twilio's default
		/// retries connection failures only). IncomingMessage answers 503 when intake failed before anything was saved, or while the first
		/// delivery of the same text is still being processed; the retry carries the same MessageSid, so the intake claim deduplicates it.
		/// Twilio never sends the fragment and leaves it out of X-Twilio-Signature, so [ValidateRequest] (which rebuilds the URL from the
		/// request) still matches.
		/// </summary>
		public const string SmsConnectionOverrides = "rp=5xx,ct,rt";

		public static Uri SmsUrl() => Build("/api/Twilio/IncomingMessage", SmsConnectionOverrides);

		public static Uri VoiceUrl() => Build("/api/Twilio/InboundVoice");

		/// <summary>
		/// The Twilio regulatory bundle SID configured for the country in <see cref="DedicatedNumberAddonConfig.RegulatoryBundles"/>
		/// ("GB=BU…,DE=BU…"); null when the country needs none or none is configured.
		/// </summary>
		public static string BundleSidFor(string countryCode)
		{
			if (string.IsNullOrWhiteSpace(countryCode) || string.IsNullOrWhiteSpace(DedicatedNumberAddonConfig.RegulatoryBundles))
				return null;

			var country = countryCode.Trim();
			var entry = DedicatedNumberAddonConfig.RegulatoryBundles.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
				.Select(e => e.Split('='))
				.FirstOrDefault(parts => parts.Length == 2 && string.Equals(parts[0].Trim(), country, StringComparison.OrdinalIgnoreCase));

			var sid = entry?[1].Trim();
			return string.IsNullOrWhiteSpace(sid) ? null : sid;
		}

		private static Uri Build(string path, string connectionOverrides = null)
		{
			var root = (SystemBehaviorConfig.ResgridApiBaseUrl ?? "").Trim().TrimEnd('/');
			var fragment = string.IsNullOrWhiteSpace(connectionOverrides) ? "" : "#" + connectionOverrides;
			// Twilio only calls public https endpoints; a missing or relative base URL must stop the purchase rather than buy a deaf number.
			return Uri.TryCreate(root + path + fragment, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps ? uri : null;
		}
	}
}
