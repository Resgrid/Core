using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Resources;

namespace Resgrid.Localization.Areas.User.CommunicationTest
{
	/// <summary>Marker type used by ASP.NET Core localization for communication test resources.</summary>
	public class CommunicationTest
	{
	}

	/// <summary>
	/// Culture-explicit access to communication test resources. The screens use
	/// <c>IStringLocalizer</c> and render in the request culture, but the messages a test sends are
	/// composed in a background worker for someone else entirely — they must render in the
	/// *recipient's* language (<c>UserProfile.Language</c>), which is why those lookups pass a
	/// culture instead of relying on the ambient thread culture.
	/// A missing culture for a key falls back to English; a missing key surfaces the key name.
	/// </summary>
	public static class CommunicationTestResources
	{
		private static readonly ResourceManager ResourceManager = new ResourceManager(
			typeof(CommunicationTest).FullName!, typeof(CommunicationTest).Assembly);

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
