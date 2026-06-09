using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace Resgrid.Framework
{
	/// <summary>
	/// Sanitizes free-form text so it is safe to store in a PostgreSQL UTF-8 database.
	///
	/// PostgreSQL's UTF-8 <c>text</c>/<c>citext</c> columns reject content that SQL Server
	/// tolerates: the NUL character (U+0000) is forbidden outright, and invalid Unicode
	/// (unpaired UTF-16 surrogates) cannot be encoded as UTF-8 at all. This class neutralizes
	/// that content and, on a best-effort basis, repairs the most common Windows-1252 mojibake
	/// so the recovered character (smart quotes, dashes, etc.) is preserved instead of discarded.
	///
	/// The transform is pure and allocation-free for already-clean strings (the common case):
	/// <see cref="Clean"/> returns the original reference unchanged when nothing needs fixing.
	/// </summary>
	public static class Utf8Sanitizer
	{
		private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

		private static readonly ConcurrentDictionary<Type, PropertyInfo[]> StringPropertyCache = new ConcurrentDictionary<Type, PropertyInfo[]>();

		// C1 control range (U+0080 - U+009F). When Windows-1252 bytes are decoded as ISO-8859-1
		// (Latin-1) their punctuation lands here; these are the candidates for mojibake repair.
		private const char C1RangeStart = (char)0x80;
		private const char C1RangeEnd = (char)0x9F;

		// Highest ISO-8859-1 (Latin-1) code point; above this a char cannot be a single Latin-1 byte.
		private const char Latin1Max = (char)0xFF;

		/// <summary>The Unicode replacement character (U+FFFD) used for content that cannot be repaired.</summary>
		public const char ReplacementChar = (char)0xFFFD;

		/// <summary>
		/// Returns a copy of <paramref name="input"/> that is safe for a PostgreSQL UTF-8 column.
		/// Returns the original reference (no allocation) when the value is already clean.
		/// </summary>
		/// <param name="repairDoubleEncoding">
		/// When true, also attempts a conservative repair of double-encoded UTF-8. Off by default
		/// because it can false-positive on legitimately Latin-1-looking text.
		/// </param>
		/// <param name="normalization">
		/// When non-null, the (possibly repaired) result is normalized to this form. NUL/surrogate
		/// safety does not require normalization, so the write hot-path leaves this null.
		/// </param>
		public static string Clean(string input, bool repairDoubleEncoding = false, NormalizationForm? normalization = null)
		{
			TryClean(input, out var cleaned, repairDoubleEncoding, normalization);
			return cleaned;
		}

		/// <summary>
		/// Cleans <paramref name="input"/> and reports whether any change was required.
		/// <paramref name="cleaned"/> is the original reference when no change was needed.
		/// </summary>
		public static bool TryClean(string input, out string cleaned, bool repairDoubleEncoding = false, NormalizationForm? normalization = null)
		{
			cleaned = input;

			if (string.IsNullOrEmpty(input))
				return false;

			if (NeedsCleaning(input))
				cleaned = Rebuild(input);

			if (repairDoubleEncoding)
			{
				var repaired = RepairDoubleEncodedUtf8(cleaned);
				if (!ReferenceEquals(repaired, cleaned))
					cleaned = repaired;
			}

			if (normalization.HasValue && cleaned.Length > 0 && !cleaned.IsNormalized(normalization.Value))
				cleaned = cleaned.Normalize(normalization.Value);

			// Defensive final guard: the rebuild above removes NUL and lone surrogates, so this
			// should never trip, but it guarantees the contract that output is valid UTF-8.
			if (!IsValidUtf8(cleaned))
				cleaned = ForceValidUtf8(cleaned);

			return !ReferenceEquals(cleaned, input);
		}

		/// <summary>Returns true when the value can be stored in a PostgreSQL UTF-8 column as-is.</summary>
		public static bool IsClean(string input)
		{
			return string.IsNullOrEmpty(input) || !NeedsCleaning(input);
		}

		/// <summary>
		/// In-place sanitizes every public readable/writable string property of <paramref name="entity"/>.
		/// Used to guard the generic <c>new DynamicParameters(entity)</c> write path where individual
		/// values do not flow through <c>DynamicParametersExtension.Add</c>.
		/// </summary>
		public static void CleanEntity<T>(T entity, bool repairDoubleEncoding = false, NormalizationForm? normalization = null) where T : class
		{
			if (entity == null)
				return;

			var properties = StringPropertyCache.GetOrAdd(entity.GetType(), GetStringProperties);

			for (int i = 0; i < properties.Length; i++)
			{
				var property = properties[i];
				var current = (string)property.GetValue(entity);

				if (current == null)
					continue;

				if (TryClean(current, out var cleaned, repairDoubleEncoding, normalization))
					property.SetValue(entity, cleaned);
			}
		}

		private static PropertyInfo[] GetStringProperties(Type type)
		{
			var result = new List<PropertyInfo>();

			foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
			{
				if (property.PropertyType != typeof(string))
					continue;

				if (!property.CanRead || !property.CanWrite)
					continue;

				// Skip indexers.
				if (property.GetIndexParameters().Length > 0)
					continue;

				result.Add(property);
			}

			return result.ToArray();
		}

		private static bool NeedsCleaning(string input)
		{
			for (int i = 0; i < input.Length; i++)
			{
				char c = input[i];

				if (c == '\0')
					return true;

				if (c >= C1RangeStart && c <= C1RangeEnd)
					return true; // C1 range: candidate for Windows-1252 mojibake repair.

				if (char.IsHighSurrogate(c))
				{
					if (i + 1 >= input.Length || !char.IsLowSurrogate(input[i + 1]))
						return true; // unpaired high surrogate

					i++; // valid pair, skip the low surrogate
				}
				else if (char.IsLowSurrogate(c))
				{
					return true; // lone low surrogate
				}
			}

			return false;
		}

		private static string Rebuild(string input)
		{
			var sb = new StringBuilder(input.Length);

			for (int i = 0; i < input.Length; i++)
			{
				char c = input[i];

				if (c == '\0')
					continue; // strip NUL: forbidden in PostgreSQL text

				if (c >= C1RangeStart && c <= C1RangeEnd)
				{
					char mapped = MapCp1252C1(c);
					if (mapped != '\0')
						sb.Append(mapped);
					// else: undefined Windows-1252 byte -> drop
					continue;
				}

				if (char.IsHighSurrogate(c))
				{
					if (i + 1 < input.Length && char.IsLowSurrogate(input[i + 1]))
					{
						sb.Append(c);
						sb.Append(input[i + 1]);
						i++;
					}
					else
					{
						sb.Append(ReplacementChar); // unpaired high surrogate
					}

					continue;
				}

				if (char.IsLowSurrogate(c))
				{
					sb.Append(ReplacementChar); // lone low surrogate
					continue;
				}

				sb.Append(c);
			}

			return sb.ToString();
		}

		/// <summary>
		/// Maps a C1-range char (U+0080-U+009F) to the Windows-1252 character it most likely
		/// represents. These show up when Windows-1252 bytes were decoded as ISO-8859-1 (Latin-1).
		/// Returns '\0' for the five undefined Windows-1252 bytes (0x81, 0x8D, 0x8F, 0x90, 0x9D).
		/// </summary>
		private static char MapCp1252C1(char c)
		{
			switch ((int)c)
			{
				case 0x80: return (char)0x20AC; // EURO SIGN
				case 0x82: return (char)0x201A; // SINGLE LOW-9 QUOTATION MARK
				case 0x83: return (char)0x0192; // LATIN SMALL LETTER F WITH HOOK
				case 0x84: return (char)0x201E; // DOUBLE LOW-9 QUOTATION MARK
				case 0x85: return (char)0x2026; // HORIZONTAL ELLIPSIS
				case 0x86: return (char)0x2020; // DAGGER
				case 0x87: return (char)0x2021; // DOUBLE DAGGER
				case 0x88: return (char)0x02C6; // MODIFIER LETTER CIRCUMFLEX ACCENT
				case 0x89: return (char)0x2030; // PER MILLE SIGN
				case 0x8A: return (char)0x0160; // LATIN CAPITAL LETTER S WITH CARON
				case 0x8B: return (char)0x2039; // SINGLE LEFT-POINTING ANGLE QUOTATION MARK
				case 0x8C: return (char)0x0152; // LATIN CAPITAL LIGATURE OE
				case 0x8E: return (char)0x017D; // LATIN CAPITAL LETTER Z WITH CARON
				case 0x91: return (char)0x2018; // LEFT SINGLE QUOTATION MARK
				case 0x92: return (char)0x2019; // RIGHT SINGLE QUOTATION MARK
				case 0x93: return (char)0x201C; // LEFT DOUBLE QUOTATION MARK
				case 0x94: return (char)0x201D; // RIGHT DOUBLE QUOTATION MARK
				case 0x95: return (char)0x2022; // BULLET
				case 0x96: return (char)0x2013; // EN DASH
				case 0x97: return (char)0x2014; // EM DASH
				case 0x98: return (char)0x02DC; // SMALL TILDE
				case 0x99: return (char)0x2122; // TRADE MARK SIGN
				case 0x9A: return (char)0x0161; // LATIN SMALL LETTER S WITH CARON
				case 0x9B: return (char)0x203A; // SINGLE RIGHT-POINTING ANGLE QUOTATION MARK
				case 0x9C: return (char)0x0153; // LATIN SMALL LIGATURE OE
				case 0x9E: return (char)0x017E; // LATIN SMALL LETTER Z WITH CARON
				case 0x9F: return (char)0x0178; // LATIN CAPITAL LETTER Y WITH DIAERESIS
				default: return '\0';           // 0x81, 0x8D, 0x8F, 0x90, 0x9D are undefined in Windows-1252
			}
		}

		/// <summary>
		/// Conservative double-encoded-UTF-8 repair: if the whole string round-trips as
		/// Latin-1 bytes back into valid UTF-8 that differs from the input, return the decoded
		/// form. Only triggers when the string actually contains the high-Latin-1 range, so plain
		/// ASCII is never touched. Opt-in because it can misfire on genuine Latin-1 text.
		/// </summary>
		private static string RepairDoubleEncodedUtf8(string input)
		{
			if (string.IsNullOrEmpty(input))
				return input;

			bool hasHighLatin = false;
			for (int i = 0; i < input.Length; i++)
			{
				char c = input[i];
				if (c > Latin1Max)
					return input; // contains non-Latin-1: not a single-byte mojibake candidate
				if (c >= C1RangeStart)
					hasHighLatin = true;
			}

			if (!hasHighLatin)
				return input;

			try
			{
				var bytes = new byte[input.Length];
				for (int i = 0; i < input.Length; i++)
					bytes[i] = (byte)input[i];

				var decoded = StrictUtf8.GetString(bytes); // throws if not valid UTF-8
				return string.Equals(decoded, input, StringComparison.Ordinal) ? input : decoded;
			}
			catch (Exception)
			{
				return input; // bytes were not valid UTF-8 -> genuine Latin-1, leave alone
			}
		}

		private static bool IsValidUtf8(string input)
		{
			if (string.IsNullOrEmpty(input))
				return true;

			if (input.IndexOf('\0') >= 0)
				return false;

			try
			{
				StrictUtf8.GetByteCount(input);
				return true;
			}
			catch (EncoderFallbackException)
			{
				return false;
			}
		}

		private static string ForceValidUtf8(string input)
		{
			var permissive = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);
			var roundTripped = permissive.GetString(permissive.GetBytes(input));
			return roundTripped.Replace("\0", string.Empty);
		}
	}
}
