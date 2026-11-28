using System;
using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model
{
	/// <summary>
	/// Industrial ERT G8 CA1 (REQ-G8-01): where a catalog template applies. A code is "CC" (ISO 3166-1 alpha-2, or "EU" for the
	/// European Union as a whole), "CC-SS" (with the ISO 3166-2 subdivision, e.g. "CA-ON"), or null/empty for a generic template
	/// that fits everywhere. Catalogs offer a department the generic templates plus the ones for its country (national, or its own
	/// subdivision); nothing is ever removed, a "show all regions" view lists the rest. Department type never gates anything.
	/// </summary>
	public static class TemplateJurisdictions
	{
		public const string European = "EU";

		/// <summary>EU member states (ISO alpha-2; Greece is "GR" in ISO 3166-1).</summary>
		public static readonly IReadOnlyCollection<string> EuropeanUnion = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			"AT", "BE", "BG", "HR", "CY", "CZ", "DK", "EE", "FI", "FR", "DE", "GR", "HU", "IE", "IT", "LV", "LT", "LU", "MT", "NL", "PL", "PT", "RO", "SK", "SI", "ES", "SE"
		};

		private static readonly Dictionary<string, string> CountryNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			["CANADA"] = "CA", ["CAN"] = "CA", ["CA"] = "CA",
			["UNITED STATES"] = "US", ["UNITED STATES OF AMERICA"] = "US", ["USA"] = "US", ["US"] = "US", ["U.S."] = "US", ["U.S.A."] = "US", ["AMERICA"] = "US",
			["UNITED KINGDOM"] = "GB", ["UK"] = "GB", ["GB"] = "GB", ["GBR"] = "GB", ["GREAT BRITAIN"] = "GB", ["ENGLAND"] = "GB", ["SCOTLAND"] = "GB", ["WALES"] = "GB", ["NORTHERN IRELAND"] = "GB",
			["IRELAND"] = "IE", ["AUSTRALIA"] = "AU", ["AUS"] = "AU", ["NEW ZEALAND"] = "NZ", ["NZL"] = "NZ", ["MEXICO"] = "MX", ["MEX"] = "MX",
			["AUSTRIA"] = "AT", ["BELGIUM"] = "BE", ["BULGARIA"] = "BG", ["CROATIA"] = "HR", ["CYPRUS"] = "CY", ["CZECHIA"] = "CZ", ["CZECH REPUBLIC"] = "CZ",
			["DENMARK"] = "DK", ["ESTONIA"] = "EE", ["FINLAND"] = "FI", ["FRANCE"] = "FR", ["GERMANY"] = "DE", ["DEUTSCHLAND"] = "DE", ["GREECE"] = "GR",
			["HUNGARY"] = "HU", ["ITALY"] = "IT", ["ITALIA"] = "IT", ["LATVIA"] = "LV", ["LITHUANIA"] = "LT", ["LUXEMBOURG"] = "LU", ["MALTA"] = "MT",
			["NETHERLANDS"] = "NL", ["THE NETHERLANDS"] = "NL", ["POLAND"] = "PL", ["POLSKA"] = "PL", ["PORTUGAL"] = "PT", ["ROMANIA"] = "RO", ["SLOVAKIA"] = "SK",
			["SLOVENIA"] = "SI", ["SPAIN"] = "ES", ["ESPAÑA"] = "ES", ["SWEDEN"] = "SE", ["SVERIGE"] = "SE", ["NORWAY"] = "NO", ["SWITZERLAND"] = "CH", ["UKRAINE"] = "UA"
		};

		private static readonly Dictionary<string, string> CanadianSubdivisions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			["ONTARIO"] = "ON", ["QUEBEC"] = "QC", ["QUÉBEC"] = "QC", ["BRITISH COLUMBIA"] = "BC", ["ALBERTA"] = "AB", ["MANITOBA"] = "MB", ["SASKATCHEWAN"] = "SK",
			["NOVA SCOTIA"] = "NS", ["NEW BRUNSWICK"] = "NB", ["NEWFOUNDLAND AND LABRADOR"] = "NL", ["NEWFOUNDLAND"] = "NL", ["PRINCE EDWARD ISLAND"] = "PE",
			["NORTHWEST TERRITORIES"] = "NT", ["NUNAVUT"] = "NU", ["YUKON"] = "YT"
		};

		private static readonly Dictionary<string, string> UsSubdivisions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			["ALABAMA"] = "AL", ["ALASKA"] = "AK", ["ARIZONA"] = "AZ", ["ARKANSAS"] = "AR", ["CALIFORNIA"] = "CA", ["COLORADO"] = "CO", ["CONNECTICUT"] = "CT",
			["DELAWARE"] = "DE", ["DISTRICT OF COLUMBIA"] = "DC", ["FLORIDA"] = "FL", ["GEORGIA"] = "GA", ["HAWAII"] = "HI", ["IDAHO"] = "ID", ["ILLINOIS"] = "IL",
			["INDIANA"] = "IN", ["IOWA"] = "IA", ["KANSAS"] = "KS", ["KENTUCKY"] = "KY", ["LOUISIANA"] = "LA", ["MAINE"] = "ME", ["MARYLAND"] = "MD",
			["MASSACHUSETTS"] = "MA", ["MICHIGAN"] = "MI", ["MINNESOTA"] = "MN", ["MISSISSIPPI"] = "MS", ["MISSOURI"] = "MO", ["MONTANA"] = "MT", ["NEBRASKA"] = "NE",
			["NEVADA"] = "NV", ["NEW HAMPSHIRE"] = "NH", ["NEW JERSEY"] = "NJ", ["NEW MEXICO"] = "NM", ["NEW YORK"] = "NY", ["NORTH CAROLINA"] = "NC",
			["NORTH DAKOTA"] = "ND", ["OHIO"] = "OH", ["OKLAHOMA"] = "OK", ["OREGON"] = "OR", ["PENNSYLVANIA"] = "PA", ["RHODE ISLAND"] = "RI",
			["SOUTH CAROLINA"] = "SC", ["SOUTH DAKOTA"] = "SD", ["TENNESSEE"] = "TN", ["TEXAS"] = "TX", ["UTAH"] = "UT", ["VERMONT"] = "VT", ["VIRGINIA"] = "VA",
			["WASHINGTON"] = "WA", ["WEST VIRGINIA"] = "WV", ["WISCONSIN"] = "WI", ["WYOMING"] = "WY", ["PUERTO RICO"] = "PR", ["GUAM"] = "GU"
		};

		/// <summary>The country part of a code ("CA" of "CA-ON"), upper case; null for a generic code.</summary>
		public static string Country(string code)
		{
			if (string.IsNullOrWhiteSpace(code)) return null;
			var dash = code.IndexOf('-');
			return (dash < 0 ? code : code.Substring(0, dash)).Trim().ToUpperInvariant();
		}

		/// <summary>The subdivision part of a code ("ON" of "CA-ON"), upper case; null for a national or generic code.</summary>
		public static string Subdivision(string code)
		{
			if (string.IsNullOrWhiteSpace(code)) return null;
			var dash = code.IndexOf('-');
			return dash < 0 || dash == code.Length - 1 ? null : code.Substring(dash + 1).Trim().ToUpperInvariant();
		}

		public static bool IsGeneric(string code) => string.IsNullOrWhiteSpace(code);

		/// <summary>
		/// True when a template coded <paramref name="template"/> is offered to a department in <paramref name="department"/>. A generic
		/// template fits everywhere; an unknown department sees everything; "EU" fits every member state; a national template fits every
		/// subdivision of its country, and a subdivision template only its own subdivision (or a department whose subdivision is unknown).
		/// </summary>
		public static bool Fits(string template, string department)
		{
			if (IsGeneric(template) || IsGeneric(department)) return true;
			var templateCountry = Country(template); var departmentCountry = Country(department);
			if (templateCountry == European) return departmentCountry == European || EuropeanUnion.Contains(departmentCountry);
			if (templateCountry != departmentCountry) return false;
			var templateSubdivision = Subdivision(template); var departmentSubdivision = Subdivision(department);
			return templateSubdivision == null || departmentSubdivision == null || templateSubdivision == departmentSubdivision;
		}

		/// <summary>The templates fitting a department, in their catalog order.</summary>
		public static IReadOnlyList<T> Filter<T>(IEnumerable<T> items, Func<T, string> jurisdiction, string department) =>
			(items ?? Enumerable.Empty<T>()).Where(item => Fits(jurisdiction(item), department)).ToList();

		/// <summary>
		/// A department's code from its free-text address: "Canada" / "CA" / "CAN" with "Ontario" / "ON" → "CA-ON". Null when the country
		/// cannot be read (the catalogs then show everything).
		/// </summary>
		public static string FromAddress(string country, string state)
		{
			var countryCode = NormalizeCountry(country);
			if (countryCode == null) return null;
			var subdivision = NormalizeSubdivision(countryCode, state);
			return subdivision == null ? countryCode : countryCode + "-" + subdivision;
		}

		private static string NormalizeCountry(string country)
		{
			var value = country?.Trim().TrimEnd('.');
			if (string.IsNullOrEmpty(value)) return null;
			if (CountryNames.TryGetValue(value, out var code)) return code;
			if (CountryNames.TryGetValue(value + ".", out code)) return code;
			return value.Length == 2 && value.All(char.IsAsciiLetter) ? value.ToUpperInvariant() : null;
		}

		private static string NormalizeSubdivision(string country, string state)
		{
			var value = state?.Trim().TrimEnd('.');
			if (string.IsNullOrEmpty(value)) return null;
			var names = country == "CA" ? CanadianSubdivisions : country == "US" ? UsSubdivisions : null;
			if (names != null && names.TryGetValue(value, out var code)) return code;
			if (value.Length >= 2 && value.Length <= 3 && value.All(char.IsAsciiLetterOrDigit))
			{
				var upper = value.ToUpperInvariant();
				// A known country's subdivision must be one of its own codes; elsewhere any short ISO-shaped code is kept.
				return names == null || names.ContainsValue(upper) ? upper : null;
			}
			return null;
		}
	}
}
