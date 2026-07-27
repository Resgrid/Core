using Autofac;
using Resgrid.Providers.Bus;
using Resgrid.Providers.Bus.Rabbit;
using Resgrid.Providers.Cache;
using Resgrid.Providers.Tracking;
using Resgrid.Repositories.DataRepository;
using Resgrid.Services;

namespace Resgrid.TrackerGateway
{
	public static class Bootstrapper
	{
		public static void ConfigureContainer(ContainerBuilder builder)
		{
			builder.RegisterModule(new DataModule());
			builder.RegisterModule(new ServicesModule());
			builder.RegisterModule(new CacheProviderModule());
			builder.RegisterModule(new BusModule());
			builder.RegisterModule(new RabbitBusModule());
			builder.RegisterModule(new TrackingProviderModule());
		}
	}
}
