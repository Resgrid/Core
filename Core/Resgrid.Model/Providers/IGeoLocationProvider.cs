using System.Threading.Tasks;

namespace Resgrid.Model.Providers
{
	public interface IGeoLocationProvider
	{
		Task<string> GetAproxAddressFromLatLong(double lat, double lon);
		Task<string> GetAddressFromLatLong(double lat, double lon);
		Task<string> GetLatLonFromAddress(string address);
		Task<RouteInformation> GetRoute(string start, string end);
		Task<RouteInformation> GetRoute(double startLat, double startLon, double endLat, double endLon);
		Task<Coordinates> GetCoordinatesFromW3W(string words);
		Task<string> GetW3WFromCoordinates(Coordinates coordinates);
		Task<Coordinates> GetLatLonFromAddressLocationIQ(string address);
		Task<string> GetAddressFromLatLonLocationIQ(string lat, string lon);
		Task<Coordinates> GetCoordinatesFromW3WAsync(string words);
	}
}
