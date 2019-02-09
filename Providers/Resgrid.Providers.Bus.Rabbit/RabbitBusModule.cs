using Autofac;
using Resgrid.Model.Providers;
using Resgrid.Providers.Bus.Rabbit;

namespace Resgrid.Providers.Bus
{
	public class RabbitBusModule : Module
	{
		protected override void Load(ContainerBuilder builder)
		{
			builder.RegisterType<RabbitOutboundQueueProvider>().As<IRabbitOutboundQueueProvider>().InstancePerLifetimeScope();
			//builder.RegisterType<UnitNotificationProvider>().As<IUnitNotificationProvider>().InstancePerLifetimeScope();
			//builder.RegisterType<OutboundQueueProvider>().As<IOutboundQueueProvider>().InstancePerLifetimeScope();
			//builder.RegisterType<CqrsProvider>().As<ICqrsProvider>().InstancePerLifetimeScope();
			//builder.RegisterType<InboundEventProvider>().As<IInboundEventProvider>().InstancePerLifetimeScope();

			//builder.RegisterType<OutboundEventProvider>().As<IOutboundEventProvider>().SingleInstance();
		}
	}
}
