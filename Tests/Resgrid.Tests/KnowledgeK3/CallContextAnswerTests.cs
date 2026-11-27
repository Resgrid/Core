using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services.Knowledge;

namespace Resgrid.Tests.KnowledgeK3
{
	/// <summary>
	/// K3 "Ask about this call" and "Ask this course" through the knowledge service: the call's operational fields are an allow-list and no
	/// protected call field (caller, address, nature, notes, contacts, form answers, protocol answers) ever reaches the model; every cited
	/// version grounds the answer; a number from the call's own fields may be quoted but a computed one is refused; "not covered" goes to the
	/// gaps report; the course scope runs through its version source and its LMS gate. No live model or embeddings endpoint is called.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class CallContextAnswerTests
	{
		private const int Dept = 7;
		private const string Member = "member-1";
		private const int CallId = 500;

		private string _endpoint, _apiKey, _model, _revision, _digest, _selfHosted;
		private Mock<IEnhancedAiAccessService> _enhancedAi;
		private Mock<IDepartmentDataProtectionService> _protection;
		private Mock<IKnowledgeRetriever> _retriever;
		private Mock<ILlmClient> _llm;
		private Mock<IAuthorizationService> _authorization;
		private Mock<ICallsService> _calls;
		private Mock<IKnowledgeIndexRepository> _index;
		private Mock<IProtocolVersionsRepository> _versions;
		private Mock<IMappingService> _mapping;
		private Mock<IKnowledgeCallDeterminantSource> _determinant;
		private Mock<IKnowledgeVersionContentSource> _courseSource;
		private List<KnowledgeGap> _gaps;
		private List<AiGenerationRow> _rows;
		private List<KnowledgeRetrievalRequest> _retrievals;
		private List<LlmRequest> _sent;
		private Call _call;
		private List<CallProtocol> _callProtocols;
		private AiKnowledgeService _service;

		/// <summary>Every protected call field carries a sentinel that must never reach a prompt.</summary>
		private static Call SentinelCall() => new Call
		{
			CallId = CallId, DepartmentId = Dept, Type = "Structure Fire", Priority = 3, LocationPoiId = 44,
			Name = "SENTINEL-NAME Jane Patient", NatureOfCall = "SENTINEL-NATURE 54yo female chest pain", Notes = "SENTINEL-NOTES", CompletedNotes = "SENTINEL-COMPLETED",
			Address = "SENTINEL-ADDRESS 12 Elm St", GeoLocationData = "39.123,-104.456", ContactName = "SENTINEL-CONTACT", ContactNumber = "SENTINEL-PHONE 555-0100",
			IncidentNumber = "SENTINEL-INCIDENT", ReferenceNumber = "SENTINEL-REFERENCE", ExternalIdentifier = "SENTINEL-EXTERNAL", CallFormData = "{\"patient\":\"SENTINEL-FORM\"}",
			MapPage = "SENTINEL-MAPPAGE", W3W = "SENTINEL.WHAT.WORDS", Number = "SENTINEL-NUMBER", SourceIdentifier = "SENTINEL-SOURCE", ReportingUserId = "SENTINEL-REPORTER",
			DeletedReason = "SENTINEL-DELETED", IndoorMapZoneId = "SENTINEL-ZONE", IndoorMapFloorId = "SENTINEL-FLOOR", ShortenedAudioUrl = "SENTINEL-AUDIO",
			ShortenedCallUrl = "SENTINEL-URL", LoggedOn = DateTime.UtcNow
		};

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
			_enhancedAi.Setup(x => x.CanUseAsync(Dept, FeatureFlagKeys.AiKnowledge)).ReturnsAsync(true);
			_protection = new Mock<IDepartmentDataProtectionService>();
			_protection.Setup(x => x.GetStateAsync(Dept, true)).ReturnsAsync(DepartmentDataProtectionState.Disabled);
			var departments = new Mock<IDepartmentsService>();
			departments.Setup(x => x.GetDepartmentByIdAsync(Dept, false)).ReturnsAsync(new Department { DepartmentId = Dept, ManagingUserId = "owner" });
			departments.Setup(x => x.GetDepartmentMemberAsync(Member, Dept, false)).ReturnsAsync(new DepartmentMember { DepartmentId = Dept, UserId = Member });
			var roles = new Mock<IPersonnelRolesService>();
			roles.Setup(x => x.GetRolesForUserAsync(Member, Dept)).ReturnsAsync(new List<PersonnelRole>());

			_retrievals = new List<KnowledgeRetrievalRequest>();
			_retriever = new Mock<IKnowledgeRetriever>();
			_retriever.Setup(x => x.RetrieveAsync(It.IsAny<KnowledgeRetrievalRequest>(), It.IsAny<CancellationToken>()))
				.Callback((KnowledgeRetrievalRequest r, CancellationToken _) => _retrievals.Add(r))
				.ReturnsAsync((KnowledgeRetrievalRequest r, CancellationToken _) => new KnowledgeRetrievalResult
				{
					Adequate = (r.Chunks ?? new List<KnowledgeChunk>()).Count > 0,
					Mode = KnowledgeRetrievalModes.Hybrid,
					Selected = (r.Chunks ?? new List<KnowledgeChunk>()).Where(c => KnowledgeAccessFilter.CanRead(r.Scope, c)).Take(6)
						.Select(c => new KnowledgeScoredChunk { Chunk = c, Score = 1 }).ToList()
				});

			var embeddings = new Mock<IEmbeddingProvider>();
			embeddings.SetupGet(x => x.IsConfigured).Returns(false);

			_index = new Mock<IKnowledgeIndexRepository>();
			_index.Setup(x => x.GetStateAsync(Dept, It.IsAny<CancellationToken>())).ReturnsAsync(new KnowledgeIndexState { DepartmentId = Dept, Active = true });
			_index.Setup(x => x.GetProtocolHeadersAsync(Dept, It.IsAny<CancellationToken>())).ReturnsAsync(new List<DispatchProtocol>
			{
				new DispatchProtocol { DispatchProtocolId = 1, DepartmentId = Dept, Name = "Structure fire", Code = "SF1", Status = (int)ProtocolStatuses.Published, CurrentVersionId = 11 },
				new DispatchProtocol { DispatchProtocolId = 2, DepartmentId = Dept, Name = "Schools", Code = "SC", Status = (int)ProtocolStatuses.Published, CurrentVersionId = 21 },
				new DispatchProtocol { DispatchProtocolId = 3, DepartmentId = Dept, Name = "Vendor", Status = (int)ProtocolStatuses.Published, CurrentVersionId = 31, ExcludeFromAi = true }
			});
			_index.Setup(x => x.GetItemChunksAsync(Dept, It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<KnowledgeChunk>());
			_versions = new Mock<IProtocolVersionsRepository>();
			_versions.Setup(x => x.GetByIdsAsync(Dept, It.IsAny<IEnumerable<int>>(), true)).ReturnsAsync(new List<ProtocolVersion>
			{
				new ProtocolVersion { ProtocolVersionId = 10, DispatchProtocolId = 1, DepartmentId = Dept, Status = (int)ProtocolVersionStatuses.Superseded, Major = 1, Minor = 0,
					ContentHtml = "<h2>Staging</h2><p>Stage engines 300 feet from the building.</p>" },
				new ProtocolVersion { ProtocolVersionId = 21, DispatchProtocolId = 2, DepartmentId = Dept, Status = (int)ProtocolVersionStatuses.Published, Major = 2, Minor = 1,
					ContentHtml = "<h2>Schools</h2><p>Meet the school's incident liaison at the main office.</p>" },
				new ProtocolVersion { ProtocolVersionId = 31, DispatchProtocolId = 3, DepartmentId = Dept, Status = (int)ProtocolVersionStatuses.Published, Major = 1, Minor = 0,
					ContentHtml = "<p>VENDOR-TEXT</p>" },
				new ProtocolVersion { ProtocolVersionId = 40, DispatchProtocolId = 1, DepartmentId = Dept, Status = (int)ProtocolVersionStatuses.Published, Major = 2, Minor = 0,
					ContentHtml = "<p>REMOVED-ROW-TEXT</p>" }
			});

			_call = SentinelCall();
			_callProtocols = new List<CallProtocol>
			{
				new CallProtocol { CallProtocolId = 1, CallId = CallId, DispatchProtocolId = 1, ProtocolVersionId = 10, AnswersJson = "{\"q\":\"SENTINEL-ANSWERS\"}", Data = "SENTINEL-DATA" },
				new CallProtocol { CallProtocolId = 2, CallId = CallId, DispatchProtocolId = 2, ProtocolVersionId = 21 },
				new CallProtocol { CallProtocolId = 3, CallId = CallId, DispatchProtocolId = 3, ProtocolVersionId = 31 },
				new CallProtocol { CallProtocolId = 4, CallId = CallId, DispatchProtocolId = 1, ProtocolVersionId = 40, RemovedOn = DateTime.UtcNow, RemovedReason = "SENTINEL-REMOVAL", StepLogJson = "SENTINEL-STEPLOG" }
			};
			_calls = new Mock<ICallsService>();
			_calls.Setup(x => x.GetCallByIdAsync(CallId, It.IsAny<bool>())).ReturnsAsync(() => _call);
			_calls.Setup(x => x.GetCallProtocolsByCallIdAsync(CallId)).ReturnsAsync(() => _callProtocols);
			_calls.Setup(x => x.GetCallPrioritiesForDepartmentAsync(Dept, It.IsAny<bool>())).ReturnsAsync(new List<DepartmentCallPriority>
			{
				new DepartmentCallPriority { DepartmentCallPriorityId = 3, DepartmentId = Dept, Name = "Emergency" }
			});
			_authorization = new Mock<IAuthorizationService>();
			_authorization.Setup(x => x.CanUserViewCallAsync(Member, CallId)).ReturnsAsync(true);
			_mapping = new Mock<IMappingService>();
			_mapping.Setup(x => x.GetPOIByIdAsync(44)).ReturnsAsync(new Poi { PoiId = 44, PoiTypeId = 5, Name = "SENTINEL-POI-NAME", Address = "SENTINEL-POI-ADDRESS", Note = "SENTINEL-POI-NOTE" });
			_mapping.Setup(x => x.GetPOITypesForDepartmentAsync(Dept)).ReturnsAsync(new List<PoiType> { new PoiType { PoiTypeId = 5, DepartmentId = Dept, Name = "School" } });
			_determinant = new Mock<IKnowledgeCallDeterminantSource>();
			_determinant.Setup(x => x.GetDeterminantAsync(Dept, CallId, It.IsAny<CancellationToken>())).ReturnsAsync("69-D-6");
			_courseSource = new Mock<IKnowledgeVersionContentSource>();
			_courseSource.SetupGet(x => x.VersionKinds).Returns(new[] { KnowledgeSourceKinds.Course });

			_gaps = new List<KnowledgeGap>();
			var gaps = new Mock<IKnowledgeGapsService>();
			gaps.Setup(x => x.RecordAsync(It.IsAny<KnowledgeGap>(), It.IsAny<CancellationToken>())).Callback((KnowledgeGap g, CancellationToken _) => _gaps.Add(g))
				.ReturnsAsync((KnowledgeGap g, CancellationToken _) => g);
			_rows = new List<AiGenerationRow>();
			var gapsRepository = new Mock<IKnowledgeGapsRepository>();
			gapsRepository.Setup(x => x.InsertGenerationAsync(It.IsAny<AiGenerationRow>(), It.IsAny<CancellationToken>())).Callback((AiGenerationRow r, CancellationToken _) => _rows.Add(r))
				.Returns(Task.CompletedTask);
			gapsRepository.Setup(x => x.GetGenerationAsync(Dept, Member, It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int d, string u, string id, CancellationToken _) => _rows.FirstOrDefault(r => r.Id == id));
			var protection = new Mock<IAdminAssistConversationProtection>();
			var admission = new Mock<IAiBackgroundAdmission>();
			admission.Setup(x => x.ReserveBackgroundAsync(Dept, AiKnowledgeService.Feature, It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<int>(),
				It.IsAny<CancellationToken>(), It.IsAny<int?>())).ReturnsAsync(new AiUsageReservation("r1", Dept, "background", AiKnowledgeService.ReservedTokens, DateTime.UtcNow.AddMinutes(2)));
			var usage = new Mock<IAiUsageMeter>();
			usage.Setup(x => x.RemainingAsync(Dept, It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(1_000_000);

			_sent = new List<LlmRequest>();
			_llm = new Mock<ILlmClient>();
			Answer(new { parts = new[] { new { text = "Meet the incident liaison at the main office.", sources = new[] { "S1" } } }, not_covered = false, conflicts = Array.Empty<object>() });

			var lazyCalls = new Lazy<ICallsService>(() => _calls.Object);
			var context = new KnowledgeCallContextService(lazyCalls, _mapping.Object, _determinant.Object);
			_service = new AiKnowledgeService(_enhancedAi.Object, Mock.Of<IFeatureToggleService>(), departments.Object, roles.Object, _protection.Object,
				Array.Empty<IKnowledgeCorpusSource>(), _retriever.Object, embeddings.Object, Mock.Of<IKnowledgeIndexService>(), _index.Object, gaps.Object, gapsRepository.Object,
				protection.Object, admission.Object, usage.Object, new Lazy<ILlmClient>(() => _llm.Object), Mock.Of<IDepartmentSettingsRepository>(), Mock.Of<IDepartmentSettingsService>(),
				Mock.Of<IEventAggregator>(), _authorization.Object, lazyCalls, _versions.Object, new[] { _courseSource.Object }, context, TimeProvider.System);
		}

		[TearDown]
		public void TearDown() =>
			(AiConfig.Endpoint, AiConfig.ApiKey, AiConfig.Model, AiConfig.ModelRevision, AiConfig.RuntimeDigest, AiConfig.SelfHostedDepartmentIds) = (_endpoint, _apiKey, _model, _revision, _digest, _selfHosted);

		private void Answer(object arguments) =>
			_llm.Setup(x => x.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
				.Callback((LlmRequest r, CancellationToken _) => _sent.Add(r))
				.ReturnsAsync(new LlmResult(null, new[] { new LlmToolCall("t1", AiKnowledgePrompt.ToolName, JsonSerializer.Serialize(arguments)) }, 900, 60, "tool_calls"));

		private static KnowledgeAskRequest CallAsk(string question = "Where do we meet the school?") => new KnowledgeAskRequest
		{
			Reader = new KnowledgeReader { DepartmentId = Dept, UserId = Member, CanViewProtocols = true, CanViewTrainings = true },
			Question = question,
			Surface = (int)KnowledgeAskSurfaces.Call,
			CallId = CallId
		};

		#region The allow-list

		[Test]
		public void The_call_context_carries_only_the_allowed_operational_fields()
		{
			var properties = typeof(KnowledgeCallContext).GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanWrite).Select(p => p.Name);
			properties.Should().BeEquivalentTo(KnowledgeCallContext.AllowedFields, "a new field on the call context is a privacy decision, not a refactor");
			KnowledgeCallContext.AllowedFields.Should().BeEquivalentTo("CallType", "Priority", "Determinant", "LocationType");
		}

		[Test]
		public async Task The_context_reads_type_priority_determinant_and_the_location_type_and_nothing_protected()
		{
			var context = await new KnowledgeCallContextService(new Lazy<ICallsService>(() => _calls.Object), _mapping.Object, _determinant.Object).BuildAsync(_call);

			context.CallType.Should().Be("Structure Fire");
			context.Priority.Should().Be("Emergency");
			context.Determinant.Should().Be("69-D-6");
			context.LocationType.Should().Be("School");
			context.ToPromptText().Should().NotContain("SENTINEL");

			// The P4 seam is optional: the default reader gives no determinant and the rest still builds.
			var withoutP4 = await new KnowledgeCallContextService(new Lazy<ICallsService>(() => _calls.Object), _mapping.Object, new NullKnowledgeCallDeterminantSource()).BuildAsync(_call);
			withoutP4.Determinant.Should().BeNull();
			withoutP4.CallType.Should().Be("Structure Fire");
		}

		[Test]
		public async Task No_protected_call_field_or_protocol_answer_ever_reaches_the_model()
		{
			var answer = await _service.AskAsync(CallAsk());

			answer.Outcome.Should().Be(KnowledgeOutcomes.Answered);
			answer.CallContextUsed.Should().BeTrue();
			answer.PromptVersion.Should().Be(AiKnowledgePrompt.CallVersion);
			var prompt = string.Join("\n", _sent.Single().Messages.Select(m => m.Content));
			prompt.Should().Contain("<<<CALL").And.Contain("Call type: Structure Fire").And.Contain("Priority: Emergency").And.Contain("Determinant: 69-D-6").And.Contain("Location type: School");
			prompt.Should().NotContain("SENTINEL", "no caller, address, nature, note, contact, form data or protocol answer is ever sent");
			prompt.Should().NotContain("39.123");
			string.Join("\n", _retrievals.Select(r => r.Question)).Should().NotContain("SENTINEL");
		}

		#endregion The allow-list

		#region Grounding

		[Test]
		public async Task Every_cited_version_grounds_the_answer_but_removed_rows_and_no_AI_protocols_never_do()
		{
			await _service.AskAsync(CallAsk());

			var chunks = _retrievals.Single().Chunks;
			chunks.Should().Contain(c => c.Text.Contains("Stage engines 300 feet") && c.VersionId == "10", "the version the call cites, even superseded");
			chunks.Should().Contain(c => c.Text.Contains("incident liaison") && c.VersionId == "21");
			chunks.Should().NotContain(c => c.Text.Contains("VENDOR-TEXT"), "a protocol excluded from AI adds no chunk");
			chunks.Should().NotContain(c => c.Text.Contains("REMOVED-ROW-TEXT"), "a removed protocol row is not cited");
			chunks.Select(c => c.KnowledgeChunkId).Should().OnlyHaveUniqueItems().And.OnlyContain(id => id < 0);
			_retrievals.Single().Question.Should().Contain("Structure Fire").And.Contain("69-D-6", "the call's fields help a short question find its sections");
		}

		[Test]
		public async Task A_reader_who_cannot_view_the_call_gets_nothing()
		{
			_authorization.Setup(x => x.CanUserViewCallAsync(Member, CallId)).ReturnsAsync(false);
			(await _service.AskAsync(CallAsk())).Outcome.Should().Be(KnowledgeOutcomes.Invalid);
			_retriever.Verify(x => x.RetrieveAsync(It.IsAny<KnowledgeRetrievalRequest>(), It.IsAny<CancellationToken>()), Times.Never);

			_authorization.Setup(x => x.CanUserViewCallAsync(Member, CallId)).ReturnsAsync(true);
			_call.DepartmentId = 99;
			(await _service.AskAsync(CallAsk())).Outcome.Should().Be(KnowledgeOutcomes.Invalid, "another department's call");
		}

		[Test]
		public async Task The_call_determinant_may_be_quoted_but_a_computed_number_is_refused()
		{
			Answer(new { parts = new[] { new { text = "For a 69-D-6 call, stage engines 300 feet from the building.", sources = new[] { "S1" } } }, not_covered = false, conflicts = Array.Empty<object>() });
			(await _service.AskAsync(CallAsk("Where do engines stage?"))).Outcome.Should().Be(KnowledgeOutcomes.Answered, "69 and 6 are the call's own determinant");

			Answer(new { parts = new[] { new { text = "Stage engines 600 feet from the building.", sources = new[] { "S1" } } }, not_covered = false, conflicts = Array.Empty<object>() });
			var refused = await _service.AskAsync(CallAsk("Where do engines stage?"));
			refused.Outcome.Should().Be(KnowledgeOutcomes.Matched, "a number the sources do not hold refuses the generated text");
			refused.Parts.Should().BeEmpty();
		}

		[Test]
		public async Task Not_covered_goes_to_the_gaps_report_from_the_call_surface()
		{
			Answer(new { parts = Array.Empty<object>(), not_covered = true, conflicts = Array.Empty<object>() });

			var answer = await _service.AskAsync(CallAsk("Is there a helipad nearby?"));

			answer.Outcome.Should().Be(KnowledgeOutcomes.NotCovered);
			var gap = _gaps.Single();
			gap.Surface.Should().Be((int)KnowledgeAskSurfaces.Call);
			gap.Question.Should().Be("Is there a helipad nearby?");
			gap.Question.Should().NotContain("SENTINEL");
			_rows.Should().OnlyContain(r => r.PromptVersion == AiKnowledgePrompt.CallVersion);
		}

		[Test]
		public async Task A_thumbs_down_on_a_call_answer_is_accepted()
		{
			var answer = await _service.AskAsync(CallAsk());
			(await _service.FeedbackAsync(CallAsk().Reader, answer.ExchangeId, false, (int)KnowledgeAskSurfaces.Call)).Should().BeTrue();
		}

		[Test]
		public async Task A_call_citing_no_version_searches_the_readers_own_corpus_with_the_call_fields()
		{
			_callProtocols.Clear();
			await _service.AskAsync(CallAsk());
			var retrieval = _retrievals.Single();
			retrieval.Chunks.Should().BeNull("the stored corpus, filtered by the reader's own scope");
			retrieval.Question.Should().Contain("Structure Fire");
		}

		#endregion Grounding

		#region Ask this course

		[Test]
		public async Task Ask_this_course_runs_through_the_course_source_and_its_gate()
		{
			var chunk = new KnowledgeChunk
			{
				KnowledgeChunkId = -1, DepartmentId = Dept, SourceKind = (int)KnowledgeSourceKinds.Course, ItemId = "12", VersionId = "34", Anchor = "l5-b-h1", Title = "Ladders",
				Text = "Meet the incident liaison at the main office.", TextHash = "x"
			};
			_courseSource.Setup(x => x.LoadForReaderAsync(It.IsAny<KnowledgeReader>(), KnowledgeSourceKinds.Course, "12", "34", It.IsAny<CancellationToken>()))
				.ReturnsAsync(new KnowledgeVersionContent { Kind = KnowledgeSourceKinds.Course, ItemId = "12", VersionId = "34", Chunks = new List<KnowledgeChunk> { chunk } });
			var ask = new KnowledgeAskRequest
			{
				Reader = CallAsk().Reader, Question = "Where is the liaison?", Surface = (int)KnowledgeAskSurfaces.Course, ScopeKind = (int)KnowledgeSourceKinds.Course,
				ScopeItemId = "12", ScopeVersionId = "34"
			};

			var answer = await _service.AskAsync(ask);
			answer.Outcome.Should().Be(KnowledgeOutcomes.Answered);
			answer.Citations.Single().Anchor.Should().Be("l5-b-h1");
			_retrievals.Single().Chunks.Should().ContainSingle();

			_courseSource.Setup(x => x.LoadForReaderAsync(It.IsAny<KnowledgeReader>(), KnowledgeSourceKinds.Course, "12", "34", It.IsAny<CancellationToken>()))
				.ReturnsAsync(KnowledgeVersionContent.Refused(KnowledgeOutcomes.NotEntitled));
			(await _service.AskAsync(ask)).Outcome.Should().Be(KnowledgeOutcomes.NotEntitled, "the LMS gate is closed: course features fail closed");

			_enhancedAi.Setup(x => x.CanUseAsync(Dept, FeatureFlagKeys.AiKnowledge)).ReturnsAsync(false);
			(await _service.AskAsync(ask)).Outcome.Should().Be(KnowledgeOutcomes.NotEntitled, "Ai.Knowledge off");
		}

		#endregion Ask this course
	}
}
