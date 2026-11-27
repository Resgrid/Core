using System.Globalization;
using System.Resources;

namespace Resgrid.Localization.Areas.User.Signatures
{
	/// <summary>The shared signature panel and signatures list (S0), and the statements signatures carry.</summary>
	public class Signatures
	{
	}

	/// <summary>
	/// Culture-explicit access to the Signatures strings for text a service composes outside a request (statements stored with
	/// external signatures, the sealed audit page, the signing page's consent). A missing culture falls back to English; a missing
	/// key surfaces the key name.
	/// </summary>
	public static class SignatureTexts
	{
		private static readonly ResourceManager ResourceManager = new ResourceManager(typeof(Signatures).FullName!, typeof(Signatures).Assembly);

		public static string Get(string key, string culture, params object[] arguments)
		{
			var cultureInfo = Culture(culture);
			var value = ResourceManager.GetString(key, cultureInfo) ?? ResourceManager.GetString(key, CultureInfo.GetCultureInfo("en")) ?? key;
			return arguments == null || arguments.Length == 0 ? value : string.Format(cultureInfo, value, arguments);
		}

		private static CultureInfo Culture(string culture)
		{
			var candidate = string.IsNullOrWhiteSpace(culture) ? "en" : culture.Trim();
			var separator = candidate.IndexOfAny(new[] { '-', '_' });
			if (separator > 0)
				candidate = candidate.Substring(0, separator);
			candidate = candidate.ToLowerInvariant();
			return SupportedLocales.SupportedLanguagesMap.ContainsKey(candidate) ? CultureInfo.GetCultureInfo(candidate) : CultureInfo.GetCultureInfo("en");
		}
	}
}
