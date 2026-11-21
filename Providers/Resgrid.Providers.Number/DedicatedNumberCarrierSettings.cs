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

		public static Uri SmsUrl() => Build("/api/Twilio/IncomingMessage");

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

		private static Uri Build(string path)
		{
			var root = (SystemBehaviorConfig.ResgridApiBaseUrl ?? "").Trim().TrimEnd('/');
			// Twilio only calls public https endpoints; a missing or relative base URL must stop the purchase rather than buy a deaf number.
			return Uri.TryCreate(root + path, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps ? uri : null;
		}
	}
}
