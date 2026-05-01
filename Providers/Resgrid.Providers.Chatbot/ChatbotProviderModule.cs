using Autofac;
using Resgrid.Providers.Chatbot.Adapters;
using Resgrid.Providers.Chatbot.Interfaces;

namespace Resgrid.Providers.Chatbot
{
	public class ChatbotProviderModule : Module
	{
		protected override void Load(ContainerBuilder builder)
		{
			// SMS Adapters (Phase 1)
			builder.RegisterType<TwilioSmsAdapter>()
				.As<IChatbotPlatformAdapter>()
				.InstancePerLifetimeScope();

			builder.RegisterType<SignalWireSmsAdapter>()
				.As<IChatbotPlatformAdapter>()
				.InstancePerLifetimeScope();

			// Phase 2: Rich platform adapters
			builder.RegisterType<DiscordBotAdapter>()
				.As<IChatbotPlatformAdapter>()
				.InstancePerLifetimeScope();

			builder.RegisterType<SlackBotAdapter>()
				.As<IChatbotPlatformAdapter>()
				.InstancePerLifetimeScope();

			builder.RegisterType<TelegramBotAdapter>()
				.As<IChatbotPlatformAdapter>()
				.InstancePerLifetimeScope();
		}
	}
}
