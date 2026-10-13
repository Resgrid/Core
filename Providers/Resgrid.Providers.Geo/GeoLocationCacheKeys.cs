using System;
using System.Globalization;
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

		/// <summary>Forward geocode (address to coordinates). Case and whitespace differences share one entry.</summary>
		public static string ForwardGeocode(string address)
		{
			return ForwardPrefix + Hash(NormalizeText(address));
		}

		/// <summary>Reverse geocode (coordinates to address).</summary>
		public static string ReverseGeocode(double latitude, double longitude)
		{
			return ReversePrefix + Hash(FormatCoordinates(latitude, longitude));
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
