using System;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Resgrid.Providers.GeoLocationProvider
{
	/// <summary>
	/// Redis keys for the geocode, route and what3words lookups in <see cref="GeoLocationProvider"/>. The keys have to
	/// come out the same in every process (web replicas, workers, restarts) or the 30 day cache never hits and every
	/// lookup is billed again, so they are a SHA-256 of a normalized input. string.GetHashCode is randomized per
	/// process on .NET Core, and at 32 bits two different addresses can share a key and return each other's
	/// coordinates.
	/// </summary>
	public static class GeoLocationCacheKeys
	{
		// Bumped from FGeo_/RGeo_/Route_/RW3W_ when the keys moved off string.GetHashCode. Entries under the old
		// prefixes are never read again and age out on their own expiry.
		private const string ForwardPrefix = "FGeo2_";
		private const string ReversePrefix = "RGeo2_";
		private const string RoutePrefix = "Route2_";
		private const string ReverseW3WPrefix = "RW3W2_";

		// 16 bytes (32 hex chars) of the SHA-256; a collision at 128 bits is not a practical concern.
		private const int HashBytes = 16;

		/// <summary>
		/// Forward geocode (address to coordinates). Case and whitespace differences share one entry. A department that
		/// geocodes through its own account (<paramref name="departmentId"/> above 0) has entries of its own, per
		/// <paramref name="geocoder"/> (its provider name), so switching providers never serves the old provider's answers;
		/// 0 is the install-wide entry every other lookup shares. The account's key is left out on purpose: a rotated key
		/// gets the same answers.
		/// </summary>
		public static string ForwardGeocode(string address, int departmentId = 0, string geocoder = null)
		{
			return ForwardPrefix + DepartmentScope(departmentId, geocoder) + Hash(NormalizeText(address));
		}

		/// <summary>Reverse geocode (coordinates to address); <paramref name="departmentId"/> and <paramref name="geocoder"/> as for <see cref="ForwardGeocode"/>.</summary>
		public static string ReverseGeocode(double latitude, double longitude, int departmentId = 0, string geocoder = null)
		{
			return ReversePrefix + DepartmentScope(departmentId, geocoder) + Hash(FormatCoordinates(latitude, longitude));
		}

		private static string DepartmentScope(int departmentId, string geocoder)
		{
			if (departmentId <= 0)
				return string.Empty;

			// Provider names are short lowercase words (google, mapbox, locationiq); anything else is dropped so the key stays
			// one plain segment.
			var name = new string((geocoder ?? string.Empty).ToLowerInvariant().Where(char.IsAsciiLetterOrDigit).ToArray());
			return "d" + departmentId.ToString(CultureInfo.InvariantCulture) + (name.Length > 0 ? "." + name : string.Empty) + "_";
		}

		/// <summary>
		/// Route between two coordinate pairs. Kept apart from <see cref="Route(string, string)"/> because the two
		/// GetRoute overloads cache different payloads (only the string one fills in duration and distance).
		/// </summary>
		public static string Route(double startLatitude, double startLongitude, double endLatitude, double endLongitude)
		{
			return RoutePrefix + Hash("coords\n" + FormatCoordinates(startLatitude, startLongitude) + "\n" + FormatCoordinates(endLatitude, endLongitude));
		}

		/// <summary>Route between two free-text locations (addresses or "lat,lon" strings).</summary>
		public static string Route(string start, string end)
		{
			// NormalizeText collapses every whitespace run to a single space, so neither side can contain the
			// "\n" separator and ("a b", "c") can never hash the same as ("a", "b c").
			return RoutePrefix + Hash("text\n" + NormalizeText(start) + "\n" + NormalizeText(end));
		}

		/// <summary>Coordinates to what3words.</summary>
		public static string ReverseW3W(double latitude, double longitude)
		{
			return ReverseW3WPrefix + Hash(FormatCoordinates(latitude, longitude));
		}

		/// <summary>
		/// A coordinate as the invariant shortest round-trip string, so a request running under a comma-decimal
		/// culture (de, fr, ...) neither gets a different key nor builds an ambiguous "47,5,8,2" pair.
		/// </summary>
		internal static string FormatCoordinate(double value)
		{
			// -0 and 0 are the same place but format as "-0" and "0".
			return (value == 0 ? 0d : value).ToString("R", CultureInfo.InvariantCulture);
		}

		private static string FormatCoordinates(double latitude, double longitude)
		{
			return FormatCoordinate(latitude) + "," + FormatCoordinate(longitude);
		}

		private static string NormalizeText(string value)
		{
			if (string.IsNullOrWhiteSpace(value))
				return string.Empty;

			return string.Join(" ", value.Split((char[])null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
		}

		private static string Hash(string value)
		{
			return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)), 0, HashBytes).ToLowerInvariant();
		}
	}
}
