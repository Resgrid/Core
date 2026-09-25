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
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Queue;
using Resgrid.Model.Services;
using Resgrid.Services.AiDispatch;

namespace Resgrid.Tests.AiDispatch
{
	/// <summary>
	/// Enrich mode never changes what was dispatched: it fills empty fields with verbatim values, notes suggestions, and on any failure
	/// leaves the call exactly as GenericTemplate built it (enhanced-ai-addon-plan.md §4).
	/// </summary>
	[TestFixture, NonParallelizable]
	public class AiDispatchEnrichmentServiceTests
	{
		private const int DepartmentId = 55;
		private const int CallId = 700;
		private const string Body = "STRUCTURE FIRE\n123 MAIN ST, SPRINGFIELD\nCaller: Jane Q Public (555) 123-4567\nIncident #F26-00417";

		private sealed class FixedClock : TimeProvider
		{
			public override DateTimeOffset GetUtcNow() => new DateTimeOffset(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);
		}

		private Mock<IEnhancedAiAccessService> _access;
		private Mock<IDepartmentsService> _departments;
		private Mock<ICallsService> _calls;
		private Mock<IGeoLocationProvider> _geo;
		private Mock<IDepartmentDataProtectionService> _protection;
		private Mock<IAiDispatchAuditRepository> _audits;
		private Mock<IAiDispatchConfigRepository> _settings;
		private DepartmentAiDispatchConfig _config;
		private Mock<IAiBackgroundAdmission> _admission;
		private Mock<IAiUsageMeter> _usage;
		private Mock<ILlmClient> _llm;
		private Mock<IEventAggregator> _events;
		private Call _call;
		private Call _related;
		private AiDispatchAuditRow _completed;
		private readonly List<CallNote> _notes = new List<CallNote>();
		private readonly List<Call> _saved = new List<Call>();
		private AiDispatchEnrichmentService _service;
		private (bool, string, bool, string, string, string, string, string, int) _savedConfig;
		private (bool, double, int) _savedDispatch;

		[SetUp]
		public void SetUp()
		{
			_savedConfig = (AiDispatchConfig.EnrichEnabled, AiConfig.Endpoint, AiConfig.AllowPrivateEndpoint, AiConfig.ApiKey, AiConfig.Model, AiConfig.ModelRevision,
				AiConfig.RuntimeDigest, AiConfig.SelfHostedDepartmentIds, AiConfig.MonthlyTokenLimit);
			_savedDispatch = (AiDispatchConfig.EnrichEnabled, AiDispatchConfig.MinimumConfidence, AiDispatchConfig.AdmissionWaitSeconds);
			AiDispatchConfig.EnrichEnabled = true;
			AiDispatchConfig.MinimumConfidence = 0.6;
			AiDispatchConfig.AdmissionWaitSeconds = 0;
			AiConfig.Endpoint = "https://inference.example.invalid/v1/chat/completions";
			AiConfig.AllowPrivateEndpoint = false;
			AiConfig.ApiKey = "unit-test-only";
			AiConfig.Model = "Qwen/Qwen3-14B-AWQ";
			AiConfig.ModelRevision = new string('a', 40);
			AiConfig.RuntimeDigest = "sha256:" + new string('b', 64);
			AiConfig.SelfHostedDepartmentIds = "";
			AiConfig.MonthlyTokenLimit = 2000000;

			_call = new Call { CallId = CallId, DepartmentId = DepartmentId, Number = "26-121", Name = "Dispatch Email", NatureOfCall = Body, Priority = 22, State = (int)CallStates.Active };
			_related = new Call { CallId = 301, DepartmentId = DepartmentId, Number = "26-120", Name = "Vehicle Accident", State = (int)CallStates.Active, LoggedOn = new DateTime(2026, 9, 25, 11, 50, 0, DateTimeKind.Utc) };
			_notes.Clear();
			_saved.Clear();
			_completed = null;

			_access = new Mock<IEnhancedAiAccessService>();
			_access.Setup(a => a.CanUseAsync(DepartmentId, FeatureFlagKeys.AiDispatchTemplate)).ReturnsAsync(true);
			_departments = new Mock<IDepartmentsService>();
			_departments.Setup(d => d.GetDepartmentEmailSettingsAsync(DepartmentId)).ReturnsAsync(new DepartmentCallEmail { DepartmentId = DepartmentId, FormatType = (int)CallEmailTypes.AI });
			_departments.Setup(d => d.GetDepartmentByIdAsync(DepartmentId, false)).ReturnsAsync(new Department { DepartmentId = DepartmentId, ManagingUserId = "owner" });
			_calls = new Mock<ICallsService>();
			_calls.Setup(c => c.GetCallByIdAsync(CallId, true)).ReturnsAsync(() => _call);
			_calls.Setup(c => c.GetCallByIdAsync(301, true)).ReturnsAsync(() => _related);
			_calls.Setup(c => c.GetCallTypesForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<CallType> { new CallType { CallTypeId = 11, Type = "Structure Fire" } });
			_calls.Setup(c => c.GetActiveCallPrioritiesForDepartmentAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new List<DepartmentCallPriority>
			{
				new DepartmentCallPriority { DepartmentCallPriorityId = 21, Name = "High", Sort = 1 },
				new DepartmentCallPriority { DepartmentCallPriorityId = 22, Name = "Low", Sort = 2 }
			});
			_calls.Setup(c => c.GetActiveCallsByDepartmentAsync(DepartmentId)).ReturnsAsync(() => new List<Call> { _call, _related });
			_calls.Setup(c => c.SaveCallAsync(It.IsAny<Call>(), It.IsAny<CancellationToken>())).ReturnsAsync((Call c, CancellationToken _) => { _saved.Add(c); return c; });
			_calls.Setup(c => c.SaveCallNoteAsync(It.IsAny<CallNote>(), It.IsAny<CancellationToken>())).ReturnsAsync((CallNote n, CancellationToken _) => { _notes.Add(n); return n; });
			_geo = new Mock<IGeoLocationProvider>();
			_geo.Setup(g => g.GetLatLonFromAddress("123 Main St.")).ReturnsAsync("39.78,-89.65");
			_protection = new Mock<IDepartmentDataProtectionService>();
			_audits = new Mock<IAiDispatchAuditRepository>();
			_audits.Setup(a => a.TryClaimAsync(It.IsAny<AiDispatchAuditRow>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
			_audits.Setup(a => a.CompleteAsync(It.IsAny<AiDispatchAuditRow>(), It.IsAny<CancellationToken>())).Callback((AiDispatchAuditRow r, CancellationToken _) => _completed = r).Returns(Task.CompletedTask);
			_config = new DepartmentAiDispatchConfig { DepartmentId = DepartmentId };
			_settings = new Mock<IAiDispatchConfigRepository>();
			_settings.Setup(r => r.GetAsync(DepartmentId, It.IsAny<CancellationToken>())).ReturnsAsync(() => _config);
			_admission = new Mock<IAiBackgroundAdmission>();
			_admission.Setup(a => a.ReserveBackgroundAsync(DepartmentId, "AiDispatch", AdminAssistAskTiers.EnhancedAi, It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>(), It.IsAny<int?>()))
				.ReturnsAsync(new AiUsageReservation("r1", DepartmentId, "system:background", 8192, DateTime.UtcNow.AddMinutes(2)));
			_usage = new Mock<IAiUsageMeter>();
			_usage.Setup(u => u.RemainingAsync(DepartmentId, It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(1_000_000);
			_llm = new Mock<ILlmClient>();
			Reply(new { isDispatch = true, confidence = 0.92, title = "Structure fire, 123 Main St", callTypeId = 11, priorityId = 21, address = "123 Main St.",
				contactName = "Jane Q Public", contactNumber = "555-123-4567", incidentNumber = "F26-00417", possibleDuplicateOfCallId = (int?)null, summary = "Structure fire reported at 123 Main St." });
			_events = new Mock<IEventAggregator>();
			_service = new AiDispatchEnrichmentService(_access.Object, Mock.Of<IFeatureToggleService>(), _departments.Object, _calls.Object, _geo.Object,
				_protection.Object, _audits.Object, _settings.Object, _admission.Object, _usage.Object, new Lazy<ILlmClient>(() => _llm.Object), _events.Object, new FixedClock());
		}

		[TearDown]
		public void TearDown()
		{
			(AiDispatchConfig.EnrichEnabled, AiConfig.Endpoint, AiConfig.AllowPrivateEndpoint, AiConfig.ApiKey, AiConfig.Model, AiConfig.ModelRevision,
				AiConfig.RuntimeDigest, AiConfig.SelfHostedDepartmentIds, AiConfig.MonthlyTokenLimit) = _savedConfig;
			(AiDispatchConfig.EnrichEnabled, AiDispatchConfig.MinimumConfidence, AiDispatchConfig.AdmissionWaitSeconds) = _savedDispatch;
		}

		private void Reply(object arguments) => _llm.Setup(l => l.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(new LlmResult(null, new[] { new LlmToolCall("t1", AiDispatchPrompt.ToolName, JsonSerializer.Serialize(arguments)) }, 900, 100, "tool_calls"));

		private Task<string> Enrich() => _service.EnrichAsync(new AiDispatchQueueItem { DepartmentId = DepartmentId, CallId = CallId, Channel = 1 }, CancellationToken.None);

		private void VerifyCallUntouched()
		{
			_saved.Should().BeEmpty();
			_notes.Should().BeEmpty();
			_events.Verify(e => e.SendMessage(It.IsAny<CallUpdatedEvent>()), Times.Never);
		}

		[Test]
		public async Task Empty_fields_are_filled_with_verbatim_values_and_a_labelled_note_but_priority_is_only_suggested()
		{
			(await Enrich()).Should().Be(AiDispatchOutcomes.Applied);

			var saved = _saved.Single();
			saved.Type.Should().Be("Structure Fire");
			saved.Address.Should().Be("123 Main St.");
			saved.GeoLocationData.Should().Be("39.78,-89.65");
			saved.ContactName.Should().Be("Jane Q Public");
			saved.ContactNumber.Should().Be("555-123-4567");
			saved.IncidentNumber.Should().Be("F26-00417");
			saved.Name.Should().Be("Structure fire, 123 Main St", "only the placeholder subject is replaced");
			saved.Priority.Should().Be(22, "enrichment never changes the priority people were paged with");
			_notes.Single().Note.Should().StartWith("AI (Enhanced AI, verify before relying on it):").And.Contain("Suggested priority: High.");
			_notes.Single().Source.Should().Be((int)CallNoteSources.System);
			_events.Verify(e => e.SendMessage(It.Is<CallUpdatedEvent>(u => u.Call.CallId == CallId)), Times.Once);
			_completed.Outcome.Should().Be(AiDispatchOutcomes.Applied);
			_completed.AppliedFields.Should().Be("Type,Address,GeoLocationData,ContactName,ContactNumber,IncidentNumber,Name,Note");
			_usage.Verify(u => u.CompleteAsync(It.Is<AiUsageReservation>(r => r.Id == "r1"), 1000, "Answered", It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task Values_a_dispatcher_already_set_are_never_overwritten()
		{
			_call.Name = "Working fire - command";
			_call.Type = "Commercial Fire";
			_call.Address = "123 Main Street, Springfield";
			_call.GeoLocationData = "39.7,-89.6";

			(await Enrich()).Should().Be(AiDispatchOutcomes.Applied);

			var saved = _saved.Single();
			saved.Name.Should().Be("Working fire - command");
			saved.Type.Should().Be("Commercial Fire");
			saved.Address.Should().Be("123 Main Street, Springfield");
			saved.GeoLocationData.Should().Be("39.7,-89.6");
			_geo.Verify(g => g.GetLatLonFromAddress(It.IsAny<string>()), Times.Never);
		}

		[Test]
		public async Task A_possible_duplicate_is_noted_on_both_calls_and_nothing_is_merged_or_deleted()
		{
			Reply(new { isDispatch = true, confidence = 0.8, title = (string)null, callTypeId = (int?)null, priorityId = (int?)null, address = (string)null, contactName = (string)null,
				contactNumber = (string)null, incidentNumber = (string)null, possibleDuplicateOfCallId = 301, summary = "Update on the vehicle accident." });

			(await Enrich()).Should().Be(AiDispatchOutcomes.Applied);

			_saved.Should().BeEmpty("no field changed");
			_notes.Should().HaveCount(2);
			_notes[0].CallId.Should().Be(CallId);
			_notes[0].Note.Should().Contain("call 26-120");
			_notes[1].CallId.Should().Be(301);
			_notes[1].Note.Should().Contain("call 26-121");
			_completed.RelatedCallId.Should().Be(301);
			_calls.Verify(c => c.DeleteCallByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task A_message_that_is_not_a_dispatch_is_only_flagged()
		{
			Reply(new { isDispatch = false, confidence = 0.9, title = "Auto reply", callTypeId = 11, priorityId = (int?)null, address = "123 Main St.", contactName = (string)null,
				contactNumber = (string)null, incidentNumber = (string)null, possibleDuplicateOfCallId = (int?)null, summary = (string)null });

			(await Enrich()).Should().Be(AiDispatchOutcomes.Applied);

			_saved.Should().BeEmpty();
			_notes.Single().Note.Should().Contain("may not be a dispatch");
		}

		[Test]
		public async Task Low_confidence_changes_nothing()
		{
			Reply(new { isDispatch = true, confidence = 0.3, title = "x", callTypeId = 11, priorityId = 21, address = "123 Main St.", contactName = (string)null,
				contactNumber = (string)null, incidentNumber = (string)null, possibleDuplicateOfCallId = (int?)null, summary = "x" });

			(await Enrich()).Should().Be(AiDispatchOutcomes.LowConfidence);
			VerifyCallUntouched();
		}

		[Test]
		public async Task An_unavailable_model_changes_nothing_and_is_not_charged()
		{
			_llm.Setup(l => l.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>())).ThrowsAsync(new LlmUnavailableException());

			(await Enrich()).Should().Be(AiDispatchOutcomes.Unavailable);
			VerifyCallUntouched();
			_completed.Outcome.Should().Be(AiDispatchOutcomes.Unavailable);
			_usage.Verify(u => u.CompleteAsync(It.IsAny<AiUsageReservation>(), 0, "Unavailable", It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task Prose_instead_of_the_tool_is_invalid_output_and_changes_nothing()
		{
			_llm.Setup(l => l.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new LlmResult("This is a structure fire.", Array.Empty<LlmToolCall>(), 900, 20, "stop"));

			(await Enrich()).Should().Be(AiDispatchOutcomes.InvalidOutput);
			VerifyCallUntouched();
		}

		[Test]
		public async Task A_redelivered_item_is_skipped_before_any_model_call()
		{
			_audits.Setup(a => a.TryClaimAsync(It.IsAny<AiDispatchAuditRow>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

			(await Enrich()).Should().Be("Duplicate");
			_llm.Verify(l => l.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()), Times.Never);
			VerifyCallUntouched();
		}

		[Test]
		public async Task Protected_data_departments_are_skipped_until_an_inference_purpose_exists()
		{
			_protection.Setup(p => p.ShouldEncryptNewWritesAsync(DepartmentId)).ReturnsAsync(true);

			(await Enrich()).Should().Be(AiDispatchOutcomes.ProtectionUnsupported);
			_llm.Verify(l => l.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()), Times.Never);
			VerifyCallUntouched();
		}

		[Test]
		public async Task Without_entitlement_or_the_ai_format_nothing_is_claimed()
		{
			_access.Setup(a => a.CanUseAsync(DepartmentId, FeatureFlagKeys.AiDispatchTemplate)).ReturnsAsync(false);
			(await Enrich()).Should().Be("NotEntitled");

			_access.Setup(a => a.CanUseAsync(DepartmentId, FeatureFlagKeys.AiDispatchTemplate)).ReturnsAsync(true);
			_departments.Setup(d => d.GetDepartmentEmailSettingsAsync(DepartmentId)).ReturnsAsync(new DepartmentCallEmail { FormatType = (int)CallEmailTypes.Generic });
			(await Enrich()).Should().Be("NotEntitled");

			AiDispatchConfig.EnrichEnabled = false;
			(await Enrich()).Should().Be("Disabled");
			_audits.Verify(a => a.TryClaimAsync(It.IsAny<AiDispatchAuditRow>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task An_exhausted_budget_or_busy_gpu_changes_nothing()
		{
			_usage.Setup(u => u.RemainingAsync(DepartmentId, It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(100);
			(await Enrich()).Should().Be(AiDispatchOutcomes.BudgetExhausted);

			_usage.Setup(u => u.RemainingAsync(DepartmentId, It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(1_000_000);
			_admission.Setup(a => a.ReserveBackgroundAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>(), It.IsAny<int?>()))
				.ReturnsAsync((AiUsageReservation)null);
			(await Enrich()).Should().Be(AiDispatchOutcomes.Busy);

			_llm.Verify(l => l.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()), Times.Never);
			VerifyCallUntouched();
		}

		[Test]
		public async Task A_call_closed_while_queued_is_left_alone()
		{
			_call.State = (int)CallStates.Closed;
			(await Enrich()).Should().Be(AiDispatchOutcomes.CallNotActive);
			VerifyCallUntouched();
		}

		[Test]
		public async Task Each_enrichment_can_be_switched_off_by_the_department()
		{
			_config = new DepartmentAiDispatchConfig
			{
				DepartmentId = DepartmentId, FillCallType = false, FillAddress = false, FillContact = false, FillIncidentNumber = false,
				RenamePlaceholder = false, AddSummaryNote = false, FlagRelatedCalls = false
			};

			(await Enrich()).Should().Be(AiDispatchOutcomes.NoChange);
			VerifyCallUntouched();
		}

		[Test]
		public async Task Only_the_enrichments_left_on_are_applied()
		{
			_config = new DepartmentAiDispatchConfig { DepartmentId = DepartmentId, FillAddress = false, FillContact = false, AddSummaryNote = false };

			(await Enrich()).Should().Be(AiDispatchOutcomes.Applied);
			var saved = _saved.Single();
			saved.Type.Should().Be("Structure Fire");
			saved.Address.Should().BeNull();
			saved.ContactName.Should().BeNull();
			saved.IncidentNumber.Should().Be("F26-00417");
			_notes.Should().BeEmpty("the summary note is off and nothing else was flagged");
		}

		[Test]
		public async Task A_department_confidence_floor_above_the_reply_changes_nothing()
		{
			_config = new DepartmentAiDispatchConfig { DepartmentId = DepartmentId, MinimumConfidence = 0.95m };

			(await Enrich()).Should().Be(AiDispatchOutcomes.LowConfidence);
			VerifyCallUntouched();
		}

		[Test]
		public async Task The_department_ai_dispatch_cap_stops_before_any_model_call()
		{
			_config = new DepartmentAiDispatchConfig { DepartmentId = DepartmentId, MonthlyTokenCap = 10000 };
			_admission.Setup(a => a.GetFeatureUsageAsync(DepartmentId, "AiDispatch", It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(5000);

			(await Enrich()).Should().Be(AiDispatchOutcomes.DispatchCapReached);
			_llm.Verify(l => l.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()), Times.Never);
			VerifyCallUntouched();
		}

		[Test]
		public async Task The_cap_is_enforced_inside_the_admission_lock_too()
		{
			_config = new DepartmentAiDispatchConfig { DepartmentId = DepartmentId, MonthlyTokenCap = 100000 };

			(await Enrich()).Should().Be(AiDispatchOutcomes.Applied);
			_admission.Verify(a => a.ReserveBackgroundAsync(DepartmentId, "AiDispatch", AdminAssistAskTiers.EnhancedAi, It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>(), 100000), Times.Once);
		}

		[Test]
		public async Task A_sender_outside_the_allowlist_is_audited_and_never_reaches_the_model()
		{
			(await _service.EnrichAsync(new AiDispatchQueueItem { DepartmentId = DepartmentId, CallId = CallId, SenderNotAllowed = true }, CancellationToken.None))
				.Should().Be(AiDispatchOutcomes.SenderNotAllowed);

			_completed.Outcome.Should().Be(AiDispatchOutcomes.SenderNotAllowed);
			_llm.Verify(l => l.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()), Times.Never);
			VerifyCallUntouched();
		}

		[Test]
		public async Task Activity_older_than_the_department_retention_is_pruned()
		{
			_config = new DepartmentAiDispatchConfig { DepartmentId = DepartmentId, AuditRetentionDays = 90 };

			await Enrich();

			_audits.Verify(a => a.PruneAsync(DepartmentId, new DateTime(2026, 6, 27, 12, 0, 0, DateTimeKind.Utc), It.IsAny<CancellationToken>()), Times.Once);
		}
	}
}
