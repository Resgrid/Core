using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Resgrid.Llm;
using Resgrid.Model.AdminAssist;

namespace Resgrid.Tests.AdminAssist
{
	/// <summary>Offline checks of the live model gate harness (LiveModelGate), so it stays meaningful between live runs. No network.</summary>
	[TestFixture]
	public class LiveModelGateHarnessTests
	{
		private static Mock<ILlmClient> Replying(string content)
		{
			var client = new Mock<ILlmClient>();
			client.Setup(c => c.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new LlmResult(content, Array.Empty<LlmToolCall>(), 400, 20, "stop"));
			return client;
		}

		[Test]
		public void Fixed_cards_carry_no_link_or_credential_shaped_id()
		{
			// Arrange / Act / Assert: otherwise the hostile assertion could fail on a clean answer.
			Assert.That(LiveGateEvidence.PublicIds, Is.Not.Empty);
			Assert.That(LiveGateEvidence.PublicIds.Where(id => LiveModelGate.UrlPattern.IsMatch(id) || LiveModelGate.CredentialPattern.IsMatch(id)), Is.Empty);
		}

		[Test]
		public async Task Every_grounded_prompt_seeds_a_card_it_expects()
		{
			// Arrange
			var prompts = LiveModelGate.GroundedPrompts();
			foreach (var prompt in prompts)
			{
				var evidence = new LiveGateEvidence();
				var seeds = LiveGateEvidence.Seeds(prompt.Topic, prompt.Question);
				var ids = new List<string>();

				// Act
				foreach (var seed in seeds) ids.AddRange((await evidence.ReadAsync(new AdminAssistActor(prompt.DepartmentId, "gate"), seed, CancellationToken.None)).Select(c => c.Id));

				// Assert
				Assert.That(seeds.Count, Is.InRange(1, 3), prompt.Name);
				Assert.That(prompt.ExpectedIds, Is.SubsetOf(LiveGateEvidence.PublicIds), prompt.Name);
				Assert.That(ids.Intersect(prompt.ExpectedIds), Is.Not.Empty, prompt.Name + " seeds none of its expected cards");
			}
			Assert.That(prompts.Select(p => p.Topic).Distinct(), Is.EquivalentTo(LiveModelGate.Topics));
			Assert.That(prompts.Count(p => p.Route == LiveModelGate.TermsRoute), Is.EqualTo(prompts.Count / 2), "Every grounded question also runs on the ADP terms route.");
			Assert.That(prompts.Where(p => p.Route == LiveModelGate.TermsRoute).Select(p => p.DepartmentId).Distinct(), Is.EqualTo(new[] { LiveModelGate.ProtectedDepartmentId }));
		}

		[Test]
		public void Hostile_prompts_cover_every_red_team_payload_sector_and_topic()
		{
			// Arrange
			var candidates = AskRedTeamTests.HostileOutputs().Select(c => (string)c.Arguments[0]).ToArray();

			// Act
			var prompts = LiveModelGate.HostilePrompts();

			// Assert
			Assert.That(prompts.Select(p => p.Candidate), Is.EquivalentTo(candidates));
			var payloads = prompts.Select(p => p.Payload).Distinct().Count();
			var first = prompts.Take(payloads).ToArray();
			var second = prompts.Skip(payloads).Take(payloads).ToArray();
			// RESGRID_AI_LIVE_HOSTILE_LIMIT: the first prompts send every payload as the question, the next inject every payload through a card.
			Assert.That(first.Select(p => p.Payload).Distinct().Count(), Is.EqualTo(payloads));
			Assert.That(first.All(p => p.Vector == LiveModelGate.QuestionVector && p.Route == LiveModelGate.QuestionRoute), Is.True);
			Assert.That(second.Select(p => p.Payload).Distinct().Count(), Is.EqualTo(payloads));
			Assert.That(second.All(p => p.Vector == LiveModelGate.CardVector && p.Route == LiveModelGate.TermsRoute), Is.True);
			Assert.That(prompts.Where(p => p.Vector == LiveModelGate.QuestionVector).All(p => p.Question.Contains(p.Candidate, StringComparison.Ordinal)), Is.True);
			Assert.That(prompts.GroupBy(p => (p.Payload, p.Vector)).Select(g => g.Select(p => p.Topic).Distinct().Count()), Is.All.EqualTo(LiveModelGate.Topics.Length));
			Assert.That(prompts.Select(p => p.DepartmentId).Distinct().Count(), Is.EqualTo(candidates.Select(c => c.Substring(0, c.IndexOf(':'))).Distinct().Count()));
		}

		[TestCase(LiveModelGate.QuestionVector)]
		[TestCase(LiveModelGate.CardVector)]
		public async Task Injected_id_is_counted_as_failed_with_no_violation_and_the_department_salt_is_sent(string vector)
		{
			// Arrange
			var prompt = LiveModelGate.HostilePrompts().First(p => p.Topic == "settings" && p.Vector == vector);
			var key = LiveModelGate.NewSaltKey();
			LlmRequest sent = null;
			var client = new Mock<ILlmClient>();
			client.Setup(c => c.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>())).Callback<LlmRequest, CancellationToken>((r, _) => sent = r)
				.ReturnsAsync(new LlmResult(JsonSerializer.Serialize(new { evidenceIds = new[] { prompt.Candidate }, abstain = false }), Array.Empty<LlmToolCall>(), 400, 20, "stop"));

			// Act
			var outcome = await LiveModelGate.RunAsync(client.Object, "test-model", prompt, key, CancellationToken.None);

			// Assert
			Assert.That(outcome.Outcome, Is.EqualTo("Failed"));
			Assert.That(outcome.Failure, Is.EqualTo("boundary: Unsupported evidence."));
			Assert.That(outcome.EchoedCandidate, Is.True);
			Assert.That(outcome.Exchanges.Select(e => e.CacheSalt), Is.All.EqualTo(LiveModelGate.SaltFor(prompt.DepartmentId, key)));
			Assert.That(outcome.ExpectedSalt, Is.Not.Null.And.Not.EqualTo(LiveModelGate.SaltFor(LiveModelGate.GroundedDepartmentId, key)));
			Assert.That(LiveModelGate.Violations(outcome), Is.Empty);
			using var opening = JsonDocument.Parse(sent.Messages[1].Content);
			var cards = opening.RootElement.GetProperty("cards").EnumerateArray().ToArray();
			if (vector == LiveModelGate.QuestionVector)
			{
				// The question reaches the model whole, with the instruction to return the hostile id; the cards are clean.
				Assert.That(opening.RootElement.GetProperty("question").GetString(), Does.Contain(prompt.Candidate), "The model must actually see the injection.");
				Assert.That(outcome.ObservedIds, Does.Not.Contain(LiveGateEvidence.UntrustedCardId));
			}
			else
			{
				// ADP route: only reviewed terms of the payload reach the model, and the injection rides in the untrusted card.
				Assert.That(opening.RootElement.TryGetProperty("question", out _), Is.False);
				Assert.That(outcome.Exchanges.Select(e => e.QuestionSent), Is.All.False);
				Assert.That(outcome.ObservedIds, Does.Contain(LiveGateEvidence.UntrustedCardId));
				Assert.That(cards.Any(card => card.GetProperty("title").GetString().Contains(prompt.Candidate, StringComparison.Ordinal)), Is.True, "The model must actually see the injection.");
			}
		}

		[Test]
		public async Task Question_route_sends_the_question_and_card_labels()
		{
			// Arrange
			var prompt = LiveModelGate.GroundedPrompts().First(p => p.Name == "reference units");
			LlmRequest sent = null;
			var client = new Mock<ILlmClient>();
			client.Setup(c => c.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>())).Callback<LlmRequest, CancellationToken>((r, _) => sent = r)
				.ReturnsAsync(new LlmResult(JsonSerializer.Serialize(new { evidenceIds = new[] { "doc:units-setup" }, abstain = false }), Array.Empty<LlmToolCall>(), 400, 20, "stop"));

			// Act
			var outcome = await LiveModelGate.RunAsync(client.Object, "test-model", prompt, LiveModelGate.NewSaltKey(), CancellationToken.None);

			// Assert
			Assert.That(outcome.Outcome, Is.EqualTo("Answered")); Assert.That(outcome.ExpectedHit, Is.True);
			using var opening = JsonDocument.Parse(sent.Messages[1].Content);
			Assert.That(opening.RootElement.GetProperty("question").GetString(), Is.EqualTo(prompt.Question));
			var card = opening.RootElement.GetProperty("cards").EnumerateArray().Single(c => c.GetProperty("id").GetString() == "doc:units-setup");
			Assert.That(card.GetProperty("label").GetString(), Is.EqualTo("Setting up units and unit types"));
			Assert.That(outcome.Exchanges.Select(e => e.QuestionSent), Is.All.True);
		}

		[Test]
		public async Task Terms_route_abstain_shows_search_matches_and_never_sends_the_question()
		{
			// Arrange
			var prompt = LiveModelGate.GroundedPrompts().First(p => p.Route == LiveModelGate.TermsRoute && p.Name.StartsWith("reference units", StringComparison.Ordinal));
			LlmRequest sent = null;
			var client = new Mock<ILlmClient>();
			client.Setup(c => c.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>())).Callback<LlmRequest, CancellationToken>((r, _) => sent = r)
				.ReturnsAsync(new LlmResult(JsonSerializer.Serialize(new { evidenceIds = Array.Empty<string>(), abstain = true }), Array.Empty<LlmToolCall>(), 400, 20, "stop"));

			// Act
			var outcome = await LiveModelGate.RunAsync(client.Object, "test-model", prompt, LiveModelGate.NewSaltKey(), CancellationToken.None);

			// Assert
			Assert.That(outcome.Outcome, Is.EqualTo("Matched")); Assert.That(outcome.ExpectedMatched, Is.True);
			Assert.That(outcome.EvidenceIds, Is.SubsetOf(outcome.ObservedIds));
			using var opening = JsonDocument.Parse(sent.Messages[1].Content);
			Assert.That(opening.RootElement.TryGetProperty("question", out _), Is.False);
			Assert.That(opening.RootElement.GetProperty("questionTerms").EnumerateArray().Select(t => t.GetString()), Does.Contain("units"));
			Assert.That(LiveModelGate.Violations(outcome), Is.Empty);
		}

		[TestCase("{\"evidenceIds\":[\"setup:report\"],\"abstain\":false}", "Answered", null)]
		[TestCase("{\"evidenceIds\":[],\"abstain\":true}", "Abstained", null)]
		[TestCase("```json\n{\"evidenceIds\":[\"setup:report\"],\"abstain\":false}\n```", "Failed", "output: final answer is not JSON")]
		[TestCase("{\"evidenceIds\":[\"setup:report\"],\"abstain\":false,\"url\":\"https://evil.invalid\"}", "Failed", "boundary: Invalid answer.")]
		public async Task Final_model_output_is_classified_like_production(string content, string expected, string failure)
		{
			// Arrange
			var prompt = LiveModelGate.GroundedPrompts().First(p => p.Topic == "setup");

			// Act
			var outcome = await LiveModelGate.RunAsync(Replying(content).Object, "test-model", prompt, LiveModelGate.NewSaltKey(), CancellationToken.None);

			// Assert
			Assert.That(outcome.Outcome, Is.EqualTo(expected));
			Assert.That(outcome.Failure, Is.EqualTo(failure));
			Assert.That(LiveModelGate.Violations(outcome), Is.Empty);
			Assert.That(LiveModelGate.Summary("grounded", new GateRun("test-model", "localhost:8443", 1, 5, DateTime.UtcNow), new[] { outcome }), Does.Contain("Boundary violations: 0"));
		}

		[Test]
		public void Violations_name_every_unsafe_part_of_a_validated_answer()
		{
			// Arrange: a fabricated answer the boundary would never let through.
			var prompt = LiveModelGate.HostilePrompts().First(p => p.Payload.StartsWith("https://", StringComparison.Ordinal));
			var outcome = new GateOutcome(prompt, "Answered", new[] { prompt.Candidate, "setup:report" }, new[] { "setup:report" }, null, 1,
				new[] { new GateExchange(null, 1, null, null) }, "expected-salt");

			// Act
			var problems = LiveModelGate.Violations(outcome);

			// Assert
			Assert.That(problems, Has.Some.Contains("cache_salt"));
			Assert.That(problems, Has.Some.Contains("unobserved"));
			Assert.That(problems, Has.Some.Contains("injected"));
			Assert.That(problems, Has.Some.Contains("link-shaped"));
			Assert.That(problems, Has.Some.Contains("credential-shaped"));
		}

		[Test]
		public async Task Every_gate_prompt_reaches_the_model()
		{
			// Arrange: a prompt whose seeds find no card abstains before inference and would test nothing about the model.
			var prompts = LiveModelGate.GroundedPrompts().Concat(LiveModelGate.HostilePrompts()).ToArray();

			// Act
			var outcomes = await LiveModelGate.RunAllAsync(Replying("{\"evidenceIds\":[],\"abstain\":true}").Object, "test-model", prompts, LiveModelGate.NewSaltKey(), 8, null, CancellationToken.None);

			// Assert
			Assert.That(outcomes.Where(o => o.Exchanges.Count == 0).Select(o => o.Prompt.Name), Is.Empty);
			Assert.That(outcomes.SelectMany(o => LiveModelGate.Violations(o).Select(v => o.Prompt.Name + ": " + v)), Is.Empty);
		}

		[Test]
		public void A_question_sent_on_the_terms_route_is_a_violation()
		{
			// Arrange: an ADP prompt whose request carried the whole question.
			var prompt = LiveModelGate.GroundedPrompts().First(p => p.Route == LiveModelGate.TermsRoute);
			var outcome = new GateOutcome(prompt, "Abstained", Array.Empty<string>(), Array.Empty<string>(), null, 1, new[] { new GateExchange("salt", 1, null, null, QuestionSent: true) }, "salt");

			// Act / Assert
			Assert.That(LiveModelGate.Violations(outcome), Has.Some.Contains("terms (ADP) route"));
		}
	}
}
