using Autofac;
using Resgrid.Model.Providers;

namespace Resgrid.Providers.Messaging
{
	public class MessagingProviderModule : Module
	{
		protected override void Load(ContainerBuilder builder)
		{
			builder.RegisterType<NovuProvider>().As<INovuProvider>().InstancePerLifetimeScope();
			builder.RegisterType<GifProvider>().As<IGifProvider>().InstancePerLifetimeScope();
			builder.RegisterType<DispatcherDeskPushProvider>().As<IDispatcherDeskPushProvider>().InstancePerLifetimeScope();
		}
	}
}
