using System.Collections.Generic;

namespace Resgrid.Framework
{
	/// <summary>
	/// Maps a country display name (as used in <c>Resgrid.Model.Countries.CountryNames</c>) to its ISO-3166 alpha-2
	/// region code, which the phone-number validator uses as the default region when parsing a national-format number
	/// (e.g. a South-African "082446..." -> region "za" -> "+2782446..."). Unmapped countries return null, in which
	/// case the validator falls back to its own default — callers should treat null as "no region hint".
	/// Extend the map as new markets are onboarded.
	/// </summary>
	public static class PhoneRegionHelper
	{
		private static readonly Dictionary<string, string> NameToIso = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
		{
			{ "United States", "us" },
			{ "Canada", "ca" },
			{ "United Kingdom", "gb" },
			{ "Australia", "au" },
			{ "New Zealand", "nz" },
			{ "Ireland", "ie" },
			{ "South Africa", "za" },
			{ "Namibia", "na" },
			{ "Botswana", "bw" },
			{ "Zimbabwe", "zw" },
			{ "Kenya", "ke" },
			{ "Nigeria", "ng" },
			{ "Ghana", "gh" },
			{ "India", "in" },
			{ "Germany", "de" },
			{ "France", "fr" },
			{ "Spain", "es" },
			{ "Italy", "it" },
			{ "Netherlands", "nl" },
			{ "Belgium", "be" },
			{ "Switzerland", "ch" },
			{ "Austria", "at" },
			{ "Sweden", "se" },
			{ "Norway", "no" },
			{ "Denmark", "dk" },
			{ "Finland", "fi" },
			{ "Portugal", "pt" },
			{ "Poland", "pl" },
			{ "Mexico", "mx" },
			{ "Brazil", "br" },
			{ "Argentina", "ar" },
			{ "Chile", "cl" },
			{ "Japan", "jp" },
			{ "China", "cn" },
			{ "Singapore", "sg" },
			{ "Philippines", "ph" },
			{ "Malaysia", "my" },
			{ "Indonesia", "id" },
			{ "United Arab Emirates", "ae" },
			{ "Saudi Arabia", "sa" },
			{ "Israel", "il" },
		};

		/// <summary>Returns the ISO-3166 alpha-2 region code for a country name, or null when not mapped/blank.</summary>
		public static string ToIso(string countryName)
		{
			if (string.IsNullOrWhiteSpace(countryName))
				return null;

			return NameToIso.TryGetValue(countryName.Trim(), out var iso) ? iso : null;
		}
	}
}
