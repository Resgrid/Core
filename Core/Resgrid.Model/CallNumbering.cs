using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
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
	/// exactly when they share a scope, so the date parts a pattern includes are also what its sequence restarts on.
	/// </summary>
	public sealed class CallNumberScope
	{
		public CallNumberScope(string prefix, string suffix, int width, CallNumberResetPeriod period, DateTime? periodStart, DateTime? periodEnd)
		{
			Prefix = prefix ?? string.Empty;
			Suffix = suffix ?? string.Empty;
			Width = width;
			Period = period;
			PeriodStart = periodStart;
			PeriodEnd = periodEnd;
		}

		public string Prefix { get; }

		public string Suffix { get; }

		public int Width { get; }

		public CallNumberResetPeriod Period { get; }

		/// <summary>Department-local start of the period the scope covers; null when the pattern never restarts.</summary>
		public DateTime? PeriodStart { get; }

		/// <summary>Department-local, exclusive end of the period; null when the pattern never restarts.</summary>
		public DateTime? PeriodEnd { get; }

		/// <summary>Stable identity of the sequence; the counter row is keyed by it. '#' never appears in a pattern literal.</summary>
		public string Key => Prefix + "#" + Suffix;

		public string Format(int sequence) => Prefix + sequence.ToString("D" + Width, CultureInfo.InvariantCulture) + Suffix;
	}

	/// <summary>
	/// Department call-number patterns (setting 115, <see cref="CallNumberingConfig.Pattern"/>). A pattern is literal text
	/// (letters, digits, '-', '_', '.' and '/') around tokens: {YYYY} or {YY} the year, {MM} the month, {DD} the day of the
	/// month, and {SEQ} the sequence, which must appear exactly once. Dates are the department's local date of the call.
	/// {MM} needs a year token and {DD} needs {MM}, so a sequence never runs on across the same month of different years.
	/// </summary>
	public static class CallNumberFormat
	{
		public const string YearToken = "{YYYY}";
		public const string ShortYearToken = "{YY}";
		public const string MonthToken = "{MM}";
		public const string DayToken = "{DD}";
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

		private static readonly string[] Tokens = { YearToken, ShortYearToken, MonthToken, DayToken, SequenceToken };

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

		/// <summary>How often a valid pattern restarts its sequence; the finest date token it writes decides.</summary>
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

		/// <summary>The sequence scope a call logged at <paramref name="localDate"/> (department time) falls in.</summary>
		public static CallNumberScope Resolve(string pattern, int width, DateTime localDate)
		{
			var parts = Parse(pattern) ?? throw new ArgumentException("The call number pattern is not valid.", nameof(pattern));
			var prefix = new StringBuilder();
			var suffix = new StringBuilder();
			var target = prefix;
			foreach (var part in parts)
			{
				if (part.Token == SequenceToken)
				{
					target = suffix;
					continue;
				}

				target.Append(part.Token == null ? part.Literal : Render(part.Token, localDate));
			}

			var period = ResetPeriod(pattern);
			DateTime? start = null, end = null;
			switch (period)
			{
				case CallNumberResetPeriod.Yearly:
					start = new DateTime(localDate.Year, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);
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

			return new CallNumberScope(prefix.ToString(), suffix.ToString(), EffectiveWidth(width), period, start, end);
		}

		private static string Render(string token, DateTime localDate)
		{
			switch (token)
			{
				case YearToken: return localDate.Year.ToString("D4", CultureInfo.InvariantCulture);
				case ShortYearToken: return (localDate.Year % 100).ToString("D2", CultureInfo.InvariantCulture);
				case MonthToken: return localDate.Month.ToString("D2", CultureInfo.InvariantCulture);
				case DayToken: return localDate.Day.ToString("D2", CultureInfo.InvariantCulture);
				default: return string.Empty;
			}
		}

		private static bool IsLiteral(char c) => (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-' || c == '_' || c == '.' || c == '/';

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
			if (parts.Any(p => p.Token == DayToken) && !hasMonth)
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
	}

	public class CallNumberingSaveResult
	{
		/// <summary>The pattern did not validate; nothing was saved.</summary>
		public bool PatternRejected { get; set; }

		/// <summary>Set when the requested next number was below what the sequence already issues next; the sequence was left as it was.</summary>
		public CallNumberSequenceStatus BelowCurrent { get; set; }

		/// <summary>The requested next number was for a sequence the saved pattern no longer produces (the pattern changed in the same save).</summary>
		public bool NextNotApplied { get; set; }
	}
}
