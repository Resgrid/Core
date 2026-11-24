using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Identity;
using Resgrid.Model.Providers;
using Resgrid.Model.Queue;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services.Intake;

namespace Resgrid.Tests.Intake
{
	/// <summary>
	/// Intake (call-intake-and-ai-destinations-plan.md D5-D7): every message is claimed once, every refusal is recorded, and an
	/// accepted message always becomes a PENDING call with nobody on it, whether a template or Enhanced AI handles it.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class IntakeServiceTests
	{
		private const int DepartmentId = 55;
		private const string Number = "+17755550100";
		private const string Sender = "+15305550199";

		private sealed class FixedClock : TimeProvider
		{
			public static readonly DateTime Now = new DateTime(2026, 11, 19, 15, 0, 0, DateTimeKind.Utc);
			public override DateTimeOffset GetUtcNow() => new DateTimeOffset(Now);
		}

		private Mock<IIntakeChannelsRepository> _channels;
		private Mock<IIntakeMessagesRepository> _messages;
		private Mock<IVoiceIntakeSessionsRepository> _sessions;
		private Mock<IDepartmentsService> _departments;
		private Mock<IDepartmentSettingsService> _settings;
		private Mock<IFeatureToggleService> _flags;
		private Mock<ICallsService> _calls;
		private Mock<INumbersService> _numbers;
		private Mock<IGeoLocationProvider> _geo;
		private Mock<IEventAggregator> _events;
		private Mock<IQueueService> _queue;
		private Mock<ICommunicationService> _communication;
		private Mock<ICacheProvider> _cache;
		private Mock<IIntakeConversationService> _conversations;
		private Mock<IIntakeConversationOperations> _conversationOperations;
		private Mock<IIntakeBlockedSendersRepository> _blocked;
		private Mock<IDispatcherRoutingService> _deskRouting;
		private readonly List<DispatcherEnqueueRequest> _deskRequests = new List<DispatcherEnqueueRequest>();
		private IntakeChannel _channel;
		private Call _templateCall;
		private readonly List<Call> _saved = new List<Call>();
		private readonly List<(int State, int? CallId, string Reason)> _completions = new List<(int, int?, string)>();
		private readonly List<AiDispatchQueueItem> _queued = new List<AiDispatchQueueItem>();
		private IntakeMessage _claimed;
		private IntakeService _service;
		private (bool Enabled, int MaxLength, bool Enrich, int SmsLimit) _savedConfig;

		[SetUp]
		public void SetUp()
		{
			_savedConfig = (IntakeConfig.Enabled, IntakeConfig.MaxMessageLength, AiDispatchConfig.EnrichEnabled, DedicatedNumberAddonConfig.MonthlySmsIntakeLimit);
			IntakeConfig.Enabled = true;
			IntakeConfig.MaxMessageLength = 4000;
			AiDispatchConfig.EnrichEnabled = true;
			DedicatedNumberAddonConfig.MonthlySmsIntakeLimit = 2000;
			_saved.Clear();
			_completions.Clear();
			_queued.Clear();
			_deskRequests.Clear();
			_claimed = null;

			_channel = new IntakeChannel
			{
				IntakeChannelId = "ch-1", DepartmentId = DepartmentId, Name = "Hospital line", ChannelType = (int)IntakeChannelTypes.Sms,
				Address = Number, ProcessingMode = (int)IntakeProcessingModes.Template, IsEnabled = true
			};

			_channels = new Mock<IIntakeChannelsRepository>();
			_channels.Setup(c => c.GetLiveByAddressAsync((int)IntakeChannelTypes.Sms, Number)).ReturnsAsync(() => _channel);
			_channels.Setup(c => c.GetByIdAsync("ch-1")).ReturnsAsync(() => _channel);

			_messages = new Mock<IIntakeMessagesRepository>();
			_messages.Setup(m => m.TryClaimAsync(It.IsAny<IntakeMessage>(), It.IsAny<CancellationToken>()))
				.Callback((IntakeMessage m, CancellationToken _) => _claimed = m).ReturnsAsync(true);
			_messages.Setup(m => m.CompleteAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
				.Callback((string id, int state, int? callId, string reason, DateTime _, CancellationToken __) => _completions.Add((state, callId, reason)))
				.ReturnsAsync(true);

			_sessions = new Mock<IVoiceIntakeSessionsRepository>();
			_departments = new Mock<IDepartmentsService>();
			_departments.Setup(d => d.GetDepartmentByIdAsync(DepartmentId, false)).ReturnsAsync(new Department { DepartmentId = DepartmentId, Name = "Springfield Fire", ManagingUserId = "owner" });
			_departments.Setup(d => d.GetActiveAdminsForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<IdentityUser> { new IdentityUser { Id = "admin-1" } });
			_settings = new Mock<IDepartmentSettingsService>();
			_flags = new Mock<IFeatureToggleService>();
			_flags.Setup(f => f.IsEnabledAsync(FeatureFlagKeys.IntakeChannels, DepartmentId, It.IsAny<bool>(), It.IsAny<IDictionary<string, string>>())).ReturnsAsync(true);

			_templateCall = null;
			_calls = new Mock<ICallsService>();
			_calls.Setup(c => c.GetActiveCallPrioritiesForDepartmentAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new List<DepartmentCallPriority>
			{
				new DepartmentCallPriority { DepartmentCallPriorityId = 21, Name = "High", Sort = 1, IsDefault = true },
				new DepartmentCallPriority { DepartmentCallPriorityId = 22, Name = "Low", Sort = 2 }
			});
			_calls.Setup(c => c.GetCallTypesForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<CallType> { new CallType { CallTypeId = 11, Type = "Transfer" } });
			_calls.Setup(c => c.GenerateCallFromEmail(It.IsAny<int>(), It.IsAny<CallEmail>(), It.IsAny<string>(), It.IsAny<List<IdentityUser>>(), It.IsAny<Department>(),
					It.IsAny<List<Call>>(), It.IsAny<List<Unit>>(), It.IsAny<int>(), It.IsAny<List<DepartmentCallPriority>>(), It.IsAny<List<CallType>>()))
				.ReturnsAsync((int type, CallEmail email, string managing, List<IdentityUser> users, Department dept, List<Call> active, List<Unit> units, int priority,
					List<DepartmentCallPriority> priorities, List<CallType> types) => _templateCall ?? new Call
				{
					// What GenericTemplate builds: subject as name (the factory falls back to the body), body as nature, a dispatch per user.
					Name = email.Subject ?? email.Body, NatureOfCall = email.Body, Priority = priority, ReportingUserId = managing,
					CallSource = (int)CallSources.EmailImport, Dispatches = new Collection<CallDispatch> { new CallDispatch { UserId = "someone" } },
					GroupDispatches = new Collection<CallDispatchGroup> { new CallDispatchGroup { DepartmentGroupId = 3 } }
				});
			_calls.Setup(c => c.SaveCallAsync(It.IsAny<Call>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((Call c, CancellationToken _) => { c.CallId = 900; c.Number = "26-900"; _saved.Add(c); return c; });

			_numbers = new Mock<INumbersService>();
			_numbers.Setup(n => n.DoesNumberMatchAnyPattern(It.IsAny<List<string>>(), It.IsAny<string>()))
				.Returns((List<string> patterns, string number) => patterns.Any(p => p.Replace("+", "") == number.Replace("+", "")));
			_geo = new Mock<IGeoLocationProvider>();
			_events = new Mock<IEventAggregator>();
			_queue = new Mock<IQueueService>();
			_queue.Setup(q => q.EnqueueAiDispatchTriageAsync(It.IsAny<AiDispatchQueueItem>(), It.IsAny<CancellationToken>()))
				.Callback((AiDispatchQueueItem i, CancellationToken _) => _queued.Add(i)).ReturnsAsync(true);
			_communication = new Mock<ICommunicationService>();
			_cache = new Mock<ICacheProvider>();

			_conversations = new Mock<IIntakeConversationService>();
			_conversationOperations = new Mock<IIntakeConversationOperations>();
			_blocked = new Mock<IIntakeBlockedSendersRepository>();
			_deskRouting = new Mock<IDispatcherRoutingService>();
			_deskRouting.Setup(r => r.EnqueueAsync(It.IsAny<DispatcherEnqueueRequest>(), It.IsAny<CancellationToken>()))
				.Callback((DispatcherEnqueueRequest r, CancellationToken _) => _deskRequests.Add(r))
				.ReturnsAsync(new DispatcherRouteResult { Outcome = DispatcherRouteOutcomes.Queued });

			_service = new IntakeService(_channels.Object, _messages.Object, _sessions.Object, _departments.Object, _settings.Object, _flags.Object, _calls.Object,
				_numbers.Object, _geo.Object, _events.Object, _queue.Object, _communication.Object, _cache.Object, _conversations.Object, _conversationOperations.Object,
				_blocked.Object, new FixedClock(), new Lazy<IDispatcherRoutingService>(() => _deskRouting.Object));
		}

		[TearDown]
		public void TearDown()
		{
			(IntakeConfig.Enabled, IntakeConfig.MaxMessageLength, AiDispatchConfig.EnrichEnabled, DedicatedNumberAddonConfig.MonthlySmsIntakeLimit) = _savedConfig;
		}

		private static IntakeEnvelope Sms(string body = "Patient transfer from Mercy to St. Luke's, room 4", string id = "SM1") => new IntakeEnvelope
		{
			ChannelType = IntakeChannelTypes.Sms, ChannelAddress = "(775) 555-0100", ProviderMessageId = id, Sender = Sender, Body = body, ReceivedOnUtc = FixedClock.Now
		};

		[Test]
		public async Task host_switch_off_refuses_without_claiming()
		{
			IntakeConfig.Enabled = false;

			var result = await _service.ReceiveAsync(Sms());

			result.State.Should().Be(IntakeMessageStates.Refused);
			result.Reason.Should().Be("IntakeDisabled");
			_messages.Verify(m => m.TryClaimAsync(It.IsAny<IntakeMessage>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task unknown_address_is_refused_without_a_record()
		{
			var result = await _service.ReceiveAsync(new IntakeEnvelope { ChannelType = IntakeChannelTypes.Sms, ChannelAddress = "+17755550111", Body = "x", ProviderMessageId = "SM9" });

			result.State.Should().Be(IntakeMessageStates.Refused);
			result.Reason.Should().Be("NoChannel");
			_messages.Verify(m => m.TryClaimAsync(It.IsAny<IntakeMessage>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task address_is_normalized_before_lookup_and_the_claim_carries_channel_and_provider_id()
		{
			await _service.ReceiveAsync(Sms());

			_claimed.Should().NotBeNull();
			_claimed.IntakeChannelId.Should().Be("ch-1");
			_claimed.ProviderMessageId.Should().Be("SM1");
			_claimed.Sender.Should().Be(Sender);
			_claimed.State.Should().Be((int)IntakeMessageStates.Received);
		}

		[Test]
		public async Task flag_off_is_recorded_as_refused_and_creates_nothing()
		{
			_flags.Setup(f => f.IsEnabledAsync(FeatureFlagKeys.IntakeChannels, DepartmentId, It.IsAny<bool>(), It.IsAny<IDictionary<string, string>>())).ReturnsAsync(false);

			var result = await _service.ReceiveAsync(Sms());

			result.State.Should().Be(IntakeMessageStates.Refused);
			result.Reason.Should().Be("FeatureOff");
			_completions.Should().ContainSingle(c => c.State == (int)IntakeMessageStates.Refused && c.Reason == "FeatureOff");
			_saved.Should().BeEmpty();
		}

		[Test]
		public async Task disabled_channel_is_recorded_as_refused()
		{
			_channel.IsEnabled = false;

			var result = await _service.ReceiveAsync(Sms());

			result.State.Should().Be(IntakeMessageStates.Refused);
			result.Reason.Should().Be("ChannelDisabled");
			_saved.Should().BeEmpty();
		}

		[Test]
		public async Task transport_refusal_is_recorded_and_creates_nothing()
		{
			var envelope = Sms();
			envelope.RefusalReason = "NumberLapsed";

			var result = await _service.ReceiveAsync(envelope);

			result.State.Should().Be(IntakeMessageStates.Refused);
			_completions.Should().ContainSingle(c => c.Reason == "NumberLapsed");
			_saved.Should().BeEmpty();
		}

		[Test]
		public async Task a_blocked_senders_text_to_a_lapsed_number_is_recorded_as_blocked()
		{
			var envelope = Sms();
			envelope.RefusalReason = "NumberLapsed";
			_blocked.Setup(b => b.GetInForceAsync(DepartmentId, Sender, FixedClock.Now)).ReturnsAsync(new IntakeBlockedSender { DepartmentId = DepartmentId, Sender = Sender });

			var result = await _service.ReceiveAsync(envelope);

			result.State.Should().Be(IntakeMessageStates.SenderBlocked, "blocked messages are never counted toward the month");
			_completions.Should().ContainSingle().Which.Should().Be(((int)IntakeMessageStates.SenderBlocked, (int?)null, "SenderBlocked"));
			_saved.Should().BeEmpty();
		}

		[Test]
		public async Task an_unreadable_block_list_keeps_the_transports_refusal()
		{
			var envelope = Sms();
			envelope.RefusalReason = "NumberLapsed";
			_blocked.Setup(b => b.GetInForceAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<DateTime>())).ThrowsAsync(new InvalidOperationException("db down"));

			var result = await _service.ReceiveAsync(envelope);

			result.State.Should().Be(IntakeMessageStates.Refused);
			_completions.Should().ContainSingle(c => c.Reason == "NumberLapsed");
		}

		[Test]
		public async Task sender_outside_the_allowed_list_is_recorded_and_ignored()
		{
			_channel.AllowedSenders = "+19165550100, +19165550101";

			var result = await _service.ReceiveAsync(Sms());

			result.State.Should().Be(IntakeMessageStates.SenderNotAllowed);
			_completions.Should().ContainSingle(c => c.State == (int)IntakeMessageStates.SenderNotAllowed);
			_saved.Should().BeEmpty();
		}

		[Test]
		public async Task sender_on_the_allowed_list_creates_the_call()
		{
			_channel.AllowedSenders = "+19165550100," + Sender;

			var result = await _service.ReceiveAsync(Sms());

			result.State.Should().Be(IntakeMessageStates.CallCreated);
		}

		[Test]
		public void email_senders_match_addresses_and_domains()
		{
			var channel = new IntakeChannel { ChannelType = (int)IntakeChannelTypes.Email, AllowedSenders = "dispatch@county.gov, @mercy.org, *@stluke.org" };

			_service.IsSenderAllowed(channel, "Dispatch@County.gov").Should().BeTrue();
			_service.IsSenderAllowed(channel, "nurse@mercy.org").Should().BeTrue();
			_service.IsSenderAllowed(channel, "er@stluke.org").Should().BeTrue();
			_service.IsSenderAllowed(channel, "someone@evilmercy.org").Should().BeFalse();
			_service.IsSenderAllowed(channel, null).Should().BeFalse();
			_service.IsSenderAllowed(new IntakeChannel { ChannelType = (int)IntakeChannelTypes.Email }, "anyone@x.org").Should().BeTrue();
		}

		[Test]
		public async Task a_redelivery_is_a_duplicate_and_returns_the_first_call()
		{
			_messages.Setup(m => m.TryClaimAsync(It.IsAny<IntakeMessage>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
			_messages.Setup(m => m.GetByProviderMessageIdAsync("ch-1", "SM1"))
				.ReturnsAsync(new IntakeMessage { IntakeMessageId = "m-1", State = (int)IntakeMessageStates.CallCreated, CallId = 812 });

			var result = await _service.ReceiveAsync(Sms());

			result.State.Should().Be(IntakeMessageStates.Duplicate);
			result.CallId.Should().Be(812);
			_saved.Should().BeEmpty();
			_calls.Verify(c => c.SaveCallAsync(It.IsAny<Call>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task a_retry_of_a_failed_message_takes_the_claim_over_once()
		{
			_messages.Setup(m => m.TryClaimAsync(It.IsAny<IntakeMessage>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
			_messages.Setup(m => m.GetByProviderMessageIdAsync("ch-1", "SM1"))
				.ReturnsAsync(new IntakeMessage { IntakeMessageId = "m-1", State = (int)IntakeMessageStates.Failed, ReceivedOn = FixedClock.Now.AddMinutes(-1) });
			_messages.Setup(m => m.TryReclaimAsync("m-1", (int)IntakeMessageStates.Failed, FixedClock.Now, FixedClock.Now.AddMinutes(-10), It.IsAny<CancellationToken>()))
				.ReturnsAsync(true);

			var result = await _service.ReceiveAsync(Sms());

			result.State.Should().Be(IntakeMessageStates.CallCreated);
			_saved.Should().ContainSingle();
			// A failed claim never saved a call, so nothing is looked up for it.
			_messages.Verify(m => m.GetUnclaimedIntakeCallIdAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int[]>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task a_message_still_in_progress_elsewhere_is_a_duplicate_the_transport_asks_to_redeliver()
		{
			_messages.Setup(m => m.TryClaimAsync(It.IsAny<IntakeMessage>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
			_messages.Setup(m => m.GetByProviderMessageIdAsync("ch-1", "SM1"))
				.ReturnsAsync(new IntakeMessage { IntakeMessageId = "m-1", State = (int)IntakeMessageStates.Received, ReceivedOn = FixedClock.Now.AddMinutes(-1) });

			var result = await _service.ReceiveAsync(Sms());

			result.State.Should().Be(IntakeMessageStates.Duplicate);
			result.Reason.Should().Be(IntakeResultReasons.DuplicateInProgress);
			result.CallId.Should().BeNull();
			_messages.Verify(m => m.TryReclaimAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task a_finished_message_is_a_plain_duplicate()
		{
			_messages.Setup(m => m.TryClaimAsync(It.IsAny<IntakeMessage>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
			_messages.Setup(m => m.GetByProviderMessageIdAsync("ch-1", "SM1"))
				.ReturnsAsync(new IntakeMessage { IntakeMessageId = "m-1", State = (int)IntakeMessageStates.SenderNotAllowed, ReceivedOn = FixedClock.Now.AddMinutes(-1) });

			var result = await _service.ReceiveAsync(Sms());

			result.State.Should().Be(IntakeMessageStates.Duplicate);
			result.Reason.Should().Be("Duplicate");
		}

		[Test]
		public async Task a_retry_after_the_first_attempt_died_between_saving_the_call_and_recording_it_returns_that_call()
		{
			// The first attempt saved call 812 and its process died before the claim got the id: the claim stayed Received.
			var receivedOn = FixedClock.Now.AddMinutes(-20);
			_messages.Setup(m => m.TryClaimAsync(It.IsAny<IntakeMessage>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
			_messages.Setup(m => m.GetByProviderMessageIdAsync("ch-1", "SM1"))
				.ReturnsAsync(new IntakeMessage { IntakeMessageId = "m-1", State = (int)IntakeMessageStates.Received, ReceivedOn = receivedOn });
			_messages.Setup(m => m.TryReclaimAsync("m-1", (int)IntakeMessageStates.Received, FixedClock.Now, FixedClock.Now.AddMinutes(-10), It.IsAny<CancellationToken>()))
				.ReturnsAsync(true);
			_messages.Setup(m => m.GetUnclaimedIntakeCallIdAsync(DepartmentId, "SM1",
					It.Is<int[]>(s => s.Contains((int)CallSources.Intake) && s.Contains((int)CallSources.VoiceIntake)), receivedOn.AddDays(-1), It.IsAny<CancellationToken>()))
				.ReturnsAsync(812);

			var result = await _service.ReceiveAsync(Sms());

			result.State.Should().Be(IntakeMessageStates.Duplicate);
			result.CallId.Should().Be(812);
			_saved.Should().BeEmpty("the call the dead attempt saved is the message's call");
			_completions.Should().ContainSingle(c => c.State == (int)IntakeMessageStates.CallCreated && c.CallId == 812);
		}

		[Test]
		public async Task a_retry_of_an_abandoned_claim_with_no_saved_call_creates_it()
		{
			_messages.Setup(m => m.TryClaimAsync(It.IsAny<IntakeMessage>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
			_messages.Setup(m => m.GetByProviderMessageIdAsync("ch-1", "SM1"))
				.ReturnsAsync(new IntakeMessage { IntakeMessageId = "m-1", State = (int)IntakeMessageStates.Received, ReceivedOn = FixedClock.Now.AddMinutes(-20) });
			_messages.Setup(m => m.TryReclaimAsync("m-1", (int)IntakeMessageStates.Received, FixedClock.Now, FixedClock.Now.AddMinutes(-10), It.IsAny<CancellationToken>()))
				.ReturnsAsync(true);

			var result = await _service.ReceiveAsync(Sms());

			result.State.Should().Be(IntakeMessageStates.CallCreated);
			_saved.Should().ContainSingle().Which.SourceIdentifier.Should().Be("SM1");
		}

		[Test]
		public async Task a_retry_that_loses_the_reclaim_race_creates_nothing()
		{
			_messages.Setup(m => m.TryClaimAsync(It.IsAny<IntakeMessage>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
			_messages.Setup(m => m.GetByProviderMessageIdAsync("ch-1", "SM1"))
				.ReturnsAsync(new IntakeMessage { IntakeMessageId = "m-1", State = (int)IntakeMessageStates.Received, ReceivedOn = FixedClock.Now.AddMinutes(-20) });
			_messages.Setup(m => m.TryReclaimAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(false);

			var result = await _service.ReceiveAsync(Sms());

			result.State.Should().Be(IntakeMessageStates.Duplicate);
			result.Reason.Should().Be(IntakeResultReasons.DuplicateInProgress);
			_saved.Should().BeEmpty();
		}

		[Test]
		public async Task template_mode_creates_a_pending_intake_call_with_nobody_on_it()
		{
			_channel.TemplateFormat = (int)CallEmailTypes.GenericPage;

			var result = await _service.ReceiveAsync(Sms());

			result.State.Should().Be(IntakeMessageStates.CallCreated);
			result.CallId.Should().Be(900);
			_calls.Verify(c => c.GenerateCallFromEmail((int)CallEmailTypes.GenericPage, It.IsAny<CallEmail>(), "owner", It.Is<List<IdentityUser>>(u => u.Count == 0),
				It.IsAny<Department>(), It.Is<List<Call>>(a => a.Count == 0), It.Is<List<Unit>>(u => u.Count == 0), 21, It.IsAny<List<DepartmentCallPriority>>(), It.IsAny<List<CallType>>()), Times.Once);

			var call = _saved.Single();
			call.State.Should().Be((int)CallStates.Pending);
			call.CallSource.Should().Be((int)CallSources.Intake);
			call.DepartmentId.Should().Be(DepartmentId);
			call.Dispatches.Should().BeEmpty();
			call.GroupDispatches.Should().BeEmpty();
			call.UnitDispatches.Should().BeEmpty();
			call.RoleDispatches.Should().BeEmpty();
			call.DispatchOn.Should().BeNull();
			call.SourceIdentifier.Should().Be("SM1");
			_completions.Should().ContainSingle(c => c.State == (int)IntakeMessageStates.CallCreated && c.CallId == 900);
			_events.Verify(e => e.SendMessage(It.Is<CallUpdatedEvent>(u => u.DepartmentId == DepartmentId && u.Call == call)), Times.Once);
			_events.Verify(e => e.SendMessage(It.IsAny<CallAddedEvent>()), Times.Never);
			_queued.Should().BeEmpty("template channels never queue AI");
		}

		[Test]
		public async Task a_chat_apps_call_goes_on_the_dispatcher_desk_as_a_chat_before_the_call_event()
		{
			_channel.ChannelType = (int)IntakeChannelTypes.Slack;
			var order = new List<string>();
			_deskRouting.Setup(r => r.EnqueueAsync(It.IsAny<DispatcherEnqueueRequest>(), It.IsAny<CancellationToken>()))
				.Callback((DispatcherEnqueueRequest r, CancellationToken _) => { _deskRequests.Add(r); order.Add("desk"); })
				.ReturnsAsync(new DispatcherRouteResult { Outcome = DispatcherRouteOutcomes.Queued });
			_events.Setup(e => e.SendMessage(It.IsAny<CallUpdatedEvent>())).Callback(() => order.Add("event"));

			var result = await _service.ReceiveAsync(new IntakeEnvelope
			{
				ChannelType = IntakeChannelTypes.Slack, IntakeChannelId = "ch-1", ProviderMessageId = "slack-1", Sender = "U0123", SenderName = "Pat Doe",
				Body = "Smoke from the dumpster behind 12 Main St", ReceivedOnUtc = FixedClock.Now
			});

			result.State.Should().Be(IntakeMessageStates.CallCreated);
			var request = _deskRequests.Should().ContainSingle().Subject;
			request.Kind.Should().Be(DispatcherQueueItemKinds.Chat);
			request.CallId.Should().Be(900);
			request.DepartmentId.Should().Be(DepartmentId);
			request.SourceAddress.Should().Be("U0123");
			request.SourceName.Should().Be("Pat Doe");
			request.Body.Should().Be("Smoke from the dumpster behind 12 Main St");
			order.Should().Equal(new[] { "desk", "event" }, "the event's pending-call hand-off then finds the chat on the desk");
		}

		[Test]
		public async Task a_desk_failure_never_loses_a_chat_apps_call()
		{
			_channel.ChannelType = (int)IntakeChannelTypes.WhatsApp;
			_deskRouting.Setup(r => r.EnqueueAsync(It.IsAny<DispatcherEnqueueRequest>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("desk down"));

			var result = await _service.ReceiveAsync(new IntakeEnvelope
			{
				ChannelType = IntakeChannelTypes.WhatsApp, IntakeChannelId = "ch-1", ProviderMessageId = "wa-1", Sender = Sender, Body = "Help", ReceivedOnUtc = FixedClock.Now
			});

			result.State.Should().Be(IntakeMessageStates.CallCreated);
			_events.Verify(e => e.SendMessage(It.IsAny<CallUpdatedEvent>()), Times.Once);
		}

		[Test]
		public async Task a_text_or_email_call_is_left_to_the_desks_pending_call_hand_off()
		{
			await _service.ReceiveAsync(Sms());

			_deskRequests.Should().BeEmpty();
		}

		[TestCase(IntakeChannelTypes.WhatsApp, true)]
		[TestCase(IntakeChannelTypes.Slack, true)]
		[TestCase(IntakeChannelTypes.Teams, true)]
		[TestCase(IntakeChannelTypes.Telegram, true)]
		[TestCase(IntakeChannelTypes.Discord, true)]
		[TestCase(IntakeChannelTypes.GoogleChat, true)]
		[TestCase(IntakeChannelTypes.Sms, false)]
		[TestCase(IntakeChannelTypes.Email, false)]
		[TestCase(IntakeChannelTypes.Webhook, false)]
		[TestCase(IntakeChannelTypes.Voice, false)]
		public void chat_channels_are_the_chat_apps(IntakeChannelTypes channelType, bool chat) =>
			IntakeService.IsChatChannel(channelType).Should().Be(chat);

		[Test]
		public async Task null_template_format_uses_generic()
		{
			await _service.ReceiveAsync(Sms());

			_calls.Verify(c => c.GenerateCallFromEmail((int)CallEmailTypes.Generic, It.IsAny<CallEmail>(), It.IsAny<string>(), It.IsAny<List<IdentityUser>>(), It.IsAny<Department>(),
				It.IsAny<List<Call>>(), It.IsAny<List<Unit>>(), It.IsAny<int>(), It.IsAny<List<DepartmentCallPriority>>(), It.IsAny<List<CallType>>()), Times.Once);
		}

		[Test]
		public async Task enhanced_ai_mode_saves_the_raw_message_then_queues_enrichment()
		{
			_channel.ProcessingMode = (int)IntakeProcessingModes.EnhancedAi;

			var result = await _service.ReceiveAsync(Sms());

			result.State.Should().Be(IntakeMessageStates.CallCreated);
			_calls.Verify(c => c.GenerateCallFromEmail(It.IsAny<int>(), It.IsAny<CallEmail>(), It.IsAny<string>(), It.IsAny<List<IdentityUser>>(), It.IsAny<Department>(),
				It.IsAny<List<Call>>(), It.IsAny<List<Unit>>(), It.IsAny<int>(), It.IsAny<List<DepartmentCallPriority>>(), It.IsAny<List<CallType>>()), Times.Never);
			var call = _saved.Single();
			call.State.Should().Be((int)CallStates.Pending);
			call.NatureOfCall.Should().Be("Patient transfer from Mercy to St. Luke's, room 4");
			var item = _queued.Single();
			item.CallId.Should().Be(900);
			item.Channel.Should().Be(AiDispatchChannels.IntakeSms);
			item.NameIsPlaceholder.Should().BeTrue();
			item.SenderNotAllowed.Should().BeFalse();
		}

		[Test]
		public async Task ai_host_switch_off_still_creates_the_call()
		{
			_channel.ProcessingMode = (int)IntakeProcessingModes.EnhancedAi;
			AiDispatchConfig.EnrichEnabled = false;

			var result = await _service.ReceiveAsync(Sms());

			result.State.Should().Be(IntakeMessageStates.CallCreated);
			_queued.Should().BeEmpty();
		}

		[Test]
		public async Task without_a_subject_the_name_is_channel_and_sender_and_defaults_apply()
		{
			_channel.DefaultPriority = 22;
			_channel.DefaultCallTypeId = 11;

			await _service.ReceiveAsync(Sms());

			var call = _saved.Single();
			call.Name.Should().Be("Hospital line: " + Sender);
			call.Priority.Should().Be(22);
			call.Type.Should().Be("Transfer");
			call.CallTypeId.Should().Be(11);
			call.ContactNumber.Should().Be(Sender);
			call.ReportingUserId.Should().Be("owner");
		}

		[Test]
		public async Task a_subject_names_the_call()
		{
			var envelope = Sms();
			envelope.Subject = "Transfer request";

			await _service.ReceiveAsync(envelope);

			_saved.Single().Name.Should().Be("Transfer request");
		}

		[Test]
		public async Task a_template_call_type_is_kept_over_the_channel_default()
		{
			_channel.DefaultCallTypeId = 11;
			_templateCall = new Call { Name = "Structure fire", NatureOfCall = "body", Type = "Fire", Priority = 21 };

			await _service.ReceiveAsync(Sms());

			_saved.Single().Type.Should().Be("Fire");
		}

		[Test]
		public async Task a_template_that_matches_a_live_call_never_touches_it()
		{
			_templateCall = new Call { CallId = 77, Name = "2nd alarm", NatureOfCall = "upgrade", State = (int)CallStates.Active };

			var result = await _service.ReceiveAsync(Sms());

			result.State.Should().Be(IntakeMessageStates.CallCreated);
			var call = _saved.Single();
			call.Should().NotBeSameAs(_templateCall);
			call.State.Should().Be((int)CallStates.Pending);
			call.NatureOfCall.Should().Be("Patient transfer from Mercy to St. Luke's, room 4");
			result.Reason.Should().Be("TemplateMatchedExistingCall");
		}

		[Test]
		public async Task a_template_that_finds_nothing_still_saves_the_message()
		{
			_calls.Setup(c => c.GenerateCallFromEmail(It.IsAny<int>(), It.IsAny<CallEmail>(), It.IsAny<string>(), It.IsAny<List<IdentityUser>>(), It.IsAny<Department>(),
				It.IsAny<List<Call>>(), It.IsAny<List<Unit>>(), It.IsAny<int>(), It.IsAny<List<DepartmentCallPriority>>(), It.IsAny<List<CallType>>())).ReturnsAsync((Call)null);

			var result = await _service.ReceiveAsync(Sms());

			result.State.Should().Be(IntakeMessageStates.CallCreated);
			result.Reason.Should().Be("TemplateFoundNoCall");
			_saved.Single().NatureOfCall.Should().Be("Patient transfer from Mercy to St. Luke's, room 4");
		}

		[Test]
		public async Task a_long_message_is_cut_with_a_marker()
		{
			IntakeConfig.MaxMessageLength = 120;
			_channel.ProcessingMode = (int)IntakeProcessingModes.EnhancedAi;

			await _service.ReceiveAsync(Sms(new string('a', 500)));

			var nature = _saved.Single().NatureOfCall;
			nature.Length.Should().Be(120);
			nature.Should().EndWith("[message shortened]");
		}

		[Test]
		public async Task a_failed_save_is_recorded_and_reported()
		{
			_calls.Setup(c => c.SaveCallAsync(It.IsAny<Call>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("db down"));

			var result = await _service.ReceiveAsync(Sms());

			result.State.Should().Be(IntakeMessageStates.Failed);
			_completions.Should().ContainSingle(c => c.State == (int)IntakeMessageStates.Failed && c.Reason.Contains("db down"));
		}

		[Test]
		public async Task a_failure_after_the_call_was_saved_keeps_the_call_on_the_claim()
		{
			_events.Setup(e => e.SendMessage(It.IsAny<CallUpdatedEvent>())).Throws(new InvalidOperationException("bus down"));

			var result = await _service.ReceiveAsync(Sms());

			result.State.Should().Be(IntakeMessageStates.CallCreated);
			result.CallId.Should().Be(900);
			_completions.Last().Should().Be(((int)IntakeMessageStates.CallCreated, (int?)900, "AfterSaveFailed"));
		}

		[Test]
		public async Task a_save_that_failed_after_the_call_row_committed_keeps_the_call_on_the_claim()
		{
			_calls.Setup(c => c.SaveCallAsync(It.IsAny<Call>(), It.IsAny<CancellationToken>()))
				.Callback((Call c, CancellationToken _) => c.CallId = 901).ThrowsAsync(new InvalidOperationException("dispatch rows failed"));

			var result = await _service.ReceiveAsync(Sms());

			result.State.Should().Be(IntakeMessageStates.CallCreated);
			result.CallId.Should().Be(901, "a retry must find this call, not create another");
		}

		[Test]
		public async Task acknowledgement_only_when_the_channel_asks_and_never_echoes_the_message()
		{
			var silent = await _service.ReceiveAsync(Sms(id: "SM1"));
			silent.Acknowledgement.Should().BeNull();

			_channel.SendAcknowledgement = true;
			var acked = await _service.ReceiveAsync(Sms(id: "SM2"));

			acked.Acknowledgement.Should().Contain("26-900").And.Contain("Springfield Fire");
			acked.Acknowledgement.Should().NotContain("Mercy");
		}

		[Test]
		public async Task voice_intake_creates_a_voice_call_with_contact_answers_and_always_queues_ai()
		{
			_channel.ChannelType = (int)IntakeChannelTypes.Voice;
			_channels.Setup(c => c.GetLiveByAddressAsync((int)IntakeChannelTypes.Voice, Number)).ReturnsAsync(() => _channel);
			var answers = new Dictionary<string, string> { ["Nature"] = "my neighbor fell", ["Location"] = "12 Oak Street", ["CallerName"] = "Pat Doe", ["Callback"] = "5305550123" };

			var result = await _service.ReceiveAsync(new IntakeEnvelope
			{
				ChannelType = IntakeChannelTypes.Voice, ChannelAddress = Number, ProviderMessageId = "CA1", Sender = Sender,
				Body = VoiceIntakeService.Transcript(answers), Answers = answers
			});

			result.State.Should().Be(IntakeMessageStates.CallCreated);
			var call = _saved.Single();
			call.CallSource.Should().Be((int)CallSources.VoiceIntake);
			call.State.Should().Be((int)CallStates.Pending);
			call.ContactName.Should().Be("Pat Doe");
			call.ContactNumber.Should().Be("5305550123");
			call.NatureOfCall.Should().Contain("Nature: my neighbor fell").And.Contain("Location: 12 Oak Street");
			_calls.Verify(c => c.GenerateCallFromEmail(It.IsAny<int>(), It.IsAny<CallEmail>(), It.IsAny<string>(), It.IsAny<List<IdentityUser>>(), It.IsAny<Department>(),
				It.IsAny<List<Call>>(), It.IsAny<List<Unit>>(), It.IsAny<int>(), It.IsAny<List<DepartmentCallPriority>>(), It.IsAny<List<CallType>>()), Times.Never);
			_queued.Single().Channel.Should().Be(AiDispatchChannels.IntakeVoice);
		}

		[Test]
		public async Task sms_over_the_fair_use_limit_still_creates_calls_and_alerts_admins_once_a_month()
		{
			DedicatedNumberAddonConfig.MonthlySmsIntakeLimit = 10;
			_messages.Setup(m => m.CountAsync(DepartmentId, (int)IntakeChannelTypes.Sms, new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
				new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc), It.IsAny<int[]>())).ReturnsAsync(11);
			var markers = UseMarkerCache();
			_communication.Setup(c => c.SendNotificationDeliveredAsync(It.IsAny<string>(), DepartmentId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Department>(),
				It.IsAny<string>(), It.IsAny<UserProfile>(), It.IsAny<bool>(), It.IsAny<string>())).ReturnsAsync(true);

			var first = await _service.ReceiveAsync(Sms(id: "SM1"));
			var second = await _service.ReceiveAsync(Sms(id: "SM2"));

			first.State.Should().Be(IntakeMessageStates.CallCreated);
			second.State.Should().Be(IntakeMessageStates.CallCreated);
			_communication.Verify(c => c.SendNotificationDeliveredAsync("admin-1", DepartmentId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Department>(), It.IsAny<string>(),
				It.IsAny<UserProfile>(), It.IsAny<bool>(), It.IsAny<string>()), Times.Once);
			markers.Should().ContainKey("IntakeSmsFairUse_55_202611");
		}

		[Test]
		public async Task a_fair_use_notice_that_failed_for_one_admin_is_retried_for_that_admin_only()
		{
			DedicatedNumberAddonConfig.MonthlySmsIntakeLimit = 10;
			_messages.Setup(m => m.CountAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int[]>())).ReturnsAsync(11);
			_departments.Setup(d => d.GetActiveAdminsForDepartmentAsync(DepartmentId))
				.ReturnsAsync(new List<IdentityUser> { new IdentityUser { Id = "admin-1" }, new IdentityUser { Id = "admin-2" } });
			var markers = UseMarkerCache();
			var admin2Attempts = 0;
			_communication.Setup(c => c.SendNotificationDeliveredAsync("admin-1", DepartmentId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Department>(),
				It.IsAny<string>(), It.IsAny<UserProfile>(), It.IsAny<bool>(), It.IsAny<string>())).ReturnsAsync(true);
			// The first notice to admin-2 throws (provider outage); the next one goes out.
			_communication.Setup(c => c.SendNotificationDeliveredAsync("admin-2", DepartmentId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Department>(),
					It.IsAny<string>(), It.IsAny<UserProfile>(), It.IsAny<bool>(), It.IsAny<string>()))
				.ReturnsAsync(() => ++admin2Attempts == 1 ? throw new InvalidOperationException("outage") : true);

			await _service.ReceiveAsync(Sms(id: "SM1"));
			markers.Should().NotContainKey("IntakeSmsFairUse_55_202611", "an admin was not told, so the next text over the limit tries again");

			await _service.ReceiveAsync(Sms(id: "SM2"));
			await _service.ReceiveAsync(Sms(id: "SM3"));

			_communication.Verify(c => c.SendNotificationDeliveredAsync("admin-1", DepartmentId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Department>(), It.IsAny<string>(),
				It.IsAny<UserProfile>(), It.IsAny<bool>(), It.IsAny<string>()), Times.Once, "admin-1 was told the first time");
			admin2Attempts.Should().Be(2);
			markers.Should().ContainKey("IntakeSmsFairUse_55_202611");
		}

		[Test]
		public async Task a_fair_use_notice_that_cannot_be_prepared_frees_the_marker_for_the_next_text()
		{
			DedicatedNumberAddonConfig.MonthlySmsIntakeLimit = 10;
			_messages.Setup(m => m.CountAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int[]>())).ReturnsAsync(11);
			var markers = UseMarkerCache();
			// The admin list read fails after this attempt set the department's marker: nobody was told.
			_departments.Setup(d => d.GetActiveAdminsForDepartmentAsync(DepartmentId)).ThrowsAsync(new InvalidOperationException("db down"));

			(await _service.ReceiveAsync(Sms(id: "SM1"))).State.Should().Be(IntakeMessageStates.CallCreated);

			markers.Should().NotContainKey("IntakeSmsFairUse_55_202611", "the next text over the limit must try again");
		}

		[Test]
		public async Task call_sources_are_read_in_batches_so_every_call_of_a_long_list_gets_one()
		{
			var ids = Enumerable.Range(1, IntakeService.CallLookupBatchSize * 2 + 100).ToList();
			var batches = new List<int>();
			_messages.Setup(m => m.GetByCallIdsAsync(DepartmentId, It.IsAny<IEnumerable<int>>()))
				.ReturnsAsync((int _, IEnumerable<int> batch) =>
				{
					var list = batch.ToList();
					batches.Add(list.Count);
					return list.Select(id => new IntakeMessage { DepartmentId = DepartmentId, CallId = id, IntakeChannelId = "ch-1", ChannelType = (int)IntakeChannelTypes.Sms, Screening = 1 }).ToList();
				});
			_channels.Setup(c => c.GetAllByDepartmentIdAsync(DepartmentId)).ReturnsAsync(new List<IntakeChannel> { _channel });

			var sources = await _service.GetCallSourcesAsync(DepartmentId, ids);

			sources.Should().HaveCount(ids.Count);
			sources[ids.Last()].Screening.Should().Be(1, "the last call is past the first 500 and still gets its screening");
			batches.Should().Equal(new[] { IntakeService.CallLookupBatchSize, IntakeService.CallLookupBatchSize, 100 });
		}

		[Test]
		public async Task a_failed_saved_call_lookup_gives_the_takeover_back_and_builds_nothing()
		{
			var receivedOn = FixedClock.Now.AddMinutes(-20);
			_messages.Setup(m => m.TryClaimAsync(It.IsAny<IntakeMessage>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
			_messages.Setup(m => m.GetByProviderMessageIdAsync("ch-1", "SM1"))
				.ReturnsAsync(new IntakeMessage { IntakeMessageId = "m-1", State = (int)IntakeMessageStates.Received, ReceivedOn = receivedOn });
			_messages.Setup(m => m.TryReclaimAsync("m-1", (int)IntakeMessageStates.Received, FixedClock.Now, FixedClock.Now.AddMinutes(-10), It.IsAny<CancellationToken>()))
				.ReturnsAsync(true);
			_messages.Setup(m => m.GetUnclaimedIntakeCallIdAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int[]>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
				.ThrowsAsync(new InvalidOperationException("db down"));

			Func<Task> receive = () => _service.ReceiveAsync(Sms());

			// An unknown answer is not "no saved call": nothing is built, and the takeover goes back so the next delivery looks again at once.
			await receive.Should().ThrowAsync<InvalidOperationException>();
			_saved.Should().BeEmpty();
			_messages.Verify(m => m.ReleaseReclaimAsync("m-1", FixedClock.Now, receivedOn, It.IsAny<CancellationToken>()), Times.Once,
				"only the takeover this attempt made (stamped now) is given back");
			_completions.Should().BeEmpty("the claim is never marked Failed: that state skips the lookup and could make a second call");
		}

		[TestCase("*")]
		[TestCase("+15305550100, *")]
		public void a_wildcard_allowed_list_is_no_allowed_list_for_the_hourly_limit(string allowed)
		{
			// "*" lets anyone in (IsSenderAllowed), so it must not also exempt everyone from the per-sender cap.
			IntakeService.IsSenderLimited(new IntakeChannel { ChannelType = (int)IntakeChannelTypes.Sms, AllowedSenders = allowed }).Should().BeTrue();
		}

		/// <summary>A working cache whose entries the test can see.</summary>
		private Dictionary<string, string> UseMarkerCache()
		{
			var markers = new Dictionary<string, string>();
			_cache.Setup(c => c.TryGetStringAsync(It.IsAny<string>())).ReturnsAsync((string key) => (true, markers.GetValueOrDefault(key)));
			_cache.Setup(c => c.SetStringAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>()))
				.Callback((string key, string value, TimeSpan _) => markers[key] = value).ReturnsAsync(true);
			_cache.Setup(c => c.RemoveAsync(It.IsAny<string>())).Callback((string key) => markers.Remove(key)).ReturnsAsync(true);
			return markers;
		}

		[Test]
		public async Task sms_under_the_limit_alerts_nobody()
		{
			_messages.Setup(m => m.CountAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int[]>())).ReturnsAsync(5);

			await _service.ReceiveAsync(Sms());

			_communication.Verify(c => c.SendNotificationDeliveredAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Department>(), It.IsAny<string>(),
				It.IsAny<UserProfile>(), It.IsAny<bool>(), It.IsAny<string>()), Times.Never);
			_communication.Verify(c => c.SendNotificationAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Department>(), It.IsAny<string>(),
				It.IsAny<UserProfile>(), It.IsAny<bool>()), Times.Never);
		}

		[Test]
		public async Task voice_monthly_count_is_sessions_started_this_month()
		{
			_sessions.Setup(s => s.CountStartedAsync(DepartmentId, new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc))).ReturnsAsync(37);

			(await _service.GetMonthlyCountAsync(DepartmentId, IntakeChannelTypes.Voice, FixedClock.Now)).Should().Be(37);
		}

		[TestCase(true)]
		[TestCase(false)]
		public async Task purge_uses_the_configured_retention(bool deleteCarrierCopy)
		{
			var saved = (IntakeConfig.MessageRetentionDays, IntakeConfig.VoiceSessionRetentionHours, IntakeConfig.VoiceRecordingDeleteCarrierCopy);
			try
			{
				IntakeConfig.MessageRetentionDays = 90;
				IntakeConfig.VoiceSessionRetentionHours = 48;
				// Sessions whose recording worker 80 still deletes at Twilio are kept (answers cleared) only when it deletes the copies at all.
				IntakeConfig.VoiceRecordingDeleteCarrierCopy = deleteCarrierCopy;
				_messages.Setup(m => m.PurgeReceivedBeforeAsync(FixedClock.Now.AddDays(-90), It.IsAny<CancellationToken>())).ReturnsAsync(4);
				_sessions.Setup(s => s.PurgeStartedBeforeAsync(FixedClock.Now.AddHours(-48), deleteCarrierCopy, It.IsAny<CancellationToken>())).ReturnsAsync(2);

				var result = await _service.PurgeExpiredAsync(FixedClock.Now);

				result.Messages.Should().Be(4);
				result.VoiceSessions.Should().Be(2);
			}
			finally
			{
				(IntakeConfig.MessageRetentionDays, IntakeConfig.VoiceSessionRetentionHours, IntakeConfig.VoiceRecordingDeleteCarrierCopy) = saved;
			}
		}

		[Test]
		public async Task call_sources_name_the_channel_even_when_it_was_deleted()
		{
			_messages.Setup(m => m.GetByCallIdsAsync(DepartmentId, It.IsAny<IEnumerable<int>>())).ReturnsAsync(new List<IntakeMessage>
			{
				new IntakeMessage { DepartmentId = DepartmentId, IntakeChannelId = "ch-1", ChannelType = (int)IntakeChannelTypes.Sms, State = (int)IntakeMessageStates.CallCreated, CallId = 900 }
			});
			_channel.IsDeleted = true;
			_channels.Setup(c => c.GetAllByDepartmentIdAsync(DepartmentId)).ReturnsAsync(new[] { _channel });

			var sources = await _service.GetCallSourcesAsync(DepartmentId, new[] { 900, 901 });

			sources.Should().ContainKey(900);
			sources[900].ChannelName.Should().Be("Hospital line");
			sources[900].ChannelType.Should().Be((int)IntakeChannelTypes.Sms);
			sources.Should().NotContainKey(901);
		}

		#region SMS conversations (plan §8 D15)

		private static IntakeEnvelope DedicatedSms(string body = "Car crash on Route 9", string id = "SM1")
		{
			var envelope = Sms(body, id);
			envelope.OpensConversation = true;
			return envelope;
		}

		[Test]
		public async Task a_conversation_welcome_replaces_the_acknowledgement()
		{
			_channel.SendAcknowledgement = true;
			_conversations.Setup(c => c.StartFromIntakeAsync(_channel, It.IsAny<IntakeEnvelope>(), It.IsAny<Call>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new IntakeConversation { IntakeConversationId = "conv-1", DepartmentId = DepartmentId, CallId = 900 });

			var result = await _service.ReceiveAsync(DedicatedSms());

			result.State.Should().Be(IntakeMessageStates.CallCreated);
			result.IntakeConversationId.Should().Be("conv-1");
			result.Acknowledgement.Should().BeNull("one text to the caller, never two");
			_conversations.Verify(c => c.StartFromIntakeAsync(_channel, It.Is<IntakeEnvelope>(e => e.ProviderMessageId == "SM1"), It.Is<Call>(k => k.CallId == 900),
				It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task when_no_conversation_opens_the_acknowledgement_still_goes()
		{
			_channel.SendAcknowledgement = true;

			var result = await _service.ReceiveAsync(DedicatedSms());

			result.IntakeConversationId.Should().BeNull();
			result.Acknowledgement.Should().Contain("26-900");
		}

		[Test]
		public async Task without_the_dedicated_number_flag_or_with_conversations_off_no_conversation_starts()
		{
			await _service.ReceiveAsync(Sms());

			var saved = IntakeConfig.ConversationsEnabled;
			try
			{
				IntakeConfig.ConversationsEnabled = false;
				await _service.ReceiveAsync(DedicatedSms(id: "SM2"));
			}
			finally
			{
				IntakeConfig.ConversationsEnabled = saved;
			}

			_conversations.Verify(c => c.StartFromIntakeAsync(It.IsAny<IntakeChannel>(), It.IsAny<IntakeEnvelope>(), It.IsAny<Call>(), It.IsAny<CancellationToken>()), Times.Never);
			_conversationOperations.Verify(c => c.GetOpenForParticipantAsync(It.IsAny<int>(), It.IsAny<IntakeChannelTypes>(), It.IsAny<string>()), Times.Never);
		}

		[Test]
		public async Task a_second_text_while_the_thread_is_open_joins_it_without_a_second_call()
		{
			var thread = new IntakeConversation { IntakeConversationId = "conv-1", DepartmentId = DepartmentId, CallId = 880, State = (int)IntakeConversationStates.Open };
			_conversationOperations.Setup(c => c.GetOpenForParticipantAsync(DepartmentId, IntakeChannelTypes.Sms, Sender)).ReturnsAsync(thread);
			_calls.Setup(c => c.GetCallByIdAsync(880, It.IsAny<bool>())).ReturnsAsync(new Call { CallId = 880, DepartmentId = DepartmentId, State = (int)CallStates.Pending });
			_conversations.Setup(c => c.HandleParticipantMessageAsync(thread, "SM2", "Also a fuel leak", It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
			_channel.SendAcknowledgement = true;

			var result = await _service.ReceiveAsync(DedicatedSms("Also a fuel leak", "SM2"));

			result.State.Should().Be(IntakeMessageStates.AddedToConversation);
			result.CallId.Should().Be(880);
			result.IntakeConversationId.Should().Be("conv-1");
			result.Acknowledgement.Should().BeNull();
			_saved.Should().BeEmpty();
			_completions.Should().ContainSingle().Which.Should().Be(((int)IntakeMessageStates.AddedToConversation, (int?)880, "Conversation"));
			_conversations.Verify(c => c.StartFromIntakeAsync(It.IsAny<IntakeChannel>(), It.IsAny<IntakeEnvelope>(), It.IsAny<Call>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task an_open_thread_whose_call_was_closed_ends_and_the_text_starts_a_new_call()
		{
			var thread = new IntakeConversation { IntakeConversationId = "conv-1", DepartmentId = DepartmentId, CallId = 880, State = (int)IntakeConversationStates.Open };
			_conversationOperations.Setup(c => c.GetOpenForParticipantAsync(DepartmentId, IntakeChannelTypes.Sms, Sender)).ReturnsAsync(thread);
			_calls.Setup(c => c.GetCallByIdAsync(880, It.IsAny<bool>())).ReturnsAsync(new Call { CallId = 880, DepartmentId = DepartmentId, State = (int)CallStates.Closed });

			var result = await _service.ReceiveAsync(DedicatedSms());

			result.State.Should().Be(IntakeMessageStates.CallCreated);
			_saved.Should().ContainSingle();
			_conversations.Verify(c => c.CloseAsync(DepartmentId, "conv-1", null, "CallClosed", false, It.IsAny<CancellationToken>()), Times.Once);
			_conversations.Verify(c => c.HandleParticipantMessageAsync(It.IsAny<IntakeConversation>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
			_conversations.Verify(c => c.StartFromIntakeAsync(_channel, It.IsAny<IntakeEnvelope>(), It.IsAny<Call>(), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task a_request_cancelled_while_joining_its_thread_builds_no_call_and_leaves_the_claim_for_the_retry()
		{
			using var cancellation = new CancellationTokenSource();
			var thread = new IntakeConversation { IntakeConversationId = "conv-1", DepartmentId = DepartmentId, CallId = 880, State = (int)IntakeConversationStates.Open };
			_conversationOperations.Setup(c => c.GetOpenForParticipantAsync(DepartmentId, IntakeChannelTypes.Sms, Sender)).ReturnsAsync(thread);
			_calls.Setup(c => c.GetCallByIdAsync(880, It.IsAny<bool>())).ReturnsAsync(new Call { CallId = 880, DepartmentId = DepartmentId, State = (int)CallStates.Pending });
			_conversations.Setup(c => c.HandleParticipantMessageAsync(thread, "SM2", It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
				.Callback(() => cancellation.Cancel())
				.ThrowsAsync(new OperationCanceledException(cancellation.Token));

			Func<Task> receive = () => _service.ReceiveAsync(DedicatedSms("Also a fuel leak", "SM2"), cancellation.Token);

			await receive.Should().ThrowAsync<OperationCanceledException>();
			_saved.Should().BeEmpty();
			_calls.Verify(c => c.GetActiveCallPrioritiesForDepartmentAsync(It.IsAny<int>(), It.IsAny<bool>()), Times.Never, "no call is built for a cancelled request");
			_completions.Should().ContainSingle().Which.Should().Be(((int)IntakeMessageStates.Failed, (int?)null, "Cancelled"));
		}

		#endregion SMS conversations (plan §8 D15)

		#region Spam screening (plan §9, M0326)

		private void PreviousCalls(params (int CallId, int State)[] calls)
		{
			var rows = calls.Select((c, i) => new IntakeMessage
			{
				IntakeMessageId = "m-" + c.CallId, DepartmentId = DepartmentId, Sender = Sender, State = (int)IntakeMessageStates.CallCreated, CallId = c.CallId,
				ReceivedOn = FixedClock.Now.AddMinutes(-5 - i)
			}).ToList();
			_messages.Setup(m => m.GetCallsCreatedBySenderSinceAsync(DepartmentId, Sender, FixedClock.Now.AddHours(-1), It.IsAny<int[]>())).ReturnsAsync(rows);
			foreach (var call in calls)
				_calls.Setup(c => c.GetCallByIdAsync(call.CallId, It.IsAny<bool>())).ReturnsAsync(new Call { CallId = call.CallId, DepartmentId = DepartmentId, State = call.State });
		}

		[Test]
		public async Task a_blocked_sender_is_recorded_and_creates_nothing()
		{
			_channel.SendAcknowledgement = true;
			_blocked.Setup(b => b.GetInForceAsync(DepartmentId, Sender, FixedClock.Now)).ReturnsAsync(new IntakeBlockedSender { DepartmentId = DepartmentId, Sender = Sender });

			var result = await _service.ReceiveAsync(DedicatedSms());

			result.State.Should().Be(IntakeMessageStates.SenderBlocked);
			result.Reason.Should().Be("SenderBlocked");
			result.Acknowledgement.Should().BeNull();
			_saved.Should().BeEmpty();
			_completions.Should().ContainSingle().Which.Should().Be(((int)IntakeMessageStates.SenderBlocked, (int?)null, "SenderBlocked"));
			_conversationOperations.Verify(c => c.GetOpenForParticipantAsync(It.IsAny<int>(), It.IsAny<IntakeChannelTypes>(), It.IsAny<string>()), Times.Never);
		}

		[Test]
		public async Task a_blocked_list_that_cannot_be_read_lets_the_message_through()
		{
			_blocked.Setup(b => b.GetInForceAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<DateTime>())).ThrowsAsync(new InvalidOperationException("db down"));

			var result = await _service.ReceiveAsync(Sms());

			result.State.Should().Be(IntakeMessageStates.CallCreated);
			_saved.Should().ContainSingle();
		}

		[Test]
		public async Task blocked_messages_and_duplicates_do_not_count_toward_the_month()
		{
			int[] excluded = null;
			_messages.Setup(m => m.CountAsync(DepartmentId, (int)IntakeChannelTypes.Sms, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int[]>()))
				.Callback((int _, int __, DateTime ___, DateTime ____, int[] states) => excluded = states).ReturnsAsync(4);

			await _service.GetMonthlyCountAsync(DepartmentId, IntakeChannelTypes.Sms, FixedClock.Now);

			excluded.Should().BeEquivalentTo(new[] { (int)IntakeMessageStates.Duplicate, (int)IntakeMessageStates.SenderBlocked });
		}

		[Test]
		public async Task past_the_hourly_limit_the_message_joins_the_senders_newest_open_call_as_a_note()
		{
			var saved = IntakeConfig.MaxCallsPerSenderPerHour;
			try
			{
				IntakeConfig.MaxCallsPerSenderPerHour = 3;
				_channel.SendAcknowledgement = true;
				PreviousCalls((803, (int)CallStates.Cancelled), (802, (int)CallStates.Pending), (801, (int)CallStates.Pending));
				CallNote note = null;
				_calls.Setup(c => c.SaveCallNoteAsync(It.IsAny<CallNote>(), It.IsAny<CancellationToken>()))
					.Callback((CallNote n, CancellationToken _) => note = n).ReturnsAsync((CallNote n, CancellationToken _) => { n.CallNoteId = 7; return n; });

				var result = await _service.ReceiveAsync(Sms("Win a free cruise", "SM4"));

				result.State.Should().Be(IntakeMessageStates.AddedToCall);
				result.CallId.Should().Be(802, "the newest call still open; 803 was cancelled");
				result.Acknowledgement.Should().BeNull();
				_saved.Should().BeEmpty();
				note.CallId.Should().Be(802);
				note.Source.Should().Be((int)CallNoteSources.IntakeText);
				note.Note.Should().Contain("Win a free cruise");
				_completions.Should().ContainSingle().Which.Should().Be(((int)IntakeMessageStates.AddedToCall, (int?)802, "SenderLimit"));
			}
			finally
			{
				IntakeConfig.MaxCallsPerSenderPerHour = saved;
			}
		}

		[Test]
		public async Task past_the_hourly_limit_with_nothing_open_the_call_stands_screened_and_nothing_goes_back()
		{
			var saved = IntakeConfig.MaxCallsPerSenderPerHour;
			try
			{
				IntakeConfig.MaxCallsPerSenderPerHour = 2;
				_channel.SendAcknowledgement = true;
				PreviousCalls((802, (int)CallStates.Cancelled), (801, (int)CallStates.Closed));

				var result = await _service.ReceiveAsync(DedicatedSms("Win a free cruise", "SM4"));

				result.State.Should().Be(IntakeMessageStates.CallCreated);
				result.Acknowledgement.Should().BeNull();
				result.IntakeConversationId.Should().BeNull();
				_saved.Should().ContainSingle();
				_messages.Verify(m => m.SetScreeningAsync(DepartmentId, 900, (int)IntakeScreenings.RepeatSender, It.Is<int[]>(r => r.SequenceEqual(new[] { 0 })),
					null, FixedClock.Now, It.IsAny<CancellationToken>()), Times.Once);
				_conversations.Verify(c => c.StartFromIntakeAsync(It.IsAny<IntakeChannel>(), It.IsAny<IntakeEnvelope>(), It.IsAny<Call>(), It.IsAny<CancellationToken>()), Times.Never);
			}
			finally
			{
				IntakeConfig.MaxCallsPerSenderPerHour = saved;
			}
		}

		[Test]
		public async Task under_the_limit_or_with_the_limit_off_the_message_makes_its_own_call()
		{
			var saved = IntakeConfig.MaxCallsPerSenderPerHour;
			try
			{
				IntakeConfig.MaxCallsPerSenderPerHour = 3;
				PreviousCalls((801, (int)CallStates.Pending), (800, (int)CallStates.Pending));
				(await _service.ReceiveAsync(Sms(id: "SM5"))).State.Should().Be(IntakeMessageStates.CallCreated);

				IntakeConfig.MaxCallsPerSenderPerHour = 0;
				PreviousCalls((803, (int)CallStates.Pending), (802, (int)CallStates.Pending), (801, (int)CallStates.Pending), (800, (int)CallStates.Pending));
				(await _service.ReceiveAsync(Sms(id: "SM6"))).State.Should().Be(IntakeMessageStates.CallCreated);

				_messages.Verify(m => m.SetScreeningAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int[]>(), It.IsAny<string>(), It.IsAny<DateTime>(),
					It.IsAny<CancellationToken>()), Times.Never);
			}
			finally
			{
				IntakeConfig.MaxCallsPerSenderPerHour = saved;
			}
		}

		[TestCase(IntakeChannelTypes.Email, null)]
		[TestCase(IntakeChannelTypes.Webhook, null)]
		[TestCase(IntakeChannelTypes.Slack, null)]
		[TestCase(IntakeChannelTypes.Sms, "+15305550199")]
		public void system_channels_and_allow_listed_senders_are_never_limited(IntakeChannelTypes type, string allowed)
		{
			// A CAD bridge or alarm panel legitimately sends many incidents an hour from one address.
			IntakeService.IsSenderLimited(new IntakeChannel { ChannelType = (int)type, AllowedSenders = allowed }).Should().BeFalse();
		}

		[TestCase(IntakeChannelTypes.Sms)]
		[TestCase(IntakeChannelTypes.Voice)]
		[TestCase(IntakeChannelTypes.WhatsApp)]
		public void the_public_phone_lines_are_limited(IntakeChannelTypes type)
		{
			IntakeService.IsSenderLimited(new IntakeChannel { ChannelType = (int)type }).Should().BeTrue();
		}

		[Test]
		public async Task an_allow_listed_sender_past_the_hourly_count_still_makes_its_own_call()
		{
			_channel.AllowedSenders = Sender;
			PreviousCalls((803, (int)CallStates.Pending), (802, (int)CallStates.Pending), (801, (int)CallStates.Pending));

			(await _service.ReceiveAsync(Sms(id: "SM8"))).State.Should().Be(IntakeMessageStates.CallCreated);
			_messages.Verify(m => m.GetCallsCreatedBySenderSinceAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<int[]>()), Times.Never);
		}

		[Test]
		public async Task a_note_past_the_limit_is_never_turned_into_a_call_when_its_update_cannot_be_published()
		{
			var saved = IntakeConfig.MaxCallsPerSenderPerHour;
			try
			{
				IntakeConfig.MaxCallsPerSenderPerHour = 3;
				PreviousCalls((802, (int)CallStates.Pending), (801, (int)CallStates.Pending), (800, (int)CallStates.Pending));
				_calls.Setup(c => c.SaveCallNoteAsync(It.IsAny<CallNote>(), It.IsAny<CancellationToken>()))
					.ReturnsAsync((CallNote n, CancellationToken _) => { n.CallNoteId = 7; return n; });
				_events.Setup(e => e.SendMessage(It.IsAny<CallUpdatedEvent>())).Throws(new InvalidOperationException("hub down"));

				var result = await _service.ReceiveAsync(Sms("Win a free cruise", "SM9"));

				result.State.Should().Be(IntakeMessageStates.AddedToCall);
				result.CallId.Should().Be(802);
				_saved.Should().BeEmpty("the text is already a note on call 802");
			}
			finally
			{
				IntakeConfig.MaxCallsPerSenderPerHour = saved;
			}
		}

		[Test]
		public async Task a_note_past_the_limit_saved_before_its_follow_up_failed_is_never_turned_into_a_call()
		{
			var saved = IntakeConfig.MaxCallsPerSenderPerHour;
			try
			{
				IntakeConfig.MaxCallsPerSenderPerHour = 3;
				PreviousCalls((802, (int)CallStates.Pending), (801, (int)CallStates.Pending), (800, (int)CallStates.Pending));
				// SaveCallNoteAsync inserted the note (the insert set its id), then its search-projection refresh threw.
				_calls.Setup(c => c.SaveCallNoteAsync(It.IsAny<CallNote>(), It.IsAny<CancellationToken>()))
					.Returns((CallNote n, CancellationToken _) =>
					{
						n.CallNoteId = 7;
						throw new InvalidOperationException("search projection down");
					});

				var result = await _service.ReceiveAsync(Sms("Win a free cruise", "SM11"));

				result.State.Should().Be(IntakeMessageStates.AddedToCall);
				result.CallId.Should().Be(802);
				_saved.Should().BeEmpty("the text is already a note on call 802");
				_completions.Should().ContainSingle().Which.Should().Be(((int)IntakeMessageStates.AddedToCall, (int?)802, "SenderLimit"));
			}
			finally
			{
				IntakeConfig.MaxCallsPerSenderPerHour = saved;
			}
		}

		[Test]
		public async Task a_note_past_the_limit_that_was_never_saved_lets_the_message_make_its_own_call()
		{
			var saved = IntakeConfig.MaxCallsPerSenderPerHour;
			try
			{
				IntakeConfig.MaxCallsPerSenderPerHour = 3;
				PreviousCalls((802, (int)CallStates.Pending), (801, (int)CallStates.Pending), (800, (int)CallStates.Pending));
				_calls.Setup(c => c.SaveCallNoteAsync(It.IsAny<CallNote>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("db down"));

				var result = await _service.ReceiveAsync(Sms("Win a free cruise", "SM12"));

				result.State.Should().Be(IntakeMessageStates.CallCreated, "nothing reached call 802, so the text is not lost");
				_saved.Should().ContainSingle();
			}
			finally
			{
				IntakeConfig.MaxCallsPerSenderPerHour = saved;
			}
		}

		[Test]
		public async Task a_note_past_the_limit_keeps_its_outcome_when_the_claim_write_fails()
		{
			var saved = IntakeConfig.MaxCallsPerSenderPerHour;
			try
			{
				IntakeConfig.MaxCallsPerSenderPerHour = 3;
				PreviousCalls((802, (int)CallStates.Pending), (801, (int)CallStates.Pending), (800, (int)CallStates.Pending));
				_calls.Setup(c => c.SaveCallNoteAsync(It.IsAny<CallNote>(), It.IsAny<CancellationToken>()))
					.ReturnsAsync((CallNote n, CancellationToken _) => { n.CallNoteId = 7; return n; });
				// The first outcome write fails after the note is on the call; the recovery write goes through.
				var writes = 0;
				_messages.Setup(m => m.CompleteAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
					.Returns((string id, int state, int? callId, string reason, DateTime _, CancellationToken __) =>
					{
						if (++writes == 1)
							throw new InvalidOperationException("db blip");
						_completions.Add((state, callId, reason));
						return Task.FromResult(true);
					});

				var result = await _service.ReceiveAsync(Sms("Win a free cruise", "SM10"));

				result.State.Should().Be(IntakeMessageStates.AddedToCall, "the provider's retry must be a duplicate, not a second copy of the note");
				result.CallId.Should().Be(802);
				_completions.Should().ContainSingle().Which.Should().Be(((int)IntakeMessageStates.AddedToCall, (int?)802, "SenderLimit"));
				_saved.Should().BeEmpty();
			}
			finally
			{
				IntakeConfig.MaxCallsPerSenderPerHour = saved;
			}
		}

		[Test]
		public async Task withheld_caller_ids_are_never_limited_as_one_sender()
		{
			var envelope = Sms(id: "CA7");
			envelope.Sender = "+266696687";

			var result = await _service.ReceiveAsync(envelope);

			result.State.Should().Be(IntakeMessageStates.CallCreated);
			_messages.Verify(m => m.GetCallsCreatedBySenderSinceAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<int[]>()), Times.Never);
			_blocked.Verify(b => b.GetInForceAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<DateTime>()), Times.Never);
		}

		[Test]
		public async Task call_sources_carry_the_screening()
		{
			_messages.Setup(m => m.GetByCallIdsAsync(DepartmentId, It.IsAny<IEnumerable<int>>())).ReturnsAsync(new List<IntakeMessage>
			{
				new IntakeMessage { DepartmentId = DepartmentId, CallId = 900, IntakeChannelId = "ch-1", ChannelType = (int)IntakeChannelTypes.Sms, Screening = (int)IntakeScreenings.LikelyNotIncident },
				new IntakeMessage { DepartmentId = DepartmentId, CallId = 901, IntakeChannelId = "ch-1", ChannelType = (int)IntakeChannelTypes.Sms }
			});

			var sources = await _service.GetCallSourcesAsync(DepartmentId, new[] { 900, 901 });

			sources[900].Screening.Should().Be((int)IntakeScreenings.LikelyNotIncident);
			sources[901].Screening.Should().Be(0);
		}

		#endregion Spam screening (plan §9, M0326)
	}
}
