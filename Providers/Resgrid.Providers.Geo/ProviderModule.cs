using Autofac;
using Resgrid.Model.Providers;

namespace Resgrid.Providers.GeoLocationProvider
{
	public class ProviderModule : Module
	{
		protected override void Load(ContainerBuilder builder)
		{
			builder.RegisterType<GeoLocationProvider>().As<IGeoLocationProvider>().InstancePerLifetimeScope();
			builder.RegisterType<KmlProvider>().As<IKmlProvider>().InstancePerLifetimeScope();

			// Geocoders in the order nothing depends on; GeoLocationProvider orders them by MappingConfig.GeocodingProviders.
			builder.RegisterType<Geocoding.GoogleGeocodingProvider>().As<IGeocodingProvider>().SingleInstance();
			builder.RegisterType<Geocoding.LocationIqGeocodingProvider>().As<IGeocodingProvider>().SingleInstance();
			builder.RegisterType<Geocoding.BingGeocodingProvider>().As<IGeocodingProvider>().SingleInstance();
			builder.RegisterType<Geocoding.NominatimGeocodingProvider>().As<IGeocodingProvider>().SingleInstance();
			builder.RegisterType<Geocoding.MapboxGeocodingProvider>().As<IGeocodingProvider>().SingleInstance();

			// Every routing engine is registered; RoutingService picks the one MappingConfig.RoutingProvider names.
			builder.RegisterType<Routing.ValhallaRoutingProvider>().As<IRoutingProvider>().SingleInstance();
			builder.RegisterType<Routing.MapboxRoutingProvider>().As<IRoutingProvider>().SingleInstance();
			builder.RegisterType<Routing.GoogleRoutingProvider>().As<IRoutingProvider>().SingleInstance();
			builder.RegisterType<Routing.OsrmRoutingProvider>().As<IRoutingProvider>().SingleInstance();
			builder.RegisterType<Routing.EstimateRoutingProvider>().As<IRoutingProvider>().SingleInstance();

			// Offline map pack storage (directory or S3-compatible bucket); stateless apart from its client, so one per process.
			builder.RegisterType<Offline.OfflineMapPackStore>().As<IOfflineMapPackStore>().SingleInstance();
		}
	}
}