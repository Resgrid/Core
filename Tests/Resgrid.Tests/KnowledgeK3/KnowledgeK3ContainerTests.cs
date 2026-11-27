using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Tests.KnowledgeK3
{
	/// <summary>K3 registrations resolve from the real container (a missing one fails here, not at application start).</summary>
	[TestFixture]
	public class KnowledgeK3ContainerTests : TestBase
	{
		[Test]
		public void The_K3_services_resolve_and_courses_join_the_corpus()
		{
			Resolve<IAiKnowledgeService>().Should().NotBeNull();
			Resolve<IKnowledgePracticeService>().Should().NotBeNull();
			Resolve<ILmsRecommendationService>().Should().NotBeNull();
			Resolve<IKnowledgeCallContextService>().Should().NotBeNull();
			Resolve<IKnowledgeCallDeterminantSource>().Should().NotBeNull();
			ResolveAll<IKnowledgeCorpusSource>().Select(s => s.GetType().Name).Should().Contain("LmsCourseKnowledgeSource").And.Contain("LmsQuestionExplanationKnowledgeSource");
			Resolve<ILmsQuestionBankService>().Should().NotBeNull();
			ResolveAll<IKnowledgeVersionContentSource>().SelectMany(s => s.VersionKinds).Should().Contain(KnowledgeSourceKinds.Course);
		}
	}
}
