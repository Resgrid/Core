using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Providers
{
	public interface IIncidentWeatherProvider
	{
		Task<IncidentWeather> GetWeatherAsync(decimal latitude, decimal longitude, int forecastHours = 24, CancellationToken cancellationToken = default);
	}
}
