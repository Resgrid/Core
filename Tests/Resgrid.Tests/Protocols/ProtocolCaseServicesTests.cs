using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.AiDispatch;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Protocols
{
	/// <summary>
	/// Protocols P4 services: the attach path walks the cited version's graph and records the case (an override needs a reason; a
	/// confirmed AI suggestion is recorded as AI, once); the case service's override after saving and the caller follow-up text that is
	/// logged on the call; the AI suggestion that fails closed and never shows licensed content to the model; the dispatch texts that
	/// carry the determinant only with the setting on.
	/// </summary>
	[TestFixture]
	public class ProtocolCaseServicesTests
	{
		private const int DepartmentId = 1;
		private const int ProtocolId = 2;
		private const int VersionId = 2000;
		private static readonly DateTime LoggedOn = new DateTime(2026, 11, 25, 15, 0, 0, DateTimeKind.Utc);

		private Mock<IDispatchProtocolRepository> _protocols;
		private Mock<IProtocolVersionsRepository> _versions;
		private Mock<IProtocolAiSuggestionService> _ai;
		private ProtocolsService _service;

		private static ProtocolVersion Version(ProtocolDecisionGraph graph, int versionId = VersionId) => new ProtocolVersion
		{
			ProtocolVersionId = versionId,
			DepartmentId = DepartmentId,
			DispatchProtocolId = ProtocolId,
			Status = (int)ProtocolVersionStatuses.Published,
			ContentJson = new ProtocolContentDocument { Blocks = new List<ProtocolContentBlock>(), Graph = graph }.ToJson()
		};

		[SetUp]
		public void SetUp()
		{
			_protocols = new Mock<IDispatchProtocolRepository>();
			_protocols.Setup(x => x.GetAllByDepartmentIdAsync(DepartmentId)).ReturnsAsync(new List<DispatchProtocol>
			{
				new DispatchProtocol { DispatchProtocolId = ProtocolId, Kind = (int)ProtocolKinds.Dispatch, Status = (int)ProtocolStatuses.Published, CurrentVersionId = VersionId, DepartmentId = DepartmentId, Name = "Medical", Code = "MED", ProtocolText = "<p>x</p>" }
			});
			var triggers = new Mock<IDispatchProtocolTriggersRepository>();
			triggers.Setup(x => x.GetTriggersByDepartmentIdAsync(DepartmentId)).ReturnsAsync(new List<DispatchProtocolTrigger>());
			var questions = new Mock<IDispatchProtocolQuestionsRepository>();
			questions.Setup(x => x.GetQuestionsWithAnswersByDepartmentIdAsync(DepartmentId)).ReturnsAsync(new List<DispatchProtocolQuestion>());
			var attachments = new Mock<IDispatchProtocolAttachmentRepository>();
			attachments.Setup(x => x.GetAttachmentInfoByDepartmentIdAsync(DepartmentId)).ReturnsAsync(new List<DispatchProtocolAttachment>());
			_versions = new Mock<IProtocolVersionsRepository>();
			_versions.Setup(x => x.GetByIdsAsync(DepartmentId, It.IsAny<IEnumerable<int>>(), true)).ReturnsAsync(new List<ProtocolVersion> { Version(ProtocolDecisionEngineTests.Sample()) });
			_ai = new Mock<IProtocolAiSuggestionService>();

			_service = new ProtocolsService(_protocols.Object, attachments.Object, questions.Object, triggers.Object, Mock.Of<IDispatchProtocolQuestionAnswersRepository>(),
				new Lazy<IDispatchTriggerFactsService>(() => Mock.Of<IDispatchTriggerFactsService>()), new Lazy<IProtocolAiSuggestionService>(() => _ai.Object), new Lazy<IProtectedWriteService>(() => Mock.Of<IProtectedWriteService>()),
				null, null, null, Mock.Of<ICallProtocolsRepository>(), Mock.Of<IDispatchProtocolSnapshotsRepository>(), _versions.Object);
		}

		private Task<ProtocolAttachResult> AttachAsync(ProtocolCaseInput input, int callId = 0, bool store = true) =>
			_service.PrepareCallProtocolsAsync(DepartmentId, new Call { CallId = callId, DepartmentId = DepartmentId, Priority = 3, LoggedOn = LoggedOn },
				new[] { new ProtocolAttachRequest { ProtocolId = ProtocolId, Case = input } }, "dispatcher", null, store);

		[Test]
		public async Task The_server_walks_the_cited_graph_and_records_the_case()
		{
			var result = await AttachAsync(new ProtocolCaseInput { Answers = new Dictionary<string, string> { ["awake"] = "yes", ["breathing"] = "no" }, AppliedPriority = 7 });

			var row = result.Attached.Should().ContainSingle().Subject;
			row.Determinant.Should().Be("MED-E1");
			row.OverrideReason.Should().BeNull();
			row.Source.Should().Be((int)CallProtocolSources.Manual);
			var record = CallProtocolCase.Parse(row.CaseJson);
			record.ProtocolVersionId.Should().Be(VersionId);
			record.ProtocolName.Should().Be("Medical");
			record.InstructionKeys.Should().Equal("airway");
			record.AppliedPriority.Should().Be(7);
		}

		[Test]
		public async Task A_determinant_override_without_a_reason_is_refused()
		{
			var refused = await AttachAsync(new ProtocolCaseInput { Answers = new Dictionary<string, string> { ["awake"] = "yes", ["breathing"] = "yes" }, OverrideDeterminant = "MED-E1" });
			refused.Attached.Should().BeEmpty();
			refused.Refused.Should().ContainSingle(r => r.Reason == ProtocolAttachRefusals.OverrideReasonRequired);

			var kept = await AttachAsync(new ProtocolCaseInput { Answers = new Dictionary<string, string> { ["awake"] = "yes", ["breathing"] = "yes" }, OverrideDeterminant = "MED-E1", OverrideReason = "Now unresponsive" });
			var row = kept.Attached.Should().ContainSingle().Subject;
			row.Determinant.Should().Be("MED-E1");
			row.OverrideReason.Should().Be("Now unresponsive");
			CallProtocolCase.Parse(row.CaseJson).ComputedDeterminant.Should().Be("MED-A1");
		}

		[Test]
		public async Task An_answer_from_outside_the_graph_is_refused()
		{
			(await AttachAsync(new ProtocolCaseInput { Answers = new Dictionary<string, string> { ["awake"] = "perhaps" } })).Refused
				.Should().ContainSingle(r => r.Reason == ProtocolAttachRefusals.CaseInvalid);
		}

		[Test]
		public async Task An_invalid_graph_or_no_case_input_attaches_as_before()
		{
			(await AttachAsync(null)).Attached.Should().ContainSingle(r => r.CaseJson == null && r.Determinant == null);

			var broken = ProtocolDecisionEngineTests.Sample();
			broken.Nodes[0].IsRoot = false;
			_versions.Setup(x => x.GetByIdsAsync(DepartmentId, It.IsAny<IEnumerable<int>>(), true)).ReturnsAsync(new List<ProtocolVersion> { Version(broken) });
			(await AttachAsync(new ProtocolCaseInput { Answers = new Dictionary<string, string> { ["awake"] = "yes" } })).Attached
				.Should().ContainSingle(r => r.CaseJson == null, "an invalid graph is never run");
		}

		[Test]
		public async Task A_confirmed_ai_suggestion_is_recorded_as_ai_once_and_a_made_up_one_is_not()
		{
			_ai.Setup(x => x.GetConfirmableAsync(DepartmentId, 55, ProtocolId, 9)).ReturnsAsync(new ProtocolAiSuggestion { ProtocolAiSuggestionId = 9, DepartmentId = DepartmentId, CallId = 55, DispatchProtocolId = ProtocolId });
			var answers = new Dictionary<string, string> { ["awake"] = "yes", ["breathing"] = "yes" };

			var dryRun = await AttachAsync(new ProtocolCaseInput { Answers = answers, AiSuggestionId = 9 }, 55, store: false);
			dryRun.Attached.Single().Source.Should().Be((int)CallProtocolSources.Ai);
			_ai.Verify(x => x.ConfirmAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never, "a dry run confirms nothing");

			var saved = await AttachAsync(new ProtocolCaseInput { Answers = answers, AiSuggestionId = 9 }, 55);
			var row = saved.Attached.Single();
			row.Source.Should().Be((int)CallProtocolSources.Ai);
			CallProtocolCase.Parse(row.CaseJson).AiSuggestionId.Should().Be(9);
			_ai.Verify(x => x.ConfirmAsync(DepartmentId, 9, "dispatcher", It.IsAny<CancellationToken>()), Times.Once);

			var madeUp = await AttachAsync(new ProtocolCaseInput { Answers = answers, AiSuggestionId = 10 }, 55);
			madeUp.Attached.Single().Source.Should().Be((int)CallProtocolSources.Manual);
			CallProtocolCase.Parse(madeUp.Attached.Single().CaseJson).AiSuggestionId.Should().BeNull();
		}

		#region Case service: override after saving and the caller follow-up

		private sealed class CaseHarness
		{
			public readonly Mock<IProtocolsService> Protocols = new Mock<IProtocolsService>();
			public readonly Mock<IProtocolVersionsRepository> Versions = new Mock<IProtocolVersionsRepository>();
			public readonly Mock<ICallProtocolsRepository> CallProtocols = new Mock<ICallProtocolsRepository>();
			public readonly Mock<IIntakeConversationService> Conversations = new Mock<IIntakeConversationService>();
			public readonly Mock<ITtsAudioService> Tts = new Mock<ITtsAudioService>();
			public readonly Mock<IEventAggregator> Events = new Mock<IEventAggregator>();
			public readonly Mock<IProtectedReadService> ProtectedRead = new Mock<IProtectedReadService>();
			public readonly Mock<IProtectedWriteService> ProtectedWrite = new Mock<IProtectedWriteService>();
			public readonly Mock<IProtectedGrantContext> Grant = new Mock<IProtectedGrantContext>();
			public readonly List<AuditEvent> Audits = new List<AuditEvent>();
			public CallProtocol Row;
			public string WrittenCase;

			public CaseHarness()
			{
				var record = ProtocolCaseFlow.Record(ProtocolCaseFlow.Run(ProtocolDecisionEngineTests.Sample(), new Dictionary<string, string> { ["awake"] = "yes", ["breathing"] = "no" }),
					new ProtocolCaseInput(), VersionId, CallProtocolSources.Manual, "d", DateTime.UtcNow).Record;
				Row = new CallProtocol { CallProtocolId = 70, CallId = 55, DispatchProtocolId = ProtocolId, Determinant = record.Determinant, CaseJson = record.ToJson() };
				CallProtocols.Setup(x => x.GetForDepartmentCallAsync(DepartmentId, 55, 70)).ReturnsAsync(() => Row);
				CallProtocols.Setup(x => x.TryUpdateCaseAsync(DepartmentId, 70, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
					.Callback((int d, int id, string det, string reason, string json, CancellationToken _) => WrittenCase = json).ReturnsAsync(true);
				Versions.Setup(x => x.GetByIdsAsync(DepartmentId, It.IsAny<IEnumerable<int>>(), true)).ReturnsAsync(new List<ProtocolVersion> { Version(ProtocolDecisionEngineTests.Sample()) });
				Events.Setup(x => x.SendMessage(It.IsAny<AuditEvent>())).Callback((AuditEvent e) => Audits.Add(e));
			}

			public ProtocolCaseService Service() => new ProtocolCaseService(Protocols.Object, Versions.Object, CallProtocols.Object,
				new Lazy<IIntakeConversationService>(() => Conversations.Object), new Lazy<ITtsAudioService>(() => Tts.Object), Events.Object,
				new Lazy<IProtectedReadService>(() => ProtectedRead.Object), new Lazy<IProtectedWriteService>(() => ProtectedWrite.Object), Grant.Object);
		}

		[Test]
		public async Task An_override_after_saving_needs_a_reason_and_is_audited()
		{
			var h = new CaseHarness();
			(await h.Service().OverrideDeterminantAsync(DepartmentId, 55, 70, "MED-A1", "", "sup")).Outcome.Should().Be(ProtocolCaseUpdateOutcomes.OverrideReasonRequired);
			h.CallProtocols.Verify(x => x.TryUpdateCaseAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

			var done = await h.Service().OverrideDeterminantAsync(DepartmentId, 55, 70, "MED-A1", "Patient now breathing", "sup");
			done.Outcome.Should().Be(ProtocolCaseUpdateOutcomes.Done);
			done.CallProtocol.Determinant.Should().Be("MED-A1");
			CallProtocolCase.Parse(h.WrittenCase).OverrideReason.Should().Be("Patient now breathing");
			h.Audits.Should().ContainSingle(a => a.Type == AuditLogTypes.ProtocolDeterminantOverridden);

			(await h.Service().OverrideDeterminantAsync(DepartmentId, 55, 999, "X", "y", "sup")).Outcome.Should().Be(ProtocolCaseUpdateOutcomes.NotFound);
		}

		[Test]
		public async Task An_override_raises_determinant_changed_once_without_the_reason_and_a_refused_or_failed_one_raises_nothing()
		{
			// Round 7C, workflow trigger 254.
			var h = new CaseHarness();
			var raised = new List<Resgrid.Model.Events.DispatchCaseWorkflowEvent>();
			h.Events.Setup(x => x.SendMessage(It.IsAny<Resgrid.Model.Events.DispatchCaseWorkflowEvent>())).Callback((Resgrid.Model.Events.DispatchCaseWorkflowEvent e) => raised.Add(e));
			var before = h.Row.Determinant;

			(await h.Service().OverrideDeterminantAsync(DepartmentId, 55, 70, "MED-A1", "", "sup")).Outcome.Should().Be(ProtocolCaseUpdateOutcomes.OverrideReasonRequired);
			var stored = (h.Row.Determinant, h.Row.CaseJson);
			h.CallProtocols.Setup(x => x.TryUpdateCaseAsync(DepartmentId, 70, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
			(await h.Service().OverrideDeterminantAsync(DepartmentId, 55, 70, "MED-A1", "Patient now breathing", "sup")).Outcome.Should().Be(ProtocolCaseUpdateOutcomes.NotFound);
			raised.Should().BeEmpty();

			// The failed write stored nothing: the row reads as before.
			(h.Row.Determinant, h.Row.CaseJson) = stored;
			h.Row.OverrideReason = null;
			h.CallProtocols.Setup(x => x.TryUpdateCaseAsync(DepartmentId, 70, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
			(await h.Service().OverrideDeterminantAsync(DepartmentId, 55, 70, "MED-A1", "Patient now breathing", "sup")).Outcome.Should().Be(ProtocolCaseUpdateOutcomes.Done);

			raised.Should().ContainSingle();
			var changed = raised.Single();
			changed.Trigger.Should().Be(WorkflowTriggerEventType.CallDeterminantChanged);
			(changed.CallId, changed.CallProtocolId, changed.ProtocolId).Should().Be((55, 70, ProtocolId));
			changed.Determinant.Should().Be("MED-A1");
			changed.PreviousDeterminant.Should().Be(before);
			changed.Overridden.Should().BeTrue();
			changed.UserId.Should().Be("sup");
			Newtonsoft.Json.JsonConvert.SerializeObject(changed).Should().NotContain("breathing", "the override reason can carry what the caller said");
		}

		[Test]
		public async Task A_caller_instruction_goes_over_the_open_sms_thread_and_is_logged_on_the_call()
		{
			var h = new CaseHarness();
			h.Conversations.Setup(x => x.GetForCallAsync(DepartmentId, 55)).ReturnsAsync(new IntakeConversation
			{
				IntakeConversationId = "conv1", DepartmentId = DepartmentId, CallId = 55, ChannelType = (int)IntakeChannelTypes.Sms, State = (int)IntakeConversationStates.Open
			});
			h.Conversations.Setup(x => x.SendDispatcherMessageAsync(DepartmentId, "conv1", "dispatcher", It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(IntakeConversationActionResults.Done);

			var sent = await h.Service().SendCallerInstructionAsync(DepartmentId, 55, 70, "airway", "dispatcher");

			sent.Outcome.Should().Be(ProtocolCaseUpdateOutcomes.Done);
			h.Conversations.Verify(x => x.SendDispatcherMessageAsync(DepartmentId, "conv1", "dispatcher", "Tilt the head back and look for breathing.", It.IsAny<CancellationToken>()), Times.Once);
			CallProtocolCase.Parse(h.WrittenCase).CallerMessages.Should().ContainSingle(m => m.NodeKey == "airway" && m.ByUserId == "dispatcher" && m.Result == "Done");
			h.Audits.Should().ContainSingle(a => a.Type == AuditLogTypes.ProtocolCallerInstructionSent);
		}

		[Test]
		public async Task Nothing_is_texted_without_an_open_thread_an_opt_in_or_a_marked_instruction()
		{
			var h = new CaseHarness();
			(await h.Service().SendCallerInstructionAsync(DepartmentId, 55, 70, "alpha", "dispatcher")).Outcome.Should().Be(ProtocolCaseUpdateOutcomes.NotAnInstruction);
			(await h.Service().SendCallerInstructionAsync(DepartmentId, 55, 70, "airway", "dispatcher")).Outcome.Should().Be(ProtocolCaseUpdateOutcomes.NoThread);

			h.Conversations.Setup(x => x.GetForCallAsync(DepartmentId, 55)).ReturnsAsync(new IntakeConversation
			{
				IntakeConversationId = "conv1", ChannelType = (int)IntakeChannelTypes.Sms, State = (int)IntakeConversationStates.Open, OptedOut = true
			});
			(await h.Service().SendCallerInstructionAsync(DepartmentId, 55, 70, "airway", "dispatcher")).Outcome.Should().Be(ProtocolCaseUpdateOutcomes.OptedOut);

			h.Conversations.Setup(x => x.GetForCallAsync(DepartmentId, 55)).ReturnsAsync(new IntakeConversation
			{
				IntakeConversationId = "conv1", ChannelType = (int)IntakeChannelTypes.Email, State = (int)IntakeConversationStates.Open
			});
			(await h.Service().SendCallerInstructionAsync(DepartmentId, 55, 70, "airway", "dispatcher")).Outcome.Should().Be(ProtocolCaseUpdateOutcomes.NoThread, "text only: the SMS thread");

			h.Conversations.Verify(x => x.SendDispatcherMessageAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		#endregion

		#region AI suggestion

		private sealed class AiHarness
		{
			public readonly Mock<IEnhancedAiAccessService> Access = new Mock<IEnhancedAiAccessService>();
			public readonly Mock<IDepartmentDataProtectionService> Protection = new Mock<IDepartmentDataProtectionService>();
			public readonly Mock<IProtocolsService> Protocols = new Mock<IProtocolsService>();
			public readonly Mock<IProtocolCaseService> Cases = new Mock<IProtocolCaseService>();
			public readonly Mock<ICallsService> Calls = new Mock<ICallsService>();
			public readonly Mock<IIntakeConversationService> Conversations = new Mock<IIntakeConversationService>();
			public readonly Mock<IProtocolAiSuggestionsRepository> Suggestions = new Mock<IProtocolAiSuggestionsRepository>();
			public readonly Mock<IProtocolCaseSuggester> Suggester = new Mock<IProtocolCaseSuggester>();
			public readonly Mock<IAiUsageMeter> Usage = new Mock<IAiUsageMeter>();
			public readonly Mock<IAiBackgroundAdmission> Admission = new Mock<IAiBackgroundAdmission>();
			public IReadOnlyList<ProtocolAiCandidate> Shown;

			public AiHarness()
			{
				Suggester.SetupGet(x => x.IsConfigured).Returns(true);
				Access.Setup(x => x.CanUseAsync(DepartmentId, FeatureFlagKeys.AiIntake)).ReturnsAsync(true);
				Protection.Setup(x => x.ShouldEncryptNewWritesAsync(DepartmentId)).ReturnsAsync(false);
				Calls.Setup(x => x.GetCallByIdAsync(55, It.IsAny<bool>())).ReturnsAsync(new Call { CallId = 55, DepartmentId = DepartmentId, NatureOfCall = "My father is not breathing" });
				Protocols.Setup(x => x.GetProtocolSummariesForDepartmentAsync(DepartmentId, true)).ReturnsAsync(new List<DispatchProtocol>
				{
					new DispatchProtocol { DispatchProtocolId = ProtocolId, DepartmentId = DepartmentId },
					new DispatchProtocol { DispatchProtocolId = 3, DepartmentId = DepartmentId, IsLicensedContent = true },
					new DispatchProtocol { DispatchProtocolId = 4, DepartmentId = DepartmentId, ExcludeFromAi = true }
				});
				Cases.Setup(x => x.GetDefinitionsAsync(DepartmentId)).ReturnsAsync(new[] { ProtocolId, 3, 4 }
					.Select(id => new ProtocolCaseDefinition { ProtocolId = id, ProtocolVersionId = id * 1000, Code = "C" + id, Name = "P" + id, Graph = ProtocolDecisionEngineTests.Sample() }).ToList());
				Suggester.Setup(x => x.SuggestAsync(DepartmentId, It.IsAny<string>(), It.IsAny<IReadOnlyList<ProtocolAiCandidate>>(), It.IsAny<CancellationToken>()))
					.Callback((int d, string text, IReadOnlyList<ProtocolAiCandidate> c, CancellationToken _) => Shown = c)
					.ReturnsAsync(new ProtocolAiPick { ProtocolId = ProtocolId, Confidence = 0.9m, Rationale = "Not breathing", Answers = new Dictionary<string, string> { ["awake"] = "no", ["age"] = "sixty", ["unknown"] = "yes" } });
				Suggestions.Setup(x => x.SaveOrUpdateAsync(It.IsAny<ProtocolAiSuggestion>(), It.IsAny<CancellationToken>(), true))
					.ReturnsAsync((ProtocolAiSuggestion s, CancellationToken _, bool __) => { s.ProtocolAiSuggestionId = 77; return s; });
				// Round 7A: the suggestion is charged to the Enhanced AI meter (the shared slot and the monthly budget).
				Usage.Setup(x => x.RemainingAsync(DepartmentId, It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(1_000_000);
				Admission.Setup(x => x.ReserveBackgroundAsync(DepartmentId, ProtocolAiSuggestionService.Feature, It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<int>(),
					It.IsAny<CancellationToken>(), It.IsAny<int?>()))
					.ReturnsAsync((int d, string f, string t, DateTime n, int tokens, int limit, CancellationToken ct, int? p) => new AiUsageReservation("r1", DepartmentId, "background", tokens, DateTime.UtcNow.AddMinutes(2)));
			}

			public ProtocolAiSuggestionService Service() => new ProtocolAiSuggestionService(Access.Object, Protection.Object, Protocols.Object, Cases.Object,
				new Lazy<ICallsService>(() => Calls.Object), new Lazy<IIntakeConversationService>(() => Conversations.Object), Suggestions.Object, Suggester.Object, Mock.Of<IEventAggregator>(),
				Usage.Object, Admission.Object, TimeProvider.System);
		}

		[Test]
		public async Task Without_the_add_on_or_under_data_protection_no_call_text_reaches_the_model()
		{
			var noAddon = new AiHarness();
			noAddon.Access.Setup(x => x.CanUseAsync(DepartmentId, FeatureFlagKeys.AiIntake)).ReturnsAsync(false);
			(await noAddon.Service().SuggestAsync(DepartmentId, 55, "d")).Outcome.Should().Be(ProtocolAiSuggestionOutcomes.NotEntitled);
			(await noAddon.Service().IsAvailableAsync(DepartmentId)).Should().BeFalse();
			noAddon.Suggester.Verify(x => x.SuggestAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<ProtocolAiCandidate>>(), It.IsAny<CancellationToken>()), Times.Never);

			var adp = new AiHarness();
			adp.Protection.Setup(x => x.ShouldEncryptNewWritesAsync(DepartmentId)).ReturnsAsync(true);
			(await adp.Service().SuggestAsync(DepartmentId, 55, "d")).Outcome.Should().Be(ProtocolAiSuggestionOutcomes.ProtectionUnsupported);

			var broken = new AiHarness();
			broken.Access.Setup(x => x.CanUseAsync(DepartmentId, FeatureFlagKeys.AiIntake)).ThrowsAsync(new InvalidOperationException("billing down"));
			(await broken.Service().SuggestAsync(DepartmentId, 55, "d")).Outcome.Should().Be(ProtocolAiSuggestionOutcomes.NotEntitled, "it fails closed");

			var unconfigured = new AiHarness();
			unconfigured.Suggester.SetupGet(x => x.IsConfigured).Returns(false);
			(await unconfigured.Service().SuggestAsync(DepartmentId, 55, "d")).Outcome.Should().Be(ProtocolAiSuggestionOutcomes.Unconfigured);
		}

		[Test]
		public async Task A_suggestion_is_stored_with_only_valid_answers_and_licensed_protocols_are_never_shown()
		{
			var h = new AiHarness();
			var result = await h.Service().SuggestAsync(DepartmentId, 55, "dispatcher");

			result.Outcome.Should().Be(ProtocolAiSuggestionOutcomes.Suggested);
			h.Shown.Select(c => c.ProtocolId).Should().Equal(ProtocolId);
			result.Suggestion.ProtocolAiSuggestionId.Should().Be(77);
			result.Suggestion.DispatchProtocolId.Should().Be(ProtocolId);
			result.Suggestion.ProtocolVersionId.Should().Be(ProtocolId * 1000);
			result.Suggestion.Answers.Should().BeEquivalentTo(new Dictionary<string, string> { ["awake"] = "no" }, "a number question answered in words and an unknown step are dropped");
			result.Suggestion.InputKind.Should().Be((int)ProtocolAiSuggestionInputs.CallText);
		}

		[Test]
		public async Task A_pick_outside_the_candidates_or_below_the_floor_is_no_match()
		{
			var h = new AiHarness();
			h.Suggester.Setup(x => x.SuggestAsync(DepartmentId, It.IsAny<string>(), It.IsAny<IReadOnlyList<ProtocolAiCandidate>>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new ProtocolAiPick { ProtocolId = 3, Confidence = 0.99m });
			(await h.Service().SuggestAsync(DepartmentId, 55, "d")).Outcome.Should().Be(ProtocolAiSuggestionOutcomes.NoMatch, "a licensed protocol is never a candidate");

			h.Suggester.Setup(x => x.SuggestAsync(DepartmentId, It.IsAny<string>(), It.IsAny<IReadOnlyList<ProtocolAiCandidate>>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new ProtocolAiPick { ProtocolId = ProtocolId, Confidence = 0.2m });
			(await h.Service().SuggestAsync(DepartmentId, 55, "d")).Outcome.Should().Be(ProtocolAiSuggestionOutcomes.NoMatch);

			h.Suggester.Setup(x => x.SuggestAsync(DepartmentId, It.IsAny<string>(), It.IsAny<IReadOnlyList<ProtocolAiCandidate>>(), It.IsAny<CancellationToken>()))
				.ThrowsAsync(new TimeoutException());
			(await h.Service().SuggestAsync(DepartmentId, 55, "d")).Outcome.Should().Be(ProtocolAiSuggestionOutcomes.Unavailable);
		}

		[Test]
		public async Task Only_a_real_unconfirmed_suggestion_for_this_call_and_protocol_can_be_confirmed()
		{
			var h = new AiHarness();
			h.Suggestions.Setup(x => x.GetByIdForDepartmentAsync(DepartmentId, 9)).ReturnsAsync(new ProtocolAiSuggestion { ProtocolAiSuggestionId = 9, DepartmentId = DepartmentId, CallId = 55, DispatchProtocolId = ProtocolId });
			h.Suggestions.Setup(x => x.GetByIdForDepartmentAsync(DepartmentId, 10)).ReturnsAsync(new ProtocolAiSuggestion { ProtocolAiSuggestionId = 10, DepartmentId = DepartmentId, CallId = 55, DispatchProtocolId = ProtocolId, ConfirmedOn = DateTime.UtcNow });

			(await h.Service().GetConfirmableAsync(DepartmentId, 55, ProtocolId, 9)).Should().NotBeNull();
			(await h.Service().GetConfirmableAsync(DepartmentId, 56, ProtocolId, 9)).Should().BeNull("another call");
			(await h.Service().GetConfirmableAsync(DepartmentId, 55, 3, 9)).Should().BeNull("another protocol");
			(await h.Service().GetConfirmableAsync(DepartmentId, 55, ProtocolId, 10)).Should().BeNull("already confirmed");
			(await h.Service().GetConfirmableAsync(DepartmentId, 55, ProtocolId, 11)).Should().BeNull("unknown");
		}

		[Test]
		public async Task A_suggestion_is_charged_to_the_enhanced_ai_meter_with_the_tokens_the_model_used()
		{
			var h = new AiHarness();
			h.Suggester.Setup(x => x.SuggestAsync(DepartmentId, It.IsAny<string>(), It.IsAny<IReadOnlyList<ProtocolAiCandidate>>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new ProtocolAiPick { ProtocolId = ProtocolId, Confidence = 0.9m, Answers = new Dictionary<string, string> { ["awake"] = "no" }, InputTokens = 700, OutputTokens = 60 });

			(await h.Service().SuggestAsync(DepartmentId, 55, "dispatcher")).Outcome.Should().Be(ProtocolAiSuggestionOutcomes.Suggested);

			h.Admission.Verify(x => x.ReserveBackgroundAsync(DepartmentId, "ProtocolSuggestion", It.IsAny<string>(), It.IsAny<DateTime>(), ProtocolAiSuggestionService.ReservedTokens,
				It.IsAny<int>(), It.IsAny<CancellationToken>(), It.IsAny<int?>()), Times.Once);
			h.Usage.Verify(x => x.CompleteAsync(It.Is<AiUsageReservation>(r => r.Id == "r1"), 760, "Answered", It.IsAny<CancellationToken>()), Times.Once);

			// Failed inference is reserved but not charged (the operator's outage, not the department's).
			var down = new AiHarness();
			down.Suggester.Setup(x => x.SuggestAsync(DepartmentId, It.IsAny<string>(), It.IsAny<IReadOnlyList<ProtocolAiCandidate>>(), It.IsAny<CancellationToken>()))
				.ThrowsAsync(new TimeoutException());
			(await down.Service().SuggestAsync(DepartmentId, 55, "d")).Outcome.Should().Be(ProtocolAiSuggestionOutcomes.Unavailable);
			down.Usage.Verify(x => x.CompleteAsync(It.IsAny<AiUsageReservation>(), 0, "Unavailable", It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task Over_the_monthly_cap_there_is_no_suggestion_and_no_model_call_never_an_error()
		{
			var h = new AiHarness();
			h.Usage.Setup(x => x.RemainingAsync(DepartmentId, It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(100);

			var result = await h.Service().SuggestAsync(DepartmentId, 55, "d");

			result.Outcome.Should().Be(ProtocolAiSuggestionOutcomes.BudgetExhausted, "the dispatcher picks by hand with the trigger matches");
			result.Suggestion.Should().BeNull();
			h.Suggester.Verify(x => x.SuggestAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<ProtocolAiCandidate>>(), It.IsAny<CancellationToken>()), Times.Never);
			h.Admission.Verify(x => x.ReserveBackgroundAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<int>(),
				It.IsAny<CancellationToken>(), It.IsAny<int?>()), Times.Never);
			h.Suggestions.Verify(x => x.SaveOrUpdateAsync(It.IsAny<ProtocolAiSuggestion>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
		}

		[Test]
		public void The_llm_suggester_reports_its_tokens_even_for_an_unusable_reply()
		{
			// The tokens of a reply that is no pick are still spent: the suggester returns a zero-confidence pick that carries them.
			var llm = new Mock<Resgrid.Llm.ILlmClient>();
			llm.Setup(x => x.CompleteAsync(It.IsAny<Resgrid.Llm.LlmRequest>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new Resgrid.Llm.LlmResult("no tool", Array.Empty<Resgrid.Llm.LlmToolCall>(), 321, 12, "stop"));
			var suggester = new LlmProtocolCaseSuggester(new Lazy<Resgrid.Llm.ILlmClient>(() => llm.Object));

			var pick = suggester.SuggestAsync(DepartmentId, "text", new List<ProtocolAiCandidate>(), CancellationToken.None).GetAwaiter().GetResult();

			pick.Should().NotBeNull();
			pick.Confidence.Should().Be(0m);
			(pick.InputTokens + pick.OutputTokens).Should().Be(333);
			ProtocolAiSuggestionService.Validate(pick, new List<ProtocolAiCandidate>()).Should().BeNull("a zero-confidence pick is no match");
		}

		[Test]
		public void The_model_tool_call_is_parsed_and_unreadable_output_is_no_pick()
		{
			var pick = LlmProtocolCaseSuggester.Parse(new Resgrid.Llm.LlmResult(null, new[]
			{
				new Resgrid.Llm.LlmToolCall("1", LlmProtocolCaseSuggester.ToolName, "{\"protocolId\":2,\"confidence\":0.8,\"reason\":\"r\",\"answers\":[{\"step\":\"awake\",\"answer\":\"no\"},{\"step\":\"age\",\"answer\":72}]}")
			}, 10, 10, "tool_calls"));
			pick.ProtocolId.Should().Be(2);
			pick.Confidence.Should().Be(0.8m);
			pick.Answers.Should().BeEquivalentTo(new Dictionary<string, string> { ["awake"] = "no", ["age"] = "72" });

			LlmProtocolCaseSuggester.Parse(new Resgrid.Llm.LlmResult("text", Array.Empty<Resgrid.Llm.LlmToolCall>(), 1, 1, "stop")).Should().BeNull();
			LlmProtocolCaseSuggester.Parse(new Resgrid.Llm.LlmResult(null, new[] { new Resgrid.Llm.LlmToolCall("1", LlmProtocolCaseSuggester.ToolName, "{not json") }, 1, 1, "x")).Should().BeNull();
		}

		#endregion

		#region Dispatch texts

		private static List<CallProtocol> OnCall() => new List<CallProtocol>
		{
			new CallProtocol { CallProtocolId = 1, Data = "SF", AttachedOn = LoggedOn },
			new CallProtocol { CallProtocolId = 2, Data = "MED", Determinant = "MED-E1", AttachedOn = LoggedOn.AddSeconds(1), Protocol = new DispatchProtocol { Name = "Medical" } }
		};

		[Test]
		public void Push_sms_and_voice_texts_carry_the_determinant_only_with_the_setting_on()
		{
			ProtocolDispatchText.PushTitle("Call 12", OnCall(), false).Should().Be("Call 12");
			ProtocolDispatchText.SmsProtocols(OnCall(), false).Should().Be("SF,MED");
			ProtocolDispatchText.Spoken(OnCall(), false).Should().BeNull();

			ProtocolDispatchText.PushTitle("Call 12", OnCall(), true).Should().Be("Call 12 · MED-E1 Medical");
			ProtocolDispatchText.SmsProtocols(OnCall(), true).Should().Be("SF,MED; MED-E1 Medical");
			ProtocolDispatchText.Spoken(OnCall(), true).Should().Be("Determinant, MED-E1. Protocol, Medical.");

			ProtocolDispatchText.PushTitle("Call 12", new List<CallProtocol> { new CallProtocol { Data = "SF" } }, true).Should().Be("Call 12", "no determinant, nothing added");
			ProtocolDispatchText.SmsProtocols(new List<CallProtocol> { new CallProtocol { Data = "X", Determinant = "D-1", RemovedOn = DateTime.UtcNow } }, true).Should().BeEmpty("a removed protocol says nothing");
		}

		[Test]
		public async Task The_voice_prompt_is_unchanged_with_the_setting_off_and_gains_the_sentence_on()
		{
			var call = new Call { CallId = 12, DepartmentId = DepartmentId, Name = "Medical call", NatureOfCall = "Man down", Priority = 1, Protocols = OnCall() };
			var settings = new Mock<IDepartmentSettingsService>();
			settings.Setup(x => x.GetProtocolLibrarySettingsAsync(DepartmentId, false)).ReturnsAsync(new ProtocolLibrarySettings());

			var off = await DispatchVoicePromptBuilder.ResolveProtocolSentenceAsync(call, settings.Object, null);
			off.Should().BeNull();
			DispatchVoicePromptBuilder.BuildDispatchPrompt(call, "123 Main St", off).Should().Be(DispatchVoicePromptBuilder.BuildDispatchPrompt(call, "123 Main St"),
				"the TTS cache key stays byte-identical while the setting is off");

			settings.Setup(x => x.GetProtocolLibrarySettingsAsync(DepartmentId, false)).ReturnsAsync(new ProtocolLibrarySettings { ShowDeterminantInDispatch = true });
			var on = await DispatchVoicePromptBuilder.ResolveProtocolSentenceAsync(call, settings.Object, null);
			DispatchVoicePromptBuilder.BuildDispatchPrompt(call, "123 Main St", on).Should().EndWith(". Determinant, MED-E1. Protocol, Medical.");

			var calls = new Mock<ICallsService>();
			calls.Setup(x => x.GetCallProtocolsByCallIdAsync(12)).ReturnsAsync(OnCall());
			call.Protocols = null;
			(await DispatchVoicePromptBuilder.ResolveProtocolSentenceAsync(call, settings.Object, calls.Object)).Should().Be(on, "both sides load the protocols the same way");
		}

		[Test]
		public void The_new_setting_survives_a_protobuf_round_trip()
		{
			var blob = Resgrid.Framework.ObjectSerialization.Serialize(new ProtocolLibrarySettings { ShowDeterminantInDispatch = true, DefaultContentLocale = "de" });
			var back = Resgrid.Framework.ObjectSerialization.Deserialize<ProtocolLibrarySettings>(blob);
			back.ShowDeterminantInDispatch.Should().BeTrue();
			Resgrid.Framework.ObjectSerialization.Deserialize<ProtocolLibrarySettings>(Resgrid.Framework.ObjectSerialization.Serialize(new ProtocolLibrarySettings()))
				.ShowDeterminantInDispatch.Should().BeFalse("off by default");
		}

		#endregion
	}
}
