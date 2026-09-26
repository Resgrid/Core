using System;
using System.Globalization;
using System.Linq;

namespace Resgrid.Web.Helpers
{
	/// <summary>
	/// The operating profile stores a season start or end as MM-DD. The profile screen edits it with a month picker and a day
	/// picker, so an administrator chooses a real date instead of typing one.
	/// </summary>
	public static class SeasonMonthDay
	{
		/// <summary>The month and day of a stored MM-DD value, or (0, 0) when it is blank or malformed.</summary>
		public static (int Month, int Day) Split(string value) =>
			value is { Length: 5 } && value[2] == '-' &&
			int.TryParse(value.AsSpan(0, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var month) &&
			int.TryParse(value.AsSpan(3, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var day) ? (month, day) : (0, 0);

		/// <summary>
		/// MM-DD for a posted month and day. Both blank means no season. Anything else that is not two numbers is kept as posted,
		/// so the profile's own validation rejects it rather than it silently becoming "no season".
		/// </summary>
		public static string Compose(string month, string day)
		{
			if (string.IsNullOrWhiteSpace(month) && string.IsNullOrWhiteSpace(day)) return null;
			return int.TryParse(month, NumberStyles.None, CultureInfo.InvariantCulture, out var m) && m is >= 1 and <= 99 &&
				int.TryParse(day, NumberStyles.None, CultureInfo.InvariantCulture, out var d) && d is >= 1 and <= 99
				? string.Create(CultureInfo.InvariantCulture, $"{m:00}-{d:00}")
				: (month ?? string.Empty).Trim() + "-" + (day ?? string.Empty).Trim();
		}

		/// <summary>Days a season boundary may use in a month. February allows the 29th, matching the profile's validation.</summary>
		public static int DaysIn(int month) => month switch { 2 => 29, 4 or 6 or 9 or 11 => 30, _ => 31 };

		/// <summary>
		/// The twelve Gregorian month names in the UI language. A language whose default calendar is not Gregorian uses its
		/// Gregorian option, so the picker never offers another calendar's months for an MM value.
		/// </summary>
		public static string[] MonthNames(CultureInfo culture)
		{
			culture ??= CultureInfo.InvariantCulture;
			var format = culture.DateTimeFormat;
			if (format.Calendar is not GregorianCalendar)
			{
				var gregorian = culture.OptionalCalendars.OfType<GregorianCalendar>().FirstOrDefault();
				if (gregorian == null) format = CultureInfo.InvariantCulture.DateTimeFormat;
				else
				{
					format = (DateTimeFormatInfo)format.Clone();
					format.Calendar = gregorian;
				}
			}
			var textInfo = culture.TextInfo;
			// Some languages write month names in lower case; a list entry starts with a capital.
			return format.MonthNames.Take(12).Select(name => string.IsNullOrEmpty(name) ? name : textInfo.ToUpper(name[0]) + name[1..]).ToArray();
		}
	}
}
