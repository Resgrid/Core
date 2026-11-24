using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Resgrid.AdminAssist;
using Resgrid.Ai;
using Resgrid.Config;
using Resgrid.Llm;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Services;
using Resgrid.Search;
using Resgrid.Services.AdminAssist;

namespace Resgrid.Tests.AdminAssist
{
	/// <summary>
	/// Question routing (decision 2026-11-23): the question reaches the model for departments without Advanced Data
	/// Protection; ADP departments send only reviewed-corpus terms and fall back to local search matches.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class AskFocusTests
	{
		private static readonly AdminAssistActor Actor = new(7, "admin", "en");
		private static readonly ConfigurationCatalog Catalog = new();
		private const string Abstain = "{\"evidenceIds\":[],\"abstain\":true}";
		private static AskEvidence Doc(string id = "doc:run-cards") => new(id, "Documentation", "Feature.run-cards", Array.Empty<string>(), new Dictionary<string, decimal>(), "Reference", null, id, "1", null, DateTime.UtcNow, "Run cards body");
		private static AskEvidence Report() => new("setup:report", "Setup", "Ui.report", new[] { "Ui.ReportBoundary" }, new Dictionary<string, decimal> { ["Ui.Required"] = 3 }, "Known", null, "setup:report", "1", "20", DateTime.UtcNow);

		private static (Mock<IAdminAssistAskQueries> Queries, Mock<ILlmClient> Client, List<LlmRequest> Sent) Runner(string output)
		{
			var queries = new Mock<IAdminAssistAskQueries>(); var client = new Mock<ILlmClient>(); var sent = new List<LlmRequest>();
			queries.Setup(q => q.ReadAsync(Actor, It.Is<AskToolInput>(t => t.Name == "get_setup_report"), It.IsAny<CancellationToken>())).ReturnsAsync(new[] { Report() });
			queries.Setup(q => q.ReadAsync(Actor, It.Is<AskToolInput>(t => t.Name == "search_reference"), It.IsAny<CancellationToken>())).ReturnsAsync(new[] { Doc() });
			client.Setup(c => c.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>())).Callback<LlmRequest, CancellationToken>((r, _) => sent.Add(r))
				.ReturnsAsync(new LlmResult(output, Array.Empty<LlmToolCall>(), 400, 20, "stop"));
			return (queries, client, sent);
		}
		private static readonly AskToolInput[] Seeds = { new("get_setup_report"), new("search_reference", Query: "How do I configure Run Cards?") };

		[Test]
		public async Task Question_and_plain_labels_reach_the_model_as_data()
		{
			// Arrange
			var (queries, client, sent) = Runner("{\"evidenceIds\":[\"doc:run-cards\"],\"abstain\":false}");
			var runner = new GroundedAskRunner(client.Object, queries.Object, label: key => key == "Feature.run-cards" ? "Run cards" : key);
			// Act
			var result = await runner.RunAsync(Actor, "local", "setup", Seeds, 32768, CancellationToken.None, AskFocus.FromQuestion("How do I   configure\nRun Cards?"));
			// Assert
			Assert.That(result.Outcome, Is.EqualTo("Answered")); Assert.That(result.Evidence.Single().Id, Is.EqualTo("doc:run-cards"));
			using var opening = JsonDocument.Parse(sent[0].Messages[1].Content);
			Assert.That(opening.RootElement.GetProperty("question").GetString(), Is.EqualTo("How do I configure Run Cards?"));
			Assert.That(opening.RootElement.TryGetProperty("questionTerms", out _), Is.False);
			var doc = opening.RootElement.GetProperty("cards").EnumerateArray().Single(c => c.GetProperty("id").GetString() == "doc:run-cards");
			Assert.That(doc.GetProperty("label").GetString(), Is.EqualTo("Run cards"));
			// A key with no translation sends no label rather than echoing the key.
			Assert.That(opening.RootElement.GetProperty("cards").EnumerateArray().Single(c => c.GetProperty("id").GetString() == "setup:report").TryGetProperty("label", out _), Is.False);
			Assert.That(JsonSerializer.Serialize(sent[0]), Does.Not.Contain("Run cards body"));
		}

		[Test]
		public async Task Terms_focus_never_sends_the_question()
		{
			// Arrange
			var (queries, client, sent) = Runner(Abstain);
			// Act
			await new GroundedAskRunner(client.Object, queries.Object).RunAsync(Actor, "local", "setup", Seeds, 32768, CancellationToken.None, AskFocus.FromTerms(new[] { "configure", "run", "cards", "run" }));
			// Assert
			using var opening = JsonDocument.Parse(sent[0].Messages[1].Content);
			Assert.That(opening.RootElement.TryGetProperty("question", out _), Is.False);
			Assert.That(opening.RootElement.GetProperty("questionTerms").EnumerateArray().Select(t => t.GetString()), Is.EqualTo(new[] { "configure", "run", "cards" }));
		}

		[Test]
		public async Task Abstain_with_search_fallback_returns_fresh_search_matches()
		{
			// Arrange
			var (queries, client, _) = Runner(Abstain);
			// Act
			var result = await new GroundedAskRunner(client.Object, queries.Object).RunAsync(Actor, "local", "setup", Seeds, 32768, CancellationToken.None, AskFocus.FromTerms(new[] { "run" }), searchFallback: true);
			// Assert
			Assert.That(result.Outcome, Is.EqualTo("Matched")); Assert.That(result.Evidence.Select(e => e.Id), Is.EqualTo(new[] { "doc:run-cards" }));
			// One seed read plus one fresh re-read for the fallback.
			queries.Verify(q => q.ReadAsync(Actor, It.Is<AskToolInput>(t => t.Name == "search_reference"), It.IsAny<CancellationToken>()), Times.Exactly(2));
		}

		[Test]
		public async Task Abstain_without_fallback_stays_abstained()
		{
			// Arrange
			var (queries, client, _) = Runner(Abstain);
			// Act
			var result = await new GroundedAskRunner(client.Object, queries.Object).RunAsync(Actor, "local", "setup", Seeds, 32768, CancellationToken.None, AskFocus.FromQuestion("Run cards"));
			// Assert
			Assert.That(result.Outcome, Is.EqualTo("Abstained")); Assert.That(result.Evidence, Is.Empty);
		}

		[Test]
		public async Task Search_fallback_rechecks_access()
		{
			// Arrange: access is revoked between the seed read and the fallback re-read.
			var (queries, client, _) = Runner(Abstain);
			queries.SetupSequence(q => q.ReadAsync(Actor, It.Is<AskToolInput>(t => t.Name == "search_reference"), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new[] { Doc() }).ThrowsAsync(new UnauthorizedAccessException());
			// Act / Assert
			Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await new GroundedAskRunner(client.Object, queries.Object)
				.RunAsync(Actor, "local", "setup", Seeds, 32768, CancellationToken.None, AskFocus.None, searchFallback: true));
		}

		[TestCase("setup", "get_setup_report")][TestCase("settings", "get_setting")][TestCase("permissions", "get_permissions")][TestCase("addons", "compare_addon_capabilities")][TestCase("reference", "search_reference")]
		public async Task Longest_non_latin_question_with_eight_labelled_cards_still_reaches_the_model(string mode, string seed)
		{
			// Arrange: a 1,000-character Greek question plus eight cards with the longest (120-character) Greek labels.
			var queries = new Mock<IAdminAssistAskQueries>(); var client = new Mock<ILlmClient>(); LlmRequest sent = null;
			var cards = Enumerable.Range(0, 8).Select(i => new AskEvidence("task:capability-" + i, "SetupTask", "Feature.capability-" + i, new[] { "Ui.Guidance" },
				new Dictionary<string, decimal>(), "NotStarted", null, "task:" + i, "1", "20", DateTime.UtcNow)).ToArray();
			queries.Setup(q => q.ReadAsync(Actor, It.IsAny<AskToolInput>(), It.IsAny<CancellationToken>())).ReturnsAsync(cards);
			client.Setup(c => c.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>())).Callback<LlmRequest, CancellationToken>((r, _) => sent = r)
				.ReturnsAsync(new LlmResult(Abstain, Array.Empty<LlmToolCall>(), 400, 20, "stop"));
			var runner = new GroundedAskRunner(client.Object, queries.Object, cacheSalt: new string('s', 64), label: _ => new string('Σ', 200));
			// Act
			await runner.RunAsync(Actor, "google/gemma-4-E4B-it", mode, new[] { new AskToolInput(seed) }, 32768, CancellationToken.None, AskFocus.FromQuestion(new string('Ω', 3000)));
			// Assert: sent (not abstained before inference), within the cap, and the question kept at least its first 200 characters.
			Assert.That(sent, Is.Not.Null);
			Assert.That(AdminAssistPrompt.InputBudget(sent), Is.LessThanOrEqualTo(6144));
			using var opening = JsonDocument.Parse(sent.Messages[1].Content);
			Assert.That(opening.RootElement.GetProperty("question").GetString().Length, Is.InRange(200, AskFocus.MaxQuestionLength));
		}

		[Test]
		public void Question_is_capped_and_whitespace_collapsed()
		{
			// Arrange / Act
			var focus = AskFocus.FromQuestion("  a \t b\r\n" + new string('x', 5000));
			// Assert
			Assert.That(focus.Question.Length, Is.EqualTo(AskFocus.MaxQuestionLength)); Assert.That(focus.Question, Does.StartWith("a b x")); Assert.That(focus.Terms, Is.Null);
		}

		[Test]
		public void Reviewed_terms_keep_corpus_words_and_drop_names_and_numbers()
		{
			// Arrange
			using var search = new AdminAssistReferenceSearch(Catalog);
			// Act
			var terms = search.ReviewedTerms("How do I configure Run Cards for Zyxqwv Plormbeck at 5551234567?");
			// Assert
			Assert.That(terms, Does.Contain("run").And.Contain("cards"));
			Assert.That(terms, Does.Not.Contain("zyxqwv").And.Not.Contain("plormbeck").And.Not.Contain("5551234567"));
			Assert.That(terms, Is.Unique);
		}

		private sealed class ServiceFixture
		{
			public readonly Mock<IAiAccessService> Access = new();
			public readonly Mock<IAdminAssistAskQueries> Queries = new();
			public readonly Mock<ILlmClient> Client = new();
			public readonly Mock<IAdminAssistReferenceSearch> Search = new();
			public readonly Mock<IDepartmentDataProtectionService> Protection = new();
			public readonly Mock<IAiUsageMeter> Usage = new();
			public readonly List<LlmRequest> Sent = new();
			public readonly AdminAssistAskService Service;
			public ServiceFixture(string output)
			{
				Access.Setup(a => a.CanUseAdminAssistAsync(Actor, It.IsAny<CancellationToken>(), It.IsAny<bool>())).ReturnsAsync(new AdminAssistAskStatus(true, "Available", 100000));
				Access.Setup(u => u.ReserveTurnAsync(Actor, It.IsAny<AdminAssistAskStatus>(), It.IsAny<CancellationToken>())).ReturnsAsync(new AiUsageReservation("reservation", 7, "admin", 32768, DateTime.UtcNow.AddMinutes(2)));
				var ordinary = new Mock<IAdminAssistAccessService>(); ordinary.Setup(a => a.CanAccessAsync(Actor, false, It.IsAny<CancellationToken>())).ReturnsAsync(true);
				Queries.Setup(q => q.ReadAsync(Actor, It.Is<AskToolInput>(t => t.Name == "search_reference"), It.IsAny<CancellationToken>())).ReturnsAsync(new[] { Doc() });
				Queries.Setup(q => q.ReadAsync(Actor, It.Is<AskToolInput>(t => t.Name != "search_reference"), It.IsAny<CancellationToken>())).ReturnsAsync(new[] { Report() });
				Client.Setup(c => c.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>())).Callback<LlmRequest, CancellationToken>((r, _) => Sent.Add(r))
					.ReturnsAsync(new LlmResult(output, Array.Empty<LlmToolCall>(), 400, 20, "stop"));
				Search.Setup(s => s.ReviewedTerms(It.IsAny<string>(), It.IsAny<int>())).Returns(new[] { "configure", "run", "cards" });
				var store = new Mock<IAdminAssistConversationStore>();
				store.Setup(s => s.SaveAsync(Actor, It.IsAny<AiGenerationRow>(), 0, It.IsAny<CancellationToken>())).Callback<AdminAssistActor, AiGenerationRow, long, CancellationToken>((_, row, _, _) => row.Revision = 1).Returns(Task.CompletedTask);
				Service = new(Access.Object, ordinary.Object, Usage.Object, Queries.Object, Catalog, store.Object, Mock.Of<IAdminAssistConversationProtection>(),
					new Lazy<ILlmClient>(() => Client.Object), TimeProvider.System, Search.Object, Protection.Object);
			}
		}

		private static async Task<T> WithAuditKey<T>(Func<Task<T>> action)
		{
			var old = AiConfig.AuditHmacKey; AiConfig.AuditHmacKey = Convert.ToBase64String(new byte[32]);
			try { return await action(); } finally { AiConfig.AuditHmacKey = old; }
		}

		[Test]
		public async Task Department_without_adp_sends_the_question_and_does_not_fall_back()
		{
			// Arrange
			var f = new ServiceFixture(Abstain);
			f.Protection.Setup(p => p.GetStateAsync(7, true)).ReturnsAsync(DepartmentDataProtectionState.Disabled);
			// Act
			var answer = await WithAuditKey(() => f.Service.AskAsync(Actor, new("How do I configure Run Cards for Jane Doe?"), CancellationToken.None));
			// Assert
			Assert.That(answer.Outcome, Is.EqualTo("Abstained"));
			Assert.That(f.Sent.Single().Messages[1].Content, Does.Contain("How do I configure Run Cards for Jane Doe?"));
			f.Search.Verify(s => s.ReviewedTerms(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
		}

		[TestCase(DepartmentDataProtectionState.Enabled)]
		[TestCase(DepartmentDataProtectionState.EnrollmentQueued)]
		[TestCase(DepartmentDataProtectionState.Decrypting)]
		public async Task Adp_department_sends_only_reviewed_terms_and_falls_back_to_search(DepartmentDataProtectionState state)
		{
			// Arrange
			var f = new ServiceFixture(Abstain);
			f.Protection.Setup(p => p.GetStateAsync(7, true)).ReturnsAsync(state);
			// Act
			var answer = await WithAuditKey(() => f.Service.AskAsync(Actor, new("How do I configure Run Cards for Jane Doe?"), CancellationToken.None));
			// Assert
			Assert.That(answer.Outcome, Is.EqualTo("Matched")); Assert.That(answer.Evidence.Single().Id, Is.EqualTo("doc:run-cards"));
			var sent = JsonSerializer.Serialize(f.Sent.Single());
			Assert.That(sent, Does.Not.Contain("Jane").And.Not.Contain("How do I"));
			Assert.That(f.Sent.Single().Messages[1].Content, Does.Contain("questionTerms"));
			f.Usage.Verify(u => u.CompleteAsync(It.IsAny<AiUsageReservation>(), It.IsAny<int>(), "Matched", It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task Unreadable_adp_state_fails_closed_to_terms()
		{
			// Arrange
			var f = new ServiceFixture(Abstain);
			f.Protection.Setup(p => p.GetStateAsync(7, true)).ThrowsAsync(new TimeoutException());
			// Act
			await WithAuditKey(() => f.Service.AskAsync(Actor, new("Run cards for Jane Doe"), CancellationToken.None));
			// Assert
			Assert.That(JsonSerializer.Serialize(f.Sent.Single()), Does.Not.Contain("Jane"));
		}
	}
}
