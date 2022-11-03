using Autofac;
using Resgrid.Model.Providers;

namespace Resgrid.Providers.Bus
{
	public class BusModule : Module
	{
		protected override void Load(ContainerBuilder builder)
		{
			builder.RegisterType<NotificationProvider>().As<INotificationProvider>().InstancePerLifetimeScope();
			builder.RegisterType<UnitNotificationProvider>().As<IUnitNotificationProvider>().InstancePerLifetimeScope();
			builder.RegisterType<CqrsProvider>().As<ICqrsProvider>().InstancePerLifetimeScope();
			builder.RegisterType<InboundEventProvider>().As<IInboundEventProvider>().InstancePerLifetimeScope();
			builder.RegisterType<PaymentProvider>().As<IPaymentProvider>().InstancePerLifetimeScope();
			builder.RegisterType<UnitLocationEventProvider>().As<IUnitLocationEventProvider>().InstancePerLifetimeScope();
			builder.RegisterType<PersonnelLocationEventProvider>().As<IPersonnelLocationEventProvider>().InstancePerLifetimeScope();

			// Singletons
			builder.RegisterType<OutboundQueueProvider>().As<IOutboundQueueProvider>().SingleInstance();
			builder.RegisterType<EventAggregator>().As<IEventAggregator>().SingleInstance();
			builder.RegisterType<OutboundEventProvider>().As<IOutboundEventProvider>().SingleInstance();
			builder.RegisterType<SignalrProvider>().As<ISignalrProvider>().SingleInstance();
		}
	}
}
