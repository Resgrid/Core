using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Resources;

namespace Resgrid.Localization.Areas.User.Moderation
{
	/// <summary>Marker type used by ASP.NET Core localization for moderation resources.</summary>
	public class Moderation
	{
	}

	/// <summary>
	/// Culture-explicit access to moderation resources for background/service messages where the
	/// recipient's preferred culture can differ from the current request culture.
	/// </summary>
	public static class ModerationResources
	{
		private static readonly ResourceManager ResourceManager = new ResourceManager(
			typeof(Moderation).FullName!, typeof(Moderation).Assembly);

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

		public static string GetCurrent(string key, params object[] arguments)
		{
			return Get(key, CultureInfo.CurrentUICulture.Name, arguments);
		}

		public static IReadOnlyDictionary<string, string> GetAll(string culture)
		{
			var resourceSet = ResourceManager.GetResourceSet(GetSupportedCulture(culture), true, false);
			if (resourceSet == null)
				return new Dictionary<string, string>();

			return resourceSet.Cast<DictionaryEntry>()
				.Where(x => x.Key is string && x.Value is string)
				.ToDictionary(x => (string)x.Key, x => (string)x.Value!);
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
