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
using Resgrid.Model.Providers;
using Resgrid.Model.Queue;
using Resgrid.Model.Services;
using Resgrid.Services.AiDispatch;

namespace Resgrid.Tests.AiDispatch
{
	/// <summary>
	/// After each Enhanced AI turn of an intake SMS conversation (plan §8 D16) the pending call is re-enriched from the texter's whole
	/// transcript through the existing AI dispatch enrichment (no second field extraction): the model reads the transcript, the call's
	/// one audit row is not claimed again, fields still empty are filled, and the AI note waits for the conversation's last turn.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class AiDispatchConversationRefreshTests
	{
		private const int DepartmentId = 55;
		private const int CallId = 700;
		private const string Transcript = "Car crash\nIt's at 123 MAIN ST, SPRINGFIELD\nMy name is Jane Q Public";

		private sealed class FixedClock : TimeProvider
		{
			public override DateTimeOffset GetUtcNow() => new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);
		}

		private Mock<ICallsService> _calls;
		private Mock<IAiDispatchAuditRepository> _audits;
		private Mock<IIntakeConversationOperations> _conversations;
		private Call _call;
		private readonly List<Call> _saved = new List<Call>();
		private readonly List<CallNote> _notes = new List<CallNote>();
		private readonly List<LlmRequest> _requests = new List<LlmRequest>();
		private AiDispatchEnrichmentService _service;
		private (bool, string, bool, string, string, string, string, string, int, bool, double, int, bool, bool) _savedConfig;

		[SetUp]
		public void SetUp()
		{
			_savedConfig = (AiDispatchConfig.EnrichEnabled, AiConfig.Endpoint, AiConfig.AllowPrivateEndpoint, AiConfig.ApiKey, AiConfig.Model, AiConfig.ModelRevision,
				AiConfig.RuntimeDigest, AiConfig.SelfHostedDepartmentIds, AiConfig.MonthlyTokenLimit, AiDispatchConfig.DecisionsEnabled, AiDispatchConfig.MinimumConfidence,
				AiDispatchConfig.AdmissionWaitSeconds, IntakeConfig.Enabled, AiConfig.SendCacheSalt);
			AiDispatchConfig.EnrichEnabled = true;
			AiDispatchConfig.DecisionsEnabled = false;
			AiDispatchConfig.MinimumConfidence = 0.6;
			AiDispatchConfig.AdmissionWaitSeconds = 0;
			IntakeConfig.Enabled = true;
			AiConfig.SendCacheSalt = false;
			AiConfig.Endpoint = "https://inference.example.invalid/v1/chat/completions";
			AiConfig.AllowPrivateEndpoint = false;
			AiConfig.ApiKey = "unit-test-only";
			AiConfig.Model = "Qwen/Qwen3-14B-AWQ";
			AiConfig.ModelRevision = new string('a', 40);
			AiConfig.RuntimeDigest = "sha256:" + new string('b', 64);
			AiConfig.SelfHostedDepartmentIds = "";
			AiConfig.MonthlyTokenLimit = 2000000;
			_saved.Clear();
			_notes.Clear();
			_requests.Clear();

			_call = new Call
			{
				CallId = CallId, DepartmentId = DepartmentId, Number = "26-700", Name = "Dedicated number SMS +17755550100: +15305550199", NatureOfCall = "Car crash",
				Priority = 22, State = (int)CallStates.Pending, CallSource = (int)CallSources.Intake
			};

			var access = new Mock<IEnhancedAiAccessService>();
			access.Setup(a => a.CanUseAsync(DepartmentId, FeatureFlagKeys.AiIntake)).ReturnsAsync(true);
			var departments = new Mock<IDepartmentsService>();
			departments.Setup(d => d.GetDepartmentByIdAsync(DepartmentId, false)).ReturnsAsync(new Department { DepartmentId = DepartmentId, ManagingUserId = "owner" });
			_calls = new Mock<ICallsService>();
			_calls.Setup(c => c.GetCallByIdAsync(CallId, true)).ReturnsAsync(() => _call);
			_calls.Setup(c => c.GetCallTypesForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<CallType> { new CallType { CallTypeId = 11, Type = "Vehicle accident" } });
			_calls.Setup(c => c.GetActiveCallPrioritiesForDepartmentAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new List<DepartmentCallPriority>
			{
				new DepartmentCallPriority { DepartmentCallPriorityId = 21, Name = "High", Sort = 1 },
				new DepartmentCallPriority { DepartmentCallPriorityId = 22, Name = "Low", Sort = 2 }
			});
			_calls.Setup(c => c.GetActiveCallsByDepartmentAsync(DepartmentId)).ReturnsAsync(new List<Call>());
			_calls.Setup(c => c.SaveCallAsync(It.IsAny<Call>(), It.IsAny<CancellationToken>())).ReturnsAsync((Call c, CancellationToken _) => { _saved.Add(c); return c; });
			_calls.Setup(c => c.SaveCallNoteAsync(It.IsAny<CallNote>(), It.IsAny<CancellationToken>())).ReturnsAsync((CallNote n, CancellationToken _) => { _notes.Add(n); return n; });
			_audits = new Mock<IAiDispatchAuditRepository>();
			_audits.Setup(a => a.TryClaimAsync(It.IsAny<AiDispatchAuditRow>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
			var settings = new Mock<IAiDispatchConfigRepository>();
			settings.Setup(r => r.GetAsync(DepartmentId, It.IsAny<CancellationToken>())).ReturnsAsync(new DepartmentAiDispatchConfig { DepartmentId = DepartmentId });
			var admission = new Mock<IAiBackgroundAdmission>();
			admission.Setup(a => a.ReserveBackgroundAsync(DepartmentId, "AiDispatch", It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>(), It.IsAny<int?>()))
				.ReturnsAsync(new AiUsageReservation("r1", DepartmentId, "system:background", 8192, DateTime.UtcNow.AddMinutes(2)));
			var usage = new Mock<IAiUsageMeter>();
			usage.Setup(u => u.RemainingAsync(DepartmentId, It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(1_000_000);
			var llm = new Mock<ILlmClient>();
			llm.Setup(l => l.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
				.Callback((LlmRequest r, CancellationToken _) => _requests.Add(r))
				.ReturnsAsync(new LlmResult(null, new[] { new LlmToolCall("t1", AiDispatchPrompt.ToolName, JsonSerializer.Serialize(new
				{
					isDispatch = true, confidence = 0.9, title = "Vehicle accident, 123 Main St", callTypeId = 11, priorityId = 21, address = "123 MAIN ST, SPRINGFIELD",
					contactName = "Jane Q Public", contactNumber = (string)null, incidentNumber = (string)null, possibleDuplicateOfCallId = (int?)null,
					summary = "Two-car crash at 123 Main St."
				})) }, 900, 100, "tool_calls"));
			_conversations = new Mock<IIntakeConversationOperations>();
			_conversations.Setup(c => c.GetParticipantTranscriptAsync(DepartmentId, "conv-1", It.IsAny<int>())).ReturnsAsync(Transcript);

			_service = new AiDispatchEnrichmentService(access.Object, Mock.Of<IFeatureToggleService>(), departments.Object, _calls.Object, Mock.Of<IGeoLocationProvider>(),
				Mock.Of<IDepartmentDataProtectionService>(), _audits.Object, settings.Object, admission.Object, usage.Object, new Lazy<ILlmClient>(() => llm.Object),
				new Lazy<IDecisionClient>(() => Mock.Of<IDecisionClient>()), Mock.Of<IEventAggregator>(), new FixedClock(),
				new Lazy<IIntakeConversationOperations>(() => _conversations.Object));
		}

		[TearDown]
		public void TearDown()
		{
			(AiDispatchConfig.EnrichEnabled, AiConfig.Endpoint, AiConfig.AllowPrivateEndpoint, AiConfig.ApiKey, AiConfig.Model, AiConfig.ModelRevision,
				AiConfig.RuntimeDigest, AiConfig.SelfHostedDepartmentIds, AiConfig.MonthlyTokenLimit, AiDispatchConfig.DecisionsEnabled, AiDispatchConfig.MinimumConfidence,
				AiDispatchConfig.AdmissionWaitSeconds, IntakeConfig.Enabled, AiConfig.SendCacheSalt) = _savedConfig;
		}

		private Task<string> Refresh(bool complete) => _service.EnrichAsync(new AiDispatchQueueItem
		{
			DepartmentId = DepartmentId, CallId = CallId, Channel = AiDispatchChannels.IntakeSms, NameIsPlaceholder = true, IntakeConversationId = "conv-1",
			ConversationComplete = complete
		}, CancellationToken.None);

		[Test]
		public async Task a_refresh_reads_the_transcript_and_fills_what_is_still_empty_without_a_second_audit_claim()
		{
			(await Refresh(false)).Should().Be(AiDispatchOutcomes.Applied);

			var user = _requests.Single().Messages.Single(m => m.Role == "user").Content;
			user.Should().Contain("SMS conversation with the department's dispatch number");
			user.Should().Contain("It's at 123 MAIN ST, SPRINGFIELD");
			var saved = _saved.Single();
			saved.Address.Should().Be("123 MAIN ST, SPRINGFIELD", "the address arrived in a later text");
			saved.Type.Should().Be("Vehicle accident");
			saved.ContactName.Should().Be("Jane Q Public");
			saved.State.Should().Be((int)CallStates.Pending);
			_audits.Verify(a => a.TryClaimAsync(It.IsAny<AiDispatchAuditRow>(), It.IsAny<CancellationToken>()), Times.Never, "the call's one audit row belongs to its first enrichment");
			_audits.Verify(a => a.CompleteAsync(It.IsAny<AiDispatchAuditRow>(), It.IsAny<CancellationToken>()), Times.Never);
			_notes.Should().BeEmpty("the AI note waits for the conversation's last turn");
		}

		[Test]
		public async Task the_last_refresh_writes_the_ai_note()
		{
			await Refresh(true);

			_notes.Should().ContainSingle().Which.Note.Should().StartWith("AI (Enhanced AI, verify before relying on it): Summary: Two-car crash");
		}

		[Test]
		public async Task a_turn_item_is_never_treated_as_an_enrichment()
		{
			(await _service.EnrichAsync(new AiDispatchQueueItem
			{
				DepartmentId = DepartmentId, CallId = CallId, Channel = AiDispatchChannels.IntakeConversation, IntakeConversationId = "conv-1"
			}, CancellationToken.None)).Should().Be("NotEnrichment");
			_requests.Should().BeEmpty();
		}

		[Test]
		public async Task the_first_enrichment_still_claims_once()
		{
			(await _service.EnrichAsync(new AiDispatchQueueItem { DepartmentId = DepartmentId, CallId = CallId, Channel = AiDispatchChannels.IntakeSms }, CancellationToken.None))
				.Should().Be("Duplicate");
			_conversations.Verify(c => c.GetParticipantTranscriptAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
		}
	}
}
