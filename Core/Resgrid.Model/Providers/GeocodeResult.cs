using System;

namespace Resgrid.Model.Providers
{
	/// <summary>What a forward geocode concluded about an address.</summary>
	public enum GeocodeOutcome
	{
		Found = 0,

		/// <summary>A geocoder answered and nothing matched: the address itself is the problem, so asking again will not help.</summary>
		NotFound = 1,

		/// <summary>No geocoder answered (error, rate limit, timeout, outage). Says nothing about the address; worth retrying later.</summary>
		Unavailable = 2,

		/// <summary>The install has no geocoder configured; retrying will not help until one is.</summary>
		NotConfigured = 3
	}

	/// <summary>The result of <see cref="IGeoLocationProvider.GeocodeAddressAsync"/>.</summary>
	public class GeocodeResult
	{
		public GeocodeOutcome Outcome { get; set; }

		/// <summary>Set when <see cref="Outcome"/> is <see cref="GeocodeOutcome.Found"/>.</summary>
		public double? Latitude { get; set; }

		public double? Longitude { get; set; }

		public static GeocodeResult Found(double latitude, double longitude) =>
			new GeocodeResult { Outcome = GeocodeOutcome.Found, Latitude = latitude, Longitude = longitude };

		public static GeocodeResult Of(GeocodeOutcome outcome) => new GeocodeResult { Outcome = outcome };

		/// <summary>
		/// A position a POI can stand at. Out of range is usually latitude and longitude swapped; 0,0 is a converter's (or a
		/// geocoder's) placeholder for "not located".
		/// </summary>
		public static bool IsValidPosition(double latitude, double longitude) =>
			!double.IsNaN(latitude) && !double.IsNaN(longitude) &&
			latitude >= -90 && latitude <= 90 && longitude >= -180 && longitude <= 180 &&
			!(latitude == 0 && longitude == 0);
	}

	/// <summary>
	/// Thrown by an <see cref="IGeocodingProvider"/> when the service did not answer (an HTTP error status, a rate limit, an
	/// unreadable body). Unlike a null match it says nothing about the address, so the address is worth asking again later.
	/// </summary>
	public class GeocodingUnavailableException : Exception
	{
		public GeocodingUnavailableException(string provider, int statusCode)
			: base($"Geocoder '{provider}' did not answer (HTTP {statusCode}).")
		{
			Provider = provider;
			StatusCode = statusCode;
		}

		public string Provider { get; }

		public int StatusCode { get; }
	}
}
