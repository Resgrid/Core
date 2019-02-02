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
		}
	}
}