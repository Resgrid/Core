using System.Threading.Tasks;

namespace Resgrid.Model.Providers
{
	public interface IGeoLocationProvider
	{
		string GetAproxAddressFromLatLong(double lat, double lon);
		string GetLatLonFromAddress(string address);
		string GetAddressFromLatLong(double lat, double lon);
		RouteInformation GetRoute(string start, string end);
		RouteInformation GetRoute(double startLat, double startLon, double endLat, double endLon);
		Coordinates GetCoordinatesFromW3W(string words);
		string GetW3WFromCoordinates(Coordinates coordinates);
		Coordinates GetLatLonFromAddressLocationIQ(string address);
		string GetAddressFromLatLonLocationIQ(string lat, string lon);
		Task<Coordinates> GetCoordinatesFromW3WAsync(string words);
	}
}
