using System;
using System.Collections.Generic;
using System.Linq;
using Resgrid.Model;
using Resgrid.Model.Providers;

namespace Resgrid.Providers.Weather
{
	public class WeatherAlertProviderFactory : IWeatherAlertProviderFactory
	{
		private readonly IEnumerable<IWeatherAlertProvider> _providers;

		public WeatherAlertProviderFactory(IEnumerable<IWeatherAlertProvider> providers)
		{
			_providers = providers;
		}

		public IWeatherAlertProvider GetProvider(WeatherAlertSourceType sourceType)
		{
			var provider = _providers.FirstOrDefault(p => p.SourceType == sourceType);
			if (provider == null)
				throw new NotSupportedException($"No weather alert provider found for source type: {sourceType}");
			return provider;
		}
	}
}
