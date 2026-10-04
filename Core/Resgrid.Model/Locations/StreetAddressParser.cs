using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Resgrid.Model
{
	/// <summary>
	/// Splits a free-text address into a <see cref="ParsedStreetAddress"/>. Heuristic, not a postal standardizer: it
	/// only has to parse two spellings of the same place the same way. It handles "110 S Main St, Springfield, IL
	/// 62701, USA" (geocoder output), "110 South Main" (typed), a business name before the street, units, ordinal
	/// words, highway forms ("US Highway 50", "CR 12") and intersections ("Main &amp; 1st"). Pure and allocation-light
	/// so it can run over a department's whole call history in the backfill.
	/// </summary>
	public static class StreetAddressParser
	{
		private static readonly Regex HouseNumberPattern = new Regex(@"^\d{1,8}[A-Z]?$|^\d{1,6}-\d{1,6}[A-Z]?$", RegexOptions.Compiled);
		private static readonly Regex FractionPattern = new Regex(@"^\d/\d{1,2}$", RegexOptions.Compiled);
		private static readonly Regex RouteNumberPattern = new Regex(@"^(\d[0-9A-Z]{0,5}|[A-Z])$", RegexOptions.Compiled);
		private static readonly Regex HighwayTokenPattern = new Regex(@"^(CR|SR|SH|US|I|HWY|RTE|FM)(\d[0-9A-Z]{0,5}|[A-Z])$", RegexOptions.Compiled);
		private static readonly Regex UsPostalPattern = new Regex(@"^(\d{5})(-?\d{4})?$", RegexOptions.Compiled);
		private static readonly Regex CanadianPostalPattern = new Regex(@"^([A-Z]\d[A-Z])(\d[A-Z]\d)?$", RegexOptions.Compiled);
		private static readonly Regex CanadianPostalTail = new Regex(@"^\d[A-Z]\d$", RegexOptions.Compiled);

		private static readonly Dictionary<string, string> Directionals = new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["N"] = "N", ["NORTH"] = "N", ["S"] = "S", ["SOUTH"] = "S", ["E"] = "E", ["EAST"] = "E", ["W"] = "W", ["WEST"] = "W",
			["NE"] = "NE", ["NORTHEAST"] = "NE", ["NW"] = "NW", ["NORTHWEST"] = "NW", ["SE"] = "SE", ["SOUTHEAST"] = "SE",
			["SW"] = "SW", ["SOUTHWEST"] = "SW"
		};

		/// <summary>A directional that is the street's name ("North Ave") is spelled out so "N Ave" and "North Ave" agree.</summary>
		private static readonly Dictionary<string, string> DirectionalNames = new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["N"] = "NORTH", ["S"] = "SOUTH", ["E"] = "EAST", ["W"] = "WEST",
			["NE"] = "NORTHEAST", ["NW"] = "NORTHWEST", ["SE"] = "SOUTHEAST", ["SW"] = "SOUTHWEST"
		};

		private static readonly Dictionary<string, string> Suffixes = BuildSuffixes();

		/// <summary>Suffixes that almost never appear inside a street name, preferred when several suffix words run together.</summary>
		private static readonly HashSet<string> StrongSuffixes = new HashSet<string>(StringComparer.Ordinal)
		{
			"ST", "AVE", "RD", "DR", "BLVD", "LN", "CT", "WAY", "PL", "CIR", "PKWY", "TER", "TRL", "HWY", "CRES", "EXPY", "FWY", "TPKE", "SQ", "PLZ"
		};

		private static readonly Dictionary<string, string> UnitDesignators = new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["#"] = "#", ["APT"] = "APT", ["APARTMENT"] = "APT", ["UNIT"] = "UNIT", ["STE"] = "STE", ["SUITE"] = "STE",
			["RM"] = "RM", ["ROOM"] = "RM", ["FL"] = "FL", ["FLR"] = "FL", ["FLOOR"] = "FL", ["BLDG"] = "BLDG",
			["BUILDING"] = "BLDG", ["LOT"] = "LOT", ["SPC"] = "SPC", ["SPACE"] = "SPC", ["TRLR"] = "TRLR", ["TRAILER"] = "TRLR",
			["DEPT"] = "DEPT", ["OFC"] = "OFC", ["OFFICE"] = "OFC", ["HNGR"] = "HNGR", ["HANGAR"] = "HNGR", ["PIER"] = "PIER",
			["SLIP"] = "SLIP"
		};

		private static readonly Dictionary<string, string> StandaloneUnits = new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["REAR"] = "REAR", ["FRONT"] = "FRNT", ["FRNT"] = "FRNT", ["UPPER"] = "UPPR", ["UPPR"] = "UPPR", ["LOWER"] = "LOWR",
			["LOWR"] = "LOWR", ["BSMT"] = "BSMT", ["BASEMENT"] = "BSMT", ["PH"] = "PH", ["PENTHOUSE"] = "PH", ["LOBBY"] = "LBBY",
			["LBBY"] = "LBBY"
		};

		private static readonly Dictionary<string, string> Ordinals = new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["FIRST"] = "1ST", ["SECOND"] = "2ND", ["THIRD"] = "3RD", ["FOURTH"] = "4TH", ["FIFTH"] = "5TH", ["SIXTH"] = "6TH",
			["SEVENTH"] = "7TH", ["EIGHTH"] = "8TH", ["NINTH"] = "9TH", ["TENTH"] = "10TH", ["ELEVENTH"] = "11TH",
			["TWELFTH"] = "12TH", ["THIRTEENTH"] = "13TH", ["FOURTEENTH"] = "14TH", ["FIFTEENTH"] = "15TH",
			["SIXTEENTH"] = "16TH", ["SEVENTEENTH"] = "17TH", ["EIGHTEENTH"] = "18TH", ["NINETEENTH"] = "19TH",
			["TWENTIETH"] = "20TH"
		};

		/// <summary>Leading name words with a standard short form ("Saint Louis Ave" = "St Louis Ave").</summary>
		private static readonly Dictionary<string, string> LeadingNameWords = new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["SAINT"] = "ST", ["SAINTE"] = "STE", ["MOUNT"] = "MT", ["FORT"] = "FT"
		};

		private static readonly HashSet<string> Regions = new HashSet<string>(StringComparer.Ordinal)
		{
			"AL", "AK", "AZ", "AR", "CA", "CO", "CT", "DE", "FL", "GA", "HI", "ID", "IL", "IN", "IA", "KS", "KY", "LA", "ME", "MD",
			"MA", "MI", "MN", "MS", "MO", "MT", "NE", "NV", "NH", "NJ", "NM", "NY", "NC", "ND", "OH", "OK", "OR", "PA", "RI", "SC",
			"SD", "TN", "TX", "UT", "VT", "VA", "WA", "WV", "WI", "WY", "DC", "PR", "VI", "GU",
			"AB", "BC", "MB", "NB", "NL", "NS", "NT", "NU", "ON", "PE", "QC", "SK", "YT"
		};

		private static readonly HashSet<string> Countries = new HashSet<string>(StringComparer.Ordinal) { "USA", "US", "UNITED STATES", "CANADA" };

		private static readonly HashSet<string> IntersectionSeparators = new HashSet<string>(StringComparer.Ordinal) { "&", "AND", "@", "AT", "/" };

		/// <summary>Words that make the number after them a route number ("Hwy 50"), not a house number.</summary>
		private static readonly HashSet<string> RoutePrefixes = new HashSet<string>(StringComparer.Ordinal)
		{
			"HWY", "HIGHWAY", "ROUTE", "RTE", "RT", "US", "CR", "SR", "SH", "I", "INTERSTATE", "FM", "ROAD", "RD", "COUNTY"
		};

		/// <summary>
		/// Parses <paramref name="address"/>. <paramref name="locality"/> and <paramref name="postalCode"/> are for
		/// sources that keep them in their own fields (occupancies, contact addresses); they win over anything found
		/// in the text. Returns null when there is nothing to parse.
		/// </summary>
		public static ParsedStreetAddress Parse(string address, string locality = null, string postalCode = null)
		{
			if (string.IsNullOrWhiteSpace(address))
				return null;

			var segments = Fold(address).Split(',').Select(s => Tokenize(s)).Where(s => s.Count > 0).ToList();
			if (segments.Count == 0)
				return null;

			var streetIndex = PickStreetSegment(segments);
			var result = new ParsedStreetAddress();
			var tail = new List<List<string>>();

			var street = segments[streetIndex];
			if (!IsHouseNumber(street[0]))
			{
				// "Joe's Diner 110 Main St" with no comma: start at the first house number that has a street after it.
				for (var i = 1; i < street.Count - 1; i++)
				{
					if (IsHouseNumber(street[i]) && !IsHouseNumber(street[i + 1]) && !IntersectionSeparators.Contains(street[i + 1]) &&
						!RoutePrefixes.Contains(street[i - 1]))
					{
						street = street.Skip(i).ToList();
						break;
					}
				}
			}

			if (IsHouseNumber(street[0]))
			{
				var index = 1;
				var number = street[0];
				if (index < street.Count && FractionPattern.IsMatch(street[index]))
					number += " " + street[index++];
				if (index < street.Count && (street[index] == "BLOCK" || street[index] == "BLK"))
				{
					index++;
					if (index < street.Count && street[index] == "OF")
						index++;
				}

				result.HouseNumber = number;
				var parts = ParseStreet(street.Skip(index).ToList());
				Apply(result, parts);
				tail.Add(parts.Tail);
			}
			else if (!TryParseIntersection(street, result))
			{
				var parts = ParseStreet(street);
				Apply(result, parts);
				tail.Add(parts.Tail);
			}

			tail.AddRange(segments.Skip(streetIndex + 1));
			foreach (var segment in tail)
				ReadLocality(segment, result);

			if (!string.IsNullOrWhiteSpace(locality))
				result.Locality = StandardizeLocality(Tokenize(Fold(locality))) ?? result.Locality;
			if (!string.IsNullOrWhiteSpace(postalCode))
			{
				var tokens = Tokenize(Fold(postalCode));
				result.PostalCode = ReadPostal(tokens, new HashSet<int>()) ?? result.PostalCode;
			}

			return result;
		}

		private sealed class StreetParts
		{
			public string PreDirectional;
			public List<string> Name = new List<string>();
			public string Suffix;
			public string PostDirectional;
			public string Unit;
			public List<string> Tail = new List<string>();
		}

		private static void Apply(ParsedStreetAddress result, StreetParts parts)
		{
			result.PreDirectional = parts.PreDirectional;
			result.StreetName = parts.Name.Count == 0 ? null : string.Join(" ", parts.Name);
			result.SuffixType = parts.Suffix;
			result.PostDirectional = parts.PostDirectional;
			result.Unit = parts.Unit;
		}

		private static StreetParts ParseStreet(List<string> input)
		{
			var parts = new StreetParts();
			var t = FoldHighways(input);
			if (t.Count == 0)
				return parts;

			var start = 0;
			if (t.Count >= 2 && Directionals.TryGetValue(t[0], out var pre) && !(t.Count == 2 && IsSuffix(t[1])) && !IsUnit(t, 1))
			{
				parts.PreDirectional = pre;
				start = 1;
			}

			int index;
			if (HighwayTokenPattern.IsMatch(t[start]))
			{
				parts.Name.Add(t[start]);
				index = start + 1;
			}
			else
			{
				var suffixIndex = -1;
				for (var i = start + 1; i < t.Count; i++)
				{
					if (IsUnit(t, i))
						break;
					if (!IsSuffix(t[i]))
						continue;

					// Several suffix words in a row ("Spring Lake Dr", "Main St Park Ridge"): the strong one is the suffix.
					int last = -1, lastStrong = -1;
					for (var j = i; j < t.Count && IsSuffix(t[j]) && !IsUnit(t, j); j++)
					{
						last = j;
						if (StrongSuffixes.Contains(Suffixes[t[j]]))
							lastStrong = j;
					}

					suffixIndex = lastStrong >= 0 ? lastStrong : last;
					break;
				}

				if (suffixIndex >= 0)
				{
					parts.Name.AddRange(t.Skip(start).Take(suffixIndex - start));
					parts.Suffix = Suffixes[t[suffixIndex]];
					index = suffixIndex + 1;
				}
				else
				{
					var end = start + 1;
					while (end < t.Count && !IsUnit(t, end))
						end++;

					var name = t.Skip(start).Take(end - start).ToList();
					// No suffix to stop at: a trailing postal code, region or country typed without commas is not the name.
					while (name.Count > 1 && (UsPostalPattern.IsMatch(name[^1]) || CanadianPostalTail.IsMatch(name[^1]) || Regions.Contains(name[^1]) || Countries.Contains(name[^1])))
					{
						parts.Tail.Insert(0, name[^1]);
						name.RemoveAt(name.Count - 1);
					}

					if (name.Count >= 2 && Directionals.TryGetValue(name[^1], out var trailing))
					{
						parts.PostDirectional = trailing;
						name.RemoveAt(name.Count - 1);
					}

					parts.Name.AddRange(name);
					index = end;
				}
			}

			if (parts.PostDirectional == null && index < t.Count && Directionals.TryGetValue(t[index], out var post) && !IsUnit(t, index))
			{
				parts.PostDirectional = post;
				index++;
			}

			if (index < t.Count && IsUnit(t, index))
			{
				parts.Unit = ReadUnit(t, ref index);
			}

			parts.Tail.AddRange(t.Skip(index));
			parts.Name = StandardizeName(parts.Name);
			return parts;
		}

		private static bool TryParseIntersection(List<string> tokens, ParsedStreetAddress result)
		{
			for (var k = 1; k < tokens.Count - 1; k++)
			{
				if (!IntersectionSeparators.Contains(tokens[k]))
					continue;

				var left = ParseStreet(tokens.Take(k).ToList());
				var right = ParseStreet(tokens.Skip(k + 1).ToList());
				if (left.Name.Count == 0 || right.Name.Count == 0)
					return false;

				var names = new[] { string.Join(" ", left.Name), string.Join(" ", right.Name) }.OrderBy(x => x, StringComparer.Ordinal).ToArray();
				if (names[0] == names[1])
					return false;

				result.StreetName = names[0];
				result.CrossStreetName = names[1];
				ReadLocality(right.Tail, result);
				return true;
			}

			return false;
		}

		private static List<string> StandardizeName(List<string> name)
		{
			if (name.Count == 0)
				return name;

			if (name.Count == 1 && DirectionalNames.TryGetValue(Directionals.TryGetValue(name[0], out var d) ? d : name[0], out var spelled))
				return new List<string> { spelled };

			var result = new List<string>(name.Count);
			for (var i = 0; i < name.Count; i++)
			{
				var token = name[i];
				if (Ordinals.TryGetValue(token, out var ordinal))
					token = ordinal;
				else if (i == 0 && name.Count > 1 && LeadingNameWords.TryGetValue(token, out var shortForm))
					token = shortForm;
				else if (Suffixes.TryGetValue(token, out var standard))
					// "Spring Lake" as a name and "Spring" + "Lake" as name + suffix must spell LAKE the same way.
					token = standard;
				result.Add(token);
			}

			return result;
		}

		/// <summary>Folds multi-word highway designators into one token: "US Highway 50" and "US-50" both become "US50".</summary>
		private static List<string> FoldHighways(List<string> t)
		{
			var result = new List<string>(t.Count);
			for (var i = 0; i < t.Count; i++)
			{
				var (designator, consumed) = MatchHighway(t, i);
				if (designator != null && i + consumed < t.Count && RouteNumberPattern.IsMatch(t[i + consumed]) &&
					(designator != "I" || char.IsDigit(t[i + consumed][0])))
				{
					result.Add(designator + t[i + consumed]);
					i += consumed;
					continue;
				}

				result.Add(t[i]);
			}

			return result;
		}

		private static (string designator, int consumed) MatchHighway(List<string> t, int i)
		{
			string At(int offset) => i + offset < t.Count ? t[i + offset] : null;
			bool Is(int offset, params string[] values) => At(offset) != null && values.Contains(At(offset));

			if (Is(0, "COUNTY") && Is(1, "ROAD", "RD", "ROUTE", "RTE", "RT", "HIGHWAY", "HWY")) return ("CR", 2);
			if (Is(0, "CO") && Is(1, "ROAD", "RD")) return ("CR", 2);
			if (Is(0, "CR", "CORD")) return ("CR", 1);
			if (Is(0, "STATE") && Is(1, "ROUTE", "RTE", "RT", "ROAD", "RD")) return ("SR", 2);
			if (Is(0, "ST") && Is(1, "ROUTE", "RTE", "RT")) return ("SR", 2);
			if (Is(0, "SR")) return ("SR", 1);
			if (Is(0, "STATE") && Is(1, "HIGHWAY", "HWY")) return ("SH", 2);
			if (Is(0, "SH")) return ("SH", 1);
			if (Is(0, "US") && Is(1, "HIGHWAY", "HWY", "ROUTE", "RTE", "RT")) return ("US", 2);
			if (Is(0, "U") && Is(1, "S") && Is(2, "HIGHWAY", "HWY", "ROUTE", "RTE", "RT")) return ("US", 3);
			if (Is(0, "US")) return ("US", 1);
			if (Is(0, "INTERSTATE", "I")) return ("I", 1);
			if (Is(0, "HIGHWAY", "HWY", "HWAY", "HIWAY", "HIWY", "HIGHWY")) return ("HWY", 1);
			if (Is(0, "ROUTE", "RTE", "RT")) return ("RTE", 1);
			if (Is(0, "FARM") && Is(1, "TO") && Is(2, "MARKET") && Is(3, "ROAD", "RD")) return ("FM", 4);
			if (Is(0, "FARM") && Is(1, "TO") && Is(2, "MARKET")) return ("FM", 3);
			if (Is(0, "FM")) return ("FM", 1);
			return (null, 0);
		}

		private static bool IsSuffix(string token) => Suffixes.ContainsKey(token);

		private static bool IsUnit(List<string> t, int i)
		{
			if (i >= t.Count)
				return false;

			var token = t[i];
			if (StandaloneUnits.ContainsKey(token))
				return true;
			if (!UnitDesignators.ContainsKey(token))
				return false;
			if (token == "#")
				return true;

			var next = i + 1 < t.Count ? t[i + 1] : null;
			if (next == null)
				return false;
			// "FL 33101" is Florida and a ZIP code, not floor 33101.
			return !(token == "FL" && UsPostalPattern.IsMatch(next));
		}

		private static string ReadUnit(List<string> t, ref int index)
		{
			var token = t[index++];
			if (StandaloneUnits.TryGetValue(token, out var standalone))
				return standalone;

			var designator = UnitDesignators[token];
			if (index < t.Count && t[index] == "#")
				index++;
			if (index < t.Count)
				return designator + " " + t[index++];
			return designator;
		}

		private static void ReadLocality(List<string> segment, ParsedStreetAddress result)
		{
			if (segment == null || segment.Count == 0)
				return;

			if (result.Unit == null && IsUnit(segment, 0))
			{
				var index = 0;
				result.Unit = ReadUnit(segment, ref index);
				return;
			}

			var used = new HashSet<int>();
			var postal = ReadPostal(segment, used);
			if (postal != null && result.PostalCode == null)
				result.PostalCode = postal;

			var words = segment.Where((_, i) => !used.Contains(i)).ToList();
			var joined = string.Join(" ", words);
			if (Countries.Contains(joined))
				return;
			while (words.Count > 0 && (Regions.Contains(words[^1]) || Countries.Contains(words[^1])))
				words.RemoveAt(words.Count - 1);

			if (result.Locality == null)
				result.Locality = StandardizeLocality(words);
		}

		private static string ReadPostal(List<string> tokens, HashSet<int> used)
		{
			for (var i = 0; i < tokens.Count; i++)
			{
				var us = UsPostalPattern.Match(tokens[i]);
				if (us.Success)
				{
					used.Add(i);
					return us.Groups[1].Value;
				}

				var ca = CanadianPostalPattern.Match(tokens[i]);
				if (ca.Success)
				{
					if (ca.Groups[2].Success)
					{
						used.Add(i);
						return tokens[i];
					}

					if (i + 1 < tokens.Count && CanadianPostalTail.IsMatch(tokens[i + 1]))
					{
						used.Add(i);
						used.Add(i + 1);
						return tokens[i] + tokens[i + 1];
					}
				}
			}

			return null;
		}

		private static string StandardizeLocality(List<string> words)
		{
			if (words == null || words.Count == 0)
				return null;

			var result = words.ToList();
			if (result.Count > 1 && LeadingNameWords.TryGetValue(result[0], out var shortForm))
				result[0] = shortForm;
			return string.Join(" ", result);
		}

		private static int PickStreetSegment(List<List<string>> segments)
		{
			int best = 0, bestScore = -1;
			for (var i = 0; i < segments.Count; i++)
			{
				var s = segments[i];
				var score = 0;
				if (IsHouseNumber(s[0]))
					score = s.Skip(1).Any(IsSuffix) || s.Skip(1).Any(x => HighwayTokenPattern.IsMatch(x)) || MatchHighway(s, 1).designator != null ? 3 : 2;
				else if (s.Skip(1).Take(s.Count - 2).Any(IntersectionSeparators.Contains))
					score = 1;
				else if (s.Any(IsHouseNumber))
					score = 1;

				if (score > bestScore)
				{
					best = i;
					bestScore = score;
				}
			}

			return best;
		}

		private static bool IsHouseNumber(string token) => token != null && HouseNumberPattern.IsMatch(token);

		/// <summary>Upper case, accents stripped, apostrophes removed, separators spaced; commas, semicolons and line breaks split segments.</summary>
		private static string Fold(string value)
		{
			var decomposed = value.Normalize(NormalizationForm.FormD);
			var sb = new StringBuilder(decomposed.Length + 8);
			foreach (var raw in decomposed)
			{
				if (CharUnicodeInfo.GetUnicodeCategory(raw) == UnicodeCategory.NonSpacingMark)
					continue;

				var c = char.ToUpperInvariant(raw);
				switch (c)
				{
					case '\'':
					case '’':
					case '`':
						continue;
					case '&':
					case '@':
					case '#':
						sb.Append(' ').Append(c).Append(' ');
						continue;
					case ',':
					case ';':
					case '\n':
					case '\r':
						sb.Append(',');
						continue;
				}

				if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '/')
					sb.Append(c);
				else
					sb.Append(' ');
			}

			return sb.ToString();
		}

		private static List<string> Tokenize(string segment)
		{
			var result = new List<string>();
			foreach (var raw in segment.Split(' ', StringSplitOptions.RemoveEmptyEntries))
			{
				// A hyphenated house number ("123-45") only means something at the start of the segment.
				if (result.Count == 0 && HouseNumberPattern.IsMatch(raw))
				{
					result.Add(raw);
					continue;
				}

				if (FractionPattern.IsMatch(raw) || UsPostalPattern.IsMatch(raw))
				{
					result.Add(raw);
					continue;
				}

				var slashParts = raw.Split('/');
				for (var s = 0; s < slashParts.Length; s++)
				{
					if (s > 0)
						result.Add("/");
					foreach (var piece in slashParts[s].Split('-', StringSplitOptions.RemoveEmptyEntries))
						result.Add(piece);
				}
			}

			// Drop separators left dangling at either end ("/ Main St", "Main St &").
			while (result.Count > 0 && (result[0] == "/" || result[0] == "&" || result[0] == "@"))
				result.RemoveAt(0);
			while (result.Count > 0 && (result[^1] == "/" || result[^1] == "&" || result[^1] == "@"))
				result.RemoveAt(result.Count - 1);
			return result;
		}

		private static Dictionary<string, string> BuildSuffixes()
		{
			var table = new (string standard, string[] variants)[]
			{
				("ALY", new[] { "ALLEY", "ALLEE", "ALLY", "ALY" }), ("ANX", new[] { "ANNEX", "ANEX", "ANNX", "ANX" }),
				("ARC", new[] { "ARCADE", "ARC" }), ("AVE", new[] { "AVENUE", "AV", "AVE", "AVEN", "AVENU", "AVN", "AVNUE" }),
				("BYU", new[] { "BAYOU", "BAYOO", "BYU" }), ("BCH", new[] { "BEACH", "BCH" }), ("BND", new[] { "BEND", "BND" }),
				("BLF", new[] { "BLUFF", "BLUF", "BLF" }), ("BTM", new[] { "BOTTOM", "BOTTM", "BTM" }),
				("BLVD", new[] { "BOULEVARD", "BOUL", "BOULV", "BLVD" }), ("BR", new[] { "BRANCH", "BRNCH", "BR" }),
				("BRG", new[] { "BRIDGE", "BRDGE", "BRG" }), ("BRK", new[] { "BROOK", "BRK" }),
				("BYP", new[] { "BYPASS", "BYPA", "BYPAS", "BYPS", "BYP" }), ("CYN", new[] { "CANYON", "CANYN", "CNYN", "CYN" }),
				("CPE", new[] { "CAPE", "CPE" }), ("CSWY", new[] { "CAUSEWAY", "CAUSWA", "CSWY" }),
				("CTR", new[] { "CENTER", "CENTRE", "CENT", "CENTR", "CNTER", "CNTR", "CTR" }),
				("CIR", new[] { "CIRCLE", "CIRC", "CIRCL", "CRCL", "CRCLE", "CIR" }), ("CLF", new[] { "CLIFF", "CLF" }),
				("CLB", new[] { "CLUB", "CLB" }), ("CMN", new[] { "COMMON", "CMN" }), ("COR", new[] { "CORNER", "COR" }),
				("CRSE", new[] { "COURSE", "CRSE" }), ("CT", new[] { "COURT", "CRT", "CT" }), ("CV", new[] { "COVE", "CV" }),
				("CRK", new[] { "CREEK", "CRK" }), ("CRES", new[] { "CRESCENT", "CRSENT", "CRSNT", "CRES" }),
				("CRST", new[] { "CREST", "CRST" }), ("XING", new[] { "CROSSING", "CRSSNG", "XING" }), ("CURV", new[] { "CURVE", "CURV" }),
				("DL", new[] { "DALE", "DL" }), ("DM", new[] { "DAM", "DM" }), ("DV", new[] { "DIVIDE", "DVD", "DV" }),
				("DR", new[] { "DRIVE", "DRIV", "DRV", "DR" }), ("EST", new[] { "ESTATE", "EST" }), ("ESTS", new[] { "ESTATES", "ESTS" }),
				("EXPY", new[] { "EXPRESSWAY", "EXPR", "EXPW", "EXPY" }), ("EXT", new[] { "EXTENSION", "EXTN", "EXTNSN", "EXT" }),
				("FLS", new[] { "FALLS", "FLS" }), ("FRY", new[] { "FERRY", "FRRY", "FRY" }), ("FLD", new[] { "FIELD", "FLD" }),
				("FLDS", new[] { "FIELDS", "FLDS" }), ("FLT", new[] { "FLAT", "FLT" }), ("FRD", new[] { "FORD", "FRD" }),
				("FRST", new[] { "FOREST", "FORESTS", "FRST" }), ("FRG", new[] { "FORGE", "FORG", "FRG" }), ("FRK", new[] { "FORK", "FRK" }),
				("FWY", new[] { "FREEWAY", "FREEWY", "FRWAY", "FRWY", "FWY" }), ("GDN", new[] { "GARDEN", "GARDN", "GRDEN", "GRDN", "GDN" }),
				("GDNS", new[] { "GARDENS", "GDNS" }), ("GTWY", new[] { "GATEWAY", "GATEWY", "GATWAY", "GTWAY", "GTWY" }),
				("GLN", new[] { "GLEN", "GLN" }), ("GRN", new[] { "GREEN", "GRN" }), ("GRV", new[] { "GROVE", "GROV", "GRV" }),
				("HBR", new[] { "HARBOR", "HARB", "HARBR", "HRBOR", "HBR" }), ("HVN", new[] { "HAVEN", "HVN" }),
				("HTS", new[] { "HEIGHTS", "HTS" }), ("HWY", new[] { "HIGHWAY", "HIGHWY", "HIWAY", "HIWY", "HWAY", "HWY" }),
				("HL", new[] { "HILL", "HL" }), ("HLS", new[] { "HILLS", "HLS" }), ("HOLW", new[] { "HOLLOW", "HLLW", "HOLLOWS", "HOLWS", "HOLW" }),
				("IS", new[] { "ISLAND", "ISLND", "IS" }), ("JCT", new[] { "JUNCTION", "JCTION", "JCTN", "JUNCTN", "JUNCTON", "JCT" }),
				("KY", new[] { "KEY", "KY" }), ("KNL", new[] { "KNOLL", "KNOL", "KNL" }), ("LK", new[] { "LAKE", "LK" }),
				("LKS", new[] { "LAKES", "LKS" }), ("LNDG", new[] { "LANDING", "LNDNG", "LNDG" }), ("LN", new[] { "LANE", "LN" }),
				("LOOP", new[] { "LOOP", "LOOPS" }), ("MALL", new[] { "MALL" }), ("MNR", new[] { "MANOR", "MNR" }),
				("MDW", new[] { "MEADOW", "MDW" }), ("MDWS", new[] { "MEADOWS", "MEDOWS", "MDWS" }), ("ML", new[] { "MILL", "ML" }),
				("MTWY", new[] { "MOTORWAY", "MTWY" }), ("MTN", new[] { "MOUNTAIN", "MNTAIN", "MNTN", "MOUNTIN", "MTIN", "MTN" }),
				("OVAL", new[] { "OVAL", "OVL" }), ("OPAS", new[] { "OVERPASS", "OPAS" }), ("PARK", new[] { "PARK", "PRK", "PARKS" }),
				("PKWY", new[] { "PARKWAY", "PARKWY", "PKWAY", "PKY", "PKWY", "PARKWAYS", "PKWYS" }), ("PASS", new[] { "PASS" }),
				("PATH", new[] { "PATH", "PATHS" }), ("PIKE", new[] { "PIKE", "PIKES" }), ("PNES", new[] { "PINES", "PNES" }),
				("PL", new[] { "PLACE", "PL" }), ("PLN", new[] { "PLAIN", "PLN" }), ("PLNS", new[] { "PLAINS", "PLNS" }),
				("PLZ", new[] { "PLAZA", "PLZA", "PLZ" }), ("PT", new[] { "POINT", "PT" }), ("PRT", new[] { "PORT", "PRT" }),
				("PR", new[] { "PRAIRIE", "PRR", "PR" }), ("RADL", new[] { "RADIAL", "RADIEL", "RADL" }),
				("RNCH", new[] { "RANCH", "RANCHES", "RNCHS", "RNCH" }), ("RDG", new[] { "RIDGE", "RDGE", "RDG" }),
				("RIV", new[] { "RIVER", "RVR", "RIVR", "RIV" }), ("RD", new[] { "ROAD", "RD" }), ("RDS", new[] { "ROADS", "RDS" }),
				("RTE", new[] { "ROUTE", "RTE" }), ("ROW", new[] { "ROW" }), ("RUN", new[] { "RUN" }),
				("SHR", new[] { "SHORE", "SHOAR", "SHR" }), ("SHRS", new[] { "SHORES", "SHRS" }), ("SKWY", new[] { "SKYWAY", "SKWY" }),
				("SPG", new[] { "SPRING", "SPNG", "SPRNG", "SPG" }), ("SPGS", new[] { "SPRINGS", "SPGS" }),
				("SQ", new[] { "SQUARE", "SQR", "SQRE", "SQU", "SQ" }), ("STA", new[] { "STATION", "STATN", "STN", "STA" }),
				("ST", new[] { "STREET", "STRT", "STR", "ST" }), ("SMT", new[] { "SUMMIT", "SUMIT", "SUMITT", "SMT" }),
				("TER", new[] { "TERRACE", "TERR", "TER" }), ("TRCE", new[] { "TRACE", "TRACES", "TRCE" }),
				("TRAK", new[] { "TRACK", "TRACKS", "TRK", "TRKS", "TRAK" }), ("TRL", new[] { "TRAIL", "TRAILS", "TRLS", "TRL" }),
				("TUNL", new[] { "TUNNEL", "TUNEL", "TUNLS", "TUNNELS", "TUNNL", "TUNL" }), ("TPKE", new[] { "TURNPIKE", "TRNPK", "TURNPK", "TPKE" }),
				("VLY", new[] { "VALLEY", "VALLY", "VLLY", "VLY" }), ("VIA", new[] { "VIADUCT", "VDCT", "VIADCT", "VIA" }),
				("VW", new[] { "VIEW", "VW" }), ("VLG", new[] { "VILLAGE", "VILL", "VILLAG", "VILLG", "VLG" }), ("VL", new[] { "VILLE", "VL" }),
				("VIS", new[] { "VISTA", "VIST", "VST", "VSTA", "VIS" }), ("WALK", new[] { "WALK", "WALKS" }), ("WAY", new[] { "WAY", "WY" }),
				("WLS", new[] { "WELLS", "WLS" })
			};

			var map = new Dictionary<string, string>(StringComparer.Ordinal);
			foreach (var (standard, variants) in table)
				foreach (var variant in variants)
					map[variant] = standard;
			return map;
		}
	}
}
