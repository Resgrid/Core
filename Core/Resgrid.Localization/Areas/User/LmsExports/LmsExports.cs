using System.Globalization;
using System.Resources;

namespace Resgrid.Localization.Areas.User.LmsExports
{
	/// <summary>Trainings → Programs → Data export and CE reporting (Training T5 data exit, CAPCE and state CE files; round 6D).</summary>
	public class LmsExports
	{
	}

	/// <summary>
	/// Culture-explicit access to the LmsExports strings for text composed outside a page's own localizer (the SCORM package a worker
	/// writes in the course's locale, the Programs strip built by another controller). A missing culture falls back to English; a
	/// missing key surfaces the key name.
	/// </summary>
	public static class LmsExportTexts
	{
		private static readonly ResourceManager ResourceManager = new ResourceManager(typeof(LmsExports).FullName!, typeof(LmsExports).Assembly);

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
