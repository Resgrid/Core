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
			builder.Register(_ => new RabbitAdminAssistTraceQueue(new RabbitMQ.Client.ConnectionFactory
			{
				UserName = Config.ServiceBusConfig.RabbitUsername, Password = Config.ServiceBusConfig.RabbbitPassword,
				AutomaticRecoveryEnabled = false
			})).As<Resgrid.Model.AdminAssist.IAdminAssistTraceQueue>().SingleInstance();

			//builder.RegisterType<UnitNotificationProvider>().As<IUnitNotificationProvider>().InstancePerLifetimeScope();
			//builder.RegisterType<OutboundQueueProvider>().As<IOutboundQueueProvider>().InstancePerLifetimeScope();
			//builder.RegisterType<CqrsProvider>().As<ICqrsProvider>().InstancePerLifetimeScope();
			//builder.RegisterType<InboundEventProvider>().As<IInboundEventProvider>().InstancePerLifetimeScope();

			//builder.RegisterType<OutboundEventProvider>().As<IOutboundEventProvider>().SingleInstance();
		}
	}
}
