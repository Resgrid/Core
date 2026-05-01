using Autofac;
using Resgrid.Chatbot.Interfaces;
using Resgrid.Chatbot.NLU.Providers;
using Resgrid.Chatbot.NLU.Services;

namespace Resgrid.Chatbot.NLU
{
	public class NLUModule : Module
	{
		protected override void Load(ContainerBuilder builder)
		{
			// Keyword classifier (primary, always available, Priority=0)
			builder.RegisterType<KeywordIntentClassifier>()
				.As<INLUProvider>()
				.InstancePerLifetimeScope();

			// ML.NET classifier (optional, requires trained model, Priority=10)
			builder.RegisterType<MLNetNluProvider>()
				.As<INLUProvider>()
				.InstancePerLifetimeScope();

			// Cloud LLM classifier (OpenAI/DeepSeek/Azure/Anthropic compatible, Priority=100)
			builder.RegisterType<OpenAiCompatibleNluProvider>()
				.As<INLUProvider>()
				.InstancePerLifetimeScope();

			// Entity extractor
			builder.RegisterType<EntityExtractor>()
				.As<IEntityExtractor>()
				.InstancePerLifetimeScope();
		}
	}
}
