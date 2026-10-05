using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Resgrid.Model
{
	/// <summary>
	/// One record-number sequence: the text a pattern renders before and after {SEQ}. Two records share a
	/// sequence exactly when they share a scope, so the year, type prefix and group a pattern includes are
	/// also what the sequence restarts on.
	/// </summary>
	public sealed class RecordNumberScope
	{
		public RecordNumberScope(string prefix, string suffix, int width)
		{
			Prefix = prefix ?? string.Empty;
			Suffix = suffix ?? string.Empty;
			Width = width;
		}

		public string Prefix { get; }

		public string Suffix { get; }

		public int Width { get; }

		/// <summary>Stable identity of the sequence; next-number floors are keyed by it. '#' never appears in a pattern literal.</summary>
		public string Key => Prefix + "#" + Suffix;

		public string Format(int sequence) => Prefix + sequence.ToString("D" + Width, CultureInfo.InvariantCulture) + Suffix;
	}

	/// <summary>
	/// Department record-number patterns (setting 72, <see cref="RecordsNumberingConfig.Pattern"/>). A pattern is literal
	/// text (letters, digits, '-', '_' and '.') around tokens: {PREFIX} the record type's prefix (RUN, TRN, INC...),
	/// {YYYY} or {YY} the year, {GROUP} the station/group as "G" plus its id, and {SEQ} the zero-padded sequence,
	/// which must appear exactly once. A department with no saved pattern keeps the one its checkboxes always produced.
	/// </summary>
	public static class RecordNumberFormat
	{
		public const string PrefixToken = "{PREFIX}";
		public const string YearToken = "{YYYY}";
		public const string ShortYearToken = "{YY}";
		public const string GroupToken = "{GROUP}";
		public const string SequenceToken = "{SEQ}";

		/// <summary>Keeps the rendered number inside the 50 character RecordNumber columns: only {GROUP} and {SEQ} render longer than written, by at most 4 and 5.</summary>
		public const int MaxPatternLength = 40;
		public const int MinWidth = 3;
		public const int MaxWidth = 8;
		public const int DefaultWidth = 4;
		public const int MaxSequence = 99999999;

		private static readonly string[] Tokens = { PrefixToken, YearToken, ShortYearToken, GroupToken, SequenceToken };

		/// <summary>The pattern the IncludeYear and PerGroupSequence checkboxes describe; it renders exactly the numbers they always have.</summary>
		public static string LegacyPattern(bool includeYear, bool perGroupSequence)
		{
			return PrefixToken + "-" + (perGroupSequence ? GroupToken + "-" : string.Empty) + (includeYear ? YearToken + "-" : string.Empty) + SequenceToken;
		}

		/// <summary>The saved pattern, or the legacy one when none is saved (or a saved one no longer validates).</summary>
		public static string EffectivePattern(RecordsNumberingConfig config)
		{
			config ??= new RecordsNumberingConfig();
			return IsValid(config.Pattern) ? Normalize(config.Pattern) : LegacyPattern(config.IncludeYear, config.PerGroupSequence);
		}

		public static int EffectiveWidth(int configuredWidth)
		{
			return Math.Max(MinWidth, Math.Min(MaxWidth, configuredWidth <= 0 ? DefaultWidth : configuredWidth));
		}

		public static bool IsValid(string pattern) => Parse(pattern) != null;

		/// <summary>Tokens are matched case-insensitively; this writes them back in their canonical upper case.</summary>
		public static string Normalize(string pattern)
		{
			var parts = Parse(pattern);
			return parts == null ? null : string.Concat(parts.Select(p => p.Token ?? p.Literal));
		}

		public static bool UsesToken(string pattern, string token)
		{
			return Parse(pattern)?.Any(p => p.Token == token) == true;
		}

		/// <summary>True when the pattern restarts its sequence every year.</summary>
		public static bool ResetsYearly(string pattern) => UsesToken(pattern, YearToken) || UsesToken(pattern, ShortYearToken);

		/// <summary>Renders the sequence scope a record falls in. An empty {GROUP} (a record with no group) takes the separator after it along.</summary>
		public static RecordNumberScope Resolve(string pattern, int width, string typePrefix, int year, int? groupId)
		{
			var parts = Parse(pattern) ?? throw new ArgumentException("The record number pattern is not valid.", nameof(pattern));
			var prefix = new StringBuilder();
			var suffix = new StringBuilder();
			var target = prefix;
			var dropSeparator = false;
			foreach (var part in parts)
			{
				if (part.Token == SequenceToken)
				{
					target = suffix;
					continue;
				}

				var text = part.Token == null ? part.Literal : Render(part.Token, typePrefix, year, groupId);
				if (dropSeparator && part.Token == null)
					text = text.Substring(1);
				dropSeparator = part.Token == GroupToken && text.Length == 0;
				target.Append(text);
			}

			return new RecordNumberScope(prefix.ToString(), suffix.ToString(), EffectiveWidth(width));
		}

		private static string Render(string token, string typePrefix, int year, int? groupId)
		{
			switch (token)
			{
				case PrefixToken: return typePrefix ?? string.Empty;
				case YearToken: return year.ToString("D4", CultureInfo.InvariantCulture);
				case ShortYearToken: return (year % 100).ToString("D2", CultureInfo.InvariantCulture);
				case GroupToken: return groupId.HasValue ? "G" + groupId.Value.ToString(CultureInfo.InvariantCulture) : string.Empty;
				default: return string.Empty;
			}
		}

		private static bool IsLiteral(char c) => (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || IsSeparator(c);

		private static bool IsSeparator(char c) => c == '-' || c == '_' || c == '.';

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

			// "G" plus an id has no fixed width, so a separator has to end it: otherwise G1 then 20001 reads the same as G12 then 0001.
			for (var p = 0; p < parts.Count; p++)
			{
				if (parts[p].Token == GroupToken && (p + 1 >= parts.Count || parts[p + 1].Token != null || !IsSeparator(parts[p + 1].Literal[0])))
					return null;
			}

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
}
