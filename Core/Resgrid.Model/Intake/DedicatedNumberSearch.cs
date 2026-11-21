using System;
using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model
{
	/// <summary>
	/// A dedicated number search (call-intake-and-ai-destinations-plan.md §8 D13): the country (allowed countries only), then any of
	/// state / province, city, postal code, area code, digits the number contains, or near the department's address. Results are local
	/// and mobile numbers that take both SMS and voice. Region, postal code, area code and distance searches are United States and Canada
	/// only (the carrier ignores them elsewhere), so they are dropped for other countries.
	/// </summary>
	public sealed class DedicatedNumberSearch
	{
		public const int DefaultDistanceMiles = 25;
		public const int MaxDistanceMiles = 500;
		public const int MaxLocalityLength = 64;

		/// <summary>ISO 3166-1 alpha-2.</summary>
		public string CountryCode { get; set; }

		/// <summary>Two-letter state or province code (United States / Canada).</summary>
		public string Region { get; set; }

		/// <summary>City or town.</summary>
		public string Locality { get; set; }

		/// <summary>ZIP or Canadian postal code (United States / Canada).</summary>
		public string PostalCode { get; set; }

		/// <summary>NANP area code; elsewhere its digits narrow the number pattern instead.</summary>
		public string AreaCode { get; set; }

		/// <summary>Digits the number contains; * matches any one digit.</summary>
		public string Contains { get; set; }

		/// <summary>Search around the department's map center or address (United States / Canada).</summary>
		public bool NearDepartment { get; set; }

		/// <summary>Resolved from the department by the service when <see cref="NearDepartment"/>; the carrier searches around it.</summary>
		public double? NearLatitude { get; set; }

		public double? NearLongitude { get; set; }

		/// <summary>Radius around the near point in miles (1–500, carrier default 25).</summary>
		public int? DistanceMiles { get; set; }

		/// <summary>Most numbers to return.</summary>
		public int Limit { get; set; } = 20;

		public bool HasNearPoint => NearLatitude.HasValue && NearLongitude.HasValue;

		/// <summary>The United States and Canada (NANP), where the carrier supports region, postal code, area code and distance filters.</summary>
		public static bool IsNanp(string countryCode)
		{
			var code = (countryCode ?? "").Trim().ToUpperInvariant();
			return code == "US" || code == "CA";
		}

		/// <summary>State or province codes and English names for the country's region filter; empty outside the United States and Canada.</summary>
		public static IReadOnlyList<KeyValuePair<string, string>> RegionsFor(string countryCode)
		{
			switch ((countryCode ?? "").Trim().ToUpperInvariant())
			{
				case "US":
					return UsRegions;
				case "CA":
					return CanadaRegions;
				default:
					return Array.Empty<KeyValuePair<string, string>>();
			}
		}

		/// <summary>The region's two-letter code when it belongs to the country (case-insensitive), else null.</summary>
		public static string NormalizeRegion(string countryCode, string region)
		{
			var value = (region ?? "").Trim();
			if (value.Length == 0)
				return null;

			return RegionsFor(countryCode).Where(r => string.Equals(r.Key, value, StringComparison.OrdinalIgnoreCase) ||
				string.Equals(r.Value, value, StringComparison.OrdinalIgnoreCase)).Select(r => r.Key).FirstOrDefault();
		}

		private static KeyValuePair<string, string> R(string code, string name) => new KeyValuePair<string, string>(code, name);

		private static readonly IReadOnlyList<KeyValuePair<string, string>> UsRegions = new[]
		{
			R("AL", "Alabama"), R("AK", "Alaska"), R("AZ", "Arizona"), R("AR", "Arkansas"), R("CA", "California"), R("CO", "Colorado"),
			R("CT", "Connecticut"), R("DE", "Delaware"), R("DC", "District of Columbia"), R("FL", "Florida"), R("GA", "Georgia"), R("HI", "Hawaii"),
			R("ID", "Idaho"), R("IL", "Illinois"), R("IN", "Indiana"), R("IA", "Iowa"), R("KS", "Kansas"), R("KY", "Kentucky"), R("LA", "Louisiana"),
			R("ME", "Maine"), R("MD", "Maryland"), R("MA", "Massachusetts"), R("MI", "Michigan"), R("MN", "Minnesota"), R("MS", "Mississippi"),
			R("MO", "Missouri"), R("MT", "Montana"), R("NE", "Nebraska"), R("NV", "Nevada"), R("NH", "New Hampshire"), R("NJ", "New Jersey"),
			R("NM", "New Mexico"), R("NY", "New York"), R("NC", "North Carolina"), R("ND", "North Dakota"), R("OH", "Ohio"), R("OK", "Oklahoma"),
			R("OR", "Oregon"), R("PA", "Pennsylvania"), R("PR", "Puerto Rico"), R("RI", "Rhode Island"), R("SC", "South Carolina"),
			R("SD", "South Dakota"), R("TN", "Tennessee"), R("TX", "Texas"), R("UT", "Utah"), R("VT", "Vermont"), R("VA", "Virginia"),
			R("WA", "Washington"), R("WV", "West Virginia"), R("WI", "Wisconsin"), R("WY", "Wyoming")
		};

		private static readonly IReadOnlyList<KeyValuePair<string, string>> CanadaRegions = new[]
		{
			R("AB", "Alberta"), R("BC", "British Columbia"), R("MB", "Manitoba"), R("NB", "New Brunswick"), R("NL", "Newfoundland and Labrador"),
			R("NS", "Nova Scotia"), R("NT", "Northwest Territories"), R("NU", "Nunavut"), R("ON", "Ontario"), R("PE", "Prince Edward Island"),
			R("QC", "Quebec"), R("SK", "Saskatchewan"), R("YT", "Yukon")
		};
	}
}
