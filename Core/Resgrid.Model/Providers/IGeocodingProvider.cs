using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Providers
{
	/// <summary>
	/// One geocoding service (Google, Mapbox, Nominatim, LocationIQ, Bing). GeoLocationProvider walks them in the order
	/// MappingConfig.GeocodingProviders lists (maplibre-mapping-routing-navigation-plan.md §6.4); nothing else calls them.
	/// </summary>
	public interface IGeocodingProvider
	{
		/// <summary>The name MappingConfig.GeocodingProviders uses: google, mapbox, nominatim, locationiq or bing.</summary>
		string Name { get; }

		/// <summary>True when the install has what this provider needs.</summary>
		bool IsConfigured { get; }

		/// <summary>
		/// Coordinates for an address. Null only when the service answered and nothing matched; when the call failed (an HTTP
		/// error, a rate limit, a timeout) it throws, typically <see cref="GeocodingUnavailableException"/>, so a caller can
		/// tell "no such address" from "ask again later".
		/// </summary>
		Task<GeocodeMatch> ForwardAsync(string address, CancellationToken cancellationToken);

		/// <summary>An address for coordinates; null when nothing matched or the call failed.</summary>
		Task<string> ReverseAsync(double latitude, double longitude, CancellationToken cancellationToken);

		/// <summary>A copy that uses a department's own key (Mapping Settings, setting 125); null when the geocoder takes no key.</summary>
		IGeocodingProvider WithApiKey(string apiKey) => null;
	}

	public class GeocodeMatch
	{
		public double Latitude { get; set; }

		public double Longitude { get; set; }

		public string FormattedAddress { get; set; }
	}
}
