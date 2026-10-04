using System;
using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model
{
	/// <summary>How strongly two addresses name the same place.</summary>
	public enum StreetAddressMatch
	{
		None = 0,
		/// <summary>Same house number and street, but one side leaves out a directional, or a locality differs, or the street name only agrees as a prefix.</summary>
		Similar = 1,
		/// <summary>Same house number, street name and directionals; a missing suffix or unit on one side does not matter.</summary>
		Same = 2
	}

	/// <summary>
	/// Decides whether two <see cref="ParsedStreetAddress"/> values are the same location. Parts present on both sides
	/// must agree; a part present on only one side ("110 South Main" against "110 S Main St") is allowed. Units never
	/// count, so every apartment in a building shares the building's history.
	/// </summary>
	public static class StreetAddressMatcher
	{
		public static StreetAddressMatch Compare(string a, string b) =>
			Compare(StreetAddressParser.Parse(a), StreetAddressParser.Parse(b));

		public static StreetAddressMatch Compare(ParsedStreetAddress a, ParsedStreetAddress b)
		{
			if (a == null || b == null || !a.HasStreetLocation || !b.HasStreetLocation)
				return StreetAddressMatch.None;

			if (a.PostalCode != null && b.PostalCode != null && !string.Equals(a.PostalCode, b.PostalCode, StringComparison.Ordinal))
				return StreetAddressMatch.None;

			var similar = LocalitiesDisagree(a.Locality, b.Locality);

			if (a.IsIntersection || b.IsIntersection)
			{
				if (!a.IsIntersection || !b.IsIntersection)
					return StreetAddressMatch.None;
				if (!string.Equals(a.StreetName, b.StreetName, StringComparison.Ordinal) || !string.Equals(a.CrossStreetName, b.CrossStreetName, StringComparison.Ordinal))
					return StreetAddressMatch.None;
				return similar ? StreetAddressMatch.Similar : StreetAddressMatch.Same;
			}

			if (!string.Equals(a.HouseNumber.Replace(" ", ""), b.HouseNumber.Replace(" ", ""), StringComparison.Ordinal))
				return StreetAddressMatch.None;

			var names = CompareNames(a, b);
			if (names == StreetAddressMatch.None)
				return StreetAddressMatch.None;
			if (names == StreetAddressMatch.Similar)
				similar = true;

			if (Conflicts(a.PreDirectional, b.PreDirectional, ref similar) || Conflicts(a.PostDirectional, b.PostDirectional, ref similar))
				return StreetAddressMatch.None;

			return similar ? StreetAddressMatch.Similar : StreetAddressMatch.Same;
		}

		private static StreetAddressMatch CompareNames(ParsedStreetAddress a, ParsedStreetAddress b)
		{
			var aName = a.StreetNameTokens;
			var bName = b.StreetNameTokens;

			if (aName.SequenceEqual(bName))
			{
				// Main St and Main Ave are different streets; Main and Main St are not.
				if (a.SuffixType != null && b.SuffixType != null && !string.Equals(a.SuffixType, b.SuffixType, StringComparison.Ordinal))
					return StreetAddressMatch.None;
				return StreetAddressMatch.Same;
			}

			// The parser read one side's last name word as its suffix ("Spring" + "Lake") and the other side's as name ("Spring Lake" + "Dr").
			if (a.StreetNameWithSuffixTokens.SequenceEqual(bName) || aName.SequenceEqual(b.StreetNameWithSuffixTokens))
				return StreetAddressMatch.Same;

			// "110 S Main St" against "110 South Main Springfield": with no suffix to stop at, the longer side's extra
			// words are most likely a locality typed without a comma.
			if (b.SuffixType == null && (IsPrefix(aName, bName) || IsPrefix(a.StreetNameWithSuffixTokens, bName)))
				return StreetAddressMatch.Similar;
			if (a.SuffixType == null && (IsPrefix(bName, aName) || IsPrefix(b.StreetNameWithSuffixTokens, aName)))
				return StreetAddressMatch.Similar;

			return StreetAddressMatch.None;
		}

		private static bool IsPrefix(IReadOnlyList<string> shorter, IReadOnlyList<string> longer)
		{
			if (shorter.Count == 0 || shorter.Count >= longer.Count)
				return false;
			for (var i = 0; i < shorter.Count; i++)
				if (!string.Equals(shorter[i], longer[i], StringComparison.Ordinal))
					return false;
			return true;
		}

		private static bool Conflicts(string a, string b, ref bool similar)
		{
			if (a != null && b != null)
				return !string.Equals(a, b, StringComparison.Ordinal);
			if (a != null || b != null)
				similar = true;
			return false;
		}

		private static bool LocalitiesDisagree(string a, string b)
		{
			if (a == null || b == null)
				return false;
			return !a.StartsWith(b, StringComparison.Ordinal) && !b.StartsWith(a, StringComparison.Ordinal);
		}
	}
}
