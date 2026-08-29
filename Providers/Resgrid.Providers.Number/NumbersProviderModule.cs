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

			// ADP outbound net (plan 7.5). SMS is scrubbed in place; voice is rebuilt through the
			// protected projection, because a prompt read aloud as "REDACTED" helps nobody.
			builder.RegisterDecorator<ProtectedTextMessageProviderDecorator, ITextMessageProvider>();
			builder.RegisterDecorator<ProtectedOutboundVoiceProviderDecorator, IOutboundVoiceProvider>();

			builder.RegisterType<PhoneNumberProcesserProvider>().As<IPhoneNumberProcesserProvider>().SingleInstance();
		}
	}
}