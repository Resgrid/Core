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
	/// Enhanced AI enrichment of intake calls (channels 4-8): pending calls are enrichable, Ai.Intake / Ai.VoiceIntake gate them
	/// instead of the department's email format, ADP departments are still skipped (D9), the model is told the call is pending,
	/// and the "{channel}: {sender}" fallback name may be replaced. The dispatch email channels behave exactly as before.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class AiDispatchIntakeEnrichmentTests
	{
		private const int DepartmentId = 55;
		private const int CallId = 700;
		private const string Body = "Patient transfer, 123 MAIN ST, SPRINGFIELD. Contact Jane Q Public (555) 123-4567";

		private sealed class FixedClock : TimeProvider
		{
			public override DateTimeOffset GetUtcNow() => new DateTimeOffset(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);
		}

		private Mock<IEnhancedAiAccessService> _access;
		private Mock<IFeatureToggleService> _flags;
		private Mock<IDepartmentsService> _departments;
		private Mock<ICallsService> _calls;
		private Mock<IDepartmentDataProtectionService> _protection;
		private Mock<IAiDispatchAuditRepository> _audits;
		private Mock<IAiUsageMeter> _usage;
		private Mock<IAiBackgroundAdmission> _admission;
		private Mock<ILlmClient> _llm;
		private Call _call;
		private AiDispatchAuditRow _completed;
		private readonly List<Call> _saved = new List<Call>();
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
			// The placeholder rename needs the enqueue-time fingerprint, which needs a usable audit key.
			_savedAuditKey = AiConfig.AuditHmacKey;
			AiConfig.AuditHmacKey = Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());
			_saved.Clear();
			_requests.Clear();
			_completed = null;

			_call = new Call
			{
				CallId = CallId, DepartmentId = DepartmentId, Number = "26-700", Name = "Hospital line: +15305550199", NatureOfCall = Body, Priority = 22,
				State = (int)CallStates.Pending, CallSource = (int)CallSources.Intake
			};

			_access = new Mock<IEnhancedAiAccessService>();
			_access.Setup(a => a.CanUseAsync(DepartmentId, FeatureFlagKeys.AiIntake)).ReturnsAsync(true);
			_access.Setup(a => a.CanUseAsync(DepartmentId, FeatureFlagKeys.AiVoiceIntake)).ReturnsAsync(true);
			_flags = new Mock<IFeatureToggleService>();
			_departments = new Mock<IDepartmentsService>();
			// The email format is not AI: intake must not depend on it.
			_departments.Setup(d => d.GetDepartmentEmailSettingsAsync(DepartmentId)).ReturnsAsync(new DepartmentCallEmail { DepartmentId = DepartmentId, FormatType = (int)CallEmailTypes.Generic });
			_departments.Setup(d => d.GetDepartmentByIdAsync(DepartmentId, false)).ReturnsAsync(new Department { DepartmentId = DepartmentId, ManagingUserId = "owner" });
			_calls = new Mock<ICallsService>();
			_calls.Setup(c => c.GetCallByIdAsync(CallId, true)).ReturnsAsync(() => _call);
			_calls.Setup(c => c.GetCallTypesForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<CallType> { new CallType { CallTypeId = 11, Type = "Transfer" } });
			_calls.Setup(c => c.GetActiveCallPrioritiesForDepartmentAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new List<DepartmentCallPriority>
			{
				new DepartmentCallPriority { DepartmentCallPriorityId = 21, Name = "High", Sort = 1 },
				new DepartmentCallPriority { DepartmentCallPriorityId = 22, Name = "Low", Sort = 2 }
			});
			_calls.Setup(c => c.GetActiveCallsByDepartmentAsync(DepartmentId)).ReturnsAsync(new List<Call>());
			_calls.Setup(c => c.SaveCallAsync(It.IsAny<Call>(), It.IsAny<CancellationToken>())).ReturnsAsync((Call c, CancellationToken _) => { _saved.Add(c); return c; });
			_calls.Setup(c => c.SaveCallNoteAsync(It.IsAny<CallNote>(), It.IsAny<CancellationToken>())).ReturnsAsync((CallNote n, CancellationToken _) => n);
			_protection = new Mock<IDepartmentDataProtectionService>();
			_audits = new Mock<IAiDispatchAuditRepository>();
			_audits.Setup(a => a.TryClaimAsync(It.IsAny<AiDispatchAuditRow>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
			_audits.Setup(a => a.CompleteAsync(It.IsAny<AiDispatchAuditRow>(), It.IsAny<CancellationToken>())).Callback((AiDispatchAuditRow r, CancellationToken _) => _completed = r).Returns(Task.CompletedTask);
			var settings = new Mock<IAiDispatchConfigRepository>();
			settings.Setup(r => r.GetAsync(DepartmentId, It.IsAny<CancellationToken>())).ReturnsAsync(new DepartmentAiDispatchConfig { DepartmentId = DepartmentId });
			_admission = new Mock<IAiBackgroundAdmission>();
			_admission.Setup(a => a.ReserveBackgroundAsync(DepartmentId, "AiDispatch", It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>(), It.IsAny<int?>()))
				.ReturnsAsync(new AiUsageReservation("r1", DepartmentId, "system:background", 8192, DateTime.UtcNow.AddMinutes(2)));
			_usage = new Mock<IAiUsageMeter>();
			_usage.Setup(u => u.RemainingAsync(DepartmentId, It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(1_000_000);
			_llm = new Mock<ILlmClient>();
			_llm.Setup(l => l.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
				.Callback((LlmRequest r, CancellationToken _) => _requests.Add(r))
				.ReturnsAsync(new LlmResult(null, new[] { new LlmToolCall("t1", AiDispatchPrompt.ToolName, JsonSerializer.Serialize(new
				{
					isDispatch = true, confidence = 0.9, title = "Patient transfer, 123 Main St", callTypeId = 11, priorityId = 21, address = "123 MAIN ST, SPRINGFIELD",
					contactName = "Jane Q Public", contactNumber = "(555) 123-4567", incidentNumber = (string)null, possibleDuplicateOfCallId = (int?)null,
					summary = "Patient transfer requested at 123 Main St."
				})) }, 900, 100, "tool_calls"));

			_screening = new Mock<IIntakeScreeningService>();
			_screening.Setup(s => s.SetAiScreeningAsync(DepartmentId, CallId, It.IsAny<bool>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

			_service = new AiDispatchEnrichmentService(_access.Object, _flags.Object, _departments.Object, _calls.Object, Mock.Of<IGeoLocationProvider>(), _protection.Object,
				_audits.Object, settings.Object, _admission.Object, _usage.Object, new Lazy<ILlmClient>(() => _llm.Object),
				new Lazy<IDecisionClient>(() => Mock.Of<IDecisionClient>()), Mock.Of<IEventAggregator>(), new FixedClock(), Mock.Of<IDispatcherDeskConfigService>(),
				screening: new Lazy<IIntakeScreeningService>(() => _screening.Object));
		}

		private Mock<IIntakeScreeningService> _screening;

		private void ModelReplies(bool isDispatch, double confidence)
		{
			_llm.Setup(l => l.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
				.Callback((LlmRequest r, CancellationToken _) => _requests.Add(r))
				.ReturnsAsync(new LlmResult(null, new[] { new LlmToolCall("t1", AiDispatchPrompt.ToolName, JsonSerializer.Serialize(new
				{
					isDispatch, confidence, title = (string)null, callTypeId = (int?)null, priorityId = (int?)null, address = (string)null,
					contactName = (string)null, contactNumber = (string)null, incidentNumber = (string)null, possibleDuplicateOfCallId = (int?)null, summary = (string)null
				})) }, 900, 100, "tool_calls"));
		}

		[Test]
		public async Task a_confident_not_a_dispatch_reading_labels_the_intake_call_likely_not_an_incident()
		{
			ModelReplies(isDispatch: false, confidence: 0.92);
			_screening.Setup(s => s.SetAiScreeningAsync(DepartmentId, CallId, true, It.IsAny<CancellationToken>())).ReturnsAsync(true);

			(await Enrich(AiDispatchChannels.IntakeSms)).Should().Be(AiDispatchOutcomes.Applied);

			_screening.Verify(s => s.SetAiScreeningAsync(DepartmentId, CallId, true, It.IsAny<CancellationToken>()), Times.Once);
			_completed.AppliedFields.Should().Contain("Screening");
			_saved.Should().BeEmpty("a label changes no call field");
		}

		[Test]
		public async Task a_dispatch_reading_clears_the_label_so_a_later_turn_can_undo_it()
		{
			(await Enrich(AiDispatchChannels.IntakeSms)).Should().Be(AiDispatchOutcomes.Applied);

			_screening.Verify(s => s.SetAiScreeningAsync(DepartmentId, CallId, false, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task a_low_confidence_reading_never_labels_the_call()
		{
			ModelReplies(isDispatch: false, confidence: 0.3);

			(await Enrich(AiDispatchChannels.IntakeSms)).Should().Be(AiDispatchOutcomes.LowConfidence);

			_screening.Verify(s => s.SetAiScreeningAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task a_label_that_cannot_be_written_never_fails_the_enrichment()
		{
			ModelReplies(isDispatch: false, confidence: 0.92);
			_screening.Setup(s => s.SetAiScreeningAsync(DepartmentId, CallId, true, It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("db down"));

			var outcome = await Enrich(AiDispatchChannels.IntakeSms);

			outcome.Should().NotBe(AiDispatchOutcomes.Unavailable);
			_completed.AppliedFields?.Should().NotContain("Screening");
		}

		[Test]
		public async Task a_note_that_fails_on_a_not_a_dispatch_reading_still_leaves_the_label()
		{
			// No field is saved for a "not a dispatch" reading, so the note used to be the only write before the label: its failure ended
			// the enrichment and the label was never written (a redelivery is a Duplicate and cannot recover it).
			ModelReplies(isDispatch: false, confidence: 0.92);
			_screening.Setup(s => s.SetAiScreeningAsync(DepartmentId, CallId, true, It.IsAny<CancellationToken>())).ReturnsAsync(true);
			_calls.Setup(c => c.SaveCallNoteAsync(It.IsAny<CallNote>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("db down"));

			var outcome = await Enrich(AiDispatchChannels.IntakeSms);

			_calls.Verify(c => c.SaveCallNoteAsync(It.IsAny<CallNote>(), It.IsAny<CancellationToken>()), Times.Once, "the reading's review note was attempted");
			_screening.Verify(s => s.SetAiScreeningAsync(DepartmentId, CallId, true, It.IsAny<CancellationToken>()), Times.Once);
			outcome.Should().Be(AiDispatchOutcomes.Applied);
			_completed.AppliedFields.Should().Be("Screening");
		}

		[Test]
		public async Task without_an_enqueue_fingerprint_the_placeholder_name_is_left_alone()
		{
			// An item queued without a usable audit key carries no fingerprint: comparing with the name the worker first read would
			// overwrite a rename made between the enqueue and that read, so the name is never replaced.
			(await _service.EnrichAsync(new AiDispatchQueueItem
			{
				DepartmentId = DepartmentId, CallId = CallId, Channel = AiDispatchChannels.IntakeSms, NameIsPlaceholder = true, PlaceholderNameFingerprint = null
			}, CancellationToken.None)).Should().Be(AiDispatchOutcomes.Applied);

			_saved.Single().Name.Should().Be("Hospital line: +15305550199");
			_saved.Single().Type.Should().Be("Transfer", "the other fields are still filled");
		}

		[Test]
		public async Task intake_prompts_carry_the_spam_guidance_and_email_prompts_do_not()
		{
			await Enrich(AiDispatchChannels.IntakeSms);
			_requests.Single().Messages.Single(m => m.Role == "user").Content.Should().Contain(AiDispatchEnrichmentService.IntakeScreeningHint.Trim());

			AiDispatchEnrichmentService.IntakeSource(AiDispatchChannels.PostmarkEmail, new Call { State = (int)CallStates.Active })
				.Should().NotContain("spam");
		}

		[TearDown]
		public void TearDown()
		{
			(AiDispatchConfig.EnrichEnabled, AiConfig.Endpoint, AiConfig.AllowPrivateEndpoint, AiConfig.ApiKey, AiConfig.Model, AiConfig.ModelRevision,
				AiConfig.RuntimeDigest, AiConfig.SelfHostedDepartmentIds, AiConfig.MonthlyTokenLimit, AiDispatchConfig.DecisionsEnabled, AiDispatchConfig.MinimumConfidence,
				AiDispatchConfig.AdmissionWaitSeconds, IntakeConfig.Enabled, AiConfig.SendCacheSalt) = _savedConfig;
			AiConfig.AuditHmacKey = _savedAuditKey;
		}

		private string _savedAuditKey;

		// As IntakeService queues it: the fingerprint of the placeholder name the call was saved with.
		private Task<string> Enrich(int channel, bool placeholder = true) => _service.EnrichAsync(new AiDispatchQueueItem
		{
			DepartmentId = DepartmentId, CallId = CallId, Channel = channel, NameIsPlaceholder = placeholder,
			PlaceholderNameFingerprint = placeholder ? AiDispatchEnrichmentService.PlaceholderFingerprint(DepartmentId, CallId, _call.Name) : null
		}, CancellationToken.None);

		[TestCase(AiDispatchChannels.IntakeSms)]
		[TestCase(AiDispatchChannels.IntakeEmail)]
		[TestCase(AiDispatchChannels.IntakeChat)]
		[TestCase(AiDispatchChannels.IntakeWebhook)]
		public async Task a_pending_intake_call_is_enriched_without_the_ai_email_format(int channel)
		{
			(await Enrich(channel)).Should().Be(AiDispatchOutcomes.Applied);

			var saved = _saved.Single();
			saved.State.Should().Be((int)CallStates.Pending, "enrichment never dispatches");
			saved.Type.Should().Be("Transfer");
			saved.Address.Should().Be("123 MAIN ST, SPRINGFIELD");
			saved.Name.Should().Be("Patient transfer, 123 Main St", "the intake fallback name is the placeholder");
			saved.Priority.Should().Be(22);
			_access.Verify(a => a.CanUseAsync(DepartmentId, FeatureFlagKeys.AiIntake), Times.AtLeastOnce);
			_access.Verify(a => a.CanUseAsync(DepartmentId, FeatureFlagKeys.AiDispatchTemplate), Times.Never);
		}

		[Test]
		public async Task the_model_is_told_the_call_is_pending_intake()
		{
			await Enrich(AiDispatchChannels.IntakeSms);

			var user = _requests.Single().Messages.Single(m => m.Role == "user").Content;
			user.Should().StartWith("Message source (trusted): a text message sent to the department's intake number.");
			user.Should().Contain("nobody has been dispatched");
		}

		[Test]
		public async Task a_call_a_dispatcher_already_sent_is_not_described_as_pending()
		{
			_call.State = (int)CallStates.Active;

			(await Enrich(AiDispatchChannels.IntakeSms)).Should().Be(AiDispatchOutcomes.Applied);

			var user = _requests.Single().Messages.Single(m => m.Role == "user").Content;
			user.Should().NotContain("nobody has been dispatched");
			user.Should().Contain("A dispatcher has dispatched the call.");
		}

		[Test]
		public void the_system_prompt_states_no_source_or_dispatch_state()
		{
			AiDispatchPrompt.System.Should().NotContain("dispatched from");
			AiDispatchPrompt.System.Should().NotContain("CAD email");
			AiDispatchEnrichmentService.IntakeSource(AiDispatchChannels.PostmarkEmail, new Call { State = (int)CallStates.Active })
				.Should().Contain("dispatched automatically", "the dispatch email paths say so in the trusted source line instead");
		}

		[Test]
		public async Task a_rename_made_before_the_worker_read_the_call_is_kept()
		{
			var savedKey = AiConfig.AuditHmacKey;
			try
			{
				AiConfig.AuditHmacKey = Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());
				var fingerprint = AiDispatchEnrichmentService.PlaceholderFingerprint(DepartmentId, CallId, "Hospital line: +15305550199");
				fingerprint.Should().NotBeNullOrEmpty().And.NotContain("+15305550199");

				// Queued with the placeholder; a dispatcher renamed the call before the worker first loaded it.
				_call.Name = "MVA on Route 9";
				(await _service.EnrichAsync(new AiDispatchQueueItem
				{
					DepartmentId = DepartmentId, CallId = CallId, Channel = AiDispatchChannels.IntakeSms, NameIsPlaceholder = true, PlaceholderNameFingerprint = fingerprint
				}, CancellationToken.None)).Should().Be(AiDispatchOutcomes.Applied);
				_saved.Single().Name.Should().Be("MVA on Route 9");

				// An untouched placeholder is still replaced by the model's title.
				_saved.Clear();
				_call = new Call
				{
					CallId = CallId, DepartmentId = DepartmentId, Number = "26-700", Name = "Hospital line: +15305550199", NatureOfCall = Body, Priority = 22,
					State = (int)CallStates.Pending, CallSource = (int)CallSources.Intake
				};
				(await _service.EnrichAsync(new AiDispatchQueueItem
				{
					DepartmentId = DepartmentId, CallId = CallId, Channel = AiDispatchChannels.IntakeSms, NameIsPlaceholder = true, PlaceholderNameFingerprint = fingerprint
				}, CancellationToken.None)).Should().Be(AiDispatchOutcomes.Applied);
				_saved.Single().Name.Should().Be("Patient transfer, 123 Main St");
			}
			finally
			{
				AiConfig.AuditHmacKey = savedKey;
			}
		}

		[Test]
		public void without_a_usable_audit_key_there_is_no_fingerprint()
		{
			var savedKey = AiConfig.AuditHmacKey;
			try
			{
				AiConfig.AuditHmacKey = "";
				AiDispatchEnrichmentService.PlaceholderFingerprint(DepartmentId, CallId, "x").Should().BeNull();
				AiConfig.AuditHmacKey = "not base64!";
				AiDispatchEnrichmentService.PlaceholderFingerprint(DepartmentId, CallId, "x").Should().BeNull();
				AiConfig.AuditHmacKey = Convert.ToBase64String(new byte[16]);
				AiDispatchEnrichmentService.PlaceholderFingerprint(DepartmentId, CallId, "x").Should().BeNull("a short key is not usable");
			}
			finally
			{
				AiConfig.AuditHmacKey = savedKey;
			}
		}

		[Test]
		public async Task voice_needs_ai_voice_intake_and_gets_the_labelled_answers_description()
		{
			_access.Setup(a => a.CanUseAsync(DepartmentId, FeatureFlagKeys.AiVoiceIntake)).ReturnsAsync(false);
			(await Enrich(AiDispatchChannels.IntakeVoice)).Should().Be("NotEntitled");

			_access.Setup(a => a.CanUseAsync(DepartmentId, FeatureFlagKeys.AiVoiceIntake)).ReturnsAsync(true);
			_call.CallSource = (int)CallSources.VoiceIntake;
			(await Enrich(AiDispatchChannels.IntakeVoice)).Should().Be(AiDispatchOutcomes.Applied);
			_requests.Single().Messages.Single(m => m.Role == "user").Content.Should().Contain("Lines are labelled Nature, Location, Caller name and Callback number");
		}

		[Test]
		public async Task without_ai_intake_the_call_stays_as_it_was()
		{
			_access.Setup(a => a.CanUseAsync(DepartmentId, FeatureFlagKeys.AiIntake)).ReturnsAsync(false);

			(await Enrich(AiDispatchChannels.IntakeSms)).Should().Be("NotEntitled");
			_saved.Should().BeEmpty();
		}

		[Test]
		public async Task the_intake_host_switch_off_skips_enrichment()
		{
			IntakeConfig.Enabled = false;

			(await Enrich(AiDispatchChannels.IntakeSms)).Should().Be("NotEntitled");
		}

		[Test]
		public async Task a_self_hosted_department_uses_the_module_switch_and_the_intake_flag()
		{
			AiConfig.SelfHostedDepartmentIds = DepartmentId.ToString();
			_access.Setup(a => a.CanUseAsync(DepartmentId, FeatureFlagKeys.AiIntake)).ReturnsAsync(false);
			_access.Setup(a => a.IsEnabledAsync(DepartmentId)).ReturnsAsync(true);
			_flags.Setup(f => f.EvaluateFreshAsync(FeatureFlagKeys.AiIntake, DepartmentId)).ReturnsAsync(new FeatureFlagEvaluation { IsEnabled = true });
			_admission.Setup(a => a.ReserveBackgroundAsync(DepartmentId, "AiDispatch", AdminAssistAskTiers.SelfHosted, It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>(), It.IsAny<int?>()))
				.ReturnsAsync(new AiUsageReservation("r1", DepartmentId, "system:background", 8192, DateTime.UtcNow.AddMinutes(2)));

			(await Enrich(AiDispatchChannels.IntakeSms)).Should().Be(AiDispatchOutcomes.Applied);
		}

		[Test]
		public async Task adp_departments_are_still_skipped()
		{
			_protection.Setup(p => p.ShouldEncryptNewWritesAsync(DepartmentId)).ReturnsAsync(true);

			(await Enrich(AiDispatchChannels.IntakeSms)).Should().Be(AiDispatchOutcomes.ProtectionUnsupported);
			_saved.Should().BeEmpty();
			_llm.Verify(l => l.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task a_name_from_the_message_subject_is_kept()
		{
			_call.Name = "Transfer request";

			(await Enrich(AiDispatchChannels.IntakeEmail, placeholder: false)).Should().Be(AiDispatchOutcomes.Applied);

			_saved.Single().Name.Should().Be("Transfer request");
		}

		[Test]
		public async Task a_closed_or_deleted_intake_call_is_not_enriched()
		{
			_call.State = (int)CallStates.Closed;
			(await Enrich(AiDispatchChannels.IntakeSms)).Should().Be(AiDispatchOutcomes.CallNotActive);

			_call.State = (int)CallStates.Pending;
			_call.IsDeleted = true;
			(await Enrich(AiDispatchChannels.IntakeSms)).Should().Be(AiDispatchOutcomes.CallNotActive);
			_saved.Should().BeEmpty();
		}

		[Test]
		public async Task the_email_channels_still_need_the_ai_format()
		{
			_access.Setup(a => a.CanUseAsync(DepartmentId, FeatureFlagKeys.AiDispatchTemplate)).ReturnsAsync(true);

			(await Enrich(AiDispatchChannels.PostmarkEmail)).Should().Be("NotEntitled", "the department's email format is not AI");

			// Pending email calls are the dispatcher desk's (held for a dispatcher), so only a closed one is refused here.
			_departments.Setup(d => d.GetDepartmentEmailSettingsAsync(DepartmentId)).ReturnsAsync(new DepartmentCallEmail { DepartmentId = DepartmentId, FormatType = (int)CallEmailTypes.AI });
			_call.State = (int)CallStates.Closed;
			(await Enrich(AiDispatchChannels.PostmarkEmail)).Should().Be(AiDispatchOutcomes.CallNotActive, "email enrichment never touches closed calls");
		}

		[Test]
		public void channel_helpers()
		{
			AiDispatchChannels.IsIntake(AiDispatchChannels.PostmarkEmail).Should().BeFalse();
			AiDispatchChannels.IsIntake(AiDispatchChannels.ImapImport).Should().BeFalse();
			AiDispatchChannels.IsIntake(AiDispatchChannels.IntakeSms).Should().BeTrue();
			AiDispatchChannels.IsIntake(AiDispatchChannels.IntakeVoice).Should().BeTrue();
			AiDispatchChannels.IntakeCapabilityFlag(AiDispatchChannels.IntakeVoice).Should().Be(FeatureFlagKeys.AiVoiceIntake);
			AiDispatchChannels.IntakeCapabilityFlag(AiDispatchChannels.IntakeChat).Should().Be(FeatureFlagKeys.AiIntake);
			AiDispatchChannels.ForIntake(IntakeChannelTypes.Slack).Should().Be(AiDispatchChannels.IntakeChat);
			AiDispatchChannels.ForIntake(IntakeChannelTypes.WhatsApp).Should().Be(AiDispatchChannels.IntakeChat);
			AiDispatchChannels.ForIntake(IntakeChannelTypes.Email).Should().Be(AiDispatchChannels.IntakeEmail);
			AiDispatchPrompt.Messages(new AiDispatchContext("m", new List<AiDispatchChoice>(), new List<AiDispatchChoice>(), new List<AiDispatchCandidateCall>()))
				.Single(m => m.Role == "user").Content.Should().StartWith("Department lists (trusted):", "the email paths send what they always did");
		}
	}
}
