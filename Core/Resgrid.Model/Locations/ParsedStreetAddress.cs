using System;
using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model
{
	/// <summary>
	/// A free-text street address split into the parts that decide whether two spellings name the same place
	/// ("110 S Main St", "110 South Main", "110 South Main Street"). Produced by <see cref="StreetAddressParser"/>,
	/// compared by <see cref="StreetAddressMatcher"/>. Every part is upper case and standardized (USPS Publication 28
	/// directionals and suffixes, ordinal words as numbers, highway designators folded to one token).
	/// </summary>
	public sealed class ParsedStreetAddress
	{
		/// <summary>Bumped whenever parsing or <see cref="IndexKey"/> changes, so stored keys can be rebuilt.</summary>
		public const int Version = 1;

		private const char Separator = '|';

		public string HouseNumber { get; set; }
		public string PreDirectional { get; set; }
		/// <summary>Street name tokens, space separated, without directionals or suffix.</summary>
		public string StreetName { get; set; }
		public string SuffixType { get; set; }
		public string PostDirectional { get; set; }
		/// <summary>Apartment, suite, floor and the like. Never part of a location match: a unit is in the same building.</summary>
		public string Unit { get; set; }
		/// <summary>For an intersection ("Main &amp; 1st"), the second street name; <see cref="StreetName"/> holds the first in sorted order.</summary>
		public string CrossStreetName { get; set; }
		public string Locality { get; set; }
		public string PostalCode { get; set; }

		public bool IsIntersection => !string.IsNullOrEmpty(CrossStreetName);

		/// <summary>True when the address names a specific point on a street: a numbered address or an intersection.</summary>
		public bool HasStreetLocation => !string.IsNullOrEmpty(StreetName) && (!string.IsNullOrEmpty(HouseNumber) || IsIntersection);

		/// <summary>
		/// The coarse lookup key stored per call: house number plus the first street-name token ("110|MAIN"), or both
		/// cross streets' first tokens ("X|1ST|MAIN"). Deliberately loose (it ignores directionals and suffixes) so a
		/// missing "St" or "S" still finds the row; <see cref="StreetAddressMatcher.Compare"/> makes the real decision.
		/// </summary>
		public string IndexKey
		{
			get
			{
				if (!HasStreetLocation)
					return null;

				if (IsIntersection)
				{
					var pair = new[] { FirstToken(StreetName), FirstToken(CrossStreetName) }.OrderBy(x => x, StringComparer.Ordinal);
					return Cap("X" + Separator + string.Join(Separator.ToString(), pair), 120);
				}

				return Cap(HouseNumber.Replace(" ", "") + Separator + FirstToken(StreetName), 120);
			}
		}

		public IReadOnlyList<string> StreetNameTokens => Tokens(StreetName);

		/// <summary>Name tokens followed by the suffix, so "SPRING" + "LAKE" can equal a "SPRING LAKE" name with a "DR" suffix.</summary>
		public IReadOnlyList<string> StreetNameWithSuffixTokens
		{
			get
			{
				var tokens = Tokens(StreetName).ToList();
				if (!string.IsNullOrEmpty(SuffixType))
					tokens.Add(SuffixType);
				return tokens;
			}
		}

		/// <summary>Round-trippable storage form; see <see cref="FromCanonical"/>.</summary>
		public string ToCanonical() =>
			string.Join(Separator.ToString(), Version.ToString(), HouseNumber, PreDirectional, StreetName, SuffixType, PostDirectional,
				Unit, CrossStreetName, Locality, PostalCode);

		/// <summary>Reads a value written by <see cref="ToCanonical"/>; null for an empty value or a different version.</summary>
		public static ParsedStreetAddress FromCanonical(string canonical)
		{
			if (string.IsNullOrWhiteSpace(canonical))
				return null;

			var parts = canonical.Split(Separator);
			if (parts.Length != 10 || parts[0] != Version.ToString())
				return null;

			return new ParsedStreetAddress
			{
				HouseNumber = NullIfEmpty(parts[1]),
				PreDirectional = NullIfEmpty(parts[2]),
				StreetName = NullIfEmpty(parts[3]),
				SuffixType = NullIfEmpty(parts[4]),
				PostDirectional = NullIfEmpty(parts[5]),
				Unit = NullIfEmpty(parts[6]),
				CrossStreetName = NullIfEmpty(parts[7]),
				Locality = NullIfEmpty(parts[8]),
				PostalCode = NullIfEmpty(parts[9])
			};
		}

		/// <summary>A readable single-line form, mainly for diagnostics and tests.</summary>
		public override string ToString()
		{
			if (IsIntersection)
				return $"{StreetName} & {CrossStreetName}";

			return string.Join(" ", new[] { HouseNumber, PreDirectional, StreetName, SuffixType, PostDirectional }.Where(x => !string.IsNullOrEmpty(x)));
		}

		private static IReadOnlyList<string> Tokens(string value) =>
			string.IsNullOrEmpty(value) ? Array.Empty<string>() : value.Split(' ', StringSplitOptions.RemoveEmptyEntries);

		private static string FirstToken(string value) => Tokens(value).FirstOrDefault() ?? string.Empty;

		private static string NullIfEmpty(string value) => string.IsNullOrEmpty(value) ? null : value;

		private static string Cap(string value, int length) => value.Length <= length ? value : value.Substring(0, length);
	}
}
