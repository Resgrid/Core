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

			// InstancePerLifetimeScope (not SingleInstance) because it now depends on the
			// per-scope IChatbotIdentityRepository (a singleton would capture a scoped DB connection).
			builder.RegisterType<ChatbotUserIdentityService>()
				.As<IChatbotUserIdentityService>()
				.InstancePerLifetimeScope();

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

			builder.RegisterType<ChatbotDepartmentConfigService>()
				.As<IChatbotDepartmentConfigService>()
				.InstancePerLifetimeScope();

			builder.RegisterType<ChatbotUserSearchService>()
				.As<IChatbotUserSearchService>()
				.InstancePerLifetimeScope();

			// Default no-op Web Chat notifier; the real SignalR-backed notifier in the web layer
			// overrides this (PreserveExistingDefaults keeps the real one winning regardless of order).
			builder.RegisterType<NullChatbotWebChatNotifier>()
				.As<IChatbotWebChatNotifier>()
				.SingleInstance()
				.PreserveExistingDefaults();

			builder.RegisterType<ChatbotRateLimiter>()
				.As<IChatbotRateLimiter>()
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

			builder.RegisterType<CloseCallHandler>()
				.As<IChatbotActionHandler>()
				.InstancePerLifetimeScope();

			builder.RegisterType<DispatchCallHandler>()
				.As<IChatbotActionHandler>()
				.InstancePerLifetimeScope();

			builder.RegisterType<RespondToCallHandler>()
				.As<IChatbotActionHandler>()
				.InstancePerLifetimeScope();

			builder.RegisterType<UnitsActionHandler>()
				.As<IChatbotActionHandler>()
				.InstancePerLifetimeScope();

			builder.RegisterType<SetUnitStatusHandler>()
				.As<IChatbotActionHandler>()
				.InstancePerLifetimeScope();

			builder.RegisterType<MyStatusActionHandler>()
				.As<IChatbotActionHandler>()
				.InstancePerLifetimeScope();

			builder.RegisterType<MessagesActionHandler>()
				.As<IChatbotActionHandler>()
				.InstancePerLifetimeScope();

			builder.RegisterType<MessageReadHandler>()
				.As<IChatbotActionHandler>()
				.InstancePerLifetimeScope();

			builder.RegisterType<MessageDeleteHandler>()
				.As<IChatbotActionHandler>()
				.InstancePerLifetimeScope();

			builder.RegisterType<MessageRespondHandler>()
				.As<IChatbotActionHandler>()
				.InstancePerLifetimeScope();

			builder.RegisterType<MessageSendHandler>()
				.As<IChatbotActionHandler>()
				.InstancePerLifetimeScope();

			builder.RegisterType<CalendarActionHandler>()
				.As<IChatbotActionHandler>()
				.InstancePerLifetimeScope();

			builder.RegisterType<CalendarRsvpHandler>()
				.As<IChatbotActionHandler>()
				.InstancePerLifetimeScope();

			builder.RegisterType<ShiftSignupHandler>()
				.As<IChatbotActionHandler>()
				.InstancePerLifetimeScope();

			builder.RegisterType<ShiftDropHandler>()
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

			builder.RegisterType<WeatherAlertHandler>()
				.As<IChatbotActionHandler>()
				.InstancePerLifetimeScope();

			builder.RegisterType<AvailabilityActionHandler>()
				.As<IChatbotActionHandler>()
				.InstancePerLifetimeScope();

			builder.RegisterType<UnitsAvailableActionHandler>()
				.As<IChatbotActionHandler>()
				.InstancePerLifetimeScope();

			builder.RegisterType<CallRespondersActionHandler>()
				.As<IChatbotActionHandler>()
				.InstancePerLifetimeScope();

			builder.RegisterType<CallDispatchedActionHandler>()
				.As<IChatbotActionHandler>()
				.InstancePerLifetimeScope();

			builder.RegisterType<MyCallsActionHandler>()
				.As<IChatbotActionHandler>()
				.InstancePerLifetimeScope();

			builder.RegisterType<PollCreateHandler>()
				.As<IChatbotActionHandler>()
				.InstancePerLifetimeScope();

			builder.RegisterType<MyScheduleActionHandler>()
				.As<IChatbotActionHandler>()
				.InstancePerLifetimeScope();
		}
	}
}
