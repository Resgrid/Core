using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Chatbot.Models;
using Resgrid.Chatbot.Services;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Tests.Knowledge
{
	/// <summary>K1 in the Assistant chat: a question no intent matched goes to knowledge answers first when the department has Ai.Knowledge.</summary>
	[TestFixture]
	public class KnowledgeChatbotFallbackTests
	{
		private const int DepartmentId = 7;

		private Mock<IChatbotConversationalFallback> _inner;
		private Mock<IAiKnowledgeService> _knowledge;
		private KnowledgeChatbotFallback _fallback;

		[SetUp]
		public void SetUp()
		{
			_inner = new Mock<IChatbotConversationalFallback>();
			_inner.Setup(x => x.TryHandleAsync(It.IsAny<ChatbotMessage>(), It.IsAny<ChatbotSession>())).ReturnsAsync(new ChatbotResponse { Text = "inner" });
			_knowledge = new Mock<IAiKnowledgeService>();
			_knowledge.Setup(x => x.IsEntitledAsync(DepartmentId)).ReturnsAsync(true);
			_knowledge.Setup(x => x.AskAsync(It.IsAny<KnowledgeAskRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new KnowledgeAnswer
			{
				Outcome = KnowledgeOutcomes.Answered,
				Parts = new List<KnowledgeAnswerPart> { new KnowledgeAnswerPart { Text = "Stage uphill and upwind.", Refs = new List<string> { "S1" } } },
				Citations = new List<KnowledgeCitation>
				{
					new KnowledgeCitation { Ref = "S1", SourceKind = 0, ItemId = "4", VersionId = "40", VersionLabel = "2.1", Anchor = "p-scene-safety", Title = "SOG 4", HeadingPath = "Scene safety" }
				}
			});
			_fallback = new KnowledgeChatbotFallback(_inner.Object, _knowledge.Object);
		}

		private static ChatbotSession Session() => new ChatbotSession { DepartmentId = DepartmentId, UserId = "member-1", Culture = "en" };

		[TestCase("Where do we stage for hazmat?", true)]
		[TestCase("how do we stage for a hazmat call", true)]
		[TestCase("¿Dónde nos posicionamos?", true)]
		[TestCase("responding", false)]
		[TestCase("thanks", false)]
		[TestCase("", false)]
		public void Questions_are_told_apart_from_remarks(string text, bool question)
		{
			KnowledgeChatbotFallback.LooksLikeQuestion(text).Should().Be(question);
		}

		[Test]
		public async Task An_entitled_departments_question_is_answered_from_its_knowledge_with_sources()
		{
			var response = await _fallback.TryHandleAsync(new ChatbotMessage { Text = "Where do we stage for hazmat?" }, Session());

			response.Text.Should().Contain("Stage uphill and upwind. [S1]");
			response.Text.Should().Contain("S1 SOG 4 - Scene safety");
			response.Text.Should().Contain("/User/Protocols/View/4?versionId=40#p-scene-safety");
			_knowledge.Verify(x => x.AskAsync(It.Is<KnowledgeAskRequest>(r => r.Surface == (int)KnowledgeAskSurfaces.Assistant && r.Reader.UserId == "member-1"),
				It.IsAny<CancellationToken>()), Times.Once);
			_inner.Verify(x => x.TryHandleAsync(It.IsAny<ChatbotMessage>(), It.IsAny<ChatbotSession>()), Times.Never);
		}

		[Test]
		public async Task Remarks_unentitled_departments_and_incident_boards_go_to_the_usual_fallback()
		{
			(await _fallback.TryHandleAsync(new ChatbotMessage { Text = "thanks" }, Session())).Text.Should().Be("inner");

			var board = Session();
			board.Context[IncidentContextResolver.IncidentCallIdContextKey] = "12";
			(await _fallback.TryHandleAsync(new ChatbotMessage { Text = "Who is in division A?" }, board)).Text.Should().Be("inner");

			_knowledge.Setup(x => x.IsEntitledAsync(DepartmentId)).ReturnsAsync(false);
			(await _fallback.TryHandleAsync(new ChatbotMessage { Text = "Where do we stage for hazmat?" }, Session())).Text.Should().Be("inner");

			_knowledge.Verify(x => x.AskAsync(It.IsAny<KnowledgeAskRequest>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Busy_or_failing_knowledge_falls_through_and_not_covered_says_so()
		{
			_knowledge.Setup(x => x.AskAsync(It.IsAny<KnowledgeAskRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new KnowledgeAnswer { Outcome = KnowledgeOutcomes.Busy });
			(await _fallback.TryHandleAsync(new ChatbotMessage { Text = "Where do we stage?" }, Session())).Text.Should().Be("inner");

			_knowledge.Setup(x => x.AskAsync(It.IsAny<KnowledgeAskRequest>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("down"));
			(await _fallback.TryHandleAsync(new ChatbotMessage { Text = "Where do we stage?" }, Session())).Text.Should().Be("inner");

			KnowledgeChatbotFallback.Format(new KnowledgeAnswer { Outcome = KnowledgeOutcomes.NotCovered }, "en")
				.Should().Be("Your department's protocols and training don't cover this.");
			KnowledgeChatbotFallback.Format(new KnowledgeAnswer { Outcome = KnowledgeOutcomes.NotCovered }, "de")
				.Should().Be("Die Protokolle und Schulungen Ihrer Abteilung decken das nicht ab.");
		}
	}
}
