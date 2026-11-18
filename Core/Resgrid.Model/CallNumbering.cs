using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using ProtoBuf;

namespace Resgrid.Model
{
	/// <summary>
	/// Department setting 115 (CallNumberingConfig): how the department's call numbers are written. A department with no
	/// saved value keeps the numbers Resgrid has always issued, "26-153" (<see cref="CallNumberFormat.LegacyPattern"/>).
	/// What each sequence issues next, and any raised starting point, live in CallNumberSequences, not here.
	/// </summary>
	[ProtoContract]
	public class CallNumberingConfig
	{
		/// <summary>The department's call number pattern (see <see cref="CallNumberFormat"/>), e.g. "FD{YYYY}-{SEQ}". Null keeps the legacy pattern.</summary>
		[ProtoMember(1)]
		public string Pattern { get; set; }

		/// <summary>Zero-padded width of the sequence part; 0 or 1 writes it unpadded, as the legacy numbers are.</summary>
		[ProtoMember(2)]
		public int SequenceWidth { get; set; }

		/// <summary>
		/// Month (1-12) the numbering year starts, for a department numbering calls by a fiscal year; 0, unset, is January.
		/// Only a pattern that restarts yearly uses it: {MM} and {DD} always write the calendar date.
		/// </summary>
		[ProtoMember(3)]
		public int YearStartMonth { get; set; }

		/// <summary>Day of <see cref="YearStartMonth"/> the numbering year starts; 0, unset, is the 1st.</summary>
		[ProtoMember(4)]
		public int YearStartDay { get; set; }

		/// <summary>
		/// <see cref="NumberingYearLabel"/> value: which year {YYYY} and {YY} write for a numbering year that does not start on
		/// January 1. 0 is the year it ends in.
		/// </summary>
		[ProtoMember(5)]
		public int YearLabel { get; set; }

		public NumberingYearStart YearStart()
		{
			return new NumberingYearStart(YearStartMonth, YearStartDay, (NumberingYearLabel)YearLabel);
		}
	}

	/// <summary>When a call number pattern starts its sequence again: whenever the date text it writes changes.</summary>
	public enum CallNumberResetPeriod
	{
		Never = 0,
		Yearly = 1,
		Monthly = 2,
		Daily = 3
	}

	/// <summary>
	/// One call-number sequence: the text a pattern writes before and after {SEQ} for one period. Calls share a sequence
	/// exactly when they share a scope, so the date parts a pattern includes are also what its sequence restarts on. The time
	/// of day ({HH}, {MIN}) is written into each number but is not part of the scope: those patterns restart daily.
	/// </summary>
	public sealed class CallNumberScope
	{
		public CallNumberScope(string prefix, string suffix, int width, CallNumberResetPeriod period, DateTime? periodStart, DateTime? periodEnd, int? year = null,
			string keyPrefix = null, string keySuffix = null)
		{
			Prefix = prefix ?? string.Empty;
			Suffix = suffix ?? string.Empty;
			Width = width;
			Period = period;
			PeriodStart = periodStart;
			PeriodEnd = periodEnd;
			Year = year;
			KeyPrefix = keyPrefix ?? Prefix;
			KeySuffix = keySuffix ?? Suffix;
		}

		/// <summary>The text written before the sequence for the moment the scope was resolved, time of day included.</summary>
		public string Prefix { get; }

		public string Suffix { get; }

		/// <summary><see cref="Prefix"/> with any time-of-day token left as the token ("{HH}", "{MIN}"), so it is the same all period.</summary>
		public string KeyPrefix { get; }

		public string KeySuffix { get; }

		/// <summary>
		/// <see cref="KeyPrefix"/> with each time-of-day digit as '?', the repository's one-character wildcard: the seed scan
		/// matches every number the scope issued, whatever time it wrote. The same length as <see cref="Prefix"/>.
		/// </summary>
		public string SearchPrefix => AsSearch(KeyPrefix);

		public string SearchSuffix => AsSearch(KeySuffix);

		public int Width { get; }

		public CallNumberResetPeriod Period { get; }

		/// <summary>Department-local start of the period the scope covers; null when the pattern never restarts.</summary>
		public DateTime? PeriodStart { get; }

		/// <summary>Department-local, exclusive end of the period; null when the pattern never restarts.</summary>
		public DateTime? PeriodEnd { get; }

		/// <summary>The year {YYYY} and {YY} write for the scope (a fiscal year's name when the numbering year starts later than January 1); null when the pattern has no year.</summary>
		public int? Year { get; }

		/// <summary>
		/// Stable identity of the sequence; the counter row is keyed by it. '#' never appears in a pattern literal, and neither
		/// do the braces a time-of-day token keeps here, so a key is the same text it always was for a pattern without one.
		/// </summary>
		public string Key => KeyPrefix + "#" + KeySuffix;

		public string Format(int sequence) => Prefix + sequence.ToString("D" + Width, CultureInfo.InvariantCulture) + Suffix;

		private static string AsSearch(string keyText) => keyText.Replace(CallNumberFormat.HourToken, "??").Replace(CallNumberFormat.MinuteToken, "??");
	}

	/// <summary>
	/// Department call-number patterns (setting 115, <see cref="CallNumberingConfig.Pattern"/>). A pattern is literal text
	/// (letters, digits, '-', '_', '.', '/' and ':') around tokens: {YYYY} or {YY} the year, {MM} the month, {DD} the day of
	/// the month, {HH} the hour (00-23), {MIN} the minute, and {SEQ} the sequence, which must appear exactly once. Dates and
	/// times are the department's local time of the call. {MM} needs a year token, {DD} needs {MM}, {HH} needs {DD} and
	/// {MIN} needs {HH}, so a sequence never runs on across the same month of different years. The time of day is written
	/// into the number but never restarts the sequence (<see cref="CallNumberScope"/>): a pattern with it restarts daily.
	/// A department numbering by a fiscal year sets the day its numbering year starts (<see cref="CallNumberingConfig.YearStart"/>).
	/// Document numbers share this grammar without the time of day (<see cref="IsValidWithoutTime"/>).
	/// </summary>
	public static class CallNumberFormat
	{
		public const string YearToken = "{YYYY}";
		public const string ShortYearToken = "{YY}";
		public const string MonthToken = "{MM}";
		public const string DayToken = "{DD}";
		/// <summary>The hour, 00 to 23.</summary>
		public const string HourToken = "{HH}";
		/// <summary>The minute; not {MM}, which is the month.</summary>
		public const string MinuteToken = "{MIN}";
		public const string SequenceToken = "{SEQ}";

		/// <summary>What every department issued before call numbering was configurable: "26-153".</summary>
		public const string LegacyPattern = ShortYearToken + "-" + SequenceToken;

		/// <summary>Date tokens only ever render shorter than written, so a number is at most this plus the sequence digits.</summary>
		public const int MaxPatternLength = 40;
		public const int MinWidth = 1;
		public const int MaxWidth = 8;
		/// <summary>Unpadded, as the legacy numbers are.</summary>
		public const int DefaultWidth = 1;
		public const int MaxSequence = 99999999;

		private static readonly string[] Tokens = { YearToken, ShortYearToken, MonthToken, DayToken, HourToken, MinuteToken, SequenceToken };

		/// <summary>The saved pattern, or the legacy one when none is saved (or a saved one no longer validates).</summary>
		public static string EffectivePattern(CallNumberingConfig config)
		{
			return IsValid(config?.Pattern) ? Normalize(config.Pattern) : LegacyPattern;
		}

		public static int EffectiveWidth(int configuredWidth)
		{
			return Math.Max(MinWidth, Math.Min(MaxWidth, configuredWidth <= 0 ? DefaultWidth : configuredWidth));
		}

		public static bool IsValid(string pattern) => Parse(pattern) != null;

		/// <summary>
		/// True when <paramref name="pattern"/> is valid and writes no time of day: no {HH} or {MIN} and no ':'. Document numbers
		/// (<see cref="DocumentNumbering"/>) take only these; the time of day is for call numbers.
		/// </summary>
		public static bool IsValidWithoutTime(string pattern)
		{
			var parts = Parse(pattern);
			return parts != null && parts.All(p => p.Token != HourToken && p.Token != MinuteToken && (p.Literal == null || p.Literal.IndexOf(':') < 0));
		}

		/// <summary>Tokens are matched case-insensitively; this writes them back in their canonical upper case. Null when not valid.</summary>
		public static string Normalize(string pattern)
		{
			var parts = Parse(pattern?.Trim());
			return parts == null ? null : string.Concat(parts.Select(p => p.Token ?? p.Literal));
		}

		public static bool UsesToken(string pattern, string token)
		{
			return Parse(pattern)?.Any(p => p.Token == token) == true;
		}

		/// <summary>How often a valid pattern restarts its sequence; the finest date token it writes decides ({HH} and {MIN} need {DD}, so daily).</summary>
		public static CallNumberResetPeriod ResetPeriod(string pattern)
		{
			var parts = Parse(pattern);
			if (parts == null)
				return CallNumberResetPeriod.Never;
			if (parts.Any(p => p.Token == DayToken))
				return CallNumberResetPeriod.Daily;
			if (parts.Any(p => p.Token == MonthToken))
				return CallNumberResetPeriod.Monthly;
			if (parts.Any(p => p.Token == YearToken || p.Token == ShortYearToken))
				return CallNumberResetPeriod.Yearly;
			return CallNumberResetPeriod.Never;
		}

		/// <summary>
		/// The sequence scope a call logged at <paramref name="localDate"/> (department time) falls in. A pattern that restarts
		/// yearly runs from <paramref name="yearStart"/> (January 1 when null) and writes that numbering year's name; one with
		/// {MM} or {DD} writes and restarts on the calendar date, so a number such as 20270115-3 always reads as the day it was logged.
		/// {HH} and {MIN} write the local time of <paramref name="localDate"/> into <see cref="CallNumberScope.Prefix"/> and
		/// <see cref="CallNumberScope.Suffix"/> only; the scope's key and search text keep them as tokens.
		/// </summary>
		public static CallNumberScope Resolve(string pattern, int width, DateTime localDate, NumberingYearStart yearStart = null)
		{
			var parts = Parse(pattern) ?? throw new ArgumentException("The call number pattern is not valid.", nameof(pattern));
			var period = ResetPeriod(pattern);
			yearStart = period == CallNumberResetPeriod.Yearly ? yearStart ?? NumberingYearStart.Calendar : NumberingYearStart.Calendar;
			var year = yearStart.YearOf(localDate);

			var prefix = new StringBuilder();
			var suffix = new StringBuilder();
			var keyPrefix = new StringBuilder();
			var keySuffix = new StringBuilder();
			var target = prefix;
			var keyTarget = keyPrefix;
			foreach (var part in parts)
			{
				if (part.Token == SequenceToken)
				{
					target = suffix;
					keyTarget = keySuffix;
					continue;
				}

				var text = part.Token == null ? part.Literal : Render(part.Token, localDate, year);
				target.Append(text);
				keyTarget.Append(part.Token == HourToken || part.Token == MinuteToken ? part.Token : text);
			}

			DateTime? start = null, end = null;
			switch (period)
			{
				case CallNumberResetPeriod.Yearly:
					start = yearStart.StartOf(localDate);
					end = start.Value.AddYears(1);
					break;
				case CallNumberResetPeriod.Monthly:
					start = new DateTime(localDate.Year, localDate.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);
					end = start.Value.AddMonths(1);
					break;
				case CallNumberResetPeriod.Daily:
					start = new DateTime(localDate.Year, localDate.Month, localDate.Day, 0, 0, 0, DateTimeKind.Unspecified);
					end = start.Value.AddDays(1);
					break;
			}

			return new CallNumberScope(prefix.ToString(), suffix.ToString(), EffectiveWidth(width), period, start, end, period == CallNumberResetPeriod.Never ? (int?)null : year,
				keyPrefix.ToString(), keySuffix.ToString());
		}

		private static string Render(string token, DateTime localDate, int year)
		{
			switch (token)
			{
				case YearToken: return year.ToString("D4", CultureInfo.InvariantCulture);
				case ShortYearToken: return (year % 100).ToString("D2", CultureInfo.InvariantCulture);
				case MonthToken: return localDate.Month.ToString("D2", CultureInfo.InvariantCulture);
				case DayToken: return localDate.Day.ToString("D2", CultureInfo.InvariantCulture);
				// Always two digits: the seed scan's wildcard (CallNumberScope.SearchPrefix) counts on it.
				case HourToken: return localDate.Hour.ToString("D2", CultureInfo.InvariantCulture);
				case MinuteToken: return localDate.Minute.ToString("D2", CultureInfo.InvariantCulture);
				default: return string.Empty;
			}
		}

		/// <summary>
		/// Keys that sort call numbers in the order they were issued, which their text does not ("26-10" reads before "26-5"):
		/// by the year, month and day a number names, then by its sequence (a time of day it names is read past, not compared:
		/// its daily sequence is already the order the day's calls were issued in). A number is read with the department's
		/// <paramref name="pattern"/>, else with the legacy one; text neither wrote (a pattern the department has since replaced)
		/// sorts ahead of them, by its characters with each run of digits compared as a number. Keys compare ordinally.
		/// </summary>
		public static Func<string, string> SortKeys(string pattern)
		{
			var readers = new[] { pattern, LegacyPattern }.Distinct().Select(NumberReader).Where(r => r != null).ToList();
			return number => SortKey(readers, number);
		}

		private static Regex NumberReader(string pattern)
		{
			var parts = Parse(pattern);
			if (parts == null)
				return null;

			var reader = new StringBuilder("^");
			foreach (var part in parts)
			{
				switch (part.Token)
				{
					case null: reader.Append(Regex.Escape(part.Literal)); break;
					case YearToken: reader.Append("(?<year>[0-9]{4})"); break;
					case ShortYearToken: reader.Append("(?<yy>[0-9]{2})"); break;
					case MonthToken: reader.Append("(?<month>[0-9]{2})"); break;
					case DayToken: reader.Append("(?<day>[0-9]{2})"); break;
					case HourToken: reader.Append("(?<hour>[0-9]{2})"); break;
					case MinuteToken: reader.Append("(?<minute>[0-9]{2})"); break;
					case SequenceToken: reader.Append("(?<seq>[0-9]+)"); break;
				}
			}

			return new Regex(reader.Append('$').ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
		}

		private static string SortKey(List<Regex> readers, string number)
		{
			if (string.IsNullOrWhiteSpace(number))
				return string.Empty;

			number = number.Trim();
			foreach (var reader in readers)
			{
				var match = reader.Match(number);
				if (!match.Success || !int.TryParse(match.Groups["seq"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var sequence))
					continue;

				var year = match.Groups["year"].Success ? Group(match, "year")
					: match.Groups["yy"].Success ? CultureInfo.InvariantCulture.Calendar.ToFourDigitYear(Group(match, "yy")) : 0;

				return "1" + year.ToString("D4", CultureInfo.InvariantCulture) + Group(match, "month").ToString("D2", CultureInfo.InvariantCulture) +
					Group(match, "day").ToString("D2", CultureInfo.InvariantCulture) + sequence.ToString("D10", CultureInfo.InvariantCulture);
			}

			var key = new StringBuilder("0");
			var i = 0;
			while (i < number.Length)
			{
				if (number[i] < '0' || number[i] > '9')
				{
					key.Append(char.ToLowerInvariant(number[i++]));
					continue;
				}

				var start = i;
				while (i < number.Length && number[i] >= '0' && number[i] <= '9')
					i++;
				key.Append(number.Substring(start, i - start).TrimStart('0').PadLeft(10, '0'));
			}

			return key.ToString();
		}

		private static int Group(Match match, string name)
		{
			return match.Groups[name].Success ? int.Parse(match.Groups[name].Value, NumberStyles.None, CultureInfo.InvariantCulture) : 0;
		}

		// Never '#' (the scope key's separator), braces (tokens, and the time-of-day tokens a scope key keeps) or '?' (the seed
		// scan's wildcard). ':' is for a time such as {HH}:{MIN}; document numbers refuse it (IsValidWithoutTime).
		private static bool IsLiteral(char c) => (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-' || c == '_' || c == '.' || c == '/' || c == ':';

		/// <summary>Null when the pattern is not valid; otherwise literal runs and tokens in order.</summary>
		private static List<PatternPart> Parse(string pattern)
		{
			if (string.IsNullOrWhiteSpace(pattern) || pattern.Length > MaxPatternLength)
				return null;

			var parts = new List<PatternPart>();
			var literal = new StringBuilder();
			var i = 0;
			while (i < pattern.Length)
			{
				if (pattern[i] == '{')
				{
					// {YYYY} is tried before {YY}, so the longer token always wins.
					var token = Tokens.FirstOrDefault(t => string.Compare(pattern, i, t, 0, t.Length, StringComparison.OrdinalIgnoreCase) == 0);
					if (token == null || parts.Any(p => p.Token == token))
						return null;
					if (literal.Length > 0)
					{
						parts.Add(new PatternPart(null, literal.ToString()));
						literal.Clear();
					}
					parts.Add(new PatternPart(token, null));
					i += token.Length;
					continue;
				}

				if (!IsLiteral(pattern[i]))
					return null;
				literal.Append(pattern[i]);
				i++;
			}
			if (literal.Length > 0)
				parts.Add(new PatternPart(null, literal.ToString()));

			if (parts.Count(p => p.Token == SequenceToken) != 1)
				return null;

			var hasYear = parts.Any(p => p.Token == YearToken || p.Token == ShortYearToken);
			var hasMonth = parts.Any(p => p.Token == MonthToken);
			if (hasMonth && !hasYear)
				return null;
			var hasDay = parts.Any(p => p.Token == DayToken);
			if (hasDay && !hasMonth)
				return null;
			// The time of day reads only with the date it is on, and the sequence restarting daily keeps such numbers unique.
			var hasHour = parts.Any(p => p.Token == HourToken);
			if (hasHour && !hasDay)
				return null;
			if (parts.Any(p => p.Token == MinuteToken) && !hasHour)
				return null;

			return parts;
		}

		private sealed class PatternPart
		{
			public PatternPart(string token, string literal)
			{
				Token = token;
				Literal = literal;
			}

			public string Token { get; }

			public string Literal { get; }
		}
	}

	/// <summary>The sequence a department's pattern produces for one moment, as the Call Settings screen shows it.</summary>
	public class CallNumberSequenceStatus
	{
		/// <summary><see cref="CallNumberScope.Key"/>; what a raised next number is saved against.</summary>
		public string ScopeKey { get; set; }

		public CallNumberResetPeriod Period { get; set; }

		/// <summary>The sequence the next call in this scope receives; a raised next number may not go below it.</summary>
		public int NextSequence { get; set; }

		/// <summary>The full number the next call receives, e.g. "26-154".</summary>
		public string NextNumber { get; set; }

		/// <summary>The department's raised starting point for this scope; 0 when none was set.</summary>
		public int FloorSequence { get; set; }
	}

	public class CallNumberingUpdate
	{
		public string Pattern { get; set; }

		public int SequenceWidth { get; set; }

		/// <summary>The scope the requested next number was shown for; a raise against any other scope is not applied.</summary>
		public string ScopeKey { get; set; }

		/// <summary>The sequence to raise the current scope's next number to; null leaves it as it is.</summary>
		public int? NextSequence { get; set; }

		/// <summary>Month the numbering year starts (1-12); null leaves the saved one as it is.</summary>
		public int? YearStartMonth { get; set; }

		/// <summary>Day of the month the numbering year starts; null leaves the saved one as it is.</summary>
		public int? YearStartDay { get; set; }

		/// <summary><see cref="NumberingYearLabel"/> value; null leaves the saved one as it is.</summary>
		public int? YearLabel { get; set; }
	}

	public class CallNumberingSaveResult
	{
		/// <summary>The pattern did not validate; nothing was saved.</summary>
		public bool PatternRejected { get; set; }

		/// <summary>The year start is not a month and day every year has (February 29 included); nothing was saved.</summary>
		public bool YearStartRejected { get; set; }

		/// <summary>Set when the requested next number was below what the sequence already issues next; the sequence was left as it was.</summary>
		public CallNumberSequenceStatus BelowCurrent { get; set; }

		/// <summary>The requested next number was for a sequence the saved pattern no longer produces (the pattern changed in the same save).</summary>
		public bool NextNotApplied { get; set; }
	}
}
