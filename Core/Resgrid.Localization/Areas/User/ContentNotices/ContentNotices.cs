using System;
using System.Globalization;
using System.Linq;
using System.Resources;

namespace Resgrid.Localization.Areas.User.ContentNotices
{
	/// <summary>
	/// Outbound text of the Protocols + Training/LMS work (round 9C): the push, e-mail and SMS notices sent to members (protocol
	/// acknowledgments and reviews, training requirements and notices, skill and task book notices, signature and approval requests, dispatch
	/// case reviews, wellness and member health notices) and the learner portal's account e-mails (invitation, verification, reset).
	/// </summary>
	public class ContentNotices
	{
	}

	/// <summary>
	/// Culture-explicit access to <see cref="ContentNotices"/>. Every notice is composed for someone else (a worker run, another member's
	/// action), so the text is read in the recipient's language and never from the ambient thread culture (outbound-message-localization-gap).
	/// A key missing in a language falls back to English; a missing key surfaces the key name.
	/// </summary>
	public static class ContentNoticeText
	{
		private static readonly ResourceManager Strings = new ResourceManager(typeof(ContentNotices).FullName!, typeof(ContentNotices).Assembly);

		/// <summary>The neutral English culture every lookup falls back to.</summary>
		public static CultureInfo English => CultureInfo.GetCultureInfo("en");

		/// <summary>The supported culture for a language code ("de", "fr-CA", "pt-BR"), or null when it is blank, unknown or not supported.</summary>
		public static CultureInfo? SupportedCulture(string? language)
		{
			if (string.IsNullOrWhiteSpace(language))
				return null;
			try
			{
				var culture = CultureInfo.GetCultureInfo(language.Trim());
				return SupportedLocales.GetSupportedCultures().Contains(culture.TwoLetterISOLanguageName)
					? CultureInfo.GetCultureInfo(culture.TwoLetterISOLanguageName)
					: null;
			}
			catch (CultureNotFoundException)
			{
				return null;
			}
		}

		/// <summary>The first supported culture among <paramref name="languages"/> (recipient, then department default ...), else English.</summary>
		public static CultureInfo CultureFor(params string?[] languages)
		{
			foreach (var language in languages ?? Array.Empty<string>())
			{
				var culture = SupportedCulture(language);
				if (culture != null)
					return culture;
			}

			return English;
		}

		/// <summary>The text of <paramref name="key"/> in <paramref name="culture"/>'s language, else English, else the key.</summary>
		public static string Get(string key, CultureInfo? culture)
		{
			if (string.IsNullOrEmpty(key))
				return string.Empty;
			var language = SupportedCulture((culture ?? English).TwoLetterISOLanguageName) ?? English;
			try
			{
				return Strings.GetResourceSet(language, true, false)?.GetString(key)
					?? Strings.GetResourceSet(English, true, false)?.GetString(key)
					?? key;
			}
			catch (MissingManifestResourceException)
			{
				return key;
			}
		}

		/// <summary><see cref="Get"/> with the arguments filled in, formatted in <paramref name="culture"/> (dates, numbers).</summary>
		public static string Format(string key, CultureInfo? culture, params object?[] args)
		{
			var text = Get(key, culture);
			try
			{
				return args == null || args.Length == 0 ? text : string.Format(culture ?? English, text, args);
			}
			catch (FormatException)
			{
				return text;
			}
		}
	}
}
