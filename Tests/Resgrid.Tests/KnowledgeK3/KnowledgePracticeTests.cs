using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Ai;
using Resgrid.Config;
using Resgrid.Llm;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.AiDispatch;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services.Knowledge;

namespace Resgrid.Tests.KnowledgeK3
{
	/// <summary>
	/// K3 tutor, scenario practice: the gate fails closed (Ai.Knowledge, the LMS add-on, the protocols view claim, membership, the model,
	/// Advanced Data Protection, "no AI" protocols); the scenario and the scores are validated by K1's number-in-source rule; scoring reads the
	/// stored scenario (never the client's copy) and refuses a changed protocol; and practice never writes a learning record, a certification
	/// or a quiz attempt. No live model is called.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class KnowledgePracticeTests
	{
		private const int Dept = 7;
		private const string Member = "member-1";
		private const int ProtocolId = 1;

		private string _endpoint, _apiKey, _model, _revision, _digest;
		private Mock<IAiKnowledgeService> _knowledge;
		private Mock<ILmsAccessService> _lms;
		private Mock<IDepartmentDataProtectionService> _protection;
		private Mock<IProtocolAccessService> _access;
		private Mock<IProtocolVersionsRepository> _versions;
		private Mock<ILlmClient> _llm;
		private Mock<IKnowledgeGapsRepository> _generations;
		private List<AiGenerationRow> _rows;
		private List<LlmRequest> _sent;
		private DispatchProtocol _protocol;
		private ProtocolVersion _version;
		private KnowledgePracticeService _service;

		[SetUp]
		public void SetUp()
		{
			(_endpoint, _apiKey, _model, _revision, _digest) = (AiConfig.Endpoint, AiConfig.ApiKey, AiConfig.Model, AiConfig.ModelRevision, AiConfig.RuntimeDigest);
			AiConfig.Endpoint = "https://ai.example.test/v1/chat/completions";
			AiConfig.ApiKey = "test-key";
			AiConfig.Model = "Qwen/Qwen3-8B-AWQ";
			AiConfig.ModelRevision = new string('a', 40);
			AiConfig.RuntimeDigest = "sha256:" + new string('b', 64);

			_knowledge = new Mock<IAiKnowledgeService>();
			_knowledge.Setup(k => k.IsEntitledAsync(Dept)).ReturnsAsync(true);
			_lms = new Mock<ILmsAccessService>();
			_lms.Setup(l => l.CanRunItemAsync(Dept, LmsRunnableOrigins.Department, null)).ReturnsAsync(true);
			var departments = new Mock<IDepartmentsService>();
			departments.Setup(x => x.GetDepartmentByIdAsync(Dept, false)).ReturnsAsync(new Department { DepartmentId = Dept, ManagingUserId = "owner" });
			departments.Setup(x => x.GetDepartmentMemberAsync(Member, Dept, false)).ReturnsAsync(new DepartmentMember { DepartmentId = Dept, UserId = Member });
			_protection = new Mock<IDepartmentDataProtectionService>();
			_protection.Setup(x => x.GetStateAsync(Dept, true)).ReturnsAsync(DepartmentDataProtectionState.Disabled);

			_protocol = new DispatchProtocol { DispatchProtocolId = ProtocolId, DepartmentId = Dept, Name = "Cardiac arrest", Status = (int)ProtocolStatuses.Published, CurrentVersionId = 11 };
			_version = new ProtocolVersion
			{
				ProtocolVersionId = 11, DispatchProtocolId = ProtocolId, DepartmentId = Dept, Status = (int)ProtocolVersionStatuses.Published, Major = 3, Minor = 1,
				EffectiveOn = DateTime.UtcNow.AddDays(-5),
				ContentHtml = "<h2>Compressions</h2><p>Compress at 100 to 120 per minute, 5 to 6 cm deep.</p><h2>Medications</h2><p>Epinephrine 1 mg IV every 3 to 5 minutes.</p>"
			};
			_access = new Mock<IProtocolAccessService>();
			_access.Setup(a => a.GetReadableAsync(Dept, Member, ProtocolId)).ReturnsAsync(() => _protocol);
			_versions = new Mock<IProtocolVersionsRepository>();
			_versions.Setup(v => v.GetByIdsAsync(Dept, It.IsAny<IEnumerable<int>>(), true)).ReturnsAsync(() => new List<ProtocolVersion> { _version });

			var protection = new Mock<IAdminAssistConversationProtection>();
			protection.Setup(p => p.ReadAsync(It.IsAny<AdminAssistActor>(), It.IsAny<AiGenerationRow>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((AdminAssistActor _, AiGenerationRow row, CancellationToken __) => JsonSerializer.Deserialize<AskStoredContent>(row.Content));
			var admission = new Mock<IAiBackgroundAdmission>();
			admission.Setup(x => x.ReserveBackgroundAsync(Dept, KnowledgePracticeService.Feature, It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<int>(),
				It.IsAny<CancellationToken>(), It.IsAny<int?>())).ReturnsAsync(new AiUsageReservation("r1", Dept, "background", KnowledgePracticeService.ReservedTokens, DateTime.UtcNow.AddMinutes(2)));
			var usage = new Mock<IAiUsageMeter>();
			usage.Setup(x => x.RemainingAsync(Dept, It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(1_000_000);

			_rows = new List<AiGenerationRow>();
			_generations = new Mock<IKnowledgeGapsRepository>(MockBehavior.Strict);
			_generations.Setup(g => g.InsertGenerationAsync(It.IsAny<AiGenerationRow>(), It.IsAny<CancellationToken>())).Callback((AiGenerationRow r, CancellationToken _) => _rows.Add(r))
				.Returns(Task.CompletedTask);
			_generations.Setup(g => g.GetGenerationAsync(Dept, Member, It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int d, string u, string id, CancellationToken _) => _rows.FirstOrDefault(r => r.Id == id));

			_sent = new List<LlmRequest>();
			_llm = new Mock<ILlmClient>();
			_service = new KnowledgePracticeService(_knowledge.Object, _lms.Object, departments.Object, _protection.Object, _access.Object, _versions.Object, protection.Object,
				admission.Object, usage.Object, new Lazy<ILlmClient>(() => _llm.Object), _generations.Object, Mock.Of<IDepartmentSettingsRepository>(), TimeProvider.System);
		}

		[TearDown]
		public void TearDown() => (AiConfig.Endpoint, AiConfig.ApiKey, AiConfig.Model, AiConfig.ModelRevision, AiConfig.RuntimeDigest) = (_endpoint, _apiKey, _model, _revision, _digest);

		private static KnowledgeReader Reader(bool protocols = true) => new KnowledgeReader { DepartmentId = Dept, UserId = Member, CanViewProtocols = protocols, CanViewTrainings = true };

		private void Model(string tool, object arguments) =>
			_llm.Setup(x => x.CompleteAsync(It.Is<LlmRequest>(r => r.Tools.Any(t => t.Name == tool)), It.IsAny<CancellationToken>()))
				.Callback((LlmRequest r, CancellationToken _) => _sent.Add(r))
				.ReturnsAsync(new LlmResult(null, new[] { new LlmToolCall("t1", tool, JsonSerializer.Serialize(arguments)) }, 800, 90, "tool_calls"));

		private void ScenarioReply() => Model(AiTutorPrompt.ScenarioTool, new
		{
			scenario = "You arrive to find an adult in cardiac arrest with bystander CPR in progress.",
			questions = new[]
			{
				new { question = "At what rate do you compress?", sources = new[] { "S1" } },
				new { question = "Which medication is given and how often?", sources = new[] { "S2" } }
			}
		});

		private void NoModelCall() => _llm.Verify(x => x.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()), Times.Never);

		#region Gates

		[Test]
		public async Task Every_gate_fails_closed_before_any_model_call()
		{
			ScenarioReply();

			_knowledge.Setup(k => k.IsEntitledAsync(Dept)).ReturnsAsync(false);
			(await _service.StartAsync(Reader(), ProtocolId)).Outcome.Should().Be(KnowledgeOutcomes.NotEntitled, "Ai.Knowledge off");
			(await _service.GetStatusAsync(Reader())).Available.Should().BeFalse();
			_knowledge.Setup(k => k.IsEntitledAsync(Dept)).ReturnsAsync(true);

			_lms.Setup(l => l.CanRunItemAsync(Dept, LmsRunnableOrigins.Department, null)).ReturnsAsync(false);
			(await _service.StartAsync(Reader(), ProtocolId)).Outcome.Should().Be(KnowledgeOutcomes.NotEntitled, "the tutor needs the LMS add-on (§17.2 row AI)");
			_lms.Setup(l => l.CanRunItemAsync(Dept, LmsRunnableOrigins.Department, null)).ThrowsAsync(new InvalidOperationException("billing down"));
			(await _service.StartAsync(Reader(), ProtocolId)).Outcome.Should().Be(KnowledgeOutcomes.NotEntitled, "an unreadable gate is closed");
			_lms.Setup(l => l.CanRunItemAsync(Dept, LmsRunnableOrigins.Department, null)).ReturnsAsync(true);

			(await _service.StartAsync(Reader(protocols: false), ProtocolId)).Outcome.Should().Be(KnowledgeOutcomes.NotEntitled, "no Protocols view claim");

			AiConfig.ModelRevision = "";
			(await _service.StartAsync(Reader(), ProtocolId)).Outcome.Should().Be(KnowledgeOutcomes.Unconfigured);
			AiConfig.ModelRevision = new string('a', 40);

			_protection.Setup(x => x.GetStateAsync(Dept, true)).ReturnsAsync(DepartmentDataProtectionState.Enabled);
			(await _service.StartAsync(Reader(), ProtocolId)).Outcome.Should().Be(KnowledgePracticeOutcomes.KeptLocal);
			(await _service.GetStatusAsync(Reader())).Reason.Should().Be(KnowledgePracticeOutcomes.KeptLocal);
			_protection.Setup(x => x.GetStateAsync(Dept, true)).ReturnsAsync(DepartmentDataProtectionState.Disabled);

			_protocol.ExcludeFromAi = true;
			(await _service.StartAsync(Reader(), ProtocolId)).Outcome.Should().Be(KnowledgePracticeOutcomes.Excluded);
			_protocol.ExcludeFromAi = false;
			_protocol.IsLicensedContent = true;
			(await _service.StartAsync(Reader(), ProtocolId)).Outcome.Should().Be(KnowledgePracticeOutcomes.Excluded);
			_protocol.IsLicensedContent = false;

			_access.Setup(a => a.GetReadableAsync(Dept, Member, ProtocolId)).ReturnsAsync((DispatchProtocol)null);
			(await _service.StartAsync(Reader(), ProtocolId)).Outcome.Should().Be(KnowledgePracticeOutcomes.NotFound, "a protocol the member cannot open");

			NoModelCall();
			_rows.Should().BeEmpty();
		}

		#endregion Gates

		#region Scenario and scoring

		[Test]
		public async Task A_scenario_cites_the_sections_and_is_scored_against_them_only()
		{
			ScenarioReply();
			var scenario = await _service.StartAsync(Reader(), ProtocolId);

			scenario.Outcome.Should().Be(KnowledgePracticeOutcomes.Ready);
			scenario.Questions.Select(q => q.Key).Should().Equal("Q1", "Q2");
			scenario.Citations.Select(c => c.Anchor).Should().OnlyContain(a => a.StartsWith("p-"));
			_rows.Single().PromptVersion.Should().Be(AiTutorPrompt.ScenarioVersion);

			Model(AiTutorPrompt.ScoreTool, new
			{
				items = new[]
				{
					new { key = "Q1", score = 2, feedback = "Right: 100 to 120 per minute.", sources = new[] { "S1" } },
					new { key = "Q2", score = 1, feedback = "Epinephrine 1 mg IV, but every 3 to 5 minutes, not 2.", sources = new[] { "S2" } }
				}
			});
			var scored = await _service.ScoreAsync(Reader(), scenario.ExchangeId, new[]
			{
				new KnowledgePracticeAnswer { Key = "Q1", Text = "110 a minute" },
				new KnowledgePracticeAnswer { Key = "Q2", Text = "Epi every 2 minutes" }
			});

			scored.Outcome.Should().Be(KnowledgePracticeOutcomes.Scored);
			scored.Total.Should().Be(3);
			scored.Max.Should().Be(4);
			scored.Items.Single(i => i.Key == "Q2").Feedback.Should().Contain("not 2", "the learner's own number may be quoted back");
			scored.QuotesDose.Should().BeTrue();
			var scoringPrompt = string.Join("\n", _sent.Last().Messages.Select(m => m.Content));
			scoringPrompt.Should().Contain("You arrive to find an adult", "the stored scenario, not the client's copy");
			_rows.Last().PromptVersion.Should().Be(AiTutorPrompt.ScoreVersion);
			JsonSerializer.Deserialize<AskStoredContent>(_rows.Last().Content).Question.Should().NotContain("Epi every 2 minutes", "the member's answers are not stored");
		}

		[Test]
		public async Task A_scenario_with_a_number_the_protocol_does_not_hold_is_never_shown()
		{
			Model(AiTutorPrompt.ScenarioTool, new
			{
				scenario = "A 70 kg adult collapses.",
				questions = new[] { new { question = "What dose do you give?", sources = new[] { "S2" } } }
			});

			var scenario = await _service.StartAsync(Reader(), ProtocolId);

			scenario.Outcome.Should().Be(KnowledgePracticeOutcomes.Refused);
			scenario.Scenario.Should().BeNull();
			scenario.Questions.Should().BeEmpty();
			_rows.Single().Outcome.Should().Be(KnowledgePracticeOutcomes.Refused);
		}

		[Test]
		public async Task Feedback_with_a_computed_dose_is_left_unscored()
		{
			ScenarioReply();
			var scenario = await _service.StartAsync(Reader(), ProtocolId);
			Model(AiTutorPrompt.ScoreTool, new
			{
				items = new[]
				{
					new { key = "Q1", score = 2, feedback = "Right: 100 to 120 per minute.", sources = new[] { "S1" } },
					new { key = "Q2", score = 2, feedback = "For 70 kg give 0.7 mg.", sources = new[] { "S2" } }
				}
			});

			var scored = await _service.ScoreAsync(Reader(), scenario.ExchangeId, new[] { new KnowledgePracticeAnswer { Key = "Q1", Text = "110" }, new KnowledgePracticeAnswer { Key = "Q2", Text = "epi" } });

			scored.Outcome.Should().Be(KnowledgePracticeOutcomes.Scored);
			var q2 = scored.Items.Single(i => i.Key == "Q2");
			q2.Score.Should().BeNull();
			q2.Feedback.Should().BeNull();
			scored.Total.Should().Be(2);
		}

		[Test]
		public async Task Scoring_refuses_a_changed_protocol_someone_elses_scenario_and_empty_answers()
		{
			ScenarioReply();
			var scenario = await _service.StartAsync(Reader(), ProtocolId);
			Model(AiTutorPrompt.ScoreTool, new { items = new[] { new { key = "Q1", score = 2, feedback = "Right: 100 to 120 per minute.", sources = new[] { "S1" } } } });

			(await _service.ScoreAsync(Reader(), scenario.ExchangeId, new[] { new KnowledgePracticeAnswer { Key = "Q1", Text = " " } })).Outcome.Should().Be(KnowledgeOutcomes.Invalid);
			(await _service.ScoreAsync(Reader(), Guid.NewGuid().ToString("D"), new[] { new KnowledgePracticeAnswer { Key = "Q1", Text = "110" } }))
				.Outcome.Should().Be(KnowledgePracticeOutcomes.NotFound, "only the member's own stored scenario is scored");

			_version.ContentHtml = "<h2>Compressions</h2><p>Compress at 110 per minute.</p>";
			(await _service.ScoreAsync(Reader(), scenario.ExchangeId, new[] { new KnowledgePracticeAnswer { Key = "Q1", Text = "110" } }))
				.Outcome.Should().Be(KnowledgePracticeOutcomes.Stale, "the cited section changed since the scenario was written");
		}

		[Test]
		public void Practice_never_writes_learning_records_certifications_attempts_or_enrollments()
		{
			var forbidden = new[]
			{
				typeof(ILearningRecordWriter), typeof(ILearningRecordService), typeof(ICertificationService), typeof(ITrainingService), typeof(ITrainingAssignmentService),
				typeof(ILmsLearnerService), typeof(ILmsAssessmentService), typeof(ILmsCourseResults), typeof(IProtocolAcknowledgmentService), typeof(ILearningRecordsRepository),
				typeof(Resgrid.Model.Repositories.ILmsEnrollmentsRepository), typeof(Resgrid.Model.Repositories.ILmsAssessmentAttemptsRepository)
			};
			typeof(KnowledgePracticeService).GetConstructors().SelectMany(c => c.GetParameters()).Select(p => p.ParameterType).Should().NotContain(forbidden);
			typeof(IKnowledgePracticeService).GetMethods().Select(m => m.Name).Should().BeEquivalentTo(new[] { "GetStatusAsync", "StartAsync", "ScoreAsync" });
		}

		[Test]
		public async Task A_full_practice_run_writes_nothing_but_its_two_audit_rows()
		{
			ScenarioReply();
			var scenario = await _service.StartAsync(Reader(), ProtocolId);
			Model(AiTutorPrompt.ScoreTool, new { items = new[] { new { key = "Q1", score = 2, feedback = "Right: 100 to 120 per minute.", sources = new[] { "S1" } } } });
			await _service.ScoreAsync(Reader(), scenario.ExchangeId, new[] { new KnowledgePracticeAnswer { Key = "Q1", Text = "110" } });

			// The strict audit store saw two inserts and the read-back, nothing else; no other store is reachable from the service.
			_rows.Should().HaveCount(2);
			_generations.Verify(g => g.InsertGenerationAsync(It.IsAny<AiGenerationRow>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
			_generations.Verify(g => g.GetGenerationAsync(Dept, Member, scenario.ExchangeId, It.IsAny<CancellationToken>()), Times.Once);
		}

		#endregion Scenario and scoring
	}
}
