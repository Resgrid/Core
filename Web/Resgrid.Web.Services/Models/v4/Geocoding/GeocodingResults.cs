namespace Resgrid.Web.Services.Models.v4.Geocoding
{
	/// <summary>
	/// Result of a forward geocode (address → coordinates) request.
	/// </summary>
	public class ForwardGeocodeResult : StandardApiResponseV4Base
	{
		/// <summary>Response data</summary>
		public ForwardGeocodeData Data { get; set; }

		public ForwardGeocodeResult()
		{
			Data = new ForwardGeocodeData();
		}
	}

	public class ForwardGeocodeData
	{
		/// <summary>Latitude of the geocoded location, or null if not found.</summary>
		public double? Latitude { get; set; }

		/// <summary>Longitude of the geocoded location, or null if not found.</summary>
		public double? Longitude { get; set; }

		/// <summary>
		/// Normalised address for the resolved point when the provider supplies one. Clients that show
		/// the resolved address fall back to the text the user typed when this is empty.
		/// </summary>
		public string Address { get; set; }

		/// <summary>
		/// True when the lookup ran successfully and simply matched nothing, false when the lookup
		/// itself failed. Clients need the difference to say "address not found" instead of "try again".
		/// </summary>
		public bool LookupSucceeded { get; set; }
	}

	/// <summary>
	/// Result of a reverse geocode (coordinates → address) request.
	/// </summary>
	public class ReverseGeocodeResult : StandardApiResponseV4Base
	{
		/// <summary>Response data</summary>
		public ReverseGeocodeData Data { get; set; }

		public ReverseGeocodeResult()
		{
			Data = new ReverseGeocodeData();
		}
	}

	public class ReverseGeocodeData
	{
		/// <summary>Human-readable address for the supplied coordinates, or empty if not found.</summary>
		public string Address { get; set; }

		/// <summary>
		/// True when the lookup ran successfully and simply matched nothing, false when the lookup
		/// itself failed. Clients need the difference to say "address not found" instead of "try again".
		/// </summary>
		public bool LookupSucceeded { get; set; }
	}
}
