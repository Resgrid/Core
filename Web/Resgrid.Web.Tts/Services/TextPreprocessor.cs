using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Resgrid.Web.Tts.Services
{
	/// <summary>
	/// Transforms dispatch jargon, abbreviations, and codes into expanded,
	/// pronounceable English that the TTS engine renders clearly.
	///
	/// The preprocessor runs <em>before</em> the cache key is computed so that
	/// two requests that differ only by abbreviation style share the same
	/// synthesised audio.
	///
	/// Expansion rules are deliberately conservative — we only touch terms
	/// that the engine routinely gets wrong.  Everything else is passed through
	/// unchanged.
	/// </summary>
	public sealed partial class TextPreprocessor : ITextPreprocessor
	{
		// ---------------------------------------------------------------
		//  Fire / EMS / Police dispatch abbreviations
		//  Ordered longest-first so "HAZMAT" matches before "MAT".
		// ---------------------------------------------------------------
		private static readonly Dictionary<string, string> AbbreviationMap = new(StringComparer.Ordinal)
		{
			// Patient / incident descriptors
			{ "SFD",   "Single Family Dwelling" },
			{ "MFD",   "Multi-Family Dwelling" },
			{ "MCI",   "Mass Casualty Incident" },
			{ "MVC",   "Motor Vehicle Collision" },
			{ "MVA",   "Motor Vehicle Accident" },
			{ "PI",    "Personal Injury" },
			{ "GSW",   "Gunshot Wound" },
			{ "DOA",   "Dead on Arrival" },
			{ "CPR",   "Cardio Pulmonary Resuscitation" },
			{ "AED",   "Automated External Defibrillator" },
			{ "CO",    "Carbon Monoxide" },
			{ "UTL",   "Unable to Locate" },
			{ "ETA",   "Estimated Time of Arrival" },

			// Service types
			{ "ALS",   "Advanced Life Support" },
			{ "BLS",   "Basic Life Support" },
			{ "EMS",   "Emergency Medical Services" },
			{ "ALSEMS","Advanced Life Support Emergency Medical Services" },

			// Agencies
			{ "HAZMAT","Hazardous Materials" },
			{ "HazMat","Hazardous Materials" },
			{ "WMD",   "Weapons of Mass Destruction" },
			{ "PD",    "Police Department" },
			{ "FD",    "Fire Department" },
			{ "SO",    "Sheriff's Office" },
			{ "SAR",   "Search and Rescue" },

			// Incident command
			{ "IC",    "Incident Command" },
			{ "PIO",   "Public Information Officer" },
			{ "POV",   "Personally Owned Vehicle" },

			// Firefighting equipment / tactics
			{ "SCBA",  "Self-Contained Breathing Apparatus" },
			{ "PASS",  "Personal Alert Safety System" },
			{ "RIT",   "Rapid Intervention Team" },
			{ "PPE",   "Personal Protective Equipment" },
			{ "PAR",   "Personnel Accountability Report" },

			// Medical
			{ "DNR",   "Do Not Resuscitate" },
			{ "CPAP",  "Continuous Positive Airway Pressure" },
			{ "BVM",   "Bag Valve Mask" },

			// Command / operations
			{ "SOP",   "Standard Operating Procedure" },
			{ "SME",   "Subject Matter Expert" },

			// Miscellaneous
			{ "FAQ",   "Frequently Asked Questions" },
		};

		// ---------------------------------------------------------------
		//  CAD / dispatch shorthand that the engine reads letter-by-letter
		//  or mispronounces as garbled words.  These are the raw tokens
		//  that appear in CAD-to-email or CAD-to-API dispatch feeds.
		//  Ordered longest-first.
		// ---------------------------------------------------------------
		private static readonly Dictionary<string, string> DispatchShorthandMap = new(StringComparer.Ordinal)
		{
			// Transport & entrapment
			{ "XPORT", "Transport" },
			{ "ENTRP", "Entrapment" },

			// Structures
			{ "BLDG",  "Building" },
			{ "APT",   "Apartment" },
			{ "RM",    "Room" },

			// Address references
			{ "ADDR",  "Address" },
			{ "BLK",   "Block" },
			{ "CS",    "Cross Street" },
			{ "LOC",   "Location" },

			// Patient / person descriptors
			{ "YOM",   "Year Old Male" },
			{ "YOF",   "Year Old Female" },
			{ "PTS",   "Patients" },
			{ "PT",    "Patient" },
			{ "UNC",   "Unconscious" },
			{ "UNK",   "Unknown" },
			{ "INJ",   "Injuries" },
			{ "RP",    "Reporting Party" },

			// Vehicles
			{ "VEH",   "Vehicle" },
			{ "VEC",   "Vehicle" },

			// Status / actions
			{ "ENR",   "En Route" },
			{ "ADV",   "Advised" },
			{ "NEG",   "Negative" },
			{ "RPT",   "Report" },

			// Communications
			{ "PX",    "Phone Extension" },
			{ "etc",   "et cetera" },

			// Geographical
			{ "NH",    "Northbound" },
			{ "SH",    "Southbound" },
			{ "EH",    "Eastbound" },
			{ "WH",    "Westbound" },
		};

		// ---------------------------------------------------------------
		//  Address abbreviations (standalone words, only after a digit).
		// ---------------------------------------------------------------
		private static readonly Dictionary<string, string> AddressAbbreviationMap = new(StringComparer.OrdinalIgnoreCase)
		{
			{ "St",   "Street" },
			{ "Ave",  "Avenue" },
			{ "Blvd", "Boulevard" },
			{ "Apt",  "Apartment" },
			{ "Ste",  "Suite" },
			{ "Rd",   "Road" },
			{ "Dr",   "Drive" },
			{ "Ct",   "Court" },
			{ "Ln",   "Lane" },
			{ "Cir",  "Circle" },
			{ "Pl",   "Place" },
			{ "Pkwy", "Parkway" },
			{ "Hwy",  "Highway" },
			{ "Fwy",  "Freeway" },
			{ "Tpke", "Turnpike" },
			{ "Xing", "Crossing" },
		};

		// ---------------------------------------------------------------
		//  Slash-notation expansions commonly used in dispatch text.
		//  The engine reads "Y/O" as "Y slash O" — we want "year old".
		// ---------------------------------------------------------------
		private static readonly Dictionary<string, string> SlashNotationMap = new(StringComparer.OrdinalIgnoreCase)
		{
			{ "Y/O", "Year Old" },
			{ "W/",  "With" },
			{ "W/O", "Without" },
		};

		// ---------------------------------------------------------------
		//  10-codes — the engine reads "10-4" as "ten dash four", which is
		//  actually fine for most listeners.  We keep them as-is for now.
		//  Uncomment the map and the handler below if you prefer expansion.
		// ---------------------------------------------------------------
		// private static readonly Dictionary<string, string> TenCodeMap = new(StringComparer.Ordinal)
		// {
		// 	{ "10-4",  "acknowledged" },
		// 	{ "10-50", "traffic accident" },
		// 	...
		// };

		private static readonly Regex WhitespaceRegex = WhitespaceExpandoRegex();
		private static readonly Regex UnitIdentifierRegex = UnitIdentifierExpandoRegex();
		private static readonly Regex NumberToWordRegexField = NumberToWordRegex();

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

			// Only preprocess English voices — other languages need their
			// own abbreviation dictionaries.
			if (!IsEnglishVoice(voice))
			{
				return result;
			}

			var original = result;

			// Order matters: expand abbreviations first so downstream
			// passes operate on natural-language words rather than codes.
			result = ExpandAbbreviations(result);
			result = ExpandDispatchShorthand(result);
			result = ExpandSlashNotation(result);
			result = ExpandAddressAbbreviations(result);
			result = ExpandUnitIdentifiers(result);
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

			return result;
		}

		// ---------------------------------------------------------------
		//  Abbreviation expansion (word-boundary-aware)
		// ---------------------------------------------------------------

		private static string ExpandAbbreviations(string text)
		{
			// Sort keys longest-first so "ALSEMS" is matched before "ALS".
			foreach (var kvp in AbbreviationMap.OrderByDescending(k => k.Key.Length))
			{
				var pattern = $@"\b{Regex.Escape(kvp.Key)}\b";
				text = Regex.Replace(text, pattern, kvp.Value, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
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
			foreach (var kvp in DispatchShorthandMap.OrderByDescending(k => k.Key.Length))
			{
				var pattern = $@"\b{Regex.Escape(kvp.Key)}\b";
				text = Regex.Replace(text, pattern, kvp.Value, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
			}

			return text;
		}

		/// <summary>
		/// Converts slash-delimited abbreviations into spoken English so
		/// the engine doesn't say the word "slash" aloud.
		/// <br/>
		/// Example: "75 Y/O" → "75 Year Old" (instead of "75 Y slash O")
		/// </summary>
		private static string ExpandSlashNotation(string text)
		{
			// Sort longest-first so "W/O" is matched before "W/".
			foreach (var kvp in SlashNotationMap.OrderByDescending(k => k.Key.Length))
			{
				// Keys like "W/" and "Y/O" contain non-word characters that
				// defeat the standard \b anchor.  Use lookaround boundaries
				// instead: (?<!\w) ensures no word character precedes, and
				// (?!\w) ensures no word character follows.
				var pattern = $@"(?<!\w){Regex.Escape(kvp.Key)}(?!\w)";
				text = Regex.Replace(text, pattern, kvp.Value, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
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
			// Address abbreviations should only be expanded when they appear
			// after a house/building number (e.g. "123 Main St" → "123 Main Street").
			// The pattern anchors to a leading digit (\b\d+\b), then lazily skips
			// over the street name (one or more words) before matching the suffix.

			foreach (var kvp in AddressAbbreviationMap.OrderByDescending(k => k.Key.Length))
			{
				var pattern = $@"\b\d+\b[\s\w,]*?\b{Regex.Escape(kvp.Key)}\b";
				text = Regex.Replace(text, pattern, kvp.Value, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
			}

			return text;
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
		//  Source-generated regex helpers
		// ---------------------------------------------------------------

		/// <summary>Matches a single letter followed by digits, as a whole word.</summary>
		[GeneratedRegex(@"\b(?<prefix>[A-Z])(?<number>\d+)\b", RegexOptions.CultureInvariant)]
		private static partial Regex UnitIdentifierExpandoRegex();

		/// <summary>Matches standalone digits 1-20 followed by a space and a letter.</summary>
		[GeneratedRegex(@"\b(?<digits>(?:[1-9]|1[0-9]|20))\s(?<letter>[A-Za-z])", RegexOptions.CultureInvariant)]
		private static partial Regex NumberToWordRegex();

		/// <summary>Collapses multiple whitespace characters into a single space.</summary>
		[GeneratedRegex(@"\s+")]
		private static partial Regex WhitespaceExpandoRegex();
	}
}