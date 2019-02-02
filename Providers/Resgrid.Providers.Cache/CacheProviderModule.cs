using Autofac;
using Resgrid.Model.Providers;

namespace Resgrid.Providers.Cache
{
	public class CacheProviderModule : Module
	{
		protected override void Load(ContainerBuilder builder)
		{
			builder.RegisterType<AzureRedisCacheProvider>().As<ICacheProvider>().SingleInstance();
		}
	}
}