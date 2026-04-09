using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Providers
{
	public interface IWeatherAlertProvider
	{
		WeatherAlertSourceType SourceType { get; }
		Task<List<WeatherAlert>> FetchAlertsAsync(WeatherAlertSource source, CancellationToken ct = default);
	}
}
