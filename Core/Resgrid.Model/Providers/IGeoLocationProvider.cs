using System.Threading.Tasks;

namespace Resgrid.Model.Providers
{
	public interface IGeoLocationProvider
	{
		Task<string> GetAproxAddressFromLatLong(double lat, double lon);
		Task<string> GetAddressFromLatLong(double lat, double lon);
		Task<string> GetLatLonFromAddress(string address);

		/// <summary>
		/// Forward geocode through the same chain and cache as <see cref="GetLatLonFromAddress"/>, telling an address no
		/// geocoder could find (<see cref="GeocodeOutcome.NotFound"/>, permanent) apart from a lookup no geocoder answered
		/// (<see cref="GeocodeOutcome.Unavailable"/>, worth retrying).
		/// </summary>
		Task<GeocodeResult> GeocodeAddressAsync(string address, System.Threading.CancellationToken cancellationToken = default);
		Task<RouteInformation> GetRoute(string start, string end);
		Task<RouteInformation> GetRoute(double startLat, double startLon, double endLat, double endLon);
		Task<Coordinates> GetCoordinatesFromW3W(string words);
		Task<string> GetW3WFromCoordinates(Coordinates coordinates);
		Task<Coordinates> GetLatLonFromAddressLocationIQ(string address);
		Task<string> GetAddressFromLatLonLocationIQ(string lat, string lon);
		Task<Coordinates> GetCoordinatesFromW3WAsync(string words);
	}
}
