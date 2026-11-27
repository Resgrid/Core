using System;
using System.Collections.Generic;
using System.Globalization;
using System.Resources;
using System.Text;

namespace Resgrid.Localization.Areas.User.Trainings
{
	public class Trainings
	{
	}

	/// <summary>
	/// Round 10: culture-explicit access to the Trainings strings for text composed outside a page's own localizer (the transcript and the
	/// certificate the v4 API prints in the member's language: the API host sets no request culture). A missing culture falls back to
	/// English; a missing key returns the key, like the page localizer.
	/// </summary>
	public static class TrainingTexts
	{
		private static readonly ResourceManager ResourceManager = new ResourceManager(typeof(Trainings).FullName!, typeof(Trainings).Assembly);

		public static string Get(string key, string culture)
		{
			if (string.IsNullOrEmpty(key))
				return key;
			var cultureInfo = Culture(culture);
			return Lookup(key, cultureInfo) ?? Lookup(key, CultureInfo.GetCultureInfo("en")) ?? key;
		}

		/// <summary>The supported culture a language tag maps to ("de-AT" → "de"); English for anything else.</summary>
		public static CultureInfo Culture(string culture)
		{
			var candidate = string.IsNullOrWhiteSpace(culture) ? "en" : culture.Trim();
			var separator = candidate.IndexOfAny(new[] { '-', '_' });
			if (separator > 0)
				candidate = candidate.Substring(0, separator);
			candidate = candidate.ToLowerInvariant();
			return SupportedLocales.SupportedLanguagesMap.ContainsKey(candidate) ? CultureInfo.GetCultureInfo(candidate) : CultureInfo.GetCultureInfo("en");
		}

		private static string? Lookup(string key, CultureInfo culture)
		{
			try
			{
				var value = ResourceManager.GetString(key, culture);
				return string.IsNullOrEmpty(value) ? null : value;
			}
			catch (MissingManifestResourceException)
			{
				// No neutral resources: a key missing from the culture's file falls through to English.
				return null;
			}
		}
	}
}
