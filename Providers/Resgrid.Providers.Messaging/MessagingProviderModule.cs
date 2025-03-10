using Autofac;
using Resgrid.Model.Providers;

namespace Resgrid.Providers.Messaging
{
	public class MessagingProviderModule : Module
	{
		protected override void Load(ContainerBuilder builder)
		{
			builder.RegisterType<NovuProvider>().As<INovuProvider>().InstancePerLifetimeScope();
		}
	}
}
