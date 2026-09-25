using System;
using System.Globalization;
using System.Text;

namespace Resgrid.AdminAssist
{
	/// <summary>Explicit calendar download with generic text only; no mail, subscription or external calendar writes.</summary>
	public static class SetupReviewCalendar
	{
		public static string Create(int departmentId, DateTime reviewOnUtc, DateTime nowUtc, string title, string description)
		{
			if (departmentId <= 0 || reviewOnUtc.Kind != DateTimeKind.Utc || nowUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("UTC dates and department scope required.");
			string Escape(string value) => (value ?? string.Empty).Replace("\\", "\\\\").Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\\n").Replace(";", "\\;").Replace(",", "\\,");
			var date = reviewOnUtc.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
			var lines = new[] { "BEGIN:VCALENDAR", "VERSION:2.0", "PRODID:-//Resgrid//Setup Review//EN", "CALSCALE:GREGORIAN", "METHOD:PUBLISH", "BEGIN:VEVENT",
				$"UID:setup-review-{departmentId}-{date}@resgrid", "DTSTAMP:" + nowUtc.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture),
				"DTSTART:" + date, "DURATION:PT30M", "SUMMARY:" + Escape(title), "DESCRIPTION:" + Escape(description), "CLASS:PRIVATE", "TRANSP:TRANSPARENT", "END:VEVENT", "END:VCALENDAR" };
			var result = new StringBuilder();
			foreach (var line in lines)
			{
				int bytes = 0;
				foreach (var rune in line.EnumerateRunes())
				{
					if (bytes + rune.Utf8SequenceLength > 75) { result.Append("\r\n "); bytes = 1; }
					result.Append(rune.ToString()); bytes += rune.Utf8SequenceLength;
				}
				result.Append("\r\n");
			}
			return result.ToString();
		}
	}
}
