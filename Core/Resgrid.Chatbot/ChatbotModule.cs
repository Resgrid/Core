using Autofac;
using Resgrid.Chatbot.Handlers;
using Resgrid.Chatbot.Interfaces;
using Resgrid.Chatbot.Services;

namespace Resgrid.Chatbot
{
	public class ChatbotModule : Module
	{
		protected override void Load(ContainerBuilder builder)
		{
			// Core Services
			builder.RegisterType<ChatbotIngressService>()
				.As<IChatbotIngressService>()
				.InstancePerLifetimeScope();

			builder.RegisterType<ChatbotIntentRouter>()
				.As<IChatbotIntentRouter>()
				.InstancePerLifetimeScope();

			builder.RegisterType<ChatbotSessionManager>()
				.As<IChatbotSessionManager>()
				.SingleInstance();

			builder.RegisterType<ChatbotUserIdentityService>()
				.As<IChatbotUserIdentityService>()
				.SingleInstance();

			// Phase 2: New services
			builder.RegisterType<IntentMapper>()
				.As<IIntentMapper>()
				.SingleInstance();

			builder.RegisterType<ConversationEngine>()
				.As<IConversationEngine>()
				.InstancePerLifetimeScope();

			builder.RegisterType<ChatbotTemplateRenderer>()
				.As<IChatbotTemplateRenderer>()
				.SingleInstance();

			builder.RegisterType<OAuthLinkingService>()
				.AsSelf()
				.InstancePerLifetimeScope();

			builder.RegisterType<CodeLinkingService>()
				.AsSelf()
				.InstancePerLifetimeScope();

			// Session store (Redis or in-memory fallback)
			builder.RegisterType<RedisSessionStore>()
				.As<IChatbotSessionStore>()
				.SingleInstance();

			// Action Handlers - registered as IEnumerable<IChatbotActionHandler>
			builder.RegisterType<StatusActionHandler>()
				.As<IChatbotActionHandler>()
				.InstancePerLifetimeScope();

			builder.RegisterType<StaffingActionHandler>()
				.As<IChatbotActionHandler>()
				.InstancePerLifetimeScope();

			builder.RegisterType<CallsActionHandler>()
				.As<IChatbotActionHandler>()
				.InstancePerLifetimeScope();

			builder.RegisterType<CallDetailActionHandler>()
				.As<IChatbotActionHandler>()
				.InstancePerLifetimeScope();

			builder.RegisterType<UnitsActionHandler>()
				.As<IChatbotActionHandler>()
				.InstancePerLifetimeScope();

			builder.RegisterType<MyStatusActionHandler>()
				.As<IChatbotActionHandler>()
				.InstancePerLifetimeScope();

			builder.RegisterType<MessagesActionHandler>()
				.As<IChatbotActionHandler>()
				.InstancePerLifetimeScope();

			builder.RegisterType<CalendarActionHandler>()
				.As<IChatbotActionHandler>()
				.InstancePerLifetimeScope();

			builder.RegisterType<PersonnelActionHandler>()
				.As<IChatbotActionHandler>()
				.InstancePerLifetimeScope();

			builder.RegisterType<HelpActionHandler>()
				.As<IChatbotActionHandler>()
				.InstancePerLifetimeScope();

			builder.RegisterType<DepartmentActionHandler>()
				.As<IChatbotActionHandler>()
				.InstancePerLifetimeScope();
		}
	}
}
