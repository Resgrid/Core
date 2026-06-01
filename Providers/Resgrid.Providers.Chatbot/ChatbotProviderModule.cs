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

			// Phase 3: additional platform adapters
			builder.RegisterType<WhatsAppAdapter>()
				.As<IChatbotPlatformAdapter>()
				.InstancePerLifetimeScope();

			builder.RegisterType<WebChatAdapter>()
				.As<IChatbotPlatformAdapter>()
				.InstancePerLifetimeScope();

			// Phase 3: proactive outbound delivery (chat as a CommunicationService channel).
			builder.RegisterType<Services.ChatbotAdapterRegistry>()
				.As<IChatbotAdapterRegistry>()
				.InstancePerLifetimeScope();

			builder.RegisterType<Services.ChatbotOutboundService>()
				.As<Resgrid.Model.Services.IChatbotOutboundService>()
				.InstancePerLifetimeScope();
		}
	}
}
