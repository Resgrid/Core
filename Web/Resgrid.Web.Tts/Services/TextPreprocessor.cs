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
	/// separate entries even though they synthesise identical speech. For the same
	/// reason a change to the output must bump <see cref="OutputRevision"/>.
	///
	/// Structural passes (markup, run-on sentences, dash pauses, sentence length)
	/// apply to every voice; vocabulary and number passes are English-only.
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
		// A street suffix belongs to the street phrase, so its bridge stops at a comma
		// — otherwise the house number reaches into the following clause and rewrites
		// an unrelated word ("100 Center St, Dr Jones" → "Drive Jones"). Sub-unit
		// designators are exempt: CAD writes them after a comma ("123 Main St, Apt 4").
		private static IReadOnlyList<(Regex, string)> CompileAddressSuffixRules(IReadOnlyDictionary<string, string> map)
		{
			return map.OrderByDescending(entry => entry.Key.Length)
				.Select(entry =>
				{
					var bridge = TtsShorthandCatalog.UnitDesignators.Contains(entry.Key) ? @"[\s\w,]" : @"[\s\w]";

					return (
						new Regex($@"(\b\d+\b{bridge}*?)\b{Regex.Escape(entry.Key)}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant),
						"${1}" + entry.Value);
				})
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
		private static readonly Regex EmphasisHeaderRegexField = EmphasisHeaderRegex();
		private static readonly Regex DecorativeSymbolRegexField = DecorativeSymbolRegex();
		private static readonly Regex RunOnSentenceRegexField = RunOnSentenceRegex();
		private static readonly Regex SpacedDashRegexField = SpacedDashRegex();
		private static readonly Regex DanglingDashRegexField = DanglingDashRegex();
		private static readonly Regex PhoneNumberRegexField = PhoneNumberRegex();
		private static readonly Regex LocalPhoneNumberRegexField = LocalPhoneNumberRegex();
		private static readonly Regex DigitListCommaRegexField = DigitListCommaRegex();
		private static readonly Regex SentenceSplitRegexField = SentenceSplitRegex();

		/// <summary>
		/// Revision of the text this class hands to Piper. It is folded into the TTS
		/// cache key (see <see cref="AudioProcessingService.GetEffectiveSynthesisProfile"/>)
		/// because the key is computed from the raw request text, before preprocessing:
		/// without it, audio synthesised from the old output keeps being replayed after a
		/// fix. Bump it whenever a change alters the preprocessed text of existing input.
		/// </summary>
		public const int OutputRevision = 2;

		// ---------------------------------------------------------------
		//  Sentence pacing. Piper synthesises one sentence per inference, and
		//  measured on en_US-ryan-medium (same settings as production) a long
		//  sentence does not just take longer — past roughly 500 phoneme ids
		//  (~50 words of prose) the audio stops growing and the words are
		//  crammed and slurred into it: 52 words took 11.6 s, 72 words 12.3 s,
		//  and a 37-word dispatch nature full of numbers came out at ~9.6
		//  syllables a second, twice the normal rate ("sped up gibberish").
		//  Every sentence is kept well under that, at clause boundaries where
		//  the text offers them.
		// ---------------------------------------------------------------

		/// <summary>Upper bound on a sentence's estimated spoken length (see EstimateSpokenLength).</summary>
		public const int MaxSpokenSentenceLength = 160;

		/// <summary>A clause or conjunction break is only taken if the sentence before it is at least this long.</summary>
		private const int MinSentenceFragmentLength = 40;

		/// <summary>Each digit is spoken as a whole word ("seven", "three hundred"), so it weighs more than a letter.</summary>
		private const int SpokenDigitWeight = 6;

		private static readonly HashSet<string> BreakBeforeConjunctions = new(StringComparer.OrdinalIgnoreCase)
		{
			"and", "or", "then", "but", "while", "where", "until"
		};

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

			var original = text.Trim();

			// Structural passes run for every voice: they fix how the text is broken
			// into sentences and pauses, which every Piper model depends on equally.
			var result = StripDecorativeSymbols(original);
			result = SeparateRunOnSentences(result);
			result = NormalizeDashPauses(result);

			// Vocabulary and number passes are English-only — they need their own
			// abbreviation dictionaries. Other languages pass through to Piper directly.
			if (IsEnglishVoice(voice))
			{
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
				// Phone numbers first among the number passes: once grouped into
				// single digits no later pass (ten-codes, long numbers) can split them.
				result = ExpandPhoneNumbers(result);
				result = SpaceDigitLists(result);
				result = ExpandTenCodes(result);
				result = ExpandUnitIdentifiers(result);
				result = ExpandLongNumbers(result);
				result = NormalizeSmallNumbers(result);
			}

			// Collapse any whitespace artefacts introduced by the passes above.
			result = WhitespaceRegex.Replace(result, " ").Trim();
			// Last, so the length estimate sees the text exactly as Piper will.
			result = PaceLongSentences(result);

			if (!string.Equals(original, result, StringComparison.Ordinal))
			{
				_logger.LogDebug(
					"TextPreprocessor normalised \"{OriginalText}\" to \"{NormalisedText}\"",
					original,
					result);
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
		//  Structural passes (every voice)
		// ---------------------------------------------------------------

		/// <summary>
		/// Removes markup the engine would otherwise read aloud: "**REQUEST" was spoken
		/// as "asterisk asterisk request" run together into one word. A CAD-style header
		/// wrapped in emphasis at the start of a clause ("Nature, ***LOST HIKERS*** 2
		/// FEMALE...") becomes its own sentence so it is heard as a heading; emphasis in
		/// the middle of a sentence ("Please **DO NOT** enter") is simply unwrapped.
		/// </summary>
		private static string StripDecorativeSymbols(string text)
		{
			if (text.IndexOfAny(DecorativeSymbols) < 0)
			{
				return text;
			}

			var source = text;
			text = EmphasisHeaderRegexField.Replace(source, match =>
			{
				var body = match.Groups["body"].Value.Trim();
				var before = source[..match.Index].TrimEnd();
				var startsClause = before.Length == 0 || before[^1] is '.' or '!' or '?' or ':' or ';' or ',';

				if (!startsClause || body.Any(char.IsLower))
				{
					return $" {body} ";
				}

				body = body.TrimEnd(':', ';', ',', '-', ' ');
				return body.Length > 0 && body[^1] is '.' or '!' or '?' ? $" {body} " : $" {body}. ";
			});

			return DecorativeSymbolRegexField.Replace(text, " ");
		}

		private static readonly char[] DecorativeSymbols = { '*', '#', '=', '~', '_', '^', '|' };

		/// <summary>
		/// Restores the sentence break in "missing.Text Powell": with no space after the
		/// period the engine reads "missing dot text" and the two sentences merge into one
		/// long one. Requires two letters before and a capital after, so initialisms
		/// ("U.S.A") and lower-case domains ("resgrid.com") are left alone.
		/// </summary>
		private static string SeparateRunOnSentences(string text)
		{
			return RunOnSentenceRegexField.Replace(text, "$1 ");
		}

		/// <summary>
		/// A dash used as a separator ("APPROVED - San Luis", "UPDATE- Missing Person")
		/// produces no pause at all, so the clauses run together. Turn it into a comma.
		/// Hyphenated words, ranges and negative numbers ("I-5", "10-4", "-117.7") have
		/// no space on both sides and are untouched.
		/// </summary>
		private static string NormalizeDashPauses(string text)
		{
			if (text.IndexOf('-') < 0 && text.IndexOf('–') < 0 && text.IndexOf('—') < 0)
			{
				return text;
			}

			text = SpacedDashRegexField.Replace(text, ", ");
			return DanglingDashRegexField.Replace(text, ",");
		}

		/// <summary>
		/// Keeps every sentence under <see cref="MaxSpokenSentenceLength"/> (see the pacing
		/// note above). An over-long sentence is broken at the last clause boundary (comma,
		/// semicolon, colon) that fits, else before a joining word ("and", "or", "then"...),
		/// else at the last space that fits — preferring one that leaves a number with the
		/// word introducing it. Breaks never fall inside a run of digits, so a phone number
		/// or a grouped "3 2 3, 5 7 3" is never cut in half.
		/// </summary>
		private static string PaceLongSentences(string text)
		{
			if (EstimateSpokenLength(text, 0, text.Length) <= MaxSpokenSentenceLength)
			{
				return text;
			}

			var pieces = new List<string>();

			foreach (var sentence in SentenceSplitRegexField.Split(text))
			{
				var remaining = sentence.Trim();

				while (remaining.Length > 0 && EstimateSpokenLength(remaining, 0, remaining.Length) > MaxSpokenSentenceLength)
				{
					var cut = FindSentenceBreak(remaining);

					if (cut <= 0)
					{
						break;
					}

					var head = remaining[..cut].TrimEnd(' ', ',', ';', ':');
					pieces.Add(head.Length > 0 && head[^1] is '.' or '!' or '?' ? head : head + ".");
					remaining = remaining[(cut + 1)..].TrimStart();
				}

				if (remaining.Length > 0)
				{
					pieces.Add(remaining);
				}
			}

			return string.Join(" ", pieces);
		}

		/// <summary>
		/// Returns the index of the character to break the sentence at (a clause
		/// separator or a space, consumed by the break), or -1 when nothing fits.
		/// </summary>
		private static int FindSentenceBreak(string sentence)
		{
			var lastClause = -1;
			var lastConjunction = -1;
			var lastSpaceBeforeWord = -1;
			var lastSpace = -1;
			var spokenLength = 0;

			for (var i = 0; i < sentence.Length; i++)
			{
				var c = sentence[i];
				spokenLength += char.IsDigit(c) ? SpokenDigitWeight : 1;

				if (spokenLength > MaxSpokenSentenceLength)
				{
					break;
				}

				var next = i + 1 < sentence.Length ? sentence[i + 1] : '\0';
				var prev = i > 0 ? sentence[i - 1] : '\0';

				if (c is ',' or ';' or ':' && next == ' ')
				{
					// "3 2 3, 5 7 3" — a comma between digits groups a number; keep it whole.
					var afterSpace = i + 2 < sentence.Length ? sentence[i + 2] : '\0';
					if (!(char.IsDigit(prev) && char.IsDigit(afterSpace)) && spokenLength >= MinSentenceFragmentLength)
					{
						lastClause = i;
					}
				}
				else if (c == ' ' && !(char.IsDigit(next) && (char.IsDigit(prev) || (prev == ',' && i > 1 && char.IsDigit(sentence[i - 2])))))
				{
					lastSpace = i;

					// A number stays with the word that introduces it ("at 3 2 3, ..."),
					// so a plain space break before a digit is the last resort.
					if (!char.IsDigit(next))
					{
						lastSpaceBeforeWord = i;
					}

					// "and then": break before the first joining word, never between the two.
					if (spokenLength >= MinSentenceFragmentLength && StartsWithConjunction(sentence, i + 1) && !EndsWithConjunction(sentence, i))
					{
						lastConjunction = i;
					}
				}
			}

			if (lastClause > 0)
			{
				return lastClause;
			}

			if (lastConjunction > 0)
			{
				return lastConjunction;
			}

			return lastSpaceBeforeWord > 0 ? lastSpaceBeforeWord : lastSpace;
		}

		private static bool StartsWithConjunction(string sentence, int start)
		{
			var end = start;
			while (end < sentence.Length && char.IsLetter(sentence[end]))
			{
				end++;
			}

			return end > start && BreakBeforeConjunctions.Contains(sentence[start..end]);
		}

		private static bool EndsWithConjunction(string sentence, int end)
		{
			var start = end;
			while (start > 0 && char.IsLetter(sentence[start - 1]))
			{
				start--;
			}

			return end > start && BreakBeforeConjunctions.Contains(sentence[start..end]);
		}

		/// <summary>
		/// Rough spoken length of a span: one per character, with digits weighted as the
		/// whole words they are read as. Only used for pacing, so it only has to rank
		/// sentences the same way the engine's phoneme count would.
		/// </summary>
		public static int EstimateSpokenLength(string text, int start, int length)
		{
			var total = 0;

			for (var i = start; i < start + length; i++)
			{
				total += char.IsDigit(text[i]) ? SpokenDigitWeight : 1;
			}

			return total;
		}

		// ---------------------------------------------------------------
		//  Phone numbers and digit lists (English)
		// ---------------------------------------------------------------

		/// <summary>
		/// Reads phone numbers digit by digit in their groups: "323-573-4264" becomes
		/// "3 2 3, 5 7 3, 4 2 6 4" — spoken "three two three, five seven three, four two
		/// six four" — instead of "three hundred twenty three dash five hundred seventy
		/// three dash four two six four". A leading +1 country code is dropped.
		/// </summary>
		private static string ExpandPhoneNumbers(string text)
		{
			text = PhoneNumberRegexField.Replace(text, match =>
				$"{SpaceDigits(match.Groups["area"].Value)}, {SpaceDigits(match.Groups["exchange"].Value)}, {SpaceDigits(match.Groups["line"].Value)}");

			return LocalPhoneNumberRegexField.Replace(text, match =>
				$"{SpaceDigits(match.Groups["exchange"].Value)}, {SpaceDigits(match.Groups["line"].Value)}");
		}

		private static string SpaceDigits(string digits)
		{
			return string.Join(" ", digits.ToCharArray());
		}

		/// <summary>
		/// "type 1,2,3" is read "one two three" with no pause at all; a space after each
		/// comma restores it. Thousands separators ("30,000", "1,250") — a comma followed
		/// by exactly three digits — are left alone.
		/// </summary>
		private static string SpaceDigitLists(string text)
		{
			return DigitListCommaRegexField.Replace(text, ", ");
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
		/// The other lookbehinds keep decimals and digit lists uniform the same way:
		/// "1.5 AND" must not become "1.five AND", nor "1, 2, 3 for" end "1, 2, three for".
		/// </summary>
		[GeneratedRegex(@"(?<!\d\s)(?<!\d[.,])(?<!\d,\s)\b(?<digits>(?:[1-9]|1[0-9]|20))\s(?<letter>[A-Za-z])", RegexOptions.CultureInvariant)]
		private static partial Regex NumberToWordRegex();

		/// <summary>
		/// A clause wrapped in matching emphasis runs: "***LOST HIKERS***", "**CANCELLED**",
		/// "## UPDATE ##". The body may not contain the markers or span a line.
		/// </summary>
		[GeneratedRegex(@"(?<mark>[*#=~_]{2,})\s*(?<body>[^*#=~_\r\n]{1,80}?)\s*\k<mark>", RegexOptions.CultureInvariant)]
		private static partial Regex EmphasisHeaderRegex();

		/// <summary>Any asterisk, or a repeated run of other decoration ("=====", "###", "~~").</summary>
		[GeneratedRegex(@"\*+|([#=~_^|])\1+", RegexOptions.CultureInvariant)]
		private static partial Regex DecorativeSymbolRegex();

		/// <summary>A sentence end with no space after it: "missing.Text", "MISSING.TEXT".</summary>
		[GeneratedRegex(@"(?<=\p{L}{2})([.!?])(?=\p{Lu}\p{L})", RegexOptions.CultureInvariant)]
		private static partial Regex RunOnSentenceRegex();

		/// <summary>A separator dash with whitespace on both sides, absorbing a comma before it.</summary>
		[GeneratedRegex(@"(?:\s*,)?\s+(?:-{1,3}|[–—])\s+", RegexOptions.CultureInvariant)]
		private static partial Regex SpacedDashRegex();

		/// <summary>A dash hanging off the end of a word before a space: "UPDATE- Missing".</summary>
		[GeneratedRegex(@"(?<=\p{L})-(?=\s)", RegexOptions.CultureInvariant)]
		private static partial Regex DanglingDashRegex();

		/// <summary>
		/// North American number: optional +1, area code (optionally in parentheses), then
		/// exchange and line with a separator. Not part of a longer number, word or decimal;
		/// a sentence-ending period or comma after it is fine.
		/// </summary>
		[GeneratedRegex(@"(?<![\w.])(?:\+?1[\s.-])?\(?(?<area>\d{3})\)?[\s.-]?(?<exchange>\d{3})[\s.-](?<line>\d{4})(?!\w|[.,\-]\d)", RegexOptions.CultureInvariant)]
		private static partial Regex PhoneNumberRegex();

		/// <summary>Seven-digit local number written with a hyphen or dot: "573-4264".</summary>
		[GeneratedRegex(@"(?<![\w.\-])(?<exchange>\d{3})[.\-](?<line>\d{4})(?!\w|[.,\-]\d)", RegexOptions.CultureInvariant)]
		private static partial Regex LocalPhoneNumberRegex();

		/// <summary>A comma inside a digit list ("1,2,3") — not a thousands separator (exactly three digits after).</summary>
		[GeneratedRegex(@"(?<=\d),(?=\d{1,2}(?!\d))", RegexOptions.CultureInvariant)]
		private static partial Regex DigitListCommaRegex();

		/// <summary>Whitespace after sentence-ending punctuation.</summary>
		[GeneratedRegex(@"(?<=[.!?])\s+", RegexOptions.CultureInvariant)]
		private static partial Regex SentenceSplitRegex();

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