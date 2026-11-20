using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Llm;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.AiDispatch;
using Resgrid.Model.Events;
using Resgrid.Model.Messages;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services.AiDestination;

namespace Resgrid.Tests.AiDestination
{
	/// <summary>
	/// Destination advice never changes a destination by itself (plan D4): it is written as advice, a confident change is noted, raised
	/// and pushed, and only a person's Apply sets the call's destination. Off, unentitled or ADP departments cost nothing.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class AiDestinationAdvisorServiceTests
	{
		private const int DepartmentId = 42;
		private const int CallId = 900;
		private const string Trauma = "cap-trauma-1";
		private const string Helipad = "cap-helipad";

		private sealed class FixedClock : TimeProvider
		{
			public DateTimeOffset Now { get; set; } = new DateTimeOffset(2026, 11, 19, 15, 0, 0, TimeSpan.Zero);
			public override DateTimeOffset GetUtcNow() => Now;
		}

		private FixedClock _clock;
		private Mock<IEnhancedAiAccessService> _access;
		private Mock<IDepartmentDataProtectionService> _protection;
		private Mock<ICallsService> _calls;
		private Mock<IMappingService> _mapping;
		private Mock<INearestDestinationService> _nearest;
		private Mock<IDestinationRulesService> _rules;
		private Mock<IDepartmentSettingsService> _settings;
		private Mock<IDepartmentsService> _departments;
		private Mock<IUserProfileService> _profiles;
		private Mock<IPushService> _push;
		private Mock<IAiDestinationAdvicesRepository> _advices;
		private Mock<IAiDestinationReviewsRepository> _reviews;
		private Mock<IAiBackgroundAdmission> _admission;
		private Mock<IAiUsageMeter> _usage;
		private Mock<ILlmClient> _llm;
		private Mock<IEventAggregator> _events;
		private AiDestinationAvailabilityCache _cache;
		private AiDestinationAdvisorService _service;

		private Call _call;
		private DestinationSuggestionConfig _suggestionConfig;
		private List<DestinationRule> _ruleList;
		private AiDestinationAdvice _latest;
		private readonly List<AiDestinationAdvice> _inserted = new List<AiDestinationAdvice>();
		private readonly List<CallNote> _notes = new List<CallNote>();
		private readonly List<Call> _saved = new List<Call>();
		private readonly List<AiDestinationAdviceUpdatedEvent> _adviceEvents = new List<AiDestinationAdviceUpdatedEvent>();
		private readonly List<string> _ledger = new List<string>();
		private object _savedConfig;

		[SetUp]
		public void SetUp()
		{
			_savedConfig = (AiDestinationConfig.Enabled, AiDestinationConfig.MinimumConfidence, AiDestinationConfig.AdmissionWaitSeconds, AiDestinationConfig.AvailabilityCacheSeconds,
				AiConfig.Endpoint, AiConfig.AllowPrivateEndpoint, AiConfig.ApiKey, AiConfig.Model, AiConfig.ModelRevision, AiConfig.RuntimeDigest, AiConfig.SelfHostedDepartmentIds,
				AiConfig.MonthlyTokenLimit, AiConfig.SendCacheSalt, SystemBehaviorConfig.DoNotBroadcast);
			AiDestinationConfig.Enabled = true;
			AiDestinationConfig.MinimumConfidence = 0.6;
			AiDestinationConfig.AdmissionWaitSeconds = 0;
			AiDestinationConfig.AvailabilityCacheSeconds = 300;
			AiConfig.Endpoint = "https://inference.example.invalid/v1/chat/completions";
			AiConfig.AllowPrivateEndpoint = false;
			AiConfig.ApiKey = "unit-test-only";
			AiConfig.Model = "Qwen/Qwen3-14B-AWQ";
			AiConfig.ModelRevision = new string('a', 40);
			AiConfig.RuntimeDigest = "sha256:" + new string('b', 64);
			AiConfig.SelfHostedDepartmentIds = "";
			AiConfig.MonthlyTokenLimit = 2000000;
			AiConfig.SendCacheSalt = false;
			SystemBehaviorConfig.DoNotBroadcast = false;

			_inserted.Clear();
			_notes.Clear();
			_saved.Clear();
			_adviceEvents.Clear();
			_ledger.Clear();
			_latest = null;
			_clock = new FixedClock();

			_call = new Call
			{
				CallId = CallId, DepartmentId = DepartmentId, Number = "26-500", Name = "Fall", Type = "Medical", CallTypeId = 3, Priority = 2,
				NatureOfCall = "Fall from a roof, unresponsive", Notes = "Ladder collapse", State = (int)CallStates.Active, GeoLocationData = "39.7392,-104.9903",
				DestinationPoiId = 10, CallSource = (int)CallSources.User, ReportingUserId = "dispatcher-1",
				CallNotes = new List<CallNote>
				{
					new CallNote { CallId = CallId, Note = "GCS 7, possible head injury", Source = (int)CallNoteSources.Web, Timestamp = new DateTime(2026, 11, 19, 14, 55, 0) },
					new CallNote { CallId = CallId, Note = "Enhanced AI destination advice: earlier", Source = (int)CallNoteSources.System, Timestamp = new DateTime(2026, 11, 19, 14, 56, 0) }
				},
				UnitDispatches = new List<CallDispatchUnit>
				{
					new CallDispatchUnit { CallId = CallId, UnitId = 7 },
					new CallDispatchUnit { CallId = CallId, UnitId = 8, ReleasedOn = new DateTime(2026, 11, 19, 14, 58, 0) }
				}
			};
			_suggestionConfig = new DestinationSuggestionConfig { Mode = (int)DestinationSuggestionModes.Suggest };
			_ruleList = new List<DestinationRule> { new DestinationRule { DestinationRuleId = "r1", DepartmentId = DepartmentId, Name = "Medical to hospital", CallTypeId = 3 } };

			_access = new Mock<IEnhancedAiAccessService>();
			_access.Setup(a => a.CanUseAsync(DepartmentId, FeatureFlagKeys.AiDestinationAdvice)).ReturnsAsync(true);
			_protection = new Mock<IDepartmentDataProtectionService>();

			_calls = new Mock<ICallsService>();
			_calls.Setup(c => c.GetCallByIdAsync(CallId, true)).ReturnsAsync(() => _call);
			_calls.Setup(c => c.GetActiveCallPrioritiesForDepartmentAsync(DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync(new List<DepartmentCallPriority> { new DepartmentCallPriority { DepartmentCallPriorityId = 2, Name = "Emergency" } });
			_calls.Setup(c => c.PopulateCallData(It.IsAny<Call>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(),
				It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>())).ReturnsAsync((Call c, bool a, bool b, bool d, bool e, bool f, bool g, bool h, bool i, bool j, bool k) => c);
			_calls.Setup(c => c.SaveCallNoteAsync(It.IsAny<CallNote>(), It.IsAny<CancellationToken>())).ReturnsAsync((CallNote n, CancellationToken _) => { _notes.Add(n); return n; });
			_calls.Setup(c => c.SaveCallAsync(It.IsAny<Call>(), It.IsAny<CancellationToken>())).ReturnsAsync((Call c, CancellationToken _) => { _saved.Add(c); return c; });

			_mapping = new Mock<IMappingService>();
			_mapping.Setup(m => m.GetPoiCapabilitiesForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<PoiCapability>
			{
				new PoiCapability { PoiCapabilityId = Trauma, DepartmentId = DepartmentId, Name = "Level I Trauma", Sort = 1 },
				new PoiCapability { PoiCapabilityId = Helipad, DepartmentId = DepartmentId, Name = "Helipad", Sort = 2 }
			});
			_mapping.Setup(m => m.GetDestinationPOIByIdAsync(DepartmentId, 10)).ReturnsAsync(new Poi { PoiId = 10, Name = "Community Hospital", PoiTypeId = 1 });
			_mapping.Setup(m => m.GetDestinationPOIByIdAsync(DepartmentId, 20)).ReturnsAsync(new Poi { PoiId = 20, Name = "Regional Trauma Center", PoiTypeId = 1 });
			_mapping.Setup(m => m.GetCurrentPoiAvailabilitiesAsync(DepartmentId)).ReturnsAsync(new Dictionary<int, PoiAvailability>());

			_nearest = new Mock<INearestDestinationService>();
			_nearest.Setup(n => n.SuggestDestinationsAsync(It.IsAny<DestinationSuggestionRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => new DestinationSuggestions
			{
				Mode = DestinationSuggestionModes.Suggest,
				Requirements = new DestinationRequirements { AppliedRules = _ruleList.ToList() },
				Destinations = new List<NearestDestination>
				{
					Destination(10, "Community Hospital", 480, Helipad),
					Destination(20, "Regional Trauma Center", 1260, Trauma, Helipad)
				}
			});

			_rules = new Mock<IDestinationRulesService>();
			_rules.Setup(r => r.GetRulesForDepartmentAsync(DepartmentId)).ReturnsAsync(() => _ruleList);
			_rules.Setup(r => r.BuildContextAsync(DepartmentId, It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<int?>(), null))
				.ReturnsAsync((int d, int? typeId, string type, int? priority, int? unit) => new DestinationContext { CallTypeId = typeId, CallPriority = priority });

			_settings = new Mock<IDepartmentSettingsService>();
			_settings.Setup(s => s.GetDestinationSuggestionConfigAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(() => _suggestionConfig);

			_departments = new Mock<IDepartmentsService>();
			_departments.Setup(d => d.GetDepartmentByIdAsync(DepartmentId, false)).ReturnsAsync(new Department { DepartmentId = DepartmentId, ManagingUserId = "owner", Code = "ABCD" });
			_profiles = new Mock<IUserProfileService>();
			_profiles.Setup(p => p.GetProfileByUserIdAsync("medic-lead", It.IsAny<bool>())).ReturnsAsync(new UserProfile { UserId = "medic-lead", FirstName = "Ana", LastName = "Ruiz" });
			_push = new Mock<IPushService>();

			_advices = new Mock<IAiDestinationAdvicesRepository>();
			_advices.Setup(a => a.GetLatestForCallAsync(DepartmentId, CallId, It.IsAny<CancellationToken>())).ReturnsAsync(() => _latest);
			_advices.Setup(a => a.InsertAsync(It.IsAny<AiDestinationAdvice>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((AiDestinationAdvice a, CancellationToken _, bool __) => { _inserted.Add(a); return a; });
			_reviews = new Mock<IAiDestinationReviewsRepository>();

			_admission = new Mock<IAiBackgroundAdmission>();
			_admission.Setup(a => a.ReserveBackgroundAsync(DepartmentId, AiDestinationAdvisorService.Feature, AdminAssistAskTiers.EnhancedAi, It.IsAny<DateTime>(), It.IsAny<int>(),
					It.IsAny<int>(), It.IsAny<CancellationToken>(), It.IsAny<int?>()))
				.ReturnsAsync(new AiUsageReservation("r1", DepartmentId, "system:background", 6144, DateTime.UtcNow.AddMinutes(2)));
			_usage = new Mock<IAiUsageMeter>();
			_usage.Setup(u => u.RemainingAsync(DepartmentId, It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(1_000_000);
			_usage.Setup(u => u.CompleteAsync(It.IsAny<AiUsageReservation>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.Callback((AiUsageReservation r, int t, string o, CancellationToken _) => _ledger.Add(o)).Returns(Task.CompletedTask);

			_llm = new Mock<ILlmClient>();
			Reply("20", 0.85, "GCS 7 after a fall from height needs Level I trauma care.", "c1");

			_events = new Mock<IEventAggregator>();
			_events.Setup(e => e.SendMessageAsync(It.IsAny<AiDestinationAdviceUpdatedEvent>())).Callback((AiDestinationAdviceUpdatedEvent e) => _adviceEvents.Add(e)).Returns(Task.CompletedTask);

			_cache = new AiDestinationAvailabilityCache();
			_service = new AiDestinationAdvisorService(_access.Object, Mock.Of<IFeatureToggleService>(), _protection.Object, _calls.Object, _mapping.Object, _nearest.Object,
				_rules.Object, _settings.Object, _departments.Object, _profiles.Object, _push.Object, _advices.Object, _reviews.Object, _admission.Object, _usage.Object,
				new Lazy<ILlmClient>(() => _llm.Object), _events.Object, _cache, _clock);
		}

		[TearDown]
		public void TearDown()
		{
			(AiDestinationConfig.Enabled, AiDestinationConfig.MinimumConfidence, AiDestinationConfig.AdmissionWaitSeconds, AiDestinationConfig.AvailabilityCacheSeconds,
				AiConfig.Endpoint, AiConfig.AllowPrivateEndpoint, AiConfig.ApiKey, AiConfig.Model, AiConfig.ModelRevision, AiConfig.RuntimeDigest, AiConfig.SelfHostedDepartmentIds,
				AiConfig.MonthlyTokenLimit, AiConfig.SendCacheSalt, SystemBehaviorConfig.DoNotBroadcast) =
				((bool, double, int, int, string, bool, string, string, string, string, string, int, bool, bool))_savedConfig;
		}

		private static NearestDestination Destination(int poiId, string name, double travelSeconds, params string[] capabilities) => new NearestDestination
		{
			Poi = new Poi { PoiId = poiId, Name = name, PoiTypeId = 1, Type = new PoiType { PoiTypeId = 1, Name = "Hospital", IsDestination = true }, CapabilityIds = capabilities.ToList() },
			DistanceMeters = travelSeconds * 15, TravelSeconds = travelSeconds, Status = PoiAvailabilityStatuses.Open, CanProvide = true, Tier = DestinationTiers.Ready
		};

		private void Reply(string poi, double confidence, string rationale, params string[] capabilities)
		{
			var json = JsonSerializer.Serialize(new { recommended_poi_id = poi, required_capability_ids = capabilities, rationale, confidence });
			_llm.Setup(l => l.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new LlmResult(null, new[] { new LlmToolCall("t1", "advise_destination", json) }, 1200, 80, "tool_calls"));
		}

		private void NoModelCall() => _llm.Verify(l => l.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()), Times.Never);

		[Test]
		public async Task Off_the_triggers_are_a_no_op_and_nothing_is_asked()
		{
			AiDestinationConfig.Enabled = false;

			await _service.RequestReviewAsync(DepartmentId, CallId, AiDestinationReviewTriggers.NoteAdded);
			(await _service.IsAvailableAsync(DepartmentId)).Should().BeFalse();
			(await _service.AdviseNowAsync(DepartmentId, CallId, "medic-lead")).Outcome.Should().Be(AiDestinationOutcomes.Disabled);

			_reviews.Verify(r => r.QueueAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<AiDestinationReviewTriggers>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
			_access.Verify(a => a.CanUseAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
			NoModelCall();
		}

		[Test]
		public async Task An_unentitled_department_queues_nothing_and_the_billing_check_is_remembered()
		{
			_access.Setup(a => a.CanUseAsync(DepartmentId, FeatureFlagKeys.AiDestinationAdvice)).ReturnsAsync(false);

			for (var i = 0; i < 5; i++)
				await _service.RequestReviewAsync(DepartmentId, CallId, AiDestinationReviewTriggers.NoteAdded);

			_reviews.Verify(r => r.QueueAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<AiDestinationReviewTriggers>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
			_access.Verify(a => a.CanUseAsync(DepartmentId, FeatureFlagKeys.AiDestinationAdvice), Times.Once, "a burst of notes must not hit the Billing API each time");

			_clock.Now = _clock.Now.AddMinutes(6);
			await _service.RequestReviewAsync(DepartmentId, CallId, AiDestinationReviewTriggers.NoteAdded);
			_access.Verify(a => a.CanUseAsync(DepartmentId, FeatureFlagKeys.AiDestinationAdvice), Times.Exactly(2), "the remembered answer expires");
		}

		[Test]
		public async Task Advanced_data_protection_departments_are_skipped()
		{
			_protection.Setup(p => p.ShouldEncryptNewWritesAsync(DepartmentId)).ReturnsAsync(true);

			(await _service.AdviseNowAsync(DepartmentId, CallId, "medic-lead")).Outcome.Should().Be(AiDestinationOutcomes.ProtectionUnsupported);
			await _service.RequestReviewAsync(DepartmentId, CallId, AiDestinationReviewTriggers.CallUpdated);
			(await _service.IsAvailableAsync(DepartmentId)).Should().BeFalse();

			_reviews.Verify(r => r.QueueAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<AiDestinationReviewTriggers>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
			NoModelCall();
		}

		[Test]
		public async Task A_trigger_queues_one_review_for_an_open_call_going_somewhere()
		{
			await _service.RequestReviewAsync(DepartmentId, CallId, AiDestinationReviewTriggers.NoteAdded);

			_reviews.Verify(r => r.QueueAsync(DepartmentId, CallId, AiDestinationReviewTriggers.NoteAdded, _clock.Now.UtcDateTime, It.IsAny<CancellationToken>()), Times.Once);
			NoModelCall();
		}

		[Test]
		public async Task Calls_without_a_destination_or_matching_rule_and_closed_calls_are_not_reviewed()
		{
			_call.DestinationPoiId = null;
			_ruleList = new List<DestinationRule> { new DestinationRule { DestinationRuleId = "r2", DepartmentId = DepartmentId, Name = "Fire calls", CallTypeId = 99 } };
			await _service.RequestReviewAsync(DepartmentId, CallId, AiDestinationReviewTriggers.CallUpdated);

			_ruleList = new List<DestinationRule> { new DestinationRule { DestinationRuleId = "r1", DepartmentId = DepartmentId, Name = "Medical", CallTypeId = 3 } };
			_suggestionConfig = new DestinationSuggestionConfig { Mode = (int)DestinationSuggestionModes.Off };
			await _service.RequestReviewAsync(DepartmentId, CallId, AiDestinationReviewTriggers.CallUpdated);

			_call.DestinationPoiId = 10;
			_call.State = (int)CallStates.Closed;
			await _service.RequestReviewAsync(DepartmentId, CallId, AiDestinationReviewTriggers.CallUpdated);

			_reviews.Verify(r => r.QueueAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<AiDestinationReviewTriggers>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);

			// A matching rule is enough (a pending call counts as open).
			_call.DestinationPoiId = null;
			_call.State = (int)CallStates.Pending;
			_suggestionConfig = new DestinationSuggestionConfig { Mode = (int)DestinationSuggestionModes.Suggest };
			await _service.RequestReviewAsync(DepartmentId, CallId, AiDestinationReviewTriggers.CallUpdated);
			_reviews.Verify(r => r.QueueAsync(DepartmentId, CallId, AiDestinationReviewTriggers.CallUpdated, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task An_availability_change_queues_the_open_calls_going_to_that_destination()
		{
			var other = new Call { CallId = 901, DepartmentId = DepartmentId, State = (int)CallStates.Active, DestinationPoiId = 20 };
			var pending = new Call { CallId = 902, DepartmentId = DepartmentId, State = (int)CallStates.Pending, DestinationPoiId = 10 };
			_calls.Setup(c => c.GetActiveCallsByDepartmentAsync(DepartmentId)).ReturnsAsync(new List<Call> { _call, other });
			_calls.Setup(c => c.GetPendingCallsByDepartmentIdAsync(DepartmentId)).ReturnsAsync(new List<Call> { pending });

			await _service.RequestReviewsForDestinationAsync(DepartmentId, 10);

			_reviews.Verify(r => r.QueueAsync(DepartmentId, CallId, AiDestinationReviewTriggers.AvailabilityChanged, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
			_reviews.Verify(r => r.QueueAsync(DepartmentId, 902, AiDestinationReviewTriggers.AvailabilityChanged, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
			_reviews.Verify(r => r.QueueAsync(DepartmentId, 901, It.IsAny<AiDestinationReviewTriggers>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task A_confident_change_is_written_noted_raised_and_pushed_but_the_destination_is_untouched()
		{
			var result = await _service.AdviseNowAsync(DepartmentId, CallId, "medic-lead");

			result.Outcome.Should().Be(AiDestinationOutcomes.Advised);
			var advice = _inserted.Should().ContainSingle().Subject;
			advice.RecommendedPoiId.Should().Be(20);
			advice.CurrentPoiId.Should().Be(10);
			advice.IsChange.Should().BeTrue();
			advice.RequiredCapabilityIds.Should().Be(Trauma);
			advice.State.Should().Be((int)AiDestinationAdviceStates.Open);
			advice.InputDigest.Should().HaveLength(64);
			advice.ReviewTrigger.Should().Be((int)AiDestinationReviewTriggers.OnDemand);
			_advices.Verify(a => a.SupersedeOpenAsync(DepartmentId, CallId, advice.AiDestinationAdviceId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);

			// Plan D4: the call itself is never saved by the advisor.
			_saved.Should().BeEmpty();
			_call.DestinationPoiId.Should().Be(10);

			var note = _notes.Should().ContainSingle().Subject;
			note.Source.Should().Be((int)CallNoteSources.System);
			note.UserId.Should().Be("owner");
			note.Note.Should().StartWith("Enhanced AI destination advice: consider Regional Trauma Center — GCS 7");

			_adviceEvents.Should().ContainSingle().Which.CallId.Should().Be(CallId);
			_adviceEvents[0].Payload.Should().Contain(advice.AiDestinationAdviceId).And.NotContain("Trauma", "the realtime payload carries ids and state only");

			// The dispatcher who logged it and the unit still on the call; the released unit is left alone. The push opens the call.
			_push.Verify(p => p.PushNotification(It.Is<StandardPushMessage>(m => m.Id == "NC:" + CallId && m.SubTitle.Contains("Regional Trauma Center") && !m.SubTitle.Contains("GCS")),
				"dispatcher-1", null), Times.Once);
			_push.Verify(p => p.PushNotificationUnit(It.IsAny<StandardPushMessage>(), 7), Times.Once);
			_push.Verify(p => p.PushNotificationUnit(It.IsAny<StandardPushMessage>(), 8), Times.Never);

			// Only the person-written note reaches the model, inside the untrusted block.
			_llm.Verify(l => l.CompleteAsync(It.Is<LlmRequest>(r => r.Messages[1].Content.Contains("GCS 7, possible head injury") &&
				!r.Messages[1].Content.Contains("Enhanced AI destination advice: earlier")), It.IsAny<CancellationToken>()), Times.Once);
			_ledger.Should().Equal("Answered");
		}

		[Test]
		public async Task A_low_confidence_change_is_shown_but_not_announced()
		{
			Reply("20", 0.4, "Possibly needs trauma care.", "c1");

			(await _service.AdviseNowAsync(DepartmentId, CallId, "medic-lead")).Outcome.Should().Be(AiDestinationOutcomes.Advised);

			_inserted.Should().ContainSingle().Which.IsChange.Should().BeTrue();
			_notes.Should().BeEmpty();
			_push.VerifyNoOtherCalls();
			_adviceEvents.Should().ContainSingle("the panel still refreshes");
		}

		[Test]
		public async Task Keeping_the_current_destination_is_not_a_change()
		{
			Reply("keep", 0.9, "The community hospital is closest and fits.");

			await _service.AdviseNowAsync(DepartmentId, CallId, "medic-lead");

			var advice = _inserted.Should().ContainSingle().Subject;
			advice.RecommendedPoiId.Should().BeNull();
			advice.IsChange.Should().BeFalse();
			_notes.Should().BeEmpty();
			_push.VerifyNoOtherCalls();
		}

		[Test]
		public async Task An_unchanged_call_is_not_asked_again()
		{
			await _service.AdviseNowAsync(DepartmentId, CallId, "medic-lead");
			_latest = _inserted.Single();

			var again = await _service.AdviseNowAsync(DepartmentId, CallId, "medic-lead");

			again.Outcome.Should().Be(AiDestinationOutcomes.Unchanged);
			again.Advice.Should().BeSameAs(_latest);
			_llm.Verify(l => l.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()), Times.Once);

			// A new person-written note is a change.
			_call.CallNotes.Add(new CallNote { CallId = CallId, Note = "Now intubated", Source = (int)CallNoteSources.Mobile, Timestamp = new DateTime(2026, 11, 19, 14, 59, 0) });
			(await _service.AdviseNowAsync(DepartmentId, CallId, "medic-lead")).Outcome.Should().Be(AiDestinationOutcomes.Advised);
			_llm.Verify(l => l.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
		}

		[Test]
		public async Task A_dismissed_advice_is_not_offered_again_in_the_background_for_the_same_inputs()
		{
			await _service.AdviseNowAsync(DepartmentId, CallId, "medic-lead");
			_latest = _inserted.Single();
			_latest.State = (int)AiDestinationAdviceStates.Dismissed;
			var review = new AiDestinationReview { AiDestinationReviewId = "rv1", DepartmentId = DepartmentId, CallId = CallId, ReviewTrigger = (int)AiDestinationReviewTriggers.NoteAdded };
			_reviews.Setup(r => r.GetDueAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<AiDestinationReview> { review });
			_reviews.Setup(r => r.TryClaimAsync("rv1", It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
			_reviews.Setup(r => r.CompleteAsync("rv1", It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

			var sweep = await _service.ProcessDueReviewsAsync(_clock.Now.UtcDateTime);

			sweep.Outcomes.Should().ContainKey(AiDestinationOutcomes.Unchanged);
			_llm.Verify(l => l.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task The_save_that_applies_advice_does_not_ask_the_model_again()
		{
			await _service.AdviseNowAsync(DepartmentId, CallId, "medic-lead");
			_latest = _inserted.Single();
			_latest.State = (int)AiDestinationAdviceStates.Applied;
			_call.DestinationPoiId = 20;
			var review = new AiDestinationReview { AiDestinationReviewId = "rv1", DepartmentId = DepartmentId, CallId = CallId, ReviewTrigger = (int)AiDestinationReviewTriggers.CallUpdated };
			_reviews.Setup(r => r.GetDueAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<AiDestinationReview> { review });
			_reviews.Setup(r => r.TryClaimAsync("rv1", It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
			_reviews.Setup(r => r.CompleteAsync("rv1", It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

			(await _service.ProcessDueReviewsAsync(_clock.Now.UtcDateTime)).Outcomes.Should().ContainKey(AiDestinationOutcomes.Unchanged);
			_llm.Verify(l => l.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()), Times.Once);

			// Anything else that changed (a new note) is asked about.
			_call.CallNotes.Add(new CallNote { CallId = CallId, Note = "Patient deteriorating", Source = (int)CallNoteSources.Web, Timestamp = new DateTime(2026, 11, 19, 14, 59, 0) });
			(await _service.ProcessDueReviewsAsync(_clock.Now.UtcDateTime)).Advised.Should().Be(1);
			_llm.Verify(l => l.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
		}

		[Test]
		public async Task Invalid_output_writes_no_advice()
		{
			Reply("999", 0.9, "Somewhere else.");

			(await _service.AdviseNowAsync(DepartmentId, CallId, "medic-lead")).Outcome.Should().Be(AiDestinationOutcomes.InvalidOutput);

			_inserted.Should().BeEmpty();
			_notes.Should().BeEmpty();
			_ledger.Should().Equal("InvalidOutput");
		}

		[Test]
		public async Task The_worker_claims_reviews_completes_them_and_gives_back_busy_ones()
		{
			var first = new AiDestinationReview { AiDestinationReviewId = "rv1", DepartmentId = DepartmentId, CallId = CallId, ReviewTrigger = (int)AiDestinationReviewTriggers.NoteAdded };
			var taken = new AiDestinationReview { AiDestinationReviewId = "rv2", DepartmentId = DepartmentId, CallId = 777, ReviewTrigger = (int)AiDestinationReviewTriggers.CallUpdated };
			_reviews.Setup(r => r.GetDueAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<AiDestinationReview> { first, taken });
			_reviews.Setup(r => r.TryClaimAsync("rv1", It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
			_reviews.Setup(r => r.TryClaimAsync("rv2", It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
			_reviews.Setup(r => r.CompleteAsync("rv1", It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

			var sweep = await _service.ProcessDueReviewsAsync(_clock.Now.UtcDateTime);

			sweep.Claimed.Should().Be(1);
			sweep.Advised.Should().Be(1);
			_inserted.Should().ContainSingle().Which.ReviewTrigger.Should().Be((int)AiDestinationReviewTriggers.NoteAdded);
			_reviews.Verify(r => r.CompleteAsync("rv1", It.IsAny<DateTime>(), AiDestinationOutcomes.Advised, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
			_reviews.Verify(r => r.DeleteProcessedBeforeAsync(_clock.Now.UtcDateTime.AddDays(-AiDestinationConfig.ReviewRetentionDays), It.IsAny<CancellationToken>()), Times.Once);

			// No idle inference slot: the review is released for the next pass, not completed.
			_admission.Setup(a => a.ReserveBackgroundAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<int>(),
				It.IsAny<CancellationToken>(), It.IsAny<int?>())).ReturnsAsync((AiUsageReservation)null);
			_call.CallNotes.Add(new CallNote { CallId = CallId, Note = "Second note", Source = (int)CallNoteSources.Web, Timestamp = new DateTime(2026, 11, 19, 14, 59, 0) });
			var busy = await _service.ProcessDueReviewsAsync(_clock.Now.UtcDateTime);

			busy.Retried.Should().Be(1);
			_reviews.Verify(r => r.ReleaseAsync("rv1", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
			_reviews.Verify(r => r.CompleteAsync("rv1", It.IsAny<DateTime>(), AiDestinationOutcomes.Busy, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Applying_sets_the_destination_through_the_call_save_and_notes_who_did_it()
		{
			_call.DestinationAddress = "Somewhere else";
			var advice = new AiDestinationAdvice
			{
				AiDestinationAdviceId = "adv-1", DepartmentId = DepartmentId, CallId = CallId, CurrentPoiId = 10, RecommendedPoiId = 20, IsChange = true,
				Confidence = 0.8, State = (int)AiDestinationAdviceStates.Open
			};
			_advices.Setup(a => a.GetForDepartmentAsync(DepartmentId, "adv-1", It.IsAny<CancellationToken>())).ReturnsAsync(advice);
			_advices.Setup(a => a.TryTransitionAsync(DepartmentId, "adv-1", AiDestinationAdviceStates.Open, AiDestinationAdviceStates.Applied, "medic-lead", It.IsAny<DateTime?>(),
				It.IsAny<CancellationToken>())).ReturnsAsync(true);

			var result = await _service.ApplyAsync(DepartmentId, "adv-1", "medic-lead");

			result.Outcome.Should().Be(AiDestinationOutcomes.Applied);
			result.Advice.State.Should().Be((int)AiDestinationAdviceStates.Applied);
			var saved = _saved.Should().ContainSingle().Subject;
			saved.DestinationPoiId.Should().Be(20);
			saved.DestinationAddress.Should().BeNull("one destination: the POI clears the free one");
			_notes.Should().ContainSingle().Which.Note.Should().Be("Enhanced AI destination advice applied by Ana Ruiz: destination set to Regional Trauma Center.");
			_events.Verify(e => e.SendMessage(It.Is<CallUpdatedEvent>(c => c.Call.CallId == CallId)), Times.Once);
			_adviceEvents.Should().ContainSingle();
		}

		[Test]
		public async Task Applying_advice_someone_else_handled_changes_nothing()
		{
			var advice = new AiDestinationAdvice { AiDestinationAdviceId = "adv-1", DepartmentId = DepartmentId, CallId = CallId, RecommendedPoiId = 20, State = (int)AiDestinationAdviceStates.Open };
			_advices.Setup(a => a.GetForDepartmentAsync(DepartmentId, "adv-1", It.IsAny<CancellationToken>())).ReturnsAsync(advice);
			_advices.Setup(a => a.TryTransitionAsync(DepartmentId, "adv-1", AiDestinationAdviceStates.Open, AiDestinationAdviceStates.Applied, It.IsAny<string>(), It.IsAny<DateTime?>(),
				It.IsAny<CancellationToken>())).ReturnsAsync(false);

			(await _service.ApplyAsync(DepartmentId, "adv-1", "medic-lead")).Outcome.Should().Be(AiDestinationOutcomes.NotOpen);
			_saved.Should().BeEmpty();

			var keep = new AiDestinationAdvice { AiDestinationAdviceId = "adv-2", DepartmentId = DepartmentId, CallId = CallId, State = (int)AiDestinationAdviceStates.Open };
			_advices.Setup(a => a.GetForDepartmentAsync(DepartmentId, "adv-2", It.IsAny<CancellationToken>())).ReturnsAsync(keep);
			(await _service.ApplyAsync(DepartmentId, "adv-2", "medic-lead")).Outcome.Should().Be(AiDestinationOutcomes.NothingToApply);
			(await _service.ApplyAsync(DepartmentId, "missing", "medic-lead")).Outcome.Should().Be(AiDestinationOutcomes.NotFound);
			_saved.Should().BeEmpty();
		}

		[Test]
		public async Task Dismissing_a_change_notes_it_with_the_reason()
		{
			var advice = new AiDestinationAdvice { AiDestinationAdviceId = "adv-1", DepartmentId = DepartmentId, CallId = CallId, RecommendedPoiId = 20, IsChange = true, State = (int)AiDestinationAdviceStates.Open };
			_advices.Setup(a => a.GetForDepartmentAsync(DepartmentId, "adv-1", It.IsAny<CancellationToken>())).ReturnsAsync(advice);
			_advices.Setup(a => a.TryTransitionAsync(DepartmentId, "adv-1", AiDestinationAdviceStates.Open, AiDestinationAdviceStates.Dismissed, "medic-lead", It.IsAny<DateTime?>(),
				It.IsAny<CancellationToken>())).ReturnsAsync(true);

			var result = await _service.DismissAsync(DepartmentId, "adv-1", "medic-lead", "  Patient refused\ntransfer ");

			result.Outcome.Should().Be(AiDestinationOutcomes.Dismissed);
			_notes.Should().ContainSingle().Which.Note.Should().Be("Enhanced AI destination advice dismissed by Ana Ruiz: Patient refused transfer");
			_saved.Should().BeEmpty();
			_adviceEvents.Should().ContainSingle();
		}

		[Test]
		public void Candidates_keep_a_farther_destination_that_adds_a_capability()
		{
			var ranked = Enumerable.Range(1, 12).Select(i => Destination(i, "H" + i, i * 60, Helipad)).ToList();
			ranked.Add(Destination(50, "Trauma Center", 3000, Trauma));

			var selected = AiDestinationAdvisorService.SelectCandidates(ranked, 10);

			selected.Select(d => d.Poi.PoiId).Should().Equal(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 50);
		}
	}
}
