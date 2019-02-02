using Autofac;
using Resgrid.Model.Providers;

namespace Resgrid.Providers.NumberProvider
{
	public class NumbersProviderModule : Module
	{
		protected override void Load(ContainerBuilder builder)
		{
			builder.RegisterType<NumberProviderFactory>().As<INumberProvider>().InstancePerLifetimeScope();
			builder.RegisterType<TextMessageProvider>().As<ITextMessageProvider>().InstancePerLifetimeScope();
			builder.RegisterType<OutboundVoiceProvider>().As<IOutboundVoiceProvider>().InstancePerLifetimeScope();

			builder.RegisterType<PhoneNumberProcesserProvider>().As<IPhoneNumberProcesserProvider>().SingleInstance();
		}
	}
}