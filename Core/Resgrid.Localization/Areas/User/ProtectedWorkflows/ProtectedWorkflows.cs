using System.Globalization;
using System.Resources;

namespace Resgrid.Localization.Areas.User.ProtectedWorkflows
{
	/// <summary>Marker type used by ASP.NET Core localization for the Protected Workflows screens.</summary>
	public class ProtectedWorkflows
	{
	}

	/// <summary>
	/// Culture-explicit access to the Protected Workflows resources, for the notices a background worker composes for
	/// department administrators (expiry and final-failure). They render in the recipient's language
	/// (UserProfile.Language); a missing culture falls back to English and a missing key surfaces the key name.
	/// </summary>
	public static class ProtectedWorkflowsResources
	{
		private static readonly ResourceManager ResourceManager = new ResourceManager(
			typeof(ProtectedWorkflows).FullName!, typeof(ProtectedWorkflows).Assembly);

		public static string Get(string key, string? culture, params object[] arguments)
		{
			var cultureInfo = GetSupportedCulture(culture);
			var value = ResourceManager.GetString(key, cultureInfo)
				?? ResourceManager.GetString(key, CultureInfo.GetCultureInfo("en"))
				?? key;

			return arguments == null || arguments.Length == 0
				? value
				: string.Format(cultureInfo, value, arguments);
		}

		private static CultureInfo GetSupportedCulture(string? culture)
		{
			var candidate = string.IsNullOrWhiteSpace(culture) ? "en" : culture.Trim();
			var separator = candidate.IndexOfAny(new[] { '-', '_' });
			if (separator > 0)
				candidate = candidate.Substring(0, separator);

			candidate = candidate.ToLowerInvariant();
			return SupportedLocales.SupportedLanguagesMap.ContainsKey(candidate)
				? CultureInfo.GetCultureInfo(candidate)
				: CultureInfo.GetCultureInfo("en");
		}
	}
}
