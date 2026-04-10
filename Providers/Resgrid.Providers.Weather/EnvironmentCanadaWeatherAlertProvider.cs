using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Providers;

namespace Resgrid.Providers.Weather
{
	public class EnvironmentCanadaWeatherAlertProvider : IWeatherAlertProvider
	{
		public WeatherAlertSourceType SourceType => WeatherAlertSourceType.EnvironmentCanada;

		public async Task<List<WeatherAlert>> FetchAlertsAsync(WeatherAlertSource source, CancellationToken ct = default)
		{
			// Check shared cache first
			if (WeatherAlertResponseCache.TryGet(SourceType, source.AreaFilter, out var cachedAlerts))
			{
				return cachedAlerts;
			}

			// TODO: Implement CAP XML parsing from Environment Canada
			// Endpoint: https://dd.weather.gc.ca/alerts/cap/
			var alerts = new List<WeatherAlert>();

			// Store in shared cache
			WeatherAlertResponseCache.Set(SourceType, source.AreaFilter, alerts);

			return alerts;
		}
	}
}
