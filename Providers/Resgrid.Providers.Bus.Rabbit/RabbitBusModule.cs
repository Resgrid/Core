using Autofac;
using Resgrid.Model.Providers;

namespace Resgrid.Providers.Bus.Rabbit
{
	public class RabbitBusModule : Module
	{
		protected override void Load(ContainerBuilder builder)
		{
			builder.RegisterType<RabbitOutboundQueueProvider>().As<IRabbitOutboundQueueProvider>().SingleInstance();
			builder.RegisterType<RabbitInboundEventProvider>().As<IRabbitInboundEventProvider>().InstancePerLifetimeScope();

			//builder.RegisterType<UnitNotificationProvider>().As<IUnitNotificationProvider>().InstancePerLifetimeScope();
			//builder.RegisterType<OutboundQueueProvider>().As<IOutboundQueueProvider>().InstancePerLifetimeScope();
			//builder.RegisterType<CqrsProvider>().As<ICqrsProvider>().InstancePerLifetimeScope();
			//builder.RegisterType<InboundEventProvider>().As<IInboundEventProvider>().InstancePerLifetimeScope();

			//builder.RegisterType<OutboundEventProvider>().As<IOutboundEventProvider>().SingleInstance();
		}
	}
}
