using Autofac;
using Resgrid.Model.Providers;

namespace Resgrid.Providers.Weather
{
	public class WeatherProviderModule : Module
	{
		protected override void Load(ContainerBuilder builder)
		{
			builder.RegisterType<NwsWeatherAlertProvider>().As<IWeatherAlertProvider>().InstancePerLifetimeScope();
			builder.RegisterType<EnvironmentCanadaWeatherAlertProvider>().As<IWeatherAlertProvider>().InstancePerLifetimeScope();
			builder.RegisterType<MeteoAlarmWeatherAlertProvider>().As<IWeatherAlertProvider>().InstancePerLifetimeScope();
			builder.RegisterType<WeatherAlertProviderFactory>().As<IWeatherAlertProviderFactory>().InstancePerLifetimeScope();
		}
	}
}
