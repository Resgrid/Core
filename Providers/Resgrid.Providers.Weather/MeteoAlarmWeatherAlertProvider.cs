using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Providers;

namespace Resgrid.Providers.Weather
{
	public class MeteoAlarmWeatherAlertProvider : IWeatherAlertProvider
	{
		public WeatherAlertSourceType SourceType => WeatherAlertSourceType.MeteoAlarm;

		public async Task<List<WeatherAlert>> FetchAlertsAsync(WeatherAlertSource source, CancellationToken ct = default)
		{
			// Check shared cache first
			if (WeatherAlertResponseCache.TryGet(SourceType, source.AreaFilter, out var cachedAlerts))
			{
				return cachedAlerts;
			}

			// TODO: Implement MeteoAlarm API integration
			// Endpoint: https://feeds.meteoalarm.org/api/v1/
			var alerts = new List<WeatherAlert>();

			// Store in shared cache
			WeatherAlertResponseCache.Set(SourceType, source.AreaFilter, alerts);

			return alerts;
		}
	}
}
