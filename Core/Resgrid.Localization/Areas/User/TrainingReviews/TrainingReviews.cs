using System.Globalization;
using System.Resources;

namespace Resgrid.Localization.Areas.User.TrainingReviews
{
	/// <summary>
	/// Training hours submissions and their review (round 9B): the reviewer inbox, a submission's page, the member's submissions, the review
	/// settings, the dashboard item, provenance labels and the member and reviewer notices (EventTypes 79-81).
	/// </summary>
	public class TrainingReviews
	{
	}

	/// <summary>
	/// Culture-explicit access to the TrainingReviews strings for text composed outside a page's own localizer (a transcript export built by the
	/// Records controller, a refusal shown by another page). A missing culture falls back to English; a missing key returns null.
	/// </summary>
	public static class TrainingReviewTexts
	{
		private static readonly ResourceManager ResourceManager = new ResourceManager(typeof(TrainingReviews).FullName!, typeof(TrainingReviews).Assembly);

		public static string? Get(string key, string? culture)
		{
			var cultureInfo = Culture(culture);
			return ResourceManager.GetString(key, cultureInfo) ?? ResourceManager.GetString(key, CultureInfo.GetCultureInfo("en"));
		}

		private static CultureInfo Culture(string? culture)
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
