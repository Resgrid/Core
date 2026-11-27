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
using Resgrid.Framework;
using Resgrid.Llm;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.AiDispatch;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Services.Knowledge;

namespace Resgrid.Tests.Knowledge
{
	/// <summary>
	/// K1 answers: the gate fails closed, "not covered" stores a gap and calls no model, Advanced Data Protection keeps the question local, an
	/// embeddings outage falls back to keywords, a valid answer cites shown sources and is audited, a computed dose is never shown, and a
	/// thumbs-down becomes a gap. No live model or embeddings endpoint is called.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class AiKnowledgeServiceTests
	{
		private const int DepartmentId = 7;
		private const string Member = "member-1";
		private const string Model = "BAAI/bge-m3";

		private string _endpoint, _apiKey, _model, _revision, _digest, _selfHosted;

		private Mock<IEnhancedAiAccessService> _enhancedAi;
		private Mock<IDepartmentDataProtectionService> _protection;
		private Mock<IKnowledgeRetriever> _retriever;
		private Mock<IEmbeddingProvider> _embeddings;
		private Mock<IKnowledgeIndexService> _index;
		private Mock<IKnowledgeIndexRepository> _indexRepository;
		private Mock<IKnowledgeGapsService> _gaps;
		private Mock<IKnowledgeGapsRepository> _gapsRepository;
		private Mock<IAdminAssistConversationProtection> _conversationProtection;
		private Mock<IAiBackgroundAdmission> _admission;
		private Mock<IAiUsageMeter> _usage;
		private Mock<ILlmClient> _llm;
		private Mock<IDepartmentSettingsRepository> _settings;
		private Mock<IDepartmentsService> _departments;
		private List<KnowledgeGap> _recordedGaps;
		private List<AiGenerationRow> _rows;
		private List<KnowledgeRetrievalRequest> _retrievals;
		private KnowledgeRetrievalResult _found;
		private AiKnowledgeService _service;

		[SetUp]
		public void SetUp()
		{
			(_endpoint, _apiKey, _model, _revision, _digest, _selfHosted) = (AiConfig.Endpoint, AiConfig.ApiKey, AiConfig.Model, AiConfig.ModelRevision, AiConfig.RuntimeDigest, AiConfig.SelfHostedDepartmentIds);
			AiConfig.Endpoint = "https://ai.example.test/v1/chat/completions";
			AiConfig.ApiKey = "test-key";
			AiConfig.Model = "Qwen/Qwen3-8B-AWQ";
			AiConfig.ModelRevision = new string('a', 40);
			AiConfig.RuntimeDigest = "sha256:" + new string('b', 64);
			AiConfig.SelfHostedDepartmentIds = "";

			_enhancedAi = new Mock<IEnhancedAiAccessService>();
			_enhancedAi.Setup(x => x.CanUseAsync(DepartmentId, FeatureFlagKeys.AiKnowledge)).ReturnsAsync(true);
			_protection = new Mock<IDepartmentDataProtectionService>();
			_protection.Setup(x => x.GetStateAsync(DepartmentId, true)).ReturnsAsync(DepartmentDataProtectionState.Disabled);

			_departments = new Mock<IDepartmentsService>();
			_departments.Setup(x => x.GetDepartmentByIdAsync(DepartmentId, false)).ReturnsAsync(new Department { DepartmentId = DepartmentId, ManagingUserId = "owner" });
			_departments.Setup(x => x.GetDepartmentMemberAsync(Member, DepartmentId, false)).ReturnsAsync(new DepartmentMember { DepartmentId = DepartmentId, UserId = Member });
			var roles = new Mock<IPersonnelRolesService>();
			roles.Setup(x => x.GetRolesForUserAsync(Member, DepartmentId)).ReturnsAsync(new List<PersonnelRole> { new PersonnelRole { DepartmentId = DepartmentId, Name = "Paramedic" } });

			_retrievals = new List<KnowledgeRetrievalRequest>();
			_found = Found(true, Chunk(1, "1", "Epinephrine 1 mg/mL: give 0.5 mg IM.", "MED 12"), Chunk(2, "2", "Call medical control.", "MED 13"));
			_retriever = new Mock<IKnowledgeRetriever>();
			_retriever.Setup(x => x.RetrieveAsync(It.IsAny<KnowledgeRetrievalRequest>(), It.IsAny<CancellationToken>()))
				.Callback((KnowledgeRetrievalRequest r, CancellationToken _) => _retrievals.Add(r))
				.ReturnsAsync(() => _found);

			_embeddings = new Mock<IEmbeddingProvider>();
			_embeddings.SetupGet(x => x.IsConfigured).Returns(true);
			_embeddings.SetupGet(x => x.ModelId).Returns(Model);
			_embeddings.Setup(x => x.EmbedAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new List<float[]> { new[] { 1f, 0f } });

			_index = new Mock<IKnowledgeIndexService>();
			_indexRepository = new Mock<IKnowledgeIndexRepository>();
			_indexRepository.Setup(x => x.GetStateAsync(DepartmentId, It.IsAny<CancellationToken>())).ReturnsAsync(new KnowledgeIndexState { DepartmentId = DepartmentId, Active = true });

			_recordedGaps = new List<KnowledgeGap>();
			_gaps = new Mock<IKnowledgeGapsService>();
			_gaps.Setup(x => x.RecordAsync(It.IsAny<KnowledgeGap>(), It.IsAny<CancellationToken>()))
				.Callback((KnowledgeGap g, CancellationToken _) => _recordedGaps.Add(g)).ReturnsAsync((KnowledgeGap g, CancellationToken _) => g);

			_rows = new List<AiGenerationRow>();
			_gapsRepository = new Mock<IKnowledgeGapsRepository>();
			_gapsRepository.Setup(x => x.InsertGenerationAsync(It.IsAny<AiGenerationRow>(), It.IsAny<CancellationToken>()))
				.Callback((AiGenerationRow r, CancellationToken _) => _rows.Add(r)).Returns(Task.CompletedTask);

			_conversationProtection = new Mock<IAdminAssistConversationProtection>();
			_conversationProtection.Setup(x => x.ProtectAsync(It.IsAny<AdminAssistActor>(), It.IsAny<AiGenerationRow>(), It.IsAny<CancellationToken>()))
				.Callback((AdminAssistActor _, AiGenerationRow row, CancellationToken __) => row.Content = "enc2:" + row.Content).Returns(Task.CompletedTask);

			_admission = new Mock<IAiBackgroundAdmission>();
			_admission.Setup(x => x.ReserveBackgroundAsync(DepartmentId, AiKnowledgeService.Feature, It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<int>(),
				It.IsAny<CancellationToken>(), It.IsAny<int?>())).ReturnsAsync(new AiUsageReservation("r1", DepartmentId, "background", AiKnowledgeService.ReservedTokens, DateTime.UtcNow.AddMinutes(2)));
			_usage = new Mock<IAiUsageMeter>();
			_usage.Setup(x => x.RemainingAsync(DepartmentId, It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(1_000_000);

			_llm = new Mock<ILlmClient>();
			_settings = new Mock<IDepartmentSettingsRepository>();

			_service = new AiKnowledgeService(_enhancedAi.Object, Mock.Of<IFeatureToggleService>(), _departments.Object, roles.Object, _protection.Object,
				Array.Empty<IKnowledgeCorpusSource>(), _retriever.Object, _embeddings.Object, _index.Object, _indexRepository.Object, _gaps.Object, _gapsRepository.Object,
				_conversationProtection.Object, _admission.Object, _usage.Object, new Lazy<ILlmClient>(() => _llm.Object), _settings.Object, Mock.Of<IDepartmentSettingsService>(),
				Mock.Of<IEventAggregator>(), Mock.Of<IAuthorizationService>(), new Lazy<ICallsService>(() => Mock.Of<ICallsService>()), Mock.Of<IProtocolVersionsRepository>(),
				Array.Empty<IKnowledgeVersionContentSource>(), Mock.Of<IKnowledgeCallContextService>(), TimeProvider.System);
		}

		[TearDown]
		public void TearDown()
		{
			(AiConfig.Endpoint, AiConfig.ApiKey, AiConfig.Model, AiConfig.ModelRevision, AiConfig.RuntimeDigest, AiConfig.SelfHostedDepartmentIds) = (_endpoint, _apiKey, _model, _revision, _digest, _selfHosted);
		}

		private static KnowledgeChunk Chunk(long id, string itemId, string text, string title) => new KnowledgeChunk
		{
			KnowledgeChunkId = id, DepartmentId = DepartmentId, SourceKind = (int)KnowledgeSourceKinds.Protocol, ItemId = itemId, VersionId = "5" + itemId,
			VersionLabel = "2.1", Anchor = "p-dose", HeadingPath = "Adult", Title = title, Text = text, EffectiveOn = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc)
		};

		private static KnowledgeRetrievalResult Found(bool adequate, params KnowledgeChunk[] chunks) => new KnowledgeRetrievalResult
		{
			Adequate = adequate,
			Mode = KnowledgeRetrievalModes.Hybrid,
			Candidates = chunks.Length,
			Selected = chunks.Select(c => new KnowledgeScoredChunk { Chunk = c, Score = 1 }).ToList()
		};

		private static KnowledgeAskRequest Ask(string question = "What epinephrine dose for an adult?") => new KnowledgeAskRequest
		{
			Reader = new KnowledgeReader { DepartmentId = DepartmentId, UserId = Member, CanViewProtocols = true, CanViewTrainings = true },
			Question = question,
			Surface = (int)KnowledgeAskSurfaces.Library
		};

		private void ModelAnswers(object arguments) =>
			_llm.Setup(x => x.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new LlmResult(null, new[] { new LlmToolCall("t1", AiKnowledgePrompt.ToolName, JsonSerializer.Serialize(arguments)) }, 1200, 80, "tool_calls"));

		private void NoModelCall() => _llm.Verify(x => x.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()), Times.Never);

		[Test]
		public async Task The_gate_fails_closed_and_nothing_is_retrieved_or_sent()
		{
			_enhancedAi.Setup(x => x.CanUseAsync(DepartmentId, FeatureFlagKeys.AiKnowledge)).ReturnsAsync(false);

			(await _service.AskAsync(Ask())).Outcome.Should().Be(KnowledgeOutcomes.NotEntitled);
			(await _service.GetStatusAsync(Ask().Reader)).Available.Should().BeFalse();

			_retriever.Verify(x => x.RetrieveAsync(It.IsAny<KnowledgeRetrievalRequest>(), It.IsAny<CancellationToken>()), Times.Never);
			_embeddings.Verify(x => x.EmbedAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Never);
			NoModelCall();
		}

		[Test]
		public async Task A_gate_that_throws_is_not_entitled()
		{
			_enhancedAi.Setup(x => x.CanUseAsync(DepartmentId, FeatureFlagKeys.AiKnowledge)).ThrowsAsync(new InvalidOperationException("billing down"));
			(await _service.IsEntitledAsync(DepartmentId)).Should().BeFalse();
		}

		[Test]
		public async Task An_unconfigured_model_and_a_non_member_are_refused_before_retrieval()
		{
			AiConfig.ModelRevision = "";
			(await _service.AskAsync(Ask())).Outcome.Should().Be(KnowledgeOutcomes.Unconfigured);
			AiConfig.ModelRevision = new string('a', 40);

			_departments.Setup(x => x.GetDepartmentMemberAsync(Member, DepartmentId, false)).ReturnsAsync(new DepartmentMember { DepartmentId = DepartmentId, UserId = Member, IsDeleted = true });
			(await _service.AskAsync(Ask())).Outcome.Should().Be(KnowledgeOutcomes.NotEntitled);
			_retriever.Verify(x => x.RetrieveAsync(It.IsAny<KnowledgeRetrievalRequest>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Not_covered_shows_the_nearest_items_stores_a_gap_and_calls_no_model()
		{
			_found = Found(false, Chunk(1, "1", "Stage uphill.", "SOG 4"), Chunk(2, "2", "Traffic control.", "SOG 9"));

			var answer = await _service.AskAsync(Ask("How do we handle a lithium battery fire?"));

			answer.Outcome.Should().Be(KnowledgeOutcomes.NotCovered);
			answer.Nearest.Select(n => n.Title).Should().Equal("SOG 4", "SOG 9");
			answer.Parts.Should().BeEmpty();
			NoModelCall();

			var gap = _recordedGaps.Single();
			gap.Reason.Should().Be((int)KnowledgeGapReasons.NotCovered);
			gap.Question.Should().Be("How do we handle a lithium battery fire?");
			gap.AskerRole.Should().Be("Paramedic");
			gap.Surface.Should().Be((int)KnowledgeAskSurfaces.Library);
			gap.NearestSources.Select(s => s.Title).Should().Equal("SOG 4", "SOG 9");
			gap.AiGenerationId.Should().Be(answer.ExchangeId).And.NotBeNull();

			var row = _rows.Single();
			row.Outcome.Should().Be(KnowledgeOutcomes.NotCovered);
			row.PromptVersion.Should().Be(AiKnowledgePrompt.Version);
			row.Content.Should().StartWith("enc2:");
		}

		[Test]
		public async Task Advanced_Data_Protection_keeps_the_question_local_sources_only_and_no_question_text_in_gaps()
		{
			_protection.Setup(x => x.GetStateAsync(DepartmentId, true)).ReturnsAsync(DepartmentDataProtectionState.Enabled);

			var answer = await _service.AskAsync(Ask());

			answer.Outcome.Should().Be(KnowledgeOutcomes.Matched);
			answer.QuestionKeptLocal.Should().BeTrue();
			answer.Citations.Should().HaveCount(2);
			_embeddings.Verify(x => x.EmbedAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Never);
			_retrievals.Single().QueryVector.Should().BeNull();
			NoModelCall();

			_found = Found(false, Chunk(1, "1", "Stage uphill.", "SOG 4"));
			await _service.AskAsync(Ask("Patient John Smith, what now?"));
			_recordedGaps.Single().Question.Should().BeNull();
		}

		[Test]
		public async Task An_embeddings_outage_falls_back_to_keywords_and_says_so()
		{
			_embeddings.Setup(x => x.EmbedAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
				.ThrowsAsync(new EmbeddingUnavailableException("timeout"));
			ModelAnswers(new { parts = new[] { new { text = "Give 0.5 mg IM.", sources = new[] { "S1" } } }, not_covered = false, conflicts = Array.Empty<object>() });

			var answer = await _service.AskAsync(Ask());

			answer.EmbeddingsUnavailable.Should().BeTrue();
			answer.Outcome.Should().Be(KnowledgeOutcomes.Answered);
			_retrievals.Single().QueryVector.Should().BeNull();
		}

		[Test]
		public async Task A_valid_answer_cites_its_sources_quotes_the_dose_links_the_dosing_tool_and_is_audited()
		{
			_settings.Setup(x => x.GetDepartmentSettingByIdTypeAsync(DepartmentId, DepartmentSettingTypes.KnowledgeSettings)).ReturnsAsync(new DepartmentSetting
			{
				DepartmentId = DepartmentId,
				Setting = ObjectSerialization.Serialize(new KnowledgeSettings { DosingToolUrl = "https://dosing.example.test/" })
			});
			ModelAnswers(new { parts = new[] { new { text = "Give 0.5 mg IM of epinephrine 1 mg/mL.", sources = new[] { "S1" } } }, not_covered = false, conflicts = Array.Empty<object>() });

			var answer = await _service.AskAsync(Ask());

			answer.Outcome.Should().Be(KnowledgeOutcomes.Answered);
			answer.Parts.Single().Refs.Should().Equal("S1");
			answer.Citations.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Ref = "S1", ItemId = "1", VersionId = "51", VersionLabel = "2.1", Anchor = "p-dose" },
				o => o.ExcludingMissingMembers());
			answer.QuotesDose.Should().BeTrue();
			answer.DosingToolUrl.Should().Be("https://dosing.example.test/");
			_retrievals.Single().QueryVector.Should().Equal(1f, 0f);

			var row = _rows.Single();
			row.Outcome.Should().Be(KnowledgeOutcomes.Answered);
			row.ModelRevision.Should().Be(AiConfig.ModelRevision);
			row.RuntimeDigest.Should().Be(AiConfig.RuntimeDigest);
			row.InputTokens.Should().Be(1200);
			row.Id.Should().Be(answer.ExchangeId);
			_usage.Verify(x => x.CompleteAsync(It.IsAny<AiUsageReservation>(), 1280, "Answered", It.IsAny<CancellationToken>()), Times.Once);

			// The model only ever sees the retrieved sources and the question, delimited.
			_llm.Verify(x => x.CompleteAsync(It.Is<LlmRequest>(r => r.Messages.Count == 2 && r.Messages[1].Content.Contains("[S1] MED 12") && r.Messages[1].Content.Contains("<<<QUESTION")),
				It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task A_computed_dose_is_never_shown_the_sources_are()
		{
			ModelAnswers(new { parts = new[] { new { text = "For 70 kg give 0.7 mg.", sources = new[] { "S1" } } }, not_covered = false, conflicts = Array.Empty<object>() });

			var answer = await _service.AskAsync(Ask());

			answer.Outcome.Should().Be(KnowledgeOutcomes.Matched);
			answer.Parts.Should().BeEmpty();
			answer.Citations.Should().HaveCount(2);
			_usage.Verify(x => x.CompleteAsync(It.IsAny<AiUsageReservation>(), It.IsAny<int>(), "InvalidOutput", It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task The_model_saying_not_covered_stores_a_gap()
		{
			ModelAnswers(new { parts = Array.Empty<object>(), not_covered = true, conflicts = Array.Empty<object>() });

			var answer = await _service.AskAsync(Ask());

			answer.Outcome.Should().Be(KnowledgeOutcomes.NotCovered);
			_recordedGaps.Should().ContainSingle(g => g.Reason == (int)KnowledgeGapReasons.NotCovered);
		}

		[Test]
		public async Task An_exchange_that_cannot_be_audited_is_never_sent_to_the_model()
		{
			_conversationProtection.Setup(x => x.PreflightAsync(It.IsAny<AdminAssistActor>(), It.IsAny<CancellationToken>())).ThrowsAsync(new UnauthorizedAccessException());

			var answer = await _service.AskAsync(Ask());

			answer.Outcome.Should().Be(KnowledgeOutcomes.Unconfigured);
			answer.Citations.Should().HaveCount(2);
			NoModelCall();
		}

		[Test]
		public async Task First_use_queues_the_department_index()
		{
			_indexRepository.Setup(x => x.GetStateAsync(DepartmentId, It.IsAny<CancellationToken>())).ReturnsAsync((KnowledgeIndexState)null);
			_found = Found(false);

			await _service.AskAsync(Ask());

			_index.Verify(x => x.RequestRebuildAsync(DepartmentId, null, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task A_thumbs_down_on_ones_own_exchange_becomes_a_gap_and_a_thumbs_up_stores_nothing()
		{
			var exchange = Guid.NewGuid().ToString("D");
			_gapsRepository.Setup(x => x.GetGenerationAsync(DepartmentId, Member, exchange, It.IsAny<CancellationToken>()))
				.ReturnsAsync(new AiGenerationRow { Id = exchange, DepartmentId = DepartmentId, UserId = Member, PromptVersion = AiKnowledgePrompt.Version, Content = "enc2:x" });
			_conversationProtection.Setup(x => x.ReadAsync(It.IsAny<AdminAssistActor>(), It.IsAny<AiGenerationRow>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new AskStoredContent("Which PPE for hazmat?", Array.Empty<AskToolInput>(),
					new[] { JsonSerializer.Serialize(new { SourceKind = 0, ItemId = "4", VersionId = "9", VersionLabel = "1.2", Anchor = "p-ppe", Title = "SOG 4" }) }));
			var reader = Ask().Reader;

			(await _service.FeedbackAsync(reader, exchange, true, (int)KnowledgeAskSurfaces.Library)).Should().BeTrue();
			_recordedGaps.Should().BeEmpty();

			(await _service.FeedbackAsync(reader, exchange, false, (int)KnowledgeAskSurfaces.Library)).Should().BeTrue();
			var gap = _recordedGaps.Single();
			gap.Reason.Should().Be((int)KnowledgeGapReasons.ThumbsDown);
			gap.Question.Should().Be("Which PPE for hazmat?");
			gap.NearestSources.Single().Title.Should().Be("SOG 4");
			gap.AiGenerationId.Should().Be(exchange);

			(await _service.FeedbackAsync(reader, Guid.NewGuid().ToString("D"), false, 0)).Should().BeFalse("not the reader's exchange");
		}

		[Test]
		public async Task The_Enhanced_AI_gate_for_knowledge_is_flags_then_module_then_add_on_and_fails_closed()
		{
			var (billing, key) = (SystemBehaviorConfig.BillingApiBaseUrl, ApiConfig.BackendInternalApikey);
			try
			{
				SystemBehaviorConfig.BillingApiBaseUrl = "https://billing.example.test";
				ApiConfig.BackendInternalApikey = "internal";
				var flags = new Mock<IFeatureToggleService>();
				flags.Setup(x => x.EvaluateFreshAsync(FeatureFlagKeys.AiEnhanced, DepartmentId)).ReturnsAsync(new FeatureFlagEvaluation { IsEnabled = true });
				flags.Setup(x => x.EvaluateFreshAsync(FeatureFlagKeys.AiKnowledge, DepartmentId)).ReturnsAsync(new FeatureFlagEvaluation { IsEnabled = true });
				var settings = new Mock<IDepartmentSettingsService>();
				var modules = new DepartmentModuleSettings();
				settings.Setup(x => x.GetDepartmentModuleSettingsAsync(DepartmentId, true)).ReturnsAsync(() => modules);
				var subscriptions = new Mock<ISubscriptionsService>();
				subscriptions.Setup(x => x.GetAllAddonPlansByTypeAsync(PlanAddonTypes.EnhancedAi, It.IsAny<bool>()))
					.ReturnsAsync(new List<PlanAddon> { new PlanAddon { PlanAddonId = "ai", AddonType = (int)PlanAddonTypes.EnhancedAi } });
				var payments = new List<PaymentAddon> { new PaymentAddon { DepartmentId = DepartmentId, PlanAddonId = "ai", EffectiveOn = DateTime.UtcNow.AddDays(-1), EndingOn = DateTime.UtcNow.AddDays(20), TransactionId = "tx" } };
				subscriptions.Setup(x => x.GetCurrentPaymentAddonsForDepartmentAsync(DepartmentId, It.IsAny<List<string>>())).ReturnsAsync(() => payments);
				var gate = new EnhancedAiAccessService(flags.Object, settings.Object, subscriptions.Object, Mock.Of<IDepartmentDataProtectionService>());

				(await gate.CanUseAsync(DepartmentId, FeatureFlagKeys.AiKnowledge)).Should().BeTrue();

				flags.Setup(x => x.EvaluateFreshAsync(FeatureFlagKeys.AiKnowledge, DepartmentId)).ReturnsAsync(new FeatureFlagEvaluation { IsEnabled = false });
				(await gate.CanUseAsync(DepartmentId, FeatureFlagKeys.AiKnowledge)).Should().BeFalse("the Ai.Knowledge flag is off");
				flags.Setup(x => x.EvaluateFreshAsync(FeatureFlagKeys.AiKnowledge, DepartmentId)).ReturnsAsync(new FeatureFlagEvaluation { IsEnabled = true });

				modules = new DepartmentModuleSettings { AiDisabled = true };
				(await gate.CanUseAsync(DepartmentId, FeatureFlagKeys.AiKnowledge)).Should().BeFalse("the department's AI module is off");
				modules = new DepartmentModuleSettings();

				payments = new List<PaymentAddon>();
				(await gate.CanUseAsync(DepartmentId, FeatureFlagKeys.AiKnowledge)).Should().BeFalse("the add-on is not active");
			}
			finally
			{
				(SystemBehaviorConfig.BillingApiBaseUrl, ApiConfig.BackendInternalApikey) = (billing, key);
			}
		}
	}
}
