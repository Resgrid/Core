using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Resgrid.Web.Tts.Services
{
	/// <summary>
	/// Transforms dispatch jargon, abbreviations, and codes into expanded,
	/// pronounceable English that the TTS engine renders clearly.
	///
	/// The preprocessor runs inside audio generation, <em>after</em> the cache
	/// key has been computed from the raw request text (see TtsService) — two
	/// requests that only differ by abbreviation style therefore cache as
	/// separate entries even though they synthesise identical speech.
	///
	/// Expansion rules are deliberately conservative — we only touch terms
	/// that the engine routinely gets wrong.  Everything else is passed through
	/// unchanged.
	/// </summary>
	public sealed partial class TextPreprocessor : ITextPreprocessor
	{
		// The shorthand data lives in TtsShorthandCatalog; this class owns the
		// matching mechanics. Rules are compiled once, ordered longest-key-first so
		// "ALSEMS" matches before "ALS" and "W/M" before "W/".
		private static readonly IReadOnlyList<(Regex Pattern, string Replacement)> AbbreviationRules =
			CompileWordRules(TtsShorthandCatalog.Abbreviations);

		private static readonly IReadOnlyList<(Regex Pattern, string Replacement)> DispatchShorthandRules =
			CompileWordRules(TtsShorthandCatalog.DispatchShorthand);

		private static readonly IReadOnlyList<(Regex Pattern, string Replacement)> SlashNotationRules =
			CompileSymbolRules(TtsShorthandCatalog.SlashNotation);

		private static readonly IReadOnlyList<(Regex Pattern, string Replacement)> AddressSuffixRules =
			CompileAddressSuffixRules(TtsShorthandCatalog.AddressSuffixes);

		private static readonly IReadOnlyList<(Regex Pattern, string Replacement)> SpellOutRules =
			CompileWordRules(TtsShorthandCatalog.SpellOut);

		private static IReadOnlyList<(Regex, string)> CompileWordRules(IReadOnlyDictionary<string, string> map)
		{
			return map.OrderByDescending(entry => entry.Key.Length)
				.Select(entry => (
					new Regex($@"\b{Regex.Escape(entry.Key)}\b", RegexOptions.Compiled | RegexOptions.CultureInvariant),
					entry.Value))
				.ToList();
		}

		// Keys like "W/" and "Y/O" contain non-word characters that defeat the
		// standard \b anchor. Use lookaround boundaries instead: (?<!\w) ensures no
		// word character precedes, and (?!\w) ensures no word character follows.
		private static IReadOnlyList<(Regex, string)> CompileSymbolRules(IReadOnlyDictionary<string, string> map)
		{
			return map.OrderByDescending(entry => entry.Key.Length)
				.Select(entry => (
					new Regex($@"(?<!\w){Regex.Escape(entry.Key)}(?!\w)", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant),
					entry.Value))
				.ToList();
		}

		// Address suffixes are only expanded after a house/building number
		// (e.g. "123 Main St" → "123 Main Street"). The pattern anchors to a leading
		// digit (\b\d+\b), then lazily skips over the street name before matching the
		// suffix; the house number and street name are captured and re-emitted.
		private static IReadOnlyList<(Regex, string)> CompileAddressSuffixRules(IReadOnlyDictionary<string, string> map)
		{
			return map.OrderByDescending(entry => entry.Key.Length)
				.Select(entry => (
					new Regex($@"(\b\d+\b[\s\w,]*?)\b{Regex.Escape(entry.Key)}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant),
					"${1}" + entry.Value))
				.ToList();
		}

		// ---------------------------------------------------------------
		//  10-codes are never translated (meanings vary by agency); the
		//  ExpandTenCodes pass below only drops the dash so "10-4" is spoken
		//  as a paced "ten four". See TtsShorthandCatalog's ground rules.
		// ---------------------------------------------------------------

		private static readonly Regex LongNumberRegex = LongNumberExpandoRegex();
		private static readonly Regex WhitespaceRegex = WhitespaceExpandoRegex();
		private static readonly Regex UnitIdentifierRegex = UnitIdentifierExpandoRegex();
		private static readonly Regex NumberToWordRegexField = NumberToWordRegex();
		private static readonly Regex AgeYoSexRegexField = AgeYoSexRegex();
		private static readonly Regex AgeSlashSexRegexField = AgeSlashSexRegex();
		private static readonly Regex AgeJoinedSexRegexField = AgeJoinedSexRegex();
		private static readonly Regex TenCodeRegexField = TenCodeRegex();
		private readonly ILogger<TextPreprocessor> _logger;

		public TextPreprocessor(ILogger<TextPreprocessor> logger)
		{
			_logger = logger;
		}

		public string Preprocess(string text, string voice)
		{
			if (string.IsNullOrWhiteSpace(text))
			{
				return text ?? string.Empty;
			}

			var result = text.Trim();

			// Only preprocess English voices — they need their own abbreviation
			// dictionaries.  Other languages pass through to Piper directly.
			if (IsEnglishVoice(voice))
			{
				var original = result;

				// Order matters: expand abbreviations first so downstream
				// passes operate on natural-language words rather than codes.
				result = ExpandAbbreviations(result);
				result = ExpandDispatchShorthand(result);
				// Age/sex before slash notation so "35/F" is consumed as a patient
				// descriptor rather than reaching the generic slash handling.
				result = ExpandAgeSexShorthand(result);
				result = ExpandSlashNotation(result);
				result = ExpandAddressAbbreviations(result);
				// After every expansion map, so a mapped meaning always beats
				// letter-spelling; before the number passes, which never touch letters.
				result = ExpandSpellOutCodes(result);
				result = ExpandTenCodes(result);
				result = ExpandUnitIdentifiers(result);
				result = ExpandLongNumbers(result);
				result = NormalizeSmallNumbers(result);
				// Collapse any whitespace artefacts introduced by expansion.
				result = WhitespaceRegex.Replace(result, " ").Trim();

				if (!string.Equals(original, result, StringComparison.Ordinal))
				{
					_logger.LogDebug(
						"TextPreprocessor normalised \"{OriginalText}\" to \"{NormalisedText}\"",
						original,
						result);
				}
			}

			// Ensure the text ends with sentence-ending punctuation so that
			// Piper does not hallucinate extra speech past the intended end.
			// Without a clear sentence boundary, neural TTS models may
			// continue generating audio that sounds like additional words.
			if (result.Length > 0 && result[^1] is not '.' and not '!' and not '?')
			{
				result += ".";
			}

			return result;
		}

		// ---------------------------------------------------------------
		//  Abbreviation expansion (word-boundary-aware)
		// ---------------------------------------------------------------

		private static string ExpandAbbreviations(string text)
		{
			// Matching is case-sensitive: short tokens like "SO", "PASS", "CO" and "PI"
			// collide with ordinary English words when lowercased, and CAD feeds emit
			// these codes in upper case (the catalog carries explicit casing variants
			// such as "HAZMAT"/"HazMat" where more than one form is expected).
			foreach (var (pattern, replacement) in AbbreviationRules)
			{
				text = pattern.Replace(text, replacement);
			}

			return text;
		}

		/// <summary>
		/// Expands raw CAD/dispatch shorthand tokens — the cryptic codes that
		/// CAD systems embed in their email/API feed output.
		/// <br/>
		/// Example: "RP ADV 2 VEH MVC" → "Reporting Party Advised 2 Vehicle Motor Vehicle Collision"
		/// </summary>
		private static string ExpandDispatchShorthand(string text)
		{
			// Case-sensitive for the same reason as ExpandAbbreviations: "APT", "PT",
			// "RM" and "ADV" lowercased are (parts of) ordinary words, and CAD systems
			// emit shorthand in upper case.
			foreach (var (pattern, replacement) in DispatchShorthandRules)
			{
				text = pattern.Replace(text, replacement);
			}

			return text;
		}

		/// <summary>
		/// Expands CAD patient age/sex shorthand into spoken English:
		/// <br/>
		///   "35/F", "35/f"        → "35 Year Old Female"
		///   "35F", "35f"          → "35 Year Old Female" (two/three digit ages only)
		///   "35YOM", "35 yof"     → "35 Year Old Male" / "35 Year Old Female"
		///   "35yo", "35 YO"       → "35 Year Old"
		/// <br/>
		/// The joined digits+letter form requires a 2-3 digit age: single-digit
		/// tokens like "Apt 5F" or grid references collide too easily. The negative
		/// lookbehind keeps highway ("I-35F"), decimal and fraction contexts out.
		/// A trailing Fahrenheit reading ("101F") is misread as an age — patient
		/// descriptors vastly outnumber temperatures in dispatch text.
		/// </summary>
		private static string ExpandAgeSexShorthand(string text)
		{
			text = AgeYoSexRegexField.Replace(text, match =>
				$"{match.Groups["age"].Value} Year Old{SexWord(match.Groups["sex"].Value)}");
			text = AgeSlashSexRegexField.Replace(text, match =>
				$"{match.Groups["age"].Value} Year Old{SexWord(match.Groups["sex"].Value)}");
			text = AgeJoinedSexRegexField.Replace(text, match =>
				$"{match.Groups["age"].Value} Year Old{SexWord(match.Groups["sex"].Value)}");

			return text;
		}

		private static string SexWord(string sexToken)
		{
			if (string.IsNullOrEmpty(sexToken))
				return string.Empty;

			return char.ToUpperInvariant(sexToken[0]) == 'M' ? " Male" : " Female";
		}

		/// <summary>
		/// Converts slash-delimited abbreviations into spoken English so
		/// the engine doesn't say the word "slash" aloud.
		/// <br/>
		/// Example: "75 Y/O" → "75 Year Old" (instead of "75 Y slash O")
		/// </summary>
		private static string ExpandSlashNotation(string text)
		{
			foreach (var (pattern, replacement) in SlashNotationRules)
			{
				text = pattern.Replace(text, replacement);
			}

			return text;
		}

		/// <summary>
		/// Converts standalone small numbers (1-20) into word form when they
		/// precede an alphabetic word, so that "2 patients" is spoken as
		/// "two patients" rather than having the digit read in isolation.
		/// <br/>
		/// Numbers followed by a digit or numeric suffix (e.g. "1st", "2nd")
		/// are left as-is — they're already handled by the engine's digit parser.
		/// </summary>
		private static string NormalizeSmallNumbers(string text)
		{
			// Match a standalone digit sequence (1-20) followed by a space and
			// a letter, but NOT followed by another digit character.
			// Group1 = the digits; Group2 = the first letter of the following word.
			return NumberToWordRegexField.Replace(text, match =>
			{
				var digits = match.Groups[1].Value;
				var following = match.Groups[2].Value;
				if (int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var num)
				    && num >= 1 && num <= 20)
				{
					return SmallNumberWords[num] + " " + following;
				}

				return match.Value;
			});
		}

		private static readonly Dictionary<int, string> SmallNumberWords = new()
		{
			{ 1, "one" },  { 2, "two" },    { 3, "three" },   { 4, "four" },   { 5, "five" },
			{ 6, "six" },  { 7, "seven" },  { 8, "eight" },   { 9, "nine" },   { 10, "ten" },
			{ 11, "eleven" }, { 12, "twelve" }, { 13, "thirteen" }, { 14, "fourteen" },
			{ 15, "fifteen" }, { 16, "sixteen" }, { 17, "seventeen" }, { 18, "eighteen" },
			{ 19, "nineteen" }, { 20, "twenty" },
		};

		private static string ExpandAddressAbbreviations(string text)
		{
			foreach (var (pattern, replacement) in AddressSuffixRules)
			{
				text = pattern.Replace(text, replacement);
			}

			return text;
		}

		/// <summary>
		/// Reads codes with no safe expansion as spaced letters ("MI" → "M I") so
		/// each letter is spoken distinctly instead of running together as a word.
		/// </summary>
		private static string ExpandSpellOutCodes(string text)
		{
			foreach (var (pattern, replacement) in SpellOutRules)
			{
				text = pattern.Replace(text, replacement);
			}

			return text;
		}

		/// <summary>
		/// Paces radio ten-codes: "10-4" → "10 4", spoken "ten four" instead of a
		/// hurried "ten dash four". The numbers are kept (meanings vary by agency).
		/// </summary>
		private static string ExpandTenCodes(string text)
		{
			return TenCodeRegexField.Replace(text, "${prefix} ${code}");
		}

		private static string ExpandUnitIdentifiers(string text)
		{
			// Transform common unit-identifier patterns so the engine speaks them
			// as separate words:
			//   "E1"   → "E 1"    (engine one)
			//   "M2"   → "M 2"    (medic two)
			//   "B3"   → "B 3"    (battalion three)
			//   "L14"  → "L 14"   (ladder fourteen)

			return UnitIdentifierRegex.Replace(text, m =>
			{
				var prefix = m.Groups[1].Value;
				var number = m.Groups[2].Value;
				return $"{prefix} {number}";
			});
		}

		// ---------------------------------------------------------------
		//  Voice detection
		// ---------------------------------------------------------------

		private static bool IsEnglishVoice(string voice)
		{
			if (string.IsNullOrWhiteSpace(voice))
			{
				return false;
			}

			var trimmed = voice.Trim();
			var variantSeparatorIndex = trimmed.IndexOf('+');
			var baseVoice = variantSeparatorIndex <= 0 ? trimmed : trimmed[..variantSeparatorIndex];

			return string.Equals(baseVoice, "en", StringComparison.OrdinalIgnoreCase)
				|| baseVoice.StartsWith("en-", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(baseVoice, "mb-us1", StringComparison.OrdinalIgnoreCase);
		}

		// ---------------------------------------------------------------
		//  Long number expansion for clarity
		// ---------------------------------------------------------------

		/// <summary>
		/// Converts digit sequences of 4 or more consecutive digits into
		/// individual space-separated digits so that Piper reads them as
		/// digit-by-digit (e.g. "12345" → "1 2 3 4 5") rather than as
		/// a large composite number ("twelve thousand three hundred forty-five").
		/// This is critical for address numbers, call IDs, and other dispatch
		/// identifiers where digit-by-digit reading is significantly clearer.
		/// </summary>
		private static string ExpandLongNumbers(string text)
		{
			return LongNumberRegex.Replace(text, match =>
			{
				var digits = match.Groups[1].Value;
				return match.Value.Replace(digits, string.Join(" ", digits.ToCharArray()));
			});
		}

		// ---------------------------------------------------------------
		//  Source-generated regex helpers
		// ---------------------------------------------------------------

		/// <summary>Matches a single letter followed by digits, as a whole word.</summary>
		[GeneratedRegex(@"\b(?<prefix>[A-Z])(?<number>\d+)\b", RegexOptions.CultureInvariant)]
		private static partial Regex UnitIdentifierExpandoRegex();

		/// <summary>
		/// Matches standalone digits 1-20 followed by a space and a letter.
		/// The negative lookbehind skips digits that are part of a spaced
		/// digit-by-digit run produced by ExpandLongNumbers (e.g. "1 2 3 4 5 Elm"),
		/// which must stay uniformly digit-form rather than ending "...4 five Elm".
		/// </summary>
		[GeneratedRegex(@"(?<!\d\s)\b(?<digits>(?:[1-9]|1[0-9]|20))\s(?<letter>[A-Za-z])", RegexOptions.CultureInvariant)]
		private static partial Regex NumberToWordRegex();

		/// <summary>Matches a run of 4+ consecutive digits not surrounded by other digits.</summary>
		[GeneratedRegex(@"(?<!\d)(?<digits>\d{4,})(?!\d)", RegexOptions.CultureInvariant)]
		private static partial Regex LongNumberExpandoRegex();

		/// <summary>Collapses multiple whitespace characters into a single space.</summary>
		[GeneratedRegex(@"\s+")]
		private static partial Regex WhitespaceExpandoRegex();

		/// <summary>Matches "35YO", "35 yo", "35YOM"/"35 yof" — age + YO + optional sex.</summary>
		[GeneratedRegex(@"(?<![\w\-./])(?<age>\d{1,3})\s*YO(?<sex>[MF])?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
		private static partial Regex AgeYoSexRegex();

		/// <summary>Matches "35/F", "9/m" — age, slash, sex letter.</summary>
		[GeneratedRegex(@"(?<![\w\-./])(?<age>\d{1,3})\s*/\s*(?<sex>[MF])\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
		private static partial Regex AgeSlashSexRegex();

		/// <summary>Matches "35F", "104m" — joined age + sex letter, 2-3 digit ages only.</summary>
		[GeneratedRegex(@"(?<![\w\-./])(?<age>\d{2,3})(?<sex>[MF])\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
		private static partial Regex AgeJoinedSexRegex();

		/// <summary>Matches radio ten-codes and eleven-codes: "10-4", "11-99".</summary>
		[GeneratedRegex(@"\b(?<prefix>1[01])-(?<code>\d{1,3})\b", RegexOptions.CultureInvariant)]
		private static partial Regex TenCodeRegex();
	}
}