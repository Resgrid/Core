using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Tests.Intake
{
	/// <summary>Every intake registration resolves from the container, so a missing one fails here rather than at application start.</summary>
	[TestFixture]
	public class IntakeContainerCompositionTests : TestBase
	{
		[Test]
		public void Intake_services_and_repositories_resolve_from_the_container()
		{
			Resolve<IIntakeChannelsRepository>().Should().NotBeNull();
			Resolve<IIntakeMessagesRepository>().Should().NotBeNull();
			Resolve<IVoiceIntakeSessionsRepository>().Should().NotBeNull();
			Resolve<IIntakeChannelsService>().Should().NotBeNull();
			Resolve<IIntakeService>().Should().NotBeNull();
			Resolve<IVoiceIntakeTranscriber>().Should().NotBeNull();
			Resolve<IVoiceIntakeService>().Should().NotBeNull();
			Resolve<IVoiceIntakeRecordingService>().Should().NotBeNull();
			Resolve<Resgrid.Model.Providers.IVoiceRecordingProvider>().Should().NotBeNull();
			Resolve<IIntakeConversationsRepository>().Should().NotBeNull();
			Resolve<IIntakeConversationMessagesRepository>().Should().NotBeNull();
			Resolve<Resgrid.Model.Providers.IIntakeSmsProvider>().Should().NotBeNull();
			Resolve<IIntakeConversationAssistant>().Should().NotBeNull();
			// One instance per scope serves the desk contract and the intake/worker operations.
			Resolve<IIntakeConversationService>().Should().NotBeNull();
			Resolve<IIntakeConversationOperations>().Should().BeSameAs(Resolve<IIntakeConversationService>());
			// No desk registered here: the routers resolve to none (the desk adds its IIntakeConversationRouter in ServicesModule).
			Resolve<System.Lazy<System.Collections.Generic.IEnumerable<IIntakeConversationRouter>>>().Value.Should().NotBeNull();
		}
	}
}
